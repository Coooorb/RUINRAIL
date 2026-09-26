using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The non-combat room regression matrix (57/58/60): every non-combat / special room type of the shipped 63-room
    /// pool — each of the six Event rooms under every one of the five seeded event kinds, the Medical rooms, the Loot,
    /// Treasure, Merchant and Start rooms and the Boss rooms' cache + transit — is instantiated from its real prefab,
    /// composed by the real composer with the shipped services, and driven through the end-to-end interaction
    /// contract: object present with its final art, prompt text, the Interact press reaching the event, the outcome
    /// (reward / cost / wave / heal) exactly once, the used state, idempotency, and the room able to complete. The
    /// result is <c>TestResults/DepthSettingsDescriptionsNonCombatProof/noncombat_room_matrix.csv</c>.
    /// </summary>
    public sealed class NonCombatRoomMatrixTests
    {
        public const string Folder = "TestResults/DepthSettingsDescriptionsNonCombatProof";
        public const string MatrixPath = Folder + "/noncombat_room_matrix.csv";
        private const int Depth = 2;

        private readonly List<UnityEngine.Object> _created = new();
        private readonly List<string[]> _rows = new();
        private GameContentCatalog _content;
        private ItemDefinitionRegistry _registry;
        private DungeonRuntimeServices _services;
        private PartyLifeRoster _roster;
        private ExpeditionService _expedition;

        private sealed class FakeBossSpawner : IBossSpawner
        {
            private readonly List<UnityEngine.Object> _created;
            public FakeBossSpawner(List<UnityEngine.Object> created) { _created = created; }

            public BossEncounter Spawn(in BossSpawnRequest request)
            {
                var root = new GameObject("BossEncounter");
                root.transform.SetParent(request.Parent, false);
                var encounter = root.AddComponent<BossEncounter>();
                var go = new GameObject("Boss");
                go.transform.SetParent(root.transform, false);
                go.transform.position = request.Position;
                go.AddComponent<CircleCollider2D>().radius = 0.7f;
                go.AddComponent<Rigidbody2D>().gravityScale = 0f;
                go.AddComponent<HealthComponent>();
                var boss = go.AddComponent<BossController>();
                var definition = ScriptableObject.CreateInstance<BossDefinition>();
                _created.Add(definition);
                Set(definition, "_id", "boss_matrix");
                Set(definition, "_baseHealth", 300);
                Set(definition, "_baseXp", 650);
                boss.SetDefinition(definition);
                encounter.Bind(boss);
                return encounter;
            }
        }

        private static void Set(object target, string field, object value)
        {
            var type = target.GetType();
            System.Reflection.FieldInfo info = null;
            while (type != null && info == null) { info = type.GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); type = type.BaseType; }
            info.SetValue(target, value);
        }

        [SetUp]
        public void SetUp()
        {
            Directory.CreateDirectory(Folder);
            _content = GameContentCatalog.Load();
            _registry = _content.BuildRegistry();
            WorldObjectArt.Resolver = _content.WorldSpriteFor;
            RoomDoorLock.SkinResolver = biome => { var skin = _content.DoorSkinFor(biome); return skin != null ? new DoorSkinSprites(skin.Open, skin.Locked) : default; };
            DamageAuthority.LocalIsAuthoritative = true;
            _roster = new PartyLifeRoster();
            _expedition = new ExpeditionService(id => _registry.TryGet(id, out var d) ? d : null, t => _registry.Definitions.OfType<AmmoItemDefinition>().FirstOrDefault(a => a.AmmoType == t), _content.AmmoBalance);
            var state = _expedition.Start(new PlayerProfile(), 5, Biome.RuinedMetro);
            _services = new DungeonRuntimeServices
            {
                LootCatalog = _content.Loot,
                GroundLoot = new GroundLootRegistry(),
                ResolveDefinition = id => _registry.TryGet(id, out var d) ? d : null,
                ItemCatalog = _content.Items,
                Prices = new PriceService(_content.Economy),
                MerchantConfig = _content.Merchant,
                CarriedWallet = state.CarriedWallet,
                EventConfig = _content.Events,
                BossSpawner = new FakeBossSpawner(_created),
                Expedition = _expedition,
                ReviveAuthority = new PartyReviveAuthority(_roster)
            };
            _rows.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            foreach (var enemy in UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (enemy != null) UnityEngine.Object.DestroyImmediate(enemy.gameObject);
            foreach (var go in _services.GroundLoot.Tracked.ToArray()) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _created.Clear();
            WorldObjectArt.Resolver = null;
            RoomDoorLock.SkinResolver = null;
        }

        private static IEnumerable<RoomDefinition> ShippedRooms() => BiomeRoomPools.Build(GameContentCatalog.Load().Rooms).Pools.Values.SelectMany(p => p.Rooms).Where(r => r != null && !r.HasTag("test_fixture")).Distinct().OrderBy(r => r.Id, StringComparer.Ordinal);

        private (RoomRuntime runtime, RoomContentBinding binding) Compose(RoomDefinition definition, int nodeId, int runSeed, Vector2 offset)
        {
            var instance = UnityEngine.Object.Instantiate(definition.Prefab, new Vector3(offset.x, offset.y, 0f), Quaternion.identity);
            instance.name = $"Room_{definition.Id}";
            _created.Add(instance);
            var root = instance.GetComponent<RoomRoot>();
            Assert.IsNotNull(root, definition.Id + ": prefab has a RoomRoot");
            var context = new DungeonRuntimeContext(runSeed, Depth, 1, _content.Enemies, new DefaultEnemySpawner(_content.Stagger), _content.DepthScaling);
            var runtime = instance.AddComponent<RoomRuntime>();
            runtime.Configure(root, nodeId, Depth, 1);
            runtime.SetScaling(context.Scaling);
            runtime.SetSpawner(context.Spawner);
            var binding = RoomCategoryComposer.Compose(runtime, context, _services);
            RoomEntryTrigger.Attach(runtime);
            return (runtime, binding);
        }

        private GameObject CreatePlayer(Vector2 position, int coins, int damage = 0)
        {
            var player = new GameObject("MatrixPlayer");
            _created.Add(player);
            player.transform.position = position;
            player.AddComponent<CircleCollider2D>().isTrigger = true;
            player.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var health = player.AddComponent<HealthComponent>();
            health.SetMaxHealth(100);
            if (damage > 0) health.TryApplyDamage(new DamageRequest(damage));
            var inventory = PlayerInventory.FromRegistry(_registry, _content.AmmoBalance);
            var receiver = player.AddComponent<PlayerLootReceiver>();
            receiver.SetInventory(inventory);
            var wallet = _services.CarriedWallet;
            var delta = coins - wallet.Balance;
            if (delta > 0) wallet.Credit(delta, "matrix"); else if (delta < 0) wallet.Debit(-delta, "matrix");
            receiver.SetWallet(wallet);
            return player;
        }

        private static string Prompt(IInteractable interactable, GameObject player) => interactable is IInteractionPrompt p ? p.PromptFor(player) : string.Empty;

        private static bool HasVisibleArt(Component c) => c != null && c.GetComponentInChildren<WorldObjectVisual>() is { } v && v.IsVisible;

        private void Record(RoomDefinition room, string category, string interaction, string prompt, bool uiRequired, bool objectPresent, bool interactionOk, bool outcomeOk, bool idempotent, string result) =>
            _rows.Add(new[] { room.Id, RoomDisplayNames.NameOf(room), RoomDisplayNames.BiomeName(room.Biome), category, interaction, prompt, uiRequired ? "yes" : "no", objectPresent ? "yes" : "no", interactionOk ? "yes" : "no", outcomeOk ? "yes" : "no", idempotent ? "yes" : "no", result });

        private static int SeedForKind(RoomDefinition room, int nodeId, DungeonEventKind kind)
        {
            for (var seed = 1; seed < 5000; seed++)
                if (RoomCategoryComposer.ResolveEventKind(room.Tags, seed, Depth, nodeId) == kind) return seed;
            throw new InvalidOperationException($"no seed under 5000 yields {kind} for {room.Id}");
        }

        [UnityTest]
        public IEnumerator EveryNonCombatRoomType_IsFunctionalEndToEnd_AndTheMatrixIsWritten()
        {
            var rooms = ShippedRooms().ToList();
            Assert.AreEqual(63, rooms.Count, "the shipped pool");
            var nonCombat = rooms.Where(r => r.RoomType != RoomType.Combat).ToList();
            Assert.AreEqual(30, nonCombat.Count, "6 Start + 3 Loot + 3 Treasure + 3 Merchant + 6 Event + 3 Medical + 6 Boss");
            var nodeId = 100;
            var offset = 0f;
            var failures = new List<string>();
            foreach (var room in nonCombat)
            {
                var kinds = room.RoomType == RoomType.Event ? RoomCategoryComposer.RandomEventKinds.Select(k => (DungeonEventKind?)k).ToArray() : new DungeonEventKind?[] { null };
                foreach (var kind in kinds)
                {
                    nodeId++;
                    offset += 60f;
                    var seed = kind.HasValue ? SeedForKind(room, nodeId, kind.Value) : 11;
                    string failure = null;
                    var step = Drive(room, kind, nodeId, seed, new Vector2(offset, 0f), f => failure = f);
                    while (step.MoveNext()) yield return step.Current;
                    if (failure != null) failures.Add($"{room.Id}{(kind.HasValue ? "/" + kind : string.Empty)}: {failure}");
                }
            }

            WriteMatrix();
            Assert.AreEqual(54, _rows.Count, "30 rooms, the 6 Event rooms under all 5 kinds each (6 x 5 + 24)");
            Assert.IsEmpty(failures, string.Join("\n", failures));
            Assert.IsTrue(_rows.All(r => r[11] == "PASS"), "no discovered non-combat room type is left NOT IMPLEMENTED or failing");
        }

        private IEnumerator Drive(RoomDefinition room, DungeonEventKind? kind, int nodeId, int seed, Vector2 offset, Action<string> fail)
        {
            var (runtime, binding) = Compose(room, nodeId, seed, offset);
            var spawn = runtime.Root.GetMarkers(RoomMarkerRole.PlayerSpawn).FirstOrDefault();
            var centre = runtime.InteriorWorldBounds.center;
            var category = room.RoomType.ToString() + (kind.HasValue ? "/" + kind : string.Empty);
            string result = "PASS";
            void Fail(string why) { result = "FAIL: " + why; fail(why); }
            if (binding.Skipped.Count > 0) Fail("composer skipped " + string.Join("|", binding.Skipped));
            yield return null;

            switch (room.RoomType)
            {
                case RoomType.Start:
                {
                    var player = CreatePlayer(centre, 0);
                    var ok = spawn != null && runtime.Root.GetMarkers(RoomMarkerRole.PlayerSpawn).All(m => m.IsInsideRoom(runtime.Root.Size));
                    if (!ok) Fail("no PlayerSpawn marker");
                    if (!runtime.NotifyPlayerEntered(player) || runtime.Lifecycle != RoomLifecycleState.Cleared) Fail("start room did not clear on entry");
                    if (UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Length > 0) Fail("enemies in the start room");
                    Record(room, category, "none (safe spawn)", "", false, ok, true, runtime.Lifecycle == RoomLifecycleState.Cleared, true, result);
                    break;
                }
                case RoomType.Loot:
                case RoomType.Treasure:
                {
                    var player = CreatePlayer(centre, 0);
                    runtime.NotifyPlayerEntered(player);
                    var chests = binding.Chests;
                    if (chests.Count == 0) Fail("no chests");
                    var expectedKind = room.RoomType == RoomType.Loot ? LootSourceKind.EquipmentChest : LootSourceKind.TreasureChest;
                    if (chests.Any(c => c.Kind != expectedKind)) Fail("wrong loot source");
                    var art = chests.All(HasVisibleArt);
                    if (!art) Fail("chest without visible art");
                    var prompt = Prompt(chests[0], player);
                    if (prompt != "OPEN CHEST") Fail("prompt '" + prompt + "'");
                    var before = _services.GroundLoot.Count;
                    var opened = chests[0].Interact(player);
                    yield return null;
                    var delivered = _services.GroundLoot.Count > before;
                    if (!opened || !delivered) Fail("chest did not deliver loot");
                    var again = chests[0].Interact(player);
                    var afterCount = _services.GroundLoot.Count;
                    yield return null;
                    var idempotent = !again && _services.GroundLoot.Count == afterCount && chests[0].IsOpened && Prompt(chests[0], player) == string.Empty;
                    if (!idempotent) Fail("chest opened twice");
                    if (!runtime.State.IsResolved("chest:0")) Fail("chest not recorded as resolved");
                    if (chests[0].GetComponentInChildren<WorldObjectVisual>().Key != WorldObjectArt.SupplyChestOpen) Fail("chest art did not change to opened");
                    foreach (var chest in chests.Skip(1)) { chest.Interact(player); yield return null; }
                    Record(room, category, $"{chests.Count} chest(s), Interact", prompt, false, chests.Count > 0 && art, opened, delivered, idempotent, result);
                    break;
                }
                case RoomType.Merchant:
                {
                    var player = CreatePlayer(centre, 500);
                    runtime.NotifyPlayerEntered(player);
                    var merchant = binding.Merchant;
                    if (merchant == null) { Fail("no merchant object"); Record(room, category, "Merchant, Interact -> trade window", "", true, false, false, false, false, result); break; }
                    var art = HasVisibleArt(merchant);
                    if (!art) Fail("merchant without visible art");
                    var prompt = Prompt(merchant, player);
                    if (prompt != "TRADE WITH MERCHANT") Fail("prompt '" + prompt + "'");
                    var opens = 0;
                    merchant.Opened += (_, _) => opens++;
                    var first = merchant.Interact(player);
                    var second = merchant.Interact(player);
                    var offers = merchant.Merchant.Offers.Count;
                    if (!first || !second || opens != 2) Fail("open/reopen did not reach the trade seam");
                    if (offers == 0) Fail("merchant has no stock");
                    Record(room, category, "Merchant, Interact -> Opened (trade window)", prompt, true, art, first && opens >= 1, offers > 0, second && opens == 2, result);
                    break;
                }
                case RoomType.MedicalRecovery:
                {
                    var station = binding.EventInstance as MedicalStationEvent;
                    if (station == null || binding.Event == null) { Fail("no medical station"); Record(room, category, "Medical Station", "", false, false, false, false, false, result); break; }
                    var art = HasVisibleArt(binding.Event);
                    if (!art) Fail("station without visible art");
                    var full = CreatePlayer(centre, 2000);
                    runtime.NotifyPlayerEntered(full);
                    var fullPrompt = Prompt(binding.Event, full);
                    if (!fullPrompt.Contains("HP FULL")) Fail("full-HP prompt lacks the reason: '" + fullPrompt + "'");
                    var refusals = 0;
                    binding.Event.Refused += (_, _, _) => refusals++;
                    var coinsBefore = _services.CarriedWallet.Balance;
                    if (binding.Event.Interact(full) || refusals != 1 || _services.CarriedWallet.Balance != coinsBefore) Fail("a full player was charged or not refused");
                    UnityEngine.Object.DestroyImmediate(full);
                    var hurt = CreatePlayer(centre, 2000, damage: 40);
                    var prompt = Prompt(binding.Event, hurt);
                    if (!prompt.StartsWith("USE MEDICAL STATION") || !prompt.Contains($"({station.HealCost} COINS)") || prompt.Contains("—")) Fail("prompt '" + prompt + "'");
                    coinsBefore = _services.CarriedWallet.Balance;
                    var healed = binding.Event.Interact(hurt);
                    var health = hurt.GetComponent<HealthComponent>();
                    var outcome = healed && health.CurrentHealth == health.MaxHealth && _services.CarriedWallet.Balance == coinsBefore - station.HealCost;
                    if (!outcome) Fail("heal did not restore to full for exactly the cost");
                    health.TryApplyDamage(new DamageRequest(30));
                    var usedPrompt = Prompt(binding.Event, hurt);
                    var againCoins = _services.CarriedWallet.Balance;
                    var again = binding.Event.Interact(hurt);
                    var idempotent = !again && _services.CarriedWallet.Balance == againCoins && usedPrompt.Contains("USED");
                    if (!idempotent) Fail("second heal for the same participant was not refused ('" + usedPrompt + "')");
                    Record(room, category, "Medical Station, Interact -> paid heal", prompt, false, art, healed, outcome, idempotent, result);
                    break;
                }
                case RoomType.Boss:
                {
                    var player = CreatePlayer(centre, 0);
                    var transit = binding.Transit;
                    if (transit == null || binding.Boss == null) { Fail("boss room without transit/boss"); Record(room, category, "Boss Cache + Transit", "", false, false, false, false, false, result); break; }
                    // 46: the boss's death spawns the Boss Cache — nothing to open (or find) before it falls.
                    if (binding.BossCache != null) Fail("a Boss Cache existed before the boss fell");
                    if (transit.CanInteract(player) || Prompt(transit, player) != string.Empty) Fail("transit usable before the boss fell");
                    runtime.NotifyPlayerEntered(player);
                    yield return null;
                    binding.Boss.Boss.Health.TryApplyDamage(new DamageRequest(99999));
                    yield return null;
                    var cache = binding.BossCache;
                    if (cache == null) { Fail("the boss's death spawned no Boss Cache"); Record(room, category, "Boss Cache + Transit", "", false, false, false, false, false, result); break; }
                    var art = HasVisibleArt(cache) && HasVisibleArt(transit);
                    if (!art) Fail("cache/transit without visible art");
                    var unlocked = !cache.IsLocked && transit.IsActivated && runtime.Lifecycle == RoomLifecycleState.Cleared;
                    if (!unlocked) Fail("defeat did not unlock the cache / activate the transit");
                    var prompt = Prompt(cache, player);
                    if (prompt != "OPEN BOSS CACHE") Fail("cache prompt '" + prompt + "'");
                    var before = _services.GroundLoot.Count;
                    var opened = cache.Interact(player);
                    yield return null;
                    var delivered = _services.GroundLoot.Count > before;
                    var idempotent = !cache.Interact(player) && cache.IsOpened && runtime.State.IsResolved("boss_cache");
                    var transitPrompt = Prompt(transit, player);
                    var boarded = transit.CanInteract(player) && transitPrompt == "BOARD TRANSIT" && transit.Interact(player) && transit.IsBoarded && !transit.Interact(player);
                    if (!opened || !delivered) Fail("boss cache delivered nothing");
                    if (!idempotent) Fail("boss cache opened twice");
                    if (!boarded) Fail("transit not boardable once ('" + transitPrompt + "')");
                    if (_expedition.Transit == null || _expedition.Transit.State != TransitDecisionState.Open) Fail("transit decision not open");
                    Record(room, category, "Boss Cache (Interact) + Transit (Interact -> decision)", prompt + " / " + transitPrompt, true, art, opened && boarded, delivered && unlocked, idempotent, result);
                    // The decision is consumed here so the next boss room starts from a fresh one.
                    _expedition.ChooseTransit(TransitChoice.DescendDeeper);
                    break;
                }
                case RoomType.Event:
                {
                    var step = DriveEvent(room, kind.Value, runtime, binding, centre, category, Fail, () => result);
                    while (step.MoveNext()) yield return step.Current;
                    break;
                }
            }
        }

        private IEnumerator DriveEvent(RoomDefinition room, DungeonEventKind kind, RoomRuntime runtime, RoomContentBinding binding, Vector2 centre, string category, Action<string> Fail, Func<string> result)
        {
            var instance = binding.EventInstance;
            var handle = binding.Event;
            if (instance == null || handle == null || instance.Kind != kind) { Fail("event not composed as " + kind); Record(room, category, kind.ToString(), "", false, false, false, false, false, result()); yield break; }
            var art = HasVisibleArt(handle) && handle.GetComponentInChildren<WorldObjectVisual>().Key == WorldObjectArt.EventKey(kind.ToString());
            if (!art) Fail("event object without its final art");
            var title = EventPromptBuilder.TitleOf(kind).ToUpperInvariant();
            var action = EventPromptBuilder.ActionOf(kind).ToUpperInvariant();
            var cost = instance.CostCoins;
            var refusals = 0;
            handle.Refused += (_, _, _) => refusals++;
            var completed = new List<DungeonEventResult>();
            instance.Completed += (_, r) => completed.Add(r);

            // 1. A player who cannot pay sees the prompt with the reason and is refused without paying.
            if (cost > 0)
            {
                var poor = CreatePlayer(centre, 0);
                runtime.NotifyPlayerEntered(poor);
                var poorPrompt = Prompt(handle, poor);
                if (!poorPrompt.StartsWith($"{action} {title} ({cost} COINS)") || !poorPrompt.Contains($"NEED {cost} MORE COINS")) Fail("poor prompt '" + poorPrompt + "'");
                if (!handle.CanInteract(poor) || handle.CanActivate(poor)) Fail("poor player targetable/activatable mismatch");
                if (handle.Interact(poor) || refusals != 1 || instance.Phase != DungeonEventPhase.Available) Fail("poor player was not refused cleanly");
                UnityEngine.Object.DestroyImmediate(poor);
            }

            var player = CreatePlayer(centre, 3000);
            runtime.NotifyPlayerEntered(player);
            var prompt = Prompt(handle, player);
            var expectedPrompt = cost > 0 ? $"{action} {title} ({cost} COINS)" : $"{action} {title}";
            if (prompt != expectedPrompt) Fail($"prompt '{prompt}' != '{expectedPrompt}'");
            var coinsBefore = _services.CarriedWallet.Balance;
            var lootBefore = _services.GroundLoot.Count;
            var pressed = handle.Interact(player);
            var last = handle.LastResult;
            yield return null;
            var outcomeOk = false;
            var uiRequired = false;
            var interaction = string.Empty;

            switch (kind)
            {
                case DungeonEventKind.BrokenMachine:
                {
                    interaction = "pay -> seeded repair: loot or nothing";
                    var machine = (BrokenMachineEvent)instance;
                    var paid = _services.CarriedWallet.Balance == coinsBefore - cost;
                    var succeeded = machine.WillRepairSucceed();
                    var lootNow = _services.GroundLoot.Count > lootBefore;
                    outcomeOk = pressed && paid && (succeeded ? last.Outcome == DungeonEventOutcome.Success && lootNow : last.Outcome == DungeonEventOutcome.Failed && !lootNow);
                    if (!outcomeOk) Fail($"repair outcome {last.Outcome} (seeded success={succeeded}, paid={paid}, loot={lootNow})");
                    var text = EventOutcomeText.For(instance, last, _services.ResolveDefinition);
                    if (string.IsNullOrEmpty(text) || !(text.StartsWith("MACHINE REPAIRED") || text.StartsWith("REPAIR FAILED"))) Fail("no player-facing outcome text: '" + text + "'");
                    break;
                }
                case DungeonEventKind.LockedVault:
                {
                    interaction = "pay -> guaranteed loot";
                    outcomeOk = pressed && last.Outcome == DungeonEventOutcome.Success && _services.CarriedWallet.Balance == coinsBefore - cost && _services.GroundLoot.Count > lootBefore;
                    if (!outcomeOk) Fail("vault did not pay out for exactly the cost");
                    break;
                }
                case DungeonEventKind.CursedChest:
                {
                    interaction = "open -> doors lock, harder wave, loot on clear";
                    var enemies = UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Where(e => e != null && e.IsAlive).ToList();
                    var started = pressed && last.Outcome == DungeonEventOutcome.Started && runtime.DoorsLocked && enemies.Count > 0 && instance.Phase == DungeonEventPhase.InProgress;
                    if (!started) Fail($"cursed chest did not start its wave (locked={runtime.DoorsLocked}, enemies={enemies.Count})");
                    if (Prompt(handle, player) != string.Empty || handle.CanInteract(player)) Fail("prompt still offered while the wave runs");
                    foreach (var enemy in enemies) enemy.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
                    for (var i = 0; i < 4; i++) yield return null;
                    var remaining = UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Count(e => e != null && e.IsAlive);
                    if (remaining > 0)
                    {
                        // Reinforcements past the cap: clear them too.
                        foreach (var enemy in UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (enemy != null && enemy.IsAlive) enemy.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
                        for (var i = 0; i < 4; i++) yield return null;
                    }

                    outcomeOk = started && completed.Count == 1 && completed[0].Outcome == DungeonEventOutcome.Success && _services.GroundLoot.Count > lootBefore && !runtime.DoorsLocked;
                    if (!outcomeOk) Fail($"cursed chest did not resolve (completed={completed.Count}, doors locked={runtime.DoorsLocked})");
                    break;
                }
                case DungeonEventKind.SupplySignal:
                {
                    interaction = "activate -> timed waves, supply drop on survival";
                    var signal = (SupplySignalEvent)instance;
                    var firstWave = UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Count(e => e != null && e.IsAlive);
                    var started = pressed && last.Outcome == DungeonEventOutcome.Started && signal.IsRunning && firstWave > 0;
                    if (!started) Fail("supply signal did not start its first wave");
                    signal.Tick(signal.WaveInterval + 0.01f);
                    yield return null;
                    var secondWave = signal.Waves.Count;
                    signal.Tick(signal.DurationSeconds);
                    yield return null;
                    outcomeOk = started && secondWave >= 2 && completed.Count == 1 && completed[0].Outcome == DungeonEventOutcome.Success && _services.GroundLoot.Count > lootBefore;
                    if (!outcomeOk) Fail($"supply signal did not resolve (waves={secondWave}, completed={completed.Count})");
                    foreach (var enemy in UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (enemy != null) UnityEngine.Object.DestroyImmediate(enemy.gameObject);
                    break;
                }
                case DungeonEventKind.WeaponCache:
                {
                    interaction = "choose one of three -> selection screen, take one";
                    uiRequired = true;
                    var cache = (WeaponCacheEvent)instance;
                    var choiceRequests = 0;
                    handle.ChoiceRequested += (_, _) => choiceRequests++;
                    // The first press (above) had no subscriber yet; press again with the screen's seam attached.
                    var pressedAgain = handle.Interact(player);
                    var backpack = player.GetComponent<PlayerLootReceiver>().Backpack;
                    var chosen = cache.Choose(handle.LastActor, 0);
                    var taken = chosen.Outcome == DungeonEventOutcome.Success && backpack.Items.Any(i => i.InstanceId == cache.Choices[0].Item.InstanceId);
                    outcomeOk = pressed && pressedAgain && choiceRequests == 1 && cache.Choices.Count == 3 && taken && cache.IsConsumed;
                    if (!outcomeOk) Fail($"weapon cache: requests={choiceRequests}, choices={cache.Choices.Count}, taken={taken}");
                    break;
                }
            }

            // 3. Used state and idempotency: the object reads as used, no prompt, no second outcome, state recorded.
            yield return null;
            var visual = handle.GetComponentInChildren<WorldObjectVisual>();
            var tinted = visual != null && visual.Renderer.color == WorldObjectVisual.ResolvedTint;
            var coinsAfter = _services.CarriedWallet.Balance;
            var lootAfter = _services.GroundLoot.Count;
            var second = handle.Interact(player);
            yield return null;
            var idempotent = !second && instance.Phase != DungeonEventPhase.Available && instance.Phase != DungeonEventPhase.InProgress
                             && _services.CarriedWallet.Balance == coinsAfter && _services.GroundLoot.Count == lootAfter
                             && Prompt(handle, player) == string.Empty && !handle.CanInteract(player)
                             && runtime.State.IsResolved("event:" + kind) && tinted;
            if (!idempotent) Fail($"not idempotent / used state (phase={instance.Phase}, tinted={tinted}, resolved={runtime.State.IsResolved("event:" + kind)})");
            if (runtime.Lifecycle != RoomLifecycleState.Cleared || runtime.DoorsLocked) Fail("room cannot be exited");

            // 4. A revisit restores the resolved state without replaying anything.
            var revisitInstance = UnityEngine.Object.Instantiate(room.Prefab, new Vector3(centre.x + 40f, centre.y, 0f), Quaternion.identity);
            _created.Add(revisitInstance);
            var revisitRuntime = revisitInstance.AddComponent<RoomRuntime>();
            revisitRuntime.Configure(revisitInstance.GetComponent<RoomRoot>(), runtime.State.NodeId, Depth, 1);
            revisitRuntime.RestoreState(runtime.State.Clone());
            var revisitContext = new DungeonRuntimeContext(RoomCategoryComposerSeed(room, runtime.State.NodeId, kind), Depth, 1, _content.Enemies, new DefaultEnemySpawner(_content.Stagger), _content.DepthScaling);
            var revisit = RoomCategoryComposer.Compose(revisitRuntime, revisitContext, _services);
            var restored = revisit.EventInstance != null && revisit.EventInstance.Kind == kind && revisit.EventInstance.Phase != DungeonEventPhase.Available && !revisit.Event.CanInteract(player) && _services.GroundLoot.Count == lootAfter;
            if (!restored) Fail("revisit replayed or lost the resolved state");

            Record(room, category, interaction, prompt, uiRequired, art, pressed, outcomeOk, idempotent && restored, result());
        }

        private static int RoomCategoryComposerSeed(RoomDefinition room, int nodeId, DungeonEventKind kind) => SeedForKind(room, nodeId, kind);

        private void WriteMatrix()
        {
            var sb = new StringBuilder();
            sb.AppendLine("room definition/id,player-facing room name,biome,category/type,interaction type,prompt,UI required,runtime object present,interaction success,reward/outcome success,idempotency success,result");
            foreach (var row in _rows) sb.AppendLine(string.Join(",", row.Select(Csv)));
            File.WriteAllText(MatrixPath, sb.ToString());
        }

        private static string Csv(string value)
        {
            value ??= string.Empty;
            return value.Contains(',') || value.Contains('"') ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
    }
}
