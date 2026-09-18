using System;
using System.Collections.Generic;

namespace RuinRail.Networking
{
    /// <summary>82: every decision the host owns. Clients only send requests for these; they never declare outcomes.</summary>
    public enum AuthoritativeDomain
    {
        RunSeed,
        RoomGraph,
        EnemySpawning,
        EnemyAi,
        DamageApplication,
        EnemyDeath,
        ChestOpeningAndLootRolls,
        WorldPickupValidity,
        BossState,
        DownedReviveDeath,
        TransitDecision,
        DepthProgression,
        CoinsAndEconomy,
        PersistentRunTransactions,

        /// <summary>81/10: session member identities (sanitized display names, ownership) are host-validated.</summary>
        PartyRoster
    }

    /// <summary>Who the local peer is for authority checks. Offline (solo) is authoritative over everything.</summary>
    public interface IAuthorityContext
    {
        NetworkRole Role { get; }
        bool IsAuthority { get; }
    }

    /// <summary>Solo / tests: the local process is the authority.</summary>
    public sealed class LocalAuthorityContext : IAuthorityContext
    {
        public static readonly LocalAuthorityContext Instance = new();
        public NetworkRole Role => NetworkRole.Offline;
        public bool IsAuthority => true;
    }

    public sealed class AuthorityViolationException : InvalidOperationException
    {
        public AuthorityViolationException(AuthoritativeDomain domain, NetworkRole role)
            : base($"{role} may not decide {domain}; only the host (or an offline solo game) is authoritative.")
        {
            Domain = domain;
            Role = role;
        }

        public AuthoritativeDomain Domain { get; }
        public NetworkRole Role { get; }
    }

    /// <summary>
    /// The host-authority contract (82) as code: which domains are host-owned (all of them), what clients may do
    /// (send requests) and a guard later systems call before applying an authoritative outcome.
    /// </summary>
    public static class HostAuthorityContract
    {
        public static readonly IReadOnlyList<AuthoritativeDomain> HostOwned = (AuthoritativeDomain[])Enum.GetValues(typeof(AuthoritativeDomain));

        /// <summary>Requests a client may send; the host validates and decides (82 "inputs/requests").</summary>
        public static readonly IReadOnlyList<string> ClientRequests = new[] { "move", "aim", "fire", "interact", "open", "pickup", "transit_vote", "revive" };

        public static bool CanDecide(IAuthorityContext context, AuthoritativeDomain domain)
        {
            return context != null && context.IsAuthority && HostOwned.Contains(domain);
        }

        public static bool CanDecide(NetworkRole role, AuthoritativeDomain domain) => role != NetworkRole.Client && HostOwned.Contains(domain);

        /// <summary>Throws when a non-authoritative peer tries to apply an authoritative outcome.</summary>
        public static void Require(IAuthorityContext context, AuthoritativeDomain domain)
        {
            if (!CanDecide(context, domain)) throw new AuthorityViolationException(domain, context?.Role ?? NetworkRole.Client);
        }

        private static bool Contains(this IReadOnlyList<AuthoritativeDomain> list, AuthoritativeDomain domain)
        {
            for (var i = 0; i < list.Count; i++) if (list[i] == domain) return true;
            return false;
        }
    }
}
