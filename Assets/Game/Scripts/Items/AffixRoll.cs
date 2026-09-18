using System;

namespace RuinRail.Gameplay.Items
{
    [Serializable]
    public struct AffixRoll
    {
        public string AffixId;
        public int Value;

        public AffixRoll(string affixId, int value)
        {
            AffixId = affixId;
            Value = value;
        }
    }
}
