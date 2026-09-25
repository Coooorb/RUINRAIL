using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Progression;
using RuinRail.Gameplay.Stats;
using RuinRail.Networking;
using RuinRail.UI.Base;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Final release audit: three shipping seams the dead-seam audit found composed by tests only, now proven through
    /// the real boot flow and the real run composition (no hand-built player, no hand-set config):
    /// • the player's stagger/knockback receiver carries the authored config and the Resilience resistances act on it;
    /// • the Defibrillator's revive request reaches the party's revive authority through the rig;
    /// • the Shelter's autosave points (113) reach disk at the end of the frame, without an explicit save.
    /// </summary>
    public sealed class ReleaseSeamCompositionTests
    {
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_release_seams_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            CursorService.SetApplier(_ => true);
            DamageAuthority.LocalIsAuthoritative = true;
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
            DamageAuthority.LocalIsAuthoritative = true;
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

        private IEnumerator EnterShelter()
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(11);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Seam Runner");
            hub.Onboarding.AcknowledgeStarterKit();
        }

        private IEnumerator EnterRun()
        {
            yield return EnterShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition(), "start: " + hub.Hub.Transit.Feedback.Text);
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;
        }

        [UnityTest]
        public IEnumerator LiveRun_PlayerImpactReceiver_IsComposed_KnockbackMovesThePlayer_ResistanceComesFromStats()
        {
            yield return EnterRun();
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var player = run.Rig.Player;
            var receiver = player.GetComponent<PlayerImpactReceiver>();
            Assert.IsNotNull(receiver, "every player entity carries the receiver");

            // Stand the player in open floor in the start room so the push is not a wall test.
            var body = player.GetComponent<Rigidbody2D>();
            var start = (Vector2)player.transform.position;
            var request = new ImpactRequest(Vector2.right, 3f, 0f);
            var expected = KnockbackMath.Distance(3f, run.Rig.StatsBinder.Stats.GetPercent(StatId.KnockbackResistance), _app.Content.Stagger);
            Assert.Greater(expected, 0f, "the authored config gives a 3-knockback hit a real distance");
            ImpactDispatcher.Apply(player.GetComponent<Collider2D>() != null ? (Component)player.GetComponent<Collider2D>() : player.transform, request);
            Assert.IsTrue(receiver.IsKnockbackActive, "the real dispatch reached the composed receiver (before the fix: config null → no knockback)");
            for (var i = 0; i < 40; i++) yield return new WaitForFixedUpdate();
            var moved = Vector2.Distance(start, body.position);
            Assert.Greater(moved, expected * 0.5f, $"the player was displaced ({moved:0.00} of {expected:0.00} tiles)");

            // Stagger: the authored threshold is reached by a heavy hit, and the Resilience stat is the one the receiver reads.
            var staggered = receiver.ApplyStagger(new ImpactRequest(Vector2.right, 0f, 1000f));
            Assert.IsTrue(staggered.Triggered, "a heavy stagger hit staggers the player");
            Assert.AreEqual(0, run.Rig.StatsBinder.Stats.GetPercent(StatId.StaggerResistance), "a fresh profile has no resistance");
        }

        [UnityTest]
        public IEnumerator LiveRun_Defibrillator_ReachesThePartyReviveAuthority_ThroughTheRig()
        {
            yield return EnterRun();
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var player = run.Rig.Player;
            var inventory = run.Rig.Inventory;

            // A fully Dead teammate in reach, on the run's own roster.
            var mate = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options
            {
                Name = "Teammate", IsLocal = false, BalanceConfig = _app.Content.PlayerBalance, Caps = _app.Content.StatCaps,
                Position = (Vector2)player.transform.position + Vector2.right * 0.6f, LifeRoster = run.Party.LifeRoster, ParticipantId = "teammate"
            });
            var mateLife = mate.GetComponent<PlayerLifeStateComponent>();
            var mateHealth = mate.GetComponent<HealthComponent>();
            mateHealth.TryApplyDamage(new DamageRequest(999999));
            mateLife.Tick(mateLife.BleedoutSeconds + 0.1f);
            Assert.AreEqual(PlayerLifeState.Dead, mateLife.State);

            inventory.Unequip(EquippedSlot.ActiveConsumable);
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("consumable_defibrillator", 1, Rarity.Legendary), EquippedSlot.ActiveConsumable));
            var result = run.Rig.Consumables.UseAction.TryUse();
            Assert.AreEqual(ConsumableUseResult.Started, result, "before the fix the rig configured no revive requester (UnsupportedEffect)");
            yield return null;
            Assert.AreEqual(PlayerLifeState.Alive, mateLife.State, "the Dead teammate is revived");
            Assert.AreEqual(Mathf.Max(1, Mathf.RoundToInt(mateHealth.MaxHealth * 0.3f)), mateHealth.CurrentHealth, "31/84: 30% Max HP");
            Assert.IsNull(inventory.GetEquipped(EquippedSlot.ActiveConsumable), "the single unit is spent");
            Assert.AreEqual(1, run.DefibrillatorRevives);
            Assert.AreEqual(ExpeditionScene.RevivedNotice, run.LastNotice);

            // Nobody left to revive: refused, and it costs nothing.
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("consumable_defibrillator", 1, Rarity.Legendary), EquippedSlot.ActiveConsumable));
            run.Rig.Consumables.UseAction.TryUse();
            yield return null;
            Assert.AreEqual(1, inventory.GetEquipped(EquippedSlot.ActiveConsumable)?.Quantity, "a refused use costs nothing");
            Assert.AreEqual(1, run.DefibrillatorRevives);
            Assert.AreEqual(ExpeditionScene.NoOneToReviveNotice, run.LastNotice);
            Object.DestroyImmediate(mate);
        }

        /// <summary>
        /// The post-Depth-30 reward continuation through the whole release path: the Shelter session composes the run's
        /// ExpeditionService with the shipped EconomyConfig, a live run descends 30 times through the real boss → vote
        /// path, and XP earned at D30 is unscaled while XP earned at D31 carries the curve's multiplier exactly once.
        /// </summary>
        [UnityTest]
        public IEnumerator LiveRun_PostDepth30Rewards_ScaleXpOnlyAfterDepth30_ThroughTheComposedService()
        {
            yield return EnterRun();
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var economy = _app.Content.Economy;
            var player = run.Rig.Player;
            player.GetComponent<HealthComponent>().SetInvulnerabilityState(new Guard());
            while (run.Expedition.State.Depth < 31)
            {
                var bossRoom = run.Rooms.Values.First(r => r.State.RoomType == RuinRail.Dungeon.Rooms.RoomType.Boss);
                var boss = bossRoom.GetComponent<RuinRail.Dungeon.Runtime.RoomContentBinding>().Boss;
                var centre = bossRoom.InteriorWorldBounds.center;
                player.transform.position = centre;
                player.GetComponent<Rigidbody2D>().position = centre;
                for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                boss.Boss.Health.TryApplyDamage(new DamageRequest(100000000));
                for (var i = 0; i < 60 && (run.Vote == null || run.Expedition.Transit?.State != RuinRail.Gameplay.Expedition.TransitDecisionState.Open); i++) yield return null;
                if (run.Expedition.State.Depth == 30)
                {
                    var before30 = run.Expedition.State.Stats.XpEarned;
                    run.Expedition.AddXp(100);
                    Assert.AreEqual(100, run.Expedition.State.Stats.XpEarned - before30, "D30: XP is exactly the authored amount (the curve starts after D30)");
                }

                var built = run.DepthsBuilt;
                Assert.IsTrue(run.Vote.Vote(RuinRail.Gameplay.Expedition.TransitChoice.DescendDeeper), $"descend from depth {run.Expedition.State.Depth}");
                for (var i = 0; i < 120 && run.DepthsBuilt == built; i++) yield return null;
                Assert.Greater(run.DepthsBuilt, built, $"depth {run.Expedition.State.Depth} built");
                for (var i = 0; i < 5; i++) yield return null;
            }

            var multiplier = economy.XpRewardMultiplier(31);
            Assert.Greater(multiplier, 1f, "the shipped curve pays more at D31");
            Assert.AreEqual((multiplier, economy.CoinRewardMultiplier(31)), run.Expedition.DeepDepthRewardMultipliers, "the composed service carries the shipped economy");
            var before31 = run.Expedition.State.Stats.XpEarned;
            run.Expedition.AddXp(100);
            Assert.AreEqual(Mathf.RoundToInt(100 * multiplier), run.Expedition.State.Stats.XpEarned - before31, "D31: XP carries the multiplier exactly once");
        }

        private sealed class Guard : IInvulnerabilityState
        {
            public bool IsInvulnerable { get; set; } = true;
        }

        [UnityTest]
        public IEnumerator Shelter_AutosavePoints_ReachDisk_AtTheEndOfTheFrame_WithoutAnExplicitSave()
        {
            yield return EnterShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            var session = _app.Menu.Session;
            yield return null;
            yield return null;
            var before = _app.ProbeSave();
            Assert.IsTrue(before.Success);

            // Skill spending (113 autosave point): nothing calls SaveNow; the flusher must write it.
            session.Progression.AddXp(LevelCurve.TotalXpForLevel(3));
            hub.Hub.Open(BaseStation.Character);
            yield return null;
            Assert.IsTrue(hub.Hub.Character.Allocate(SkillId.Vitality));
            yield return null;
            yield return null;
            var afterSkill = _app.ProbeSave();
            Assert.AreEqual(1, afterSkill.SkillRanks[(int)SkillId.Vitality], "the skill purchase is on disk without an explicit save");
            Assert.AreEqual(session.Profile.TotalXp, afterSkill.TotalXp);

            // Trader purchase (113 autosave point): coins and item settle on disk together.
            hub.Hub.Close();
            session.Banked.Credit(5000, "test_funds");
            hub.Hub.Open(BaseStation.Trader);
            yield return null;
            var offer = hub.Hub.Trader.Offers.First(o => !o.IsSold);
            Assert.IsTrue(hub.Hub.Trader.Buy(offer.Index), hub.Hub.Trader.Feedback.Text);
            yield return null;
            yield return null;
            var afterBuy = _app.ProbeSave();
            Assert.AreEqual(session.Banked.Balance, afterBuy.BankedCoins, "the trader settlement is on disk without an explicit save");
            Assert.AreEqual(5000 - offer.Price, afterBuy.BankedCoins);
        }
    }
}
