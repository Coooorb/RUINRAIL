using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace RuinRail.Tests
{
    public class InputActionsAssetTests
    {
        private const string AssetPath = "Assets/Game/Settings/Input/RuinRailInputActions.inputactions";

        private static InputActionAsset LoadAsset()
        {
            return AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
        }

        private static bool HasBinding(InputAction action, string path)
        {
            return action.bindings.Any(binding => binding.path == path);
        }

        [Test]
        public void Asset_ExistsAndContainsPlayerActionMap()
        {
            var asset = LoadAsset();
            Assert.IsNotNull(asset, $"Expected an InputActionAsset at {AssetPath}.");

            var map = asset.FindActionMap("Player");
            Assert.IsNotNull(map, "Expected an action map named 'Player'.");
        }

        [TestCase("Move", "Vector2")]
        [TestCase("Aim", "Vector2")]
        [TestCase("Fire", "Button")]
        [TestCase("Special", "Button")]
        [TestCase("Dash", "Button")]
        [TestCase("Reload", "Button")]
        [TestCase("Interact", "Button")]
        [TestCase("Weapon1", "Button")]
        [TestCase("Weapon2", "Button")]
        [TestCase("WeaponSwap", "Button")]
        [TestCase("Consumable", "Button")]
        [TestCase("Inventory", "Button")]
        public void PlayerMap_ContainsAction(string actionName, string expectedControlType)
        {
            var map = LoadAsset().FindActionMap("Player");
            var action = map.FindAction(actionName);

            Assert.IsNotNull(action, $"Expected action '{actionName}' on the Player map.");
            Assert.AreEqual(expectedControlType, action.expectedControlType,
                $"Action '{actionName}' should expect control type '{expectedControlType}'.");
        }

        [TestCase("Move", "<Keyboard>/w")]
        [TestCase("Move", "<Gamepad>/leftStick")]
        [TestCase("Aim", "<Mouse>/position")]
        [TestCase("Aim", "<Gamepad>/rightStick")]
        [TestCase("Fire", "<Mouse>/leftButton")]
        [TestCase("Fire", "<Gamepad>/rightTrigger")]
        [TestCase("Special", "<Mouse>/rightButton")]
        [TestCase("Special", "<Gamepad>/leftTrigger")]
        [TestCase("Dash", "<Keyboard>/space")]
        [TestCase("Dash", "<Gamepad>/buttonEast")]
        [TestCase("Reload", "<Keyboard>/r")]
        [TestCase("Reload", "<Gamepad>/buttonWest")]
        [TestCase("Interact", "<Keyboard>/e")]
        [TestCase("Interact", "<Gamepad>/buttonSouth")]
        [TestCase("Weapon1", "<Keyboard>/1")]
        [TestCase("Weapon1", "<Gamepad>/dpad/left")]
        [TestCase("Weapon2", "<Keyboard>/2")]
        [TestCase("Weapon2", "<Gamepad>/dpad/right")]
        [TestCase("WeaponSwap", "<Mouse>/scroll/y")]
        [TestCase("WeaponSwap", "<Gamepad>/buttonNorth")]
        [TestCase("Consumable", "<Keyboard>/g")]
        [TestCase("Consumable", "<Gamepad>/rightShoulder")]
        [TestCase("Inventory", "<Keyboard>/tab")]
        [TestCase("Inventory", "<Gamepad>/select")]
        public void Action_HasExpectedBinding(string actionName, string expectedBindingPath)
        {
            var map = LoadAsset().FindActionMap("Player");
            var action = map.FindAction(actionName);

            Assert.IsNotNull(action, $"Expected action '{actionName}' on the Player map.");
            Assert.IsTrue(HasBinding(action, expectedBindingPath),
                $"Expected action '{actionName}' to have a binding to '{expectedBindingPath}'.");
        }

        [Test]
        public void Move_UsesComposite2DVectorForKeyboard()
        {
            var map = LoadAsset().FindActionMap("Player");
            var action = map.FindAction("Move");

            var composite = action.bindings.FirstOrDefault(binding => binding.isComposite);
            Assert.IsTrue(composite.isComposite, "Expected Move to have a composite binding for WASD.");
            Assert.AreEqual("2DVector", composite.path);
        }

        [Test]
        public void Asset_DefinesKeyboardMouseAndGamepadControlSchemes()
        {
            var asset = LoadAsset();
            var schemeNames = asset.controlSchemes.Select(scheme => scheme.name).ToArray();

            CollectionAssert.Contains(schemeNames, "Keyboard&Mouse");
            CollectionAssert.Contains(schemeNames, "Gamepad");
        }
    }
}
