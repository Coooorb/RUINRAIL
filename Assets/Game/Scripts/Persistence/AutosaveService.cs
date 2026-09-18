using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Progression;

namespace RuinRail.Persistence
{
    /// <summary>Last save outcome, exposed for UI/diagnostics; never reports success it did not get from the store.</summary>
    public readonly struct SaveStatus
    {
        public SaveStatus(SaveError error, string reason, DateTime utc, SaveDiagnostics diagnostics)
        {
            Error = error;
            Reason = reason;
            Utc = utc;
            Diagnostics = diagnostics;
        }

        public SaveError Error { get; }
        public bool Succeeded => Error == SaveError.None;
        public string Reason { get; }
        public DateTime Utc { get; }
        public SaveDiagnostics Diagnostics { get; }
    }

    /// <summary>
    /// Safe-point autosave (113 Autosave Points): approved mutations mark the slot dirty with a reason; one write happens
    /// per <see cref="Flush"/> (end of frame via <see cref="AutosaveFlusher"/>, or immediately for transaction commits
    /// through <see cref="SaveNow"/>). Nothing is written while clean, so there is no per-frame I/O, and several
    /// mutations inside one frame (a trader purchase: coins + storage) collapse into one write.
    /// </summary>
    public sealed class AutosaveService
    {
        private readonly SaveSlot _slot;
        private readonly SaveSlotService _saves;
        private readonly List<string> _pendingReasons = new();

        public AutosaveService(SaveSlot slot, SaveSlotService saves)
        {
            _slot = slot ?? throw new ArgumentNullException(nameof(slot));
            _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        }

        public SaveSlot Slot => _slot;
        public bool IsDirty => _pendingReasons.Count > 0;
        public IReadOnlyList<string> PendingReasons => _pendingReasons;
        public int SaveCount { get; private set; }
        public int FailedSaveCount { get; private set; }
        public SaveStatus LastStatus { get; private set; }

        /// <summary>Raised before serialization so live base state (loadout inventory, storage) can be written into the slot.</summary>
        public event Action<SaveSlot> BeforeSave;
        public event Action<SaveStatus> Saved;
        public event Action<SaveStatus> SaveFailed;

        /// <summary>Marks an approved safe point; the write happens on the next Flush.</summary>
        public void MarkDirty(string reason)
        {
            _pendingReasons.Add(string.IsNullOrEmpty(reason) ? "unspecified" : reason);
        }

        /// <summary>Writes once if anything is pending. Returns None when clean or committed; the store error otherwise.</summary>
        public SaveError Flush()
        {
            if (!IsDirty) return SaveError.None;
            var reason = string.Join("+", _pendingReasons);
            return Commit(reason);
        }

        /// <summary>Immediate write for transaction commits (expedition start/end); pending reasons are folded in.</summary>
        public SaveError SaveNow(string reason)
        {
            _pendingReasons.Add(string.IsNullOrEmpty(reason) ? "unspecified" : reason);
            return Commit(string.Join("+", _pendingReasons));
        }

        private SaveError Commit(string reason)
        {
            BeforeSave?.Invoke(_slot);
            var diagnostics = new SaveDiagnostics();
            var error = _saves.Save(_slot, diagnostics);
            var status = new SaveStatus(error, reason, DateTime.UtcNow, diagnostics);
            LastStatus = status;
            if (error == SaveError.None)
            {
                _pendingReasons.Clear();
                SaveCount++;
                Saved?.Invoke(status);
            }
            else
            {
                // Stay dirty: the next safe point retries, and the failure is visible until then.
                FailedSaveCount++;
                UnityEngine.Debug.LogError($"Autosave failed ({reason}): {error}\n{diagnostics}");
                SaveFailed?.Invoke(status);
            }

            return error;
        }
    }

    /// <summary>
    /// Subscribes the documented safe points to an <see cref="AutosaveService"/>: storage changes, trader
    /// purchase/sale/refresh, skill spend/respec, base upgrades and banked-coin movements. Expedition start/end are
    /// committed by <see cref="ExpeditionTransactionRecorder"/>. Every parameter is optional so partial base scenes bind
    /// what they have.
    /// </summary>
    public sealed class AutosaveBinder : IDisposable
    {
        private readonly List<Action> _unsubscribe = new();

        public AutosaveBinder(AutosaveService autosave, Storage storage = null, TraderService trader = null, WorkshopService workshop = null,
            ProgressionService progression = null, CoinWallet bankedWallet = null)
        {
            if (autosave == null) throw new ArgumentNullException(nameof(autosave));

            if (storage != null)
            {
                Action h = () => autosave.MarkDirty("storage");
                storage.Changed += h;
                _unsubscribe.Add(() => storage.Changed -= h);
            }

            if (trader != null)
            {
                Action<TraderOffer> bought = _ => autosave.MarkDirty("trader_buy");
                Action<Gameplay.Items.ItemInstance, int> sold = (_, _) => autosave.MarkDirty("trader_sell");
                Action refreshed = () => autosave.MarkDirty("trader_refresh");
                trader.Bought += bought;
                trader.Sold += sold;
                trader.Refreshed += refreshed;
                _unsubscribe.Add(() => { trader.Bought -= bought; trader.Sold -= sold; trader.Refreshed -= refreshed; });
            }

            if (workshop != null)
            {
                Action<int> s = _ => autosave.MarkDirty("workshop_storage_upgrade");
                Action<int> t = _ => autosave.MarkDirty("workshop_trader_upgrade");
                workshop.StorageUpgraded += s;
                workshop.TraderUpgraded += t;
                _unsubscribe.Add(() => { workshop.StorageUpgraded -= s; workshop.TraderUpgraded -= t; });
            }

            if (progression != null)
            {
                Action h = () => autosave.MarkDirty("skills");
                progression.SkillsChanged += h;
                _unsubscribe.Add(() => progression.SkillsChanged -= h);
            }

            if (bankedWallet != null)
            {
                if (bankedWallet.Domain != CoinDomain.Banked) throw new ArgumentException("Only the Banked wallet is a safe point; carried coins are never saved.", nameof(bankedWallet));
                Action<CoinReceipt> h = _ => autosave.MarkDirty("banked_coins");
                bankedWallet.Changed += h;
                _unsubscribe.Add(() => bankedWallet.Changed -= h);
            }
        }

        public void Dispose()
        {
            foreach (var u in _unsubscribe) u();
            _unsubscribe.Clear();
        }
    }
}
