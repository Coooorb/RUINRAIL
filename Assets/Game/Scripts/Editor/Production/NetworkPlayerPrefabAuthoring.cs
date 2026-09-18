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
            var source = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options
            {
                Name = "PlayerNetworkEntity",
                IsLocal = false,
                Position = Vector2.zero
            });

            try
            {
                var networkObject = source.AddComponent<NetworkObject>();
                networkObject.AlwaysReplicateAsRoot = true;
                source.AddComponent<NetworkPlayerObject>();
                source.AddComponent<NetworkHealth>();
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
