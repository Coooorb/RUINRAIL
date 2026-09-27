using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.UI.Inventory;
using RuinRail.UI.SecureRelay;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The Secure Relay's runtime seam (57.7). The relay answers the Interact press by asking for its screen; the screen
    /// belongs to whoever pressed on their own machine and shows that member's carried slots. The transfer itself is one
    /// call: solo and host run the relay's own Secure into this profile's Shelter Storage and write the save at once. A
    /// co-op member first moves the unit out of its run into a <see cref="SecureRelayEscrow"/> persisted in its save, then
    /// asks the host, which decides the single use once (and remembers it per participant and transaction). The escrow
    /// resolves exactly once and only the host's acceptance puts it in Storage: accepted → Storage; refused → back into
    /// the run; no verdict (link lost) → the same request is re-sent after the reconnect and resolves from the verdict the
    /// host recorded; a run that ends before any acceptance reached this member returns the unit to the run's own fate
    /// (BaseSession). Coins never move, and the death/wipe transactions are untouched.
    /// </summary>
    public sealed partial class ExpeditionScene
    {
        public const string SecuredWorldKeySuffix = "_secured";

        private SecureRelayViewModel _relay;
        private DungeonEventInteractable _openRelay;
        private bool _relayOverlay;
        private readonly List<(SecureRelayEvent relay, WorldObjectVisual visual)> _relays = new();
        /// <summary>Per escrow: when this scene may (re)send its request. Empty after a reconnect's recomposition → re-send at once.</summary>
        private readonly Dictionary<string, float> _relayResendAt = new(StringComparer.Ordinal);
        public const float RelayAnswerWaitSeconds = 10f;
        public const float RelayRetrySeconds = 3f;
        public const int RelayResendLimitPerScene = 10;

        public SecureRelayViewModel SecureRelay => _relay;
        public SecureRelayView SecureRelayView { get; private set; }

        /// <summary>Transfers this peer committed into its own Storage (diagnostics / proof).</summary>
        public int RelayTransfers { get; private set; }

        /// <summary>Co-op member: escrowed units returned to the run on the host's refusal, and requests re-sent (diagnostics / proof).</summary>
        public int RelayReturns { get; private set; }
        public int RelayResends { get; private set; }

        /// <summary>Co-op member: this run's Secure Relay escrows not settled yet, whatever their state (persisted in the save).</summary>
        public IReadOnlyList<SecureRelayEscrow> PendingRelayEscrows
        {
            get
            {
                var escrows = _app?.Menu.Session?.Slot.RelayEscrow;
                var run = _expedition?.State?.TransactionId;
                return escrows == null || run == null ? Array.Empty<SecureRelayEscrow>() : escrows.Where(e => e != null && e.RunTransactionId == run).ToList();
            }
        }

        private bool RelayOpen => _relay != null && _relay.IsOpen;

        private void ComposeSecureRelayUi(IWorldPause worldPause, bool isCoop)
        {
            _relay = new SecureRelayViewModel();
            _relay.ConfigurePause(worldPause, isCoop);
            _disposables.Add(_relay);
            _relay.Changed += RefreshSecureRelayUi;
            SecureRelayView = SecureRelayView.Create(_relay);
        }

        private void AttachSecureRelay(RoomRuntime runtime)
        {
            var binding = runtime.GetComponent<RoomContentBinding>();
            if (binding == null || binding.Event == null || binding.EventInstance is not SecureRelayEvent relay) return;
            binding.Event.ChoiceRequested += OnSecureRelayRequested;
            _relays.Add((relay, binding.Event.GetComponent<WorldObjectVisual>()));
        }

        private string LocalParticipantId => _rig?.Player != null ? DungeonEventInteractable.ActorFor(_rig.Player)?.ParticipantId : null;

        private void OnSecureRelayRequested(DungeonEventInteractable interactable, EventActor actor)
        {
            if (_relay == null || interactable == null || actor == null || interactable.Event is not SecureRelayEvent relay) return;
            // A joined member's press replayed on the host never opens the host's screen (that member opened its own).
            if (Mode != CoopRunMode.Solo && actor.GameObject != null && _rig?.Player != null && actor.GameObject != _rig.Player) return;
            if (_relay.IsOpen || (_pause?.IsOpen ?? false) || (_inventory?.IsOpen ?? false) || (_merchant?.IsOpen ?? false) || (_weaponCache?.IsOpen ?? false)) return;
            var receiver = actor.GameObject != null ? actor.GameObject.GetComponent<PlayerLootReceiver>() : null;
            _openRelay = interactable;
            _relay.Bind(relay, actor, _expedition?.State?.Inventory, receiver != null ? receiver.CarriedContainers : null, _app.Menu.Session?.Storage, _app.Configs.Resolve, _app.Specials);
            if (Mode == CoopRunMode.Client)
            {
                var node = NodeOf(interactable);
                _relay.Commit = null;
                _relay.Request = instanceId => OpenRelayEscrow(relay, node, instanceId);
                _relay.AwaitingHost = () => PendingRelayEscrows.Any(e => e.State == SecureRelayEscrowState.Pending);
            }
            else
            {
                _relay.Request = null;
                _relay.AwaitingHost = null;
                _relay.Commit = instanceId => CommitRelayTransfer(relay, instanceId);
            }

            _relay.Open();
        }

        /// <summary>Solo / host: moves one unit of the carried item into this profile's Shelter Storage through the relay and writes the save.</summary>
        private SecureRelayResult CommitRelayTransfer(SecureRelayEvent relay, string instanceId)
        {
            var session = _app.Menu.Session;
            var participant = LocalParticipantId;
            var receiver = _rig?.Player != null ? _rig.Player.GetComponent<PlayerLootReceiver>() : null;
            if (session == null || relay == null || receiver == null || string.IsNullOrEmpty(participant) || _expedition == null || !_expedition.IsExpeditionActive)
                return SecureRelayResult.Refused(SecureRelayRefusal.TransferFailed, instanceId);

            var result = relay.Secure(participant, receiver.CarriedContainers, instanceId, new SecureRelayStorageTarget(session.Storage));
            if (!result.Success) return result;

            RelayTransfers++;
            // The unit is now Storage's and no longer the run's; the save carries it so a crash, quit or later loss of the
            // run cannot take it back. A failed write keeps the autosave dirty and retries at the next safe point.
            session.SaveNow("secure_relay");
            _coopClient?.MarkInventoryDirty();
            RefreshInventoryUi();
            _relay?.Refresh();
            var definition = _app.Configs.Resolve(result.DefinitionId);
            Notify("ITEM SECURED: " + (definition != null ? definition.DisplayName.ToUpperInvariant() : result.DefinitionId) + " SENT TO SHELTER STORAGE", false);
            SyncRelayVisuals();
            return result;
        }

        // ---------------------------------------------------------------- co-op member: escrow → request → verdict

        /// <summary>
        /// Co-op member's SECURE: the unit leaves the run into an escrow that is on disk before the request goes out, so no
        /// later link loss, crash or run end can lose it or put a granted unit back into the run. One request at a time.
        /// </summary>
        private bool OpenRelayEscrow(SecureRelayEvent relay, int node, string instanceId)
        {
            var session = _app.Menu.Session;
            var participant = LocalParticipantId;
            var receiver = _rig?.Player != null ? _rig.Player.GetComponent<PlayerLootReceiver>() : null;
            if (_coopClient == null || session == null || receiver == null || string.IsNullOrEmpty(participant) || _expedition == null || !_expedition.IsExpeditionActive) return false;
            if (PendingRelayEscrows.Count > 0) return false;
            if (SecureRelayEscrows.Open(relay, participant, receiver.CarriedContainers, instanceId, session.Storage, out var escrow) != SecureRelayRefusal.None) return false;
            escrow.TransactionId = Guid.NewGuid().ToString("N");
            escrow.RunTransactionId = _expedition.State.TransactionId;
            escrow.Depth = _expedition.State.Depth;
            escrow.RoomNode = node;
            session.Slot.RelayEscrow.Add(escrow);
            if (session.SaveNow("secure_relay_escrow") != RuinRail.Persistence.SaveError.None)
            {
                // Not durable: nothing is sent and the unit goes straight back where it was.
                session.Slot.RelayEscrow.Remove(escrow);
                SecureRelayEscrows.Return(escrow, receiver.CarriedContainers);
                AfterRelayInventoryChange();
                return false;
            }

            SendRelayEscrow(escrow);
            AfterRelayInventoryChange();
            return true;
        }

        private void SendRelayEscrow(SecureRelayEscrow escrow)
        {
            _coopClient.SendRelaySecure(escrow.RoomNode, escrow.SourceInstanceId, escrow.TransactionId, escrow.Depth);
            _relayResendAt[escrow.TransactionId] = Time.realtimeSinceStartup + RelayAnswerWaitSeconds;
        }

        /// <summary>
        /// Co-op member, every frame: a Pending escrow this scene has not sent yet (the run recomposed after a reconnect) or
        /// whose answer is overdue is re-sent under its own transaction id — the host replays a verdict it already gave. An
        /// Accepted escrow Storage could not take, or a Refused one the run had no room for, is retried in place.
        /// </summary>
        private void TickRelayEscrow()
        {
            if (Mode != CoopRunMode.Client || _coopClient == null || CoopGameplayHeld || _expedition == null || !_expedition.IsExpeditionActive) return;
            var pending = PendingRelayEscrows;
            if (pending.Count == 0) return;
            var now = Time.realtimeSinceStartup;
            foreach (var escrow in pending)
            {
                if (escrow.State != SecureRelayEscrowState.Pending)
                {
                    if (_relayResendAt.TryGetValue(escrow.TransactionId, out var retry) && now < retry) continue;
                    _relayResendAt[escrow.TransactionId] = now + RelayRetrySeconds;
                    TrySettleRelayEscrow(escrow);
                    continue;
                }

                if (_relayResendAt.TryGetValue(escrow.TransactionId, out var at) && now < at) continue;
                if (RelayResends >= RelayResendLimitPerScene) return; // the run's end releases it into Storage
                RelayResends++;
                SendRelayEscrow(escrow);
            }
        }

        /// <summary>
        /// Co-op member: the host's verdict. Only a Pending escrow takes one (a duplicate or late verdict changes nothing);
        /// the decided state is saved before anything else happens, so a crash cannot turn it into something else.
        /// </summary>
        private void OnClientRelayResult(RelayResultMessage result)
        {
            var session = _app.Menu.Session;
            var escrow = result == null || session == null ? null : session.Slot.RelayEscrow.FirstOrDefault(e => e != null && e.TransactionId == result.TransactionId);
            if (escrow == null || escrow.State != SecureRelayEscrowState.Pending) return;
            if (!result.Final)
            {
                _relayResendAt[escrow.TransactionId] = Time.realtimeSinceStartup + RelayRetrySeconds; // "not now": ask again
                return;
            }

            var refusal = result.Accepted ? SecureRelayRefusal.None : Enum.TryParse<SecureRelayRefusal>(result.Refusal, out var parsed) ? parsed : SecureRelayRefusal.TransferFailed;
            escrow.State = result.Accepted ? SecureRelayEscrowState.Accepted : SecureRelayEscrowState.Refused;
            var definition = escrow.Unit != null ? _app.Configs.Resolve(escrow.Unit.DefinitionId) : null;
            var name = definition != null ? definition.DisplayName.ToUpperInvariant() : escrow.Unit?.DefinitionId;
            if (result.Accepted)
            {
                RelayTransfers++;
                RelayOf(escrow)?.RestoreSecured(escrow.ParticipantId, escrow.Unit?.DefinitionId);
                Notify("ITEM SECURED: " + name + " SENT TO SHELTER STORAGE", false);
            }
            else
            {
                RelayReturns++;
                Notify("SECURE RELAY: " + (refusal == SecureRelayRefusal.AlreadySecured ? "ALREADY USED" : "REFUSED") + " — " + name + " KEPT", true);
            }

            if (!TrySettleRelayEscrow(escrow)) session.SaveNow("secure_relay_verdict"); // the decision is kept; the move is retried
            _relay?.ReportRemoteResult(result.Accepted, refusal);
        }

        /// <summary>Carries out a decided escrow: Accepted → Storage, Refused → back into the run. Saves when it settled.</summary>
        private bool TrySettleRelayEscrow(SecureRelayEscrow escrow)
        {
            var session = _app.Menu.Session;
            var receiver = _rig?.Player != null ? _rig.Player.GetComponent<PlayerLootReceiver>() : null;
            if (session == null || escrow == null) return false;
            var settled = escrow.State switch
            {
                SecureRelayEscrowState.Accepted => SecureRelayEscrows.Release(escrow, session.Storage),
                SecureRelayEscrowState.Refused => receiver != null && SecureRelayEscrows.Return(escrow, receiver.CarriedContainers),
                _ => false
            };
            if (!settled) return false;
            session.Slot.RelayEscrow.Remove(escrow);
            _relayResendAt.Remove(escrow.TransactionId);
            session.SaveNow(escrow.State == SecureRelayEscrowState.Accepted ? "secure_relay" : "secure_relay_refused");
            AfterRelayInventoryChange();
            SyncRelayVisuals();
            return true;
        }

        /// <summary>This member's copy of the escrow's relay, when the run still stands on that depth.</summary>
        private SecureRelayEvent RelayOf(SecureRelayEscrow escrow) =>
            escrow != null && _expedition?.State != null && _expedition.State.Depth == escrow.Depth && Rooms != null && Rooms.TryGetValue(escrow.RoomNode, out var room) && room != null
                ? room.GetComponent<RoomContentBinding>()?.EventInstance as SecureRelayEvent
                : null;

        private void AfterRelayInventoryChange()
        {
            _coopClient?.MarkInventoryDirty();
            RefreshInventoryUi();
            _relay?.Refresh();
        }

        /// <summary>The relay a member has used reads as spent on that member's screen: the secured-state sprite (or the used tint).</summary>
        private void SyncRelayVisuals()
        {
            if (_relays.Count == 0) return;
            var participant = LocalParticipantId;
            foreach (var (relay, visual) in _relays)
            {
                if (visual == null || relay == null || !relay.HasSecured(participant)) continue;
                var key = WorldObjectArt.EventKey(DungeonEventKind.SecureRelay.ToString()) + SecuredWorldKeySuffix;
                if (visual.Key == key) continue;
                if (WorldObjectArt.Resolve(key) != null) visual.Show(key);
                else visual.SetTint(WorldObjectVisual.ResolvedTint);
            }
        }

        private void CloseSecureRelayForDepthChange()
        {
            if (_relay != null && _relay.IsOpen) _relay.Close();
            _openRelay = null;
            _relays.Clear();
        }

        private void RefreshSecureRelayUi()
        {
            SyncOverlay(ref _relayOverlay, _relay.IsOpen);
            if (_menuInput == null || SecureRelayView == null) return;
            var list = SecureRelayView.FocusList;
            if (_relay.IsOpen) { if (!_menuInput.Stack.Contains(list)) _menuInput.Stack.Push(list); }
            else _menuInput.Stack.Remove(list);
        }
    }
}
