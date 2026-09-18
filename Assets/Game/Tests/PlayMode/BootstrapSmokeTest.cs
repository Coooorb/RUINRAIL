using System.Collections;
using NUnit.Framework;
using RuinRail.Core;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    public class BootstrapSmokeTest
    {
        [UnityTest]
        public IEnumerator Bootstrap_LoadsMainMenu()
        {
            yield return SceneManager.LoadSceneAsync(SceneNames.Bootstrap, LoadSceneMode.Single);
            yield return null;
            yield return null;

            Assert.AreEqual(SceneNames.MainMenu, SceneManager.GetActiveScene().name);
        }
    }
}
