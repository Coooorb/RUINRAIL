namespace RuinRail.Gameplay.Combat
{
    public interface IDamageRoller
    {
        int Roll(int minInclusive, int maxInclusive);
    }
}
