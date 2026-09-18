using UnityEngine;

namespace RuinRail.App
{
    /// <summary>Hooks the App composition root into the Bootstrap scene's AppRoot without the Core assembly referencing App.</summary>
    public static class AppBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            RuinRail.Core.AppRoot.Booting -= OnBooting;
            RuinRail.Core.AppRoot.Booting += OnBooting;
        }

        private static void OnBooting()
        {
            if (Application.isPlaying) GameApp.Ensure();
        }
    }
}
