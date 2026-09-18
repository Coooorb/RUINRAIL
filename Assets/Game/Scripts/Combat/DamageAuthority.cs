namespace RuinRail.Gameplay.Combat
{
    /// <summary>
    /// 82: damage application is a host decision. A client process sets this to false at network bootstrap so its
    /// replicated HealthComponents never apply local hits (projectiles/melee/explosions render only); solo and host
    /// processes leave it true. Process-wide mode flag, never per-object state.
    /// </summary>
    public static class DamageAuthority
    {
        public static bool LocalIsAuthoritative { get; set; } = true;
    }
}
