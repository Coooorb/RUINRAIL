using System;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Networking;
using RuinRail.Persistence;
using UnityEditor;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 100 — party Ready/loadout flow (81 Ready Flow, 94 Base UI multiplayer, 20 class-agnostic slots).</summary>
    public sealed class PartyLobbyTests
    {
        private const ulong Host = 0;
        private const ulong ClientA = 1;
        private const ulong ClientB = 2;

        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;

        private PartyLobby NewLobby() => new PartyLobby(Host, Resolve);

        private static InventorySnapshot WeaponLoadout(string weaponId = "weapon_p9_ranger", string instanceId = null)
        {
            var item = new ItemInstance(weaponId);
            var snap = item.ToSnapshot();
            if (instanceId != null) snap.InstanceId = instanceId;
            return new InventorySnapshot
            {
                Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = snap } },
                Backpack = Array.Empty<InventorySnapshot.Entry>()
            };
        }

        private static InventorySnapshot EmptyLoadout() => new InventorySnapshot { Equipped = Array.Empty<InventorySnapshot.Entry>(), Backpack = Array.Empty<InventorySnapshot.Entry>() };

        private PartyLobby ReadyParty(params ulong[] clients)
        {
            var lobby = NewLobby();
            foreach (var c in clients)
            {
                lobby.Join(c, "p" + c);
                lobby.SetLoadout(c, WeaponLoadout());
                Assert.IsTrue(lobby.SetReady(c, true), $"client {c} could not ready");
            }

            return lobby;
        }

        // ---- Req 1: prepare loadout + Ready; changing gear after Ready unreadies ----

        [Test]
        public void Ready_RequiresValidLoadout_AndEmptyLoadoutIsRejected()
        {
            var lobby = NewLobby();
            lobby.Join(Host, "host");
            Assert.IsFalse(lobby.SetReady(Host, true), "No loadout submitted yet.");
            lobby.SetLoadout(Host, EmptyLoadout());
            Assert.IsFalse(lobby.Get(Host).HasValidLoadout);
            Assert.AreEqual("no weapon equipped", lobby.Get(Host).InvalidReason);
            Assert.IsFalse(lobby.SetReady(Host, true));

            lobby.SetLoadout(Host, WeaponLoadout());
            Assert.IsTrue(lobby.Get(Host).HasValidLoadout);
            Assert.IsTrue(lobby.SetReady(Host, true));
            Assert.IsTrue(lobby.Get(Host).IsReady);
        }

        [Test]
        public void ChangingLoadout_AfterReady_ClearsReady_ButResubmittingSameLoadoutDoesNot()
        {
            var lobby = NewLobby();
            lobby.Join(ClientA, "a");
            var loadout = WeaponLoadout(instanceId: "w1");
            lobby.SetLoadout(ClientA, loadout);
            lobby.SetReady(ClientA, true);

            lobby.SetLoadout(ClientA, WeaponLoadout(instanceId: "w1"));
            Assert.IsTrue(lobby.Get(ClientA).IsReady, "Identical loadout resubmitted: still Ready.");

            lobby.SetLoadout(ClientA, WeaponLoadout("weapon_p9_ranger", "w2"));
            Assert.IsFalse(lobby.Get(ClientA).IsReady, "81: changing loadout after Ready clears Ready.");
        }

        [Test]
        public void SecondaryWeaponAlone_IsAValidLoadout_SlotsAreClassAgnostic()
        {
            var lobby = NewLobby();
            lobby.Join(Host, "host");
            var snap = new InventorySnapshot
            {
                Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.SecondaryWeapon, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } },
                Backpack = Array.Empty<InventorySnapshot.Entry>()
            };
            lobby.SetLoadout(Host, snap);
            Assert.IsTrue(lobby.Get(Host).HasValidLoadout);
        }

        [Test]
        public void Loadout_WithUnknownItemOrDuplicateInstanceIds_IsInvalid()
        {
            var lobby = NewLobby();
            lobby.Join(Host, "host");
            var unknown = WeaponLoadout();
            unknown.Equipped[0].Item.DefinitionId = "weapon_does_not_exist";
            lobby.SetLoadout(Host, unknown);
            Assert.IsFalse(lobby.Get(Host).HasValidLoadout);
            StringAssert.Contains("unknown item", lobby.Get(Host).InvalidReason);

            var dup = WeaponLoadout(instanceId: "same");
            dup.Backpack = new[] { new InventorySnapshot.Entry { Slot = 0, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } };
            dup.Backpack[0].Item.InstanceId = "same";
            lobby.SetLoadout(Host, dup);
            Assert.IsFalse(lobby.Get(Host).HasValidLoadout);
            Assert.AreEqual("duplicate instance ids", lobby.Get(Host).InvalidReason);
        }

        [Test]
        public void LoadoutBinder_ResubmitsOnEquipChange_AndUnreadies()
        {
            var lobby = NewLobby();
            lobby.Join(Host, "host");
            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            using var binder = new LobbyLoadoutBinder(lobby, Host, inventory);
            Assert.IsFalse(lobby.Get(Host).HasValidLoadout, "Empty inventory is not a valid loadout.");

            Assert.IsTrue(inventory.TryEquip(new ItemInstance("weapon_p9_ranger"), EquippedSlot.PrimaryWeapon));
            Assert.IsTrue(lobby.Get(Host).HasValidLoadout);
            Assert.IsTrue(lobby.SetReady(Host, true));

            Assert.IsTrue(inventory.TryEquip(new ItemInstance("weapon_p9_ranger"), EquippedSlot.SecondaryWeapon));
            Assert.IsFalse(lobby.Get(Host).IsReady, "Equipping after Ready unreadies through the binder.");
            Assert.IsTrue(lobby.SetReady(Host, true));

            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("weapon_p9_ranger")));
            Assert.IsFalse(lobby.Get(Host).IsReady, "Backpack mutation also unreadies.");
        }

        // ---- Req 2: host-only start, all Ready ----

        [Test]
        public void Start_FailsUntilEveryMemberIsReady_AndOnlyHostCanStart()
        {
            var lobby = NewLobby();
            lobby.Join(Host, "host");
            lobby.Join(ClientA, "a");
            lobby.SetLoadout(Host, WeaponLoadout());
            lobby.SetLoadout(ClientA, WeaponLoadout());
            lobby.SetReady(Host, true);

            Assert.AreEqual(LobbyStartError.NotAllReady, lobby.TryStart(Host, 7, Biome.RuinedMetro, out _));
            Assert.IsFalse(lobby.HasStarted);

            lobby.SetReady(ClientA, true);
            Assert.AreEqual(LobbyStartError.NotHost, lobby.TryStart(ClientA, 7, Biome.RuinedMetro, out _), "Only the host starts.");
            Assert.IsFalse(lobby.HasStarted);

            Assert.AreEqual(LobbyStartError.None, lobby.TryStart(Host, 7, Biome.RuinedMetro, out var snapshot));
            Assert.IsTrue(lobby.HasStarted);
            Assert.AreEqual(2, snapshot.PartySize);
        }

        [Test]
        public void Start_FailsWhenAnyLoadoutBecameInvalid_OrNobodyJoined()
        {
            var lobby = NewLobby();
            Assert.AreEqual(LobbyStartError.NoMembers, lobby.TryStart(Host, 1, Biome.RuinedMetro, out _));

            lobby.Join(Host, "host");
            lobby.SetLoadout(Host, EmptyLoadout());
            Assert.AreEqual(LobbyStartError.InvalidLoadout, lobby.TryStart(Host, 1, Biome.RuinedMetro, out _));
        }

        [Test]
        public void AMemberLeaving_LetsTheRemainingReadyPartyStart_WithReducedSize()
        {
            var lobby = ReadyParty(Host, ClientA, ClientB);
            lobby.SetReady(ClientB, false);
            Assert.AreEqual(LobbyStartError.NotAllReady, lobby.TryStart(Host, 3, Biome.RuinedMetro, out _));
            lobby.Leave(ClientB);
            Assert.AreEqual(LobbyStartError.None, lobby.TryStart(Host, 3, Biome.RuinedMetro, out var snapshot));
            Assert.AreEqual(2, snapshot.PartySize);
        }

        // ---- Req 3: party size + risk snapshots captured exactly once ----

        [Test]
        public void Start_CapturesPartySize_AndEachMemberRiskSnapshotExactlyOnce()
        {
            var lobby = ReadyParty(Host, ClientA, ClientB);
            var started = 0;
            lobby.Started += _ => started++;
            Assert.AreEqual(LobbyStartError.None, lobby.TryStart(Host, 99, Biome.RuinedMetro, out var snapshot));

            Assert.AreEqual(3, snapshot.PartySize);
            Assert.AreEqual(99, snapshot.RunSeed);
            Assert.AreEqual((int)Biome.RuinedMetro, snapshot.Biome);
            Assert.AreEqual(3, snapshot.Members.Count);
            CollectionAssert.AllItemsAreUnique(snapshot.Members.Select(m => m.ClientId).ToList());
            foreach (var member in snapshot.Members)
            {
                Assert.IsNotNull(member.Loadout);
                Assert.AreEqual(lobby.Get(member.ClientId).LoadoutFingerprint, member.LoadoutFingerprint);
            }

            Assert.AreEqual(1, started);
            Assert.IsFalse(string.IsNullOrEmpty(snapshot.StartTransactionId));
        }

        // ---- Req 4: duplicate start + late mutation ----

        [Test]
        public void DuplicateStartRequests_ReturnTheSameSnapshot_AndRaiseStartedOnce()
        {
            var lobby = ReadyParty(Host, ClientA);
            var started = 0;
            lobby.Started += _ => started++;
            Assert.AreEqual(LobbyStartError.None, lobby.TryStart(Host, 5, Biome.RuinedMetro, out var first));
            Assert.AreEqual(LobbyStartError.AlreadyStarted, lobby.TryStart(Host, 5, Biome.RuinedMetro, out var second));
            Assert.AreEqual(LobbyStartError.AlreadyStarted, lobby.TryStart(Host, 6, Biome.RuinedMetro, out var third));
            Assert.AreSame(first, second);
            Assert.AreSame(first, third);
            Assert.AreEqual(1, started);
            Assert.AreEqual(3, lobby.StartRequests);
        }

        [Test]
        public void AfterStart_LoadoutsAreLocked_AndPartyIsClosed()
        {
            var lobby = ReadyParty(Host, ClientA);
            lobby.TryStart(Host, 5, Biome.RuinedMetro, out var snapshot);
            var before = lobby.Get(ClientA).LoadoutFingerprint;

            Assert.IsFalse(lobby.SetLoadout(ClientA, WeaponLoadout(instanceId: "late")), "Late loadout mutation is refused.");
            Assert.AreEqual(before, lobby.Get(ClientA).LoadoutFingerprint);
            Assert.AreEqual(before, snapshot.Members.First(m => m.ClientId == ClientA).LoadoutFingerprint);
            Assert.IsTrue(lobby.Get(ClientA).IsReady);
            Assert.IsFalse(lobby.SetReady(ClientA, false));
            Assert.Throws<InvalidOperationException>(() => lobby.Join(ClientB, "b"));
        }

        [Test]
        public void ExpeditionStartCoordinator_StartsExactlyOnePerTransaction_AcrossRedelivery()
        {
            var lobby = ReadyParty(Host, ClientA);
            lobby.TryStart(Host, 21, Biome.RuinedMetro, out var snapshot);

            var slot = SaveSlotService.CreateNew();
            slot.Profile.SafeLoadout = WeaponLoadout();
            var ammoByType = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            var expedition = new ExpeditionService(Resolve, t => ammoByType.TryGetValue(t, out var a) ? a : null, _ammoBalance);
            var starts = 0;
            expedition.ExpeditionStarted += _ => starts++;

            var coordinator = new ExpeditionStartCoordinator();
            var state = coordinator.Apply(snapshot, expedition, slot.Profile);
            Assert.IsNotNull(state);
            Assert.AreEqual(21, state.RunSeed);
            Assert.AreSame(state, coordinator.Apply(snapshot, expedition, slot.Profile), "Re-delivered start is ignored.");
            Assert.AreSame(state, coordinator.Apply(snapshot, expedition, slot.Profile));
            Assert.AreEqual(1, starts);
            Assert.AreEqual(1, coordinator.Applied);
            Assert.AreEqual(2, coordinator.Ignored);
            Assert.IsTrue(expedition.IsExpeditionActive);
        }

        // ---- Req 5: max 3, join-code session only ----

        [Test]
        public void Party_IsCappedAtThree_AndJoinIsIdempotent()
        {
            var lobby = NewLobby();
            Assert.AreEqual(3, lobby.MaxMembers);
            lobby.Join(Host, "host");
            Assert.AreSame(lobby.Get(Host), lobby.Join(Host, "host"));
            lobby.Join(ClientA, "a");
            lobby.Join(ClientB, "b");
            Assert.Throws<InvalidOperationException>(() => lobby.Join(3, "d"));
            Assert.AreEqual(3, lobby.Members.Count);
        }
    }
}
