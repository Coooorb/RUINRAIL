using System;
using System.Collections.Generic;
using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Presentation.Animation;
using UnityEngine;

namespace RuinRail.Audio
{
    /// <summary>
    /// Read-only subscriptions from gameplay events to authored audio events (art/105 coverage rule). Every hook only
    /// calls <see cref="AudioService.Play"/>/<see cref="AudioService.PlayLoop"/>; nothing here changes gameplay, and a
    /// telegraph cue never replaces the visual telegraph. Loops (blaster heat, revive channel) are stopped on their own
    /// end conditions and on depth/expedition transitions.
    /// </summary>
    public sealed class GameplayAudioBinder : MonoBehaviour
    {
        /// <summary>V1 FINAL (TASK 179): heat fraction where the rising-energy loop starts and where the overheat warning fires (art/105: warning before Overheat).</summary>
        public const float HeatRisingFraction = 0.5f;
        public const float OverheatWarningFraction = 0.8f;

        [SerializeField] private AudioService _audio;

        private readonly List<Action> _unsubscribe = new();
        private readonly List<WeaponWatch> _weapons = new();
        private readonly List<EnemyController> _enemies = new();
        private readonly Dictionary<EnemyController, EnemyState> _enemyStates = new();
        private readonly Dictionary<PlayerLifeStateComponent, LoopHandle> _reviveLoops = new();
        private PartyLifeRoster _roster;

        private sealed class WeaponWatch
        {
            public WeaponVisualDriver Driver;
            public int Shots;
            public int Swings;
            public WeaponVisualState State;
            public bool Warned;
            public bool Overheated;
            public LoopHandle HeatLoop;
        }

        public AudioService Audio => _audio;
        public int Hooks { get; private set; }

        public void Configure(AudioService audio)
        {
            _audio = audio;
            UiSoundBus.Raised -= OnUiSound;
            UiSoundBus.Raised += OnUiSound;
        }

        private void Awake()
        {
            if (_audio == null) _audio = GetComponent<AudioService>();
            UiSoundBus.Raised -= OnUiSound;
            UiSoundBus.Raised += OnUiSound;
        }

        private void OnDestroy()
        {
            UiSoundBus.Raised -= OnUiSound;
            foreach (var u in _unsubscribe) u();
            _unsubscribe.Clear();
        }

        private void Play(string id, Vector2? at = null) => _audio?.Play(id, at);

        private void Sub(Action subscribe, Action unsubscribe)
        {
            subscribe();
            _unsubscribe.Add(unsubscribe);
            Hooks++;
        }

        // ---- UI ----

        private void OnUiSound(UiSound sound) => Play(sound switch
        {
            UiSound.Navigate => AudioEventIds.UiNavigate,
            UiSound.Confirm => AudioEventIds.UiConfirm,
            UiSound.Cancel => AudioEventIds.UiCancel,
            UiSound.Failure => AudioEventIds.UiFailure,
            UiSound.Purchase => AudioEventIds.UiPurchase,
            _ => AudioEventIds.UiNavigate
        });

        // ---- Weapons (state read through the visual driver's counters and weapon state) ----

        public GameplayAudioBinder Attach(WeaponVisualDriver driver)
        {
            if (driver == null) return this;
            _weapons.Add(new WeaponWatch { Driver = driver, Shots = driver.ShotsShown, Swings = driver.SwingsShown, State = driver.State });
            Hooks++;
            return this;
        }

        /// <summary>The dry click of a firearm with an empty magazine and nothing to reload (authored weapon.dry_fire).</summary>
        public GameplayAudioBinder Attach(RangedWeapon weapon)
        {
            if (weapon == null) return this;
            Action<RangedWeapon> dry = w => Play(AudioEventIds.DryFire, w.transform.position);
            Sub(() => weapon.DryFired += dry, () => weapon.DryFired -= dry);
            return this;
        }

        public GameplayAudioBinder Attach(Projectile projectile)
        {
            if (projectile == null) return this;
            Action<Projectile, Vector2> exploded = (_, p) => Play(AudioEventIds.RocketExplosion, p);
            Sub(() => projectile.Exploded += exploded, () => projectile.Exploded -= exploded);
            return this;
        }

        // ---- Enemies ----

        public GameplayAudioBinder Attach(MovesetActorController actor)
        {
            if (actor == null) return this;
            var isBoss = actor is BossController;
            Action<MovesetActorController, EnemyAttackDefinition> telegraph = (a, _) => Play(isBoss ? AudioEventIds.BossTelegraph : AudioEventIds.EliteTelegraph, a.transform.position);
            Action<MovesetActorController> died = a => Play(AudioEventIds.EnemyDeath, a.transform.position);
            Sub(() => { actor.AttackTelegraphStarted += telegraph; actor.Died += died; }, () => { actor.AttackTelegraphStarted -= telegraph; actor.Died -= died; });
            if (!isBoss) Play(AudioEventIds.EliteSpawn, actor.transform.position);
            if (actor is BossController boss)
            {
                Action<BossController, int> phase = (b, _) => Play(AudioEventIds.BossPhase, b.transform.position);
                Sub(() => boss.PhaseChanged += phase, () => boss.PhaseChanged -= phase);
            }

            return this;
        }

        public GameplayAudioBinder Attach(EnemyController enemy)
        {
            if (enemy == null) return this;
            _enemies.Add(enemy);
            _enemyStates[enemy] = enemy.State;
            Action<EnemyController> died = e => Play(AudioEventIds.EnemyDeath, e.transform.position);
            Sub(() => enemy.Died += died, () => enemy.Died -= died);
            return this;
        }

        public GameplayAudioBinder Attach(HealthComponent health, bool isPlayer)
        {
            if (health == null) return this;
            Action<int> damaged = _ => Play(isPlayer ? AudioEventIds.PlayerHit : AudioEventIds.EnemyHit, health.transform.position);
            Sub(() => health.Damaged += damaged, () => health.Damaged -= damaged);
            if (isPlayer)
            {
                Action<int> healed = _ => Play(AudioEventIds.Heal, health.transform.position);
                Sub(() => health.Healed += healed, () => health.Healed -= healed);
            }

            return this;
        }

        public GameplayAudioBinder Attach(ImpactReceiver receiver)
        {
            if (receiver == null) return this;
            Action<ImpactReceiver> staggered = r => Play(AudioEventIds.EnemyStagger, r.transform.position);
            Sub(() => receiver.Staggered += staggered, () => receiver.Staggered -= staggered);
            return this;
        }

        // ---- Player ----

        public GameplayAudioBinder Attach(RuinRail.Gameplay.Combat.Weapons.Specials.LegendarySpecialController special)
        {
            if (special == null) return this;
            Action<RuinRail.Gameplay.Combat.Weapons.Specials.ILegendarySpecial> fired = _ => Play(AudioEventIds.LegendarySpecial, special.transform.position);
            Sub(() => special.SpecialFired += fired, () => special.SpecialFired -= fired);
            return this;
        }

        public GameplayAudioBinder Attach(PlayerDash dash)
        {
            if (dash == null) return this;
            Action<PlayerDash, Vector2> started = (d, _) => Play(AudioEventIds.PlayerDash, d.transform.position);
            Sub(() => dash.DashStarted += started, () => dash.DashStarted -= started);
            return this;
        }

        /// <summary>Downed / death / revive-complete from the authoritative roster; the revive channel loop follows the arbiter.</summary>
        public GameplayAudioBinder Attach(PartyLifeRoster roster)
        {
            if (roster == null) return this;
            _roster = roster;
            Action<PlayerLifeStateComponent, PlayerLifeState, PlayerLifeState> changed = (member, from, to) =>
            {
                var at = (Vector2)member.transform.position;
                if (to == PlayerLifeState.Downed) Play(AudioEventIds.PlayerDowned, at);
                else if (to == PlayerLifeState.Dead) Play(AudioEventIds.PlayerDeath, at);
                else if (from == PlayerLifeState.Downed && to == PlayerLifeState.Alive) Play(AudioEventIds.PlayerReviveComplete, at);
            };
            Sub(() => roster.MemberStateChanged += changed, () => roster.MemberStateChanged -= changed);
            return this;
        }

        public GameplayAudioBinder Attach(ConsumableEffectRunner effects, Transform owner)
        {
            if (effects == null) return this;
            Action<ConsumableDefinition> used = _ => Play(AudioEventIds.ConsumableUse, owner != null ? (Vector2?)owner.position : null);
            Sub(() => effects.BuffStarted += used, () => effects.BuffStarted -= used);
            return this;
        }

        // ---- Loot / world ----

        public GameplayAudioBinder Attach(WorldItemPickup pickup)
        {
            if (pickup == null) return this;
            if (pickup.Item != null) Play(AudioEventIds.DropEventFor(pickup.Item.Rarity), pickup.transform.position);
            Action<WorldItemPickup> picked = p => Play(AudioEventIds.PickupItem, p.transform.position);
            Sub(() => pickup.PickedUp += picked, () => pickup.PickedUp -= picked);
            return this;
        }

        public GameplayAudioBinder Attach(CoinPickup coins)
        {
            if (coins == null) return this;
            Action<CoinPickup, int> collected = (c, _) => Play(AudioEventIds.PickupCoins, c.transform.position);
            Sub(() => coins.Collected += collected, () => coins.Collected -= collected);
            return this;
        }

        public GameplayAudioBinder Attach(SupplyChest chest)
        {
            if (chest == null) return this;
            Action<SupplyChest, LootResult> opened = (c, _) => Play(AudioEventIds.ChestOpen, c.transform.position);
            Sub(() => chest.Opened += opened, () => chest.Opened -= opened);
            return this;
        }

        /// <summary>
        /// A combat door shutting or opening at a world position (world.door.open is the authored door-mechanism cue and
        /// plays for both transitions); the dungeon composer forwards each room door's lock transitions here because
        /// the audio assembly does not see the dungeon runtime.
        /// </summary>
        public void PlayDoor(Vector2 at) => Play(AudioEventIds.DoorOpen, at);

        /// <summary>Hazard damage on the player (authored player.hazard).</summary>
        public void PlayHazard(Vector2 at) => Play(AudioEventIds.HazardDamage, at);

        public GameplayAudioBinder Attach(DungeonMerchantInteractable merchant)
        {
            if (merchant == null) return this;
            Action<DungeonMerchantInteractable, GameObject> opened = (m, _) => Play(AudioEventIds.MerchantOpen, m.transform.position);
            Sub(() => merchant.Opened += opened, () => merchant.Opened -= opened);
            return this;
        }

        /// <summary>Depth / expedition transitions clean every loop; the transit departure cue plays on each new depth.</summary>
        public GameplayAudioBinder Attach(ExpeditionService expedition)
        {
            if (expedition == null) return this;
            Action<ExpeditionState> depth = _ => { _audio?.StopAllLoops(); ClearLoopState(); Play(AudioEventIds.TransitDepart); };
            Action<ExpeditionSummary> ended = _ => { _audio?.StopAllLoops(); ClearLoopState(); };
            Sub(() => { expedition.DepthEntered += depth; expedition.ExpeditionEnded += ended; }, () => { expedition.DepthEntered -= depth; expedition.ExpeditionEnded -= ended; });
            return this;
        }

        private void ClearLoopState()
        {
            foreach (var w in _weapons) w.HeatLoop = null;
            _reviveLoops.Clear();
        }

        // ---- Polled state (weapons, normal-enemy telegraphs, revive channels) ----

        public void Tick(float deltaTime)
        {
            foreach (var w in _weapons)
            {
                var d = w.Driver;
                if (d == null) continue;
                var weapon = d.ActiveWeapon;
                if (d.ShotsShown > w.Shots)
                {
                    var id = weapon switch
                    {
                        RangedWeapon r when r.Definition != null => AudioEventIds.FireEventFor(r.Definition.WeaponClass),
                        BlasterWeapon => AudioEventIds.FireBlaster,
                        BowWeapon => AudioEventIds.BowRelease,
                        _ => AudioEventIds.FirePistol
                    };
                    Play(id, d.transform.position);
                }

                if (d.SwingsShown > w.Swings)
                {
                    var id = weapon is MeleeWeapon m && m.Definition != null && m.Definition.WeaponClass == WeaponClassSpear ? AudioEventIds.SpearThrust : AudioEventIds.KnifeSlash;
                    Play(id, d.transform.position);
                }

                if (d.State != w.State)
                {
                    if (d.State == WeaponVisualState.Reloading) Play(AudioEventIds.Reload, d.transform.position);
                    if (d.State == WeaponVisualState.Charging) Play(AudioEventIds.BowDraw, d.transform.position);
                    if (w.State == WeaponVisualState.Charging && d.State == WeaponVisualState.Firing) { /* release already covered by the shot counter */ }
                }

                if (weapon is BlasterWeapon)
                {
                    var heat = d.HeatFraction;
                    var overheated = d.State == WeaponVisualState.Overheated;
                    if (heat >= HeatRisingFraction && w.HeatLoop == null && !overheated) w.HeatLoop = _audio?.PlayLoop(AudioEventIds.BlasterHeatRising, d.transform);
                    if ((heat < HeatRisingFraction || overheated) && w.HeatLoop != null) { _audio?.StopLoop(w.HeatLoop); w.HeatLoop = null; }
                    if (heat >= OverheatWarningFraction && !w.Warned && !overheated) { Play(AudioEventIds.BlasterOverheatWarning, d.transform.position); w.Warned = true; }
                    if (heat < HeatRisingFraction) w.Warned = false;
                    if (overheated && !w.Overheated) Play(AudioEventIds.BlasterOverheat, d.transform.position);
                    if (!overheated && w.Overheated) { Play(AudioEventIds.BlasterVent, d.transform.position); w.Warned = false; }
                    w.Overheated = overheated;
                }
                else if (w.HeatLoop != null) { _audio?.StopLoop(w.HeatLoop); w.HeatLoop = null; }

                w.Shots = d.ShotsShown;
                w.Swings = d.SwingsShown;
                w.State = d.State;
            }

            for (var i = _enemies.Count - 1; i >= 0; i--)
            {
                var enemy = _enemies[i];
                if (enemy == null) { _enemies.RemoveAt(i); continue; }
                var previous = _enemyStates[enemy];
                if (enemy.State == EnemyState.Telegraph && previous != EnemyState.Telegraph) Play(AudioEventIds.EnemyTelegraph, enemy.transform.position);
                _enemyStates[enemy] = enemy.State;
            }

            if (_roster != null)
            {
                foreach (var member in _roster.Members)
                {
                    if (member == null) continue;
                    var channel = member.State == PlayerLifeState.Downed ? _roster.Revives.ChannelOn(member) : null;
                    var active = channel != null;
                    var has = _reviveLoops.TryGetValue(member, out var loop) && loop != null && loop.IsPlaying;
                    if (active && !has) { var h = _audio?.PlayLoop(AudioEventIds.PlayerReviveStart, member.transform); if (h != null) _reviveLoops[member] = h; else Play(AudioEventIds.PlayerReviveStart, member.transform.position); }
                    else if (!active && has) { _audio?.StopLoop(loop); _reviveLoops.Remove(member); }
                }
            }
        }

        private static readonly RuinRail.Gameplay.Items.WeaponClass WeaponClassSpear = RuinRail.Gameplay.Items.WeaponClass.Spear;

        private void Update() => Tick(Time.deltaTime);
    }
}
