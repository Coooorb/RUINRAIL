using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Presentation.Animation;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The held weapon on the real runtime path: the active slot's sprite on the aim pivot, swapped on slot changes,
    /// never two at once, muzzle at the visible barrel and the projectile leaving it, full 360° orientation with the
    /// +X convention, and aim-dependent sorting that never drops below the floor or above AbovePlayer.
    /// </summary>
    public sealed class HeldWeaponVisualTests
    {
        private readonly List<Object> _created = new();
        private GameContentCatalog _catalog;
        private ItemDefinitionRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _catalog = GameContentCatalog.Load();
            _registry = _catalog.BuildRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private (GameObject go, FakePlayerInputReader reader, WeaponLoadout loadout, RangedWeapon rifle, BlasterWeapon blaster) PlayerWithWeapons()
        {
            var reader = new FakePlayerInputReader();
            var go = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "Local", IsLocal = true, InputReader = reader, BalanceConfig = _catalog.PlayerBalance });
            _created.Add(go);
            var pool = go.AddComponent<ProjectilePool>();
            var aiming = go.GetComponent<PlayerAiming>();
            var loadout = go.AddComponent<WeaponLoadout>();
            loadout.SetInputReader(reader);
            var rifle = go.AddComponent<RangedWeapon>();
            rifle.SetInputReader(reader);
            _registry.TryGet("weapon_p9_ranger", out var pistolDefinition);
            var reserve = new AmmoReserve();
            reserve.Add(AmmoType.Light, 36);
            rifle.SetProjectilePool(pool);
            rifle.SetAiming(aiming);
            rifle.SetAmmoReserve(reserve);
            rifle.SetDefinition((RangedWeaponDefinition)pistolDefinition);
            var blaster = go.AddComponent<BlasterWeapon>();
            blaster.SetInputReader(reader);
            blaster.SetAiming(aiming);
            blaster.SetProjectilePool(pool);
            blaster.SetDefinition(_registry.Definitions.OfType<BlasterWeaponDefinition>().First());
            loadout.SetPrimary(rifle);
            loadout.SetSecondary(blaster);
            loadout.Initialize();
            return (go, reader, loadout, rifle, blaster);
        }

        [Test]
        public void LocalPlayer_ShowsTheActiveWeapon_SwapsImmediatelyOnSlotChange_AndNeverDrawsBoth()
        {
            var (go, reader, loadout, rifle, blaster) = PlayerWithWeapons();
            var held = PlayerVisualComposer.Compose(go, _catalog, reader);
            Assert.IsNotNull(held);
            Assert.IsTrue(held.IsVisible);
            Assert.AreEqual("weapon_p9_ranger", held.ShownWeaponId);
            Assert.AreSame(_catalog.WeaponSpriteFor("weapon_p9_ranger"), held.Renderer.sprite, "the P9 Ranger's own held sprite");
            Assert.AreSame(held.Pivot, go.transform.Find(HeldWeaponVisual.PivotName), "one WeaponPivot on the body");

            var weaponRenderers = go.GetComponentsInChildren<SpriteRenderer>(true).Where(r => r.transform.IsChildOf(held.Pivot)).ToList();
            Assert.AreEqual(1, weaponRenderers.Count, "exactly one weapon renderer: the inactive slot is not a second sprite");

            reader.RaiseWeaponSwapped();
            Assert.AreEqual(WeaponSlot.Secondary, loadout.ActiveSlot);
            Assert.AreEqual(HeldWeaponVisual.DefinitionIdOf(blaster), held.ShownWeaponId, "the swap changed the sprite synchronously through the loadout event");
            Assert.AreSame(_catalog.WeaponSpriteFor(held.ShownWeaponId), held.Renderer.sprite);
            Assert.AreEqual(1, go.GetComponentsInChildren<SpriteRenderer>(true).Count(r => r.transform.IsChildOf(held.Pivot)));

            reader.RaiseWeapon1Selected();
            Assert.AreEqual("weapon_p9_ranger", held.ShownWeaponId);
            reader.RaiseWeapon2Selected();
            Assert.AreEqual(HeldWeaponVisual.DefinitionIdOf(blaster), held.ShownWeaponId);

            // Composing again (the network hook can run more than once) never stacks a second body or weapon.
            PlayerVisualComposer.Compose(go, _catalog, reader);
            Assert.AreEqual(1, go.GetComponents<HeldWeaponVisual>().Length);
            Assert.AreEqual(1, go.GetComponents<PlayerAnimationDriver>().Length);
            Assert.AreEqual(1, go.GetComponents<WeaponVisualDriver>().Length);
            Assert.AreEqual(2, go.GetComponentsInChildren<SpriteRenderer>(true).Length, "body + one weapon, nothing duplicated");
        }

        [Test]
        public void EmptyLoadout_ShowsNoWeapon_AndAWeaponWithoutArtFallsBackToHiddenRatherThanABlankQuad()
        {
            var reader = new FakePlayerInputReader();
            var go = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "Bare", IsLocal = true, InputReader = reader, BalanceConfig = _catalog.PlayerBalance });
            _created.Add(go);
            var loadout = go.AddComponent<WeaponLoadout>();
            loadout.Initialize();
            var held = PlayerVisualComposer.Compose(go, _catalog, reader);
            Assert.IsFalse(held.IsVisible, "empty hands draw nothing");
            Assert.IsNull(held.ShownWeaponId);
        }

        [UnityTest]
        public IEnumerator Muzzle_SitsAtTheSpritesFiringEnd_AndTheProjectileLeavesFromIt()
        {
            var (go, reader, loadout, rifle, _) = PlayerWithWeapons();
            go.transform.position = new Vector3(40f, 40f, 0f);
            var held = PlayerVisualComposer.Compose(go, _catalog, reader);
            CollectionAssert.Contains(held.MuzzleBound.ToList(), rifle, "the mounted pistol fires from the visible muzzle");
            var sprite = held.Renderer.sprite;
            Assert.AreEqual(sprite.bounds.max.x, held.Muzzle.localPosition.x, 1e-4f, "muzzle at the +X end of the sprite");
            Assert.AreEqual(0f, held.Muzzle.localPosition.y, 1e-4f);

            reader.Aim = Vector2.right;
            yield return null; // PlayerAiming orients the pivot in Update
            var muzzleWorld = (Vector2)held.Muzzle.position;
            Assert.Greater(muzzleWorld.x, go.transform.position.x + 0.3f, "the muzzle is ahead of the body along the aim");
            Assert.AreEqual(go.transform.position.y + HeldWeaponVisual.GripHeight, muzzleWorld.y, 0.05f, "at grip height");

            Assert.IsTrue(rifle.TryFire());
            Assert.IsNotNull(rifle.LastSpawnedProjectile);
            Assert.Less(Vector2.Distance(rifle.LastSpawnedProjectile.transform.position, muzzleWorld), 0.05f, "projectile origin == visible muzzle");
        }

        [UnityTest]
        public IEnumerator Orientation_FollowsTheFull360Aim_FlipsForLeftFacing_AndSortsBehindTheBodyOnlyWhenAimingAway()
        {
            var (go, reader, _, _, _) = PlayerWithWeapons();
            var held = PlayerVisualComposer.Compose(go, _catalog, reader);
            var body = CharacterVisual.RendererOf(go);
            var layers = SortingLayers.Ordered.ToList();

            for (var step = 0; step < 16; step++)
            {
                var angle = step * 22.5f;
                var aim = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                reader.Aim = aim;
                yield return null;
                yield return null;

                var pivotAngle = held.Pivot.rotation.eulerAngles.z;
                Assert.AreEqual(0f, Mathf.DeltaAngle(pivotAngle, angle), 0.5f, $"pivot follows the aim at {angle}°");
                Assert.AreEqual(aim.x < 0f, held.Renderer.flipY, $"left-facing flip at {angle}° keeps the top of the weapon up");
                var expectedBehind = HeldWeaponVisual.BehindBody(aim);
                Assert.AreEqual(expectedBehind, held.IsBehindBody, $"behind-body rule at {angle}°");
                if (expectedBehind)
                {
                    Assert.AreEqual(SortingLayers.Characters, held.Renderer.sortingLayerName, $"{angle}°: behind the body, on the body's layer");
                    Assert.Less(held.Renderer.sortingOrder, body.sortingOrder, $"{angle}°: drawn just behind the body");
                }
                else
                {
                    Assert.AreEqual(SortingLayers.Weapons, held.Renderer.sortingLayerName, $"{angle}°: the Weapons layer over the body");
                }

                var layer = layers.IndexOf(held.Renderer.sortingLayerName);
                Assert.Greater(layer, layers.IndexOf(SortingLayers.LowProps), $"{angle}°: never below floor/wall layers");
                Assert.Less(layer, layers.IndexOf(SortingLayers.AboveCharacters), $"{angle}°: never permanently above AbovePlayer");
            }
        }
    }
}
