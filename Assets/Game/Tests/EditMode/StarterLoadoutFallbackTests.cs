using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Networking;
using RuinRail.Persistence;
using RuinRail.UI.Base;
using RuinRail.UI.Multiplayer;
using UnityEditor;

namespace RuinRail.Tests
{
    /// <summary>
    /// Automatic free Starter Loadout fallback: a player who reaches Ready/Start with no weapon equipped gets the
    /// existing kit (75) equipped through the existing service before the run begins; a valid loadout is never touched;
    /// the fallback is idempotent, never writes storage and never runs on scene load or during a run.
    /// </summary>
    public class StarterLoadoutFallbackTests
    {
        private ItemDefinitionRegistry _registry;
        private BaseConfigs _configs;
        private StarterKitService _kit;

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            _configs = new BaseConfigs
            {
                Registry = _registry,
                AmmoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset"),
                Economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset"),
                Trader = AssetDatabase.LoadAssetAtPath<TraderConfig>("Assets/Game/ScriptableObjects/Base/TraderConfig.asset"),
                Workshop = AssetDatabase.LoadAssetAtPath<WorkshopConfig>("Assets/Game/ScriptableObjects/Base/WorkshopConfig.asset")
            };
            _kit = new StarterKitService(_configs.Resolve, _configs.ResolveAmmo, _configs.AmmoBalance);
        }

        private PlayerInventory NewInventory() => new(_configs.Resolve, _configs.ResolveAmmo, _configs.AmmoBalance);

        private static void AssertIsStarterLoadout(PlayerInventory inventory)
        {
            var pistol = inventory.GetEquipped(EquippedSlot.PrimaryWeapon);
            var knife = inventory.GetEquipped(EquippedSlot.SecondaryWeapon);
            var vest = inventory.GetEquipped(EquippedSlot.Armor);
            var bandage = inventory.GetEquipped(EquippedSlot.ActiveConsumable);
            Assert.AreEqual(StarterKitService.PistolId, pistol?.DefinitionId);
            Assert.AreEqual(StarterKitService.KnifeId, knife?.DefinitionId);
            Assert.AreEqual(StarterKitService.VestId, vest?.DefinitionId);
            Assert.AreEqual(StarterKitService.BandageId, bandage?.DefinitionId);
            Assert.GreaterOrEqual(inventory.Get(AmmoType.Light), StarterKitService.LightAmmoCount, "the kit's light ammo is there (on top of whatever the player already carried)");
            foreach (var piece in new[] { pistol, knife, vest })
            {
                Assert.AreEqual(Rarity.Common, piece.Rarity, piece.DefinitionId);
                Assert.IsEmpty(piece.AffixRolls, piece.DefinitionId);
                Assert.IsTrue(piece.IsUnsellable, piece.DefinitionId + " keeps the starter restriction");
            }
        }

        // ---- the service rule ----

        [Test]
        public void EmptyLoadout_GetsTheStarterLoadout()
        {
            var inventory = NewInventory();
            Assert.IsFalse(_kit.HasEquippedWeapon(inventory));
            Assert.IsTrue(_kit.EnsureEquippedLoadout(inventory));
            AssertIsStarterLoadout(inventory);
            Assert.IsTrue(_kit.HasEquippedWeapon(inventory));
        }

        [Test]
        public void EmptyEquipment_WithItemsOnlyInTheBackpack_GetsTheStarterLoadout_AndKeepsTheBackpackItems()
        {
            var inventory = NewInventory();
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("consumable_medkit", 2)));
            var medkit = inventory.BackpackSlots.First(i => i != null && i.DefinitionId == "consumable_medkit");
            Assert.IsTrue(_kit.EnsureEquippedLoadout(inventory));
            AssertIsStarterLoadout(inventory);
            Assert.IsTrue(inventory.Contains(medkit.InstanceId), "nothing already owned is lost");
            Assert.AreEqual(2, inventory.CountOf("consumable_medkit"));
            Assert.AreEqual(StarterKitService.LightAmmoCount, inventory.Get(AmmoType.Light));
        }

        [Test]
        public void ValidLoadout_IsPreservedExactly()
        {
            var inventory = NewInventory();
            var rifle = new ItemInstance("weapon_p9_ranger") { Rarity = Rarity.Rare };
            Assert.IsTrue(inventory.TryEquip(rifle, EquippedSlot.PrimaryWeapon));
            var before = LoadoutValidation.Fingerprint(inventory.ToSnapshot());
            Assert.IsFalse(_kit.EnsureEquippedLoadout(inventory));
            Assert.AreEqual(before, LoadoutValidation.Fingerprint(inventory.ToSnapshot()), "an intentional valid loadout is never overwritten");
            Assert.AreSame(rifle, inventory.GetEquipped(EquippedSlot.PrimaryWeapon));
            Assert.IsNull(inventory.GetEquipped(EquippedSlot.SecondaryWeapon), "no kit piece is added beside a valid loadout");
            Assert.AreEqual(0, inventory.Get(AmmoType.Light));
        }

        [Test]
        public void OnlyASecondaryWeapon_CountsAsEquipped()
        {
            var inventory = NewInventory();
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("weapon_field_knife"), EquippedSlot.SecondaryWeapon));
            Assert.IsFalse(_kit.EnsureEquippedLoadout(inventory));
            Assert.IsNull(inventory.GetEquipped(EquippedSlot.PrimaryWeapon));
        }

        [Test]
        public void ArmorWithoutAWeapon_IsNotAValidLoadout_AndTheKitFillsOnlyTheEmptySlots()
        {
            var inventory = NewInventory();
            var plate = new ItemInstance("armor_heavy_plate") { Rarity = Rarity.Epic };
            Assert.IsTrue(inventory.TryEquip(plate, EquippedSlot.Armor));
            Assert.IsTrue(_kit.EnsureEquippedLoadout(inventory));
            Assert.AreSame(plate, inventory.GetEquipped(EquippedSlot.Armor), "the equipped armor stays; the kit vest is not forced over it");
            Assert.AreEqual(StarterKitService.PistolId, inventory.GetEquipped(EquippedSlot.PrimaryWeapon).DefinitionId);
            Assert.AreEqual(StarterKitService.KnifeId, inventory.GetEquipped(EquippedSlot.SecondaryWeapon).DefinitionId);
            Assert.AreEqual(1, inventory.BackpackSlots.Count(i => i != null && i.DefinitionId == StarterKitService.VestId), "the vest went to the backpack instead");
        }

        [Test]
        public void RepeatedPreparation_IsIdempotent_NoDuplicates()
        {
            var inventory = NewInventory();
            Assert.IsTrue(_kit.EnsureEquippedLoadout(inventory));
            var fingerprint = LoadoutValidation.Fingerprint(inventory.ToSnapshot());
            Assert.IsFalse(_kit.EnsureEquippedLoadout(inventory));
            Assert.IsFalse(_kit.EnsureEquippedLoadout(inventory));
            Assert.AreEqual(fingerprint, LoadoutValidation.Fingerprint(inventory.ToSnapshot()));
            Assert.AreEqual(1, inventory.BackpackSlots.Count(i => i != null), "one light-ammo stack, nothing else was added again");
            Assert.AreEqual(StarterKitService.LightAmmoCount, inventory.Get(AmmoType.Light));
        }

        [Test]
        public void UnresolvedWeaponReferences_AreQuarantinedByTheSaveValidator_SoTheFallbackSeesAnEmptySlot_AndEquipsTheStarter()
        {
            var store = new MemorySaveStore();
            var saves = new SaveSlotService(store, _configs.Resolve);
            var slot = SaveSlotService.CreateNew();
            slot.Profile.StarterKitGranted = true; // the first-profile grant already happened once; only the broken weapon is left
            slot.Profile.SafeLoadout = new InventorySnapshot
            {
                Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstanceSnapshot { InstanceId = "ghost-1", DefinitionId = "weapon_does_not_exist", Quantity = 1 } } },
                Backpack = System.Array.Empty<InventorySnapshot.Entry>()
            };
            Assert.AreEqual(SaveError.None, saves.Save(slot));
            var result = saves.Load();
            Assert.IsTrue(result.Success, result.Diagnostics.ToString());
            var loaded = result.Slot;
            Assert.IsTrue(loaded.Quarantine.Any(q => q.Item.InstanceId == "ghost-1"), "the broken reference is kept aside, not deleted");

            var session = BaseSession.Open(loaded, saves, _configs);
            // Open() ran the existing rescue path (no weapon anywhere), which already equips the kit: the fallback then has nothing to do.
            Assert.IsTrue(_kit.HasEquippedWeapon(session.Loadout));
            Assert.IsFalse(session.EnsureStarterLoadoutIfEmpty());
            AssertIsStarterLoadout(session.Loadout);
            session.Dispose();
        }

        // ---- Ready / Start through the shelter view models ----

        private (BaseSession session, MainMenuViewModel menu) OpenSession(int seed = 7)
        {
            var store = new MemorySaveStore();
            var saves = new SaveSlotService(store, _configs.Resolve);
            var menu = new MainMenuViewModel(saves, _configs);
            menu.Play();
            return (menu.Session, menu);
        }

        private static void StripEquipmentIntoStorage(BaseSession session)
        {
            foreach (var slot in new[] { EquippedSlot.PrimaryWeapon, EquippedSlot.SecondaryWeapon, EquippedSlot.Armor, EquippedSlot.ActiveConsumable })
            {
                var item = session.Loadout.Unequip(slot);
                if (item != null) Assert.IsTrue(session.Storage.TryAdd(item), slot + " into storage");
            }
        }

        [Test]
        public void Ready_WithNothingEquippedButGearInStorage_EquipsTheStarterLoadout_AndReportsIt()
        {
            var (session, menu) = OpenSession();
            using var hub = new BaseHubViewModel(session, null, () => 7);
            StripEquipmentIntoStorage(session);
            var storageBefore = session.Storage.Items.Select(i => i.InstanceId).OrderBy(s => s).ToList();
            Assert.IsFalse(session.Lobby.Get(BaseSession.LocalClientId).HasValidLoadout, "no weapon equipped");

            var ammoBefore = session.Loadout.Get(AmmoType.Light);
            Assert.IsTrue(hub.Multiplayer.SetReady(true), hub.Multiplayer.Feedback.Text);
            StringAssert.Contains(MultiplayerPanelViewModel.StarterLoadoutEquippedMessage, hub.Multiplayer.Feedback.Text);
            Assert.IsFalse(hub.Multiplayer.Feedback.IsError, "a notice, not an error");
            Assert.IsTrue(hub.Multiplayer.LocalReady);
            AssertIsStarterLoadout(session.Loadout);
            Assert.AreEqual(ammoBefore + StarterKitService.LightAmmoCount, session.Loadout.Get(AmmoType.Light), "the kit's ammo joins what the player kept in the backpack");
            Assert.AreEqual(1, session.StarterLoadoutFallbacks);
            CollectionAssert.AreEqual(storageBefore, session.Storage.Items.Select(i => i.InstanceId).OrderBy(s => s).ToList(), "storage is untouched: no duplication into storage");
            Assert.IsTrue(session.Lobby.Get(BaseSession.LocalClientId).HasValidLoadout, "the lobby re-validated the changed loadout");

            Assert.IsTrue(hub.Transit.StartExpedition(), hub.Transit.Feedback.Text);
            Assert.IsTrue(session.Expedition.IsExpeditionActive);
            var carried = session.Expedition.State.Inventory;
            Assert.AreEqual(StarterKitService.PistolId, carried.GetEquipped(EquippedSlot.PrimaryWeapon).DefinitionId, "the run started with the starter pistol");
            Assert.AreEqual(1, session.StarterLoadoutFallbacks, "Start did not grant a second kit");
            menu.LeaveBase();
        }

        [Test]
        public void TerminalReady_WithNothingEquipped_EquipsTheStarterLoadout_AndShowsTheNoticeInItsStatus()
        {
            // The MULTIPLAYER station's READY control is the terminal view model's ToggleReady, which toggles the lobby
            // directly: it must run the same fallback hook the Transit/panel path uses.
            var (session, menu) = OpenSession();
            var terminal = new TerminalViewModel(new MultiplayerTerminalService(new NetworkSessionController(new FakeMultiplayerServices(), new FakeNetworkDriver())), session.Lobby, BaseSession.LocalClientId)
            {
                PrepareLoadout = session.EnsureStarterLoadoutIfEmpty
            };
            StripEquipmentIntoStorage(session);
            var member = session.Lobby.Get(BaseSession.LocalClientId);
            Assert.IsFalse(member.HasValidLoadout);
            Assert.IsFalse(session.Lobby.SetReady(BaseSession.LocalClientId, true), "the lobby itself still refuses an invalid loadout");

            var task = terminal.ActivateAsync(TerminalAction.ToggleReady);
            task.Wait();
            Assert.IsTrue(task.Result.IsNone);
            Assert.IsTrue(member.IsReady, "READY went through after the fallback equipped the kit");
            AssertIsStarterLoadout(session.Loadout);
            Assert.AreEqual(1, session.StarterLoadoutFallbacks);
            Assert.AreEqual(TerminalViewModel.StarterLoadoutEquippedNotice, terminal.Notice, "the notice is shown by the station");
            Assert.IsEmpty(terminal.ErrorText, "a notice, not an error");

            // Un-ready and Ready again: the loadout is valid now, so no second kit and no notice.
            task = terminal.ActivateAsync(TerminalAction.ToggleReady); task.Wait();
            Assert.IsFalse(member.IsReady);
            task = terminal.ActivateAsync(TerminalAction.ToggleReady); task.Wait();
            Assert.IsTrue(member.IsReady);
            Assert.AreEqual(1, session.StarterLoadoutFallbacks);
            Assert.IsEmpty(terminal.Notice);
            terminal.Dispose();
            menu.LeaveBase();
        }

        [Test]
        public void Start_WithAValidLoadout_NeverEquipsTheStarter()
        {
            var (session, menu) = OpenSession();
            using var hub = new BaseHubViewModel(session, null, () => 7);
            var fingerprint = LoadoutValidation.Fingerprint(session.Loadout.ToSnapshot());
            Assert.IsTrue(hub.Multiplayer.SetReady(true), hub.Multiplayer.Feedback.Text);
            Assert.AreEqual("Ready.", hub.Multiplayer.Feedback.Text);
            Assert.IsTrue(hub.Transit.StartExpedition(), hub.Transit.Feedback.Text);
            Assert.IsFalse(hub.Transit.Feedback.Text.Contains("STARTER"), hub.Transit.Feedback.Text);
            Assert.AreEqual(0, session.StarterLoadoutFallbacks);
            Assert.AreEqual(fingerprint, LoadoutValidation.Fingerprint(session.Expedition.State.Inventory.ToSnapshot()), "the run carries the player's own loadout, untouched");
            menu.LeaveBase();
        }

        [Test]
        public void Fallback_NeverRunsDuringARun_AndTheWipeRecoveryStillWorks()
        {
            var (session, menu) = OpenSession();
            using var hub = new BaseHubViewModel(session, null, () => 7);
            StripEquipmentIntoStorage(session);
            Assert.IsTrue(hub.Multiplayer.SetReady(true), hub.Multiplayer.Feedback.Text);
            Assert.IsTrue(hub.Transit.StartExpedition(), hub.Transit.Feedback.Text);
            Assert.AreEqual(1, session.StarterLoadoutFallbacks);

            // During the run the Base loadout is deliberately empty (the gear is at risk): the fallback must not fire.
            Assert.IsNull(session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon));
            Assert.IsFalse(session.EnsureStarterLoadoutIfEmpty(), "never during a run");
            Assert.AreEqual(1, session.StarterLoadoutFallbacks);

            // A wipe destroys the at-risk gear; back at the Base the loadout is empty again and the next Ready recovers it.
            session.Expedition.Fail();
            Assert.IsFalse(session.Expedition.IsExpeditionActive);
            Assert.IsNull(session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon), "the starter pistol was lost with the run");
            Assert.IsTrue(hub.Multiplayer.SetReady(true), hub.Multiplayer.Feedback.Text);
            AssertIsStarterLoadout(session.Loadout);
            Assert.AreEqual(2, session.StarterLoadoutFallbacks);
            Assert.AreEqual(1, session.Storage.Items.Count(i => i.IsUnsellable && i.DefinitionId == StarterKitService.PistolId), "only the copy the player stored is in storage; the fallback never writes storage");
            menu.LeaveBase();
        }

        [Test]
        public void OpeningTheShelterAgain_DoesNotGrantAnotherKit_WhenTheLoadoutIsValid()
        {
            var store = new MemorySaveStore();
            var saves = new SaveSlotService(store, _configs.Resolve);
            var menu = new MainMenuViewModel(saves, _configs);
            menu.Play();
            var session = menu.Session;
            var fingerprint = LoadoutValidation.Fingerprint(session.Loadout.ToSnapshot());
            Assert.IsTrue(session.GrantedFirstKit);
            menu.LeaveBase();

            menu.Play();
            var again = menu.Session;
            Assert.IsFalse(again.GrantedFirstKit);
            Assert.IsFalse(again.GrantedRescueKit);
            Assert.AreEqual(0, again.StarterLoadoutFallbacks, "nothing fires on scene load");
            Assert.AreEqual(fingerprint, LoadoutValidation.Fingerprint(again.Loadout.ToSnapshot()));
            menu.LeaveBase();
        }
    }
}
