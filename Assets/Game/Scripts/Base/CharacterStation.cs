using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Progression;

namespace RuinRail.Gameplay.Base
{
    public enum RespecError
    {
        None,
        NotAtBase,
        NothingToRefund,
        InsufficientFunds
    }

    /// <summary>Everything the Character Station screen shows (base/73_CHARACTER_STATION); plain data for the UI task.</summary>
    public readonly struct CharacterSheet
    {
        public CharacterSheet(int level, int totalXp, int xpIntoLevel, int xpToNextLevel, bool isMaxLevel, int unspentPoints, IReadOnlyDictionary<SkillId, int> ranks, int respecPrice, int bankedCoins)
        {
            Level = level;
            TotalXp = totalXp;
            XpIntoLevel = xpIntoLevel;
            XpToNextLevel = xpToNextLevel;
            IsMaxLevel = isMaxLevel;
            UnspentPoints = unspentPoints;
            Ranks = ranks;
            RespecPrice = respecPrice;
            BankedCoins = bankedCoins;
        }

        public int Level { get; }
        public int TotalXp { get; }
        public int XpIntoLevel { get; }
        public int XpToNextLevel { get; }
        public bool IsMaxLevel { get; }
        public int UnspentPoints { get; }
        public IReadOnlyDictionary<SkillId, int> Ranks { get; }
        public int RespecPrice { get; }
        public int BankedCoins { get; }
    }

    /// <summary>
    /// Base-only progression service: allocation and respec are possible only while the player is in the Shelter.
    /// Respec debits exactly the configured Banked Coins price and refunds every allocated point in one atomic step —
    /// insufficient funds change neither coins nor skills; XP and level are never touched.
    /// </summary>
    public sealed class CharacterStation
    {
        private readonly ProgressionService _progression;
        private readonly CoinWallet _banked;
        private readonly EconomyConfig _economy;

        public CharacterStation(ProgressionService progression, CoinWallet bankedWallet, EconomyConfig economy)
        {
            _progression = progression ?? throw new ArgumentNullException(nameof(progression));
            _banked = bankedWallet ?? throw new ArgumentNullException(nameof(bankedWallet));
            if (_banked.Domain != CoinDomain.Banked) throw new ArgumentException("The Character Station uses Banked Coins only.", nameof(bankedWallet));
            _economy = economy != null ? economy : throw new ArgumentNullException(nameof(economy));
        }

        public ProgressionService Progression => _progression;
        public int RespecPrice => _economy.SkillRespecPrice;

        /// <summary>Base context flag; expedition runtime leaves it false so allocations are display-only there.</summary>
        public bool IsAtBase
        {
            get => _progression.IsAtBase;
            set => _progression.IsAtBase = value;
        }

        public event Action<CharacterSheet> Changed;

        public CharacterSheet GetSheet()
        {
            var ranks = new Dictionary<SkillId, int>();
            foreach (var skill in SkillRules.All) ranks[skill] = _progression.Profile.Skills.GetRank(skill);
            return new CharacterSheet(
                _progression.Level, _progression.TotalXp, _progression.XpIntoLevel, _progression.XpToNextLevel, _progression.IsMaxLevel,
                _progression.UnspentSkillPoints, ranks, RespecPrice, _banked.Balance);
        }

        public SkillSpendError Allocate(SkillId skill, int points = 1)
        {
            var result = _progression.TrySpend(skill, points);
            if (result == SkillSpendError.None) Changed?.Invoke(GetSheet());
            return result;
        }

        public bool CanRespec(out RespecError error)
        {
            if (!IsAtBase) { error = RespecError.NotAtBase; return false; }
            if (_progression.SpentSkillPoints == 0) { error = RespecError.NothingToRefund; return false; }
            if (!_banked.CanAfford(RespecPrice)) { error = RespecError.InsufficientFunds; return false; }
            error = RespecError.None;
            return true;
        }

        /// <summary>Atomic respec: debit first (rejected debits leave everything untouched), then refund all points.</summary>
        public RespecError Respec()
        {
            if (!CanRespec(out var error)) return error;
            var debit = _banked.Debit(RespecPrice, "skill_respec");
            if (!debit.Success) return RespecError.InsufficientFunds;

            _progression.ResetSkills();
            Changed?.Invoke(GetSheet());
            return RespecError.None;
        }
    }
}
