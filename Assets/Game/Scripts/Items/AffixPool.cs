using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RuinRail.Gameplay.Items
{
    /// <summary>
    /// Item-family-specific set of affixes an equipment definition may roll from
    /// (e.g. heat affixes only in the Blaster pool, charge affixes only in the Bow pool).
    /// </summary>
    [CreateAssetMenu(menuName = "RuinRail/Items/Affix Pool", fileName = "AffixPool_")]
    public sealed class AffixPool : ScriptableObject
    {
        [SerializeField] private string _id;
        [SerializeField] private AffixDefinition[] _affixes = System.Array.Empty<AffixDefinition>();

        public string Id => _id;
        public IReadOnlyList<AffixDefinition> Affixes => _affixes;

        public bool Contains(string affixId)
        {
            return _affixes.Any(a => a != null && a.Id == affixId);
        }
    }
}
