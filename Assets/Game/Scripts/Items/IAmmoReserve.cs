namespace RuinRail.Gameplay.Items
{
    public interface IAmmoReserve
    {
        int Get(AmmoType type);
        int Add(AmmoType type, int amount);
        int Consume(AmmoType type, int amount);
    }
}
