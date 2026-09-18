using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Gameplay.Items;
using RuinRail.Presentation.Animation;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>Every one of the 33 weapon definitions reaches the runtime with a held world sprite bound through the content catalog.</summary>
    public sealed class HeldWeaponBindingTests
    {
        [Test]
        public void EveryWeaponDefinition_HasAHeldSpriteInTheCatalog_AuthoredPlusXWithAGripPivot()
        {
            var catalog = GameContentCatalog.Load();
            Assert.IsNotNull(catalog);
            var weapons = catalog.Items.OfType<WeaponDefinition>().ToList();
            Assert.AreEqual(33, weapons.Count, "the approved weapon count");
            CollectionAssert.IsEmpty(catalog.Problems());

            foreach (var weapon in weapons)
            {
                var sprite = catalog.WeaponSpriteFor(weapon.Id);
                Assert.IsNotNull(sprite, $"{weapon.Id}: no held sprite bound");
                Assert.AreEqual(weapon.Id, sprite.name, "the sprite is keyed by the weapon's stable id");
                // Grip pivot: on the left half of the sprite, so the pivot rotates about the hand and the muzzle is the +X end.
                var pivot01 = sprite.pivot.x / sprite.rect.width;
                Assert.Less(pivot01, 0.5f, $"{weapon.Id}: grip pivot on the left half ({pivot01:0.00})");
                Assert.Greater(sprite.bounds.max.x, 0f, $"{weapon.Id}: the muzzle end lies at +X of the pivot");
                Assert.AreEqual(FilterMode.Point, sprite.texture.filterMode, $"{weapon.Id}: pixel art is point filtered");
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sprite)) as TextureImporter;
                Assert.IsNotNull(importer);
                Assert.AreEqual(32, Mathf.RoundToInt(importer.spritePixelsPerUnit), $"{weapon.Id}: world PPU 32 (art/101)");
            }

            Assert.AreEqual(33, catalog.WeaponSprites.Count, "one held sprite per weapon, no strays");
        }

        [Test]
        public void HeldWeaponSortingRule_PutsTheWeaponBehindTheBodyOnlyWhenAimingAway()
        {
            Assert.IsTrue(HeldWeaponVisual.BehindBody(Vector2.up));
            Assert.IsTrue(HeldWeaponVisual.BehindBody(new Vector2(0.7f, 0.7f)));
            Assert.IsFalse(HeldWeaponVisual.BehindBody(Vector2.right));
            Assert.IsFalse(HeldWeaponVisual.BehindBody(Vector2.down));
            Assert.IsFalse(HeldWeaponVisual.BehindBody(new Vector2(-0.95f, 0.3f)));
        }
    }
}
