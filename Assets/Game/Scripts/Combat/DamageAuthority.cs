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

        /// <summary>
        /// 82 "clients send requests": on a client process a hit the local player lands on a replicated enemy is not
        /// applied — it is handed to this relay, which forwards it to the host as a damage request for the host to
        /// validate and apply. Returns true when the hit was forwarded (the hit landed; presentation may follow), false
        /// when the target is not something this client may request damage on. Null outside a co-op client run.
        /// </summary>
        public static System.Func<HealthComponent, DamageRequest, bool> RemoteDamageRelay { get; set; }

        /// <summary>
        /// Same seam for healing on a client (a consumable heal on the local player): the host decides the result and
        /// replicates the health back. Returns true when forwarded. Null outside a co-op client run.
        /// </summary>
        public static System.Func<HealthComponent, int, bool> RemoteHealRelay { get; set; }

        /// <summary>Clears both relays (run teardown): a later solo or host run must never forward anything.</summary>
        public static void ClearRelays()
        {
            RemoteDamageRelay = null;
            RemoteHealRelay = null;
        }
    }
}
