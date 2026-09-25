using System.IO;
using System.Linq;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// TASK 180 — authors the release network player prefab and registers it with NGO.
    ///
    /// The prefab is built from <see cref="PlayerEntityBuilder"/>, the same composer the solo game already uses, so
    /// the networked player is the proven player entity plus NGO components — not a second, parallel player built by
    /// hand that could drift from it. The three NetworkBehaviours added on top are the ones the runtime already
    /// expects to find: NetworkPlayerObject (identity and input isolation), NetworkHealth and NetworkPlayerCombat.
    ///
    /// Authoring is idempotent: re-running rebuilds the prefab at the same path and leaves the prefab list with
    /// exactly one entry for it.
    /// </summary>
    public static class NetworkPlayerPrefabAuthoring
    {
        public const string PrefabFolder = "Assets/Game/Prefabs/Network";
        public const string PrefabPath = PrefabFolder + "/PlayerNetworkEntity.prefab";
        public const string PrefabListPath = "Assets/DefaultNetworkPrefabs.asset";
        /// <summary>The session's co-op expedition channel (depth payload, world records, enemy motion).</summary>
        public const string LinkPrefabPath = PrefabFolder + "/CoopRunLink.prefab";

        [MenuItem("RuinRail/Production/Author Network Player Prefab")]
        public static void AuthorMenu()
        {
            var prefab = Author();
            Debug.Log(prefab != null
                ? $"Network player prefab authored at {PrefabPath} and registered in {PrefabListPath}."
                : "Network player prefab authoring failed.");
        }

        public static GameObject Author()
        {
            Directory.CreateDirectory(PrefabFolder);

            // Build the same player the solo game builds, without a local input reader: on a networked instance the
            // owner attaches input in NetworkPlayerObject.OnNetworkSpawn and replicas get the null reader.
            // The approved balance and caps are serialized into the prefab: without them a networked player has no move
            // speed, no max HP and no dash/revive tuning at all, because a spawned prefab has no builder to hand them to.
            var balance = AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            var caps = AssetDatabase.LoadAssetAtPath<RuinRail.Gameplay.Items.GlobalStatCapsConfig>("Assets/Game/ScriptableObjects/Balance/GlobalStatCapsConfig.asset");
            var source = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options
            {
                Name = "PlayerNetworkEntity",
                IsLocal = false,
                BalanceConfig = balance,
                Caps = caps,
                Position = Vector2.zero
            });

            try
            {
                var networkObject = source.AddComponent<NetworkObject>();
                networkObject.AlwaysReplicateAsRoot = true;
                // 85: a member whose connection drops keeps its character (held, at risk) for the reconnect grace; NGO
                // would otherwise destroy a player object the moment its owner disconnects, leaving nothing to reclaim.
                networkObject.DontDestroyWithOwner = true;
                source.AddComponent<NetworkPlayerObject>();
                // 82 + 11/14: the owner sends intents, the host applies and publishes state, replicas interpolate.
                // Without this component on the spawned object a networked player never moves on any other peer.
                source.AddComponent<NetworkPlayerMotion>();
                source.AddComponent<NetworkHealth>();
                // 84: Downed/Dead and the bleedout clock reach every peer, so a teammate's state is visible at all.
                source.AddComponent<NetworkPlayerLife>();
                source.AddComponent<NetworkPlayerCombat>();

                var prefab = PrefabUtility.SaveAsPrefabAsset(source, PrefabPath);
                AssetDatabase.SaveAssets();
                Register(prefab);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        /// <summary>
        /// The co-op expedition channel prefab: one NetworkObject carrying <see cref="NetworkDungeonSync"/> (the host's
        /// depth payload) and <see cref="CoopRunLink"/> (world records, requests, enemy motion). Idempotent.
        /// </summary>
        public static GameObject AuthorLink()
        {
            Directory.CreateDirectory(PrefabFolder);
            var source = new GameObject("CoopRunLink");
            try
            {
                var networkObject = source.AddComponent<NetworkObject>();
                networkObject.AlwaysReplicateAsRoot = true;
                networkObject.DontDestroyWithOwner = true;
                source.AddComponent<NetworkDungeonSync>();
                source.AddComponent<CoopRunLink>();
                var prefab = PrefabUtility.SaveAsPrefabAsset(source, LinkPrefabPath);
                AssetDatabase.SaveAssets();
                Register(prefab);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [MenuItem("RuinRail/Production/Author Co-op Network Prefabs")]
        public static void AuthorAllMenu()
        {
            var player = Author();
            var link = AuthorLink();
            var catalog = GameContentCatalogBuilder.Build();
            Debug.Log($"Co-op prefabs authored: player={(player != null)} link={(link != null)} catalogLink={(catalog != null && catalog.CoopRunLink != null)}.");
        }

        /// <summary>Batch entry: authors both network prefabs, registers them and rebuilds the content catalog.</summary>
        public static void AuthorAllBatch()
        {
            var ok = false;
            try
            {
                var player = Author();
                var link = AuthorLink();
                var catalog = GameContentCatalogBuilder.Build();
                ok = player != null && link != null && catalog != null && catalog.CoopRunLink != null && catalog.NetworkPlayerEntity != null && IsRegistered() && IsLinkRegistered();
                Debug.Log($"COOP-PREFABS authored ok={ok}");
            }
            catch (System.Exception e)
            {
                Debug.LogError("COOP-PREFABS authoring failed: " + e);
            }

            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static GameObject LoadLink() => AssetDatabase.LoadAssetAtPath<GameObject>(LinkPrefabPath);

        public static bool IsLinkRegistered()
        {
            var prefab = LoadLink();
            if (prefab == null) return false;
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            return list != null && list.PrefabList.Any(p => p != null && p.Prefab == prefab);
        }

        /// <summary>Adds the prefab to the release NetworkManager prefab list, exactly once.</summary>
        public static void Register(GameObject prefab)
        {
            if (prefab == null) return;

            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null)
            {
                Debug.LogError($"{PrefabListPath} is missing; NGO cannot spawn the player without a prefab list.");
                return;
            }

            if (list.PrefabList.Any(p => p != null && p.Prefab == prefab)) return;

            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
        }

        public static GameObject Load() => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        public static bool IsRegistered()
        {
            var prefab = Load();
            if (prefab == null) return false;
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            return list != null && list.PrefabList.Any(p => p != null && p.Prefab == prefab);
        }
    }
}
