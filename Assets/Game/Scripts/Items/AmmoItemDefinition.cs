using UnityEngine;

namespace RuinRail.Gameplay.Items
{
    [CreateAssetMenu(fileName = "AmmoItemDefinition", menuName = "RuinRail/Items/Ammo Item Definition")]
    public sealed class AmmoItemDefinition : ItemDefinition
    {
        [SerializeField] private AmmoType _ammoType;

        public AmmoType AmmoType => _ammoType;
    }
}
