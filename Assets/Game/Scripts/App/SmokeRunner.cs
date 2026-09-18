using System;
using System.Collections;
using System.IO;
using System.Linq;
using RuinRail.Gameplay.Expedition;
using RuinRail.Core;
using RuinRail.Gameplay.Items;
using RuinRail.UI.Base;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// Built-player smoke (`-smoke [-savedir dir] [-screenshot file.png]`): boot → Main Menu → PLAY (new profile) → Shelter → READY + START →
    /// Dungeon composed with rooms and the player → Return through the service → Shelter with the summary → save →
    /// reload the saved slot and compare → write smoke_result.json → quit with exit code 0 (1 on any failure).
    /// </summary>
    public sealed partial class SmokeRunner : MonoBehaviour
    {
        [Serializable]
        public sealed class Result
        {
            public bool Success;
            public string Stage = "boot";
            public string Error = "";
            public string ScenesComposed = "";
            public int RoomsComposed;
            public string Biome = "";
            public int BankedCoinsAfterReturn;
            public int TotalXpAfterReturn;
            public string Pistol = "";
            public bool SaveReloaded;
            /// <summary>Second expedition left through Pause → RETURN TO MAIN MENU: failed once, marker closed, banked state intact.</summary>
            public bool ReturnToMenuOk;
            /// <summary>Playability checks that passed in the dungeon stage (one line each).</summary>
            public string[] PlayabilityChecks = Array.Empty<string>();
            /// <summary>Combat/aim/collision checks that passed in the dungeon stage (one line each).</summary>
            public string[] CombatChecks = Array.Empty<string>();
            public string Screenshot = "";
            /// <summary>Loot / ammo / starter checks that passed in the dungeon stage (one line each).</summary>
            public string[] LootChecks = Array.Empty<string>();
            /// <summary>Audio runtime checks that passed across the whole run (one line each).</summary>
            public string[] AudioChecks = Array.Empty<string>();
            /// <summary>Graphical inventory checks that passed in the dungeon stage (one line each).</summary>
            public string[] InventoryChecks = Array.Empty<string>();
            /// <summary>Run-start HP / dash icon / graphical HUD slot checks that passed (one line each).</summary>
            public string[] HudChecks = Array.Empty<string>();
            /// <summary>Merchant trade-flow checks that passed (one line each).</summary>
            public string[] MerchantChecks = Array.Empty<string>();
            /// <summary>Room-title / minimap / coin / vignette / dash-wipe checks that passed (one line each).</summary>
            public string[] RoomHudChecks = Array.Empty<string>();
            /// <summary>Weapon Cache interaction and reward checks that passed (one line each).</summary>
            public string[] WeaponCacheChecks = Array.Empty<string>();
            public string ProofDirectory = "";
            public string AudioEvidence = "";
            public string[] ProofCaptures = Array.Empty<string>();
            public int MusicPeakSamplesNonZero;
            public string UnityVersion = Application.unityVersion;
        }

        public const string ScreenshotArgument = "-screenshot";

        private GameApp _app;
        private readonly Result _result = new();
        private string _composed = string.Empty;

        public Result Current => _result;

        public void Begin(GameApp app)
        {
            _app = app;
            app.SceneComposed += name => _composed += name + ";";
            BeginAudioEvidence();
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            Application.logMessageReceived += OnLog;
            try
            {
                yield return WaitFor(() => _composed.Contains(SceneNames.MainMenu), "main menu composed");
                Stage("menu");
                var menu = _app.Menu;
                var outcome = menu.Play();
                if (outcome == PlayOutcome.Failed) { Fail("PLAY failed: " + menu.Message); yield break; }
                yield return WaitFor(() => _composed.Contains(SceneNames.Base), "base composed");
                Stage("base");
                var screen = FindFirstObjectByType<BaseHubScreen>();
                if (screen == null) { Fail("no BaseHubScreen"); yield break; }
                var session = menu.Session;
                var pistol = session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon);
                _result.Pistol = pistol != null ? pistol.InstanceId : "";
                if (!screen.Hub.Multiplayer.SetReady(true)) { Fail("ready refused"); yield break; }
                screen.Hub.Open(BaseStation.Transit);
                if (!screen.Hub.Transit.StartExpedition()) { Fail("start refused: " + screen.Hub.Transit.Feedback.Text); yield break; }
                yield return WaitFor(() => _composed.Contains(SceneNames.Dungeon), "dungeon composed");
                Stage("dungeon");
                yield return null;
                var run = FindFirstObjectByType<ExpeditionScene>();
                if (run == null || run.Rooms == null || run.Rooms.Count == 0) { Fail("no rooms composed"); yield break; }
                _result.RoomsComposed = run.Rooms.Count;
                _result.Biome = run.Expedition.State.Biome.ToString();
                if (run.Rig?.Player == null) { Fail("no player"); yield break; }
                // Let a few frames of gameplay run (physics, HUD, camera), then extract through the service.
                for (var i = 0; i < 30; i++) yield return null;
                yield return StartHpDashIconHudChecks(run);
                if (!string.IsNullOrEmpty(_result.Error)) yield break;
                yield return PlayabilityChecks(run);
                if (!string.IsNullOrEmpty(_result.Error)) yield break;
                yield return CombatChecks(run);
                if (!string.IsNullOrEmpty(_result.Error)) yield break;
                yield return LootAmmoStarterChecks(run);
                if (!string.IsNullOrEmpty(_result.Error)) yield break;
                yield return DungeonAudioChecks(run);
                if (!string.IsNullOrEmpty(_result.Error)) yield break;
                yield return InventoryChecks(run);
                if (!string.IsNullOrEmpty(_result.Error)) yield break;
                yield return MerchantChecks(run);
                if (!string.IsNullOrEmpty(_result.Error)) yield break;
                yield return WeaponCacheChecks(run);
                if (!string.IsNullOrEmpty(_result.Error)) yield break;
                yield return RoomHudQolChecks(run);
                if (!string.IsNullOrEmpty(_result.Error)) yield break;
                // Optional visual proof from the shipped player (needs a graphics device, i.e. no -nographics).
                var shotIndex = Array.IndexOf(Environment.GetCommandLineArgs(), ScreenshotArgument);
                if (shotIndex >= 0 && shotIndex + 1 < Environment.GetCommandLineArgs().Length)
                {
                    var shotPath = Environment.GetCommandLineArgs()[shotIndex + 1];
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(shotPath)) ?? ".");
                    ScreenCapture.CaptureScreenshot(shotPath);
                    // The capture is written asynchronously; give it a couple of seconds of frames to land.
                    for (var i = 0; i < 180 && !File.Exists(shotPath); i++) yield return null;
                    _result.Screenshot = File.Exists(shotPath) ? shotPath : "";
                    Debug.Log("[SMOKE] screenshot " + (File.Exists(shotPath) ? "written → " + shotPath : "NOT written (no graphics device?)"));
                }
                run.Expedition.AddCarriedCoins(25);
                var summary = run.Expedition.Return();
                if (!summary.IsSuccess) { Fail("return not successful"); yield break; }
                yield return WaitFor(() => _composed.Split(';').Length >= 4 && _composed.EndsWith(SceneNames.Base + ";"), "base composed after return");
                Stage("return");
                _result.BankedCoinsAfterReturn = menu.Session.Profile.BankedCoins;
                _result.TotalXpAfterReturn = menu.Session.Profile.TotalXp;
                if (menu.Session.SaveNow("smoke") != RuinRail.Persistence.SaveError.None) { Fail("save failed"); yield break; }
                var reloaded = _app.ProbeSave();
                _result.SaveReloaded = reloaded.Success && reloaded.BankedCoins == _result.BankedCoinsAfterReturn && Array.IndexOf(reloaded.EquippedInstanceIds, _result.Pistol) >= 0;
                if (!_result.SaveReloaded) { Fail("reload mismatch"); yield break; }

                // Second expedition: leave it through the pause menu (RETURN TO MAIN MENU → confirm). The run must resolve
                // as the one failure transaction, the profile must reload with the marker closed and banked state intact.
                Stage("leave");
                screen = FindFirstObjectByType<BaseHubScreen>();
                if (screen == null) { Fail("no BaseHubScreen after return"); yield break; }
                var bankedBeforeLeave = menu.Session.Profile.BankedCoins;
                if (!screen.Hub.Multiplayer.SetReady(true)) { Fail("ready refused (2)"); yield break; }
                screen.Hub.Open(BaseStation.Transit);
                if (!screen.Hub.Transit.StartExpedition()) { Fail("second start refused: " + screen.Hub.Transit.Feedback.Text); yield break; }
                yield return WaitFor(() => _composed.EndsWith(SceneNames.Dungeon + ";"), "second dungeon composed");
                yield return null;
                run = FindFirstObjectByType<ExpeditionScene>();
                if (run == null || run.Pause == null || run.PauseScreen == null) { Fail("no pause screen on the second run"); yield break; }
                var expedition = run.Expedition;
                run.Pause.Open();
                yield return null;
                if (!run.PauseScreen.IsShowing || run.PauseScreen.Controls.Count < 4) { Fail("pause screen not usable"); yield break; }
                if (RuinRail.UI.Theme.CursorService.Current != RuinRail.UI.Theme.CursorKind.Pointer) { Fail("pause did not take the pointer cursor"); yield break; }
                run.Pause.Activate(RuinRail.UI.Pause.PauseMenuItem.ReturnToMainMenu);
                if (!run.Pause.IsConfirming) { Fail("no leave confirmation"); yield break; }
                run.Pause.Confirm();
                yield return WaitFor(() => _composed.EndsWith(SceneNames.MainMenu + ";"), "main menu composed after leaving");
                yield return MenuAudioChecksAfterReturn();
                if (!string.IsNullOrEmpty(_result.Error)) yield break;
                var afterLeave = _app.ProbeSave();
                _result.ReturnToMenuOk = expedition.LastSummary != null && expedition.LastSummary.Outcome == ExpeditionOutcome.Failed
                    && menu.Session == null && afterLeave.Success && !afterLeave.ExpeditionMarkerOpen && afterLeave.BankedCoins == bankedBeforeLeave
                    && RuinRail.UI.Theme.CursorService.Current == RuinRail.UI.Theme.CursorKind.Pointer;
                if (!_result.ReturnToMenuOk) { Fail($"return to main menu: outcome {expedition.LastSummary?.Outcome}, session {(menu.Session == null ? "closed" : "open")}, marker {afterLeave.ExpeditionMarkerOpen}, banked {afterLeave.BankedCoins}/{bankedBeforeLeave}"); yield break; }
                if (menu.Play() == PlayOutcome.Failed || menu.AbandonedExpedition != null) { Fail("profile did not continue cleanly after leaving"); yield break; }

                _result.Success = true;
                Stage("done");
            }
            finally
            {
                _result.ScenesComposed = _composed;
                Write();
                Application.logMessageReceived -= OnLog;
                Application.Quit(_result.Success ? 0 : 1);
            }
        }

        /// <summary>
        /// The playability pass verified in the shipped player: bodies and held weapons drawn, exits sealed, HUD and
        /// inventory on the pixel face, cursors bound and owned by gameplay, pause usable, and a combat room entered
        /// from its interior with the door shut behind and enemies (with health bars) inside it.
        /// </summary>
        private IEnumerator PlayabilityChecks(ExpeditionScene run)
        {
            var passed = new System.Collections.Generic.List<string>();
            void Check(string what, bool ok) { if (ok) passed.Add(what); else if (string.IsNullOrEmpty(_result.Error)) Fail("playability: " + what); }

            var player = run.Rig.Player;
            var body = RuinRail.Presentation.Animation.CharacterVisual.RendererOf(player);
            Check("player body drawn", body != null && body.enabled && body.sprite != null);
            var held = player.GetComponent<RuinRail.Presentation.Animation.HeldWeaponVisual>();
            Check("held weapon drawn (" + (held != null ? held.ShownWeaponId : "none") + ")", held != null && held.IsVisible);
            Check("no open exit into the void", run.ExitProblems.Count == 0);
            var hud = FindFirstObjectByType<RuinRail.UI.Hud.DungeonHudView>();
            Check("HUD on the pixel face", hud != null && hud.GetComponentsInChildren<UnityEngine.UI.Text>(true).All(t => t.font == RuinRail.UI.Theme.UiFont.Font() && t.font.name != "LegacyRuntime"));
            var inventory = FindFirstObjectByType<RuinRail.UI.Inventory.InventoryView>();
            Check("inventory on the pixel face", inventory != null && inventory.GetComponentsInChildren<UnityEngine.UI.Text>(true).All(t => t.font == RuinRail.UI.Theme.UiFont.Font()));
            var skin = RuinRail.UI.Theme.UiSkin.Load();
            Check("custom cursors bound", skin != null && skin.HasCursors);
            Check("gameplay owns the aim cursor", RuinRail.UI.Theme.CursorService.Current == RuinRail.UI.Theme.CursorKind.Aim);
            run.Pause.Open();
            yield return null;
            Check("pause menu usable (4 controls, pointer cursor)", run.PauseScreen.IsShowing && run.PauseScreen.Controls.Count >= 4 && RuinRail.UI.Theme.CursorService.Current == RuinRail.UI.Theme.CursorKind.Pointer);
            run.Pause.Close();
            yield return null;
            Check("aim cursor restored after pause", RuinRail.UI.Theme.CursorService.Current == RuinRail.UI.Theme.CursorKind.Aim);

            // Combat room: enter from its interior, the door shuts behind, the encounter is inside with bodies and bars.
            var node = run.Generation.Graph.Nodes.FirstOrDefault(n => n.Type == RuinRail.Dungeon.Rooms.RoomType.Combat && !n.IsElite);
            if (node != null)
            {
                var room = run.Rooms[node.Id];
                var volume = RuinRail.Dungeon.Runtime.RoomEntryTrigger.InteriorVolume(room.Root.Size);
                var centre = (Vector2)room.Root.transform.TransformPoint(volume.center);
                var rb = player.GetComponent<Rigidbody2D>();
                player.transform.position = centre;
                rb.position = centre;
                for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                yield return null;
                Check("combat room activated from its interior", room.Lifecycle == RuinRail.Dungeon.Runtime.RoomLifecycleState.Active);
                Check("entry locked behind the player", room.DoorsLocked && room.Doors.Where(d => run.Generation.Layout.IsSocketUsed(node.Id, d.Socket.Direction)).All(d => d.IsBlocking));
                var enemies = FindObjectsByType<RuinRail.Gameplay.Enemies.EnemyController>(FindObjectsSortMode.None).Where(e => e != null).ToList();
                var bounds = new Rect(room.Root.transform.position, (Vector2)room.Root.Size * RuinRail.Dungeon.Grid.GridConstants.TileWorldSize);
                Check("enemies spawned inside the entered room", enemies.Count > 0 && enemies.All(e => bounds.Contains(e.transform.position)));
                Check("enemy bodies drawn", enemies.All(e => RuinRail.Presentation.Animation.CharacterVisual.RendererOf(e.gameObject)?.sprite != null));
                var bar = enemies.Count > 0 ? enemies[0].GetComponent<RuinRail.Presentation.Vfx.WorldHealthBar>() : null;
                Check("enemy health bar hidden at full health", bar != null && !bar.IsVisible);
                if (enemies.Count > 0) enemies[0].GetComponent<RuinRail.Gameplay.Combat.HealthComponent>().TryApplyDamage(new RuinRail.Gameplay.Combat.DamageRequest(1));
                Check("enemy health bar shown after damage", bar != null && bar.IsVisible);
            }
            else
            {
                Check("combat room present on the depth", false);
            }

            _result.PlayabilityChecks = passed.ToArray();
            Debug.Log("[SMOKE] playability: " + string.Join("; ", passed));
        }

        /// <summary>
        /// Combat/aim/collision checks in the shipped player, after the room checks left the player inside the locked
        /// combat room with its encounter: enemies have solid bodies and hurtboxes, the door art is final, no effect
        /// falls back to the placeholder quad, a projectile aimed at a hurtbox takes health, the assist bends a near
        /// miss but never through a shut door, the auto reload starts on the last round, nothing gets through walls or
        /// the shut door (a Charger charge included), and the doorway is traversable again once open.
        /// </summary>
        private IEnumerator CombatChecks(ExpeditionScene run)
        {
            var passed = new System.Collections.Generic.List<string>();
            void Check(string what, bool ok) { if (ok) passed.Add(what); else if (string.IsNullOrEmpty(_result.Error)) Fail("combat: " + what); }
            var player = run.Rig.Player;
            var content = _app.Content;
            var node = run.Generation.Graph.Nodes.FirstOrDefault(n => n.Type == RuinRail.Dungeon.Rooms.RoomType.Combat && !n.IsElite);
            if (node == null) { Check("combat room present on the depth", false); yield break; }
            var room = run.Rooms[node.Id];
            var rect = new Rect(room.Root.transform.position, (Vector2)room.Root.Size * RuinRail.Dungeon.Grid.GridConstants.TileWorldSize);
            var enemies = FindObjectsByType<RuinRail.Gameplay.Enemies.EnemyController>(FindObjectsSortMode.None).Where(e => e != null && e.IsAlive).ToList();
            Check("enemies have solid bodies", enemies.Count > 0 && enemies.All(e => e.GetComponent<CircleCollider2D>() != null && !e.GetComponent<CircleCollider2D>().isTrigger && e.gameObject.layer == RuinRail.Gameplay.Combat.CombatLayers.Enemy));
            Check("enemies have hurtboxes", enemies.All(e => e.GetComponentInChildren<RuinRail.Gameplay.Combat.CombatHurtbox>() != null));

            // Door art: the entry is drawn shut with the biome's locked sprite while it is solid.
            var entry = room.Doors.FirstOrDefault(d => d.Socket != null && run.Generation.Layout.IsSocketUsed(node.Id, d.Socket.Direction));
            var skin = content.DoorSkinFor(run.Expedition.State.Biome);
            Check("door visual is the final biome art (locked)", entry != null && skin != null && entry.IsBlocking && entry.IsPlateVisible && entry.DoorRenderer != null && entry.DoorRenderer.sprite == skin.Locked);

            // Effects: every kind the runtime spawns has final frames bound; nothing live is the placeholder quad.
            var effects = FindFirstObjectByType<RuinRail.Presentation.Vfx.EffectPool>();
            var kinds = new[] { "muzzle", "impact", "explosion", "melee", "stagger", "heal", "status", "loot_glow", "telegraph_stationary", "telegraph_dash", "telegraph_projectile", "telegraph_zone", "telegraph_slam" };
            Check("no placeholder effect art on the release path", effects != null && kinds.All(effects.HasArtFor));

            // Direct hit: a projectile from the weapon pivot at a frozen enemy's hurtbox centre takes health.
            var weapon = run.Rig.Loadout.ActiveWeapon as RuinRail.Gameplay.Combat.Weapons.RangedWeapon;
            var aiming = player.GetComponent<RuinRail.Gameplay.Player.PlayerAiming>();
            var victim = enemies.OrderByDescending(e => e.GetComponent<RuinRail.Gameplay.Combat.HealthComponent>().MaxHealth).FirstOrDefault();
            if (weapon != null && victim != null && aiming != null)
            {
                victim.enabled = false;
                var vb = victim.GetComponent<Rigidbody2D>();
                vb.linearVelocity = Vector2.zero;
                vb.bodyType = RigidbodyType2D.Kinematic;
                var origin = aiming.AimOrigin;
                var at = ClearSpotAround(origin, 4f) ?? ClearSpotAround(origin, 3f) ?? origin + Vector2.right * 4f; // rooms have cover: a clear line for the reference shot
                victim.transform.position = at;
                vb.position = at;
                var health = victim.GetComponent<RuinRail.Gameplay.Combat.HealthComponent>();
                if (health.MaxHealth < weapon.Definition.DamageMax * 3) health.SetMaxHealth(weapon.Definition.DamageMax * 3);
                Physics2D.SyncTransforms();
                yield return new WaitForFixedUpdate();
                var hurtbox = victim.GetComponentInChildren<RuinRail.Gameplay.Combat.CombatHurtbox>();
                var hp0 = health.CurrentHealth;
                var direction = (hurtbox.AimPoint - origin).normalized;
                var direct = RuinRail.Gameplay.Combat.Weapons.ShotSolver.Solve(origin, origin + direction * 0.55f, direction, weapon.Definition.Range, weapon.Definition.WeaponClass, player, RuinRail.Gameplay.Combat.DamageTeam.Player, null, null);
                run.Rig.Projectiles.Spawn(direct.SpawnPosition, new RuinRail.Gameplay.Combat.Projectiles.ProjectileSpawnData(weapon.Definition.DamageMin, weapon.Definition.ProjectileSpeed, weapon.Definition.Range, 0f, 0f, direct.Direction, player, sourceTeam: RuinRail.Gameplay.Combat.DamageTeam.Player));
                var end = Time.time + 1.5f;
                while (Time.time < end && health.CurrentHealth == hp0) yield return null;
                Check("direct crosshair-on-enemy projectile reduces HP", health.CurrentHealth < hp0);

                // Assist: 10 degrees off the hurtbox is bent onto it; the same target behind the shut door is not.
                var off = (Vector2)(Quaternion.Euler(0f, 0f, 10f) * direction);
                var assisted = RuinRail.Gameplay.Combat.Weapons.ShotSolver.Solve(origin, origin + off * 0.55f, off, weapon.Definition.Range, weapon.Definition.WeaponClass, player, RuinRail.Gameplay.Combat.DamageTeam.Player, content.AimAssist, null);
                Check("aim assist bends a near miss onto the hurtbox", assisted.Assisted && Vector2.Angle(assisted.Direction, hurtbox.AimPoint - assisted.SpawnPosition) < 1f);
                if (entry != null)
                {
                    var (dc, _) = entry.BlockerArea();
                    var step = (Vector2)RuinRail.Dungeon.Rooms.DoorDirections.Step(entry.Socket.Direction);
                    var behind = dc + step * 2f;
                    victim.transform.position = behind;
                    vb.position = behind;
                    Physics2D.SyncTransforms();
                    yield return new WaitForFixedUpdate();
                    var from = dc - step * 2.5f;
                    var throughDoor = RuinRail.Gameplay.Combat.Weapons.ShotSolver.Solve(from, from + step * 0.55f, step, weapon.Definition.Range, weapon.Definition.WeaponClass, player, RuinRail.Gameplay.Combat.DamageTeam.Player, content.AimAssist, null);
                    Check("aim assist does not target through a shut door", !throughDoor.Assisted);
                    victim.transform.position = at;
                    vb.position = at;
                    Physics2D.SyncTransforms();
                }

                // Auto reload on the final round.
                weapon.ApplyAuthoritativeState(1, false);
                var fired = weapon.TryFire();
                Check("auto reload starts after the final magazine round", fired && weapon.MagazineAmmo == 0 && weapon.IsReloading && weapon.AutoReloads == 1);
            }
            else
            {
                Check("ranged weapon, aiming and a target available", false);
            }

            // Walls and the shut door hold the pack; the player stands outside a plain wall, then outside the door.
            var wallSide = new[] { RuinRail.Dungeon.Rooms.DoorDirection.West, RuinRail.Dungeon.Rooms.DoorDirection.East, RuinRail.Dungeon.Rooms.DoorDirection.South, RuinRail.Dungeon.Rooms.DoorDirection.North }
                .First(d => room.Root.GetSocket(d) == null || !run.Generation.Layout.IsSocketUsed(node.Id, d));
            var wallStep = (Vector2)RuinRail.Dungeon.Rooms.DoorDirections.Step(wallSide);
            var wallPoint = rect.center + Vector2.Scale(wallStep, rect.size * 0.5f);
            var body = player.GetComponent<Rigidbody2D>();
            void Put(Vector2 p) { player.transform.position = p; body.position = p; body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms(); }
            Put(wallPoint + wallStep * 1.6f);
            var worst = 0f;
            for (var i = 0; i < 100; i++) { yield return new WaitForFixedUpdate(); foreach (var e in enemies) if (e != null && e.IsAlive && e.enabled) worst = Mathf.Max(worst, Penetration(e)); }
            Check("enemies cannot pass through walls", enemies.Where(e => e != null && e.IsAlive).All(e => rect.Contains(e.transform.position)) && worst <= 0.12f);

            // A Charger a few tiles inside the wall, on floor clear of cover, charging at the player standing just outside it.
            // The pack is held still for the charge so the lane is the Charger's alone: this proves the wall, not a pile-up.
            foreach (var e in enemies) { if (e == null || !e.IsAlive) continue; e.enabled = false; var eb = e.GetComponent<Rigidbody2D>(); eb.linearVelocity = Vector2.zero; eb.bodyType = RigidbodyType2D.Kinematic; }
            // A lane of its own (no cover, none of the pack pressed on the wall there), the player just outside its end.
            var chargerSpot = wallPoint - wallStep * 3.5f;
            var lanePoint = wallPoint;
            var along = new Vector2(-wallStep.y, wallStep.x);
            var laneFound = false;
            foreach (var side in new[] { 0f, 2f, -2f, 4f, -4f, 1f, -1f, 3f, -3f })
            {
                foreach (var back in new[] { 3.5f, 3f, 4f, 2.5f, 4.5f })
                {
                    var face = wallPoint + along * side;
                    var candidate = face - wallStep * back;
                    if (!rect.Contains(candidate) || !RuinRail.Dungeon.Runtime.RoomRuntime.IsSpawnClear(candidate)) continue;
                    var clear = true;
                    foreach (var hit in Physics2D.CircleCastAll(candidate, 0.45f, wallStep, back - 1.6f))
                        if (hit.collider != null && !hit.collider.isTrigger && (hit.collider.GetComponentInParent<RuinRail.Gameplay.Combat.EnvironmentObstacle>() != null || hit.collider.GetComponentInParent<RuinRail.Gameplay.Enemies.EnemyController>() != null)) { clear = false; break; }
                    if (!clear) continue;
                    chargerSpot = candidate;
                    lanePoint = face;
                    laneFound = true;
                    break;
                }

                if (laneFound) break;
            }

            Put(lanePoint + wallStep * 1.6f);

            var charger = new RuinRail.Gameplay.Enemies.DefaultEnemySpawner(content.Stagger).Spawn(content.Enemies.First(e => e.Id == "charger"), chargerSpot, player.transform);
            run.BindEnemyPresentation(charger);
            var charge = charger.GetComponent<RuinRail.Gameplay.Enemies.EnemyChargeAttack>();
            var chargerWorst = 0f;
            var deadline = Time.time + 5f;
            while (Time.time < deadline && charge.ChargesStarted == 0) { yield return new WaitForFixedUpdate(); chargerWorst = Mathf.Max(chargerWorst, Penetration(charger)); }
            for (var i = 0; i < 40; i++) { yield return new WaitForFixedUpdate(); chargerWorst = Mathf.Max(chargerWorst, Penetration(charger)); }
            var chargerOk = charge.ChargesStarted == 1 && charge.LastChargeStoppedByWall && rect.Contains(charger.transform.position) && chargerWorst <= 0.12f;
            Check("fast enemy (Charger) cannot tunnel through solid geometry" + (chargerOk ? "" : $" [charges={charge.ChargesStarted} stoppedByWall={charge.LastChargeStoppedByWall} inside={rect.Contains(charger.transform.position)} penetration={chargerWorst:0.000} spot={chargerSpot} end={charger.transform.position} wall={lanePoint} lane={laneFound}]"), chargerOk);
            Destroy(charger.gameObject);
            foreach (var e in enemies) { if (e == null || !e.IsAlive || e == victim) continue; e.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Dynamic; e.enabled = true; }

            if (entry != null)
            {
                var (dc, _) = entry.BlockerArea();
                var step = (Vector2)RuinRail.Dungeon.Rooms.DoorDirections.Step(entry.Socket.Direction);
                Put(dc + step * 1.6f);
                for (var i = 0; i < 100; i++) { yield return new WaitForFixedUpdate(); foreach (var e in enemies) if (e != null && e.IsAlive && e.enabled) worst = Mathf.Max(worst, Penetration(e)); }
                Check("enemies cannot pass through the shut combat door", entry.IsBlocking && enemies.Where(e => e != null && e.IsAlive).All(e => rect.Contains(e.transform.position) && Vector2.Dot((Vector2)e.transform.position - dc, step) < 0f) && worst <= 0.12f);

                // Open again: the doorway is traversable and drawn open.
                room.UnlockDoors();
                yield return null;
                var hits = new Collider2D[8];
                var (centre, size) = entry.BlockerArea();
                var solid = Physics2D.OverlapBox(centre, size * 0.9f, 0f, ContactFilter2D.noFilter, hits);
                var blocked = false;
                for (var i = 0; i < solid; i++) if (hits[i] != null && !hits[i].isTrigger && hits[i].enabled && hits[i].GetComponentInParent<RuinRail.Gameplay.Combat.EnvironmentObstacle>() != null) blocked = true;
                Check("open doorway is traversable and drawn open", !entry.IsBlocking && !blocked && entry.IsOpenVisible && entry.DoorRenderer.sprite == skin.Open);
            }

            _result.CombatChecks = passed.ToArray();
            Debug.Log("[SMOKE] combat: " + string.Join("; ", passed));
        }

        /// <summary>A point at the distance from the origin that a body fits in, with nothing solid on the line between.</summary>
        private static Vector2? ClearSpotAround(Vector2 from, float distance)
        {
            for (var angle = 0; angle < 360; angle += 45)
            {
                var dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                var at = from + dir * distance;
                if (!RuinRail.Dungeon.Runtime.RoomRuntime.IsSpawnClear(at) || !RuinRail.Dungeon.Runtime.RoomRuntime.IsSpawnClear(at + Vector2.up * RuinRail.Gameplay.Combat.CombatHurtbox.NormalOffset.y)) continue;
                var blocked = false;
                foreach (var hit in Physics2D.CircleCastAll(from, 0.2f, dir, distance))
                    if (hit.collider != null && !hit.collider.isTrigger && hit.collider.GetComponentInParent<RuinRail.Gameplay.Combat.EnvironmentObstacle>() != null) { blocked = true; break; }
                if (!blocked) return at;
            }

            return null;
        }

        private static readonly Collider2D[] PenetrationHits = new Collider2D[16];

        /// <summary>How far an enemy body sits inside solid geometry (0 = touching or clear).</summary>
        private static float Penetration(RuinRail.Gameplay.Enemies.EnemyController actor)
        {
            var position = (Vector2)actor.transform.position;
            var radius = RuinRail.Gameplay.Enemies.DefaultEnemySpawner.BodyRadius;
            var count = Physics2D.OverlapCircle(position, radius, ContactFilter2D.noFilter, PenetrationHits);
            var worst = 0f;
            for (var i = 0; i < count; i++)
            {
                var c = PenetrationHits[i];
                if (c == null || c.isTrigger || !c.enabled || c.GetComponentInParent<RuinRail.Gameplay.Combat.EnvironmentObstacle>() == null || c.transform.IsChildOf(actor.transform)) continue;
                var distance = Vector2.Distance(c.ClosestPoint(position), position);
                worst = Mathf.Max(worst, c.OverlapPoint(position) ? radius + distance : radius - distance);
            }

            return worst;
        }

        private IEnumerator WaitFor(Func<bool> condition, string what)
        {
            var deadline = Time.realtimeSinceStartup + 60f;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline) { Fail("timeout waiting for " + what); yield break; }
                yield return null;
            }
        }

        private void Stage(string stage) { _result.Stage = stage; Debug.Log("[SMOKE] " + stage); }
        private void Fail(string error) { _result.Error = error; Debug.LogError("[SMOKE] FAIL " + error); }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if ((type == LogType.Exception || type == LogType.Error) && !condition.StartsWith("[SMOKE]") && string.IsNullOrEmpty(_result.Error)) _result.Error = type + ": " + condition;
        }

        private void Write()
        {
            WriteAudioEvidence();
            var path = Path.Combine(_app.SaveDirectory, GameApp.SmokeResultFile);
            File.WriteAllText(path, JsonUtility.ToJson(_result, true));
            Debug.Log("[SMOKE] result " + (_result.Success ? "PASS" : "FAIL") + " → " + path);
        }
    }
}
