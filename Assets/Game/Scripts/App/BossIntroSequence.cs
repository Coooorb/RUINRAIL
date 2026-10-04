using System;
using System.Collections.Generic;
using RuinRail.Core;
using RuinRail.Core.Input;
using RuinRail.Core.Rendering;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Presentation;
using RuinRail.Presentation.Animation;
using RuinRail.Presentation.Vfx;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RuinRail.App
{
    /// <summary>
    /// The boss room introduction, staged in beats inside the <see cref="BossEngagement"/> hold:
    /// <list type="number">
    /// <item>the letterbox closes with a thin accent edge and the frame darkens at its rim while the camera travels from
    /// the survivor to the boss;</item>
    /// <item>as the camera arrives the boss is <b>revealed</b> — it flares in its own glow, a shockwave runs out across the
    /// floor in its accent, the arena throws up its biome's matter (sparks, embers, spores, frost or grit), the screen
    /// kicks once and the boss's cue sounds;</item>
    /// <item>the name band slides in over the lower third: biome kicker, the name opening from its centre over an accent
    /// shadow between two growing rules, and the boss's approved identity line;</item>
    /// <item>the band fades, the camera returns, the letterbox opens and control comes back.</item>
    /// </list>
    /// Each boss reads as itself through <see cref="BossIntroStyle"/> (accent, glow, identity line, arena reaction).
    ///
    /// The contract is unchanged: it runs for exactly the hold, during which the boss has no target (it cannot attack)
    /// and gameplay input is held; the run HUD steps aside and returns as it was; Confirm (Enter/Space/E, left click,
    /// A/Start) skips it after a short grace period; the fight starts when the hold ends, whichever finishes first.
    /// Presentation only: it reads the boss, moves the camera focus and draws over the arena — it never moves the boss.
    /// </summary>
    public sealed class BossIntroSequence : MonoBehaviour
    {
        private const float TravelIn = 0.55f;
        private const float TravelOut = 0.45f;
        private const float BarsIn = 0.3f;
        private const float SkipGrace = 0.25f;
        private const int BarHeight = 34;

        /// <summary>The reveal beat: the camera is all but on the boss.</summary>
        public const float RevealAt = 0.5f;
        private const float CardIn = 0.58f;
        private const float CardSlide = 0.22f;
        private const float TitleWipe = 0.3f;
        private const float CardOut = 0.22f;
        private const float FlashSeconds = 0.4f;
        private const int ShockwaveRadius = 88;
        private const float ShockwaveSeconds = 0.35f;
        private const int ShockwaveFrames = 6;

        private CameraRig _camera;
        private BossController _bossController;
        private Transform _boss;
        private Transform _player;
        private BossEngagement _engagement;
        private CameraShake _shake;
        private Action<Vector2> _revealCue;
        private BossIntroStyle _style;
        private float _duration;
        private float _elapsed;
        private bool _finished;
        private bool _holding;
        private bool _revealed;
        private Canvas _canvas;
        private RectTransform _top;
        private RectTransform _bottom;
        private Image _vignette;
        private Image _flash;
        private CanvasGroup _card;
        private RectTransform _cardRect;
        private RectTransform _titleMask;
        private int _titleWidth;
        private readonly List<RectTransform> _rules = new();
        private int _ruleLength;
        private CanvasGroup _identity;
        private readonly List<Canvas> _hidden = new();

        // The reveal on the arena floor and on the boss.
        private SpriteRenderer _shockwave;
        private SpriteRenderer _bossBody;
        private SpriteRenderer _bossFlare;
        private float _revealTime;
        private readonly List<Particle> _particles = new();

        private sealed class Particle
        {
            public SpriteRenderer Renderer;
            public Vector2 Position;
            public Vector2 Velocity;
            public float Age;
            public float Life;
            public float Gravity;
            public float Drag;
        }

        public bool IsPlaying => !_finished;
        public bool WasSkipped { get; private set; }
        public float Elapsed => _elapsed;
        public string Title { get; private set; } = string.Empty;
        public string Subtitle { get; private set; } = string.Empty;
        /// <summary>The boss's approved identity line on the name band (empty for an unstyled boss).</summary>
        public string Identity => _style.Identity;
        /// <summary>The accent the band and the reveal burn in (tests / captures).</summary>
        public Color Accent => _style.Accent;
        /// <summary>True once the reveal beat has played (flare, shockwave, arena reaction, kick, cue).</summary>
        public bool Revealed => _revealed;
        /// <summary>Arena-reaction particles alive right now (diagnostics).</summary>
        public int LiveParticles => _particles.Count;

        /// <summary>The intro most recently started in this process (tests, smoke).</summary>
        public static BossIntroSequence Current { get; private set; }

        public static BossIntroSequence Play(CameraRig camera, BossController boss, Transform player, BossEngagement engagement, string subtitle,
            Biome biome = Biome.RuinedMetro, CameraShake shake = null, Action<Vector2> revealCue = null)
        {
            var go = new GameObject("BossIntroSequence");
            var intro = go.AddComponent<BossIntroSequence>();
            intro._camera = camera;
            intro._bossController = boss;
            intro._boss = boss != null ? boss.transform : null;
            intro._player = player;
            intro._engagement = engagement;
            intro._shake = shake;
            intro._revealCue = revealCue;
            intro._style = BossIntroStyle.For(boss != null && boss.Definition != null ? boss.Definition.Id : null, biome);
            intro._duration = engagement != null && engagement.IntroHoldSeconds > 0f ? engagement.IntroHoldSeconds : BossEngagement.DefaultIntroHoldSeconds;
            intro.Title = boss != null && boss.Definition != null ? boss.Definition.DisplayName.ToUpperInvariant() : "BOSS";
            intro.Subtitle = subtitle ?? string.Empty;
            intro.Build();
            intro.Begin();
            // The hold and the introduction end together, whichever clock gets there first.
            if (engagement != null) engagement.IntroEnded += intro.OnHoldEnded;
            Debug.Log($"[BOSS-INTRO] {intro.Title} started ({intro._duration:0.00}s, input holds {GameplayInputGate.Holds}).");
            Current = intro;
            return intro;
        }

        // ---------------------------------------------------------------- the screen layer

        private void Build()
        {
            _canvas = UiKit.Canvas("BossIntroCanvas", 50);
            _canvas.transform.SetParent(transform, false);
            var root = UiKit.ReferenceRoot(_canvas.transform);

            // The frame: a dithered dark rim (the arena's edges recede, the middle stays the picture) and the reveal flash.
            _vignette = UiKit.Plate(root, ScreenLayout.Screen, new Color(1f, 1f, 1f, 0f), "Vignette");
            _vignette.sprite = VignetteSprite();
            // The reveal's pixel art is made now (once per process), so the reveal frame itself does no texture work.
            for (var i = 0; i < ShockwaveFrames; i++) ShockwaveFrame(i);
            MatterSprite(1);
            MatterSprite(2);
            _flash = UiKit.Plate(root, ScreenLayout.Screen, new Color(1f, 1f, 1f, 0f), "RevealFlash");

            // Letterbox bars, each with a 1 px accent edge on its inner side.
            _top = UiKit.Plate(root, new UiRect(0, -BarHeight, ScreenLayout.Width, BarHeight), UiTheme.NearBlack, "LetterboxTop").rectTransform;
            _bottom = UiKit.Plate(root, new UiRect(0, ScreenLayout.Height, ScreenLayout.Width, BarHeight), UiTheme.NearBlack, "LetterboxBottom").rectTransform;
            UiKit.Plate(_top, new UiRect(0, BarHeight - 1, ScreenLayout.Width, 1), UiTheme.WithAlpha(_style.Accent, 0.75f), "LetterboxEdge");
            UiKit.Plate(_bottom, new UiRect(0, 0, ScreenLayout.Width, 1), UiTheme.WithAlpha(_style.Accent, 0.75f), "LetterboxEdge");

            BuildNameBand(root);
        }

        /// <summary>
        /// The name band over the lower third, clear of the boss in the middle of the frame: kicker (biome · BOSS) in the
        /// accent, the name at 3× over a 1 px accent shadow between two rules with diamond caps, and the identity line.
        /// </summary>
        private void BuildNameBand(RectTransform root)
        {
            const int titleScale = 3;
            var titleHeight = UiText.Height(1, titleScale);
            var kicker = Subtitle.Length > 0 ? Subtitle : "BOSS";
            var hasIdentity = _style.Identity.Length > 0;
            const int pad = 7;
            var bandHeight = pad + UiText.LineHeight + 4 + titleHeight + (hasIdentity ? 5 + UiText.LineHeight : 0) + pad;
            var bandY = ScreenLayout.Height - BarHeight - bandHeight;

            var cardGo = new GameObject("BossCard", typeof(RectTransform));
            cardGo.transform.SetParent(root, false);
            _cardRect = (RectTransform)cardGo.transform;
            _cardRect.anchorMin = _cardRect.anchorMax = new Vector2(0f, 1f);
            _cardRect.pivot = new Vector2(0f, 1f);
            _cardRect.anchoredPosition = Vector2.zero;
            _cardRect.sizeDelta = new Vector2(ScreenLayout.Width, ScreenLayout.Height);
            _card = cardGo.AddComponent<CanvasGroup>();
            _card.alpha = 0f;
            var card = cardGo.transform;

            // Opaque enough that a bright floor (acid, ice) never shows through the name; it rests on the letterbox.
            UiKit.Plate(card, new UiRect(0, bandY, ScreenLayout.Width, bandHeight), UiTheme.WithAlpha(UiTheme.NearBlack, 0.94f), "Band");
            UiKit.Plate(card, new UiRect(0, bandY, ScreenLayout.Width, 1), UiTheme.WithAlpha(_style.Accent, 0.55f), "BandEdge");

            var y = bandY + pad;
            var kickerWidth = UiText.Width(kicker);
            var kickerLabel = UiKit.Label(card, kicker, new UiRect((ScreenLayout.Width - kickerWidth) / 2, y, kickerWidth, UiText.Height()), 1, TextAnchor.UpperCenter, _style.Accent);
            kickerLabel.alignment = TextAnchor.UpperCenter;
            y += UiText.LineHeight + 4;

            // The name opens from its centre: a mask that widens over the shadow and the name.
            _titleWidth = UiText.Width(Title, titleScale);
            var maskGo = new GameObject("TitleMask", typeof(RectTransform), typeof(RectMask2D));
            maskGo.transform.SetParent(card, false);
            _titleMask = (RectTransform)maskGo.transform;
            _titleMask.anchorMin = _titleMask.anchorMax = new Vector2(0f, 1f);
            _titleMask.pivot = new Vector2(0.5f, 1f);
            _titleMask.anchoredPosition = new Vector2(ScreenLayout.Width / 2f, -y);
            _titleMask.sizeDelta = new Vector2(0f, titleHeight + 2);
            CenteredLabel(maskGo.transform, Title, titleScale, new Vector2(1f, -1f), UiTheme.Darken(_style.Accent, 0.25f), "TitleShadow");
            CenteredLabel(maskGo.transform, Title, titleScale, Vector2.zero, UiTheme.Ink, "Title");

            // Rules from the name's edges outward, each ending in a 3 px diamond.
            _ruleLength = Mathf.Max(24, Mathf.Min(96, (ScreenLayout.Width - _titleWidth) / 2 - 40));
            var ruleY = y + titleHeight / 2;
            foreach (var side in new[] { -1, 1 })
            {
                var rule = UiKit.Plate(card, new UiRect(0, ruleY, 1, 1), _style.Accent, "TitleRule").rectTransform;
                rule.pivot = new Vector2(side < 0 ? 1f : 0f, 1f);
                rule.anchoredPosition = new Vector2(ScreenLayout.Width / 2f + side * (_titleWidth / 2f + 10f), -ruleY);
                _rules.Add(rule);
                var cap = UiKit.Plate(rule, new UiRect(0, -1, 3, 3), _style.Glow, "RuleCap").rectTransform;
                cap.anchorMin = cap.anchorMax = new Vector2(side < 0 ? 0f : 1f, 0.5f);
                cap.pivot = new Vector2(0.5f, 0.5f);
                cap.anchoredPosition = Vector2.zero;
                cap.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }

            y += titleHeight + 5;
            if (hasIdentity)
            {
                var identityGo = new GameObject("Identity", typeof(RectTransform));
                identityGo.transform.SetParent(card, false);
                var identityRect = (RectTransform)identityGo.transform;
                identityRect.anchorMin = identityRect.anchorMax = new Vector2(0f, 1f);
                identityRect.pivot = new Vector2(0f, 1f);
                identityRect.anchoredPosition = Vector2.zero;
                identityRect.sizeDelta = new Vector2(ScreenLayout.Width, ScreenLayout.Height);
                _identity = identityGo.AddComponent<CanvasGroup>();
                _identity.alpha = 0f;
                var width = UiText.Width(_style.Identity);
                var label = UiKit.Label(identityGo.transform, _style.Identity, new UiRect((ScreenLayout.Width - width) / 2, y, width, UiText.Height()), 1, TextAnchor.UpperCenter, Color.Lerp(UiTheme.InkMuted, UiTheme.Ink, 0.5f));
                label.alignment = TextAnchor.UpperCenter;
            }
        }

        private static void CenteredLabel(Transform parent, string text, int scale, Vector2 offset, Color color, string name)
        {
            var width = UiText.Width(text, scale);
            var label = UiKit.Label(parent, text, new UiRect(0, 0, width, UiText.Height(1, scale)), scale, TextAnchor.UpperCenter, color);
            label.name = name;
            label.alignment = TextAnchor.UpperCenter;
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = offset;
        }

        // ---------------------------------------------------------------- flow

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

        private static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        private void Update()
        {
            if (_finished) return;
            _elapsed += Time.deltaTime;

            // Bars and rim in at the start, out at the end.
            var bars = _elapsed < BarsIn ? Smooth(_elapsed / BarsIn) : _elapsed > _duration - BarsIn ? Smooth((_duration - _elapsed) / BarsIn) : 1f;
            if (_top != null) _top.anchoredPosition = new Vector2(0f, BarHeight * (1f - bars));
            if (_bottom != null) _bottom.anchoredPosition = new Vector2(0f, -(ScreenLayout.Height - BarHeight * bars));
            if (_vignette != null) _vignette.color = new Color(1f, 1f, 1f, bars);

            if (!_revealed && _elapsed >= RevealAt) Reveal();
            AnimateReveal();
            AnimateCard();

            if (_elapsed >= SkipGrace && SkipPressed())
            {
                WasSkipped = true;
                Finish();
                return;
            }

            if (_elapsed >= _duration || (_boss == null && _elapsed >= TravelIn)) Finish();
        }

        /// <summary>The name band: slides in and opens, holds, fades before the camera leaves.</summary>
        private void AnimateCard()
        {
            if (_card == null) return;
            var inT = (_elapsed - CardIn) / CardSlide;
            var outT = (_elapsed - (_duration - TravelOut - CardOut * 0.5f)) / CardOut;
            _card.alpha = Mathf.Clamp01(inT) * (1f - Mathf.Clamp01(outT));
            if (_cardRect != null) _cardRect.anchoredPosition = new Vector2(Mathf.Round(-28f * (1f - EaseOut(inT))), 0f);

            var open = EaseOut((_elapsed - CardIn) / TitleWipe);
            if (_titleMask != null) _titleMask.sizeDelta = new Vector2(Mathf.Round((_titleWidth + 4) * open), _titleMask.sizeDelta.y);
            var grow = EaseOut((_elapsed - CardIn - 0.12f) / 0.3f);
            foreach (var rule in _rules) rule.sizeDelta = new Vector2(Mathf.Max(1f, Mathf.Round(_ruleLength * grow)), 1f);
            if (_identity != null) _identity.alpha = Mathf.Clamp01((_elapsed - CardIn - 0.2f) / 0.2f);
        }

        // ---------------------------------------------------------------- the reveal beat

        private void Reveal()
        {
            _revealed = true;
            _revealTime = _elapsed;
            if (_boss == null) return;
            var at = (Vector2)_boss.position;

            // The boss flares in its glow: a copy of its own frame drawn over it, fading (the body itself is untouched).
            _bossBody = FindBody(_bossController);
            if (_bossBody != null)
            {
                var flare = new GameObject("RevealFlare");
                flare.transform.SetParent(_bossBody.transform, false);
                _bossFlare = flare.AddComponent<SpriteRenderer>();
                _bossFlare.sortingLayerID = _bossBody.sortingLayerID;
                _bossFlare.sortingOrder = _bossBody.sortingOrder + 1;
                _bossFlare.color = new Color(_style.Glow.r, _style.Glow.g, _style.Glow.b, 0f);
            }

            // The floor answers: a shockwave in the accent runs out from under the boss.
            var wave = new GameObject("RevealShockwave");
            wave.transform.SetParent(transform, false);
            wave.transform.position = new Vector3(at.x, at.y, 0f);
            _shockwave = wave.AddComponent<SpriteRenderer>();
            _shockwave.sprite = ShockwaveFrame(0);
            _shockwave.color = DustColor(1f);
            SpriteSorting.Apply(_shockwave, SortingRole.Hazard);
            _shockwave.sortingOrder += 50;

            SpawnReaction(at);
            if (_flash != null) _flash.color = FlashColor(0.1f);
            _shake?.Request(ShakeKind.BossSlam);
            _revealCue?.Invoke(at);
        }

        /// <summary>The reveal glint: white with a breath of the boss's glow (a coloured wash would read as damage).</summary>
        private Color FlashColor(float alpha)
        {
            var c = Color.Lerp(Color.white, _style.Glow, 0.25f);
            c.a = Mathf.Max(0f, alpha);
            return c;
        }

        /// <summary>
        /// The kicked-up matter of the shockwave, in the biome's own stuff — frost white, tunnel grit, forge embers, spore
        /// green, or for power and signal the boss's own light — never a telegraph's red or orange outline.
        /// </summary>
        private Color DustColor(float alpha)
        {
            var c = _style.ArenaReaction switch
            {
                BossIntroStyle.Reaction.Frost => new Color(0.86f, 0.94f, 0.96f),
                BossIntroStyle.Reaction.Debris => new Color(0.72f, 0.70f, 0.63f),
                BossIntroStyle.Reaction.Embers => new Color(0.95f, 0.66f, 0.36f),
                BossIntroStyle.Reaction.Spores => Color.Lerp(_style.Accent, Color.white, 0.3f),
                _ => Color.Lerp(_style.Accent, Color.white, 0.45f)
            };
            c.a = Mathf.Clamp01(alpha);
            return c;
        }

        private static SpriteRenderer FindBody(BossController boss)
        {
            if (boss == null) return null;
            foreach (var renderer in boss.GetComponentsInChildren<SpriteRenderer>(true))
                if (renderer != null && renderer.name == CharacterVisual.BodyName) return renderer;
            return null;
        }

        private void AnimateReveal()
        {
            if (!_revealed) return;
            var since = _elapsed - _revealTime;
            if (_flash != null) _flash.color = FlashColor(Mathf.Lerp(0.1f, 0f, since / 0.18f));

            if (_bossFlare != null && _bossBody != null)
            {
                _bossFlare.sprite = _bossBody.sprite;
                _bossFlare.flipX = _bossBody.flipX;
                // Two pulses: the flare, then a softer after-glow as the name lands.
                var a = since < FlashSeconds ? 0.85f * (1f - since / FlashSeconds)
                    : since < FlashSeconds + 0.35f ? 0.3f * Mathf.Sin((since - FlashSeconds) / 0.35f * Mathf.PI) : 0f;
                _bossFlare.color = new Color(_style.Glow.r, _style.Glow.g, _style.Glow.b, a);
            }

            if (_shockwave != null)
            {
                var t = since / ShockwaveSeconds;
                if (t >= 1f) { Destroy(_shockwave.gameObject); _shockwave = null; }
                else
                {
                    _shockwave.sprite = ShockwaveFrame(Mathf.Min(ShockwaveFrames - 1, Mathf.FloorToInt(t * ShockwaveFrames)));
                    _shockwave.color = DustColor(1f - t * 0.7f);
                }
            }

            for (var i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.Age += Time.deltaTime;
                if (p.Age >= p.Life || p.Renderer == null)
                {
                    if (p.Renderer != null) Destroy(p.Renderer.gameObject);
                    _particles.RemoveAt(i);
                    continue;
                }

                p.Velocity *= Mathf.Max(0f, 1f - p.Drag * Time.deltaTime);
                p.Velocity += Vector2.down * (p.Gravity * Time.deltaTime);
                p.Position += p.Velocity * Time.deltaTime;
                // Whole pixels: the matter moves on the same 1/32 grid as the art it flies over.
                p.Renderer.transform.position = new Vector3(Mathf.Round(p.Position.x * 32f) / 32f, Mathf.Round(p.Position.y * 32f) / 32f, 0f);
                var life = p.Age / p.Life;
                var c = Color.Lerp(_style.Glow, DustColor(1f), Mathf.Clamp01(life * 1.6f));
                c.a = life < 0.7f ? 1f : 1f - (life - 0.7f) / 0.3f;
                p.Renderer.color = c;
            }
        }

        /// <summary>The arena's answer in the biome's own matter, thrown out from the boss's footing.</summary>
        private void SpawnReaction(Vector2 at)
        {
            var rng = new System.Random(Title.GetHashCode());
            var count = _style.ArenaReaction == BossIntroStyle.Reaction.Spores ? 22 : 30;
            for (var i = 0; i < count; i++)
            {
                var angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.7f);
                var speed = 1f + (float)rng.NextDouble();
                var particle = new Particle { Position = at + dir * 0.4f, Life = 0.7f + (float)rng.NextDouble() * 0.35f };
                switch (_style.ArenaReaction)
                {
                    case BossIntroStyle.Reaction.Sparks: particle.Velocity = dir * (speed * 5.5f); particle.Drag = 4.5f; particle.Life *= 0.7f; break;
                    case BossIntroStyle.Reaction.Embers: particle.Velocity = dir * (speed * 2.2f) + Vector2.up * 1.6f; particle.Drag = 1.6f; particle.Gravity = -1.4f; break;
                    case BossIntroStyle.Reaction.Spores: particle.Velocity = dir * (speed * 1.3f) + Vector2.up * 0.5f; particle.Drag = 1.2f; particle.Gravity = -0.5f; particle.Life *= 1.3f; break;
                    case BossIntroStyle.Reaction.Frost: particle.Velocity = dir * (speed * 3.8f); particle.Drag = 3f; particle.Gravity = 1.2f; break;
                    default: particle.Velocity = dir * (speed * 3.2f) + Vector2.up * 2f; particle.Drag = 2f; particle.Gravity = 7f; break;
                }

                var go = new GameObject("RevealMatter");
                go.transform.SetParent(transform, false);
                particle.Renderer = go.AddComponent<SpriteRenderer>();
                particle.Renderer.sprite = MatterSprite(_style.ArenaReaction == BossIntroStyle.Reaction.Sparks && i % 2 == 1 ? 1 : 2);
                SpriteSorting.Apply(particle.Renderer, SortingRole.WorldVfx);
                _particles.Add(particle);
            }
        }

        // ---------------------------------------------------------------- pixel art made once

        private static Sprite _vignetteSprite;
        private static readonly Sprite[] Shockwave = new Sprite[ShockwaveFrames];
        private static readonly Sprite[] Matter = new Sprite[3];

        private static readonly int[] Bayer =
        {
            0, 8, 2, 10,
            12, 4, 14, 6,
            3, 11, 1, 9,
            15, 7, 13, 5
        };

        /// <summary>A dark rim at reference resolution, ordered-dithered into three steps (no smooth gradient on pixel art).</summary>
        private static Sprite VignetteSprite()
        {
            if (_vignetteSprite != null) return _vignetteSprite;
            const int w = ScreenLayout.Width;
            const int h = ScreenLayout.Height;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var pixels = new Color32[w * h];
            var ink = (Color32)UiTheme.NearBlack;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var dx = (x + 0.5f - w / 2f) / (w / 2f);
                var dy = (y + 0.5f - h / 2f) / (h / 2f);
                var d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy * 0.6f);
                var strength = Mathf.Clamp01((d - 0.62f) / 0.5f);
                var threshold = (Bayer[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f;
                var step = strength * 3f;
                var level = Mathf.FloorToInt(step) + (step - Mathf.Floor(step) > threshold ? 1 : 0);
                pixels[y * w + x] = new Color32(ink.r, ink.g, ink.b, (byte)(Mathf.Clamp(level, 0, 3) * 55));
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            _vignetteSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 1f);
            _vignetteSprite.hideFlags = HideFlags.DontSave;
            return _vignetteSprite;
        }

        /// <summary>
        /// One frame of the floor shockwave, white (tinted by the boss's accent), native 32 px/tile: a 2 px ring that runs
        /// out to the full radius, thinning and breaking into a dotted edge as it fades.
        /// </summary>
        private static Sprite ShockwaveFrame(int frame)
        {
            if (Shockwave[frame] != null) return Shockwave[frame];
            var size = ShockwaveRadius * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var pixels = new Color32[size * size];
            var t = (frame + 1f) / ShockwaveFrames;
            var radius = Mathf.Lerp(10f, ShockwaveRadius, Mathf.Sqrt(t));
            var thickness = frame < 2 ? 3.5f : frame < 4 ? 2.5f : 1.5f;
            var c = size / 2f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = x + 0.5f - c;
                var dy = (y + 0.5f - c) / 0.62f; // flattened onto the floor plane
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                // A broken front of dust and grit, ragged in reach and density — never a clean ring, which is the
                // telegraph's language for "danger lands here".
                var angle = Mathf.Atan2(dy, dx);
                var ragged = radius * (0.88f + 0.12f * Noise(Mathf.FloorToInt((angle + Mathf.PI) * 9f), 7));
                if (Mathf.Abs(d - ragged) > thickness) continue;
                if (Noise(x * 31 + y, frame) < (frame < 2 ? 0.45f : frame < 4 ? 0.62f : 0.78f)) continue;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(frame < 3 ? 255 : 190));
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            Shockwave[frame] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
            Shockwave[frame].hideFlags = HideFlags.DontSave;
            return Shockwave[frame];
        }

        private static float Noise(int a, int b)
        {
            unchecked
            {
                var h = (uint)(a * 374761393 + b * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        /// <summary>A square fleck of <paramref name="pixels"/> px at native density.</summary>
        private static Sprite MatterSprite(int pixels)
        {
            pixels = Mathf.Clamp(pixels, 1, 2);
            if (Matter[pixels] != null) return Matter[pixels];
            var tex = new Texture2D(pixels, pixels, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            var fill = new Color32[pixels * pixels];
            for (var i = 0; i < fill.Length; i++) fill[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(fill);
            tex.Apply(false, true);
            Matter[pixels] = Sprite.Create(tex, new Rect(0, 0, pixels, pixels), new Vector2(0.5f, 0.5f), 32f);
            Matter[pixels].hideFlags = HideFlags.DontSave;
            return Matter[pixels];
        }

        // ---------------------------------------------------------------- end

        private static bool SkipPressed()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            return (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame))
                   || (mouse != null && mouse.leftButton.wasPressedThisFrame)
                   || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
        }

        private void OnHoldEnded(BossEngagement engagement) => Finish();

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
            if (_engagement != null) _engagement.IntroEnded -= OnHoldEnded;
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
            if (_engagement != null) _engagement.IntroEnded -= OnHoldEnded;
            RestoreHud();
            // The flare rides on the boss's body, not under this object: it goes with the intro.
            if (_bossFlare != null) Destroy(_bossFlare.gameObject);
            if (Current == this) Current = null;
        }
    }
}
