using RuinRail.Core.Input;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    /// <summary>
    /// The one player composition (movement, aiming, dash, impact, stats, health, loot receiver, interactor, pickup
    /// attraction). Local players get a real PlayerInput; remote replicas get the null reader on every consumer, so
    /// network players and solo players are the same object graph and never a parallel implementation.
    /// </summary>
    public static class PlayerEntityBuilder
    {
        public sealed class Options
        {
            public string Name = "Player";
            public bool IsLocal = true;
            public IPlayerInputReader InputReader;
            public PlayerBalanceConfig BalanceConfig;
            public GlobalStatCapsConfig Caps;
            public Vector2 Position;
            /// <summary>Party life roster (84): null = solo semantics (0 HP is death, never Downed).</summary>
            public PartyLifeRoster LifeRoster;
            /// <summary>Party participant id for revive/loot authority lookups; null = the object name.</summary>
            public string ParticipantId;
        }

        public static GameObject Build(Options options)
        {
            options ??= new Options();
            var go = new GameObject(options.Name);
            go.transform.position = options.Position;
            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            var collider = go.AddComponent<CircleCollider2D>();
            collider.radius = 0.4f;
            CombatLayers.TagPlayerBody(go);
            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var health = go.AddComponent<HealthComponent>();
            if (options.BalanceConfig != null) health.SetMaxHealth(options.BalanceConfig.MaxHealth);

            IPlayerInputReader reader;
            if (options.IsLocal)
            {
                reader = options.InputReader ?? go.AddComponent<PlayerInput>().Reader;
            }
            else
            {
                reader = NullPlayerInputReader.Instance;
            }

            var movement = go.AddComponent<PlayerMovement>();
            movement.SetInputReader(reader);
            if (options.BalanceConfig != null) movement.SetBalanceConfig(options.BalanceConfig);
            var aiming = go.AddComponent<PlayerAiming>();
            aiming.SetInputReader(reader);
            var dash = go.AddComponent<PlayerDash>();
            dash.SetInputReader(reader);
            if (options.BalanceConfig != null) dash.SetBalanceConfig(options.BalanceConfig);
            go.AddComponent<PlayerImpactReceiver>();
            var life = go.AddComponent<PlayerLifeStateComponent>();
            life.SetInputReader(reader);
            if (options.BalanceConfig != null) life.SetBalanceConfig(options.BalanceConfig);
            life.SetParticipantId(options.ParticipantId ?? options.Name);
            life.SetRoster(options.LifeRoster);
            movement.RefreshMovementOverrides();
            go.AddComponent<ReviveProtection>();
            var reviver = go.AddComponent<PlayerReviver>();
            reviver.SetInputReader(reader);
            if (options.BalanceConfig != null) reviver.SetBalanceConfig(options.BalanceConfig);
            go.AddComponent<DeadSpectatorFollow>().SetInputReader(reader);
            go.AddComponent<PlayerInteractor>().SetInputReader(reader);
            go.AddComponent<PlayerLootReceiver>();
            go.AddComponent<PickupAttractor>();
            var binder = go.AddComponent<PlayerStatsBinder>();
            binder.Configure(options.Caps, options.BalanceConfig);
            // Health was composed first: pick up the dash iFrames and revive protection added after it.
            health.RefreshInvulnerabilityStates();
            return go;
        }

        /// <summary>True when every input consumer on the object reads the null reader (remote replica).</summary>
        public static bool IsInputIsolated(GameObject player)
        {
            return player != null && player.GetComponent<PlayerInput>() == null;
        }
    }
}
