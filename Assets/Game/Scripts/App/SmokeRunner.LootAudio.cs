using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RuinRail.Audio;
using RuinRail.Core;
using RuinRail.Core.Rendering;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// Loot / ammo / starter-loadout and audio-runtime stages of the built-player smoke (`-proofdir dir` writes the
    /// captures and <c>audio_runtime_evidence.txt</c> there). Everything here runs inside the shipped executable on
    /// the real composition: a Supply Chest in a generated room is opened once through the player's interactor, the
    /// reward pickups are collected and the reserve counted, the starter Field Knife damages an enemy at zero
    /// firearm reserve, and every audio voice that actually starts is recorded with its clip, bus, volume and gains.
    /// </summary>
    public sealed partial class SmokeRunner
    {
        public const string ProofDirArgument = "-proofdir";
        public const string AudioEvidenceFile = "audio_runtime_evidence.txt";

        private readonly StringBuilder _audioEvidence = new();
        private readonly List<string> _captures = new();
        private readonly List<string> _voicesStarted = new();
        private string _proofDir;
        private float _listenerPeak;
        private int _listenerNonZeroFrames;
        private readonly float[] _listenerBuffer = new float[1024];

        private string ProofDir
        {
            get
            {
                if (_proofDir != null) return _proofDir;
                var args = Environment.GetCommandLineArgs();
                var index = Array.IndexOf(args, ProofDirArgument);
                _proofDir = index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : string.Empty;
                if (_proofDir.Length > 0) Directory.CreateDirectory(_proofDir);
                _result.ProofDirectory = _proofDir;
                return _proofDir;
            }
        }

        // ---- Audio evidence (recorded across the whole run) ----

        private void BeginAudioEvidence()
        {
            _ = ProofDir;
            _audioEvidence.AppendLine("# RUINRAIL built-player audio runtime evidence");
            _audioEvidence.AppendLine($"# {DateTime.Now:yyyy-MM-dd HH:mm:ss} unity {Application.unityVersion} batch={Application.isBatchMode} graphics={SystemInfo.graphicsDeviceType}");
            _audioEvidence.AppendLine("# columns: timestamp | scene/state | kind | role/event | clip | mixer group | source volume | master/category gain | isPlaying");
            if (_app.Audio != null) _app.Audio.VoiceStarted += OnVoiceStarted;
            _app.SceneComposed += scene => { _audioEvidence.AppendLine(); RecordMusicState("scene composed: " + scene); };
        }

        private void OnVoiceStarted(string id, AudioSource source)
        {
            _voicesStarted.Add(id);
            var bus = _app.Audio.Catalog != null && _app.Audio.Catalog.TryGet(id, out var d) ? d.Bus.ToString() : "?";
            Evidence("sfx", id, source, bus, AudioService.GainFor(bus == "Music" ? AudioBus.Music : AudioBus.Weapons));
        }

        private void Evidence(string kind, string role, AudioSource source, string bus, float gain)
        {
            var clip = source != null && source.clip != null ? source.clip.name : "(none)";
            var group = source != null && source.outputAudioMixerGroup != null ? source.outputAudioMixerGroup.name : "direct(" + bus + ")";
            var playing = source != null && source.isPlaying;
            var volume = source != null ? source.volume : 0f;
            _audioEvidence.AppendLine($"{Time.realtimeSinceStartup,8:0.000}s | {_app.ComposedScene}/{_result.Stage} | {kind} | {role} | {clip} | {group} | vol {volume:0.00} | master {AudioLevels.Master:0.00} music {AudioLevels.Music:0.00} sfx {AudioLevels.Sfx:0.00} muted {AudioLevels.Muted} | isPlaying={playing}");
        }

        private void RecordMusicState(string note)
        {
            var director = _app.Music;
            if (director == null) return;
            _audioEvidence.AppendLine($"{Time.realtimeSinceStartup,8:0.000}s | {_app.ComposedScene}/{_result.Stage} | note | {note}");
            var (track, ambience) = MusicSources();
            Evidence("music", director.ActiveRole?.ToString() ?? "(none)", track, "Music", AudioService.GainFor(AudioBus.Music));
            Evidence("ambience", director.ActiveAmbience?.ToString() ?? "(none)", ambience, "Ambience", MusicDirector.AmbienceGain());
        }

        /// <summary>The music director's playing track source (MusicA/MusicB) and its ambience source.</summary>
        private (AudioSource track, AudioSource ambience) MusicSources()
        {
            AudioSource track = null, ambience = null;
            var expected = _app.Music.ActiveRole.HasValue && _app.Music.Catalog != null ? _app.Music.Catalog.TrackFor(_app.Music.ActiveRole.Value) : null;
            foreach (var source in _app.Music.GetComponentsInChildren<AudioSource>(true))
            {
                if (source.name == "Ambience") ambience = source;
                // During a crossfade both beds play; the active role's clip is the incoming one.
                else if ((source.name == "MusicA" || source.name == "MusicB") && source.isPlaying && source.clip != null && (track == null || source.clip == expected)) track = source;
            }

            return (track, ambience);
        }

        /// <summary>Samples the listener's mixed output this frame: proof that the mix at the listener is not silent (not a claim about the speakers).</summary>
        private void SampleListener()
        {
            AudioListener.GetOutputData(_listenerBuffer, 0);
            var peak = 0f;
            foreach (var s in _listenerBuffer) peak = Mathf.Max(peak, Mathf.Abs(s));
            if (peak > 0.001f) _listenerNonZeroFrames++;
            _listenerPeak = Mathf.Max(_listenerPeak, peak);
        }

        private IEnumerator Capture(string name)
        {
            if (string.IsNullOrEmpty(ProofDir)) yield break;
            var path = Path.Combine(ProofDir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            for (var i = 0; i < 180 && !File.Exists(path); i++) yield return null;
            if (File.Exists(path)) _captures.Add(path);
            Debug.Log("[SMOKE] capture " + (File.Exists(path) ? "written → " + path : "NOT written: " + path));
        }

        private bool _evidenceWritten;

        private void WriteAudioEvidence()
        {
            if (_evidenceWritten) return;
            _evidenceWritten = true;
            _audioEvidence.AppendLine();
            _audioEvidence.AppendLine($"# listener mix: peak {_listenerPeak:0.000}, frames with signal {_listenerNonZeroFrames}");
            _audioEvidence.AppendLine("#   (this samples the mix at the listener, NOT the speakers: it cannot prove the output device was heard)");
            _audioEvidence.AppendLine($"# voices started: {_voicesStarted.Count} ({string.Join(", ", _voicesStarted.Distinct().OrderBy(v => v))})");
            _audioEvidence.AppendLine();
            _audioEvidence.AppendLine("================ runtime audio state at the end of the run ================");
            _audioEvidence.Append(AudioRuntimeDiagnostics.Capture().ToText());
            _audioEvidence.AppendLine($"# music director output-device restarts: {(_app.Music != null ? _app.Music.OutputResets : 0)}; " +
                                      $"audio service loop restarts: {(_app.Audio != null ? _app.Audio.OutputResets : 0)}");
            if (string.IsNullOrEmpty(ProofDir)) return;
            var path = Path.Combine(ProofDir, AudioEvidenceFile);
            File.WriteAllText(path, _audioEvidence.ToString());
            _result.AudioEvidence = path;
            _result.ProofCaptures = _captures.ToArray();
            _result.MusicPeakSamplesNonZero = _listenerNonZeroFrames;
        }

        // ---- Loot / ammo / starter (dungeon) ----

        private IEnumerator LootAmmoStarterChecks(ExpeditionScene run)
        {
            var passed = new List<string>();
            void Check(string what, bool ok) { if (ok) passed.Add(what); else if (string.IsNullOrEmpty(_result.Error)) Fail("loot: " + what); }
            var player = run.Rig.Player;
            var inventory = run.Expedition.State.Inventory;
            var content = _app.Content;

            // Starter loadout in the live run: P9 Ranger in Primary, Field Knife in Secondary, both mounted.
            var primary = inventory.GetEquipped(EquippedSlot.PrimaryWeapon);
            var secondary = inventory.GetEquipped(EquippedSlot.SecondaryWeapon);
            Check("starter primary is the P9 Ranger", primary != null && primary.DefinitionId == RuinRail.Gameplay.Base.StarterKitService.PistolId);
            Check("starter secondary is the ammo-free Field Knife", secondary != null && secondary.DefinitionId == RuinRail.Gameplay.Base.StarterKitService.KnifeId && run.Rig.Loadout.GetSlot(WeaponSlot.Secondary) is MeleeWeapon);
            yield return Capture("07_starter_inventory_primary_and_knife_hud");

            // World art bound for every runtime object kind.
            Check("world-object art bound for every runtime key", WorldObjectArt.RequiredKeys.All(k => content.WorldSpriteFor(k) != null));

            // Loot-focused rooms are never empty: every Loot/Treasure room bound its chests, the boss room its cache.
            var bindings = run.Rooms.Values.Select(r => (room: r, binding: r.GetComponent<RoomContentBinding>())).ToList();
            var special = bindings.Where(b => b.room.State.RoomType == RoomType.Loot || b.room.State.RoomType == RoomType.Treasure).ToList();
            Check($"loot/treasure rooms carry their reward source ({special.Count} on this depth)", special.All(b => b.binding != null && b.binding.Chests.Count > 0 && b.binding.Chests.All(c => c.Visual != null && c.Visual.IsVisible)));
            var boss = bindings.FirstOrDefault(b => b.room.State.RoomType == RoomType.Boss);
            Check("boss room holds its boss and no Boss Cache until the boss dies (46: its death spawns the cache)", boss.binding != null && boss.binding.Boss != null && boss.binding.BossCache == null);
            var supplyRooms = bindings.Where(b => b.binding != null && b.binding.SupplyChest != null).ToList();
            var chestTotal = bindings.Sum(b => b.binding != null ? b.binding.Chests.Count : 0);
            Check($"depth has meaningful loot before the boss ({supplyRooms.Count} supply chests in ordinary rooms, {chestTotal} chests total)", supplyRooms.Count >= SupplyChestPlanner.MinimumPerDepth);
            var merchant = bindings.FirstOrDefault(b => b.binding != null && b.binding.Merchant != null);
            if (merchant.binding != null)
            {
                var offers = merchant.binding.Merchant.Merchant.Offers;
                Check("merchant present with an ammo offer", offers.Any(o => o.Definition is AmmoItemDefinition && o.Item.Quantity > 0) && merchant.binding.Merchant.GetComponent<WorldObjectVisual>() != null && merchant.binding.Merchant.GetComponent<WorldObjectVisual>().IsVisible);
            }

            // A closed Supply Chest in a generated room: walk up to it, read the prompt, open it exactly once.
            var chestRoom = supplyRooms.FirstOrDefault();
            if (chestRoom.binding == null) { Check("a supply chest exists to open", false); yield break; }
            var chest = chestRoom.binding.SupplyChest;
            var chestPosition = (Vector2)chest.transform.position;
            var body = player.GetComponent<Rigidbody2D>();
            void Put(Vector2 p) { player.transform.position = p; body.position = p; body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms(); }
            Put(chestPosition + Vector2.down * 0.9f);
            for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            for (var i = 0; i < 6; i++) yield return null;
            var interactor = player.GetComponent<PlayerInteractor>();
            Check("closed chest is drawn with the final crate art in walkable room space", !chest.IsOpened && chest.Visual != null && chest.Visual.IsVisible && chest.Visual.Key == WorldObjectArt.SupplyChest && chest.Visual.Renderer.sortingLayerName == SortingLayers.Characters && RoomRuntime.IsSpawnClear(chestPosition));
            Check("chest interaction prompt shown", run.CurrentInteractionPrompt.Contains("OPEN CHEST"));
            yield return Capture("01_closed_supply_chest_in_room");

            // Snapshotted before the chest opens: the survivor is standing next to it, and the baseline pickup reach
            // (PickupAttractor.DefaultBaseRadiusTiles) draws coins and ammo in on its own within a frame or two. So the
            // spawned-loot checks below have to be about "did the loot reach the player" rather than "is it still on
            // the floor waiting to be pressed".
            var reserveBeforeOpen = System.Enum.GetValues(typeof(AmmoType)).Cast<AmmoType>().ToDictionary(t => t, inventory.Get);
            var coinsBefore = run.Expedition.State.CarriedCoins;
            var opened = interactor.TryInteract();
            yield return null;
            Check("chest opened exactly once through the interactor", opened && chest.IsOpened && chest.LastResult != null && !chest.LastResult.IsEmpty && chest.Visual.Key == WorldObjectArt.SupplyChestOpen);
            Check("second interaction is a no-op", !chest.TryOpen(out _) && chest.SpawnedPickups.Count > 0);
            // A pickup the attraction reach has already taken is gone from the scene; every one still on the ground
            // must be drawn with its final art.
            Check("loot spawned as visible pickups (or was already drawn in by the attraction reach)",
                chest.SpawnedPickups.Where(p => p != null).All(p => p.GetComponent<WorldObjectVisual>() != null && p.GetComponent<WorldObjectVisual>().IsVisible));
            Check("chest open sound started", _voicesStarted.Contains(AudioEventIds.ChestOpen));
            yield return Capture("02_supply_chest_opened_loot_spawned");

            // The ammo the chest rolled reaches the reserve exactly once, whether the attraction reach took it while
            // the player stood at the chest or the player pressed Interact on it.
            var rolledAmmo = chest.LastResult.Items
                .Select(i => (item: i, definition: content.Items.OfType<AmmoItemDefinition>().FirstOrDefault(a => a.Id == i.DefinitionId)))
                .Where(pair => pair.definition != null).ToList();
            Check("supply chest produced an ammo stack", rolledAmmo.Count > 0 && rolledAmmo.All(pair => pair.item.Quantity > 0));
            foreach (var (item, definition) in rolledAmmo)
            {
                var ammoType = definition.AmmoType;
                var quantity = item.Quantity;
                var pickup = chest.SpawnedPickups.Where(p => p != null).Select(p => p.GetComponent<WorldItemPickup>())
                    .FirstOrDefault(p => p != null && !p.IsConsumed && p.Item != null && p.Item.InstanceId == item.InstanceId);
                if (pickup != null)
                {
                    yield return Capture("04_ammo_pickup_before_collection");
                    Put((Vector2)pickup.transform.position + Vector2.down * 0.2f);
                    for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                    yield return null;
                    if (pickup != null && !pickup.IsConsumed) pickup.Interact(player);
                    yield return null;
                }

                var after = inventory.Get(ammoType);
                var cap = content.AmmoBalance.GetStackLimit(ammoType);
                var expected = Mathf.Min(cap, reserveBeforeOpen[ammoType] + quantity);
                Check($"reserve increased after collection ({ammoType} {reserveBeforeOpen[ammoType]} → {after}, +{quantity}, cap {cap})", after == expected && after > reserveBeforeOpen[ammoType]);
                Check("ammo pickup consumed once (gone from the ground)",
                    chest.SpawnedPickups.Where(p => p != null).Select(p => p.GetComponent<WorldItemPickup>())
                        .All(p => p == null || p.IsConsumed || p.Item == null || p.Item.InstanceId != item.InstanceId));
                Check("pickup sound started", _voicesStarted.Contains(AudioEventIds.PickupItem));
                yield return Capture("05_reserve_increased_after_pickup");
            }

            if (chest.LastResult.Coins > 0)
            {
                var stillOnTheGround = chest.SpawnedPickups.Where(p => p != null).Select(p => p.GetComponent<CoinPickup>()).FirstOrDefault(c => c != null && !c.IsCollected);
                if (stillOnTheGround != null) stillOnTheGround.Interact(player);
                yield return null;
                Check($"coins collected once ({coinsBefore} → {run.Expedition.State.CarriedCoins}, +{chest.LastResult.Coins})",
                    run.Expedition.State.CarriedCoins == coinsBefore + chest.LastResult.Coins);
            }

            // Zero firearm reserve → the knife still works and consumes nothing.
            var pistol = run.Rig.Loadout.GetSlot(WeaponSlot.Primary) as RangedWeapon;
            inventory.Consume(AmmoType.Light, inventory.Get(AmmoType.Light));
            if (pistol != null) pistol.ApplyAuthoritativeState(0, false);
            Check("firearm reserve is 0 and the magazine empty", inventory.Get(AmmoType.Light) == 0 && pistol != null && pistol.MagazineAmmo == 0);
            var dry = pistol != null && !pistol.TryFire();
            Check("dry fire click at zero reserve", dry && pistol.DryFires >= 1 && _voicesStarted.Contains(AudioEventIds.DryFire));
            run.Rig.Loadout.SelectSlot(WeaponSlot.Secondary);
            yield return null;
            var knife = run.Rig.Loadout.ActiveWeapon as MeleeWeapon;
            Check("swap to the ammo-free secondary works with reserve 0", knife != null && run.Rig.Loadout.ActiveSlot == WeaponSlot.Secondary && knife.Definition.Id == RuinRail.Gameplay.Base.StarterKitService.KnifeId);
            yield return Capture("08_field_knife_active_at_zero_firearm_reserve");

            // A frozen enemy inside knife reach along the current aim: the swing lands, HP drops, no ammo moves.
            var aiming = player.GetComponent<PlayerAiming>();
            var reach = Mathf.Min(0.8f, knife != null ? knife.Definition.AttackRange * 0.7f : 0.8f);
            // Precondition, not a tolerance: the dummy goes on open floor with nothing solid between it and the player.
            // Where the player happened to stop (by the chest) the aim could point into a wall or a prop on some seeds, and
            // the swing then rightly hit nothing; the player is moved to the nearest interior point with a clear lane.
            bool LaneClear(Vector2 from, Vector2 dir) => RuinRail.Dungeon.Runtime.RoomRuntime.IsSpawnClear(from) && RuinRail.Dungeon.Runtime.RoomRuntime.IsSpawnClear(from + dir * reach)
                && !Physics2D.CircleCastAll(from, 0.3f, dir, reach + 0.4f).Any(h => h.collider != null && !h.collider.isTrigger && h.collider.GetComponentInParent<RuinRail.Gameplay.Combat.EnvironmentObstacle>() != null);
            if (!LaneClear(player.transform.position, aiming.AimDirection.normalized) && run.CurrentRoom != null)
            {
                var interior = run.CurrentRoom.InteriorWorldBounds;
                var here = (Vector2)player.transform.position;
                var candidates = new List<Vector2>();
                for (var x = interior.xMin + 1f; x <= interior.xMax - 1f; x += 0.5f)
                for (var y = interior.yMin + 1f; y <= interior.yMax - 1f; y += 0.5f)
                    candidates.Add(new Vector2(x, y));
                foreach (var candidate in candidates.OrderBy(c => Vector2.Distance(c, here)))
                {
                    if (!LaneClear(candidate, aiming.AimDirection.normalized)) continue;
                    Put(candidate);
                    for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                    yield return null;
                    if (LaneClear(player.transform.position, aiming.AimDirection.normalized)) break;
                }
            }

            // The aim follows the pointer through the camera, which trails the player after any move: wait until it is
            // steady, or the swing resolves toward a different direction than the one the dummy was placed on.
            var settledFrames = 0;
            var lastAim = aiming.AimDirection;
            for (var i = 0; i < 180 && settledFrames < 8; i++)
            {
                yield return null;
                settledFrames = Vector2.Angle(lastAim, aiming.AimDirection) < 0.5f ? settledFrames + 1 : 0;
                lastAim = aiming.AimDirection;
            }

            var spot = (Vector2)player.transform.position + aiming.AimDirection.normalized * reach;
            var victim = new DefaultEnemySpawner(content.Stagger).Spawn(content.Enemies.First(e => e.Id == "grunt"), spot, player.transform);
            run.BindEnemyPresentation(victim);
            victim.enabled = false;
            var vb = victim.GetComponent<Rigidbody2D>();
            vb.linearVelocity = Vector2.zero;
            vb.bodyType = RigidbodyType2D.Kinematic;
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();
            // The aim eases toward the pointer, so it can still be turning by a few degrees per frame: put the dummy on
            // the aim as it is at the moment of the swing (the swing resolves ~0.08 s later, well inside the 80° arc).
            var onAim = (Vector2)player.transform.position + aiming.AimDirection.normalized * reach;
            victim.transform.position = onAim;
            vb.position = onAim;
            Physics2D.SyncTransforms();
            var health = victim.GetComponent<HealthComponent>();
            var hp0 = health.CurrentHealth;
            var reservesBefore = new[] { AmmoType.Light, AmmoType.Medium, AmmoType.Heavy, AmmoType.Shells }.Select(inventory.Get).ToArray();
            var stateBefore = knife != null ? knife.State.ToString() : "-";
            var swung = knife != null && knife.TryAttack();
            // Hold the dummy on the aim through the wind-up: the aim eases after the camera, and the swing resolves on the
            // knife's own clock toward whatever the aim is at that moment.
            for (var f = 0; f < 60 && swung && knife.State == MeleeAttackState.WindUp && health.CurrentHealth == hp0; f++)
            {
                var onAimNow = (Vector2)player.transform.position + aiming.AimDirection.normalized * reach;
                victim.transform.position = onAimNow;
                vb.position = onAimNow;
                Physics2D.SyncTransforms();
                yield return null;
            }

            var deadline = Time.time + 2f;
            while (Time.time < deadline && health.CurrentHealth == hp0) yield return null;
            var toVictim = (Vector2)victim.transform.position - (Vector2)(knife != null ? knife.transform.position : player.transform.position);
            var knifeDiag = $"swung={swung} stateBefore={stateBefore} equipped={knife?.IsEquipped} active={run.Rig.Loadout.ActiveSlot} player={(Vector2)player.transform.position} aim={aiming.AimDirection} victim={(Vector2)victim.transform.position} dist={toVictim.magnitude:0.00} angle={Vector2.Angle(aiming.AimDirection, toVictim):0.0} alive={victim.IsAlive} gated={RuinRail.Core.Input.GameplayInputGate.IsHeld}";
            Debug.Log("[SMOKE] knife: " + knifeDiag);
            Check($"ammo-free secondary damages an enemy at zero firearm reserve (HP {hp0} → {health.CurrentHealth}; {knifeDiag})", swung && health.CurrentHealth < hp0);
            var reservesAfter = new[] { AmmoType.Light, AmmoType.Medium, AmmoType.Heavy, AmmoType.Shells }.Select(inventory.Get).ToArray();
            Check("no ammo consumed by the melee swing", reservesBefore.SequenceEqual(reservesAfter) && reservesAfter[0] == 0);
            Check("enemy hit sound started", _voicesStarted.Contains(AudioEventIds.EnemyHit));
            yield return Capture("09_knife_damages_enemy_at_zero_reserve");
            // Finish it for the death cue.
            health.TryApplyDamage(new DamageRequest(health.CurrentHealth + 1));
            yield return null;
            Check("enemy death sound started", _voicesStarted.Contains(AudioEventIds.EnemyDeath));
            run.Rig.Loadout.SelectSlot(WeaponSlot.Primary);
            inventory.Add(AmmoType.Light, 60); // leave the profile in a normal state for the save/reload stage

            // A Treasure/Loot room's guaranteed source, captured where it stands.
            if (special.Count > 0 && special[0].binding.Chests.Count > 0)
            {
                var reward = special[0].binding.Chests[0];
                Put((Vector2)reward.transform.position + Vector2.down * 1.2f);
                for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                for (var i = 0; i < 4; i++) yield return null;
                Check($"{special[0].room.State.RoomType} room reward source stands closed and drawn ({reward.Kind})", !reward.IsOpened && reward.Visual.IsVisible);
                yield return Capture("06_loot_room_guaranteed_reward_source");
            }

            if (merchant.binding != null)
            {
                Put((Vector2)merchant.binding.Merchant.transform.position + Vector2.down * 1.2f);
                for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                for (var i = 0; i < 4; i++) yield return null;
                yield return Capture("10_dungeon_merchant_ammo_source");
            }

            _result.LootChecks = passed.ToArray();
            Debug.Log("[SMOKE] loot: " + string.Join("; ", passed));
        }

        // ---- Audio (dungeon) ----

        private IEnumerator DungeonAudioChecks(ExpeditionScene run)
        {
            var passed = new List<string>();
            void Check(string what, bool ok) { if (ok) passed.Add(what); else if (string.IsNullOrEmpty(_result.Error)) Fail("audio: " + what); }
            var director = _app.Music;
            var biome = run.Expedition.State.Biome;
            var listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Check("exactly one AudioListener, enabled, following the camera", listeners.Length == 1 && listeners[0].enabled && AudioListenerRig.Current != null && !AudioListener.pause && Vector2.Distance(AudioListenerRig.Position, run.Camera.transform.position) < 0.01f);
            Check("audio service and music director persist on the app root", _app.Audio != null && _app.Music != null && _app.Audio.transform.root == _app.transform && FindObjectsByType<AudioService>(FindObjectsSortMode.None).Length == 1 && FindObjectsByType<MusicDirector>(FindObjectsSortMode.None).Length == 1);
            Check("fresh-profile audio levels are audible", AudioLevels.Master > 0f && AudioLevels.Music > 0f && AudioLevels.Sfx > 0f && AudioLevels.AmbienceGain > 0f && !AudioLevels.Muted && AudioListener.volume > 0f);
            // The output-device watchdog is what keeps a shipped run attached to the device the player listens on.
            Check("output-device watchdog running on the persistent root", AudioOutputWatchdog.Current != null && AudioOutputWatchdog.Current.transform.root == _app.transform);
            var diagnostics = AudioRuntimeDiagnostics.Capture();
            Check($"runtime audio state has no in-engine cause of silence ({diagnostics.Configuration})", diagnostics.Problems().Count == 0);
            var (track, ambience) = MusicSources();
            RecordMusicState("dungeon audio checks");
            var expectedRole = MusicStateResolver.Resolve(MusicScreen.Expedition, biome, _app.MusicBinder.Intensity);
            Check($"dungeon music bed playing ({director.ActiveRole})", director.ActiveRole == expectedRole && track != null && track.clip != null && track.isPlaying && track.volume > 0f && !director.IsSilent);
            Check($"{biome} ambience loop playing", director.ActiveAmbience == biome && ambience != null && ambience.clip != null && ambience.isPlaying && ambience.loop && ambience.volume > 0f && ambience.volume <= track.volume);
            Check("exactly one music track source playing outside a crossfade", director.IsCrossfading || director.PlayingTrackSources == 1);

            // A real shot and reload through the mounted weapon (the binder reads them from the visual driver's counters).
            var pistol = run.Rig.Loadout.GetSlot(WeaponSlot.Primary) as RangedWeapon;
            if (pistol != null)
            {
                run.Rig.Loadout.SelectSlot(WeaponSlot.Primary);
                pistol.ApplyAuthoritativeState(pistol.Definition.MagazineSize, false);
                yield return null;
                pistol.TryFire();
                for (var i = 0; i < 4; i++) yield return null;
                pistol.TryStartReload();
                for (var i = 0; i < 4; i++) yield return null;
            }

            // Representative SFX actually started (the rest came from the combat and loot stages).
            foreach (var id in new[] { AudioEventIds.FirePistol, AudioEventIds.Reload, AudioEventIds.EnemyHit, AudioEventIds.EnemyDeath, AudioEventIds.ChestOpen, AudioEventIds.PickupItem, AudioEventIds.DoorOpen, AudioEventIds.DryFire })
                Check("sfx started: " + id, _voicesStarted.Contains(id));
            // Every SFX voice went through a 2D source with a clip and a non-zero volume.
            var play = _app.Audio.Play(AudioEventIds.UiConfirm);
            var last = _app.Audio.LastStartedSource;
            Check("ui sfx voice allocated with a clip, 2D, audible volume", play && last != null && last.clip != null && last.isPlaying && last.volume > 0f && last.spatialBlend == 0f);

            // Pause / resume never mutes: the beds keep playing under the pause (unscaled) and after it.
            run.Pause.Open();
            for (var i = 0; i < 20; i++) yield return null;
            var (pausedTrack, pausedAmbience) = MusicSources();
            RecordMusicState("paused");
            Check("music and ambience keep playing while paused", pausedTrack != null && pausedTrack.isPlaying && pausedAmbience != null && pausedAmbience.isPlaying && !AudioListener.pause);
            run.Pause.Close();
            for (var i = 0; i < 10; i++) yield return null;
            var (resumedTrack, _) = MusicSources();
            RecordMusicState("resumed");
            Check("music playing after resume", resumedTrack != null && resumedTrack.isPlaying && resumedTrack.volume > 0f);

            // The listener's mixed output carries signal (sampled over a second of frames).
            for (var i = 0; i < 60; i++) { SampleListener(); yield return null; }
            Check($"listener mix carries signal (peak {_listenerPeak:0.000}, frames {_listenerNonZeroFrames}/60)", Application.isBatchMode || _listenerNonZeroFrames > 0);

            _result.AudioChecks = passed.ToArray();
            Debug.Log("[SMOKE] audio: " + string.Join("; ", passed));
        }

        private IEnumerator MenuAudioChecksAfterReturn()
        {
            var passed = _result.AudioChecks.ToList();
            void Check(string what, bool ok) { if (ok) passed.Add(what); else if (string.IsNullOrEmpty(_result.Error)) Fail("audio: " + what); }
            for (var i = 0; i < 5; i++) yield return null;
            var director = _app.Music;
            var (track, ambience) = MusicSources();
            RecordMusicState("main menu after leaving the run");
            Check("main menu music restored after return to menu", director.ActiveRole == MusicRole.MainMenu && track != null && track.isPlaying && track.clip != null);
            Check("no ambience outside the expedition", director.ActiveAmbience == null && (ambience == null || !ambience.isPlaying));
            Check("still exactly one AudioListener", FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1);
            Check("expedition-failed stinger played on leaving", director.LastStinger == StingerRole.ExpeditionFailed);
            _result.AudioChecks = passed.ToArray();
            WriteAudioEvidence();
        }
    }
}
