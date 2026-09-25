using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>A player-side component that wants room lifecycle notifications (raises the player's combat events).</summary>
    public interface IRoomEventListener
    {
        void OnCombatRoomEntered(RoomRuntime room);
        void OnCombatRoomCleared(RoomRuntime room, RoomClearedContext context);
    }

    /// <summary>
    /// Runtime lifecycle of one instantiated room (55): detects the first player entry, and for Combat rooms locks every
    /// door, spawns the composed encounter at validated EnemySpawn markers (at least ~5 tiles from the entry point where
    /// possible) and unlocks + clears exactly once when every encounter actor is resolved. Start and other rooms are
    /// simply marked Cleared on entry without ever spawning combat. State is a plain serializable record.
    /// </summary>
    public sealed class RoomRuntime : MonoBehaviour
    {
        /// <summary>47/80: spawn markers closer than this to the entry point are skipped when others exist.</summary>
        public const float MinSpawnDistanceFromEntryTiles = 5f;

        /// <summary>The outer tile ring of every room is wall (door cells included): the encounter interior starts one tile in.</summary>
        public const float WallRingTiles = 1f;

        private RoomRoot _root;
        private readonly RoomRuntimeState _state = new();
        private EncounterPlan _plan;
        private IEnemySpawner _spawner;
        private EncounterRuntime _encounter;
        private IRoomEngagement _engagement;
        private readonly List<RoomDoorLock> _doors = new();
        private readonly List<GameObject> _occupants = new();
        private readonly List<EncounterRuntime> _extraEncounters = new();
        private int _depth = 1;
        private int _partySize = 1;
        private DepthScalingConfig _scalingConfig;
        private bool _scaleSpawns = true;

        public RoomRoot Root => _root;
        /// <summary>What the deterministic prop-dressing pass did to this room (diagnostics; presentation only).</summary>
        public RoomPropDressing.Result Dressing { get; internal set; }
        public RoomRuntimeState State => _state;
        public RoomLifecycleState Lifecycle => _state.State;
        public EncounterPlan Plan => _plan;
        public EncounterRuntime Encounter => _encounter;
        public IReadOnlyList<RoomDoorLock> Doors => _doors;
        public IReadOnlyList<GameObject> Occupants => _occupants;
        public int Depth => _depth;
        public int PartySize => _partySize;
        public bool DoorsLocked => _doors.Count > 0 && _doors.All(d => d.IsLocked);
        public bool HasEncounter => _plan != null && _plan.TotalCount > 0 && _spawner != null;

        /// <summary>False on a client: the room only mirrors the host's replicated state (doors/lifecycle), never runs engagements.</summary>
        public bool IsAuthoritative { get; private set; } = true;

        public void SetAuthoritative(bool authoritative) => IsAuthoritative = authoritative;
        public IRoomEngagement Engagement => _engagement;

        /// <summary>True when entering this room starts something that must resolve before it clears (encounter, boss).</summary>
        public bool HasEngagement => _engagement != null || (_state.RoomType == RoomType.Combat && HasEncounter);

        /// <summary>
        /// A player has genuinely entered this room interior (the inset <see cref="RoomEntryTrigger"/> volume, the same
        /// authority that activates a combat room). Raised once per entry — a revisit raises it again, standing still
        /// inside the room never does. Presentation (room-title reveal, minimap) reads this and nothing else.
        /// </summary>
        public event Action<RoomRuntime, GameObject> PlayerEntered;

        public event Action<RoomRuntime> Activated;
        public event Action<RoomRuntime> StateRestored;
        public event Action<RoomRuntime, RoomClearedContext> Cleared;
        public event Action<RoomRuntime, EnemyController> EnemySpawned;

        public void Configure(RoomRoot root, int nodeId, int depth, int partySize, bool isElite = false)
        {
            _root = root != null ? root : throw new ArgumentNullException(nameof(root));
            _state.NodeId = nodeId;
            _state.RoomId = root.Definition != null ? root.Definition.Id : root.name;
            _state.RoomType = root.Definition != null ? root.Definition.RoomType : RoomType.Combat;
            _state.IsElite = isElite;
            _depth = Math.Max(1, depth);
            _partySize = Math.Max(1, partySize);
            _doors.Clear();
            foreach (var socket in root.GetSockets()) _doors.Add(RoomDoorLock.GetOrAttach(socket));
        }

        /// <summary>The encounter this room runs once on first entry (Combat rooms only; null = no combat).</summary>
        public void SetEncounter(EncounterPlan plan, IEnemySpawner spawner)
        {
            _plan = plan;
            _spawner = spawner;
            _state.EncounterSignature = plan?.Signature;
        }

        /// <summary>Enemy spawner used by extra encounters (events) in rooms without their own encounter.</summary>
        public void SetSpawner(IEnemySpawner spawner) => _spawner = spawner;

        /// <summary>Custom engagement (e.g. a boss fight) that runs once on first entry and gates the clear.</summary>
        public void SetEngagement(IRoomEngagement engagement)
        {
            _engagement = engagement;
        }

        public void SetScaling(DepthScalingConfig config, bool applyToSpawns = true)
        {
            _scalingConfig = config;
            _scaleSpawns = applyToSpawns;
        }

        /// <summary>Restores a serialized state (network sync / reload): a Cleared room never re-runs its encounter.</summary>
        public void RestoreState(RoomRuntimeState state)
        {
            if (state == null) return;
            var wasActive = _state.State == RoomLifecycleState.Active;
            _state.State = state.State;
            _state.EntryCount = state.EntryCount;
            _state.EnemiesSpawned = state.EnemiesSpawned;
            _state.EnemiesDefeated = state.EnemiesDefeated;
            _state.EnemiesRemaining = state.EnemiesRemaining;
            _state.Resolved = new List<string>(state.Resolved ?? new List<string>());
            SetDoorsLocked(_state.State == RoomLifecycleState.Active);
            // A client learns of the activation from the host's state: its own player's room listeners (per-room passives
            // such as Emergency Care / Second Wind) hear the entry the host decided, exactly as the cleared case below.
            if (!IsAuthoritative && !wasActive && _state.State == RoomLifecycleState.Active)
            {
                foreach (var listener in Listeners()) listener.OnCombatRoomEntered(this);
            }

            if (!IsAuthoritative && wasActive && _state.State == RoomLifecycleState.Cleared)
            {
                var context = new RoomClearedContext(_state.NodeId, _state.RoomId, _state.RoomType, _state.IsElite, _depth, _state.EnemiesDefeated);
                Cleared?.Invoke(this, context);
                foreach (var listener in Listeners()) listener.OnCombatRoomCleared(this, context);
            }

            StateRestored?.Invoke(this);
        }

        /// <summary>Entry notification (from the room trigger or the host's movement authority). Idempotent per player.</summary>
        public bool NotifyPlayerEntered(GameObject player)
        {
            if (player == null) return false;
            if (!_occupants.Contains(player))
            {
                _occupants.Add(player);
                // Presentation seam: raised for every genuine entry, including a revisit of an already cleared room.
                PlayerEntered?.Invoke(this, player);
            }

            if (!IsAuthoritative) return false; // clients never advance room state locally (82)
            if (_state.State != RoomLifecycleState.Unentered) return false;

            _state.EntryCount++;
            if (HasEngagement)
            {
                Activate(player);
                return true;
            }

            // Start / Loot / Merchant / Event rooms: visited, nothing to fight (55: Start has no immediate enemies).
            _state.State = RoomLifecycleState.Cleared;
            return true;
        }

        public void NotifyPlayerLeft(GameObject player)
        {
            _occupants.Remove(player);
        }

        private void Activate(GameObject enteringPlayer)
        {
            _state.State = RoomLifecycleState.Active;
            SetDoorsLocked(true);

            if (_engagement == null)
            {
                var points = SpawnPointsFor(enteringPlayer.transform.position);
                _encounter = new EncounterRuntime(_plan, _spawner, points, enteringPlayer.transform);
                _encounter.Spawned += OnEnemySpawned;
                _engagement = new EncounterEngagement(_encounter);
            }

            _engagement.Completed += OnEngagementCompleted;
            Activated?.Invoke(this);
            foreach (var listener in Listeners()) listener.OnCombatRoomEntered(this);
            _engagement.Begin(this, enteringPlayer);
            RefreshEnemiesRemaining();
            // An engagement resolved before the room was entered (e.g. a boss already dead) clears immediately.
            if (_engagement.IsComplete) OnEngagementCompleted(_engagement);
        }

        /// <summary>
        /// True while this room is the kind of place the HUD counts enemies for (91): an active standard Combat
        /// encounter (director encounter or Elite), never a Boss arena and never a non-combat/event room, whatever
        /// event waves may be running there.
        /// </summary>
        public bool ShowsEnemyCount => IsCountedCombat && _state.State == RoomLifecycleState.Active;

        private bool IsCountedCombat => _state.RoomType == RoomType.Combat && _engagement is not BossEngagement;

        /// <summary>
        /// The authoritative number of encounter enemies still to be resolved in this room (see
        /// <see cref="RoomRuntimeState.EnemiesRemaining"/>): 0 when nothing is being counted. On the host it is
        /// recomputed from the encounter's own membership; a client reads the replicated figure.
        /// </summary>
        public int EnemiesRemaining => IsAuthoritative ? RefreshEnemiesRemaining() : _state.EnemiesRemaining;

        private int RefreshEnemiesRemaining()
        {
            var count = 0;
            if (IsAuthoritative && _state.State == RoomLifecycleState.Active && IsCountedCombat)
            {
                switch (_engagement)
                {
                    case EncounterEngagement encounter:
                        count = encounter.Runtime.LivingCount + encounter.Runtime.PendingCount;
                        break;
                    case EliteEngagement elite:
                        count = elite.Encounter != null && elite.Encounter.Elite != null && elite.Encounter.Elite.Health != null && elite.Encounter.Elite.Health.IsAlive && !elite.IsComplete ? 1 : 0;
                        break;
                }
            }

            _state.EnemiesRemaining = count;
            return count;
        }

        /// <summary>Spawns an extra encounter inside this room (events such as Cursed Chest / Supply Signal waves).</summary>
        public EncounterRuntime SpawnAdditionalEncounter(EncounterPlan plan, GameObject aroundPlayer)
        {
            if (plan == null || _spawner == null || aroundPlayer == null) return null;
            var runtime = new EncounterRuntime(plan, _spawner, SpawnPointsFor(aroundPlayer.transform.position), aroundPlayer.transform);
            runtime.Spawned += OnEnemySpawned;
            runtime.Start();
            _extraEncounters.Add(runtime);
            return runtime;
        }

        public void LockDoors() => SetDoorsLocked(true);
        public void UnlockDoors() => SetDoorsLocked(false);

        /// <summary>
        /// The legal encounter interior of this room in world units: the room bounds minus the wall ring, which is also
        /// where the door cells and the combat door blockers are. An encounter actor's collider must stay inside it.
        /// </summary>
        public Rect InteriorWorldBounds => InteriorWorldBoundsOf(_root);

        /// <summary>Pure: the encounter interior (world) of a placed room root.</summary>
        public static Rect InteriorWorldBoundsOf(RoomRoot root)
        {
            if (root == null) return new Rect();
            var min = (Vector2)root.transform.TransformPoint(new Vector3(WallRingTiles * GridConstants.TileWorldSize, WallRingTiles * GridConstants.TileWorldSize, 0f));
            var size = new Vector2(root.Size.x - WallRingTiles * 2f, root.Size.y - WallRingTiles * 2f) * GridConstants.TileWorldSize;
            return new Rect(min, new Vector2(Mathf.Max(0f, size.x), Mathf.Max(0f, size.y)));
        }

        /// <summary>
        /// Makes this room the authoritative owner of an encounter actor: its collider may never leave
        /// <see cref="InteriorWorldBounds"/> until it dies or is despawned. Applied to every actor the room spawns
        /// (encounters, reinforcements, summons, event waves), to its Elite and to its Boss.
        /// </summary>
        public EncounterBounds BindEncounterBounds(GameObject actor) =>
            actor == null ? null : EncounterBounds.Bind(actor, InteriorWorldBounds, _state.RoomId, _state.NodeId);

        /// <summary>Validated EnemySpawn marker centers, preferring those at least ~5 tiles from the entry point.</summary>
        public List<Vector2> SpawnPointsFor(Vector2 entryWorldPosition)
        {
            var markers = _root.GetMarkers(RoomMarkerRole.EnemySpawn)
                .Where(m => m.IsInsideRoom(_root.Size))
                .Select(m => (Vector2)_root.transform.TransformPoint(m.WorldCenter))
                .Where(IsSpawnClear) // never inside a wall, an obstacle, a sealed socket or a door blocker
                .ToList();
            var far = markers.Where(p => Vector2.Distance(p, entryWorldPosition) >= MinSpawnDistanceFromEntryTiles).ToList();
            if (far.Count > 0) return far;
            // Small rooms may have no marker that far away: fall back to the farthest markers first, never to nothing.
            return markers.OrderByDescending(p => Vector2.Distance(p, entryWorldPosition)).ToList();
        }

        private static readonly Collider2D[] SpawnOverlaps = new Collider2D[8];

        /// <summary>A spawn point is usable only when a normal enemy body fits there without touching solid geometry.</summary>
        public static bool IsSpawnClear(Vector2 world)
        {
            var count = Physics2D.OverlapCircle(world, Gameplay.Enemies.DefaultEnemySpawner.BodyRadius, ContactFilter2D.noFilter, SpawnOverlaps);
            for (var i = 0; i < count; i++)
            {
                if (SpawnOverlaps[i] != null && !SpawnOverlaps[i].isTrigger && SpawnOverlaps[i].GetComponentInParent<Gameplay.Combat.EnvironmentObstacle>() != null) return false;
            }

            return true;
        }

        private void OnEnemySpawned(EncounterRuntime runtime, EnemyController enemy)
        {
            _state.EnemiesSpawned++;
            RefreshEnemiesRemaining();
            BindEncounterBounds(enemy.gameObject); // the room that spawned it owns it: no door, open or pending, is an exit
            if (_scaleSpawns) EnemySpawnScaling.Apply(enemy, _depth, _partySize, _scalingConfig);
            enemy.Died += OnEnemyDied;
            EnemySpawned?.Invoke(this, enemy);
        }

        private void OnEnemyDied(EnemyController enemy)
        {
            enemy.Died -= OnEnemyDied;
            _state.EnemiesDefeated++;
            RefreshEnemiesRemaining();
        }

        private void OnEngagementCompleted(IRoomEngagement engagement)
        {
            if (_state.State != RoomLifecycleState.Active) return;
            _engagement.Completed -= OnEngagementCompleted;
            // Completion means every spawned actor is resolved; the last Died callback may still be queued behind this one.
            _state.EnemiesDefeated = Math.Max(_state.EnemiesDefeated, _state.EnemiesSpawned);
            _state.State = RoomLifecycleState.Cleared;
            _state.EnemiesRemaining = 0;
            SetDoorsLocked(false);
            var context = new RoomClearedContext(_state.NodeId, _state.RoomId, _state.RoomType, _state.IsElite, _depth, _state.EnemiesDefeated);
            Cleared?.Invoke(this, context);
            foreach (var listener in Listeners()) listener.OnCombatRoomCleared(this, context);
        }

        private void SetDoorsLocked(bool locked)
        {
            foreach (var door in _doors) door.SetLocked(locked);
        }

        private IEnumerable<IRoomEventListener> Listeners()
        {
            _occupants.RemoveAll(o => o == null);
            return _occupants.SelectMany(o => o.GetComponents<IRoomEventListener>()).ToList();
        }

        private void Update()
        {
            if (_state.State == RoomLifecycleState.Active)
            {
                _engagement?.Tick(Time.deltaTime);
                RefreshEnemiesRemaining();
            }

            for (var i = _extraEncounters.Count - 1; i >= 0; i--)
            {
                _extraEncounters[i].Tick();
                if (_extraEncounters[i].IsComplete) _extraEncounters.RemoveAt(i);
            }
        }
    }

    /// <summary>Something a room runs once on first entry and that must resolve before the room clears.</summary>
    public interface IRoomEngagement
    {
        void Begin(RoomRuntime room, GameObject enteringPlayer);
        void Tick(float deltaTime);
        bool IsComplete { get; }
        event Action<IRoomEngagement> Completed;
    }

    /// <summary>Combat room engagement: the composed encounter, complete when every actor is resolved.</summary>
    public sealed class EncounterEngagement : IRoomEngagement
    {
        private readonly EncounterRuntime _runtime;
        private bool _raised;

        public EncounterEngagement(EncounterRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _runtime.Completed += _ => Raise();
        }

        public EncounterRuntime Runtime => _runtime;
        public bool IsComplete => _runtime.IsComplete;
        public event Action<IRoomEngagement> Completed;

        public void Begin(RoomRuntime room, GameObject enteringPlayer) => _runtime.Start();
        public void Tick(float deltaTime) => _runtime.Tick();

        private void Raise()
        {
            if (_raised) return;
            _raised = true;
            Completed?.Invoke(this);
        }
    }

    /// <summary>
    /// Trigger volume of the room interior; forwards player entries to the runtime.
    ///
    /// The volume is the room bounds inset by <see cref="InteriorMarginTiles"/> on every side, not the bounds
    /// themselves. The outer tile ring is the wall row — including the door cells the <see cref="RoomDoorLock"/>
    /// blocker covers — so a volume that reached the edge fired the moment a player's collider touched the doorway
    /// from the previous room, and the room locked its entry with the player still outside it. With the inset, a
    /// player has to stand inside the room proper (past the door cells, with the whole collider clear of them) before
    /// the room activates, and the lock then closes behind them. The inset volume never overlaps a neighbouring room.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class RoomEntryTrigger : MonoBehaviour
    {
        /// <summary>Wall row (1) + the door blocker row it shares + the player collider's reach (0.4 tiles) rounded up: 2 tiles.</summary>
        public const float InteriorMarginTiles = 2f;

        private RoomRuntime _runtime;

        /// <summary>Pure: the activation volume (local tiles) for a room of this size.</summary>
        public static Rect InteriorVolume(Vector2Int roomSize)
        {
            var margin = Mathf.Min(InteriorMarginTiles, Mathf.Min(roomSize.x, roomSize.y) * 0.5f - 0.5f);
            margin = Mathf.Max(0f, margin);
            var size = new Vector2(roomSize.x - margin * 2f, roomSize.y - margin * 2f) * GridConstants.TileWorldSize;
            var min = new Vector2(margin, margin) * GridConstants.TileWorldSize;
            return new Rect(min, size);
        }

        public void Bind(RoomRuntime runtime) => _runtime = runtime;

        public static RoomEntryTrigger Attach(RoomRuntime runtime)
        {
            var go = new GameObject("RoomEntryTrigger");
            go.transform.SetParent(runtime.transform, false);
            var collider = go.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            var volume = InteriorVolume(runtime.Root.Size);
            collider.size = volume.size;
            collider.offset = volume.center;
            var trigger = go.AddComponent<RoomEntryTrigger>();
            trigger.Bind(runtime);
            return trigger;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var player = other.GetComponentInParent<PlayerMovement>();
            if (player != null && _runtime != null) _runtime.NotifyPlayerEntered(player.gameObject);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            var player = other.GetComponentInParent<PlayerMovement>();
            if (player != null && _runtime != null) _runtime.NotifyPlayerLeft(player.gameObject);
        }
    }
}
