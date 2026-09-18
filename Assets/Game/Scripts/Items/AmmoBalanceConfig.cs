using UnityEngine;

namespace RuinRail.Gameplay.Items
{
    [CreateAssetMenu(fileName = "AmmoBalanceConfig", menuName = "RuinRail/Items/Ammo Balance Config")]
    public sealed class AmmoBalanceConfig : ScriptableObject
    {
        [SerializeField] private int _lightStackLimit = 180;
        [SerializeField] private int _mediumStackLimit = 120;
        [SerializeField] private int _heavyStackLimit = 60;
        [SerializeField] private int _shellsStackLimit = 40;

        public int GetStackLimit(AmmoType type)
        {
            return type switch
            {
                AmmoType.Light => _lightStackLimit,
                AmmoType.Medium => _mediumStackLimit,
                AmmoType.Heavy => _heavyStackLimit,
                AmmoType.Shells => _shellsStackLimit,
                _ => 0
            };
        }
    }
}
