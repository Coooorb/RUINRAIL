using System;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Weapons
{
    /// <summary>
    /// Reusable heat resource for Blasters (24_WEAPON_CLASSES / 33_WEAPON_CATALOG): shots add heat; cooling starts after
    /// a delay; reaching max heat triggers an Overheated lockout during which firing is blocked. Ticks independently of
    /// equip state so a holstered blaster keeps cooling. Plain C# so HUD/accessory modifiers can read it without Unity coupling.
    /// </summary>
    public sealed class BlasterHeatState
    {
        // Absorbs float accumulation error so e.g. 0.5s + 0.1s of ticks fully spends a 0.6s timer.
        private const float TimerEpsilon = 1e-4f;

        public BlasterHeatState(float maxHeat, float coolingRatePerSecond, float coolingDelaySeconds, float overheatLockoutSeconds)
        {
            MaxHeat = Mathf.Max(1f, maxHeat);
            CoolingRatePerSecond = Mathf.Max(0f, coolingRatePerSecond);
            CoolingDelaySeconds = Mathf.Max(0f, coolingDelaySeconds);
            OverheatLockoutSeconds = Mathf.Max(0f, overheatLockoutSeconds);
        }

        public float MaxHeat { get; }

        /// <summary>The authored cooling rate; <see cref="EffectiveCoolingRatePerSecond"/> is what actually cools.</summary>
        public float CoolingRatePerSecond { get; }

        /// <summary>
        /// Live Blaster Cooling Rate multiplier from the wielder's stat pipeline (1 = authored). Held as a multiplier
        /// rather than rebuilt into a new state so equipping or removing a cooling source never discards accumulated
        /// heat, an overheat lockout or a cooling delay.
        /// </summary>
        public float CoolingRateMultiplier
        {
            get => _coolingRateMultiplier;
            set => _coolingRateMultiplier = Mathf.Max(0f, value);
        }

        private float _coolingRateMultiplier = 1f;

        /// <summary>Heat shed per second after the capped Blaster Cooling Rate bonus.</summary>
        public float EffectiveCoolingRatePerSecond => CoolingRatePerSecond * _coolingRateMultiplier;
        public float CoolingDelaySeconds { get; }
        public float OverheatLockoutSeconds { get; }

        public float Heat { get; private set; }

        /// <summary>Owner-side convergence: adopts the host's heat value (never above MaxHeat).</summary>
        public void ApplyAuthoritativeHeat(float heat)
        {
            Heat = UnityEngine.Mathf.Clamp(heat, 0f, MaxHeat);
        }
        public float HeatFraction => Heat / MaxHeat;
        public bool IsOverheated { get; private set; }
        public float LockoutRemaining { get; private set; }
        public float CoolingDelayRemaining { get; private set; }
        public bool IsCooling => Heat > 0f && CoolingDelayRemaining <= 0f;

        public event Action Overheated;
        public event Action LockoutEnded;

        public bool CanFire => !IsOverheated;

        /// <summary>Applies one shot's heat. Returns false (and adds nothing) while overheated.</summary>
        public bool AddShotHeat(float heatPerShot)
        {
            if (IsOverheated)
            {
                return false;
            }

            Heat = Mathf.Min(MaxHeat, Heat + Mathf.Max(0f, heatPerShot));
            CoolingDelayRemaining = CoolingDelaySeconds;

            if (Heat >= MaxHeat)
            {
                IsOverheated = true;
                LockoutRemaining = OverheatLockoutSeconds;
                Overheated?.Invoke();
            }

            return true;
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            if (IsOverheated)
            {
                LockoutRemaining = Mathf.Max(0f, LockoutRemaining - deltaTime);
                if (LockoutRemaining <= TimerEpsilon)
                {
                    LockoutRemaining = 0f;
                    IsOverheated = false;
                    LockoutEnded?.Invoke();
                }
            }

            if (CoolingDelayRemaining > 0f)
            {
                var consumed = Mathf.Min(CoolingDelayRemaining, deltaTime);
                CoolingDelayRemaining -= consumed;
                deltaTime -= consumed;
                if (CoolingDelayRemaining <= TimerEpsilon)
                {
                    CoolingDelayRemaining = 0f;
                }

                if (deltaTime <= 0f)
                {
                    return;
                }
            }

            Heat = Mathf.Max(0f, Heat - EffectiveCoolingRatePerSecond * deltaTime);
        }
    }
}
