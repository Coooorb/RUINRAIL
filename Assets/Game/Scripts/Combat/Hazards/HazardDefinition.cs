using UnityEngine;

namespace RuinRail.Gameplay.Combat.Hazards
{
    /// <summary>
    /// Authored environmental hazard (50: Hazards layer = trigger/damage logic; 56: electricity, rail machinery, acid).
    /// A hazard is a damage source with a tag, a band and a tick interval. It is not a status/elemental system: the
    /// only tags are the approved DamageKind values, so Blast Suit / Shock Absorber rules apply to explosion-tagged
    /// hazards and everything else is plain environmental damage.
    /// </summary>
    [CreateAssetMenu(fileName = "HazardDefinition", menuName = "RuinRail/Combat/Hazard Definition")]
    public sealed class HazardDefinition : ScriptableObject
    {
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField] private DamageKind _kind = DamageKind.Hazard;
        [SerializeField, Min(0)] private int _damageMin = 5;
        [SerializeField, Min(0)] private int _damageMax = 8;
        [Tooltip("Seconds between ticks for one occupant. The first tick lands after InitialDelay.")]
        [SerializeField, Min(0.05f)] private float _tickIntervalSeconds = 1f;
        [Tooltip("Seconds an occupant may stand inside before the first tick (0 = immediate on contact).")]
        [SerializeField, Min(0f)] private float _initialDelaySeconds;
        [SerializeField] private bool _affectsPlayers = true;
        [SerializeField] private bool _affectsEnemies = true;
        [SerializeField, Min(0f)] private float _knockback;
        [SerializeField, Min(0f)] private float _staggerPower;

        public string Id => _id;
        public string DisplayName => _displayName;
        public DamageKind Kind => _kind;
        public int DamageMin => _damageMin;
        public int DamageMax => Mathf.Max(_damageMin, _damageMax);
        public float TickIntervalSeconds => Mathf.Max(0.05f, _tickIntervalSeconds);
        public float InitialDelaySeconds => Mathf.Max(0f, _initialDelaySeconds);

        /// <summary>Hazards hurt players regardless of the friendly-fire rule; this flag exists only for enemy-only traps.</summary>
        public bool AffectsPlayers => _affectsPlayers;
        public bool AffectsEnemies => _affectsEnemies;
        public float Knockback => _knockback;
        public float StaggerPower => _staggerPower;

        /// <summary>In-memory definition for tests and editor tooling.</summary>
        public static HazardDefinition Create(string id, DamageKind kind, int damageMin, int damageMax, float tickInterval, float initialDelay = 0f,
            bool affectsPlayers = true, bool affectsEnemies = true, float knockback = 0f, float staggerPower = 0f)
        {
            var d = CreateInstance<HazardDefinition>();
            d._id = id;
            d._displayName = id;
            d._kind = kind;
            d._damageMin = damageMin;
            d._damageMax = damageMax;
            d._tickIntervalSeconds = tickInterval;
            d._initialDelaySeconds = initialDelay;
            d._affectsPlayers = affectsPlayers;
            d._affectsEnemies = affectsEnemies;
            d._knockback = knockback;
            d._staggerPower = staggerPower;
            return d;
        }
    }
}
