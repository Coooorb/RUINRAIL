using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Expedition;
using RuinRail.Networking;
using RuinRail.Presentation.Animation;
using RuinRail.Presentation.Vfx;
using RuinRail.UI.Base;
using RuinRail.UI.Hud;
using RuinRail.UI.Inventory;
using RuinRail.UI.Pause;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The final playability pass measured on live expedition runs (real boot flow, real scene composition): held
    /// weapon, remote replica through the presence harness, enemy / Elite / Boss health presentation, combat-room
    /// entry with the door shut behind the player, pause screens, inventory in the pixel face, cursor ownership, and
    /// RETURN TO MAIN MENU through the one expedition-failure transaction. Captures go to
    /// <c>TestResults/FinalPlayabilityProof</c>.
    /// </summary>
    public sealed class FinalPlayabilityProofTests
    {
        private const string Folder = "TestResults/FinalPlayabilityProof";
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_final_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            Directory.CreateDirectory(Folder);
            CursorService.SetApplier(_ => true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var screen in Object.FindObjectsByType<MainMenuScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
            foreach (var screen in Object.FindObjectsByType<BaseHubScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || IsTestRunner(root)) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            NetworkPlayerObject.VisualComposer = null;
            CursorService.Reset();
            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private static bool IsTestRunner(GameObject root)
        {
            if (root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (var component in root.GetComponents<Component>())
            {
                if (component != null && (component.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools")) return true;
            }

            return false;
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (_app.ComposedScene != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time (last: '{_app.ComposedScene}').");
                yield return null;
            }
        }

        private IEnumerator EnterDungeon()
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            Assert.AreEqual(CursorKind.Pointer, CursorService.Current, "menus own the pointer cursor");
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Proof Runner");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;
        }

        private static Vector2 InteriorCentre(ExpeditionScene run, int nodeId)
        {
            var root = run.Rooms[nodeId].Root;
            var volume = RoomEntryTrigger.InteriorVolume(root.Size);
            return root.transform.TransformPoint(volume.center);
        }

        private static IEnumerator Teleport(ExpeditionScene run, Vector2 position)
        {
            var body = run.Rig.Player.GetComponent<Rigidbody2D>();
            run.Rig.Player.transform.position = position;
            body.position = position;
            for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            for (var i = 0; i < 20; i++) yield return null; // the camera rig converges on the new position
        }

        private static string WriteCursorProof(Texture2D texture, string name, int scale)
        {
            var zoomed = new Texture2D(texture.width * scale, texture.height * scale, TextureFormat.RGBA32, false);
            var pixels = texture.GetPixels32();
            var output = new Color32[zoomed.width * zoomed.height];
            for (var y = 0; y < zoomed.height; y++)
            for (var x = 0; x < zoomed.width; x++)
                output[y * zoomed.width + x] = pixels[y / scale * texture.width + x / scale];
            zoomed.SetPixels32(output);
            zoomed.Apply();
            var path = Path.Combine(Folder, name + ".png");
            File.WriteAllBytes(path, zoomed.EncodeToPNG());
            Object.DestroyImmediate(zoomed);
            return path;
        }

        [UnityTest]
        public IEnumerator LiveRun_HeldWeapon_Replica_HealthBars_RoomEntry_Pause_Inventory_AndCursors()
        {
            yield return EnterDungeon();
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var ppu = run.Camera.Config.PixelsPerUnit;
            var player = run.Rig.Player;
            var content = _app.Content;
            Assert.AreEqual(CursorKind.Aim, CursorService.Current, "gameplay owns the aim cursor");

            // ---- 1. held P9 Ranger on the local player ----
            var held = player.GetComponent<HeldWeaponVisual>();
            Assert.IsNotNull(held, "held weapon visual composed");
            Assert.IsTrue(held.IsVisible);
            Assert.AreEqual("weapon_p9_ranger", held.ShownWeaponId);
            Assert.AreSame(content.WeaponSpriteFor("weapon_p9_ranger"), held.Renderer.sprite);
            var shot = LiveDungeonCapture.Capture(Folder, "01_local_player_held_p9_ranger", run.Camera.Camera, ppu);
            held.Renderer.enabled = false;
            var without = LiveDungeonCapture.Capture(Folder, "01_control_weapon_hidden", run.Camera.Camera, ppu);
            held.Renderer.enabled = true;
            var hand = shot.WorldToPixel(held.Pivot.position);
            Assert.Greater(Differing(shot, without, new RectInt(hand.x - 24, hand.y - 16, 48, 32)), 20, "the weapon sprite is drawn at the hands");

            // ---- 2. a remote replica through the presence harness: body + replicated weapon ----
            var factory = new LocalPlayerEntityFactory(content.PlayerBalance, content.StatCaps, _ => (Vector2)player.transform.position + Vector2.right * 2f,
                (go, _) => PlayerVisualComposer.Compose(go, content));
            var replica = factory.Spawn(new PlayerIdentity(1, "Mate", false), isLocalOwner: false);
            replica.GetComponent<RuinRail.Gameplay.Combat.Weapons.IHeldWeaponView>().ShowWeapon("weapon_p9_ranger");
            replica.GetComponent<RuinRail.Gameplay.Player.PlayerAiming>().ApplyReplicatedAim(Vector2.left);
            yield return null;
            yield return null;
            Assert.IsNotNull(CharacterVisual.RendererOf(replica).sprite, "the replica's body is drawn");
            Assert.IsTrue(replica.GetComponent<HeldWeaponVisual>().IsVisible, "the replica's held weapon is drawn");
            var duo = LiveDungeonCapture.Capture(Folder, "02_remote_replica_body_and_weapon", run.Camera.Camera, ppu);
            var replicaPx = duo.WorldToPixel(replica.transform.position);
            Assert.Greater(duo.CountLit(new RectInt(replicaPx.x - 12, replicaPx.y, 24, 40), 60), 60, "replica body pixels present");
            Object.DestroyImmediate(replica);

            // ---- 9 + 3. combat room entry: activation inside, door shut behind, enemies with health bars ----
            var combatNode = run.Generation.Graph.Nodes.First(n => n.Type == RoomType.Combat && !n.IsElite);
            var combatRoom = run.Rooms[combatNode.Id];
            Assert.AreEqual(RoomLifecycleState.Unentered, combatRoom.Lifecycle);
            yield return Teleport(run, InteriorCentre(run, combatNode.Id));
            Assert.AreEqual(RoomLifecycleState.Active, combatRoom.Lifecycle, "entering the interior activates the room");
            Assert.IsTrue(combatRoom.DoorsLocked);
            yield return null;
            foreach (var door in combatRoom.Doors)
            {
                if (door.Socket != null && run.Generation.Layout.IsSocketUsed(combatNode.Id, door.Socket.Direction))
                    Assert.IsTrue(door.IsBlocking && door.IsPlateVisible, $"entry {door.Socket.Direction} is shut behind the player");
            }

            var enemies = Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Where(e => e != null && e.GetComponent<HealthComponent>().IsAlive).ToList();
            Assert.Greater(enemies.Count, 0, "the encounter spawned inside the entered room");
            var roomBounds = new Rect(combatRoom.Root.transform.position, (Vector2)combatRoom.Root.Size * GridConstants.TileWorldSize);
            foreach (var e in enemies)
            {
                Assert.IsTrue(roomBounds.Contains(e.transform.position), $"enemy {e.name} inside the entered room");
                Assert.IsNotNull(CharacterVisual.RendererOf(e.gameObject)?.sprite, $"enemy {e.name} body drawn");
                Assert.IsNotNull(e.GetComponent<WorldHealthBar>(), $"enemy {e.name} carries a health bar");
                Assert.IsFalse(e.GetComponent<WorldHealthBar>().IsVisible, "full health: bar hidden");
            }

            Debug.Log($"[PROOF] player {player.transform.position} body {player.GetComponent<Rigidbody2D>().position} camera {run.Camera.Camera.transform.position} room {roomBounds} enemies {string.Join(" ", enemies.Select(e => e.name + "@" + e.transform.position))} doors {string.Join(" ", combatRoom.Doors.Select(d => d.Socket.Direction + ":" + d.IsBlocking + "/" + d.IsPlateVisible + "@" + d.transform.position))}");
            LiveDungeonCapture.Capture(Folder, "09_combat_room_entry_locked_behind", run.Camera.Camera, ppu);

            // The camera clamps to the dungeon bounds, so the room may be off-centre: measure the bar on an enemy that is in frame.
            var camPos = (Vector2)run.Camera.Camera.transform.position;
            var inView = enemies.Where(e => Mathf.Abs(e.transform.position.x - camPos.x) < 8f && Mathf.Abs(e.transform.position.y - camPos.y) < 4f).OrderBy(e => Vector2.Distance(e.transform.position, camPos)).ToList();
            var victim = (inView.Count > 0 ? inView : enemies)[0];
            var victimHealth = victim.GetComponent<HealthComponent>();
            victimHealth.TryApplyDamage(new DamageRequest(Mathf.Max(1, victimHealth.MaxHealth * 4 / 10)));
            Assert.IsTrue(victim.GetComponent<WorldHealthBar>().IsVisible, "damaged: bar visible");
            yield return null;
            var enemyShot = LiveDungeonCapture.Capture(Folder, "03_normal_enemy_health_bar", run.Camera.Camera, ppu);
            var barPx = enemyShot.WorldToPixel((Vector2)victim.transform.position + Vector2.up * 1.35f);
            if (inView.Count > 0) Assert.Greater(enemyShot.CountLit(new RectInt(barPx.x - 14, barPx.y - 3, 28, 6), 60), 10, "health bar pixels above the enemy");

            // ---- 4. an Elite with the stronger bar ----
            EliteController elite = null;
            var eliteNode = run.Generation.Graph.Nodes.FirstOrDefault(n => n.IsElite);
            if (eliteNode != null && run.Rooms[eliteNode.Id].Engagement is EliteEngagement)
            {
                yield return Teleport(run, InteriorCentre(run, eliteNode.Id));
                var engagement = (EliteEngagement)run.Rooms[eliteNode.Id].Engagement;
                elite = engagement.Encounter != null ? engagement.Encounter.Elite : null;
            }

            if (elite == null)
            {
                // No Elite node on this depth's graph: spawn the biome's Elite through the real spawner and the real presentation binding.
                var definition = content.Elites.First(d => d.Biome == run.Expedition.State.Biome);
                var encounter = new DefaultEliteSpawner(content.Stagger).Spawn(definition, (Vector2)player.transform.position + Vector2.right * 3f, combatRoom.transform, player.transform);
                elite = encounter.Elite;
                run.BindActorPresentation(elite, definition.Id, isElite: true);
            }

            var eliteBar = elite.GetComponent<WorldHealthBar>();
            Assert.IsNotNull(eliteBar, "Elite carries the Elite bar");
            Assert.AreEqual(WorldHealthBar.Style.Elite, eliteBar.BarStyle);
            elite.Health.TryApplyDamage(new DamageRequest(Mathf.Max(1, elite.Health.MaxHealth * 3 / 10)));
            Assert.IsTrue(eliteBar.IsVisible);
            yield return null;
            LiveDungeonCapture.Capture(Folder, "04_elite_health_bar", run.Camera.Camera, ppu);

            // ---- 5. the Boss bar during the boss encounter ----
            var bossNode = run.Generation.Graph.BossId;
            var bossBinding = run.Rooms[bossNode].GetComponent<RoomContentBinding>();
            Assert.IsNotNull(bossBinding?.Boss?.Boss, "boss composed for the depth");
            Assert.IsFalse(run.Hud.Snapshot.BossVisible, "no boss bar before the encounter");
            yield return Teleport(run, InteriorCentre(run, bossNode));
            Assert.IsTrue(bossBinding.Boss.IsStarted, "the boss engagement started on entry");
            run.Hud.Tick();
            Assert.IsTrue(run.Hud.Snapshot.BossVisible, "the boss bar is up during the encounter");
            bossBinding.Boss.Boss.Health.TryApplyDamage(new DamageRequest(Mathf.Max(1, bossBinding.Boss.Boss.Health.MaxHealth / 4)));
            yield return null;
            var hud = Object.FindFirstObjectByType<DungeonHudView>();
            Assert.IsTrue(hud.BossVisible);
            StringAssert.Contains(bossBinding.Boss.Boss.Definition.DisplayName, hud.BossNameText);
            LiveDungeonCapture.Capture(Folder, "05_boss_bar_during_encounter", run.Camera.Camera, ppu);

            // ---- 6-8. pause screens ----
            run.Pause.Open();
            yield return null;
            Assert.IsTrue(run.PauseScreen.IsShowing);
            Assert.AreEqual(CursorKind.Pointer, CursorService.Current, "pause takes the pointer cursor");
            LiveDungeonCapture.Capture(Folder, "06_pause_menu", run.Camera.Camera, ppu);
            run.Pause.Activate(PauseMenuItem.Settings);
            yield return null;
            Assert.IsTrue(run.PauseScreen.SettingsShowing);
            LiveDungeonCapture.Capture(Folder, "07_pause_settings", run.Camera.Camera, ppu);
            run.Pause.Back();
            run.Pause.Activate(PauseMenuItem.ReturnToMainMenu);
            yield return null;
            Assert.IsTrue(run.PauseScreen.ConfirmationShowing);
            LiveDungeonCapture.Capture(Folder, "08_pause_return_to_main_menu_confirmation", run.Camera.Camera, ppu);
            run.Pause.CancelConfirmation();
            run.Pause.Close();
            yield return null;
            Assert.AreEqual(CursorKind.Aim, CursorService.Current, "the aim cursor returns after the pause closes");

            // ---- 12. inventory in the pixel face ----
            var inventory = Object.FindFirstObjectByType<InventoryView>();
            inventory.ViewModel.Open();
            yield return null;
            Assert.IsTrue(inventory.IsVisible);
            Assert.AreEqual(CursorKind.Pointer, CursorService.Current, "the inventory overlay takes the pointer");
            foreach (var text in inventory.GetComponentsInChildren<UnityEngine.UI.Text>(true)) Assert.AreSame(UiFont.Font(), text.font, text.name);
            LiveDungeonCapture.Capture(Folder, "12_inventory_pixel_font", run.Camera.Camera, ppu);
            inventory.ViewModel.Close();
            yield return null;
            Assert.AreEqual(CursorKind.Aim, CursorService.Current);

            // ---- 10-11. cursors: a hardware cursor is not part of a rendered frame; the bound textures are the proof ----
            var skin = UiSkin.Load();
            Assert.IsTrue(skin.HasCursors);
            WriteCursorProof(skin.CursorPointer, "10_cursor_ui_pointer_x4", 4);
            WriteCursorProof(skin.CursorHover, "10_cursor_ui_hover_x4", 4);
            WriteCursorProof(skin.CursorAim, "11_cursor_gameplay_aim_x4", 4);
        }

        private static int Differing(LiveDungeonCapture.Result a, LiveDungeonCapture.Result b, RectInt rect)
        {
            var count = 0;
            for (var y = rect.yMin; y < rect.yMax; y++)
            for (var x = rect.xMin; x < rect.xMax; x++)
            {
                var pa = a.At(x, y);
                var pb = b.At(x, y);
                if (pa.r != pb.r || pa.g != pb.g || pa.b != pb.b) count++;
            }

            return count;
        }

        [UnityTest]
        public IEnumerator LiveRun_Corpses_StayWhereAndHowTheyDied_UnderPlayerEnemyBlastAndKnockbackPressure()
        {
            yield return EnterDungeon();
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var ppu = run.Camera.Config.PixelsPerUnit;
            var player = run.Rig.Player;
            var combatNode = run.Generation.Graph.Nodes.First(n => n.Type == RoomType.Combat && !n.IsElite);
            var combatRoom = run.Rooms[combatNode.Id];
            yield return Teleport(run, InteriorCentre(run, combatNode.Id));
            var enemies = Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Where(e => e != null && e.IsAlive).ToList();
            Assert.GreaterOrEqual(enemies.Count, 2, "a victim and at least one living enemy to press against it");

            // A composed room enemy and a composed Elite (moveset path), each with a knockback still in flight when it dies.
            var grunt = enemies.OrderBy(e => Vector2.Distance(e.transform.position, player.transform.position)).First();
            var eliteDefinition = _app.Content.Elites.First(d => d.Biome == run.Expedition.State.Biome);
            var elite = new DefaultEliteSpawner(_app.Content.Stagger).Spawn(eliteDefinition, (Vector2)grunt.transform.position + Vector2.right * 2f, combatRoom.transform, player.transform).Elite;
            run.BindActorPresentation(elite, eliteDefinition.Id, isElite: true);
            yield return new WaitForFixedUpdate();
            var corpses = new List<GameObject> { grunt.gameObject, elite.gameObject };
            foreach (var corpse in corpses)
            {
                RuinRail.Gameplay.Combat.Impact.ImpactDispatcher.Apply(corpse.GetComponent<Collider2D>(), new RuinRail.Gameplay.Combat.Impact.ImpactRequest(Vector2.up, 12f, 0f, DamageKind.Normal, null, null));
                var health = corpse.GetComponent<HealthComponent>();
                Assert.IsTrue(health.TryApplyDamage(new DamageRequest(health.CurrentHealth + 1)));
            }

            yield return null;
            var positions = corpses.Select(c => c.transform.position).ToList();
            var rotations = corpses.Select(c => c.transform.rotation).ToList();
            var facings = corpses.Select(c => c.GetComponent<EnemyAnimationDriver>().Facing).ToList();
            LiveDungeonCapture.Capture(Folder, "20_corpses_at_death", run.Camera.Camera, ppu);

            // The player stands on each corpse, living enemies chase into it, a player-team blast and a direct knockback hit it.
            var pressureUntil = Time.time + 1.2f;
            var next = 0;
            while (Time.time < pressureUntil)
            {
                var at = (Vector2)positions[next++ % positions.Count];
                player.GetComponent<Rigidbody2D>().position = at + Vector2.left * 0.1f;
                RuinRail.Gameplay.Combat.Impact.ShockwaveResolver.Emit(at + new Vector2(0.4f, 0.4f), 3f, 12f, 30f, DamageTeam.Player);
                foreach (var corpse in corpses)
                    RuinRail.Gameplay.Combat.Impact.ImpactDispatcher.Apply(corpse.GetComponent<Collider2D>(), new RuinRail.Gameplay.Combat.Impact.ImpactRequest(Vector2.left, 12f, 30f, DamageKind.Normal, null, null));
                for (var i = 0; i < 6; i++) yield return new WaitForFixedUpdate();
            }

            yield return null;
            LiveDungeonCapture.Capture(Folder, "21_corpses_after_pressure_player_on_corpse", run.Camera.Camera, ppu);
            Debug.Log($"[PROOF] corpses {string.Join(" ", corpses.Select((c, i) => c.name + "@" + positions[i].ToString("F4") + "->" + c.transform.position.ToString("F4") + " facing " + facings[i] + "->" + c.GetComponent<EnemyAnimationDriver>().Facing))}");
            for (var i = 0; i < corpses.Count; i++)
            {
                var corpse = corpses[i];
                Assert.AreEqual(0f, Vector3.Distance(positions[i], corpse.transform.position), 1e-5f, $"{corpse.name}: the corpse must not move after death");
                Assert.AreEqual(0f, Quaternion.Angle(rotations[i], corpse.transform.rotation), 1e-3f, $"{corpse.name}: the corpse must not turn after death");
                Assert.AreEqual(facings[i], corpse.GetComponent<EnemyAnimationDriver>().Facing, $"{corpse.name}: the corpse keeps its death facing");
                Assert.AreEqual(EnemyAnimState.Death, corpse.GetComponent<EnemyAnimationDriver>().State);
                Assert.IsNotNull(CharacterVisual.RendererOf(corpse)?.sprite, $"{corpse.name}: the corpse is still drawn");
            }
        }

        [UnityTest]
        public IEnumerator ReturnToMainMenu_FromAnActiveExpedition_FailsTheRunOnce_KeepsSafeState_AndLoadsTheMainMenu()
        {
            yield return EnterDungeon();
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var session = _app.Menu.Session;
            var bankedBefore = session.Profile.BankedCoins;
            Assert.IsNull(session.Profile.SafeLoadout, "during a run every carried item is at risk: there is no safe copy (85)");
            var xpBefore = session.Profile.TotalXp;
            run.Expedition.AddCarriedCoins(30);
            run.Expedition.AddXp(15);
            var expedition = run.Expedition;

            run.Pause.Open();
            run.Pause.Activate(PauseMenuItem.ReturnToMainMenu);
            Assert.IsTrue(run.Pause.IsConfirming);
            run.Pause.Confirm();
            yield return WaitComposed(SceneNames.MainMenu);

            Assert.IsNotNull(expedition.LastSummary, "the expedition was resolved");
            Assert.AreEqual(ExpeditionOutcome.Failed, expedition.LastSummary.Outcome, "leaving mid-run is the approved failure, not an extraction");
            Assert.IsFalse(expedition.IsExpeditionActive);
            Assert.IsNull(_app.Menu.Session, "the session was left cleanly");
            Assert.AreEqual(1f, Time.timeScale, "no pause hold survives the leave");
            Assert.AreEqual(CursorKind.Pointer, CursorService.Current);

            var probe = _app.ProbeSave();
            Assert.IsTrue(probe.Success, "the safe profile was flushed");
            Assert.IsFalse(probe.ExpeditionMarkerOpen, "the failure transaction closed the expedition marker");
            Assert.AreEqual(bankedBefore, probe.BankedCoins, "carried coins are lost, banked coins untouched");
            Assert.AreEqual(0, probe.EquippedInstanceIds.Length, "the at-risk gear is lost with the failed run — never duplicated back into the safe profile");
            Assert.AreEqual(ExpeditionOutcome.Failed, expedition.LastSummary.Outcome);
            Assert.Greater(expedition.LastSummary.LostItems.Count, 0, "the loss is recorded by the one failure transaction");
            Assert.GreaterOrEqual(probe.TotalXp, xpBefore + 15, "XP earned still commits on failure");

            // PLAY again continues the same profile without an 'abandoned expedition' recovery message.
            var outcome = _app.Menu.Play();
            Assert.AreEqual(PlayOutcome.Continued, outcome);
            Assert.IsNull(_app.Menu.AbandonedExpedition, "the run was resolved properly, not left open");
            Assert.AreEqual(bankedBefore, _app.Menu.Session.Profile.BankedCoins);
        }

        [UnityTest]
        public IEnumerator ReturnToMainMenu_FromTheShelter_LeavesWithoutFabricatingAnExpeditionFailure()
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Shelter Leaver");
            hub.Onboarding.AcknowledgeStarterKit();
            var session = _app.Menu.Session;
            session.Banked.Credit(120, "test");
            var expeditionsBefore = session.Profile.ExpeditionsEnded;
            var equipped = session.Profile.SafeLoadout.Equipped.Select(e => e.Item.InstanceId).OrderBy(s => s).ToArray();

            _app.Menu.LeaveBase();
            _app.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);

            var probe = _app.ProbeSave();
            Assert.IsTrue(probe.Success);
            Assert.AreEqual(120, probe.BankedCoins, "saved on leave");
            Assert.IsFalse(probe.ExpeditionMarkerOpen, "no expedition was open, so none is marked");
            CollectionAssert.AreEqual(equipped, probe.EquippedInstanceIds.OrderBy(s => s).ToArray());
            Assert.AreEqual(PlayOutcome.Continued, _app.Menu.Play());
            Assert.IsNull(_app.Menu.AbandonedExpedition, "no expedition failure was fabricated by leaving the Shelter");
            Assert.AreEqual(expeditionsBefore, _app.Menu.Session.Profile.ExpeditionsEnded);
        }
    }
}
