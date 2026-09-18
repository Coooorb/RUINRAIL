using System.Collections.Generic;
using NUnit.Framework;
using RuinRail.Core.Rendering;
using RuinRail.Dungeon.Grid;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Player;
using RuinRail.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

namespace RuinRail.Tests
{
    /// <summary>TASK 137 — pixel-perfect camera rig: follow/aim/bounds, spectator follow limited to teammates, sorting of characters/weapons/projectiles/tilemaps, lighting applier.</summary>
    public class CameraRigTests
    {
        private readonly List<Object> _created = new();
        private PlayerBalanceConfig _balance;
        private CameraRigConfig _config;

        [SetUp]
        public void SetUp()
        {
            _balance = AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            _config = AssetDatabase.LoadAssetAtPath<CameraRigConfig>("Assets/Game/ScriptableObjects/Presentation/CameraRigConfig.asset");
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private CameraRig Rig()
        {
            var go = new GameObject("CameraRig");
            _created.Add(go);
            go.AddComponent<Camera>();
            var rig = go.AddComponent<CameraRig>();
            rig.SetConfig(_config);
            return rig;
        }

        private (GameObject go, FakePlayerInputReader reader, PlayerLifeStateComponent life, HealthComponent health) Player(string name, PartyLifeRoster roster, Vector2 position)
        {
            var reader = new FakePlayerInputReader();
            var go = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = name, IsLocal = true, InputReader = reader, BalanceConfig = _balance, Position = position, LifeRoster = roster, ParticipantId = name });
            _created.Add(go);
            return (go, reader, go.GetComponent<PlayerLifeStateComponent>(), go.GetComponent<HealthComponent>());
        }

        [Test]
        public void Rig_ConfiguresPixelPerfectCamera_FollowsOnTheGrid_WithSubtleAimOffset_AndClampsToBounds()
        {
            var rig = Rig();
            Assert.IsTrue(rig.Camera.orthographic);
            Assert.AreEqual(5.625f, rig.Camera.orthographicSize, 1e-5f);
            var pp = rig.PixelPerfect;
            Assert.IsNotNull(pp);
            Assert.AreEqual(32, pp.assetsPPU);
            Assert.AreEqual(640, pp.refResolutionX);
            Assert.AreEqual(360, pp.refResolutionY);
            Assert.AreEqual(PixelPerfectCamera.GridSnapping.PixelSnapping, pp.gridSnapping, "No sub-pixel sprite movement.");
            Assert.AreEqual(PixelPerfectCamera.CropFrame.None, pp.cropFrame);

            var target = new GameObject("Target").transform;
            _created.Add(target.gameObject);
            target.position = new Vector3(12.3f, 4.7f, 0f);
            rig.SetFollow(target);
            rig.Step(1f / 60f);
            Assert.IsTrue(CameraFraming.IsOnPixelGrid(rig.Position, 32));
            Assert.AreEqual(CameraFraming.SnapToPixelGrid(target.position, 32), rig.Position, "First frame snaps to the target.");

            var aimPoint = new Vector2(40f, 4.7f);
            rig.SetAim(() => aimPoint);
            for (var i = 0; i < 120; i++)
            {
                target.position += new Vector3(0.05f, 0f, 0f);
                rig.Step(1f / 60f);
                Assert.IsTrue(CameraFraming.IsOnPixelGrid(rig.Position, 32), $"Frame {i} off grid: {rig.Position}");
            }

            var lead = rig.Position.x - target.position.x;
            Assert.Greater(lead, 0f, "The view leans toward the aim…");
            Assert.LessOrEqual(lead, _config.AimOffsetMaxTiles + 0.5f, "…subtly.");

            rig.SetVisibleBounds(new Rect(0f, 0f, 30f, 20f));
            target.position = new Vector3(200f, 200f, 0f);
            for (var i = 0; i < 120; i++) rig.Step(1f / 60f);
            Assert.LessOrEqual(rig.Position.x + rig.HalfExtents.x, 30f + 1e-3f, "Right edge never beyond the visible world.");
            Assert.LessOrEqual(rig.Position.y + rig.HalfExtents.y, 20f + 1e-3f);
            Assert.AreEqual(0f, rig.Camera.transform.position.z, "Rig never changes the camera depth.");
        }

        [Test]
        public void DeadSpectator_CameraFollowsOnlyLivingTeammates_NeverAFreePosition()
        {
            var roster = new PartyLifeRoster();
            var (localGo, localReader, local, localHealth) = Player("Local", roster, Vector2.zero);
            var (bGo, _, _, _) = Player("B", roster, new Vector2(6f, 0f));
            var (cGo, _, _, _) = Player("C", roster, new Vector2(12f, 0f));
            var follow = localGo.GetComponent<DeadSpectatorFollow>();
            var rig = Rig();
            rig.SetVisibleBounds(new Rect(-50f, -50f, 100f, 100f));
            rig.SetFollow(() => follow.FollowPosition);
            rig.Step(1f / 60f);
            Assert.AreEqual(Vector2.zero, rig.Position);

            localHealth.TryApplyDamage(new DamageRequest(999999));
            local.Tick(20.01f);
            Assert.AreEqual(PlayerLifeState.Dead, local.State);
            for (var i = 0; i < 240; i++) rig.Step(1f / 60f);
            Assert.AreEqual(6f, rig.Position.x, 0.05f, "Spectating B: the camera is at B.");
            localReader.RaiseInteract();
            for (var i = 0; i < 240; i++) rig.Step(1f / 60f);
            Assert.AreEqual(12f, rig.Position.x, 0.05f, "Interact cycles to C; the camera goes there and nowhere else.");

            // Spectator input never moves the camera freely.
            localReader.Move = Vector2.up;
            for (var i = 0; i < 60; i++) rig.Step(1f / 60f);
            Assert.AreEqual(12f, rig.Position.x, 0.05f);
            Assert.AreEqual(0f, rig.Position.y, 0.05f);

            // Losing the follow target keeps the last framed position rather than roaming.
            rig.SetFollow((System.Func<Vector2>)null);
            rig.Step(1f / 60f);
            Assert.AreEqual(12f, rig.Position.x, 0.05f);
            Assert.IsFalse(rig.HasTarget);
            Object.DestroyImmediate(bGo); Object.DestroyImmediate(cGo);
        }

        [Test]
        public void Sorting_CharactersWeaponsProjectiles_AndTilemapLayers_RenderInTheApprovedLayers()
        {
            var body = new GameObject("Body").AddComponent<SpriteRenderer>();
            _created.Add(body.gameObject);
            body.transform.position = new Vector3(0f, 2f, 0f);
            var sorter = SpriteSorting.Attach(body, SortingRole.Character);
            Assert.IsNotNull(sorter);
            Assert.AreEqual("Characters", body.sortingLayerName);
            Assert.AreEqual(-64, body.sortingOrder);
            body.transform.position = new Vector3(0f, 1f, 0f);
            sorter.Refresh();
            Assert.AreEqual(-32, body.sortingOrder, "Lower on screen draws on top of a character further up.");

            var weapon = new GameObject("Weapon").AddComponent<SpriteRenderer>();
            _created.Add(weapon.gameObject);
            weapon.transform.SetParent(body.transform, false);
            SpriteSorting.Attach(weapon, SortingRole.Weapon);
            Assert.AreEqual("Weapons", weapon.sortingLayerName);
            Assert.Greater(SortingLayer.GetLayerValueFromName(weapon.sortingLayerName), SortingLayer.GetLayerValueFromName(body.sortingLayerName), "Aimed weapon over the body in every direction.");

            var projectile = new GameObject("Projectile").AddComponent<SpriteRenderer>();
            _created.Add(projectile.gameObject);
            SpriteSorting.Apply(projectile, SortingRole.Projectile);
            Assert.AreEqual("Projectiles", projectile.sortingLayerName);
            Assert.IsNull(projectile.GetComponent<YSorter>());

            var grid = RoomGridBuilder.CreateRoomGrid("SortRoom");
            _created.Add(grid.gameObject);
            string LayerOf(string name) => grid.transform.Find(name).GetComponent<TilemapRenderer>().sortingLayerName;
            Assert.AreEqual("Ground", LayerOf("Floor"));
            Assert.AreEqual("GroundDetails", LayerOf("FloorDetail"));
            Assert.AreEqual("LowProps", LayerOf("Walls"));
            Assert.AreEqual("AboveCharacters", LayerOf("AbovePlayer"));
            int V(string layer) => SortingLayer.GetLayerValueFromName(layer);
            Assert.Less(V(LayerOf("Floor")), V(body.sortingLayerName), "Characters never under the floor.");
            Assert.Less(V(LayerOf("Walls")), V(body.sortingLayerName));
            Assert.Less(V(body.sortingLayerName), V(LayerOf("AbovePlayer")));
            Assert.Less(V(LayerOf("AbovePlayer")), V(projectile.sortingLayerName), "Projectiles never under the upper walls.");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Rooms/RuinedMetro/Metro_Boss_01.prefab");
            Assert.IsNotNull(prefab);
            var floor = System.Array.Find(prefab.GetComponentsInChildren<Tilemap>(true), t => t.gameObject.name == "Floor");
            Assert.IsNotNull(floor);
            Assert.AreEqual("Ground", floor.GetComponent<TilemapRenderer>().sortingLayerName, "Baked rooms carry the convention.");
        }

        [Test]
        public void LightingApplier_ConfiguresAGlobalLightFromTheProfile_AndKeepsFullVisibility()
        {
            var go = new GameObject("Lighting");
            _created.Add(go);
            var applier = go.AddComponent<BiomeLightingApplier>();
            var profile = AssetDatabase.LoadAssetAtPath<BiomeLightingProfile>("Assets/Game/ScriptableObjects/Presentation/Lighting_Rustworks.asset");
            applier.Apply(profile);
            Assert.IsNotNull(applier.GlobalLight);
            Assert.AreEqual(Light2D.LightType.Global, applier.GlobalLight.lightType);
            Assert.GreaterOrEqual(applier.GlobalLight.intensity, 1f);
            Assert.AreEqual(profile.GlobalLightColor, applier.GlobalLight.color);
            Assert.AreSame(profile, applier.Applied);
        }
    }
}
