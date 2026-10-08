using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Networking;
using RuinRail.UI.Base;
using RuinRail.UI.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The bottom-centre weapon HUD in a live run at 640×360 (shipping HUD composition and loadout): the starter kit,
    /// switching, a low magazine, a reload, a large magazine, no ammo, blaster heat, bow draw and a held / holstered
    /// Legendary special — each captured full-frame plus a ×4 crop of the bottom band. Captures: TestResults/WeaponHud.
    /// </summary>
    public sealed class WeaponHudLiveTests
    {
        private const string Folder = "TestResults/WeaponHud";
        private GameApp _app;
        private string _saveDir;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_weaponhud_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            Directory.CreateDirectory(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (root.GetComponents<Component>().Any(c => c != null && (c.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools"))) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            NetworkPlayerObject.VisualComposer = null;
            RoomDoorLock.SkinResolver = null;
            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 90f;
            while (_app.ComposedScene != scene) { Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed"); yield return null; }
        }

        private static IEnumerator Wait(float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        private static void Crop(LiveDungeonCapture.Result shot, RectInt rect, string name)
        {
            const int zoom = 4;
            var texture = new Texture2D(rect.width * zoom, rect.height * zoom, TextureFormat.RGBA32, false);
            var pixels = new Color32[texture.width * texture.height];
            for (var y = 0; y < texture.height; y++)
            for (var x = 0; x < texture.width; x++)
                pixels[y * texture.width + x] = shot.At(rect.x + x / zoom, rect.y + y / zoom);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        [UnityTest]
        public IEnumerator LiveRun_WeaponHud_SlotsAmmoReloadSwitchAndLegendarySpecial([Values(11, 27)] int seed)
        {
            RuinRail.Core.Input.GameplayInputGate.Reset();
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(seed);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Weapon Hud");
            hub.Onboarding.AcknowledgeStarterKit();
            _app.Settings.Current.Tutorial.ShowPrompts = false;
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            yield return Wait(0.5f);

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var biome = run.Expedition.State.Biome;
            var view = run.HudView;
            var p = view.PrimarySlot;
            var s = view.SecondarySlot;
            var loadout = run.Rig.Loadout;
            var inventory = run.Rig.Inventory;
            var ammo = run.Expedition.State.Inventory;
            var cam = run.Camera.Camera;
            var ppu = run.Camera.Config.PixelsPerUnit;
            var band = new RectInt(180, 0, 290, 50);

            void Shot(string state)
            {
                var shot = LiveDungeonCapture.Capture(Folder, $"weapons_{biome}_{state}", cam, ppu, includeUi: true);
                Crop(shot, band, $"weapons_{biome}_{state}_crop");
            }

            IEnumerator Equip(string id, Rarity rarity, EquippedSlot slot)
            {
                inventory.Unequip(slot);
                Assert.IsTrue(inventory.TryEquip(new ItemInstance(id, 1, rarity), slot), id);
                yield return null;
                yield return null;
            }

            RangedWeapon Ranged() => loadout.ActiveWeapon as RangedWeapon;

            // 1. The starter kit: the pistol held (lit card, brackets, pips), the knife holstered (dim, no readout).
            Assert.IsTrue(p.IsActive && p.BracketsVisible && !s.IsActive && !s.BracketsVisible);
            Assert.AreEqual(Ranged().CurrentMagazineSize, p.PipsShown, "one pip per round");
            Assert.AreEqual(Ranged().MagazineAmmo, p.PipsLit);
            Assert.IsFalse(s.ResourceVisible, "melee: no fake ammo");
            StringAssert.IsMatch(@"^\d+ / \d+$", p.ResourceText);
            Shot("01_start");

            // 2. Switching: the newly held slot flashes once, then reads as held. (One clean frame first: the frame
            // after a capture carries the capture's stall as its delta time and would swallow a 0.25 s flash.)
            yield return null;
            loadout.SelectSlot(WeaponSlot.Secondary);
            yield return null;
            Assert.IsTrue(s.IsActive && s.FlashActive && !p.IsActive);
            Shot("02_switch_flash");
            yield return Wait(0.4f);
            Assert.IsFalse(s.FlashActive, "the flash is short");
            Shot("03_knife_held");
            loadout.SelectSlot(WeaponSlot.Primary);
            yield return Wait(0.4f);

            // 3. A low magazine: the last quarter of rounds runs warm.
            Ranged().ApplyAuthoritativeState(2, false);
            yield return null;
            Assert.AreEqual(2, p.PipsLit);
            Shot("04_low_mag");

            // 4. Reloading: the gauge becomes the reload sweep and the status line reads RELOADING; it flashes when done.
            Assert.IsTrue(Ranged().TryStartReload());
            var reloadSeconds = Ranged().CurrentReloadTime;
            yield return Wait(reloadSeconds * 0.5f);
            Assert.IsTrue(Ranged().IsReloading);
            Assert.AreEqual("RELOADING", p.SpecialText);
            Assert.Greater(p.GaugeFill, 0.2f);
            Assert.Less(p.GaugeFill, 0.95f);
            Shot("05_reloading");
            var deadline = Time.realtimeSinceStartup + reloadSeconds + 1f;
            while (Ranged().IsReloading && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Assert.IsTrue(p.FlashActive, "a finished reload flashes the gauge");
            Assert.AreEqual(Ranged().CurrentMagazineSize, p.PipsLit);
            Shot("06_reloaded");

            // 5. A rifle in slot 1 (large magazine: continuous gauge), a blaster in slot 2.
            yield return Equip("weapon_ar_17", Rarity.Rare, EquippedSlot.PrimaryWeapon);
            loadout.SelectSlot(WeaponSlot.Primary);
            yield return Equip("weapon_pulse_carbine_b1", Rarity.Epic, EquippedSlot.SecondaryWeapon);
            yield return Wait(0.4f);
            Assert.IsTrue(p.PipsShown > 0 || p.GaugeFill > 0.99f, "a full magazine reads full");
            Shot("07_rifle_and_blaster");

            // 6. No ammo at all: the card shows red.
            var rifle = Ranged();
            var reserveBefore = ammo.Get(rifle.Definition.AmmoType);
            ammo.Consume(rifle.Definition.AmmoType, reserveBefore);
            rifle.ApplyAuthoritativeState(0, false);
            yield return null;
            Assert.IsTrue(p.IsUnavailable);
            Assert.AreEqual("NO AMMO", p.ResourceText);
            Shot("08_no_ammo");
            ammo.Add(rifle.Definition.AmmoType, reserveBefore);
            rifle.ApplyAuthoritativeState(rifle.CurrentMagazineSize, false);

            // 7. The blaster held, heated by real shots.
            loadout.SelectSlot(WeaponSlot.Secondary);
            var blaster = loadout.ActiveWeapon as BlasterWeapon;
            Assert.IsNotNull(blaster);
            var until = Time.realtimeSinceStartup + 1.2f;
            while (Time.realtimeSinceStartup < until) { blaster.TryFire(); yield return null; }
            Assert.Greater(s.GaugeFill, 0.05f, "heat shows on the gauge");
            Shot("09_blaster_heat");

            // 8. A bow drawing.
            yield return Equip("weapon_recurve_bow", Rarity.Uncommon, EquippedSlot.SecondaryWeapon);
            loadout.SelectSlot(WeaponSlot.Secondary);
            yield return Wait(0.3f);
            Shot("10_bow");

            // 9. The held Legendary: its special is shown on its card only, ready; after a real activation it cools down.
            yield return Equip("weapon_vanguard", Rarity.Legendary, EquippedSlot.PrimaryWeapon);
            loadout.SelectSlot(WeaponSlot.Primary);
            yield return Wait(0.4f);
            Assert.AreEqual("RMB READY", p.SpecialText);
            Assert.AreEqual(string.Empty, s.SpecialText);
            Shot("11_legendary_ready");
            Assert.IsTrue(run.Rig.Special.TryActivate());
            while (run.Rig.Special.IsRunning) yield return null;
            yield return Wait(0.3f);
            StringAssert.IsMatch(@"^RMB \d+%$", p.SpecialText, "cooling down");
            Shot("12_legendary_cooldown");
            loadout.SelectSlot(WeaponSlot.Secondary);
            yield return Wait(0.3f);
            Assert.AreEqual(string.Empty, p.SpecialText, "holstered: no special shown");
            Assert.AreEqual(string.Empty, s.SpecialText);
            Shot("13_legendary_holstered");
        }
    }
}
