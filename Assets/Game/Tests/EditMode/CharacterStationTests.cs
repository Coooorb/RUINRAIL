using NUnit.Framework;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Progression;
using RuinRail.Gameplay.Stats;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    public class CharacterStationTests
    {
        private EconomyConfig _economy;
        private PlayerProfile _profile;
        private ProgressionService _progression;
        private CoinWallet _banked;
        private CharacterStation _station;

        [SetUp]
        public void SetUp()
        {
            _economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset");
            _profile = new PlayerProfile { TotalXp = LevelCurve.TotalXpForLevel(9), BankedCoins = 3000 };
            _progression = new ProgressionService(_profile);
            _banked = new CoinWallet(CoinDomain.Banked, () => _profile.BankedCoins, v => _profile.BankedCoins = v);
            _station = new CharacterStation(_progression, _banked, _economy) { IsAtBase = true };
        }

        // ---- Acceptance 1 ----

        [Test]
        public void Sheet_ExposesLevelXpPointsAndRanks_AndAllocationSpendsOnePoint()
        {
            var sheet = _station.GetSheet();
            Assert.AreEqual(9, sheet.Level);
            Assert.AreEqual(LevelCurve.TotalXpForLevel(9), sheet.TotalXp);
            Assert.AreEqual(0, sheet.XpIntoLevel);
            Assert.AreEqual(LevelCurve.XpToNextLevel(9), sheet.XpToNextLevel);
            Assert.IsFalse(sheet.IsMaxLevel);
            Assert.AreEqual(8, sheet.UnspentPoints);
            Assert.AreEqual(6, sheet.Ranks.Count);
            Assert.AreEqual(2500, sheet.RespecPrice);
            Assert.AreEqual(3000, sheet.BankedCoins);

            var changes = 0;
            _station.Changed += _ => changes++;
            Assert.AreEqual(SkillSpendError.None, _station.Allocate(SkillId.Power));
            Assert.AreEqual(1, _profile.Skills.Power);
            Assert.AreEqual(7, _station.GetSheet().UnspentPoints);
            Assert.AreEqual(1, changes);

            var caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>();
            var stats = new PlayerStats(caps, 100);
            stats.SetSource(new SkillStatSource(_profile.Skills));
            Assert.AreEqual(1, stats.GetPercent(StatId.WeaponDamage), "Power rank 1 = +1% weapon damage through the pipeline.");
            Object.DestroyImmediate(caps);
        }

        [Test]
        public void ExpeditionContext_CanReadButNotAllocateOrRespec()
        {
            _station.Allocate(SkillId.Mobility, 2);
            _station.IsAtBase = false;

            Assert.AreEqual(SkillSpendError.NotAtBase, _station.Allocate(SkillId.Mobility));
            Assert.AreEqual(2, _profile.Skills.Mobility);
            Assert.AreEqual(6, _station.GetSheet().UnspentPoints, "Still readable outside the Base.");
            Assert.AreEqual(RespecError.NotAtBase, _station.Respec());
            Assert.AreEqual(3000, _profile.BankedCoins);
            Assert.AreEqual(2, _profile.Skills.Mobility);
        }

        // ---- Acceptance 2 + 4 ----

        [Test]
        public void Respec_CostsExactly2500_RefundsAllPoints_LeavesXpAndLevel()
        {
            _station.Allocate(SkillId.Vitality, 3);
            _station.Allocate(SkillId.Recovery, 2);
            _station.Allocate(SkillId.Resilience, 3);
            Assert.AreEqual(8, _progression.SpentSkillPoints);
            Assert.AreEqual(0, _progression.UnspentSkillPoints);

            Assert.AreEqual(RespecError.None, _station.Respec());

            Assert.AreEqual(500, _profile.BankedCoins, "3000 − 2500");
            Assert.AreEqual(8, _progression.UnspentSkillPoints, "Every spent point returned.");
            Assert.AreEqual(0, _profile.Skills.TotalSpent);
            foreach (var skill in SkillRules.All) Assert.AreEqual(0, _profile.Skills.GetRank(skill), skill.ToString());
            Assert.AreEqual(9, _progression.Level);
            Assert.AreEqual(LevelCurve.TotalXpForLevel(9), _profile.TotalXp);
            Assert.AreEqual(RespecError.NothingToRefund, _station.Respec(), "Nothing allocated: no charge.");
            Assert.AreEqual(500, _profile.BankedCoins);
        }

        // ---- Acceptance 3 ----

        [Test]
        public void FailedRespec_IsAtomic_NeitherCoinsNorSkillsChange()
        {
            _profile.BankedCoins = 2499;
            _station.Allocate(SkillId.Handling, 4);

            Assert.IsFalse(_station.CanRespec(out var error));
            Assert.AreEqual(RespecError.InsufficientFunds, error);
            Assert.AreEqual(RespecError.InsufficientFunds, _station.Respec());
            Assert.AreEqual(2499, _profile.BankedCoins);
            Assert.AreEqual(4, _profile.Skills.Handling);
            Assert.AreEqual(4, _progression.UnspentSkillPoints);

            _profile.BankedCoins = 2500;
            Assert.AreEqual(RespecError.None, _station.Respec());
            Assert.AreEqual(0, _profile.BankedCoins, "Exactly 2500 suffices.");
            Assert.AreEqual(8, _progression.UnspentSkillPoints);
        }

        [Test]
        public void Station_RequiresTheBankedWallet()
        {
            Assert.Throws<System.ArgumentException>(() => new CharacterStation(_progression, new CoinWallet(CoinDomain.Carried, 5000), _economy));
        }
    }
}
