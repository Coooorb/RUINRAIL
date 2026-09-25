using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Player;
using RuinRail.Presentation;
using RuinRail.Presentation.Animation;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// Regression: the player composition must carry a drawn body. The animation driver only chooses clips; the
    /// sprite renderer + animator it draws into come from <see cref="CharacterVisual"/>, and the player's animation
    /// set has to reach a built player through the content catalog. Every state × facing the driver can request must
    /// resolve to a sprite, so no gameplay state ever blanks the player.
    /// </summary>
    public sealed class PlayerVisibilityTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void ContentCatalog_CarriesThePlayerAnimationSet_WithEveryClipTheDriverCanRequest()
        {
            var catalog = GameContentCatalog.Load();
            Assert.IsNotNull(catalog);
            var set = catalog.AnimationSetFor(CharacterVisual.PlayerActorId);
            Assert.IsNotNull(set, "GameContentCatalog.AnimationSets must include the 'player' set (rebuild via RuinRail/Production/Build Game Content Catalog).");
            CollectionAssert.IsEmpty(set.Missing(AnimationRules.PlayerClipKeys), "every player clip role × 8 facings is authored");
            CollectionAssert.IsEmpty(catalog.Problems());
        }

        [Test]
        public void PlayerBody_IsVisibleAndAboveTheFloor_InEveryStateAndFacing()
        {
            var catalog = GameContentCatalog.Load();
            var player = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "Player", IsLocal = true, InputReader = new FakePlayerInputReader(), BalanceConfig = catalog.PlayerBalance });
            _created.Add(player);
            var animator = CharacterVisual.Attach(player, catalog.AnimationSetFor(CharacterVisual.PlayerActorId));
            var driver = player.AddComponent<PlayerAnimationDriver>();
            driver.Configure(animator, player.GetComponent<PlayerLifeStateComponent>(), player.GetComponent<PlayerDash>(), player.GetComponent<PlayerAiming>(), null, player.GetComponent<Rigidbody2D>());

            var renderer = CharacterVisual.RendererOf(player);
            Assert.IsNotNull(renderer, "a SpriteRenderer child exists");
            Assert.IsTrue(renderer.enabled);
            Assert.IsTrue(renderer.gameObject.activeInHierarchy);
            Assert.AreEqual(Color.white, renderer.color, "no tint, alpha 1");
            Assert.AreEqual(SortingLayers.Characters, renderer.sortingLayerName);
            Assert.IsNotNull(renderer.GetComponent<YSorter>(), "characters are y-sorted at the feet (art/102)");
            var order = SortingLayers.Ordered.ToList();
            Assert.Greater(order.IndexOf(renderer.sortingLayerName), order.IndexOf(SortingLayers.LowProps), "above floor, floor details and low props");

            driver.Tick(0f);
            Assert.AreEqual(PlayerAnimState.Idle, driver.State);
            Assert.IsNotNull(renderer.sprite, "the first tick assigns a sprite");
            Assert.IsFalse(animator.IsPlaceholder);

            foreach (var key in AnimationRules.PlayerClipKeys)
            foreach (var facing in AnimationRules.AllFacings)
            {
                animator.Play(key, facing);
                animator.Tick(0.2f);
                Assert.IsNotNull(renderer.sprite, $"{key}/{facing} draws a frame");
                Assert.IsFalse(animator.IsPlaceholder, $"{key}/{facing} is an authored clip, not a placeholder hold");
            }

            Assert.AreSame(animator, CharacterVisual.Attach(player, null), "attaching twice reuses the one body rather than stacking a second renderer");
            Assert.AreEqual(1, player.GetComponentsInChildren<SpriteRenderer>(true).Count(r => r.GetComponentInParent<RuinRail.Gameplay.Combat.Projectiles.Projectile>(true) == null), "one body renderer (pooled projectile sprites excluded)");
        }

        [Test]
        public void CharacterVisual_WithoutASet_StillCreatesAnEnabledRenderer_AndNeverThrows()
        {
            var actor = new GameObject("Actor");
            _created.Add(actor);
            var animator = CharacterVisual.Attach(actor, null);
            Assert.IsNotNull(animator);
            Assert.IsTrue(animator.Renderer.enabled);
            animator.Play("Idle", BodyFacing8.S);
            animator.Tick(0.1f);
            Assert.IsTrue(animator.IsPlaceholder, "missing art is reported as the explicit placeholder path");
        }
    }
}
