using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;

namespace RuinRail.Persistence
{
    /// <summary>Boot-time resolution of an expedition that was open when the application last closed.</summary>
    public sealed class AbandonedExpeditionReport
    {
        public AbandonedExpeditionReport(string transactionId, int runSeed, int expeditionIndex, IReadOnlyList<string> lostInstanceIds)
        {
            TransactionId = transactionId;
            RunSeed = runSeed;
            ExpeditionIndex = expeditionIndex;
            LostInstanceIds = lostInstanceIds;
        }

        public string TransactionId { get; }
        public int RunSeed { get; }
        public int ExpeditionIndex { get; }
        public IReadOnlyList<string> LostInstanceIds { get; }
    }

    /// <summary>
    /// Persists the expedition transaction boundary (113 Save Transactions / 85 Solo Quit rule):
    ///   Start  → the slot records an open transaction (safe loadout already gone) and is saved immediately, so a quit,
    ///            crash or Alt+F4 from this point on can only ever resolve as failure on the next boot.
    ///   Return/Fail → the marker is cleared in the same save as the committed profile.
    /// Idempotent by transaction id: a replayed end event for an already-cleared transaction writes nothing.
    /// </summary>
    public sealed class ExpeditionTransactionRecorder : IDisposable
    {
        private readonly ExpeditionService _service;
        private readonly SaveSlot _slot;
        private readonly Func<string, SaveDiagnostics, SaveError> _persist;
        private bool _disposed;

        public ExpeditionTransactionRecorder(ExpeditionService service, SaveSlot slot, SaveSlotService saves)
            : this(service, slot, saves != null ? (reason, diagnostics) => saves.Save(slot, diagnostics) : throw new ArgumentNullException(nameof(saves)))
        {
        }

        /// <summary>Commits through the autosave so pending safe points and the transaction land in one write.</summary>
        public ExpeditionTransactionRecorder(ExpeditionService service, SaveSlot slot, AutosaveService autosave)
            : this(service, slot, autosave != null ? (reason, diagnostics) => autosave.SaveNow(reason) : throw new ArgumentNullException(nameof(autosave)))
        {
            if (!ReferenceEquals(autosave.Slot, slot)) throw new ArgumentException("The autosave must own the same slot.", nameof(autosave));
        }

        private ExpeditionTransactionRecorder(ExpeditionService service, SaveSlot slot, Func<string, SaveDiagnostics, SaveError> persist)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _slot = slot ?? throw new ArgumentNullException(nameof(slot));
            _persist = persist;
            _service.ExpeditionStarted += OnStarted;
            _service.ExpeditionEnded += OnEnded;
        }

        public SaveError LastSaveError { get; private set; }
        public SaveDiagnostics LastDiagnostics { get; private set; }
        public event Action<SaveError, SaveDiagnostics> SaveFailed;

        private void OnStarted(ExpeditionState state)
        {
            if (!ReferenceEquals(_service.Profile, _slot.Profile))
            {
                throw new InvalidOperationException("The expedition profile must be the save slot's profile; otherwise the persisted commit would be stale.");
            }

            _slot.ActiveExpedition = new ExpeditionMarker
            {
                TransactionId = state.TransactionId,
                RunSeed = state.RunSeed,
                StartedUtcTicks = DateTime.UtcNow.Ticks,
                AtRiskInstanceIds = AllItems(state.Inventory).Select(i => i.InstanceId).ToArray()
            };
            Persist("expedition_start");
        }

        private void OnEnded(ExpeditionSummary summary)
        {
            var marker = _slot.ActiveExpedition;
            if (marker == null || !marker.IsOpen || marker.TransactionId != summary.TransactionId)
            {
                return; // already cleared for this transaction (replayed callback) or not ours: nothing to write
            }

            _slot.ActiveExpedition = new ExpeditionMarker();
            Persist(summary.IsSuccess ? "expedition_extracted" : "expedition_failed");
        }

        private void Persist(string reason)
        {
            var diagnostics = new SaveDiagnostics();
            LastSaveError = _persist(reason, diagnostics);
            LastDiagnostics = diagnostics;
            if (LastSaveError != SaveError.None)
            {
                UnityEngine.Debug.LogError($"Expedition transaction save failed: {LastSaveError}\n{diagnostics}");
                SaveFailed?.Invoke(LastSaveError, diagnostics);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _service.ExpeditionStarted -= OnStarted;
            _service.ExpeditionEnded -= OnEnded;
        }

        private static IEnumerable<ItemInstance> AllItems(PlayerInventory inventory)
        {
            foreach (EquippedSlot slot in Enum.GetValues(typeof(EquippedSlot)))
            {
                var equipped = inventory.GetEquipped(slot);
                if (equipped != null) yield return equipped;
            }

            foreach (var item in inventory.BackpackSlots)
            {
                if (item != null) yield return item;
            }
        }
    }

    /// <summary>
    /// Resolves an expedition marker found in a freshly loaded slot as failure (no mid-run resume in V1). The at-risk
    /// gear and carried coins were never persisted after start, so nothing is restored; permanent XP, skills, banked
    /// coins and storage are untouched. Calling it twice changes nothing.
    /// </summary>
    public static class AbandonedExpeditionResolver
    {
        public static AbandonedExpeditionReport Resolve(SaveSlot slot, SaveDiagnostics diagnostics = null)
        {
            if (slot?.ActiveExpedition == null || !slot.ActiveExpedition.IsOpen) return null;
            var marker = slot.ActiveExpedition;

            if (slot.Profile.SafeLoadout != null && HasAnyItem(slot.Profile.SafeLoadout))
            {
                // Start clears the safe loadout before the marker is ever written; a populated loadout next to an open
                // marker means the document was edited by hand. Nothing here can be trusted as safe: it is the at-risk gear.
                diagnostics?.Warning("expedition.abandoned.loadout", "Open expedition marker with a populated safe loadout; the loadout is treated as at-risk and lost.");
            }

            slot.Profile.SafeLoadout = null;
            slot.Profile.ExpeditionsEnded++;
            slot.ActiveExpedition = new ExpeditionMarker();
            diagnostics?.Info("expedition.abandoned", $"Expedition {marker.TransactionId} (seed {marker.RunSeed}) was open at last shutdown: resolved as failure.");
            return new AbandonedExpeditionReport(marker.TransactionId, marker.RunSeed, slot.Profile.ExpeditionsEnded, marker.AtRiskInstanceIds ?? Array.Empty<string>());
        }

        private static bool HasAnyItem(InventorySnapshot snapshot)
        {
            return (snapshot.Equipped != null && snapshot.Equipped.Any(e => e?.Item != null))
                   || (snapshot.Backpack != null && snapshot.Backpack.Any(e => e?.Item != null));
        }
    }
}
