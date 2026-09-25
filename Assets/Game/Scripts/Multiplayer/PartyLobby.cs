using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RuinRail.Core;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;

namespace RuinRail.Networking
{
    /// <summary>81 Ready Flow rules for one loadout: resolvable definitions, unique instance ids, a weapon to fight with.</summary>
    public static class LoadoutValidation
    {
        public static bool IsValid(InventorySnapshot snapshot, Func<string, ItemDefinition> resolve, out string reason)
        {
            reason = null;
            if (snapshot == null)
            {
                reason = "no loadout";
                return false;
            }

            var entries = (snapshot.Equipped ?? Array.Empty<InventorySnapshot.Entry>()).Concat(snapshot.Backpack ?? Array.Empty<InventorySnapshot.Entry>()).Where(e => e.Item != null).ToList();
            var ids = entries.Select(e => e.Item.InstanceId).ToList();
            if (ids.Count != ids.Distinct().Count())
            {
                reason = "duplicate instance ids";
                return false;
            }

            foreach (var entry in entries)
            {
                if (resolve == null || resolve(entry.Item.DefinitionId) == null)
                {
                    reason = $"unknown item '{entry.Item.DefinitionId}'";
                    return false;
                }
            }

            var hasWeapon = (snapshot.Equipped ?? Array.Empty<InventorySnapshot.Entry>()).Any(e => e.Item != null && (e.Slot == (int)EquippedSlot.PrimaryWeapon || e.Slot == (int)EquippedSlot.SecondaryWeapon) && resolve(e.Item.DefinitionId) is WeaponDefinition);
            if (!hasWeapon)
            {
                reason = "no weapon equipped";
                return false;
            }

            return true;
        }

        /// <summary>Stable digest of a loadout (slots, instance ids, quantities): any change flips it.</summary>
        public static string Fingerprint(InventorySnapshot snapshot)
        {
            if (snapshot == null) return "none";
            var sb = new StringBuilder();
            foreach (var e in (snapshot.Equipped ?? Array.Empty<InventorySnapshot.Entry>()).OrderBy(e => e.Slot))
            {
                if (e.Item != null) sb.Append('E').Append(e.Slot).Append(':').Append(e.Item.InstanceId).Append('x').Append(e.Item.Quantity).Append(';');
            }

            foreach (var e in (snapshot.Backpack ?? Array.Empty<InventorySnapshot.Entry>()).OrderBy(e => e.Slot))
            {
                if (e.Item != null) sb.Append('B').Append(e.Slot).Append(':').Append(e.Item.InstanceId).Append('x').Append(e.Item.Quantity).Append(';');
            }

            return DungeonFingerprints.Hash(sb.ToString());
        }
    }

    public sealed class LobbyMember
    {
        public LobbyMember(ulong clientId, string participantId, bool isHost)
        {
            ClientId = clientId;
            ParticipantId = participantId;
            IsHost = isHost;
        }

        public ulong ClientId { get; }
        public string ParticipantId { get; }
        public bool IsHost { get; }
        public bool IsReady { get; internal set; }
        public bool HasValidLoadout { get; internal set; }
        public string LoadoutFingerprint { get; internal set; } = "none";
        public string InvalidReason { get; internal set; }
        public InventorySnapshot Loadout { get; internal set; }
    }

    public enum LobbyStartError
    {
        None,
        NotHost,
        NoMembers,
        NotAllReady,
        InvalidLoadout,
        AlreadyStarted
    }

    /// <summary>Captured exactly once when the host starts: party size, seed, biome and every member's risk snapshot.</summary>
    [Serializable]
    public sealed class ExpeditionStartSnapshot
    {
        public string StartTransactionId;
        public int RunSeed;
        public int Biome;
        public int PartySize;
        public List<MemberRisk> Members = new();

        [Serializable]
        public sealed class MemberRisk
        {
            public ulong ClientId;
            public string ParticipantId;
            public string LoadoutFingerprint;
            public InventorySnapshot Loadout;
        }
    }

    /// <summary>
    /// 81 Ready Flow on the current join-code session (host-authoritative): every member prepares a loadout and marks
    /// Ready; any loadout change after Ready clears that Ready; only the host starts, and only when every member is
    /// Ready with a valid loadout. The start captures party size and each member's risk snapshot exactly once and
    /// locks loadouts, so late mutations can never diverge safe/risk ownership. Duplicate start requests return the
    /// same snapshot. Party limit 3 (80).
    /// </summary>
    public sealed class PartyLobby
    {
        private readonly List<LobbyMember> _members = new();
        private readonly Func<string, ItemDefinition> _resolve;
        private readonly ulong _hostClientId;

        public PartyLobby(ulong hostClientId, Func<string, ItemDefinition> resolveDefinition)
        {
            _hostClientId = hostClientId;
            _resolve = resolveDefinition ?? throw new ArgumentNullException(nameof(resolveDefinition));
        }

        public IReadOnlyList<LobbyMember> Members => _members;
        public int MaxMembers => SessionRequest.MaxPartySize;
        public ExpeditionStartSnapshot StartSnapshot { get; private set; }
        public bool HasStarted => StartSnapshot != null;
        public bool AllReady => _members.Count > 0 && _members.All(m => m.IsReady && m.HasValidLoadout);
        public int StartRequests { get; private set; }

        public event Action<LobbyMember> MemberChanged;
        public event Action<ExpeditionStartSnapshot> Started;

        public LobbyMember Get(ulong clientId) => _members.FirstOrDefault(m => m.ClientId == clientId);

        /// <summary>
        /// The expedition the party started has ended (extracted or failed): the party reopens for the next run. The
        /// members stay, their Ready is cleared (81: a new run needs a new unanimous Ready over the current loadouts),
        /// and the start snapshot is released so TryStart can produce a fresh one. Host decision; the host-side session
        /// calls it from the expedition-ended event.
        /// </summary>
        public void Reopen()
        {
            if (!HasStarted) return;
            StartSnapshot = null;
            foreach (var member in _members)
            {
                if (!member.IsReady) continue;
                member.IsReady = false;
                MemberChanged?.Invoke(member);
            }
        }

        public LobbyMember Join(ulong clientId, string participantId)
        {
            var existing = Get(clientId);
            if (existing != null) return existing;
            if (HasStarted) throw new InvalidOperationException("The expedition has started; the party is closed.");
            if (_members.Count >= MaxMembers) throw new InvalidOperationException($"Party is full ({MaxMembers}).");
            var member = new LobbyMember(clientId, participantId, clientId == _hostClientId);
            _members.Add(member);
            MemberChanged?.Invoke(member);
            return member;
        }

        public bool Leave(ulong clientId)
        {
            var member = Get(clientId);
            if (member == null) return false;
            _members.Remove(member);
            MemberChanged?.Invoke(member);
            return true;
        }

        /// <summary>A member (re)submits its loadout; a changed loadout clears Ready. Locked once the expedition started.</summary>
        public bool SetLoadout(ulong clientId, InventorySnapshot loadout)
        {
            var member = Get(clientId);
            if (member == null || HasStarted) return false;
            var fingerprint = LoadoutValidation.Fingerprint(loadout);
            var changed = fingerprint != member.LoadoutFingerprint;
            member.Loadout = loadout;
            member.LoadoutFingerprint = fingerprint;
            member.HasValidLoadout = LoadoutValidation.IsValid(loadout, _resolve, out var reason);
            member.InvalidReason = reason;
            if (changed && member.IsReady) member.IsReady = false;
            MemberChanged?.Invoke(member);
            return true;
        }

        public bool SetReady(ulong clientId, bool ready)
        {
            var member = Get(clientId);
            if (member == null || HasStarted) return false;
            if (ready && !member.HasValidLoadout) return false;
            if (member.IsReady == ready) return true;
            member.IsReady = ready;
            MemberChanged?.Invoke(member);
            return true;
        }

        /// <summary>Host start with the first biome drawn from the run seed (56/129: uniform at depth 1).</summary>
        public LobbyStartError TryStart(ulong requesterClientId, int runSeed, out ExpeditionStartSnapshot snapshot) => TryStart(requesterClientId, runSeed, BiomeSelector.SelectFirst(runSeed), out snapshot);

        /// <summary>Host-only authoritative start; idempotent — a second request returns the captured snapshot.</summary>
        public LobbyStartError TryStart(ulong requesterClientId, int runSeed, Biome biome, out ExpeditionStartSnapshot snapshot)
        {
            StartRequests++;
            snapshot = StartSnapshot;
            if (HasStarted) return LobbyStartError.AlreadyStarted;
            if (requesterClientId != _hostClientId) return LobbyStartError.NotHost;
            if (_members.Count == 0) return LobbyStartError.NoMembers;
            if (_members.Any(m => !m.HasValidLoadout)) return LobbyStartError.InvalidLoadout;
            if (_members.Any(m => !m.IsReady)) return LobbyStartError.NotAllReady;

            snapshot = new ExpeditionStartSnapshot
            {
                StartTransactionId = Guid.NewGuid().ToString("N"),
                RunSeed = runSeed,
                Biome = (int)biome,
                PartySize = _members.Count
            };
            foreach (var member in _members.OrderBy(m => m.ClientId))
            {
                snapshot.Members.Add(new ExpeditionStartSnapshot.MemberRisk { ClientId = member.ClientId, ParticipantId = member.ParticipantId, LoadoutFingerprint = member.LoadoutFingerprint, Loadout = member.Loadout });
            }

            StartSnapshot = snapshot;
            Started?.Invoke(snapshot);
            return LobbyStartError.None;
        }
    }

    /// <summary>
    /// Mirrors a local player's inventory into the lobby: any equip/backpack change resubmits the loadout, which clears
    /// that player's Ready (81 "Changing loadout after Ready automatically clears that player's Ready state").
    /// </summary>
    public sealed class LobbyLoadoutBinder : IDisposable
    {
        private readonly PartyLobby _lobby;
        private readonly ulong _clientId;
        private readonly PlayerInventory _inventory;

        public LobbyLoadoutBinder(PartyLobby lobby, ulong clientId, PlayerInventory inventory)
        {
            _lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
            _clientId = clientId;
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _inventory.EquippedChanged += OnEquippedChanged;
            _inventory.BackpackChanged += Submit;
            Submit();
        }

        public int Submissions { get; private set; }

        public void Submit()
        {
            Submissions++;
            _lobby.SetLoadout(_clientId, _inventory.ToSnapshot());
        }

        private void OnEquippedChanged(EquippedSlot slot, ItemInstance item) => Submit();

        public void Dispose()
        {
            _inventory.EquippedChanged -= OnEquippedChanged;
            _inventory.BackpackChanged -= Submit;
        }
    }

    /// <summary>
    /// Each peer starts its own expedition transaction from the replicated start snapshot exactly once (idempotent by
    /// start transaction id), so a re-delivered start never spawns a second expedition on any machine.
    /// </summary>
    public sealed class ExpeditionStartCoordinator
    {
        private readonly HashSet<string> _applied = new(StringComparer.Ordinal);

        public int Applied { get; private set; }
        public int Ignored { get; private set; }

        public ExpeditionState Apply(ExpeditionStartSnapshot snapshot, ExpeditionService expedition, PlayerProfile profile) => Apply(snapshot, expedition, profile, null);

        /// <summary>
        /// A co-op client applies the host's start with the participant id the host assigned it, so this peer's
        /// transaction, roster entry and vote id are the ones every other peer uses for it.
        /// </summary>
        public ExpeditionState Apply(ExpeditionStartSnapshot snapshot, ExpeditionService expedition, PlayerProfile profile, string transactionId)
        {
            if (snapshot == null || expedition == null || profile == null) return null;
            if (!_applied.Add(snapshot.StartTransactionId))
            {
                Ignored++;
                return expedition.State;
            }

            Applied++;
            return expedition.Start(profile, snapshot.RunSeed, (Biome)snapshot.Biome, snapshot.PartySize, transactionId);
        }
    }
}
