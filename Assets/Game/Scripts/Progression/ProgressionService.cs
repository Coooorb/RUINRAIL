using System;
using RuinRail.Gameplay.Expedition;

namespace RuinRail.Gameplay.Progression
{
    public enum SkillSpendError
    {
        None,
        NoUnspentPoints,
        RankAtMax,
        NotAtBase,
        InvalidAmount
    }

    /// <summary>
    /// Permanent progression over the profile: XP is committed immediately and permanently (never tied to extraction),
    /// levels derive from lifetime XP, each level-up grants one skill point, and points are allocated only while at the
    /// Shelter (Base domain). Overspend/over-rank requests are rejected atomically.
    /// </summary>
    public sealed class ProgressionService
    {
        private readonly PlayerProfile _profile;

        public ProgressionService(PlayerProfile profile)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            ReconcilePoints();
        }

        public PlayerProfile Profile => _profile;
        public int Level => _profile.Level;
        public int TotalXp => _profile.TotalXp;
        public int XpIntoLevel => LevelCurve.XpIntoCurrentLevel(_profile.TotalXp);
        public int XpToNextLevel => LevelCurve.XpToNextLevel(Level);
        public bool IsMaxLevel => Level >= LevelCurve.MaxLevel;
        public int UnspentSkillPoints => _profile.UnspentSkillPoints;
        public int SpentSkillPoints => _profile.Skills.TotalSpent;

        /// <summary>True while the player is in the Shelter; skill points can only be spent there (13).</summary>
        public bool IsAtBase { get; set; }

        public event Action<int, int> LevelledUp;
        public event Action<int> XpGained;

        /// <summary>Skill points spent or refunded (a persistence safe point).</summary>
        public event Action SkillsChanged;

        /// <summary>Adds XP permanently; returns the number of levels gained. XP past the cap is kept as lifetime XP but grants nothing.</summary>
        public int AddXp(int amount)
        {
            if (amount <= 0) return 0;
            var before = Level;
            _profile.TotalXp += amount;
            var after = Level;
            var gained = after - before;
            if (gained > 0)
            {
                _profile.UnspentSkillPoints += gained * LevelCurve.SkillPointsPerLevel;
                LevelledUp?.Invoke(before, after);
            }

            XpGained?.Invoke(amount);
            return gained;
        }

        public SkillSpendError TrySpend(SkillId skill, int points = 1)
        {
            if (points <= 0) return SkillSpendError.InvalidAmount;
            if (!IsAtBase) return SkillSpendError.NotAtBase;
            if (_profile.UnspentSkillPoints < points) return SkillSpendError.NoUnspentPoints;
            var rank = _profile.Skills.GetRank(skill);
            if (rank + points > SkillRules.MaxRank) return SkillSpendError.RankAtMax;

            _profile.Skills.SetRank(skill, rank + points);
            _profile.UnspentSkillPoints -= points;
            SkillsChanged?.Invoke();
            return SkillSpendError.None;
        }

        /// <summary>Character Station respec: all allocated points return as unspent; level and XP unchanged.</summary>
        public int ResetSkills()
        {
            var refunded = _profile.Skills.TotalSpent;
            _profile.Skills.Reset();
            _profile.UnspentSkillPoints += refunded;
            if (refunded > 0) SkillsChanged?.Invoke();
            return refunded;
        }

        /// <summary>Keeps the invariant spent + unspent == points earned by level (repairs corrupted/older profiles).</summary>
        public void ReconcilePoints()
        {
            var earned = LevelCurve.SkillPointsEarned(_profile.TotalXp);
            var spent = _profile.Skills.TotalSpent;
            if (spent > earned)
            {
                _profile.Skills.Reset();
                spent = 0;
            }

            _profile.UnspentSkillPoints = earned - spent;
        }
    }
}
