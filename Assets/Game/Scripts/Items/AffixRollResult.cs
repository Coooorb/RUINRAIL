namespace RuinRail.Gameplay.Items
{
    public enum AffixRollError
    {
        None,
        InvalidRequest,
        NotEquipment,
        MissingPool,
        PoolTooSmall,
        AlreadyRolled
    }

    public readonly struct AffixRollResult
    {
        public bool Success { get; }
        public AffixRollError Error { get; }
        public int AffixCount { get; }

        private AffixRollResult(bool success, AffixRollError error, int affixCount)
        {
            Success = success;
            Error = error;
            AffixCount = affixCount;
        }

        public static AffixRollResult Ok(int affixCount) => new(true, AffixRollError.None, affixCount);
        public static AffixRollResult Fail(AffixRollError error) => new(false, error, 0);
    }
}
