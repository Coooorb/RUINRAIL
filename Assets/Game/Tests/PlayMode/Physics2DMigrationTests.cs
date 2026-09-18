using System.Collections.Generic;
using NUnit.Framework;
using RuinRail.Gameplay;
using UnityEngine;

namespace RuinRail.Tests.PlayMode
{
    /// <summary>
    /// TASK 177 — the deprecated NonAlloc queries were replaced with ContactFilter2D overloads. The one way that
    /// migration can silently break the game is trigger semantics: a default ContactFilter2D excludes triggers, and
    /// pickups, interactables and hazards are triggers. These tests pin the behaviour the old API had.
    /// </summary>
    public sealed class Physics2DMigrationTests
    {
        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private GameObject Collider(Vector2 position, bool isTrigger)
        {
            var go = new GameObject(isTrigger ? "Trigger" : "Solid");
            go.transform.position = position;
            var collider = go.AddComponent<CircleCollider2D>();
            collider.radius = 0.4f;
            collider.isTrigger = isTrigger;
            _spawned.Add(go);
            return go;
        }

        [Test]
        public void LegacyQueryFilter_MatchesTheProjectTriggerSetting_NotTheContactFilterDefault()
        {
            var filter = Physics2DQueries.LegacyQueryFilter();

            Assert.AreEqual(Physics2D.queriesHitTriggers, filter.useTriggers,
                "The NonAlloc overloads honoured Physics2D.queriesHitTriggers; the migration must too.");
            Assert.IsFalse(filter.useLayerMask, "The old calls queried all layers.");
            Assert.IsFalse(filter.useDepth);
            Assert.IsFalse(filter.useNormalAngle);

            Assert.AreNotEqual(new ContactFilter2D().useTriggers, filter.useTriggers,
                "A default-constructed ContactFilter2D excludes triggers, which is exactly the regression this guards.");
        }

        [Test]
        public void OverlapCircle_FindsTriggerColliders_AsTheNonAllocCallDid()
        {
            Collider(Vector2.zero, isTrigger: true);
            Physics2D.SyncTransforms();

            var results = new Collider2D[8];
            var count = Physics2D.OverlapCircle(Vector2.zero, 1f, Physics2DQueries.LegacyQueryFilter(), results);

            Assert.AreEqual(1, count,
                "Pickups, interactables and hazards are triggers; if the filter drops them, loot and doors stop working.");
        }

        [Test]
        public void OverlapCircle_FindsSolidAndTriggerColliders_AndRespectsTheBufferCap()
        {
            for (var i = 0; i < 6; i++) Collider(new Vector2(i * 0.1f, 0f), isTrigger: i % 2 == 0);
            Physics2D.SyncTransforms();

            var full = new Collider2D[8];
            Assert.AreEqual(6, Physics2D.OverlapCircle(Vector2.zero, 2f, Physics2DQueries.LegacyQueryFilter(), full),
                "Both solid and trigger colliders are returned.");

            var small = new Collider2D[3];
            Assert.AreEqual(3, Physics2D.OverlapCircle(Vector2.zero, 2f, Physics2DQueries.LegacyQueryFilter(), small),
                "The supported overload keeps the NonAlloc buffer semantics: fill the array, return how many were written, never resize.");
        }

        [Test]
        public void Raycast_AndCircleCast_KeepBufferSemanticsAndHitTriggers()
        {
            Collider(new Vector2(2f, 0f), isTrigger: true);
            Physics2D.SyncTransforms();

            var hits = new RaycastHit2D[4];
            Assert.AreEqual(1, Physics2D.Raycast(Vector2.zero, Vector2.right, Physics2DQueries.LegacyQueryFilter(), hits, 5f));
            Assert.AreEqual(0, Physics2D.Raycast(Vector2.zero, Vector2.right, Physics2DQueries.LegacyQueryFilter(), hits, 1f),
                "Distance still bounds the query.");

            var sweep = new RaycastHit2D[4];
            Assert.AreEqual(1, Physics2D.CircleCast(Vector2.zero, 0.2f, Vector2.right, Physics2DQueries.LegacyQueryFilter(), sweep, 5f),
                "Projectile sweeps must still find trigger-based targets.");
        }

        [Test]
        public void OverlapBox_FindsTargetsInsideTheZone_AndNotOutsideIt()
        {
            Collider(new Vector2(0.5f, 0f), isTrigger: false);
            Collider(new Vector2(6f, 0f), isTrigger: false);
            Physics2D.SyncTransforms();

            var results = new Collider2D[8];
            var count = Physics2D.OverlapBox(Vector2.zero, new Vector2(3f, 1f), 0f, Physics2DQueries.LegacyQueryFilter(), results);

            Assert.AreEqual(1, count, "Enemy Zone attacks must hit inside the telegraphed box and nothing beyond it.");
        }
    }
}
