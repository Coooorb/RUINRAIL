using RuinRail.Gameplay.Combat;
using UnityEngine;

namespace RuinRail.Tests
{
    internal sealed class TestDamageableTarget : MonoBehaviour, IDamageable
    {
        public int HitCount { get; private set; }
        public int LastDamageAmount { get; private set; }
        public bool ReturnValueOnApply { get; set; } = true;

        public bool TryApplyDamage(DamageRequest request)
        {
            HitCount++;
            LastDamageAmount = request.Amount;
            return ReturnValueOnApply;
        }
    }
}
