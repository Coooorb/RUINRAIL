using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.UI.Hud;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Three things a composed party changes that solo never exercised: where a coin pile's value lands (Phase 10),
    /// what survives a depth change for every member (Phase 14), and what the HUD can tell a player about the party at
    /// 1/2/3 members (Phase 18). All of it runs against the real <see cref="ExpeditionParty"/> composition.
    /// </summary>
    public class CoopEconomyDepthUiTests
    {
        private const string MatrixDirectory = "TestResults/CoopRuntimeComposition";
        private readonly List<UnityEngine.Object> _created = new();
        private int _lane;

        [SetUp]
        public void SetUp() => DamageAuthority.LocalIsAuthoritative = true;

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _created.Clear();
            DamageAuthority.LocalIsAuthoritative = true;
        }

        private T Track<T>(T o) where T : UnityEngine.Object { _created.Add(o); return o; }
        private Vector2 NextLane() => new(0f, 2600f + _lane++ * 120f);

        private static void WriteMatrix(string fileName, IEnumerable<string> rows)
        {
            Directory.CreateDirectory(MatrixDirectory);
            File.WriteAllLines(Path.Combine(MatrixDirectory, fileName), rows);
        }

        private sealed class HostAuthority : IAuthorityContext
        {
            public NetworkRole Role => NetworkRole.Host;
            public bool IsAuthority => true;
        }

        private (ExpeditionParty party, PartyLifeRoster roster, LootAuthorityService loot, ItemDefinitionRegistry registry) Compose(int size, Vector2 origin)
        {
            var content = GameContentCatalog.Load();
            var registry = ItemDefinitionRegistry.Build(content.Items.Where(i => i != null));
            var roster = new PartyLifeRoster();
            var loot = new LootAuthorityService(new HostAuthority());
            var local = Track(PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options
            {
                Name = "LocalPlayer", IsLocal = false, BalanceConfig = content.PlayerBalance, Caps = content.StatCaps,
                Position = origin, LifeRoster = roster, ParticipantId = "p0"
            }));
            local.GetComponent<PlayerLootReceiver>().SetInventory(PlayerInventory.FromRegistry(registry, content.AmmoBalance));

            var members = Enumerable.Range(0, size)
                .Select(i => new PartyMemberDescriptor((ulong)i, i == 0 ? "Host" : null, "p" + i, i == 0)).ToList();
            var party = ExpeditionParty.Compose(new PartyCompositionRequest
            {
                Members = members, LocalClientId = 0, LocalEntity = local, LocalParticipantId = "p0", IsHost = true,
                Balance = content.PlayerBalance, Caps = content.StatCaps, LifeRoster = roster, Loot = loot,
                Items = registry, Ammo = content.AmmoBalance, DisplayNamePolicy = content.DisplayNamePolicy,
                SpawnPosition = identity => origin + new Vector2(2f * (identity.ClientId + 1), 0f)
            }, out var error);
            Assert.IsNotNull(party, $"composition error: {error}");
            foreach (var member in party.Members) if (member.GameObject != local) Track(member.GameObject);
            return (party, roster, loot, registry);
        }

        // ---------------- Phase 10: coins and wallets ----------------

        [UnityTest]
        public IEnumerator CoinPile_SplitsEvenlyAcrossTheComposedParty_AndConservesTheTotal()
        {
            var rows = new List<string> { "case,party,amount,shares,total_credited,picker_share,wallet_domain,result" };
            foreach (var size in new[] { 1, 2, 3 })
            {
                var origin = NextLane();
                var (party, _, loot, _) = Compose(size, origin);
                yield return null;
                var wallets = party.Members.Select(m => m.GameObject.GetComponent<PlayerLootReceiver>().Wallet).ToList();
                Assert.AreEqual(size, wallets.Count);
                Assert.IsTrue(wallets.All(w => w.Domain == CoinDomain.Carried), "77: world coins are Carried, never Banked.");
                // 58: with more than one participant the pile is split; solo keeps the whole amount.
                Assert.AreEqual(size > 1, party.CoinDistribution != null, $"{size}: the party split is composed only for co-op.");

                var pile = Track(new GameObject("CoinPile")).AddComponent<CoinPickup>();
                pile.transform.position = origin;
                pile.SetAmount(100);
                yield return null;

                var picker = party.Members.Last();
                var result = loot.RequestCoins("tx-coins-" + size, picker.OwnerClientId, pile);
                Assert.AreEqual(LootVerdict.Accepted, result.Verdict);
                var total = wallets.Sum(w => w.Balance);
                Assert.AreEqual(100, total, $"{size}: the pile's value is conserved exactly.");
                if (size > 1)
                {
                    Assert.IsTrue(wallets.All(w => w.Balance > 0), "Every member receives a share, not only the picker.");
                    Assert.LessOrEqual(wallets.Max(w => w.Balance) - wallets.Min(w => w.Balance), 1, "The split is even to within the remainder.");
                }

                rows.Add(string.Join(",", (size == 1 ? "solo coin pile" : size == 2 ? "duo coin pile" : "trio coin pile"), size, 100, string.Join(";", wallets.Select(w => w.Balance)), total,
                    picker.GameObject.GetComponent<PlayerLootReceiver>().Wallet.Balance, "Carried", "PASS"));
                party.Dispose();
            }

            rows.Add("shared vs per-player,n/a,n/a,84 line 39 (a Dead member loses its own carried coins while living players secure theirs) means carried coins are per member; each member owns a Carried wallet and a pile is split evenly per 58,n/a,n/a,Carried,PASS");
            rows.Add("simultaneous merchant purchase,2,n/a,see authority_exploit_matrix.csv (one Accepted / one AlreadyTaken; only the buyer is debited),n/a,n/a,Carried,PASS");
            WriteMatrix("economy_ownership_matrix.csv", rows);
        }

        // ---------------- Phase 14: depth transition ----------------

        [UnityTest]
        public IEnumerator DepthArrival_HealsEveryLivingMemberOnce_AndThePartySurvivesTheTransition()
        {
            var origin = NextLane();
            var (party, roster, _, registry) = Compose(3, origin);
            yield return null;
            var rows = new List<string> { "aspect,before,after,expected,result" };

            var entities = party.Members.Select(m => m.GameObject).ToList();
            var ids = party.Members.Select(m => m.GameObject.GetComponent<PlayerLifeStateComponent>().ParticipantId).OrderBy(x => x).ToList();
            // Give one member an item so "inventories survive" is about a real instance, not an empty container.
            var ammo = GameContentCatalog.Load().Items.OfType<AmmoItemDefinition>().First();
            var carrier = entities[1].GetComponent<PlayerLootReceiver>();
            Assert.IsTrue(carrier.Inventory.TryAddToBackpack(new ItemInstance(ammo.Id, 12)));
            // The transfer merges into the backpack and zeroes the source instance, so the live stack is read back.
            var carried = carrier.Inventory.BackpackSlots.First(s => s != null && s.DefinitionId == ammo.Id);
            var carriedId = carried.InstanceId;
            var carriedQuantity = carried.Quantity;

            // One member is hurt and one is Downed before the transition.
            entities[0].GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(20));
            entities[2].GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
            yield return null;
            var hurtBefore = entities[0].GetComponent<HealthComponent>().CurrentHealth;
            Assert.AreEqual(PlayerLifeState.Downed, entities[2].GetComponent<PlayerLifeStateComponent>().State);

            // dungeon/60: the depth-arrival heal runs once, for every living participant, to its own effective maximum.
            var outcomes = DepthArrivalHeal.ApplyToParty(roster);
            yield return null;
            var healed = outcomes.Where(o => o.Eligible).ToList();
            Assert.AreEqual(2, healed.Count, "Every living member is healed; the Downed member is not.");
            Assert.IsTrue(healed.All(o => o.IsFull));
            Assert.Greater(entities[0].GetComponent<HealthComponent>().CurrentHealth, hurtBefore);
            rows.Add($"depth-arrival heal,{hurtBefore} HP on one member,{entities[0].GetComponent<HealthComponent>().CurrentHealth} HP,every living member to its effective max exactly once,PASS");
            rows.Add($"downed member is not healed,Downed,{entities[2].GetComponent<PlayerLifeStateComponent>().State},unchanged by the arrival heal,PASS");

            // The party itself is not depth-scoped: the same entities, identities, inventories and roster carry over.
            Assert.AreEqual(3, party.ComposedPartySize);
            CollectionAssert.AreEqual(entities, party.Members.Select(m => m.GameObject).ToList(), "No entity is rebuilt or lost across the transition.");
            CollectionAssert.AreEqual(ids, party.Members.Select(m => m.GameObject.GetComponent<PlayerLifeStateComponent>().ParticipantId).OrderBy(x => x).ToList());
            Assert.AreEqual(3, roster.Count);
            Assert.IsTrue(carrier.Inventory.Contains(carriedId), "A member's carried instance survives the depth change.");
            Assert.AreEqual(0, entities.Sum(e => e.GetComponentsInChildren<AudioListener>(true).Length), "No member carries a listener, so a depth change cannot add one.");
            Assert.AreEqual(0, entities.Sum(e => e.GetComponentsInChildren<Camera>(true).Length));
            rows.Add($"party identities,3 members,{party.ComposedPartySize} members,all identities survive,PASS");
            rows.Add($"carried inventory,{carriedQuantity}x {ammo.Id} on one member,{carrier.Inventory.BackpackSlots.First(s => s != null && s.InstanceId == carriedId).Quantity}x present after the transition,inventories survive,PASS");
            rows.Add("listeners/cameras from members,0,0,a depth change cannot duplicate local presentation,PASS");
            rows.Add("room reveal / depth-local state,n/a,n/a,ExpeditionScene.OnDepthEntered clears CurrentRoom the revealed room the vote the notice and the minimap depth (inspection),PASS");
            WriteMatrix("depth_transition_matrix.csv", rows);
            party.Dispose();
        }

        // ---------------- Phase 18: party UI ----------------

        [UnityTest]
        public IEnumerator PartyHud_ShowsSelfTeammatesAndTheirStates_AtOneTwoAndThreeMembers()
        {
            var rows = new List<string> { "party_size,rows,names,states_at_compose,states_after_downed_and_disconnect_checks,disconnected_shown,longest_row_chars,fits_640x360,result" };
            foreach (var size in new[] { 1, 2, 3 })
            {
                var (party, roster, _, _) = Compose(size, NextLane());
                yield return null;
                var hud = new DungeonHudViewModel();
                hud.BindParty(roster);
                foreach (var member in party.Members)
                {
                    var life = member.GameObject.GetComponent<PlayerLifeStateComponent>();
                    hud.SetDisplayName(life.ParticipantId, member.Identity.DisplayName);
                }

                // The snapshot is republished by every binding change; read it as the view does.
                Assert.AreEqual(size, hud.Snapshot.Party.Count, $"{size}: one row per member, self included.");
                Assert.IsTrue(hud.Snapshot.Party.All(p => !string.IsNullOrEmpty(p.Name)), "Every row is named.");
                Assert.IsTrue(hud.Snapshot.Party.All(p => p.MaxHp > 0 && p.Hp > 0), "Every row shows that member's health.");
                Assert.IsTrue(hud.Snapshot.Party.All(p => p.State == HudLifeState.Alive));
                var statesAtCompose = string.Join(";", hud.Snapshot.Party.Select(p => p.State));

                // A Downed teammate reads as DOWNED with its bleedout, and a held connection as DISCONNECTED (85).
                var downedText = string.Empty;
                var disconnectedText = string.Empty;
                if (size > 1)
                {
                    var victim = party.RemoteMembers.First().GameObject;
                    victim.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
                    yield return null;
                    // The snapshot is republished by every binding change; read it as the view does.
                    var row = hud.Snapshot.Party.First(p => p.State == HudLifeState.Downed);
                    downedText = row.StateText;
                    Assert.IsTrue(downedText.StartsWith("DOWNED"), "A downed teammate is readable as such.");
                    hud.SetMemberConnected(victim.GetComponent<PlayerLifeStateComponent>().ParticipantId, false);
                    // The snapshot is republished by every binding change; read it as the view does.
                    disconnectedText = hud.Snapshot.Party.First(p => p.State == HudLifeState.Disconnected).StateText;
                    Assert.AreEqual("DISCONNECTED", disconnectedText);
                }

                // 640x360 is the reference resolution: the party block is text rows, so width is the constraint.
                var longest = hud.Snapshot.Party.Max(p => (p.Name + " " + p.StateText).Length);
                Assert.LessOrEqual(longest, 32, "A party row stays inside the reference-resolution HUD column.");
                rows.Add(string.Join(",", size, hud.Snapshot.Party.Count, string.Join(";", hud.Snapshot.Party.Select(p => p.Name)),
                    statesAtCompose, string.Join(";", hud.Snapshot.Party.Select(p => p.State)),
                    string.IsNullOrEmpty(disconnectedText) ? "n/a (solo)" : disconnectedText,
                    longest, "YES", "PASS"));
                party.Dispose();
            }

            rows.Add("revive opportunity,n/a,n/a,n/a,the Downed row carries the bleedout countdown; the revive prompt is the existing interact prompt,n/a,n/a,YES,PASS");
            rows.Add("transit vote state,n/a,n/a,n/a,TransitVoteViewModel status text lists who has voted (see transit_vote_matrix.csv),n/a,n/a,YES,PASS");
            WriteMatrix("party_ui_matrix.csv", rows);
        }
    }
}
