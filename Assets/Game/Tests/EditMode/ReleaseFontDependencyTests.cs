using System.IO;
using System.Linq;
using NUnit.Framework;

namespace RuinRail.Tests
{
    /// <summary>
    /// FINAL_ART_PRODUCTION_SPEC 29.12: the builtin LegacyRuntime.ttf is not a release dependency. The only place the
    /// runtime may name it is the shared <c>UiFont</c> fallback (which the art validator fails the build on if it is
    /// what ships); every screen, HUD and overlay draws through that one seam.
    /// </summary>
    public sealed class ReleaseFontDependencyTests
    {
        [Test]
        public void NoReleaseScript_LoadsTheBuiltinFontDirectly_ExceptTheSharedUiFontFallback()
        {
            var offenders = Directory.GetFiles("Assets/Game/Scripts", "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Replace('\\', '/').Contains("/Editor/"))
                .Where(path => Path.GetFileName(path) != "UiFont.cs")
                .Where(path => File.ReadAllText(path).Contains("GetBuiltinResource<Font>"))
                .Select(path => path.Replace('\\', '/'))
                .ToList();
            CollectionAssert.IsEmpty(offenders, "release UI scripts must resolve the face through UiFont / UiKit.Font()");

            var seam = File.ReadAllText("Assets/Game/Scripts/UI/Theme/UiFont.cs");
            StringAssert.Contains("Resources.Load<Font>", seam, "the seam loads the project pixel font first");
        }
    }
}
