using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuinRail.Core
{
    /// <summary>Bootstrap scene entry: raises the app-composition event (the App assembly creates GameApp) and opens the Main Menu.</summary>
    public sealed class AppRoot : MonoBehaviour
    {
        /// <summary>Raised once at boot before the menu loads; the App layer subscribes through RuntimeInitializeOnLoadMethod.</summary>
        public static event System.Action Booting;

        private void Start()
        {
            Booting?.Invoke();
            SceneManager.LoadScene(SceneNames.MainMenu);
        }
    }
}
