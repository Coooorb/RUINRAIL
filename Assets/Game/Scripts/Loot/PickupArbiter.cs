using System;
using UnityEngine;

namespace RuinRail.Gameplay.Loot
{
    /// <summary>
    /// The seam a co-op run uses to resolve a world pickup through the host arbiter instead of locally.
    ///
    /// Installed and cleared by the expedition's composition root (never by a pickup, a player or a UI), and only for a
    /// co-op party: with nothing installed every pickup takes exactly the accepted solo path. A hook returns true/false
    /// for a resolved request and null to mean "not mine, use the local path".
    /// </summary>
    public static class PickupArbiter
    {
        /// <summary>Resolves one world item pickup for the interacting player.</summary>
        public static Func<WorldItemPickup, GameObject, bool?> Items { get; set; }

        /// <summary>Resolves one coin pile for the interacting player (58: the party split happens on the host).</summary>
        public static Func<CoinPickup, GameObject, bool?> Coins { get; set; }

        /// <summary>Teardown: the next run (or the menu) must never inherit a torn-down run's arbiter.</summary>
        public static void Clear()
        {
            Items = null;
            Coins = null;
        }
    }
}
