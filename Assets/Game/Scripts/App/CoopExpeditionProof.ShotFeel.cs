using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Items;
using RuinRail.Networking;
using RuinRail.Presentation.Vfx;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The <c>shotfeel</c> scenario: the host and then the client fire one trigger pull of a ballistic gun, a shotgun, a
    /// blaster and a rocket launcher at a frozen target, and both peers log what they drew — the muzzle flash (count and
    /// the profile's kind), the projectiles (on a gameplay pool on the shooter's peer, on a zero-damage presentation pool
    /// on the other), their profile, and every impact or blast. The host requires the same chain exactly once on both
    /// peers: no missing, duplicated or ownership-wrong effect.
    /// </summary>
    public sealed partial class CoopExpeditionProof
    {
        // The starter pistol last: each peer ends holding a gun its starting ammo feeds, for the boss fight that ends the run.
        private static readonly string[] ShotFeelWeapons = { "weapon_breacher_12", "weapon_pulse_carbine_b1", "weapon_pipe_launcher", "weapon_p9_ranger" };
        private GameObject _shotTarget;

        [Serializable]
        public sealed class ShotFeelLog
        {
            public string Weapon = "";
            public bool Fired;
            public int Flashes;
            public int FlashKind;
            public int GameplayShots;
            public int PresentationShots;
            public int Impacts;
            public int ImpactEffects;
            public int MaxImpactsPerShot;
            public int Explosions;
            public int ExplosionEffects;
            public string Profiles = "";
        }

        private IEnumerator ClientShotWatch(ProofMessage command)
        {
            var log = new ShotFeelLog();
            yield return RecordShotFeel(command.Seconds, command.Arg, command.Number == 1, log);
            Ack(command, JsonUtility.ToJson(log));
        }

        /// <summary>Where this peer's target is: the host's real (frozen) enemy, or the client's replica of it.</summary>
        private Vector2? ShotTargetPoint()
        {
            if (_run?.CoopHost != null)
                return _shotTarget != null ? (_shotTarget.GetComponentInChildren<CombatHurtbox>()?.AimPoint ?? (Vector2)_shotTarget.transform.position) : null;
            var replica = NearestReplica(LocalPlayer, -1);
            if (replica == null) return null;
            return replica.GetComponentInChildren<CombatHurtbox>() != null ? replica.GetComponentInChildren<CombatHurtbox>().AimPoint : (Vector2)replica.transform.position;
        }

        /// <summary>Records every shot effect this peer draws for <paramref name="seconds"/>; when <paramref name="fire"/>, this peer pulls the trigger once.</summary>
        private IEnumerator RecordShotFeel(float seconds, string weaponId, bool fire, ShotFeelLog log)
        {
            log.Weapon = weaponId;
            var feedback = _run.Feedback;
            var definition = _app.Configs.Resolve(weaponId) as WeaponDefinition;
            var profile = ProjectileVisualCatalog.Active?.Find(ProjectileVisualCatalog.ResolveWeaponVisualId(definition));
            var kind = profile != null && !string.IsNullOrEmpty(profile.MuzzleKind) ? profile.MuzzleKind : "muzzle";

            if (fire)
            {
                var inventory = _run.Rig.Inventory;
                inventory.Unequip(EquippedSlot.PrimaryWeapon);
                inventory.TryEquip(new ItemInstance(weaponId, 1, Rarity.Rare), EquippedSlot.PrimaryWeapon);
                yield return null;
                _run.Rig.Loadout.SelectSlot(WeaponSlot.Primary);
                yield return null;
            }

            var flashes = feedback.CountOf("muzzle");
            var flashKind = feedback.CountOf(kind);
            var impacts = feedback.CountOf("impact");
            var explosions = feedback.CountOf("explosion");
            var spawns = new Dictionary<ProjectilePool, int>();
            foreach (var pool in UnityEngine.Object.FindObjectsByType<ProjectilePool>(FindObjectsSortMode.None)) spawns[pool] = pool.SpawnCount;
            var perShot = new Dictionary<Projectile, int>();
            var profiles = new HashSet<string>();
            Action<Projectile, Vector2, bool> impacted = (p, _, _) =>
            {
                if (p == null || p.Data.SourceTeam != DamageTeam.Player) return;
                log.Impacts++;
                perShot[p] = (perShot.TryGetValue(p, out var n) ? n : 0) + 1;
            };
            Action<Projectile, Vector2> exploded = (p, _) => { if (p != null && p.Data.SourceTeam == DamageTeam.Player) log.Explosions++; };
            Projectile.AnyImpacted += impacted;
            Projectile.AnyExploded += exploded;
            try
            {
                var until = Time.realtimeSinceStartup + seconds;
                var fireAt = Time.realtimeSinceStartup + 0.8f;
                while (Time.realtimeSinceStartup < until)
                {
                    KeepTargetAlive();
                    if (fire && !log.Fired)
                    {
                        var target = ShotTargetPoint();
                        var player = LocalPlayer;
                        if (target.HasValue && player != null) _reader.AimAt(target.Value - (Vector2)player.GetComponent<RuinRail.Gameplay.Player.PlayerAiming>().AimOrigin);
                        if (Time.realtimeSinceStartup >= fireAt)
                        {
                            var weapon = _run.Rig.Loadout.ActiveWeapon;
                            if (weapon is RangedWeapon ranged) ranged.ApplyAuthoritativeState(ranged.Definition.MagazineSize, false);
                            log.Fired = weapon switch { RangedWeapon r => r.TryFire(), BlasterWeapon b => b.TryFire(), _ => false };
                        }
                    }

                    // Every player projectile in flight on this peer, by the profile it draws.
                    foreach (var visual in UnityEngine.Object.FindObjectsByType<ProjectileVisual>(FindObjectsSortMode.None))
                    {
                        var projectile = visual.GetComponent<Projectile>();
                        if (projectile != null && projectile.isActiveAndEnabled && projectile.Data.SourceTeam == DamageTeam.Player && visual.IsVisible) profiles.Add(visual.ProfileId);
                    }

                    yield return null;
                }
            }
            finally
            {
                Projectile.AnyImpacted -= impacted;
                Projectile.AnyExploded -= exploded;
            }

            log.Flashes = feedback.CountOf("muzzle") - flashes;
            log.FlashKind = feedback.CountOf(kind) - flashKind;
            log.ImpactEffects = feedback.CountOf("impact") - impacts;
            log.ExplosionEffects = feedback.CountOf("explosion") - explosions;
            log.MaxImpactsPerShot = perShot.Count == 0 ? 0 : perShot.Values.Max();
            foreach (var pool in UnityEngine.Object.FindObjectsByType<ProjectilePool>(FindObjectsSortMode.None))
            {
                var made = pool.SpawnCount - (spawns.TryGetValue(pool, out var before) ? before : 0);
                if (pool.IsPresentationOnly) log.PresentationShots += made; else log.GameplayShots += made;
            }

            log.Profiles = string.Join(",", profiles.OrderBy(p => p, StringComparer.Ordinal));
        }

        private void KeepTargetAlive()
        {
            var health = _shotTarget != null ? _shotTarget.GetComponent<HealthComponent>() : null;
            if (health != null && health.CurrentHealth < health.MaxHealth / 2) health.Heal(health.MaxHealth);
        }

        // ---------------------------------------------------------------- host side

        private IEnumerator ShotFeelScenario(List<ulong> clients)
        {
            var client = clients.First();
            yield return ReportAll();
            var hostView = View("host");
            _report.Views.Add(hostView);
            Record("identical D1 on both peers", clients.All(c => ViewOf(c) is { } v && v.LayoutFingerprint == hostView.LayoutFingerprint && v.RoomSignature == hostView.RoomSignature),
                $"host={hostView.Seed}/{hostView.LayoutFingerprint} " + string.Join(" ", clients.Select(c => $"c{c}={ViewOf(c)?.LayoutFingerprint}")));

            // Two clear firing rows in the start room, the frozen target between their far ends.
            var room = _run.Rooms[_run.Generation.Graph.StartId];
            var interior = room.InteriorWorldBounds;
            (Vector2 from, Vector2 to)? lane = null;
            for (var y = interior.yMin + 1.2f; y < interior.yMax - 2.6f && lane == null; y += 0.5f)
            for (var x = interior.xMin + 1.2f; x + 5f < interior.xMax - 0.8f && lane == null; x += 0.5f)
            {
                var a = new Vector2(x, y);
                bool Clear(Vector2 p) => !Physics2D.CircleCastAll(p, 0.5f, Vector2.right, 5f).Any(h => h.collider != null && !h.collider.isTrigger && h.collider.GetComponentInParent<EnvironmentObstacle>() != null);
                if (Clear(a) && Clear(a + Vector2.up * 1.4f)) lane = (a, a + Vector2.right * 5f);
            }

            if (lane == null) { Record("a clear firing lane in the start room", false, "none"); Finish("shotfeel", "no lane"); yield break; }
            var (from, to) = lane.Value;
            Teleport(HostPlayer, from);
            Teleport(_run.MemberEntity(client), from + Vector2.up * 1.4f);
            var content = _app.Content;
            var definition = content.Enemies.First(e => e.AttackKind == EnemyAttackKind.Moveset);
            var enemy = new DefaultEnemySpawner(content.Stagger).Spawn(definition, to + Vector2.up * 0.7f, HostPlayer.transform);
            room.BindEncounterBounds(enemy.gameObject);
            _run.BindEnemyPresentation(enemy);
            enemy.enabled = false;
            var body = enemy.GetComponent<Rigidbody2D>();
            body.linearVelocity = Vector2.zero;
            body.bodyType = RigidbodyType2D.Kinematic;
            enemy.GetComponent<HealthComponent>().SetMaxHealth(100000);
            enemy.GetComponent<HealthComponent>().Heal(100000);
            _shotTarget = enemy.gameObject;
            yield return WaitFor(() => _run.CoopHost.TryGetNetId(enemy.gameObject, out _), 5f);
            yield return Seconds(1.5f);

            foreach (var weapon in ShotFeelWeapons)
            {
                yield return ShotFeelWindow(client, weapon, true);
                yield return ShotFeelWindow(client, weapon, false);
            }

            _shotTarget = null;
            enemy.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(1000000));
            yield return Seconds(1f);

            // ---- end as every scenario does: the boss falls, the party returns ----
            yield return BossSteps(Clients, true);
            yield return Command(new ProofMessage { Step = "vote", Arg = "return" }, new[] { client }, 20f);
            yield return WaitFor(() => _summary != null, 20f);
            foreach (var member in Clients) SendProof(member, new ProofMessage { Step = "finish" });
            yield return HostEnd(Clients);
        }

        /// <summary>One trigger pull by the host or the client, recorded on both peers, then the exactly-once comparison.</summary>
        private IEnumerator ShotFeelWindow(ulong client, string weaponId, bool hostFires)
        {
            const float seconds = 3f;
            var hostLog = new ShotFeelLog();
            _acks.Remove(client);
            SendProof(client, new ProofMessage { Step = "shot-watch", Arg = weaponId, Seconds = seconds, Number = hostFires ? 0 : 1 });
            yield return RecordShotFeel(seconds, weaponId, hostFires, hostLog);
            yield return WaitFor(() => _acks.TryGetValue(client, out var a) && a.Step == "ack:shot-watch", 20f);
            var clientLog = _acks.TryGetValue(client, out var ack) && !string.IsNullOrEmpty(ack.Arg) ? JsonUtility.FromJson<ShotFeelLog>(ack.Arg) : null;
            var label = $"{weaponId} fired by the {(hostFires ? "host" : "client")}";
            if (clientLog == null) { Record($"shot feel exactly once: {label}", false, "the client sent no shot log"); yield break; }

            var definition = _app.Configs.Resolve(weaponId) as WeaponDefinition;
            var profile = ProjectileVisualCatalog.Active?.Find(ProjectileVisualCatalog.ResolveWeaponVisualId(definition));
            var pellets = definition is RangedWeaponDefinition r ? Mathf.Max(1, r.ProjectilesPerShot) : 1;
            var explosive = definition is RangedWeaponDefinition rr && rr.ExplosionRadiusTiles > 0f;
            var shooter = hostFires ? hostLog : clientLog;
            var remote = hostFires ? clientLog : hostLog;
            var problems = new List<string>();
            if (!shooter.Fired) problems.Add("the shooter's weapon did not fire");
            void Peer(string who, ShotFeelLog log, bool own)
            {
                if (log.Flashes != 1 || log.FlashKind != 1) problems.Add($"{who}: {log.Flashes} flashes ({log.FlashKind} of '{profile?.MuzzleKind}')");
                if (own && (log.GameplayShots != pellets || log.PresentationShots != 0)) problems.Add($"{who} (shooter): gameplay {log.GameplayShots}/{pellets}, presentation {log.PresentationShots}");
                if (!own && (log.GameplayShots != 0 || log.PresentationShots != pellets)) problems.Add($"{who} (remote): presentation {log.PresentationShots}/{pellets}, gameplay {log.GameplayShots}");
                if (log.Profiles != profile?.Id) problems.Add($"{who}: drew profiles [{log.Profiles}], expected {profile?.Id}");
                if (explosive)
                {
                    if (log.Explosions != 1 || log.ExplosionEffects != 1 || log.ImpactEffects != 0) problems.Add($"{who}: blasts {log.Explosions} (effects {log.ExplosionEffects}), impact effects {log.ImpactEffects}");
                }
                else
                {
                    if (log.Impacts < 1 || log.ImpactEffects != log.Impacts || log.MaxImpactsPerShot > 1 || log.Explosions != 0)
                        problems.Add($"{who}: impacts {log.Impacts}, impact effects {log.ImpactEffects}, most per shot {log.MaxImpactsPerShot}, blasts {log.Explosions}");
                }
            }

            Peer(hostFires ? "host" : "client", shooter, true);
            Peer(hostFires ? "client" : "host", remote, false);
            if (pellets == 1 && !explosive && remote.Impacts != shooter.Impacts) problems.Add($"impacts differ: shooter {shooter.Impacts}, remote {remote.Impacts}");
            Record($"shot feel exactly once: {label}", problems.Count == 0,
                $"shooter flash {shooter.Flashes} shots {shooter.GameplayShots}+{shooter.PresentationShots} impacts {shooter.Impacts}/{shooter.ImpactEffects} blasts {shooter.Explosions}/{shooter.ExplosionEffects} [{shooter.Profiles}]; " +
                $"remote flash {remote.Flashes} shots {remote.GameplayShots}+{remote.PresentationShots} impacts {remote.Impacts}/{remote.ImpactEffects} blasts {remote.Explosions}/{remote.ExplosionEffects} [{remote.Profiles}]; problems: {(problems.Count == 0 ? "none" : string.Join(" | ", problems))}");
            yield return Seconds(0.5f);
        }
    }
}
