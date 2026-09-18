using System;

namespace RuinRail.Gameplay.Items
{
    [Serializable]
    public sealed class ItemInstanceSnapshot
    {
        public string InstanceId;
        public string DefinitionId;
        public int Rarity;
        public AffixRoll[] AffixRolls;
        public int Quantity;
        public bool IsAtRisk;
        public bool IsUnsellable;
    }
}
