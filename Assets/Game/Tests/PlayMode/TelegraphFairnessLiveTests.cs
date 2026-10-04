using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Networking;
using RuinRail.Presentation.Animation;
using RuinRail.Presentation.Vfx;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The red telegraph is a fairness contract: whenever a telegraphed attack damages the player, the player is visibly
    /// inside its red area on that frame — some opaque pixel of the frame the player's body is drawing overlaps a pixel the
    /// attacker's marker paints. Checked on the real composed player in a live run (its real colliders, its own shots in
    /// flight), at the frame the damage lands.
    /// </summary>
    public sealed class TelegraphFairnessLiveTests
    {
        private GameApp _app;
        private string _saveDir;

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (root.GetComponents<Component>().Any(c => c != null && (c.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools"))) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            NetworkPlayerObject.VisualComposer = null;
            RoomDoorLock.SkinResolver = null;
            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        // ---------------------------------------------------------------- the visibility oracle

        /// <summary>The shipped player sheet, decoded readable so the drawn frame's opaque pixels can be read.</summary>
        internal static class PlayerSilhouette
        {
            private static Texture2D _sheet;

            private static Texture2D Sheet
            {
                get
                {
                    if (_sheet != null) return _sheet;
                    _sheet = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    _sheet.LoadImage(File.ReadAllBytes("Assets/Game/Art/Characters/player/player_sheet.png"));
                    return _sheet;
                }
            }

            /// <summary>World-pixel squares (lower-left corners in world units) of every opaque pixel the body draws now.</summary>
            public static List<Vector2> DrawnPixels(GameObject player)
            {
                var result = new List<Vector2>();
                var renderer = CharacterVisual.RendererOf(player);
                if (renderer == null || renderer.sprite == null || !renderer.enabled) return result;
                var sprite = renderer.sprite;
                var rect = sprite.rect;
                var pivot = sprite.pivot;
                var origin = (Vector2)renderer.transform.position;
                var ppu = sprite.pixelsPerUnit;
                var sheet = Sheet;
                for (var y = 0; y < (int)rect.height; y++)
                for (var x = 0; x < (int)rect.width; x++)
                {
                    if (sheet.GetPixel((int)rect.x + x, (int)rect.y + y).a <= 0f) continue;
                    var lx = renderer.flipX ? pivot.x - x - 1f : x - pivot.x;
                    result.Add(origin + new Vector2(lx / ppu, (y - pivot.y) / ppu));
                }

                return result;
            }

            /// <summary>True when any drawn pixel square overlaps a pixel the marker paints.</summary>
            /// <summary>True when the drawn body overlaps any red marker drawn on screen right now.</summary>
            public static bool OverlapsAnyShown(List<Vector2> drawn) =>
                Object.FindObjectsByType<TelegraphMarkerView>(FindObjectsSortMode.None).Any(v => v.IsVisible && Overlaps(drawn, v));

            public static bool Overlaps(List<Vector2> drawn, TelegraphMarkerView view)
            {
                if (view == null || view.CoveredPixels == 0 || !view.IsVisible) return false;
                const int Ppu = TelegraphMarkerView.PixelsPerUnit;
                foreach (var corner in drawn)
                {
                    var x0 = Mathf.FloorToInt(corner.x * Ppu + 0.0001f);
                    var y0 = Mathf.FloorToInt(corner.y * Ppu + 0.0001f);
                    var x1 = Mathf.CeilToInt((corner.x + 1f / Ppu) * Ppu - 0.0001f) - 1;
                    var y1 = Mathf.CeilToInt((corner.y + 1f / Ppu) * Ppu - 0.0001f) - 1;
                    for (var y = y0; y <= y1; y++)
                    for (var x = x0; x <= x1; x++)
                        if (view.PaintsPixel(x, y)) return true;
                }

                return false;
            }

            /// <summary>Distance in world pixels from the drawn body to the nearest painted marker pixel (diagnostics).</summary>
            public static float NearestGapPixels(List<Vector2> drawn, TelegraphMarkerView view)
            {
                if (view == null) return float.PositiveInfinity;
                const int Ppu = TelegraphMarkerView.PixelsPerUnit;
                var best = float.PositiveInfinity;
                foreach (var corner in drawn)
                {
                    var cx = Mathf.FloorToInt(corner.x * Ppu);
                    var cy = Mathf.FloorToInt(corner.y * Ppu);
                    for (var r = 0; r <= 24 && r < best; r++)
                    for (var dy = -r; dy <= r; dy++)
                    for (var dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        if (view.PaintsPixel(cx + dx, cy + dy)) best = Mathf.Min(best, r);
                    }
                }

                return best;
            }
        }

        /// <summary>
        /// Every damage the player takes, judged against the red drawn at that moment; a hit a damaging floor's own tick
        /// explains (same frame, same amount — floors are no telegraph) is set apart, every other hit is judged.
        /// </summary>
        internal sealed class HitJudge
        {
            private readonly List<(int Frame, int Amount, bool Visible, float Gap)> _hits = new();
            private readonly List<(int Frame, int Amount)> _floor = new();
            private readonly List<(RuinRail.Gameplay.Combat.Hazards.HazardVolume Volume, System.Action<IDamageable, int> Handler)> _volumes = new();

            public HitJudge(GameObject player)
            {
                var health = player.GetComponent<HealthComponent>();
                health.Damaged += amount =>
                {
                    var drawn = PlayerSilhouette.DrawnPixels(player);
                    var visible = PlayerSilhouette.OverlapsAnyShown(drawn);
                    var gap = visible ? 0f : Object.FindObjectsByType<TelegraphMarkerView>(FindObjectsSortMode.None).Where(v => v.IsVisible).Select(v => PlayerSilhouette.NearestGapPixels(drawn, v)).DefaultIfEmpty(float.PositiveInfinity).Min();
                    _hits.Add((Time.frameCount, amount, visible, gap));
                    if (!visible)
                    {
                        var at = (Vector2)player.transform.position;
                        var near = Object.FindObjectsByType<RuinRail.Gameplay.Enemies.EnemyController>(FindObjectsSortMode.None)
                            .Where(e => e != null && Vector2.Distance(e.transform.position, at) < 5f)
                            .Select(e => $"{e.Definition?.Id}:{e.State} d={Vector2.Distance(e.transform.position, at):0.0} telegraph={(e.GetComponent<TelegraphIndicator>() != null)}");
                        var actors = Object.FindObjectsByType<MovesetActorController>(FindObjectsSortMode.None)
                            .Where(a => a != null && Vector2.Distance(a.transform.position, at) < 6f)
                            .Select(a => $"{a.name}:{a.State} {a.CurrentAttack?.name} d={Vector2.Distance(a.transform.position, at):0.0} windows={a.Resolver.WindowsFired}/{(a.Resolver.Current != null ? a.Resolver.Current.HitCount : 0)}");
                        var shots = Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None).Count(p => p.Data.SourceTeam == DamageTeam.Enemy && Vector2.Distance(p.transform.position, at) < 1.5f);
                        _details.Add((Time.frameCount, amount, $"{amount}hp at {at}: enemies [{string.Join("; ", near)}] actors [{string.Join("; ", actors)}] enemy shots near {shots}"));
                    }
                };
                _health = health;
                Refresh();
            }

            private readonly HealthComponent _health;
            private readonly List<(int Frame, int Amount, string Text)> _details = new();

            /// <summary>Picks up damaging volumes created since (an attack's lingering burn zone, a new room's floor).</summary>
            public void Refresh()
            {
                foreach (var volume in Object.FindObjectsByType<RuinRail.Gameplay.Combat.Hazards.HazardVolume>(FindObjectsSortMode.None))
                {
                    if (_volumes.Any(v => v.Volume == volume)) continue;
                    var health = _health;
                    System.Action<IDamageable, int> handler = (target, amount) => { if (ReferenceEquals(target, health)) _floor.Add((Time.frameCount, amount)); };
                    volume.Ticked += handler;
                    _volumes.Add((volume, handler));
                }
            }

            public List<string> Details => _details.Where(d => !_floor.Any(f => f.Frame == d.Frame && f.Amount == d.Amount)).Select(d => d.Text).ToList();
            public int FloorHits => _hits.Count(IsFloor);
            public List<(int Amount, bool Visible, float Gap)> Telegraphed => _hits.Where(h => !IsFloor(h)).Select(h => (h.Amount, h.Visible, h.Gap)).ToList();

            private bool IsFloor((int Frame, int Amount, bool Visible, float Gap) hit) => _floor.Any(f => f.Frame == hit.Frame && f.Amount == hit.Amount);
        }

        // ---------------------------------------------------------------- live harness

        private IEnumerator Boot(int seed)
        {
            RuinRail.Core.Input.GameplayInputGate.Reset();
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_fairness_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(seed);
            SceneManager.LoadScene(SceneNames.MainMenu);
            var deadline = Time.realtimeSinceStartup + 90f;
            while (_app.ComposedScene != SceneNames.MainMenu) { Assert.Less(Time.realtimeSinceStartup, deadline); yield return null; }
            _app.Menu.Play();
            while (_app.ComposedScene != SceneNames.Base) { Assert.Less(Time.realtimeSinceStartup, deadline); yield return null; }
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Fairness");
            hub.Onboarding.AcknowledgeStarterKit();
            _app.Settings.Current.Tutorial.ShowPrompts = false;
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            while (_app.ComposedScene != SceneNames.Dungeon) { Assert.Less(Time.realtimeSinceStartup, deadline); yield return null; }
            for (var i = 0; i < 12; i++) yield return null;
        }

        private static void Put(GameObject entity, Vector2 p)
        {
            var body = entity.GetComponent<Rigidbody2D>();
            entity.transform.position = p;
            if (body != null) { body.position = p; body.linearVelocity = Vector2.zero; }
            Physics2D.SyncTransforms();
        }

        /// <summary>
        /// A spot for the player such that the segment from <paramref name="from"/> to <paramref name="to"/> (offsets from the
        /// spot) and the player's own surroundings are free of walls and props within <paramref name="clearance"/>.
        /// </summary>
        internal static Vector2 ClearLaneSpot(Rect interior, Vector2 from, Vector2 to, float clearance)
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.NoFilter();
            filter.useTriggers = false;
            var hits = new RaycastHit2D[16];
            for (var y = interior.yMin + 2f; y <= interior.yMax - 2f; y += 0.5f)
            for (var x = interior.xMin + 3f; x <= interior.xMax - 7f; x += 0.5f)
            {
                var spot = new Vector2(x, y);
                var a = spot + from;
                var b = spot + to;
                if (!interior.Contains(a) || !interior.Contains(b)) continue;
                var count = Physics2D.CircleCast(a, clearance * 0.5f, (b - a).normalized, filter, hits, Vector2.Distance(a, b));
                var blocked = false;
                for (var i = 0; i < count && !blocked; i++)
                    if (hits[i].collider.GetComponentInParent<EnvironmentObstacle>() != null || hits[i].collider.GetComponentInParent<IDamageable>() != null) blocked = true;
                if (!blocked) return spot;
            }

            Assert.Fail("no clear lane in the room");
            return interior.center;
        }

        private static EliteDefinition WithMoveset(EliteDefinition source, params EnemyAttackDefinition[] moves)
        {
            var copy = Object.Instantiate(source);
            var so = new SerializedObject(copy);
            var list = so.FindProperty("_moveset");
            list.arraySize = moves.Length;
            for (var i = 0; i < moves.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = moves[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return copy;
        }

        private static EnemyAttackDefinition Attack(string name) =>
            AssetDatabase.LoadAssetAtPath<EnemyAttackDefinition>($"Assets/Game/ScriptableObjects/Enemies/Attacks/{name}.asset");

        /// <summary>
        /// One attack by a real Elite (its shipping presentation, its telegraph marker) at the live player, aimed at a decoy
        /// so the player's place relative to the footprint is exact; <paramref name="during"/> may move the player or do
        /// anything else each frame. Returns every damage the player took with whether it was visibly inside the marker.
        /// </summary>
        private IEnumerator Strike(ExpeditionScene run, EnemyAttackDefinition attack, Vector2 elitePos, Vector2 decoyPos, Vector2 playerPos,
            List<(int Amount, bool Visible, float Gap, Vector2 Player)> hits, System.Action<float> during = null, float seconds = 0f)
        {
            var player = run.Rig.Player;
            var health = player.GetComponent<HealthComponent>();
            health.Heal(100000);
            var definition = WithMoveset(_app.Content.Elites.First(), attack);
            var room = run.Rooms[run.Generation.Graph.StartId];
            var decoy = new GameObject("Decoy");
            decoy.transform.position = decoyPos;
            Put(player, playerPos);
            var encounter = new DefaultEliteSpawner(_app.Content.Stagger).Spawn(definition, elitePos, room.transform, decoy.transform);
            var elite = encounter.Elite;
            run.BindActorPresentation(elite, definition.Id, true);
            var indicator = elite.GetComponent<TelegraphIndicator>();
            System.Action<int> damaged = amount =>
            {
                var drawn = PlayerSilhouette.DrawnPixels(player);
                var view = indicator != null ? indicator.View : null;
                hits.Add((amount, PlayerSilhouette.Overlaps(drawn, view), PlayerSilhouette.NearestGapPixels(drawn, view), player.transform.position));
            };
            health.Damaged += damaged;
            var started = Time.time;
            var deadline = Time.time + Mathf.Max(seconds, attack.TelegraphSeconds + attack.HitCount * attack.HitIntervalSeconds + 4.5f);
            var sawAttack = false;
            var resolvedAt = -1f;
            while (Time.time < deadline)
            {
                if (elite.State == MovesetActorState.Telegraph || elite.State == MovesetActorState.Attacking) sawAttack = true;
                // Shots and bombs still in the air land after the attack resolves: keep the actor (and its pool) a while.
                if (sawAttack && resolvedAt < 0f && elite.State == MovesetActorState.Recovery) resolvedAt = Time.time;
                if (resolvedAt >= 0f && Time.time - resolvedAt > 1.5f && seconds <= 0f) break;
                if (resolvedAt >= 0f && elite.State == MovesetActorState.Telegraph) elite.SetTarget(null); // one attack only
                during?.Invoke(Time.time - started);
                if (health.CurrentHealth < health.MaxHealth * 0.5f) health.Heal(100000);
                yield return null;
            }

            health.Damaged -= damaged;
            Assert.IsTrue(sawAttack, attack.name + ": the Elite performed the attack");
            Object.DestroyImmediate(encounter.gameObject);
            Object.DestroyImmediate(decoy);
            for (var i = 0; i < 3; i++) yield return null;
        }

        /// <summary>
        /// The three reproductions: a slam whose red edge lies just below the player's feet, the player's own shot in flight
        /// inside a slam while the player stands far outside it, and an enemy shot whose lane passes just below the feet.
        /// In each the player is visibly outside the red; none may hurt the player.
        /// </summary>
        [UnityTest]
        public IEnumerator VisiblyOutsideTheRed_NeverDamaged_EdgeBelowTheFeet_OwnShotInsideASlam_LaneBelowTheFeet()
        {
            yield return Boot(11);
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var room = run.Rooms[run.Generation.Graph.StartId];
            var c = (Vector2)room.InteriorWorldBounds.center;
            var report = new List<string>();
            var failures = new List<string>();
            foreach (var col in run.Rig.Player.GetComponentsInChildren<Collider2D>(true))
                report.Add($"player collider '{col.gameObject.name}' {col.GetType().Name} trigger={col.isTrigger} enabled={col.enabled && col.gameObject.activeInHierarchy} bounds={col.bounds.size} layer={LayerMask.LayerToName(col.gameObject.layer)}");

            // R1 — a slam ring (radius R) whose top edge is 0.3 tiles below the player's feet.
            var slam = Attack("Attack_TunnelMaw_Roar");
            var hits = new List<(int, bool, float, Vector2)>();
            yield return Strike(run, slam, c, c + Vector2.down * 3f, c + Vector2.up * (slam.HitRadius + 0.3f), hits);
            report.Add($"R1 slam edge 0.3 below the feet: {hits.Count} hit(s) " + string.Join(",", hits.Select(h => $"{h.Item1}hp visible={h.Item2} gap={h.Item3}px")));
            failures.AddRange(hits.Where(h => !h.Item2).Select(h => $"R1: {h.Item1} damage while visibly outside (gap {h.Item3} px)"));

            // R2 — the player 2 tiles outside the slam, firing: its own shot sits inside the ring when the slam lands.
            hits = new List<(int, bool, float, Vector2)>();
            var playerAt = c + Vector2.left * (slam.HitRadius + 2f);
            var shotPool = run.Rig.Projectiles;
            yield return Strike(run, slam, c, c + Vector2.down * 3f, playerAt, hits, t =>
            {
                if (shotPool.SpawnedProjectilesActive() == 0)
                    shotPool.Spawn(c + new Vector2(-1.4f, -1.0f), new ProjectileSpawnData(1, 0.001f, 1000f, 0f, 0f, Vector2.right, run.Rig.Player, null, 0f, DamageTeam.Player, false, null));
            });
            report.Add($"R2 own shot inside the slam, player 2 tiles out: {hits.Count} hit(s) " + string.Join(",", hits.Select(h => $"{h.Item1}hp visible={h.Item2} gap={h.Item3}px")));
            failures.AddRange(hits.Where(h => !h.Item2).Select(h => $"R2: {h.Item1} damage while visibly outside (gap {h.Item3} px)"));

            foreach (var shot in shotPool.GetComponentsInChildren<Projectile>(false)) shotPool.Return(shot);

            // R3 — a single-lane volley aimed 0.45 tiles below the player's feet.
            var volley = _app.Content.Elites.SelectMany(e => e.Moveset).First(a => a != null && a.Motion == AttackMotion.Projectile && a.ProjectileCount == 1);
            hits = new List<(int, bool, float, Vector2)>();
            var p3 = ClearLaneSpot(room.InteriorWorldBounds, new Vector2(-2f, -0.45f), new Vector2(6.6f, -0.45f), 1.2f);
            var closest = float.MaxValue;
            var shots = 0;
            var minX = float.MaxValue;
            var ys = new HashSet<string>();
            var eliteAt = "";
            yield return Strike(run, volley, p3 + new Vector2(6f, -0.45f), p3 + new Vector2(-2f, -0.45f), p3, hits, _ =>
            {
                foreach (var shot in Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None))
                {
                    if (shot.Data.SourceTeam != DamageTeam.Enemy) continue;
                    shots++;
                    closest = Mathf.Min(closest, Vector2.Distance(shot.transform.position, p3));
                    minX = Mathf.Min(minX, shot.transform.position.x - p3.x);
                    ys.Add((shot.transform.position.y - p3.y).ToString("0.00"));
                    if (eliteAt.Length == 0 && shot.Data.Source != null) eliteAt = ((Vector2)shot.Data.Source.transform.position - p3).ToString();
                }
            });
            foreach (var col in Physics2D.OverlapCircleAll(p3 + new Vector2(1.05f, -0.45f), 0.35f))
                report.Add($"  at the stop point: '{col.gameObject.name}' {col.GetType().Name} trigger={col.isTrigger} parent='{(col.transform.parent != null ? col.transform.parent.name : "-")}' damageable={col.GetComponentInParent<IDamageable>() != null} obstacle={col.GetComponentInParent<EnvironmentObstacle>() != null}");
            report.Add($"R3 lane 0.45 below the feet ({volley.name}): {hits.Count} hit(s) " + string.Join(",", hits.Select(h => $"{h.Item1}hp visible={h.Item2} gap={h.Item3}px")) + $"; enemy shot samples {shots}, closest approach to the feet {closest:0.00}, shots reached dx {minX:0.00}, dy {string.Join("/", ys.Take(6))}, elite at {eliteAt}");
            failures.AddRange(hits.Where(h => !h.Item2).Select(h => $"R3: {h.Item1} damage while visibly outside (gap {h.Item3} px)"));

            // R4 — a charge deflected sideways by a body in its lane (busy fights): the player stands just outside the lane
            // on the side the dashing body is pushed towards.
            var charge = Attack("Attack_CryoEnforcer_EnforcerCharge");
            var p4 = ClearLaneSpot(room.InteriorWorldBounds, new Vector2(-4f, 1.4f), new Vector2(4f, 1.4f), 2.6f);
            var hurtboxTop = CombatHurtbox.PlayerBodyOffset.y + CombatHurtbox.PlayerBodySize.y * 0.5f;
            var laneY = p4.y + hurtboxTop + charge.HitRadius + 0.12f;
            var blocker = new RuinRail.Gameplay.Enemies.DefaultEnemySpawner(_app.Content.Stagger).Spawn(_app.Content.Enemies.First(e => e.Id == "brute"), new Vector2(p4.x - 0.6f, laneY + 0.55f), null);
            blocker.enabled = false;
            var blockerBody = blocker.GetComponent<Rigidbody2D>();
            blockerBody.bodyType = RigidbodyType2D.Static;
            hits = new List<(int, bool, float, Vector2)>();
            yield return Strike(run, charge, new Vector2(p4.x - 3.5f, laneY), new Vector2(p4.x + 3.5f, laneY), p4, hits);
            Object.DestroyImmediate(blocker.gameObject);
            report.Add($"R4 charge deflected by a body toward a player whose drawn body is 0.12 outside its lane: {hits.Count} hit(s) " + string.Join(",", hits.Select(h => $"{h.Item1}hp visible={h.Item2} gap={h.Item3}px")));
            failures.AddRange(hits.Where(h => !h.Item2).Select(h => $"R4: {h.Item1} damage while visibly outside (gap {h.Item3} px)"));

            // Controls: visibly inside, the same attacks still land — the fix must not make the player unhittable.
            hits = new List<(int, bool, float, Vector2)>();
            yield return Strike(run, slam, c, c + Vector2.down * 3f, c + Vector2.up * (slam.HitRadius - 0.15f), hits);
            report.Add($"C1 feet 0.15 inside the slam: {hits.Count} hit(s) " + string.Join(",", hits.Select(h => $"{h.Item1}hp visible={h.Item2}")));
            if (hits.Count == 0) failures.Add("C1: a slam over the feet did not land");
            failures.AddRange(hits.Where(h => !h.Item2).Select(h => $"C1: {h.Item1} damage while visibly outside (gap {h.Item3} px)"));
            hits = new List<(int, bool, float, Vector2)>();
            yield return Strike(run, volley, p3 + new Vector2(6f, 0.1f), p3 + new Vector2(-2f, 0.1f), p3, hits);
            report.Add($"C3 lane through the feet ({volley.name}, {volley.HitCount} volleys): {hits.Count} hit(s) " + string.Join(",", hits.Select(h => $"{h.Item1}hp visible={h.Item2}")));
            if (hits.Count < volley.HitCount) failures.Add($"C3: only {hits.Count}/{volley.HitCount} shots through the feet landed");
            failures.AddRange(hits.Where(h => !h.Item2).Select(h => $"C3: {h.Item1} damage while visibly outside (gap {h.Item3} px)"));

            Debug.Log("[FAIRNESS]\n" + string.Join("\n", report));
            Assert.IsEmpty(failures, string.Join("\n", failures) + "\n" + string.Join("\n", report));
        }

        /// <summary>
        /// Every shipped Elite and Boss attack, performed by a real Elite at the live player: the player stands just outside
        /// its footprint above it (where nothing of the player is drawn below the feet) and beside it, and just inside it.
        /// Every damage that lands finds the player's drawn body on a drawn red pixel; the inside placements do land.
        /// </summary>
        [UnityTest, Timeout(3600000)]
        public IEnumerator EveryEliteAndBossAttack_AtTheFootprintEdge_DamagesOnlyAVisiblyTouchingPlayer()
        {
            yield return Boot(11);
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var room = run.Rooms[run.Generation.Graph.StartId];
            var interior = room.InteriorWorldBounds;
            var attacks = _app.Content.Bosses.SelectMany(b => b.Moveset.Concat(b.PhaseTwoArenaHazards)).Concat(_app.Content.Elites.SelectMany(e => e.Moveset))
                .Where(a => a != null).Distinct().ToList();
            var failures = new List<string>();
            var report = new List<string>();
            var insideHits = 0;
            var insideTried = 0;
            var shapes = new List<AttackFootprint.Shape>();
            foreach (var attack in attacks)
            {
                // The Elite stands west of centre, the decoy east of it inside the attack's trigger band: direction +x.
                var reach = Mathf.Clamp((attack.MinTriggerRange + attack.MaxTriggerRange) * 0.5f, attack.MinTriggerRange + 0.1f, Mathf.Max(attack.MinTriggerRange + 0.1f, attack.MaxTriggerRange - 0.1f));
                var elitePos = new Vector2(interior.xMin + 2.5f, interior.center.y);
                var decoyPos = elitePos + Vector2.right * reach;
                shapes.Clear();
                TelegraphIndicator.FootprintOf(attack, elitePos, Vector2.right, null, shapes, out _, out _);
                var s = shapes[shapes.Count / 2];
                // Edge points: the top edge (the player stands above it) and the far / east edge (beside it).
                var top = new Vector2(s.Form == FootprintForm.Circle ? s.Centre.x : (s.Start.x + s.End.x) * 0.5f, s.Centre.y + s.Size.y * 0.5f);
                if (s.Form == FootprintForm.Circle) top = s.Centre + Vector2.up * s.Radius;
                var east = s.Form == FootprintForm.Circle ? s.Centre + Vector2.right * s.Radius
                    : s.Form == FootprintForm.Box ? s.Centre + s.Direction * (s.Size.x * 0.5f) : s.End + s.Direction * s.Radius;
                var placements = new List<(string Label, Vector2 Player, bool Inside)>
                {
                    ("above +0.04", top + Vector2.up * 0.04f, false),
                    ("beside +0.04", east + Vector2.right * (CombatHurtbox.PlayerFeetSize.x * 0.5f + 0.04f), false),
                    ("above -0.12", top + Vector2.down * 0.12f, true),
                };
                foreach (var (label, playerPos, inside) in placements)
                {
                    if (!interior.Contains(playerPos)) continue;
                    var hits = new List<(int, bool, float, Vector2)>();
                    yield return Strike(run, attack, elitePos, decoyPos, playerPos, hits);
                    if (inside) { insideTried++; if (hits.Count > 0) insideHits++; }
                    foreach (var h in hits.Where(h => !h.Item2)) failures.Add($"{attack.name} {label}: {h.Item1} damage while visibly outside (gap {h.Item3} px)");
                    report.Add($"{attack.name} {attack.Motion} {label}: {hits.Count} hit(s)");
                }
            }

            Debug.Log("[FAIRNESS-SWEEP]\n" + string.Join("\n", report) + $"\ninside placements landed {insideHits}/{insideTried}");
            Assert.IsEmpty(failures, string.Join("\n", failures));
            Assert.Greater(insideHits, insideTried / 2, "the inside placements land: the player is still hittable inside the red");
        }

        /// <summary>
        /// A busy fight of normal enemies (shooter, bomber, melee grunt, charger, Brute) and the biome's Elite around the live
        /// player, who strafes in and out of their markers near a wall: every hit lands on a visibly-touching player.
        /// </summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator BusyNormalAndEliteFight_Strafing_EveryHitIsVisiblyInsideTheRed([Values(11, 27)] int seed)
        {
            yield return Boot(seed);
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var content = _app.Content;
            var room = run.Rooms[run.Generation.Graph.StartId];
            var interior = room.InteriorWorldBounds;
            var player = run.Rig.Player;
            var health = player.GetComponent<HealthComponent>();
            var centre = new Vector2(interior.xMin + 3.5f, interior.center.y);
            Put(player, centre);
            var judge = new HitJudge(player);

            void Normal(RuinRail.Gameplay.Enemies.EnemyDefinition definition, Vector2 offset)
            {
                var enemy = new RuinRail.Gameplay.Enemies.DefaultEnemySpawner(content.Stagger).Spawn(definition, centre + offset, player.transform);
                if (enemy.GetComponent<ProjectilePool>() == null) enemy.gameObject.AddComponent<ProjectilePool>();
                room.BindEncounterBounds(enemy.gameObject);
                run.BindEnemyPresentation(enemy);
            }

            var kinds = new[] { RuinRail.Gameplay.Enemies.EnemyAttackKind.Projectile, RuinRail.Gameplay.Enemies.EnemyAttackKind.Lob, RuinRail.Gameplay.Enemies.EnemyAttackKind.MeleeContact,
                RuinRail.Gameplay.Enemies.EnemyAttackKind.Charge, RuinRail.Gameplay.Enemies.EnemyAttackKind.Moveset };
            var offsets = new[] { new Vector2(5.5f, 2.2f), new Vector2(6f, -2.4f), new Vector2(2.2f, 0.6f), new Vector2(4.5f, -0.4f), new Vector2(3f, 1.8f) };
            for (var i = 0; i < kinds.Length; i++) Normal(content.Enemies.First(e => e.AttackKind == kinds[i]), offsets[i]);
            var eliteDefinition = content.Elites.First(e => e.Biome == run.Expedition.State.Biome);
            var encounter = new DefaultEliteSpawner(content.Stagger).Spawn(eliteDefinition, centre + new Vector2(4f, -2f), room.transform, player.transform);
            room.BindEncounterBounds(encounter.Elite.gameObject);
            run.BindActorPresentation(encounter.Elite, eliteDefinition.Id, true);

            var started = Time.time;
            var body = player.GetComponent<Rigidbody2D>();
            while (Time.time - started < 25f)
            {
                judge.Refresh();
                if (health.CurrentHealth < health.MaxHealth * 0.5f) health.Heal(100000);
                var t = Time.time - started;
                var at = centre + new Vector2(1.5f * Mathf.Sin(t * 1.7f), 2.2f * Mathf.Sin(t * 1.1f));
                if (interior.Contains(at)) body.MovePosition(Vector2.MoveTowards(body.position, at, 5f * Time.deltaTime));
                yield return null;
            }

            var hits = judge.Telegraphed;
            var invisible = hits.Where(h => !h.Visible).ToList();
            Debug.Log($"[FAIRNESS-BUSY] seed {seed} floor ticks set apart {judge.FloorHits}; {run.Expedition.State.Biome}: {hits.Count} hits, {invisible.Count} visibly outside " + string.Join(",", invisible.Select(h => $"{h.Amount}hp gap {h.Gap}px")));
            Assert.IsEmpty(invisible, "damage while visibly outside the red");
            Assert.Greater(hits.Count, 3, "a real fight: the strafing player was hit");
        }

        /// <summary>
        /// Every shipped Boss in a real fight (pinned by the generator search the boss proof uses), the player strafing the
        /// arena through the boss's footprints, phase two forced half way: every damage the boss deals lands on a player whose
        /// drawn body touches a drawn red marker.
        /// </summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator EveryBossFight_StrafingTheEdges_EveryHitIsVisiblyInsideTheRed([ValueSource(typeof(BossPresentationProofTests), nameof(BossPresentationProofTests.BossIds))] string bossId)
        {
            yield return Boot(BossPresentationProofTests.SeedFor(bossId));
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var boss = Object.FindFirstObjectByType<RuinRail.Gameplay.Enemies.Bosses.BossController>();
            Assert.AreEqual(bossId, boss.Definition.Id);
            var room = run.Rooms.Values.First(r => r.State.RoomType == RuinRail.Dungeon.Rooms.RoomType.Boss);
            var player = run.Rig.Player;
            var health = player.GetComponent<HealthComponent>();
            var telegraphs = 0;
            boss.AttackTelegraphStarted += (_, _) => telegraphs++;
            var judge = new HitJudge(player);
            Put(player, (Vector2)room.InteriorWorldBounds.center + Vector2.down * 3f);
            var intro = BossIntroSequence.Current;
            var until = Time.time + 6f;
            while (intro != null && intro.IsPlaying && Time.time < until) yield return null;
            var started = Time.time;
            var phaseForced = false;
            while (Time.time - started < 24f)
            {
                if (health.CurrentHealth < health.MaxHealth * 0.5f) health.Heal(100000);
                judge.Refresh();
                if (!phaseForced && Time.time - started > 12f)
                {
                    phaseForced = true;
                    var bh = boss.Health;
                    var threshold = Mathf.FloorToInt(bh.MaxHealth * boss.Definition.PhaseTwoHealthFraction) - 5;
                    if (bh.CurrentHealth > threshold) bh.TryApplyDamage(new DamageRequest(bh.CurrentHealth - threshold));
                }

                // Strafe: circle the boss at a radius that sweeps through its footprints' edges.
                var t = Time.time - started;
                var radius = 2.4f + 1.6f * Mathf.Sin(t * 0.7f); // 0.8 .. 4.0 tiles: in and out of every footprint
                var at = (Vector2)boss.transform.position + new Vector2(Mathf.Cos(t * 1.3f), Mathf.Sin(t * 1.3f)) * radius;
                var body = player.GetComponent<Rigidbody2D>();
                if (room.InteriorWorldBounds.Contains(at)) body.MovePosition(Vector2.MoveTowards(body.position, at, 6f * Time.deltaTime));
                yield return null;
            }

            var hits = judge.Telegraphed;
            var invisible = hits.Where(h => !h.Visible).ToList();
            Debug.Log($"[FAIRNESS-BOSS] {bossId}: {telegraphs} telegraphs, {judge.FloorHits} damaging-floor ticks set apart; {hits.Count} hits, {invisible.Count} visibly outside " + string.Join(",", invisible.Select(h => $"{h.Amount}hp gap {h.Gap}px")) + $", phase {boss.Phase}");
            Assert.IsEmpty(invisible, $"{bossId}: damage while visibly outside the red: " + string.Join(" | ", judge.Details));
            Assert.Greater(hits.Count, 0, $"{bossId}: the strafe was hit at least once (the fight was real)");
        }
    }

    internal static class ProjectilePoolTestExtensions
    {
        public static int SpawnedProjectilesActive(this ProjectilePool pool) =>
            pool.GetComponentsInChildren<Projectile>(false).Count(p => p.gameObject.activeInHierarchy);
    }
}
