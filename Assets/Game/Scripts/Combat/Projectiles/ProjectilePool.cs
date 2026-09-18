using System.Collections.Generic;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Projectiles
{
    public sealed class ProjectilePool : MonoBehaviour
    {
        [SerializeField] private Projectile _projectilePrefab;
        [SerializeField] private int _initialSize = 8;

        private readonly Stack<Projectile> _inactive = new();

        /// <summary>Total spawns since creation (diagnostics/tests).</summary>
        public int SpawnCount { get; private set; }

        private void Awake()
        {
            for (var i = 0; i < _initialSize; i++)
            {
                _inactive.Push(CreateInstance());
            }
        }

        public Projectile Spawn(Vector2 position, ProjectileSpawnData data)
        {
            var projectile = _inactive.Count > 0 ? _inactive.Pop() : CreateInstance();

            projectile.transform.position = position;
            projectile.gameObject.SetActive(true);
            projectile.Activate(data);
            SpawnCount++;

            return projectile;
        }

        public void Return(Projectile projectile)
        {
            projectile.gameObject.SetActive(false);
            _inactive.Push(projectile);
        }

        private Projectile CreateInstance()
        {
            Projectile instance;

            if (_projectilePrefab != null)
            {
                instance = Instantiate(_projectilePrefab, transform);
            }
            else
            {
                var instanceObject = new GameObject("Projectile");
                instanceObject.transform.SetParent(transform, false);
                var collider = instanceObject.AddComponent<CircleCollider2D>();
                collider.isTrigger = true;
                collider.radius = 0.15f;
                instance = instanceObject.AddComponent<Projectile>();
            }

            instance.SetPool(this);
            instance.gameObject.SetActive(false);
            return instance;
        }
    }
}
