using RuinRail.Core.Input;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Presentation;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RuinRail.App
{
    /// <summary>
    /// The boss room introduction: letterbox bars close in, the camera travels from the survivor to the boss, the
    /// boss's name and biome are shown, then the camera returns and the bars open as control comes back. It runs for
    /// exactly the <see cref="BossEngagement"/> hold, during which the boss has no target (so it cannot attack) and
    /// gameplay input is held; Confirm (Enter/Space/E, left click, A/Start) skips it after a short grace period.
    ///
    /// Presentation only: it reads the boss and moves the camera focus; the fight starts when the engagement's hold
    /// ends, whichever of the two finishes first.
    /// </summary>
    public sealed class BossIntroSequence : MonoBehaviour
    {
        private const float TravelIn = 0.55f;
        private const float TravelOut = 0.45f;
        private const float BarsIn = 0.3f;
        private const float SkipGrace = 0.25f;
        private const int BarHeight = 34;

        private CameraRig _camera;
        private Transform _boss;
        private Transform _player;
        private BossEngagement _engagement;
        private float _duration;
        private float _elapsed;
        private bool _finished;
        private bool _holding;
        private Canvas _canvas;
        private RectTransform _top;
        private RectTransform _bottom;
        private CanvasGroup _card;
        private readonly System.Collections.Generic.List<Canvas> _hidden = new();

        public bool IsPlaying => !_finished;
        public bool WasSkipped { get; private set; }
        public float Elapsed => _elapsed;
        public string Title { get; private set; } = string.Empty;
        public string Subtitle { get; private set; } = string.Empty;

        /// <summary>The intro most recently started in this process (tests, smoke).</summary>
        public static BossIntroSequence Current { get; private set; }

        public static BossIntroSequence Play(CameraRig camera, BossController boss, Transform player, BossEngagement engagement, string subtitle)
        {
            var go = new GameObject("BossIntroSequence");
            var intro = go.AddComponent<BossIntroSequence>();
            intro._camera = camera;
            intro._boss = boss != null ? boss.transform : null;
            intro._player = player;
            intro._engagement = engagement;
            intro._duration = engagement != null && engagement.IntroHoldSeconds > 0f ? engagement.IntroHoldSeconds : BossEngagement.DefaultIntroHoldSeconds;
            intro.Title = boss != null && boss.Definition != null ? boss.Definition.DisplayName.ToUpperInvariant() : "BOSS";
            intro.Subtitle = subtitle ?? string.Empty;
            intro.Build();
            intro.Begin();
            Debug.Log($"[BOSS-INTRO] {intro.Title} started ({intro._duration:0.00}s, input holds {GameplayInputGate.Holds}).");
            Current = intro;
            return intro;
        }

        private void Build()
        {
            _canvas = UiKit.Canvas("BossIntroCanvas", 50);
            _canvas.transform.SetParent(transform, false);
            var root = UiKit.ReferenceRoot(_canvas.transform);

            _top = UiKit.Plate(root, new UiRect(0, -BarHeight, ScreenLayout.Width, BarHeight), UiTheme.NearBlack, "LetterboxTop").rectTransform;
            _bottom = UiKit.Plate(root, new UiRect(0, ScreenLayout.Height, ScreenLayout.Width, BarHeight), UiTheme.NearBlack, "LetterboxBottom").rectTransform;

            // The name card sits on the lower bar's edge, clear of the boss in the middle of the frame.
            var cardGo = new GameObject("BossCard", typeof(RectTransform));
            cardGo.transform.SetParent(root, false);
            var cardRect = (RectTransform)cardGo.transform;
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0f, 1f);
            cardRect.pivot = new Vector2(0f, 1f);
            cardRect.anchoredPosition = Vector2.zero;
            cardRect.sizeDelta = new Vector2(ScreenLayout.Width, ScreenLayout.Height);
            _card = cardGo.AddComponent<CanvasGroup>();
            _card.alpha = 0f;

            const int titleScale = 3;
            var titleWidth = UiText.Width(Title, titleScale);
            var y = ScreenLayout.Height - BarHeight - UiText.Height(1, titleScale) - 30;
            UiKit.Plate(cardGo.transform, new UiRect((ScreenLayout.Width - titleWidth) / 2 - 14, y - 8, titleWidth + 28, UiText.Height(1, titleScale) + 28),
                UiTheme.WithAlpha(UiTheme.NearBlack, 0.72f), "CardPlate");
            UiKit.Plate(cardGo.transform, new UiRect((ScreenLayout.Width - titleWidth) / 2, y + UiText.Height(1, titleScale) + 4, titleWidth, 1), UiTheme.Danger, "CardRule");
            var title = UiKit.Label(cardGo.transform, Title, new UiRect((ScreenLayout.Width - titleWidth) / 2, y, titleWidth, UiText.Height(1, titleScale)),
                titleScale, TextAnchor.UpperCenter, UiTheme.Ink);
            title.alignment = TextAnchor.UpperCenter;
            var subtitleWidth = UiText.Width(Subtitle);
            var subtitle = UiKit.Label(cardGo.transform, Subtitle,
                new UiRect((ScreenLayout.Width - subtitleWidth) / 2, y + UiText.Height(1, titleScale) + 8, subtitleWidth, UiText.Height()), 1, TextAnchor.UpperCenter, UiTheme.Amber);
            subtitle.alignment = TextAnchor.UpperCenter;
        }

        private void Begin()
        {
            GameplayInputGate.Hold();
            _holding = true;
            if (_camera != null) _camera.FocusOverride = Focus;

            // The cinematic owns the screen: the run HUD, prompts and banners step aside and come back exactly as they were.
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas == null || canvas == _canvas || !canvas.isRootCanvas || !canvas.enabled || canvas.renderMode == RenderMode.WorldSpace) continue;
                canvas.enabled = false;
                _hidden.Add(canvas);
            }
        }

        private void RestoreHud()
        {
            foreach (var canvas in _hidden) if (canvas != null) canvas.enabled = true;
            _hidden.Clear();
        }

        /// <summary>Where the camera looks: out to the boss, a beat on it, back to the survivor.</summary>
        private Vector2 Focus()
        {
            var player = _player != null ? (Vector2)_player.position : _camera != null ? _camera.Position : Vector2.zero;
            var boss = _boss != null ? (Vector2)_boss.position : player;
            var t = _elapsed < TravelIn ? Smooth(_elapsed / TravelIn)
                : _elapsed > _duration - TravelOut ? Smooth((_duration - _elapsed) / TravelOut)
                : 1f;
            return Vector2.Lerp(player, boss, t);
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private void Update()
        {
            if (_finished) return;
            _elapsed += Time.deltaTime;

            // Bars in at the start, out at the end; the card is up between the travels.
            var bars = _elapsed < BarsIn ? Smooth(_elapsed / BarsIn) : _elapsed > _duration - BarsIn ? Smooth((_duration - _elapsed) / BarsIn) : 1f;
            if (_top != null) _top.anchoredPosition = new Vector2(0f, BarHeight * (1f - bars));
            if (_bottom != null) _bottom.anchoredPosition = new Vector2(0f, -(ScreenLayout.Height - BarHeight * bars));
            if (_card != null)
                _card.alpha = _elapsed < TravelIn ? 0f : _elapsed < TravelIn + 0.2f ? (_elapsed - TravelIn) / 0.2f : _elapsed > _duration - TravelOut ? Mathf.Clamp01((_duration - _elapsed) / 0.2f) : 1f;

            if (_elapsed >= SkipGrace && SkipPressed())
            {
                WasSkipped = true;
                Finish();
                return;
            }

            if (_elapsed >= _duration || (_boss == null && _elapsed >= TravelIn)) Finish();
        }

        private static bool SkipPressed()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            return (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame))
                   || (mouse != null && mouse.leftButton.wasPressedThisFrame)
                   || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
        }

        /// <summary>Ends the intro now: control and the camera return, the boss acquires its target.</summary>
        public void Finish()
        {
            if (_finished) return;
            _finished = true;
            if (_camera != null) _camera.FocusOverride = null;
            if (_holding)
            {
                GameplayInputGate.Release();
                _holding = false;
            }

            RestoreHud();
            _engagement?.EndIntro();
            Debug.Log($"[BOSS-INTRO] {Title} ended after {_elapsed:0.00}s{(WasSkipped ? " (skipped)" : string.Empty)}, input holds {GameplayInputGate.Holds}.");
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_holding)
            {
                GameplayInputGate.Release();
                _holding = false;
            }

            if (_camera != null && !_finished) _camera.FocusOverride = null;
            RestoreHud();
            if (Current == this) Current = null;
        }
    }
}
