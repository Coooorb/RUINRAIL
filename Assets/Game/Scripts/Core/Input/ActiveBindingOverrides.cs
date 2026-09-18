using System;
using UnityEngine.InputSystem;

namespace RuinRail.Core.Input
{
    /// <summary>
    /// The process-wide binding-override document (InputActionAsset.SaveBindingOverridesAsJson format) that every
    /// <see cref="PlayerInputReader"/> applies to its own action instance. Settings persistence owns the value on
    /// disk (technical/113: settings live outside the gameplay save); this is only the in-memory publication point so
    /// a rebind made in Settings reaches readers created later (restart) and readers already alive.
    /// </summary>
    public static class ActiveBindingOverrides
    {
        public static string Json { get; private set; } = string.Empty;

        public static event Action Changed;

        public static void Set(string json)
        {
            json ??= string.Empty;
            if (json == Json) return;
            Json = json;
            Changed?.Invoke();
        }

        public static void Clear() => Set(string.Empty);

        /// <summary>Replaces every override on <paramref name="asset"/> with the published document (defaults when empty).</summary>
        public static void ApplyTo(InputActionAsset asset)
        {
            if (asset == null) return;
            asset.RemoveAllBindingOverrides();
            if (!string.IsNullOrWhiteSpace(Json)) asset.LoadBindingOverridesFromJson(Json);
        }
    }
}
