using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.UI.Inventory;
using RuinRail.UI.RunEnd;
using RuinRail.UI.Theme;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// The Death / Run Lost screen: shown once from the closed failure transaction's summary with the tracked figures
    /// only, two real exits reachable by mouse, keyboard and controller through one focus list, no gameplay input
    /// leakage while it is up, and the trigger semantics — a solo death or a co-op wipe is final, a downed-and-
    /// revivable co-op player is not — driven by the existing <see cref="PartyExpeditionBinding"/> → <c>Fail()</c> path.
    /// </summary>
    public sealed class RunFailedScreenTests
    {
        private readonly List<Object> _created = new();
        private PlayerBalanceConfig _balance;
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private TimeScalePause _world;
        private MenuInput _input;
        private RunFailedViewModel _failed;
        private RunFailedScreen _screen;
        private int _shelter;
        private int _menu;

        [SetUp]
        public void SetUp()
        {
            CursorService.Reset();
            CursorService.SetApplier(_ => true);
            CursorService.SetBase(CursorKind.Aim);
            GameplayInputGate.Reset();
            DamageAuthority.LocalIsAuthoritative = true;
            _balance = AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            _world = new TimeScalePause();
            _shelter = 0;
            _menu = 0;
            _failed = new RunFailedViewModel(() => _shelter++, () => _menu++);
            _failed.ConfigurePause(_world, isCoop: false);
            var canvas = UiKit.Canvas("RunUi", 20);
            _created.Add(canvas.gameObject);
            _input = canvas.gameObject.AddComponent<MenuInput>();
            _input.KeyboardBackEnabled = false;
            _screen = RunFailedScreen.Create(canvas.transform, _input, _failed);
        }

        [TearDown]
        public void TearDown()
        {
            _failed.Dispose();
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            Time.timeScale = 1f;
            CursorService.Reset();
            GameplayInputGate.Reset();
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;

        private ExpeditionService NewExpedition(out PlayerProfile profile)
        {
            profile = new PlayerProfile();
            profile.SafeLoadout = new InventorySnapshot
            {
                Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } },
                Backpack = new[] { new InventorySnapshot.Entry { Slot = 0, Item = new ItemInstance("consumable_bandage", 2).ToSnapshot() } }
            };
            var ammoByType = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            return new ExpeditionService(Resolve, t => ammoByType.TryGetValue(t, out var a) ? a : null, _ammoBalance);
        }

        private (GameObject go, PlayerLifeStateComponent life, HealthComponent health) Player(string name, PartyLifeRoster roster, Vector2 position)
        {
            var go = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = name, IsLocal = true, InputReader = new FakePlayerInputReader(), BalanceConfig = _balance, Position = position, LifeRoster = roster });
            _created.Add(go);
            return (go, go.GetComponent<PlayerLifeStateComponent>(), go.GetComponent<HealthComponent>());
        }

        private static void Kill(HealthComponent health) => health.TryApplyDamage(new DamageRequest(999999));

        /// <summary>A failed run with tracked figures, closed through the real Fail() transaction.</summary>
        private ExpeditionSummary FailedSummary()
        {
            var expedition = NewExpedition(out var profile);
            expedition.Start(profile, 31, Biome.Rustworks, 1);
            expedition.AddCarriedCoins(137);
            expedition.RecordRoomCleared();
            expedition.RecordRoomCleared();
            expedition.RecordRoomCleared();
            expedition.RecordEnemyDefeated(10);
            expedition.RecordEnemyDefeated(10);
            return expedition.Fail();
        }

        private UiControl Control(string id) => _screen.Controls.First(c => c.Id == id);

        // ---- content ----

        [Test]
        public void Show_PresentsTheClosedFailureSummary_WithTrackedFiguresOnly()
        {
            var summary = FailedSummary();
            Assert.IsFalse(_screen.IsShowing);
            Assert.IsTrue(_failed.Show(summary));
            Assert.IsTrue(_screen.IsShowing);
            Assert.AreEqual("RUN LOST", _screen.TitleText);
            var rows = _screen.RowTexts;
            var expected = new Dictionary<string, string>
            {
                ["DEPTH REACHED"] = "1", ["BIOME"] = "RUSTWORKS", ["ROOMS CLEARED"] = "3", ["ENEMIES DEFEATED"] = "2", ["ELITES DEFEATED"] = "0",
                ["BOSS DEFEATED"] = "NO", ["CARRIED COINS LOST"] = "137", ["ITEMS LOST"] = "2", ["XP EARNED (KEPT)"] = "20",
                // The personal-best depth is the one piece of progress a wipe leaves intact, so the run-lost screen
                // reports it too. It is a tracked figure from the summary, not an invented stat.
                ["DEEPEST DEPTH"] = "0"
            };
            foreach (var (label, value) in expected)
            {
                var row = rows.FirstOrDefault(r => r.label == label);
                Assert.IsNotNull(row.label, $"row '{label}' is drawn");
                Assert.AreEqual(value, row.value, label);
            }

            Assert.AreEqual(expected.Count, rows.Count, "no invented stat rows: " + string.Join(", ", rows.Select(r => r.label)));
            Assert.AreEqual(2, summary.LostItems.Count, "the pistol and the bandages went with the run");
            Assert.AreEqual(137, summary.CoinsLost);
        }

        [Test]
        public void Show_IgnoresASuccessfulRun_AndNeverShowsTwice()
        {
            var expedition = NewExpedition(out var profile);
            expedition.Start(profile, 5, Biome.RuinedMetro, 1);
            var success = expedition.Return();
            Assert.IsFalse(_failed.Show(success), "an extraction is not a loss");
            Assert.IsFalse(_screen.IsShowing);

            var failed = FailedSummary();
            Assert.IsTrue(_failed.Show(failed));
            Assert.IsFalse(_failed.Show(failed), "the same run never shows the screen twice");
            Assert.AreEqual(1, _failed.Shows);
            Assert.AreEqual(1, _input.Stack.Depth, "one layer, no duplicate screen");
        }

        // ---- input ownership ----

        [Test]
        public void WhileShowing_GameplayInputIsHeld_TheWorldIsPaused_AndThePointerCursorIsUp_AndFocusIsOnReturnToShelter()
        {
            Assert.AreEqual(0, GameplayInputGate.Holds);
            _failed.Show(FailedSummary());
            Assert.IsTrue(GameplayInputGate.IsHeld, "no click or key reaches the dead player's weapons");
            Assert.IsTrue(_world.IsPaused);
            Assert.AreSame(_screen.List, _input.Stack.Current);
            Assert.AreEqual("runfailed.shelter", _input.Stack.Focused.Id, "RETURN TO SHELTER has the focus first");
            CollectionAssert.AreEqual(new[] { "runfailed.shelter", "runfailed.menu" }, _screen.Controls.Select(c => c.Id));
            CollectionAssert.AreEqual(new[] { "RETURN TO SHELTER", "MAIN MENU" }, _screen.List.Items.Select(i => i.Label));
        }

        [Test]
        public void Mouse_ClickOnReturnToShelter_ResolvesOnce_AndHidesTheScreen()
        {
            _failed.Show(FailedSummary());
            var shelter = Control("runfailed.shelter");
            shelter.SimulateHover(true);
            Assert.IsTrue(shelter.IsHovered);
            shelter.SimulateClick();
            Assert.AreEqual(1, _shelter);
            Assert.AreEqual(0, _menu);
            Assert.AreEqual(RunFailedChoice.ReturnToShelter, _failed.Choice);
            Assert.IsFalse(_screen.IsShowing);
            Assert.AreEqual(0, _input.Stack.Depth, "the layer left the stack");
            shelter.SimulateClick();
            Control("runfailed.menu").SimulateClick();
            Assert.AreEqual(1, _shelter, "a second click changes nothing");
            Assert.AreEqual(0, _menu, "and the other exit is closed once one was taken");
        }

        [Test]
        public void KeyboardAndController_StepToMainMenu_ThroughTheFocusStack_AndActivate()
        {
            _failed.Show(FailedSummary());
            Assert.IsTrue(_input.Stack.Move(+1));
            Assert.AreEqual("runfailed.menu", _input.Stack.Focused.Id);
            Assert.IsTrue(_input.Stack.Current.ActivateFocused(), "Enter / pad A activates the focused exit");
            Assert.AreEqual(1, _menu);
            Assert.AreEqual(0, _shelter);
            Assert.AreEqual(RunFailedChoice.MainMenu, _failed.Choice);
            Assert.IsFalse(_screen.IsShowing);
        }

        [Test]
        public void Dispose_ReleasesTheInputHold_AndThePause()
        {
            _failed.Show(FailedSummary());
            Assert.IsTrue(GameplayInputGate.IsHeld);
            _failed.Dispose();
            Assert.IsFalse(GameplayInputGate.IsHeld);
            Assert.IsFalse(_world.IsPaused);
        }

        // ---- trigger semantics through the existing wipe → Fail() path ----

        private static bool IsConclusiveLoss(ExpeditionSummary summary, PartyLifeRoster roster, PlayerLifeStateComponent local) =>
            summary != null && !summary.IsSuccess && (roster.IsWiped || (local != null && local.IsDead));

        [Test]
        public void SoloDeath_FailsOnce_AndShowsTheScreenOnce()
        {
            var roster = new PartyLifeRoster();
            var (_, life, health) = Player("Solo", roster, Vector2.zero);
            var expedition = NewExpedition(out var profile);
            expedition.Start(profile, 9, Biome.OvergrownLabs, 1);
            using var binding = new PartyExpeditionBinding(expedition, roster, life);
            var ended = 0;
            expedition.ExpeditionEnded += s => { ended++; if (IsConclusiveLoss(s, roster, life)) _failed.Show(s); };

            Kill(health);
            Assert.IsTrue(life.IsDead, "the last standing player at 0 HP dies outright");
            Assert.IsFalse(expedition.IsExpeditionActive);
            Assert.AreEqual(1, ended, "Fail() exactly once");
            Assert.AreEqual(1, binding.WipeFailures);
            Assert.IsTrue(_screen.IsShowing);
            Assert.AreEqual(1, _failed.Shows);
            Assert.AreEqual("OVERGROWN LABS", _screen.RowTexts.First(r => r.label == "BIOME").value);
        }

        [Test]
        public void CoopDowned_WithALivingTeammate_ShowsNoFinalScreen_TheWipeDoes()
        {
            var roster = new PartyLifeRoster();
            var (_, a, healthA) = Player("A", roster, Vector2.zero);
            var (_, b, healthB) = Player("B", roster, new Vector2(4f, 0f));
            var expedition = NewExpedition(out var profile);
            expedition.Start(profile, 9, Biome.RuinedMetro, 2);
            using var binding = new PartyExpeditionBinding(expedition, roster, a);
            var ended = 0;
            expedition.ExpeditionEnded += s => { ended++; if (IsConclusiveLoss(s, roster, a)) _failed.Show(s); };

            Kill(healthA);
            Assert.AreEqual(PlayerLifeState.Downed, a.State, "downed, revivable");
            Assert.IsTrue(expedition.IsExpeditionActive, "the run continues while a teammate lives");
            Assert.IsFalse(_screen.IsShowing, "no final screen while merely downed");
            Assert.AreEqual(0, ended);

            Kill(healthB);
            Assert.IsTrue(roster.IsWiped);
            Assert.IsFalse(expedition.IsExpeditionActive);
            Assert.AreEqual(1, ended, "the wipe fails the run exactly once");
            Assert.IsTrue(_screen.IsShowing, "the authoritative wipe shows the final screen");
            Assert.AreEqual(1, _failed.Shows);

            a.Tick(25f);
            Assert.IsTrue(a.IsDead);
            Assert.AreEqual(1, ended, "the later bleed-out death never re-fails or re-shows");
            Assert.AreEqual(1, _failed.Shows);
        }
    }
}
