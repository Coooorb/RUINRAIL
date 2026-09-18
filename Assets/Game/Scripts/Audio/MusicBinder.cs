using System;
using System.Collections.Generic;
using RuinRail.Core;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Progression;
using UnityEngine;

namespace RuinRail.Audio
{
    /// <summary>
    /// Routes authoritative game state to the music director: menu / Shelter screens, the expedition's biome per depth,
    /// combat rooms (reported by the dungeon runtime), boss encounters, and the six stingers from their gameplay events.
    /// Read-only over gameplay.
    /// </summary>
    public sealed class MusicBinder : MonoBehaviour
    {
        [SerializeField] private MusicDirector _director;

        private readonly List<Action> _unsubscribe = new();
        private MusicScreen _screen = MusicScreen.MainMenu;
        private Biome _biome = Biome.RuinedMetro;
        private CombatIntensity _intensity = CombatIntensity.Exploration;
        private int _activeCombatRooms;

        public MusicDirector Director => _director;
        public MusicScreen Screen => _screen;
        public Biome Biome => _biome;
        public CombatIntensity Intensity => _intensity;

        public void Configure(MusicDirector director)
        {
            _director = director;
            Apply();
        }

        private void Awake()
        {
            if (_director == null) _director = GetComponent<MusicDirector>();
        }

        private void OnDestroy()
        {
            foreach (var u in _unsubscribe) u();
            _unsubscribe.Clear();
        }

        private void Sub(Action subscribe, Action unsubscribe)
        {
            subscribe();
            _unsubscribe.Add(unsubscribe);
        }

        private void Apply()
        {
            if (_director == null) return;
            _director.SetState(_screen, _biome, _intensity);
            _director.SetAmbience(_screen == MusicScreen.Expedition ? _biome : (Biome?)null);
        }

        // ---- Screens ----

        public void EnterMainMenu() { _screen = MusicScreen.MainMenu; _intensity = CombatIntensity.Exploration; _activeCombatRooms = 0; Apply(); }
        public void EnterShelter() { _screen = MusicScreen.Shelter; _intensity = CombatIntensity.Exploration; _activeCombatRooms = 0; Apply(); }

        /// <summary>
        /// The dungeon scene composed for a running expedition: the biome track and ambience start now. The expedition
        /// started before this scene existed, so ExpeditionStarted has already fired and Attach alone would leave the
        /// Shelter bed playing under the dungeon; the composer calls this explicitly with the current state.
        /// </summary>
        public void EnterExpedition(ExpeditionState state)
        {
            if (state == null) return;
            _screen = MusicScreen.Expedition;
            _biome = state.Biome;
            _intensity = CombatIntensity.Exploration;
            _activeCombatRooms = 0;
            Apply();
        }

        // ---- Expedition ----

        public MusicBinder Attach(ExpeditionService expedition)
        {
            if (expedition == null) return this;
            Action<ExpeditionState> started = s => { _screen = MusicScreen.Expedition; _biome = s.Biome; _intensity = CombatIntensity.Exploration; _activeCombatRooms = 0; Apply(); };
            Action<ExpeditionState> depth = s => { _screen = MusicScreen.Expedition; _biome = s.Biome; _intensity = CombatIntensity.Exploration; _activeCombatRooms = 0; Apply(); };
            Action<ExpeditionSummary> ended = summary =>
            {
                _director?.PlayStinger(summary.IsSuccess ? StingerRole.ExtractionSuccess : StingerRole.ExpeditionFailed);
                EnterShelter();
            };
            Sub(() => { expedition.ExpeditionStarted += started; expedition.DepthEntered += depth; expedition.ExpeditionEnded += ended; },
                () => { expedition.ExpeditionStarted -= started; expedition.DepthEntered -= depth; expedition.ExpeditionEnded -= ended; });
            return this;
        }

        /// <summary>Combat rooms (55): the dungeon runtime reports encounter start / clear; nested reports are counted so overlapping rooms never flip to exploration early.</summary>
        public void ObserveCombatStarted()
        {
            _activeCombatRooms++;
            if (_intensity != CombatIntensity.Boss) { _intensity = CombatIntensity.Combat; Apply(); }
        }

        public void ObserveCombatEnded()
        {
            _activeCombatRooms = Mathf.Max(0, _activeCombatRooms - 1);
            if (_activeCombatRooms == 0 && _intensity == CombatIntensity.Combat) { _intensity = CombatIntensity.Exploration; Apply(); }
        }

        /// <summary>
        /// Boss room: the Boss track (both bosses of the biome share it) from the room's activation — reported by the
        /// dungeon runtime through <see cref="ObserveBossRoomEntered"/>, never from the actor's own BossStarted, which
        /// fires the moment the boss acquires a target at spawn (depth build), long before the player reaches the arena —
        /// stinger + exploration on defeat.
        /// </summary>
        public MusicBinder Attach(BossEncounter encounter)
        {
            if (encounter == null) return this;
            Action<BossEncounter, int> defeated = (_, _) => { _director?.PlayStinger(StingerRole.BossDefeated); _intensity = CombatIntensity.Exploration; _activeCombatRooms = 0; Apply(); };
            Sub(() => encounter.BossDefeated += defeated, () => encounter.BossDefeated -= defeated);
            return this;
        }

        /// <summary>The boss room activated (player inside, doors shut): the Boss bed replaces exploration/combat.</summary>
        public void ObserveBossRoomEntered()
        {
            _intensity = CombatIntensity.Boss;
            Apply();
        }

        /// <summary>Elite encounter stinger when the Elite's encounter starts; the room stays on the Combat track.</summary>
        public MusicBinder Attach(EliteController elite)
        {
            if (elite == null) return this;
            Action<MovesetActorController> started = _ => _director?.PlayStinger(StingerRole.EliteEncounter);
            Sub(() => elite.EncounterStartedEvent += started, () => elite.EncounterStartedEvent -= started);
            return this;
        }

        public MusicBinder Attach(ProgressionService progression)
        {
            if (progression == null) return this;
            Action<int, int> levelled = (_, _) => _director?.PlayStinger(StingerRole.LevelUp);
            Sub(() => progression.LevelledUp += levelled, () => progression.LevelledUp -= levelled);
            return this;
        }

        /// <summary>A Legendary drop plays its stinger the moment it lands (once per pickup).</summary>
        public MusicBinder Attach(WorldItemPickup pickup)
        {
            if (pickup != null && pickup.Item != null && pickup.Item.Rarity == Rarity.Legendary) _director?.PlayStinger(StingerRole.LegendaryDrop);
            return this;
        }
    }
}
