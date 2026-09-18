using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Player;
using RuinRail.Presentation.Animation;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// Builds a <see cref="CharacterAnimationSet"/> per character family from its sliced sheet.
    ///
    /// The runtime resolves clips by (ActorId, clip key, facing), so this is the seam that turns 22 sheets into the
    /// 1,056 clip roles the manifest tracks — without the animation architecture changing at all. Sheet layout is
    /// deterministic: one row per facing in BodyFacing8 order, states left to right in VisualState order, frames in
    /// sequence, so a sprite's grid position uniquely identifies its clip.
    ///
    /// The player and the enemy families use different clip-key vocabularies (AnimationRules), so the six generated
    /// visual states are mapped onto whichever keys the runtime actually asks for rather than inventing new states.
    /// </summary>
    public static class AnimationSetBuilder
    {
        public const string Folder = ArtIntegration.ArtRoot + "/Characters";

        /// <summary>
        /// Maps a runtime clip key to the generated visual state.
        ///
        /// Player keys are Idle, Walk, Dash, Downed, GetUp, Death; enemy keys are Idle, Move, Telegraph, Attack,
        /// Recover, Death. Both vocabularies are covered without adding a runtime state (spec 5.1).
        /// </summary>
        public static VisualState StateForKey(string key) => key switch
        {
            "Idle" => VisualState.Idle,
            "Walk" or "Move" => VisualState.Move,
            "Dash" => VisualState.Special,
            "Telegraph" => VisualState.Special,
            "Attack" => VisualState.Attack,
            "Downed" or "Recover" => VisualState.Hit,
            "GetUp" => VisualState.Hit,
            "Death" => VisualState.Death,
            _ => VisualState.Idle
        };

        /// <summary>Column index where a state's frames begin in a sheet row.</summary>
        public static int ColumnOffset(VisualState state)
        {
            var offset = 0;
            foreach (VisualState s in Enum.GetValues(typeof(VisualState)))
            {
                if (s == state) return offset;
                offset += CharacterSpriteFactory.FrameCount(s);
            }
            return 0;
        }

        public static int TotalColumns()
        {
            var total = 0;
            foreach (VisualState s in Enum.GetValues(typeof(VisualState))) total += CharacterSpriteFactory.FrameCount(s);
            return total;
        }

        /// <summary>
        /// Converts the art generator's facing order to the runtime's.
        ///
        /// The two enums deliberately do not share an order — Facing8 starts at S for drawing convenience, BodyFacing8
        /// starts at E for the aiming maths — so this must be an explicit map. Casting by index would silently rotate
        /// every character's animation by three compass points, which no test would catch.
        /// </summary>
        public static BodyFacing8 ToRuntimeFacing(Facing8 facing) => facing switch
        {
            Facing8.S => BodyFacing8.S,
            Facing8.SE => BodyFacing8.SE,
            Facing8.E => BodyFacing8.E,
            Facing8.NE => BodyFacing8.NE,
            Facing8.N => BodyFacing8.N,
            Facing8.NW => BodyFacing8.NW,
            Facing8.W => BodyFacing8.W,
            _ => BodyFacing8.SW
        };

        [MenuItem("RuinRail/Art/Build Character Animation Sets")]
        public static void BuildAllMenu() => Debug.Log($"Built {BuildAll()} character animation sets.");

        public static int BuildAll()
        {
            var built = 0;
            var facings = (Facing8[])Enum.GetValues(typeof(Facing8));
            var columns = TotalColumns();

            foreach (var profile in CharacterCatalog.All())
            {
                var sheetPath = $"{Folder}/{profile.Id}/{profile.Id}_sheet.png";
                var sprites = AssetDatabase.LoadAllAssetsAtPath(sheetPath).OfType<Sprite>().ToList();
                if (sprites.Count == 0)
                {
                    Debug.LogError($"{profile.Id}: no sliced sprites at {sheetPath}.");
                    continue;
                }

                // Sprites are named "<sheet>_<row>_<col>"; index them so lookup is exact rather than order-dependent.
                var grid = new Dictionary<(int row, int col), Sprite>();
                foreach (var sprite in sprites)
                {
                    var parts = sprite.name.Split('_');
                    if (parts.Length < 2) continue;
                    if (!int.TryParse(parts[^2], out var row) || !int.TryParse(parts[^1], out var col)) continue;
                    grid[(row, col)] = sprite;
                }

                var clips = new List<SpriteAnimationClip>();
                var keys = profile.Id == AnimationAssetAuditKeys.PlayerActorId
                    ? AnimationRules.PlayerClipKeys
                    : AnimationRules.EnemyClipKeys;

                foreach (var key in keys)
                {
                    var state = StateForKey(key);
                    var frameCount = CharacterSpriteFactory.FrameCount(state);
                    var startCol = ColumnOffset(state);

                    for (var f = 0; f < facings.Length; f++)
                    {
                        var frames = new List<Sprite>();
                        for (var i = 0; i < frameCount; i++)
                            if (grid.TryGetValue((f, startCol + i), out var sprite)) frames.Add(sprite);

                        if (frames.Count == 0)
                        {
                            Debug.LogError($"{profile.Id}: no frames for {key}/{facings[f]}.");
                            continue;
                        }

                        clips.Add(new SpriteAnimationClip
                        {
                            Key = key,
                            Facing = ToRuntimeFacing(facings[f]),
                            Frames = frames.ToArray(),
                            // Idle reads calmer; everything else runs at the art target (spec 5.3).
                            FramesPerSecond = key == "Idle" ? AnimationRules.MinFps : AnimationRules.DefaultFps,
                            Loop = key is "Idle" or "Walk" or "Move"
                        });
                    }
                }

                var setPath = $"{Folder}/{profile.Id}/{profile.Id}_animation_set.asset";
                var set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(setPath);
                if (set == null)
                {
                    set = ScriptableObject.CreateInstance<CharacterAnimationSet>();
                    AssetDatabase.CreateAsset(set, setPath);
                }

                set.Configure(profile.Id, clips);
                EditorUtility.SetDirty(set);
                built++;
            }

            AssetDatabase.SaveAssets();
            return built;
        }
    }

    /// <summary>The player's actor id, kept beside the builder so it does not depend on the production audit tool.</summary>
    internal static class AnimationAssetAuditKeys
    {
        public const string PlayerActorId = "player";
    }
}
