using System;
using System.Collections.Generic;
using RuinRail.Core;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using UnityEngine;

namespace RuinRail.Gameplay.Events
{
    /// <summary>57: exactly the six approved MVP event types.</summary>
    public enum DungeonEventKind
    {
        CursedChest,
        LockedVault,
        BrokenMachine,
        SupplySignal,
        MedicalStation,
        WeaponCache
    }

    /// <summary>Stable detail codes a result can carry, so callers never match on a loose string literal.</summary>
    public static class DungeonEventDetails
    {
        /// <summary>The event presents a choice: activation opened nothing by itself and the chooser must pick (57.6 Weapon Cache).</summary>
        public const string ChoiceRequired = "choice_required";
    }

    public enum DungeonEventPhase
    {
        /// <summary>Not yet used; may be activated.</summary>
        Available,

        /// <summary>Activated and waiting for its encounter/timer to resolve.</summary>
        InProgress,

        /// <summary>Resolved with its reward paid out (or its consumable use spent). Terminal.</summary>
        Completed,

        /// <summary>Resolved without reward (encounter lost, repair failed). Terminal.</summary>
        Failed
    }

    public enum DungeonEventOutcome
    {
        /// <summary>Nothing happened (not activatable, already resolved, payment refused). State unchanged.</summary>
        None,
        Started,
        Success,
        Failed,
        InsufficientFunds,
        Unavailable
    }

    /// <summary>The player activating an event: their Carried wallet (paid events) and their backpack (choice events).</summary>
    public sealed class EventActor
    {
        public EventActor(CoinWallet carriedWallet, IItemContainer backpack = null, string participantId = "local", GameObject gameObject = null, IMedicalPatient patient = null)
        {
            Wallet = carriedWallet;
            Backpack = backpack;
            ParticipantId = participantId;
            GameObject = gameObject;
            Patient = patient;
        }

        public CoinWallet Wallet { get; }
        public IItemContainer Backpack { get; }
        public string ParticipantId { get; }
        public GameObject GameObject { get; }

        /// <summary>The actor's own health for healing purchases (null when the actor cannot be healed).</summary>
        public IMedicalPatient Patient { get; }
    }

    /// <summary>
    /// Deterministic inputs of one event instance: RunSeed + Depth + event index. Loot comes from the Loot stream
    /// (event salt keeps it apart from chests and the merchant), encounters from the Encounter stream (event room
    /// index space), so an event replays identically on host and in tests and never perturbs room generation (114).
    /// </summary>
    public sealed class DungeonEventContext
    {
        public const int LootSalt = 0x4556; // "EV"
        public const int EncounterRoomIndexBase = 100000;
        public const int EncounterWaveStride = 16;

        public DungeonEventContext(int runSeed, int depth, int eventIndex, int partySize = 1, Biome biome = Biome.RuinedMetro, IReadOnlyCollection<AmmoType> usefulAmmoTypes = null)
        {
            RunSeed = runSeed;
            Depth = Math.Max(1, depth);
            EventIndex = eventIndex;
            PartySize = Math.Max(1, partySize);
            Biome = biome;
            UsefulAmmoTypes = usefulAmmoTypes;
        }

        public int RunSeed { get; }
        public int Depth { get; }
        public int EventIndex { get; }
        public int PartySize { get; }
        public Biome Biome { get; }
        public IReadOnlyCollection<AmmoType> UsefulAmmoTypes { get; }

        public ulong Seed(int salt) => SeededRandom.MixSeed(RunSeed, Depth, (int)RngStream.Loot, LootSalt, EventIndex, salt);

        /// <summary>A private random stream for one decision family of this event (repair roll, choice picks…).</summary>
        public SeededRandom Random(int salt) => new(Seed(salt));

        public LootContext ForLoot(LootQuality quality, int salt = 0)
        {
            var seed = Seed(salt);
            return new LootContext(Depth, quality, new SeededRandom(seed), PartySize, seed, UsefulAmmoTypes);
        }

        /// <summary>Encounter context of this event; wave > 0 derives a distinct but reproducible follow-up wave.</summary>
        public EncounterContext ForEncounter(IReadOnlyList<string> roomTags = null, int spawnMarkerCount = 0, int wave = 0)
        {
            return new EncounterContext(RunSeed, Depth, PartySize, Biome, EncounterRoomIndexBase + EventIndex * EncounterWaveStride + wave, roomTags, spawnMarkerCount);
        }
    }

    /// <summary>Immutable outcome record of one activation/resolution step; the terminal one is kept as the event's Result.</summary>
    public sealed class DungeonEventResult
    {
        public DungeonEventResult(DungeonEventKind kind, int eventIndex, DungeonEventOutcome outcome, int coinsSpent = 0, LootResult loot = null, string detail = null)
            : this(kind, eventIndex, outcome, coinsSpent, loot, detail, outcome == DungeonEventOutcome.Success || outcome == DungeonEventOutcome.Failed)
        {
        }

        private DungeonEventResult(DungeonEventKind kind, int eventIndex, DungeonEventOutcome outcome, int coinsSpent, LootResult loot, string detail, bool terminal)
        {
            Kind = kind;
            EventIndex = eventIndex;
            Outcome = outcome;
            CoinsSpent = coinsSpent;
            Loot = loot;
            Detail = detail;
            IsTerminal = terminal;
        }

        public DungeonEventKind Kind { get; }
        public int EventIndex { get; }
        public DungeonEventOutcome Outcome { get; }
        public int CoinsSpent { get; }
        public LootResult Loot { get; }
        public string Detail { get; }
        /// <summary>True when this result ends the event; multi-use services (Medical Station) return non-terminal successes.</summary>
        public bool IsTerminal { get; }

        public DungeonEventResult AsNonTerminal() => new(Kind, EventIndex, Outcome, CoinsSpent, Loot, Detail, false);
    }

    /// <summary>
    /// Gameplay contract of a dungeon event. Activation is idempotent per phase: a resolved event returns None and
    /// changes nothing, whatever the caller. Completed fires exactly once with the terminal result so the room
    /// lifecycle (doors, room-clear bookkeeping) can react.
    /// </summary>
    public interface IDungeonEvent
    {
        DungeonEventKind Kind { get; }
        int EventIndex { get; }
        DungeonEventPhase Phase { get; }
        DungeonEventResult Result { get; }

        /// <summary>Carried Coins the activation costs (0 for free events).</summary>
        int CostCoins { get; }

        bool CanActivate(EventActor actor);
        DungeonEventResult Activate(EventActor actor);

        event Action<IDungeonEvent, DungeonEventResult> Completed;
    }

    /// <summary>Where an event's shared-world reward goes (57 Co-op: rewards spawn into the shared world).</summary>
    public interface IRewardDeliverer
    {
        void Deliver(LootResult loot);
    }

    /// <summary>Delivers rewards as world pickups through the loot spawner at a fixed origin (the event's position).</summary>
    public sealed class LootSpawnerDeliverer : IRewardDeliverer
    {
        private readonly LootSpawner _spawner;
        private readonly Func<Vector2> _origin;
        private readonly Transform _parent;

        public LootSpawnerDeliverer(LootSpawner spawner, Func<Vector2> origin, Transform parent = null)
        {
            _spawner = spawner != null ? spawner : throw new ArgumentNullException(nameof(spawner));
            _origin = origin ?? throw new ArgumentNullException(nameof(origin));
            _parent = parent;
        }

        public IReadOnlyList<GameObject> LastSpawned { get; private set; } = Array.Empty<GameObject>();

        public void Deliver(LootResult loot)
        {
            LastSpawned = _spawner.Spawn(loot, _origin(), _parent);
        }
    }

    /// <summary>Shared once-only state machine for events; subclasses implement the activation/resolution steps.</summary>
    public abstract class DungeonEventBase : IDungeonEvent
    {
        protected DungeonEventBase(DungeonEventKind kind, DungeonEventContext context)
        {
            Kind = kind;
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public DungeonEventKind Kind { get; }
        public DungeonEventContext Context { get; }
        public int EventIndex => Context.EventIndex;
        public DungeonEventPhase Phase { get; private set; } = DungeonEventPhase.Available;
        public DungeonEventResult Result { get; private set; }
        public virtual int CostCoins => 0;

        public event Action<IDungeonEvent, DungeonEventResult> Completed;

        public bool IsResolved => Phase == DungeonEventPhase.Completed || Phase == DungeonEventPhase.Failed;
        public bool IsSuccess => Phase == DungeonEventPhase.Completed;

        public virtual bool CanActivate(EventActor actor)
        {
            return Phase == DungeonEventPhase.Available && actor != null && (CostCoins <= 0 || (actor.Wallet != null && actor.Wallet.CanAfford(CostCoins)));
        }

        public DungeonEventResult Activate(EventActor actor)
        {
            if (Phase != DungeonEventPhase.Available)
            {
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.None, detail: "already_used");
            }

            if (actor == null)
            {
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.Unavailable, detail: "no_actor");
            }

            if (CostCoins > 0 && (actor.Wallet == null || !actor.Wallet.CanAfford(CostCoins)))
            {
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.InsufficientFunds);
            }

            // Phase flips before any side effect so a re-entrant call can never activate twice.
            Phase = DungeonEventPhase.InProgress;
            var result = OnActivate(actor);
            if (result.IsTerminal)
            {
                Finish(result);
            }
            else if (result.Outcome != DungeonEventOutcome.Started)
            {
                // Activation refused inside the step (e.g. a debit that failed): back to Available, nothing changed.
                Phase = DungeonEventPhase.Available;
            }

            return result;
        }

        /// <summary>Runs the activation: return Started (async resolution), Success/Failed (terminal) or a refusal.</summary>
        protected abstract DungeonEventResult OnActivate(EventActor actor);

        /// <summary>Restores a persisted terminal phase (room revisit / sync) without replaying side effects or events.</summary>
        public void RestoreResolved(bool success)
        {
            if (IsResolved) return;
            Phase = success ? DungeonEventPhase.Completed : DungeonEventPhase.Failed;
            Result = new DungeonEventResult(Kind, EventIndex, success ? DungeonEventOutcome.Success : DungeonEventOutcome.Failed, detail: "restored");
        }

        /// <summary>Terminal transition; ignored once resolved.</summary>
        protected bool Finish(DungeonEventResult result)
        {
            if (IsResolved) return false;
            Phase = result.Outcome == DungeonEventOutcome.Success ? DungeonEventPhase.Completed : DungeonEventPhase.Failed;
            Result = result;
            Completed?.Invoke(this, result);
            return true;
        }

        protected DungeonEventResult Success(int coinsSpent = 0, LootResult loot = null, string detail = null) => new(Kind, EventIndex, DungeonEventOutcome.Success, coinsSpent, loot, detail);
        protected DungeonEventResult Failed(int coinsSpent = 0, string detail = null) => new(Kind, EventIndex, DungeonEventOutcome.Failed, coinsSpent, null, detail);
        protected DungeonEventResult Started(int coinsSpent = 0, string detail = null) => new(Kind, EventIndex, DungeonEventOutcome.Started, coinsSpent, null, detail);
    }
}
