using System.Collections.Generic;
using NUnit.Framework;
using RuinRail.Gameplay.Expedition;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 107 — 86 co-op transit voting: unanimous Descend among living players, any Return returns everyone, Dead have no vote.</summary>
    public sealed class TransitVotingTests
    {
        private static TransitDecision Open(IEnumerable<string> living, IEnumerable<string> dead = null)
        {
            var decision = new TransitDecision(new PartyTransitPolicy(), living, dead);
            decision.Open();
            return decision;
        }

        [Test]
        public void Descend_RequiresEveryLivingPlayer_AnyReturnReturnsTheParty()
        {
            var trio = Open(new[] { "a", "b", "c" });
            Assert.IsFalse(trio.Submit("a", TransitChoice.DescendDeeper));
            Assert.IsFalse(trio.Submit("b", TransitChoice.DescendDeeper));
            Assert.AreEqual(TransitDecisionState.Open, trio.State, "Two of three is not unanimity.");
            CollectionAssert.AreEquivalent(new[] { "c" }, trio.PendingVoters);
            Assert.IsTrue(trio.Submit("c", TransitChoice.DescendDeeper));
            Assert.AreEqual(TransitChoice.DescendDeeper, trio.Result);

            var duo = Open(new[] { "a", "b" });
            Assert.IsTrue(duo.Submit("b", TransitChoice.ReturnToShelter), "One Return decides at once, whoever is still pending.");
            Assert.AreEqual(TransitChoice.ReturnToShelter, duo.Result);
            Assert.IsFalse(duo.Submit("a", TransitChoice.DescendDeeper), "Resolved: later votes are ignored.");
            Assert.AreEqual(TransitChoice.ReturnToShelter, duo.Result);

            var mixed = Open(new[] { "a", "b", "c" });
            mixed.Submit("a", TransitChoice.DescendDeeper);
            Assert.IsTrue(mixed.Submit("b", TransitChoice.ReturnToShelter));
            Assert.AreEqual(TransitChoice.ReturnToShelter, mixed.Result, "A single Return overrides any Descend votes.");
        }

        [Test]
        public void DeadPlayers_HaveNoVote_AndAreNotWaitedFor()
        {
            var decision = Open(new[] { "a", "b" }, new[] { "dead" });
            Assert.IsFalse(decision.CanVote("dead"));
            Assert.IsFalse(decision.Submit("dead", TransitChoice.ReturnToShelter), "Ignored: a dead Return does not return the party.");
            Assert.AreEqual(TransitDecisionState.Open, decision.State);
            Assert.AreEqual(0, decision.Choices.Count);
            Assert.IsFalse(decision.Submit("stranger", TransitChoice.DescendDeeper), "Unknown ids never vote.");
            decision.Submit("a", TransitChoice.DescendDeeper);
            Assert.IsTrue(decision.Submit("b", TransitChoice.DescendDeeper), "Unanimity counts living players only.");
            Assert.AreEqual(TransitChoice.DescendDeeper, decision.Result);
        }

        [Test]
        public void AVoterThatDies_LosesItsVote_AndTheDecisionReEvaluates_NoDeadlock()
        {
            var decision = Open(new[] { "a", "b", "c" });
            decision.Submit("a", TransitChoice.DescendDeeper);
            decision.Submit("b", TransitChoice.DescendDeeper);
            Assert.AreEqual(TransitDecisionState.Open, decision.State, "Waiting for c.");

            Assert.IsTrue(decision.RemoveVoter("c"), "c died (or its reconnect grace expired).");
            Assert.AreEqual(TransitDecisionState.Resolved, decision.State, "The two remaining living players were unanimous.");
            Assert.AreEqual(TransitChoice.DescendDeeper, decision.Result);
            CollectionAssert.Contains(decision.DeadPlayers, "c");
            Assert.IsFalse(decision.RemoveVoter("c"), "Already removed.");
            Assert.IsFalse(decision.CanVote("c"));

            // A voter whose Descend was cast and who then dies: the rest still need to be unanimous.
            var other = Open(new[] { "a", "b" });
            other.Submit("a", TransitChoice.DescendDeeper);
            other.RemoveVoter("a");
            Assert.AreEqual(TransitDecisionState.Open, other.State, "b has not voted; nothing is decided by a dead player's earlier vote.");
            Assert.IsTrue(other.Submit("b", TransitChoice.ReturnToShelter));
        }

        [Test]
        public void ARevivedPlayer_GainsAVote_WhileTheDecisionIsOpen()
        {
            var decision = Open(new[] { "a", "b" }, new[] { "c" });
            decision.Submit("a", TransitChoice.DescendDeeper);
            Assert.IsTrue(decision.AddVoter("c"), "c was revived at the boss room.");
            Assert.IsFalse(decision.RequiresReturnWarning, "Nobody is Dead any more.");
            Assert.IsFalse(decision.Submit("b", TransitChoice.DescendDeeper), "c must agree too.");
            Assert.IsTrue(decision.Submit("c", TransitChoice.DescendDeeper));
            Assert.AreEqual(TransitChoice.DescendDeeper, decision.Result);
            Assert.IsFalse(decision.AddVoter("d"), "Resolved: the voter set is frozen.");
        }

        [Test]
        public void RepeatedOrChangedVotes_ResolveExactlyOneTransition()
        {
            var decision = Open(new[] { "a", "b" });
            var resolutions = new List<TransitChoice>();
            decision.Resolved += (_, c) => resolutions.Add(c);

            decision.Submit("a", TransitChoice.DescendDeeper);
            decision.Submit("a", TransitChoice.DescendDeeper);
            decision.Submit("a", TransitChoice.DescendDeeper);
            Assert.AreEqual(1, decision.Choices.Count, "Repeated votes are one choice.");
            Assert.AreEqual(TransitDecisionState.Open, decision.State);

            decision.Submit("a", TransitChoice.ReturnToShelter);
            Assert.AreEqual(TransitDecisionState.Resolved, decision.State, "A changed vote counts (Return decides).");
            decision.Submit("a", TransitChoice.DescendDeeper);
            decision.Submit("b", TransitChoice.DescendDeeper);
            decision.Submit("b", TransitChoice.ReturnToShelter);
            decision.RemoveVoter("b");
            CollectionAssert.AreEqual(new[] { TransitChoice.ReturnToShelter }, resolutions, "Exactly one transition, never re-resolved.");
            Assert.AreEqual(1, decision.Resolutions);
            Assert.AreEqual(4, decision.Submissions, "Only submissions while Open are counted.");
        }

        [Test]
        public void ReturnWarning_ListsDeadTeammates_BeforeConfirmation()
        {
            var none = Open(new[] { "a", "b" });
            Assert.IsFalse(none.RequiresReturnWarning);
            Assert.IsFalse(none.ReturnWarning.IsRequired);
            Assert.AreEqual(string.Empty, none.ReturnWarning.Message);

            var withDead = Open(new[] { "a" }, new[] { "b", "c" });
            Assert.IsTrue(withDead.RequiresReturnWarning);
            CollectionAssert.AreEqual(new[] { "b", "c" }, withDead.ReturnWarning.DeadPlayerIds);
            StringAssert.Contains("b, c are Dead", withDead.ReturnWarning.Message);
            StringAssert.Contains("loses their carried gear, loot and coins", withDead.ReturnWarning.Message);

            var one = Open(new[] { "a" }, new[] { "b" });
            StringAssert.Contains("b is Dead", one.ReturnWarning.Message);
            Assert.AreEqual(TransitDecisionState.Open, one.State, "The warning never decides anything by itself.");
        }

        [Test]
        public void SoloPolicy_IsUnchanged_AndNobodyAliveResolvesNothing()
        {
            var solo = new TransitDecision(new SoloTransitPolicy(), new[] { "local" });
            solo.Open();
            Assert.IsTrue(solo.Submit("local", TransitChoice.DescendDeeper));

            var empty = Open(new string[0]);
            Assert.IsFalse(empty.Submit("a", TransitChoice.DescendDeeper));
            Assert.AreEqual(TransitDecisionState.Open, empty.State, "No living voters: the team wipe (84) ends the run instead.");
        }
    }
}
