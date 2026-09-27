using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using RuinRail.Networking;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Manual ammo drops (32) against the auto-pickup attraction (30): the dropper's own reach holds its drop back until
    /// the reach has left it once — the stack stays on the ground with exactly the dropped amount — while a teammate
    /// collects it at once and naturally spawned ammo still flies in. Every shipped ammo type, whole and partial stacks,
    /// repeated cycles, a failed world drop, and the co-op authority path (a member's drop marks its host body).
    /// </summary>
    public sealed class DroppedAmmoRepickupTests
    {
        private readonly List<Object> _created = new();
        private GameContentCatalog _content;
        private ItemDefinitionRegistry _registry;
        private GroundLootRegistry _ground;
        private LootSpawner _spawner;
        private ItemDropService _drops;

        [SetUp]
        public void SetUp()
        {
            _content = GameContentCatalog.Load();
            _registry = _content.BuildRegistry();
            _ground = new GroundLootRegistry();
            var spawnerObject = new GameObject("LootSpawner");
            _created.Add(spawnerObject);
            _spawner = spawnerObject.AddComponent<LootSpawner>();
            _spawner.SetRegistry(_ground);
            _spawner.SetDefinitionResolver(id => _registry.TryGet(id, out var d) ? d : null);
            _drops = new ItemDropService(_spawner);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            foreach (var go in _ground.Tracked.ToArray()) if (go != null) Object.DestroyImmediate(go);
        }

        private (GameObject go, PlayerLootReceiver receiver, PlayerInventory inventory, PickupAttractor attractor) Player(string name, Vector2 at)
        {
            var go = new GameObject(name);
            _created.Add(go);
            go.transform.position = at;
            var inventory = PlayerInventory.FromRegistry(_registry, _content.AmmoBalance);
            var receiver = go.AddComponent<PlayerLootReceiver>();
            receiver.SetInventory(inventory);
            receiver.SetDropService(_drops);
            var attractor = go.AddComponent<PickupAttractor>();
            attractor.SetStats(new PlayerStats(_content.StatCaps));
            return (go, receiver, inventory, attractor);
        }

        private static void Step(PickupAttractor attractor, int steps = 60)
        {
            Physics2D.SyncTransforms();
            for (var i = 0; i < steps; i++) attractor.Step(0.02f);
        }

        private static void Move(GameObject go, Vector2 to)
        {
            go.transform.position = to;
            Physics2D.SyncTransforms();
        }

        private WorldItemPickup OnlyPickup() => _ground.Tracked.Where(g => g != null).Select(g => g.GetComponent<WorldItemPickup>()).Single(p => p != null && !p.IsConsumed && p.Item != null);

        private IEnumerable<AmmoItemDefinition> ShippedAmmo() => _content.Items.OfType<AmmoItemDefinition>().OrderBy(a => a.Id);

        [UnityTest]
        public IEnumerator EveryShippedAmmoType_DroppedWholeOrPartial_StaysOnTheGround_AndComesBackOnlyAfterTheDropperLeftIt()
        {
            var types = ShippedAmmo().ToList();
            Assert.AreEqual(4, types.Count, "the four normal ammo categories");
            var x = 0f;
            foreach (var ammo in types)
            {
                foreach (var partial in new[] { false, true })
                {
                    x += 40f;
                    var origin = new Vector2(x, 0f);
                    var (go, receiver, inventory, attractor) = Player("Dropper", origin);
                    var stack = ammo.MaxStack > 1 ? System.Math.Min(ammo.MaxStack, 30) : 1;
                    Assert.AreEqual(stack, inventory.Add(ammo.AmmoType, stack));
                    var carried = inventory.BackpackSlots.First(s => s != null && s.DefinitionId == ammo.Id);
                    var amount = partial ? stack / 3 : stack;
                    var label = $"{ammo.Id} {(partial ? "partial" : "whole")}";

                    var result = partial ? receiver.TryDrop(carried.InstanceId, amount) : receiver.TryDrop(carried.InstanceId);
                    Assert.IsTrue(result.Success, label);
                    var pickup = result.Pickup;
                    Assert.AreEqual(amount, pickup.Item.Quantity, label + ": the ground stack holds exactly the dropped amount");
                    Assert.AreEqual(stack - amount, inventory.Get(ammo.AmmoType), label + ": exactly that much left the reserve");
                    Assert.AreSame(go, pickup.DroppedBy, label + ": the drop remembers its dropper");
                    Assert.IsTrue(pickup.IsAttractionEligible, label + ": still an ordinary attractable ammo pickup");

                    // Standing on it for a while: the dropper's reach does not pull it back.
                    var droppedAt = (Vector2)pickup.transform.position;
                    Step(attractor, 120);
                    yield return null;
                    Assert.IsFalse(pickup == null || pickup.IsConsumed, label + ": still on the ground");
                    Assert.AreEqual(droppedAt, (Vector2)pickup.transform.position, label + ": not dragged onto the dropper");
                    Assert.AreEqual(stack - amount, inventory.Get(ammo.AmmoType), label + ": nothing boomeranged back");
                    Assert.AreEqual(1, attractor.HeldBackCount);

                    // Walking out of reach releases the hold; walking back collects it through the normal pickup rules.
                    Move(go, origin + new Vector2(attractor.Radius + 3f, 0f));
                    Step(attractor, 2);
                    Assert.IsNull(pickup.DroppedBy, label + ": leaving the drop's reach released it");
                    Assert.AreEqual(0, attractor.HeldBackCount);
                    Move(go, origin);
                    Step(attractor, 120);
                    yield return null;
                    Assert.AreEqual(stack, inventory.Get(ammo.AmmoType), label + ": picked up again later, exactly the dropped amount back");
                    Assert.IsTrue(pickup == null || pickup.IsConsumed);
                }
            }
        }

        [UnityTest]
        public IEnumerator Teammate_CollectsAnotherPlayersDropAtOnce_AndNaturalAmmoStillFliesIn()
        {
            var light = ShippedAmmo().First(a => a.AmmoType == AmmoType.Light);
            var dropper = Player("Dropper", Vector2.zero);
            var mate = Player("Mate", new Vector2(0.6f, 0f));
            Assert.AreEqual(60, dropper.inventory.Add(AmmoType.Light, 60));
            var id = dropper.inventory.BackpackSlots.First(s => s != null).InstanceId;
            var pickup = dropper.receiver.TryDrop(id, 25).Pickup;
            Assert.IsNotNull(pickup);

            // Both reaches cover the pickup: the dropper holds it back, the teammate takes it on the next steps.
            Physics2D.SyncTransforms();
            for (var i = 0; i < 60; i++) { dropper.attractor.Step(0.02f); mate.attractor.Step(0.02f); }
            yield return null;
            Assert.AreEqual(25, mate.inventory.Get(AmmoType.Light), "the teammate collected the whole dropped stack");
            Assert.AreEqual(35, dropper.inventory.Get(AmmoType.Light), "the dropper kept exactly the rest");
            Assert.AreEqual(60, mate.inventory.Get(AmmoType.Light) + dropper.inventory.Get(AmmoType.Light), "nothing duplicated or lost");

            // Naturally spawned ammo at the dropper's feet (no dropper): collected at once, exactly as before. This is
            // also the control: the drop spot is inside the reach, so without the hold-back the drop would boomerang.
            var natural = _spawner.CreateItemPickup(dropper.go.transform.position);
            natural.Hold(new ItemInstance(light.Id, 12), ItemCategory.Ammo);
            Assert.IsNull(natural.DroppedBy);
            Step(dropper.attractor, 60);
            yield return null;
            Assert.AreEqual(47, dropper.inventory.Get(AmmoType.Light), "natural ammo still auto-picks immediately");

            // Coin piles are never player drops: nothing is ever held back.
            var coins = _spawner.CreateCoinPickup(dropper.go.transform.position);
            coins.SetAmount(7);
            Assert.IsFalse(coins.IsHeldBackFrom(dropper.go));
            Step(dropper.attractor, 60);
            yield return null;
            Assert.AreEqual(7, dropper.receiver.CarriedCoins);
        }

        [UnityTest]
        public IEnumerator RepeatedDropsAndPickups_NeverDuplicateOrLose_AndAFailedWorldDropKeepsTheAmmo()
        {
            var (go, receiver, inventory, attractor) = Player("Cycler", Vector2.zero);
            Assert.AreEqual(90, inventory.Add(AmmoType.Medium, 90));
            for (var cycle = 0; cycle < 6; cycle++)
            {
                var stack = inventory.BackpackSlots.First(s => s != null && s.DefinitionId == ShippedAmmo().First(a => a.AmmoType == AmmoType.Medium).Id);
                var amount = 5 + cycle * 3;
                Assert.IsTrue(receiver.TryDrop(stack.InstanceId, amount).Success);
                Step(attractor, 60);
                var onGround = _ground.Tracked.Where(g => g != null).Select(g => g.GetComponent<WorldItemPickup>()).Where(p => p != null && !p.IsConsumed && p.Item != null).Sum(p => p.Item.Quantity);
                Assert.AreEqual(90, inventory.Get(AmmoType.Medium) + onGround, $"cycle {cycle}: carried + ground is conserved while held back");
                Move(go, new Vector2(10f, 0f));
                Step(attractor, 2);
                Move(go, Vector2.zero);
                Step(attractor, 120);
                yield return null;
                Assert.AreEqual(90, inventory.Get(AmmoType.Medium), $"cycle {cycle}: all of it came back, once");
            }

            // A world that refuses the drop (the pickup it hands out is occupied): the ammo stays exactly as it was.
            var refusing = new ItemDropService(new OccupiedFactory(_created));
            receiver.SetDropService(refusing);
            var carried = inventory.BackpackSlots.First(s => s != null);
            var result = receiver.TryDrop(carried.InstanceId, 10);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(90, inventory.Get(AmmoType.Medium), "a failed world drop leaves the reserve unchanged");
        }

        private sealed class OccupiedFactory : IWorldPickupFactory
        {
            private readonly List<Object> _created;
            public OccupiedFactory(List<Object> created) => _created = created;

            public WorldItemPickup CreateItemPickup(Vector2 position, Transform parent = null)
            {
                var go = new GameObject("Occupied");
                _created.Add(go);
                var pickup = go.AddComponent<WorldItemPickup>();
                pickup.Hold(new ItemInstance("ammo_light", 1), ItemCategory.Ammo);
                return pickup;
            }

            public ItemCategory? CategoryOf(ItemInstance item) => ItemCategory.Ammo;
        }

        [Test]
        public void CoopAuthority_AMembersDrop_MarksTheMembersHostBody_SoOnlyThatBodyIsHeldBack()
        {
            var member = Player("MemberHostCopy", new Vector2(200f, 0f));
            var host = Player("HostPlayer", new Vector2(200.6f, 0f));
            Assert.AreEqual(40, member.inventory.Add(AmmoType.Shells, 40));
            var authority = new LootAuthorityService(LocalAuthorityContext.Instance);
            authority.SetDropService(_drops);
            authority.RegisterParticipant(new LootParticipant(7, "member", member.receiver.Backpack, member.receiver.Wallet, member.go, member.receiver.CarriedContainers));
            var id = member.inventory.BackpackSlots.First(s => s != null).InstanceId;
            var verdict = authority.RequestDrop("tx-drop", 7, null, id, 15, member.go.transform.position);
            Assert.AreEqual(LootVerdict.Accepted, verdict.Verdict);
            var pickup = OnlyPickup();
            Assert.AreSame(member.go, pickup.DroppedBy, "the host resolves the member's drop against the member's own body");
            Assert.IsTrue(pickup.IsHeldBackFrom(member.go));
            Assert.IsFalse(pickup.IsHeldBackFrom(host.go), "every other player may take it at once");
            Assert.AreEqual(25, member.inventory.Get(AmmoType.Shells));

            Step(member.attractor, 60);
            Assert.IsFalse(pickup.IsConsumed, "the member's own body on the host does not pull it back");
            Step(host.attractor, 60);
            Assert.AreEqual(15, host.inventory.Get(AmmoType.Shells), "the teammate's pickup through the ordinary path");
            Assert.AreEqual(40, host.inventory.Get(AmmoType.Shells) + member.inventory.Get(AmmoType.Shells));
        }
    }

    /// <summary>
    /// The real path in a live run (boot → Shelter → dungeon): the Tab inventory's DROP on the starter ammo stack puts
    /// exactly that stack on the ground at the player's feet, the running game's own attraction (real physics steps)
    /// leaves it there, and walking away and back picks it up again through the ordinary pull.
    /// </summary>
    public sealed class DroppedAmmoLiveRunTests
    {
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ruinrail_ammo_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_saveDir);
            RuinRail.Core.Input.GameplayInputGate.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (root.GetComponents<Component>().Any(c => c != null && (c.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools"))) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            NetworkPlayerObject.VisualComposer = null;
            RuinRail.Core.Input.GameplayInputGate.Reset();
            RuinRail.UI.Theme.CursorService.Reset();
            try { System.IO.Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (_app.ComposedScene != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time");
                yield return null;
            }
        }

        private static IEnumerator Teleport(ExpeditionScene run, Vector2 position)
        {
            var body = run.Rig.Player.GetComponent<Rigidbody2D>();
            run.Rig.Player.transform.position = position;
            body.position = position;
            for (var i = 0; i < 4; i++) yield return new WaitForFixedUpdate();
        }

        [UnityTest]
        public IEnumerator LiveRun_InventoryDropOfAmmo_StaysOnTheGround_ThenComesBackAfterWalkingAwayAndBack()
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(11);
            UnityEngine.SceneManagement.SceneManager.LoadScene(RuinRail.Core.SceneNames.MainMenu);
            yield return WaitComposed(RuinRail.Core.SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(RuinRail.Core.SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Ammo Dropper");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(RuinRail.Core.SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var inventory = run.Expedition.State.Inventory;
            var player = run.Rig.Player;
            var start = run.Rooms.Values.First(r => r.State.RoomType == RuinRail.Dungeon.Rooms.RoomType.Start);
            var spot = (Vector2)start.InteriorWorldBounds.center;
            yield return Teleport(run, spot);
            var slot = inventory.BackpackSlots.ToList().FindIndex(i => i != null && _app.Content.Items.OfType<AmmoItemDefinition>().Any(a => a.Id == i.DefinitionId));
            Assert.GreaterOrEqual(slot, 0, "the starter kit carries an ammo stack");
            var stack = inventory.BackpackSlots[slot];
            var type = _app.Content.Items.OfType<AmmoItemDefinition>().First(a => a.Id == stack.DefinitionId).AmmoType;
            var reserveBefore = inventory.Get(type);
            var dropped = stack.Quantity;
            var groundBefore = run.GroundLoot.Tracked.Count(g => g != null);

            // The real window: Tab inventory, cursor on the ammo, the DROP button.
            run.Inventory.Open();
            yield return null;
            run.Inventory.SetCursor(new RuinRail.UI.Inventory.InventorySlotRef(RuinRail.UI.Inventory.InventorySlotKind.Backpack, slot));
            run.InventoryView.Buttons[RuinRail.UI.Inventory.InventoryView.DropFocusId].SimulateClick();
            run.Inventory.Close();
            yield return null;
            Assert.AreEqual(reserveBefore - dropped, inventory.Get(type), "exactly the stack left the reserve");
            var pickup = run.GroundLoot.Tracked.Where(g => g != null).Select(g => g.GetComponent<WorldItemPickup>()).Single(p => p != null && p.Item != null && p.Item.DefinitionId == stack.DefinitionId && !p.IsConsumed);
            Assert.AreEqual(dropped, pickup.Item.Quantity);
            Assert.AreEqual(groundBefore + 1, run.GroundLoot.Tracked.Count(g => g != null));

            // The running game's own attraction, one real second of physics while standing on it.
            var deadline = Time.time + 1f;
            while (Time.time < deadline) yield return new WaitForFixedUpdate();
            Assert.IsFalse(pickup == null || pickup.IsConsumed, "the drop is still on the ground");
            Assert.AreEqual(reserveBefore - dropped, inventory.Get(type), "it did not boomerang into the reserve");
            Assert.Less(Vector2.Distance(pickup.transform.position, player.transform.position), 0.6f, "at the dropper's feet, visibly available");

            // Walk out of reach and back: the ordinary pull collects it, exactly once.
            var attractor = player.GetComponent<PickupAttractor>();
            yield return Teleport(run, spot + new Vector2(attractor.Radius + 3f, 0f));
            yield return Teleport(run, spot);
            deadline = Time.time + 2f;
            while (Time.time < deadline && inventory.Get(type) != reserveBefore) yield return new WaitForFixedUpdate();
            Assert.AreEqual(reserveBefore, inventory.Get(type), "picked up again later, the full dropped amount");
            Assert.IsTrue(pickup == null || pickup.IsConsumed);
        }
    }

    /// <summary>The nearby-item prompt draws the item name in its rarity colour (RarityStyle), in a live run. Captures: TestResults/RegressionProof/prompt_rarity_*.png.</summary>
    public sealed class PickupPromptRarityLiveTests
    {
        private string _saveDir;
        private GameApp _app;

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (root.GetComponents<Component>().Any(c => c != null && (c.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools"))) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            NetworkPlayerObject.VisualComposer = null;
            try { System.IO.Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        [UnityTest]
        public IEnumerator LiveRun_PromptItemNameUsesTheRarityColour()
        {
            _saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ruinrail_prompt_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_saveDir);
            System.IO.Directory.CreateDirectory("TestResults/RegressionProof");
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(11);
            UnityEngine.SceneManagement.SceneManager.LoadScene(RuinRail.Core.SceneNames.MainMenu);
            var deadline = Time.realtimeSinceStartup + 30f;
            while (_app.ComposedScene != RuinRail.Core.SceneNames.MainMenu) { Assert.Less(Time.realtimeSinceStartup, deadline); yield return null; }
            _app.Menu.Play();
            while (_app.ComposedScene != RuinRail.Core.SceneNames.Base) { Assert.Less(Time.realtimeSinceStartup, deadline + 30f); yield return null; }
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Prompt Reader");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            while (_app.ComposedScene != RuinRail.Core.SceneNames.Dungeon) { Assert.Less(Time.realtimeSinceStartup, deadline + 60f); yield return null; }
            for (var i = 0; i < 12; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var player = run.Rig.Player;
            var start = run.Rooms.Values.First(r => r.State.RoomType == RuinRail.Dungeon.Rooms.RoomType.Start);
            player.transform.position = start.InteriorWorldBounds.center;
            player.GetComponent<Rigidbody2D>().position = start.InteriorWorldBounds.center;
            for (var i = 0; i < 4; i++) yield return new WaitForFixedUpdate();
            var spawnerHost = new GameObject("PromptLoot");
            var spawner = run.CreateLootSpawnerFor(spawnerHost);
            var weapon = _app.Content.Items.OfType<WeaponDefinition>().OrderByDescending(w => w.DisplayName.Length).First();
            foreach (Rarity rarity in System.Enum.GetValues(typeof(Rarity)))
            {
                var pickup = spawner.CreateItemPickup((Vector2)player.transform.position + Vector2.right * 0.6f);
                pickup.Hold(new ItemInstance(weapon.Id, 1, rarity), ItemCategory.Weapon);
                pickup.SetDisplayName(weapon.DisplayName);
                for (var i = 0; i < 4; i++) yield return new WaitForFixedUpdate();
                yield return null;
                StringAssert.Contains("TAKE " + weapon.DisplayName.ToUpperInvariant(), run.CurrentInteractionPrompt, "the prompt text itself is unchanged");
                var hex = ColorUtility.ToHtmlStringRGB(RuinRail.UI.Navigation.RarityStyle.For(rarity).TextColor);
                var shown = Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None).FirstOrDefault(t => t.text.Contains("TAKE <color="));
                Assert.IsNotNull(shown, rarity + ": the label draws the name in colour");
                StringAssert.Contains("<color=#" + hex + ">" + weapon.DisplayName.ToUpperInvariant(), shown.text, rarity + ": the canonical rarity colour");
                StringAssert.StartsWith("[", shown.text);
                LiveDungeonCapture.Capture("TestResults/RegressionProof", "prompt_rarity_" + rarity, run.Camera.Camera, run.Camera.Config.PixelsPerUnit, includeUi: true);
                Object.DestroyImmediate(pickup.gameObject);
                yield return null;
            }
        }
    }
}
