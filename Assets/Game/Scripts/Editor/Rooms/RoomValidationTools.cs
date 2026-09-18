using System.Collections.Generic;
using System.Linq;
using RuinRail.Dungeon.Rooms;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Rooms
{
    /// <summary>
    /// Editor entry points for room validation: validates every RoomDefinition asset in the project (or the selected one)
    /// without opening scenes, so the same call serves menu use, EditMode tests and production audits.
    /// </summary>
    public static class RoomValidationTools
    {
        public static List<RoomDefinition> LoadAllRoomDefinitions()
        {
            return AssetDatabase.FindAssets("t:RoomDefinition")
                .Select(guid => AssetDatabase.LoadAssetAtPath<RoomDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(d => d != null)
                .OrderBy(d => d.Id)
                .ToList();
        }

        public static List<RoomValidationReport> ValidateProject()
        {
            return RoomValidator.ValidateAll(LoadAllRoomDefinitions());
        }

        [MenuItem("RuinRail/Rooms/Validate All Rooms")]
        public static void ValidateAllRoomsMenu()
        {
            var reports = ValidateProject();
            var failed = reports.Where(r => !r.IsGeneratorReady).ToList();
            foreach (var report in failed)
            {
                Debug.LogError($"Room '{report.RoomId}' is NOT generator-ready:\n  " + string.Join("\n  ", report.Problems));
            }

            Debug.Log($"Room validation: {reports.Count - failed.Count}/{reports.Count} rooms generator-ready.");
        }

        [MenuItem("RuinRail/Rooms/Validate Selected Room")]
        public static void ValidateSelectedRoomMenu()
        {
            var definition = Selection.activeObject as RoomDefinition;
            var root = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<RoomRoot>() : null;
            var report = definition != null ? RoomValidator.Validate(definition) : root != null ? RoomValidator.Validate(root) : null;
            if (report == null)
            {
                Debug.LogWarning("Select a RoomDefinition asset or a RoomRoot prefab/object to validate.");
                return;
            }

            if (report.IsGeneratorReady)
            {
                Debug.Log($"Room '{report.RoomId}' is generator-ready.");
            }
            else
            {
                Debug.LogError($"Room '{report.RoomId}' is NOT generator-ready:\n  " + string.Join("\n  ", report.Problems));
            }
        }
    }
}
