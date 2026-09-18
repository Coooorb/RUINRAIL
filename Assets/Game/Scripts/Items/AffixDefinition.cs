using UnityEngine;

namespace RuinRail.Gameplay.Items
{
    /// <summary>
    /// One random affix. Values are integer percent points and always positive (random affixes are positive-only).
    /// </summary>
    [CreateAssetMenu(menuName = "RuinRail/Items/Affix Definition", fileName = "Affix_")]
    public sealed class AffixDefinition : ScriptableObject
    {
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField] private AffixStat _stat;
        [SerializeField, Min(1)] private int _minValue = 1;
        [SerializeField, Min(1)] private int _maxValue = 1;

        public string Id => _id;
        public string DisplayName => _displayName;
        public AffixStat Stat => _stat;
        public int MinValue => Mathf.Max(1, _minValue);
        public int MaxValue => Mathf.Max(MinValue, _maxValue);
    }
}
