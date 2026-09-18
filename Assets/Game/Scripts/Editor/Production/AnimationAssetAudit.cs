using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Items;
using RuinRail.Presentation.Animation;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// TASK 138 final-gate listing: which animation sets exist and which clips are still external work. The runtime
    /// falls back explicitly (SpriteAnimator placeholder path); this report is what marks them BLOCKED_EXTERNAL_ASSET.
    /// </summary>
    public static class AnimationAssetAudit
    {
        public const string AnimationFolder = "Assets/Game/Animation";
        public const string ReportPath = "TestResults/animation_assets.md";
        public const string PlayerActorId = "player";

        public sealed class ActorLine
        {
            public string ActorId;
            public string Kind;
            public bool HasSet;
            public int Required;
            public int Missing;
            public bool Complete => HasSet && Missing == 0;
        }

        public sealed class Report
        {
            public readonly List<ActorLine> Actors = new();
            public int WeaponSpritesRequired;
            public int WeaponSpritesPresent;
            public IEnumerable<ActorLine> Blocked => Actors.Where(a => !a.Complete);
            public bool AllPresent => Actors.All(a => a.Complete) && WeaponSpritesPresent >= WeaponSpritesRequired;

            public string ToMarkdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine("# RUINRAIL V1 animation asset audit (art/103)");
                sb.AppendLine();
                sb.AppendLine($"Status: **{(AllPresent ? "COMPLETE" : "BLOCKED_EXTERNAL_ASSET")}** — {Actors.Count(a => a.Complete)}/{Actors.Count} actor animation sets complete; weapon sprites {WeaponSpritesPresent}/{WeaponSpritesRequired}.");
                sb.AppendLine();
                sb.AppendLine("| Actor | Kind | Set | Required clips | Missing | Status |");
                sb.AppendLine("|---|---|---|---:|---:|---|");
                foreach (var a in Actors) sb.AppendLine($"| {a.ActorId} | {a.Kind} | {(a.HasSet ? "yes" : "none")} | {a.Required} | {a.Missing} | {(a.Complete ? "OK" : "BLOCKED_EXTERNAL_ASSET")} |");
                sb.AppendLine();
                sb.AppendLine("Runtime behaviour without these assets: SpriteAnimator records the missing clip id and keeps the last frame (placeholder), drivers keep mapping gameplay state; no exceptions.");
                return sb.ToString();
            }
        }

        [MenuItem("RuinRail/Production/Audit Animation Assets")]
        public static void AuditMenu() => Debug.Log(WriteReport().ToMarkdown());

        public static Report WriteReport()
        {
            var report = Audit();
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath) ?? "TestResults");
            File.WriteAllText(ReportPath, report.ToMarkdown());
            return report;
        }

        public static Report Audit()
        {
            var sets = AssetDatabase.FindAssets("t:CharacterAnimationSet").Select(g => AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(AssetDatabase.GUIDToAssetPath(g))).Where(s => s != null).ToList();
            var report = new Report();
            report.Actors.Add(Line(PlayerActorId, "Player", AnimationRules.PlayerClipKeys, sets));
            foreach (var e in LoadAll<EnemyDefinition>()) report.Actors.Add(Line(e.Id, "Enemy", AnimationRules.EnemyClipKeys, sets));
            foreach (var e in LoadAll<EliteDefinition>()) report.Actors.Add(Line(e.Id, "Elite", AnimationRules.EnemyClipKeys, sets));
            foreach (var b in LoadAll<BossDefinition>()) report.Actors.Add(Line(b.Id, "Boss", AnimationRules.EnemyClipKeys, sets));
            var weapons = LoadAll<WeaponDefinition>();
            report.WeaponSpritesRequired = weapons.Count;
            // A weapon's world sprite lives at its convention path, keyed by the weapon's stable id. Counting the
            // files that actually resolve replaces the old hard-coded zero, which predated the art existing.
            report.WeaponSpritesPresent = weapons.Count(w =>
                !string.IsNullOrEmpty(w.Id) &&
                AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Game/Art/Weapons/{w.Id}.png") != null);
            return report;
        }

        private static ActorLine Line(string actorId, string kind, string[] keys, List<CharacterAnimationSet> sets)
        {
            var set = sets.FirstOrDefault(s => string.Equals(s.ActorId, actorId, StringComparison.OrdinalIgnoreCase));
            var required = keys.Length * AnimationRules.AllFacings.Length;
            return new ActorLine { ActorId = actorId, Kind = kind, HasSet = set != null, Required = required, Missing = set != null ? set.Missing(keys).Count : required };
        }

        private static List<T> LoadAll<T>() where T : UnityEngine.Object =>
            AssetDatabase.FindAssets($"t:{typeof(T).Name}").Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g))).Where(a => a != null).OrderBy(a => AssetDatabase.GetAssetPath(a), StringComparer.Ordinal).ToList();
    }
}
