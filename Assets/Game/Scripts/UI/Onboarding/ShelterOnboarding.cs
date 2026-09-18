using System;
using System.Linq;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Persistence;
using RuinRail.UI.Base;

namespace RuinRail.UI.Onboarding
{
    /// <summary>Tutorial "seen" state in the gameplay save (ui/95), enabled flag from the settings document; every change is an autosave reason.</summary>
    public sealed class SaveSlotTutorialProgress : ITutorialProgress
    {
        private readonly SaveSlot _slot;
        private readonly AutosaveService _autosave;
        private readonly Func<bool> _enabled;

        public SaveSlotTutorialProgress(SaveSlot slot, AutosaveService autosave, Func<bool> promptsEnabled)
        {
            _slot = slot ?? throw new ArgumentNullException(nameof(slot));
            _autosave = autosave;
            _enabled = promptsEnabled;
            _slot.FirstLaunch ??= new FirstLaunchFlags();
            _slot.FirstLaunch.TutorialPromptsSeen ??= new System.Collections.Generic.List<string>();
        }

        public bool PromptsEnabled => _enabled?.Invoke() ?? true;
        public bool IsSeen(TutorialPromptId id) => _slot.FirstLaunch.TutorialPromptsSeen.Contains(id.ToString());

        public void MarkSeen(TutorialPromptId id)
        {
            if (IsSeen(id)) return;
            _slot.FirstLaunch.TutorialPromptsSeen.Add(id.ToString());
            _autosave?.MarkDirty("tutorial_seen");
        }

        /// <summary>Settings "reset tutorials": the seen list only; nothing else in the save changes.</summary>
        public void ResetSeen()
        {
            if (_slot.FirstLaunch.TutorialPromptsSeen.Count == 0) return;
            _slot.FirstLaunch.TutorialPromptsSeen.Clear();
            _autosave?.MarkDirty("tutorial_reset");
        }
    }

    public enum ShelterOnboardingStep
    {
        ChooseDisplayName,
        ReceiveStarterKit,
        EquipGear,
        StartFirstExpedition,
        Complete
    }

    /// <summary>
    /// ui/95 first launch: 1. choose display name, 2. receive the starter kit, 3. equip Primary Weapon and Armor,
    /// 4. start the first expedition. Runs over the existing DisplayNameService / StarterKitService / Base loadout /
    /// ExpeditionService — nothing is granted or validated here a second time, so every step stays idempotent.
    /// Existing profiles (kit granted before this session, or any expedition ended) are marked complete on open and
    /// only ever see the name step when a name was never confirmed.
    /// </summary>
    public sealed class ShelterOnboardingViewModel : IDisposable
    {
        private readonly BaseSession _session;
        private readonly DisplayNameService _names;
        private bool _kitAcknowledged;

        public ShelterOnboardingViewModel(BaseSession session, DisplayNamePolicy policy)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _names = policy != null ? new DisplayNameService(session.Slot, policy) : null;
            var flags = session.Slot.FirstLaunch ??= new FirstLaunchFlags();
            var existingProfile = !session.GrantedFirstKit && session.Profile.StarterKitGranted || session.Profile.ExpeditionsEnded > 0;
            if (!flags.ShelterOnboardingComplete && existingProfile)
            {
                flags.ShelterOnboardingComplete = true;
                session.Autosave.MarkDirty("onboarding_existing_profile");
            }

            _session.Loadout.EquippedChanged += OnEquippedChanged;
            _session.Expedition.ExpeditionStarted += OnExpeditionStarted;
            Refresh();
        }

        public ShelterOnboardingStep Step { get; private set; }
        public bool IsComplete => Step == ShelterOnboardingStep.Complete;
        public string PromptText { get; private set; } = string.Empty;
        public string NameError { get; private set; } = string.Empty;
        public bool NeedsDisplayName => _names != null && _names.NeedsDisplayName;
        public bool IsGearEquipped => _session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon) != null && _session.Loadout.GetEquipped(EquippedSlot.Armor) != null;

        public event Action Changed;

        /// <summary>Step 1: validated through the approved policy; invalid input changes nothing and reports the reason.</summary>
        public bool SubmitDisplayName(string raw)
        {
            if (_names == null) return false;
            var result = _names.TrySet(raw);
            if (!result.IsValid)
            {
                NameError = result.Error.ToString();
                Changed?.Invoke();
                return false;
            }

            NameError = string.Empty;
            _session.Autosave.MarkDirty("display_name");
            _session.Autosave.Flush();
            Refresh();
            return true;
        }

        /// <summary>Step 2: the kit was granted once by the session; this only closes the message.</summary>
        public void AcknowledgeStarterKit()
        {
            _kitAcknowledged = true;
            Refresh();
        }

        public void Refresh()
        {
            var flags = _session.Slot.FirstLaunch;
            if (NeedsDisplayName) Step = ShelterOnboardingStep.ChooseDisplayName;
            else if (flags.ShelterOnboardingComplete) Step = ShelterOnboardingStep.Complete;
            else if (_session.GrantedFirstKit && !_kitAcknowledged) Step = ShelterOnboardingStep.ReceiveStarterKit;
            else if (!IsGearEquipped) Step = ShelterOnboardingStep.EquipGear;
            else Step = ShelterOnboardingStep.StartFirstExpedition;

            PromptText = Step switch
            {
                ShelterOnboardingStep.ChooseDisplayName => "Choose your display name (3–16 characters: letters, numbers, spaces, _ and -).",
                ShelterOnboardingStep.ReceiveStarterKit => "Your free Starter Kit is in your loadout: " + KitSummary() + ".",
                ShelterOnboardingStep.EquipGear => "Equip a Primary Weapon and Armor at the Loadout station before you leave.",
                ShelterOnboardingStep.StartFirstExpedition => "Head to the Transit station and start your first expedition.",
                _ => string.Empty
            };
            Changed?.Invoke();
        }

        private string KitSummary()
        {
            var kit = StarterKitService.CreateKit().Select(k => _session.Configs.Resolve(k.item.DefinitionId)?.DisplayName ?? k.item.DefinitionId).Distinct();
            return string.Join(", ", kit);
        }

        private void OnEquippedChanged(EquippedSlot slot, ItemInstance item) => Refresh();

        private void OnExpeditionStarted(ExpeditionState state)
        {
            var flags = _session.Slot.FirstLaunch;
            if (!flags.ShelterOnboardingComplete)
            {
                flags.ShelterOnboardingComplete = true;
                _session.Autosave.MarkDirty("onboarding_complete");
            }

            Refresh();
        }

        public void Dispose()
        {
            _session.Loadout.EquippedChanged -= OnEquippedChanged;
            _session.Expedition.ExpeditionStarted -= OnExpeditionStarted;
        }
    }
}
