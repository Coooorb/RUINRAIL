using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.UI.Onboarding;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 136 — contextual first-expedition prompts: shown only under their context, completed by the player's own action, once per profile, never touching gameplay.</summary>
    public class ExpeditionTutorialTests
    {
        private readonly List<Object> _created = new();
        private PlayerBalanceConfig _balance;

        [SetUp]
        public void SetUp()
        {
            _balance = AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            DamageAuthority.LocalIsAuthoritative = true;
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;
        private AmmoItemDefinition ResolveAmmo(AmmoType type) => _registry.Definitions.OfType<AmmoItemDefinition>().FirstOrDefault(a => a.AmmoType == type);

        [Test]
        public void Prompts_AppearOnlyInContext_CompleteOnTheTaughtAction_AndNeverRepeat()
        {
            var reader = new FakePlayerInputReader();
            var progress = new InMemoryTutorialProgress();
            var prompts = new TutorialPromptService(progress, new SchemeGlyphs(InputScheme.KeyboardMouse));
            var go = new GameObject("Local");
            _created.Add(go);
            var health = go.AddComponent<HealthComponent>();
            health.SetMaxHealth(100);
            var inventory = new PlayerInventory(Resolve, ResolveAmmo, _ammoBalance);
            var expedition = new ExpeditionService(Resolve, ResolveAmmo, _ammoBalance, null, "Local");
            using var binder = new ExpeditionTutorialBinder(prompts, reader).Attach(expedition).Attach(health, inventory);

            Assert.IsNull(prompts.Active, "Nothing before any context.");
            reader.RaiseDash();
            Assert.IsNull(prompts.Active, "Actions without a prompt do nothing.");

            // Movement + aim at expedition start; completes only after the player moved AND aimed.
            binder.ObserveExpeditionStarted();
            Assert.AreEqual(TutorialPromptId.MoveAim, prompts.Active);
            StringAssert.Contains("WASD", prompts.ActiveText);
            binder.Tick();
            reader.Move = Vector2.right;
            binder.Tick();
            Assert.AreEqual(TutorialPromptId.MoveAim, prompts.Active, "Moved but not aimed yet.");
            reader.Aim = new Vector2(3f, 1f);
            binder.Tick();
            Assert.IsNull(prompts.Active);
            Assert.IsTrue(progress.IsSeen(TutorialPromptId.MoveAim));

            // Fire on the first enemy; completes on the first held fire.
            binder.ObserveEnemySpawned();
            Assert.AreEqual(TutorialPromptId.Fire, prompts.Active);
            reader.FireHeld = true;
            binder.Tick();
            reader.FireHeld = false;
            Assert.IsNull(prompts.Active);

            // Dash at the first dangerous attack; dash input completes it. A second telegraph never re-prompts.
            binder.ObserveAttackTelegraph();
            Assert.AreEqual(TutorialPromptId.Dash, prompts.Active);
            Assert.AreEqual("Enemy attack incoming — Space: Dash through it.", prompts.ActiveText);
            reader.RaiseDash();
            Assert.IsNull(prompts.Active);
            binder.ObserveAttackTelegraph();
            Assert.IsNull(prompts.Active, "Seen: no repeat.");

            // Pickup + inventory at the first item; Interact completes it.
            binder.ObserveLootSpawned();
            Assert.AreEqual(TutorialPromptId.PickupInventory, prompts.Active);
            StringAssert.Contains("E: pick it up", prompts.ActiveText);
            StringAssert.Contains("Tab: open your inventory", prompts.ActiveText);
            reader.RaiseInteract();
            Assert.IsNull(prompts.Active);

            // Consumable only when healing is relevant AND a consumable is carried.
            health.TryApplyDamage(new DamageRequest(health.MaxHealth / 2 + 1));
            Assert.IsNull(prompts.Active, "Low health without a consumable: nothing to teach.");
            var bandage = new ItemInstance("consumable_bandage", 1);
            Assert.IsTrue(inventory.TryEquip(bandage, EquippedSlot.ActiveConsumable));
            health.TryApplyDamage(new DamageRequest(1));
            Assert.AreEqual(TutorialPromptId.Consumable, prompts.Active);
            StringAssert.Contains("G: use your active consumable", prompts.ActiveText);
            reader.RaiseConsumableUsed();
            Assert.IsNull(prompts.Active);

            // Weapon swap once both weapon slots are occupied.
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("weapon_p9_ranger"), EquippedSlot.PrimaryWeapon));
            Assert.IsNull(prompts.Active, "One weapon: no swap to teach.");
            const string secondary = "weapon_ar_17";
            Assert.IsTrue(inventory.TryEquip(new ItemInstance(secondary), EquippedSlot.SecondaryWeapon), "Weapon slots are class-agnostic.");
            Assert.AreEqual(TutorialPromptId.WeaponSwap, prompts.Active);
            reader.RaiseWeaponSwapped();
            Assert.IsNull(prompts.Active);

            // Transit after the first boss: completes when the party decision resolves; the prompt never votes.
            var decision = new TransitDecision(new PartyTransitPolicy(), new[] { "Local" });
            decision.Open();
            binder.ObserveTransitOpened(decision);
            Assert.AreEqual(TutorialPromptId.Transit, prompts.Active);
            Assert.AreEqual(TransitDecisionState.Open, decision.State, "The prompt does not vote.");
            decision.Submit("Local", TransitChoice.ReturnToShelter);
            Assert.IsNull(prompts.Active);
            Assert.AreEqual(7, progress.Seen.Count, "Every taught prompt is stored as seen (Reload needs an empty magazine).");

            // Gameplay untouched by the whole flow: the player still has the damage it took, nothing else changed.
            Assert.AreEqual(health.MaxHealth - (health.MaxHealth / 2 + 1) - 1, health.CurrentHealth);
            Assert.AreEqual(secondary, inventory.GetEquipped(EquippedSlot.SecondaryWeapon).DefinitionId);
        }

        [Test]
        public void Prompts_AreLocalOnly_DisabledProgressShowsNothing_AndRemoteReplicasNeverTrigger()
        {
            var progress = new InMemoryTutorialProgress { PromptsEnabled = false };
            var prompts = new TutorialPromptService(progress, new SchemeGlyphs(InputScheme.Gamepad));
            var reader = new FakePlayerInputReader();
            using var binder = new ExpeditionTutorialBinder(prompts, reader);
            binder.ObserveExpeditionStarted();
            binder.ObserveEnemySpawned();
            Assert.IsNull(prompts.Active);
            Assert.AreEqual(0, prompts.Shown);
            Assert.AreEqual(0, progress.Seen.Count, "Disabled prompts are not marked seen either.");

            // Two players, two progress records: the veteran's state never affects the newcomer.
            var veteran = new InMemoryTutorialProgress();
            foreach (TutorialPromptId id in System.Enum.GetValues(typeof(TutorialPromptId))) veteran.MarkSeen(id);
            var veteranPrompts = new TutorialPromptService(veteran, new SchemeGlyphs(InputScheme.KeyboardMouse));
            var newcomer = new TutorialPromptService(new InMemoryTutorialProgress(), new SchemeGlyphs(InputScheme.KeyboardMouse));
            Assert.IsFalse(veteranPrompts.Trigger(TutorialPromptId.Fire));
            Assert.IsTrue(newcomer.Trigger(TutorialPromptId.Fire));
            Assert.IsNull(veteranPrompts.Active);
        }
    }
}
