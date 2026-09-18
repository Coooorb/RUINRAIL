using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Player;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The orange-line lock plate is gone: every unsealed socket of every biome's combat rooms draws the biome's
    /// pixel-art door (open housing while traversable, a real shutter/barrier while locked), the sprite covers the
    /// socket the blocker covers, the sprite and the collider switch together, and the doorway is physically open
    /// when it reads open. A sealed spare socket is wall and gets no door.
    /// </summary>
    public sealed class DoorVisualTests
    {
        private readonly List<Object> _created = new();
        private GameContentCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = GameContentCatalog.Load();
            RoomDoorLock.SkinResolver = biome => { var skin = _catalog.DoorSkinFor(biome); return skin != null ? new DoorSkinSprites(skin.Open, skin.Locked) : default; };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            RoomDoorLock.SkinResolver = null;
        }

        private (RoomRuntime runtime, RoomRoot root) Room(RoomDefinition definition, Vector2 origin)
        {
            var instance = Object.Instantiate(definition.Prefab, new Vector3(origin.x, origin.y, 0f), Quaternion.identity);
            _created.Add(instance);
            var root = instance.GetComponent<RoomRoot>();
            var runtime = instance.AddComponent<RoomRuntime>();
            runtime.Configure(root, 1, 1, 1);
            return (runtime, root);
        }

        private IEnumerable<RoomDefinition> CombatRooms() =>
            new[] { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs }
                .SelectMany(b => new[] { RoomSizeClass.Small, RoomSizeClass.Medium, RoomSizeClass.Large }
                    .Select(s => _catalog.Rooms.FirstOrDefault(r => r.Biome == b && r.RoomType == RoomType.Combat && r.SizeClass == s)))
                .Where(r => r != null);

        [Test]
        public void Catalog_BindsADistinctOpenAndLockedDoorSprite_PerBiome()
        {
            var seen = new HashSet<Sprite>();
            foreach (Biome biome in System.Enum.GetValues(typeof(Biome)))
            {
                var skin = _catalog.DoorSkinFor(biome);
                Assert.IsNotNull(skin, biome + " door skin");
                Assert.IsNotNull(skin.Open, biome + " open");
                Assert.IsNotNull(skin.Locked, biome + " locked");
                Assert.AreNotSame(skin.Open, skin.Locked);
                Assert.IsTrue(seen.Add(skin.Open) && seen.Add(skin.Locked), biome + " has its own art, not another biome's");
                Assert.AreEqual(new Vector2(2f, 1f), (Vector2)skin.Locked.bounds.size, biome + " door is 2×1 tiles (a 2-wide socket)");
                Assert.AreEqual(32f, skin.Locked.pixelsPerUnit);
            }
        }

        [Test]
        public void DoorArt_CarriesTheBiomeLampColours_AndTheLockedShutterFillsTheDoorway()
        {
            // Read the shipped PNGs directly (import settings are irrelevant to the pixel content).
            var metroLocked = Load(_catalog.DoorSkinFor(Biome.RuinedMetro).Locked);
            var rustLocked = Load(_catalog.DoorSkinFor(Biome.Rustworks).Locked);
            var labsLocked = Load(_catalog.DoorSkinFor(Biome.OvergrownLabs).Locked);
            var labsOpen = Load(_catalog.DoorSkinFor(Biome.OvergrownLabs).Open);
            var metroOpen = Load(_catalog.DoorSkinFor(Biome.RuinedMetro).Open);

            Assert.IsTrue(Has(metroLocked, "#C84B42"), "Metro locked: red lock lamp");
            Assert.IsTrue(Has(metroLocked, "#E7A74A"), "Metro locked: amber stencil accent");
            Assert.IsTrue(Has(rustLocked, "#C06434"), "Rustworks locked: hot oxide-orange lamp/chevrons");
            Assert.IsTrue(Has(labsLocked, "#C84B42"), "Labs locked: emergency red system light");
            Assert.IsTrue(Has(labsOpen, "#52A9A4"), "Labs open: cyan system light");
            Assert.IsFalse(Has(labsOpen, "#C84B42"), "Labs open: no red");
            Assert.IsFalse(Has(metroOpen, "#C84B42"), "Metro open: no red lamp");

            // The old plate was a flat bar of one colour; the shutter is real art: the doorway interior is mostly opaque
            // when locked and mostly clear when open.
            Assert.Greater(OpaqueFraction(metroLocked, 4, 0, 56, 28), 0.9f, "locked: the doorway between the posts is filled");
            Assert.Less(OpaqueFraction(metroOpen, 4, 2, 56, 26), 0.1f, "open: the doorway between the posts is clear");
            Assert.Greater(DistinctColours(metroLocked), 6, "pixel art, not a flat plate");
            Assert.Greater(DistinctColours(rustLocked), 6);
            Assert.Greater(DistinctColours(labsLocked), 6);
        }

        [UnityTest]
        public IEnumerator EveryUnsealedSocket_DrawsItsBiomeDoor_CoveringTheSocket_AndSwitchesWithTheBlocker()
        {
            var index = 0;
            var checkedSockets = 0;
            foreach (var definition in CombatRooms())
            {
                var (runtime, root) = Room(definition, new Vector2(500f + index++ * 60f, 500f));
                var skin = _catalog.DoorSkinFor(definition.Biome);
                var sockets = root.GetSockets();
                Assert.Greater(runtime.Doors.Count, 0, definition.Id);

                // Open on arrival: the housing only, nothing solid.
                foreach (var door in runtime.Doors)
                {
                    var label = definition.Id + " " + door.Socket.Direction;
                    Assert.IsNotNull(door.DoorRenderer, label + ": a real socket has a door visual");
                    Assert.IsTrue(door.IsOpenVisible, label + ": open housing drawn");
                    Assert.AreSame(skin.Open, door.DoorRenderer.sprite, label + ": the biome's open sprite");
                    Assert.IsFalse(door.IsBlocking, label);
                    Assert.IsFalse(DoorwayIsSolid(door), label + ": doorway physically open");
                }

                runtime.LockDoors();
                yield return new WaitForFixedUpdate();
                yield return null; // LateUpdate refresh
                foreach (var door in runtime.Doors)
                {
                    var label = definition.Id + " " + door.Socket.Direction;
                    Assert.IsTrue(door.IsBlocking, label + ": solid");
                    Assert.IsTrue(door.IsPlateVisible, label + ": shutter drawn");
                    Assert.AreSame(skin.Locked, door.DoorRenderer.sprite, label + ": the biome's locked sprite");
                    Assert.IsTrue(DoorwayIsSolid(door), label + ": doorway physically shut");
                    Assert.AreEqual(RuinRail.Core.Rendering.SortingLayers.LowProps, door.DoorRenderer.sortingLayerName, label);

                    // The sprite covers the socket the blocker covers (E/W sockets are rotated a quarter turn).
                    var (centre, size) = door.BlockerArea();
                    var bounds = door.DoorRenderer.bounds;
                    Assert.AreEqual(centre.x, bounds.center.x, 0.05f, label + ": centred on the socket");
                    Assert.AreEqual(centre.y, bounds.center.y, 0.05f, label);
                    Assert.AreEqual(size.x, bounds.size.x, 0.05f, label + ": covers the socket width");
                    Assert.AreEqual(size.y, bounds.size.y, 0.05f, label + ": one tile deep");
                    checkedSockets++;
                }

                runtime.UnlockDoors();
                yield return null;
                foreach (var door in runtime.Doors)
                {
                    var label = definition.Id + " " + door.Socket.Direction;
                    Assert.IsFalse(door.IsBlocking, label);
                    Assert.IsTrue(door.IsOpenVisible, label + ": back to the open housing");
                    Assert.IsFalse(DoorwayIsSolid(door), label + ": traversable again");
                }
            }

            Assert.GreaterOrEqual(checkedSockets, 20, "N/E/S/W sockets across three biomes and three size classes");
        }

        [UnityTest]
        public IEnumerator SealedSocket_GetsNoDoorVisual_AndStaysWall()
        {
            var definition = _catalog.Rooms.First(r => r.Biome == Biome.Rustworks && r.RoomType == RoomType.Combat && r.Prefab.GetComponent<RoomRoot>().GetSockets().Count >= 3);
            var instance = Object.Instantiate(definition.Prefab, new Vector3(700f, 700f, 0f), Quaternion.identity);
            _created.Add(instance);
            var root = instance.GetComponent<RoomRoot>();
            var spare = root.GetSockets().Last();
            Assert.IsTrue(RoomExitSealer.Seal(root, spare));
            var runtime = instance.AddComponent<RoomRuntime>();
            runtime.Configure(root, 1, 1, 1);
            yield return null;

            var sealedLock = runtime.Doors.FirstOrDefault(d => d.Socket == spare);
            if (sealedLock != null)
            {
                Assert.IsTrue(sealedLock.IsSealedSocket);
                Assert.IsNull(sealedLock.DoorRenderer, "a sealed socket is wall: no door drawn on it");
            }

            Assert.IsTrue(RoomExitSealer.IsSealed(root, spare));
            foreach (var door in runtime.Doors.Where(d => d.Socket != spare)) Assert.IsNotNull(door.DoorRenderer, "the real sockets keep their doors");
        }

        [UnityTest]
        public IEnumerator OpenDoor_LetsAPlayerBodyThrough_LockedDoorStopsIt()
        {
            var definition = CombatRooms().First(r => r.Biome == Biome.RuinedMetro);
            var (runtime, root) = Room(definition, new Vector2(800f, 800f));
            var door = runtime.Doors.First();
            var step = (Vector2)DoorDirections.Step(door.Socket.Direction);
            var (centre, _) = door.BlockerArea();
            var start = centre - step * 1.5f;

            var player = new GameObject("Walker");
            _created.Add(player);
            player.transform.position = start;
            var body = player.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            player.AddComponent<CircleCollider2D>().radius = 0.35f;
            CombatLayers.TagPlayerBody(player);

            // Open: walk out through the doorway.
            for (var i = 0; i < 60; i++) { body.linearVelocity = step * 4f; yield return new WaitForFixedUpdate(); }
            body.linearVelocity = Vector2.zero;
            var outside = Vector2.Dot((Vector2)player.transform.position - centre, step);
            Assert.Greater(outside, 0.5f, "the open door is traversable");

            // Back inside, then lock and try again.
            body.position = start;
            player.transform.position = start;
            yield return new WaitForFixedUpdate();
            runtime.LockDoors();
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(door.IsBlocking);
            for (var i = 0; i < 60; i++) { body.linearVelocity = step * 4f; yield return new WaitForFixedUpdate(); }
            body.linearVelocity = Vector2.zero;
            var pushed = Vector2.Dot((Vector2)player.transform.position - centre, step);
            Assert.Less(pushed, 0f, "the locked shutter is solid: the player stays on the inside of the socket");
        }

        private static bool DoorwayIsSolid(RoomDoorLock door)
        {
            var (centre, size) = door.BlockerArea();
            var hits = new Collider2D[8];
            var count = Physics2D.OverlapBox(centre, size * 0.9f, 0f, ContactFilter2D.noFilter, hits);
            for (var i = 0; i < count; i++)
                if (hits[i] != null && hits[i].enabled && !hits[i].isTrigger && hits[i].GetComponentInParent<EnvironmentObstacle>() != null && hits[i].transform.IsChildOf(door.transform)) return true;
            return false;
        }

        private Texture2D Load(Sprite sprite)
        {
            var path = UnityEditor.AssetDatabase.GetAssetPath(sprite);
            Assert.IsTrue(path.StartsWith("Assets/Game/Art/World/Doors/door_"), "door art lives in Art/World/Doors: " + path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            _created.Add(tex);
            Assert.IsTrue(tex.LoadImage(File.ReadAllBytes(path)), path);
            return tex;
        }

        private static bool Has(Texture2D tex, string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var wanted);
            var w = (Color32)wanted;
            foreach (var p in tex.GetPixels32()) if (p.a > 0 && p.r == w.r && p.g == w.g && p.b == w.b) return true;
            return false;
        }

        private static float OpaqueFraction(Texture2D tex, int x, int y, int w, int h)
        {
            var opaque = 0;
            for (var j = y; j < y + h; j++) for (var i = x; i < x + w; i++) if (tex.GetPixel(i, j).a > 0.5f) opaque++;
            return opaque / (float)(w * h);
        }

        private static int DistinctColours(Texture2D tex) => tex.GetPixels32().Where(p => p.a > 0).Select(p => (p.r, p.g, p.b)).Distinct().Count();
    }
}
