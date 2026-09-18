using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Persistence;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 049 — display name validation/persistence and separate settings persistence.</summary>
    public class DisplayNameAndSettingsTests
    {
        private DisplayNamePolicy _policy;
        private ItemDefinitionRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _policy = AssetDatabase.LoadAssetAtPath<DisplayNamePolicy>("Assets/Game/ScriptableObjects/Player/DisplayNamePolicy.asset");
            Assert.IsNotNull(_policy, "Approved policy asset must exist.");
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;

        // ---- Acceptance 1: boundary / character / whitespace / markup / control / profanity fixtures ----

        [Test]
        public void Policy_UsesApprovedLengths_AndValidatesEveryFixtureClass()
        {
            Assert.AreEqual(3, _policy.MinLength);
            Assert.AreEqual(16, _policy.MaxLength);
            Assert.IsNotEmpty(_policy.Blocklist, "A small local blocklist is required.");

            // Boundary lengths after normalisation.
            Assert.AreEqual(DisplayNameError.TooShort, Check("ab"));
            Assert.AreEqual(DisplayNameError.None, Check("abc"));
            Assert.AreEqual(DisplayNameError.None, Check("1234567890123456"));
            Assert.AreEqual(DisplayNameError.TooLong, Check("12345678901234567"));
            Assert.AreEqual(DisplayNameError.TooShort, Check("   ab   "), "Length is measured after trimming.");
            Assert.AreEqual(DisplayNameError.None, Check(" a   b   c "), "Collapsed to 'a b c' = 5.");
            Assert.AreEqual(DisplayNameError.Empty, Check(""));
            Assert.AreEqual(DisplayNameError.Empty, Check("     "));
            Assert.AreEqual(DisplayNameError.Empty, Check(null));

            // Whitespace normalisation.
            Assert.AreEqual("Rail Ghost", DisplayNameValidator.Validate("  Rail    Ghost  ", _policy).Normalized);
            Assert.AreEqual("A B", DisplayNameValidator.Normalize("A       B"));

            // Allowed characters only.
            Assert.AreEqual(DisplayNameError.None, Check("Rail_Ghost-42"));
            Assert.AreEqual(DisplayNameError.InvalidCharacters, Check("Rail.Ghost"));
            Assert.AreEqual(DisplayNameError.InvalidCharacters, Check("Rail!"));
            Assert.AreEqual(DisplayNameError.InvalidCharacters, Check("Railé"));
            Assert.AreEqual(DisplayNameError.InvalidCharacters, Check("Rail\u200BGhost"), "Zero-width characters are not letters.");
            Assert.AreEqual(DisplayNameError.InvalidCharacters, Check("Rail\u00A0Ghost"), "Non-breaking space is not a space.");

            // Rich text / markup and control characters are rejected, never silently stripped into a valid name.
            Assert.AreEqual(DisplayNameError.Markup, Check("<b>Rail</b>"));
            Assert.AreEqual(DisplayNameError.Markup, Check("Rail<color=red>"));
            Assert.AreEqual(DisplayNameError.ControlCharacters, Check("Rail\nGhost"));
            Assert.AreEqual(DisplayNameError.ControlCharacters, Check("Rail\tGhost"));
            Assert.AreEqual(DisplayNameError.ControlCharacters, Check("Rail\u0007"));
            Assert.AreEqual(DisplayNameError.ControlCharacters, Check("\u001B[31mRail"));

            // Profanity: case-insensitive, token match, and collapsed match for longer entries; ordinary names pass.
            var first = _policy.Blocklist[0];
            Assert.AreEqual(DisplayNameError.Blocked, Check(first));
            Assert.AreEqual(DisplayNameError.Blocked, Check(first.ToUpperInvariant()));
            Assert.AreEqual(DisplayNameError.Blocked, Check("Rail " + first));
            Assert.AreEqual(DisplayNameError.Blocked, Check("Rail_" + first + "_99"));
            Assert.AreEqual(DisplayNameError.None, Check("Cassandra"), "Short entries never match inside ordinary names.");
            Assert.AreEqual(DisplayNameError.None, Check("Rail Ghost"));
        }

        [Test]
        public void CustomPolicy_TokenAndCollapsedMatchingRules()
        {
            var policy = DisplayNamePolicy.Create(3, 16, "bad", "verybad");
            try
            {
                Assert.AreEqual(DisplayNameError.Blocked, DisplayNameValidator.Validate("bad", policy).Error);
                Assert.AreEqual(DisplayNameError.Blocked, DisplayNameValidator.Validate("so-BAD-name", policy).Error);
                Assert.AreEqual(DisplayNameError.None, DisplayNameValidator.Validate("badger", policy).Error, "3-letter entry: token only.");
                Assert.AreEqual(DisplayNameError.Blocked, DisplayNameValidator.Validate("xVeRyBaDx", policy).Error, "5+ letter entry: also inside the name.");
                Assert.AreEqual(DisplayNameError.Blocked, DisplayNameValidator.Validate("very_bad", policy).Error, "Separators do not hide it.");
            }
            finally
            {
                Object.DestroyImmediate(policy);
            }
        }

        private DisplayNameError Check(string raw) => DisplayNameValidator.Validate(raw, _policy).Error;

        // ---- Acceptance 2: normalised name survives save/load and migration ----

        [Test]
        public void ConfirmedName_IsStoredNormalized_AndSurvivesSaveLoadAndMigration()
        {
            var store = new MemorySaveStore();
            var saves = new SaveSlotService(store, Resolve);
            var slot = SaveSlotService.CreateNew();
            var names = new DisplayNameService(slot, _policy);
            Assert.IsTrue(names.NeedsDisplayName);

            var rejected = names.TrySet("<i>x</i>");
            Assert.IsFalse(rejected.IsValid);
            Assert.IsTrue(names.NeedsDisplayName, "Invalid input changes nothing.");
            Assert.AreEqual("Runner", slot.Profile.DisplayName);

            var ok = names.TrySet("   Rail    Ghost_7  ");
            Assert.IsTrue(ok.IsValid);
            Assert.AreEqual("Rail Ghost_7", slot.Profile.DisplayName);
            Assert.IsFalse(names.NeedsDisplayName);
            Assert.AreEqual(SaveError.None, saves.Save(slot));

            var loaded = saves.Load();
            Assert.AreEqual("Rail Ghost_7", loaded.Slot.Profile.DisplayName);
            Assert.IsTrue(loaded.Slot.FirstLaunch.DisplayNameConfirmed);
            Assert.IsFalse(new DisplayNameService(loaded.Slot, _policy).NeedsDisplayName);

            // Rename applies the same rules.
            var renamed = new DisplayNameService(loaded.Slot, _policy).TrySet("New-Name");
            Assert.IsTrue(renamed.IsValid);
            Assert.AreEqual("New-Name", loaded.Slot.Profile.DisplayName);

            // Legacy v1 document: the name is carried through the migration unchanged and counts as confirmed.
            var legacy = JsonUtility.ToJson(new PlayerProfile { DisplayName = "Old Runner", TotalXp = 10 });
            var migrated = new SaveSlotService(new MemorySaveStore { Document = legacy }, Resolve).Load();
            Assert.IsTrue(migrated.Success, migrated.Diagnostics.ToString());
            Assert.AreEqual("Old Runner", migrated.Slot.Profile.DisplayName);
            Assert.IsFalse(new DisplayNameService(migrated.Slot, _policy).NeedsDisplayName);
        }

        // ---- Acceptance 3: settings and gameplay are saved/reset independently ----

        [Test]
        public void Settings_PersistSeparately_AndResetsAreIndependent()
        {
            var gameplayStore = new MemorySaveStore();
            var settingsStore = new MemorySaveStore();
            var saves = new SaveSlotService(gameplayStore, Resolve);
            var settings = new UserSettingsService(settingsStore);

            var slot = SaveSlotService.CreateNew();
            slot.Profile.TotalXp = 777;
            slot.Profile.BankedCoins = 4321;
            slot.Profile.DisplayName = "Keeper";
            slot.Storage.Slots = new[] { new InventorySnapshot.Entry { Slot = 0, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } };
            Assert.AreEqual(SaveError.None, saves.Save(slot));

            var loadedDefaults = settings.Load();
            Assert.AreEqual(SaveError.NoSave, settings.LastError);
            Assert.AreEqual(1f, loadedDefaults.Audio.MasterVolume);
            settings.Current.Audio.MusicVolume = 0.25f;
            settings.Current.Video.Fullscreen = false;
            settings.Current.Controls.BindingOverridesJson = "{\"bindings\":[{\"action\":\"Player/Dash\",\"path\":\"<Keyboard>/leftShift\"}]}";
            settings.Current.Accessibility.ScreenShake = false;
            Assert.AreEqual(SaveError.None, settings.Save());
            Assert.AreEqual(1, settingsStore.WriteCount);
            Assert.AreEqual(1, gameplayStore.WriteCount, "Saving settings never writes the gameplay slot.");

            var reloaded = new UserSettingsService(settingsStore).Load();
            Assert.AreEqual(0.25f, reloaded.Audio.MusicVolume);
            Assert.IsFalse(reloaded.Video.Fullscreen);
            StringAssert.Contains("leftShift", reloaded.Controls.BindingOverridesJson);
            Assert.IsFalse(reloaded.Accessibility.ScreenShake);
            Assert.IsFalse(gameplayStore.Document.Contains("MusicVolume"), "No settings inside the gameplay document.");
            Assert.IsFalse(settingsStore.Document.Contains("BankedCoins"), "No gameplay inside the settings document.");

            // Reset settings: gameplay untouched.
            Assert.AreEqual(SaveError.None, PlayerDataReset.ResetSettings(settings));
            Assert.AreEqual(1f, settings.Current.Audio.MusicVolume);
            Assert.AreEqual("", settings.Current.Controls.BindingOverridesJson);
            var gameplayAfter = saves.Load().Slot;
            Assert.AreEqual(777, gameplayAfter.Profile.TotalXp);
            Assert.AreEqual(4321, gameplayAfter.Profile.BankedCoins);
            Assert.AreEqual(1, gameplayAfter.Storage.Slots.Length);
            Assert.AreEqual(1, gameplayStore.WriteCount);

            // Reset gameplay: settings untouched, previous gameplay kept as backup, explicit only.
            settings.Current.Audio.SfxVolume = 0.5f;
            settings.Save();
            Assert.AreEqual(SaveError.None, PlayerDataReset.ResetGameplay(saves, out var fresh));
            Assert.AreEqual(0, fresh.Profile.TotalXp);
            Assert.AreEqual(0, saves.Load().Slot.Profile.BankedCoins);
            Assert.IsTrue(gameplayStore.Backup.Contains("\"BankedCoins\":4321"), "The wiped profile is still in the backup.");
            Assert.AreEqual(0.5f, new UserSettingsService(settingsStore).Load().Audio.SfxVolume);

            // Corrupt settings: defaults reported, nothing overwritten, gameplay untouched.
            settingsStore.Document = "not json";
            settingsStore.Backup = null;
            var corrupt = new UserSettingsService(settingsStore);
            corrupt.Load();
            Assert.AreEqual(SaveError.Corrupt, corrupt.LastError);
            Assert.IsTrue(corrupt.LastDiagnostics.Entries.Any(e => e.Code == "settings.parse"));
            Assert.AreEqual("not json", settingsStore.Document);
            Assert.AreEqual(1f, corrupt.Current.Audio.SfxVolume);
        }

        // ---- Acceptance 4: no account ids / secrets in gameplay data ----

        [Test]
        public void GameplaySave_HoldsNoAccountIdsOrSecrets()
        {
            var banned = new[] { "password", "secret", "token", "account", "email", "auth", "credential", "apikey", "session" };
            foreach (var type in new[] { typeof(SaveSlot), typeof(PlayerProfile), typeof(FirstLaunchFlags), typeof(ExpeditionMarker), typeof(SettingsData) })
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    var name = field.Name.ToLowerInvariant();
                    Assert.IsFalse(banned.Any(name.Contains), $"{type.Name}.{field.Name} looks like account/secret data.");
                }
            }

            var document = SaveSlotService.Serialize(SaveSlotService.CreateNew()).ToLowerInvariant();
            Assert.IsFalse(banned.Any(document.Contains));
            Assert.IsFalse(document.Contains("profileseed") && document.Contains("\"email"), "Only a local seed and a display name identify the profile.");
        }
    }
}
