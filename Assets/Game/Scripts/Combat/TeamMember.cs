using UnityEngine;

namespace RuinRail.Gameplay.Combat
{
    /// <summary>Team tag next to a HealthComponent so area effects and projectiles can apply the friendly-fire rule.</summary>
    public sealed class TeamMember : MonoBehaviour
    {
        [SerializeField] private DamageTeam _team = DamageTeam.Enemy;

        public DamageTeam Team => _team;

        public void SetTeam(DamageTeam team)
        {
            _team = team;
        }

        /// <summary>
        /// True only when both owners carry an explicit team tag and it is the same team. Untagged objects (test dummies,
        /// props) are never anyone's ally, so they stay hittable by everything — the friendly-fire rule needs two tags.
        /// </summary>
        public static bool AreAllies(Component a, Component b)
        {
            var ma = a != null ? a.GetComponentInParent<TeamMember>() : null;
            var mb = b != null ? b.GetComponentInParent<TeamMember>() : null;
            return ma != null && mb != null && ma.Team == mb.Team;
        }

        /// <summary>True when the collider's owner carries an explicit tag equal to <paramref name="team"/>.</summary>
        public static bool IsTagged(Component component, DamageTeam team)
        {
            var member = component != null ? component.GetComponentInParent<TeamMember>() : null;
            return member != null && member.Team == team;
        }

        /// <summary>Team of a collider's owner; objects without a tag count as Enemy targets (dummies, props).</summary>
        public static DamageTeam TeamOf(Component component)
        {
            var member = component != null ? component.GetComponentInParent<TeamMember>() : null;
            return member != null ? member.Team : DamageTeam.Enemy;
        }
    }
}
