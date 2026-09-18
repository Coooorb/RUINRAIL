using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Combat;
using RuinRail.Presentation.Vfx;
using RuinRail.UI.Hud;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Enemy health presentation: the compact world bar is hidden at full health, appears with the first point of
    /// damage, maps the fraction to whole pixels, goes with death, is built once (never per damage tick), keeps its
    /// world alignment, and shows replicated health on a client. Elites carry the wider accented bar; bosses use the
    /// dedicated screen bar that is up only while the encounter is active.
    /// </summary>
    public sealed class EnemyHealthBarTests
    {
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp() => DamageAuthority.LocalIsAuthoritative = true;

        [TearDown]
        public void TearDown()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private (GameObject go, HealthComponent health, WorldHealthBar bar) Enemy(int maxHealth, WorldHealthBar.Style style, Color? fill = null)
        {
            var go = new GameObject("Enemy");
            _created.Add(go);
            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Enemy);
            var health = go.AddComponent<HealthComponent>();
            health.SetMaxHealth(maxHealth);
            var bar = go.AddComponent<WorldHealthBar>();
            bar.Configure(health, style, 1.35f, fill);
            return (go, health, bar);
        }

        private static int PixelWidth(WorldHealthBar bar) => Mathf.RoundToInt(bar.transform.Find("HealthBar/Fill").localScale.x);

        [Test]
        public void NormalEnemy_HiddenAtFullHealth_VisibleFromFirstDamage_FractionInWholePixels_HiddenAgainWhenHealedFull_AndOnDeath()
        {
            var (go, health, bar) = Enemy(100, WorldHealthBar.Style.Normal);
            Assert.IsFalse(bar.IsVisible, "full health: no bar");
            Assert.AreEqual(WorldHealthBar.NormalWidthPx, bar.WidthPx);
            var childrenBefore = go.GetComponentsInChildren<SpriteRenderer>(true).Length;
            Assert.AreEqual(3, childrenBefore, "border, back, fill — built once");

            health.TryApplyDamage(new DamageRequest(1));
            Assert.IsTrue(bar.IsVisible, "visible immediately after the first damage");
            Assert.AreEqual(0.99f, bar.Fill01, 0.001f);
            Assert.AreEqual(WorldHealthBar.NormalWidthPx - 2, PixelWidth(bar), "99% rounds up to the full inner width");

            health.TryApplyDamage(new DamageRequest(49));
            Assert.AreEqual(0.5f, bar.Fill01, 0.001f);
            Assert.AreEqual((WorldHealthBar.NormalWidthPx - 2) / 2, PixelWidth(bar), "half health = half the inner pixels");
            for (var i = 0; i < 20; i++) health.TryApplyDamage(new DamageRequest(1));
            Assert.AreEqual(childrenBefore, go.GetComponentsInChildren<SpriteRenderer>(true).Length, "no objects created per damage tick");

            health.Heal(1000);
            Assert.IsFalse(bar.IsVisible, "back to full: hidden again");

            health.TryApplyDamage(new DamageRequest(1));
            Assert.IsTrue(bar.IsVisible);
            health.TryApplyDamage(new DamageRequest(1000));
            Assert.IsFalse(health.IsAlive);
            Assert.IsFalse(bar.IsVisible, "dead: the bar goes");
        }

        [Test]
        public void BarGeometry_CompactPixelBar_WithDarkBackAndOneMillimetreBorder()
        {
            var (go, health, bar) = Enemy(10, WorldHealthBar.Style.Normal);
            health.TryApplyDamage(new DamageRequest(3));
            var border = go.transform.Find("HealthBar/Border").GetComponent<SpriteRenderer>();
            var back = go.transform.Find("HealthBar/Back").GetComponent<SpriteRenderer>();
            var fill = go.transform.Find("HealthBar/Fill").GetComponent<SpriteRenderer>();
            Assert.AreEqual(WorldHealthBar.NormalWidthPx, Mathf.RoundToInt(border.transform.localScale.x), "24 px wide at reference resolution");
            Assert.AreEqual(WorldHealthBar.HeightPx, Mathf.RoundToInt(border.transform.localScale.y), "4 px tall including the border");
            Assert.AreEqual(WorldHealthBar.NormalWidthPx - 2, Mathf.RoundToInt(back.transform.localScale.x), "1 px border each side");
            Assert.AreEqual(WorldHealthBar.HeightPx - 2, Mathf.RoundToInt(back.transform.localScale.y));
            Assert.Less(back.color.grayscale, 0.25f, "dark back");
            Assert.Less(border.color.grayscale, 0.1f, "near-black border");
            Assert.Greater(fill.color.r, 0.7f, "high-contrast fill");
            foreach (var r in new[] { border, back, fill }) Assert.AreEqual(SortingLayers.UIWorld, r.sortingLayerName, "world UI layer: above characters and props in every biome");
            Assert.Greater(fill.sortingOrder, back.sortingOrder);
            Assert.Greater(back.sortingOrder, border.sortingOrder);
        }

        [UnityTest]
        public IEnumerator Bar_FollowsTheEnemy_AndNeverRotatesWithIt()
        {
            var (go, health, bar) = Enemy(50, WorldHealthBar.Style.Normal);
            health.TryApplyDamage(new DamageRequest(10));
            go.transform.position = new Vector3(12f, 7f, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, 135f);
            yield return null;
            var root = go.transform.Find("HealthBar");
            Assert.Less(Quaternion.Angle(Quaternion.identity, root.rotation), 0.01f, "world-aligned regardless of the body rotation");
            Assert.AreEqual(12f, root.position.x, 0.04f, "follows x");
            Assert.AreEqual(7f + 1.35f, root.position.y, 0.04f, "sits above the enemy");
            Assert.AreEqual(0f, Mathf.Repeat(root.position.x * 32f, 1f), 0.01f, "pixel-snapped");
        }

        [Test]
        public void Elite_UsesTheWiderBar_InTheEliteAccent()
        {
            var accent = new Color(0.95f, 0.6f, 0.2f, 1f);
            var (_, health, elite) = Enemy(200, WorldHealthBar.Style.Elite, accent);
            var (_, _, normal) = Enemy(200, WorldHealthBar.Style.Normal);
            Assert.AreEqual(WorldHealthBar.EliteWidthPx, elite.WidthPx);
            Assert.Greater(elite.WidthPx, normal.WidthPx, "visibly stronger than a normal enemy's");
            Assert.LessOrEqual(elite.WidthPx, 36, "still unobtrusive");
            Assert.AreEqual(accent, elite.FillColor, "Elite status shown through the existing Elite accent");
            Assert.AreNotEqual(elite.FillColor, normal.FillColor);
            health.TryApplyDamage(new DamageRequest(1));
            Assert.IsTrue(elite.IsVisible);
        }

        [Test]
        public void Client_ShowsReplicatedAuthoritativeHealth()
        {
            var (_, health, bar) = Enemy(80, WorldHealthBar.Style.Normal);
            DamageAuthority.LocalIsAuthoritative = false;
            Assert.IsFalse(bar.IsVisible);
            health.ApplyReplicatedHealth(20, 80);
            Assert.IsTrue(bar.IsVisible, "replicated damage shows the bar on a client");
            Assert.AreEqual(0.25f, bar.Fill01, 0.001f);
            health.ApplyReplicatedHealth(0, 80);
            Assert.IsFalse(bar.IsVisible, "replicated death hides it");
        }

        [Test]
        public void Boss_UsesTheDedicatedScreenBar_OnlyWhileTheEncounterIsActive_WithNameAndFraction()
        {
            var go = new GameObject("Boss");
            _created.Add(go);
            var health = go.AddComponent<HealthComponent>();
            health.SetMaxHealth(1000);
            var active = false;
            var vm = new DungeonHudViewModel();
            var view = DungeonHudView.Create(vm);
            _created.Add(view.gameObject);
            vm.BindBoss("The Conductor", health, () => active);
            Assert.IsFalse(vm.Snapshot.BossVisible, "boss bar is not up before the encounter starts");
            Assert.IsFalse(view.BossVisible);

            active = true;
            vm.Tick();
            Assert.IsTrue(vm.Snapshot.BossVisible);
            Assert.IsTrue(view.BossVisible);
            StringAssert.Contains("The Conductor", view.BossNameText);
            StringAssert.Contains("1000 / 1000", view.BossNameText);
            Assert.AreEqual(1f, view.BossFill, 0.001f);

            health.TryApplyDamage(new DamageRequest(250));
            Assert.AreEqual(0.75f, vm.Snapshot.BossHp01, 0.001f);
            Assert.AreEqual(0.75f, view.BossFill, 0.001f);
            StringAssert.Contains("750 / 1000", view.BossNameText);

            Assert.IsNull(go.GetComponent<WorldHealthBar>(), "no redundant small world bar on a boss");

            health.TryApplyDamage(new DamageRequest(5000));
            Assert.IsFalse(vm.Snapshot.BossVisible, "defeated: the bar goes");
            Assert.IsFalse(view.BossVisible);

            // The boss bar sits between the top-left block and the coins block.
            var boss = view.BossPanel;
            var x = 320f + boss.anchoredPosition.x - boss.sizeDelta.x * 0.5f;
            Assert.Greater(x, 266f, "clear of the depth/objective block");
            Assert.Less(x + boss.sizeDelta.x, 514f, "clear of the coins block");
        }
    }
}
