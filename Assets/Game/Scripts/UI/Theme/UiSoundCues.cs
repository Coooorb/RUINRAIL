using System;
using RuinRail.Core.Rendering;
using RuinRail.UI.Navigation;
using UnityEngine;

namespace RuinRail.UI.Theme
{
    /// <summary>
    /// A front-end screen's UI sound feedback, on the existing <see cref="UiSoundBus"/>.
    ///
    /// The screen's input layer (focus steps, confirm, back), its controls (pointer hover, a click on a disabled control)
    /// and its own action outcomes (an accepted purchase, a refused move) report <em>cues</em> here; at the end of the
    /// frame the one that matters most is played — Failure over Purchase over Confirm over Cancel over Navigate — so a
    /// confirm that turns into a refused purchase sounds like the refusal, never like both. A view model that already
    /// speaks for itself on the bus (the inventory, the multiplayer terminal) wins the frame: this stays quiet then,
    /// so nothing is ever heard twice.
    ///
    /// Opt-in per screen (the screen adds and binds it), presentation only: it never changes what an input does.
    /// </summary>
    public sealed class UiSoundCues : MonoBehaviour
    {
        /// <summary>The shortest gap between two hover ticks: sweeping the pointer across a row of tabs is not a drumroll.</summary>
        public const float HoverTickSeconds = 0.06f;

        private UiSound? _pending;
        private bool _external;
        private bool _raising;
        private float _lastHover = -1f;
        private Func<bool> _isLive;

        /// <summary>Sounds this component played (tests / diagnostics), by kind.</summary>
        public int Played { get; private set; }
        public UiSound? LastPlayed { get; private set; }

        /// <summary>
        /// Starts listening. <paramref name="isLive"/> says whether the screen currently owns the input (false while a
        /// scene transition or another owner holds it), so stray global events never sound for a hidden screen.
        /// </summary>
        public void Bind(Func<bool> isLive = null)
        {
            _isLive = isLive;
            FocusItem.AnyActivation -= OnActivation;
            FocusItem.AnyActivation += OnActivation;
            UiControl.AnyHoverEntered -= OnHover;
            UiControl.AnyHoverEntered += OnHover;
            UiControl.AnyClickRefused -= OnRefused;
            UiControl.AnyClickRefused += OnRefused;
            UiSoundBus.Raised -= OnBusRaised;
            UiSoundBus.Raised += OnBusRaised;
        }

        private void OnDestroy()
        {
            FocusItem.AnyActivation -= OnActivation;
            UiControl.AnyHoverEntered -= OnHover;
            UiControl.AnyClickRefused -= OnRefused;
            UiSoundBus.Raised -= OnBusRaised;
        }

        private bool Live => isActiveAndEnabled && (_isLive == null || _isLive());

        // ---- cues ----

        /// <summary>A keyboard / controller step (true = the focus or value moved; a step against the end is silent).</summary>
        public void Step(bool moved) { if (moved) Cue(UiSound.Navigate); }

        /// <summary>A section (tab) opened or switched.</summary>
        public void Section() => Cue(UiSound.Confirm);

        /// <summary>Back / cancel closed something.</summary>
        public void Back() => Cue(UiSound.Cancel);

        /// <summary>An action's own result: refused (error), a coin transaction, or a plain success.</summary>
        public void Outcome(bool error, bool purchase = false) => Cue(error ? UiSound.Failure : purchase ? UiSound.Purchase : UiSound.Confirm);

        public void Cue(UiSound sound)
        {
            if (!Live) return;
            if (_pending == null || Rank(sound) > Rank(_pending.Value)) _pending = sound;
        }

        private void OnActivation(FocusItem item, bool ran) => Cue(ran ? UiSound.Confirm : UiSound.Failure);

        private void OnRefused(UiControl control) => Cue(UiSound.Failure);

        private void OnHover(UiControl control)
        {
            if (!Live || Time.unscaledTime - _lastHover < HoverTickSeconds) return;
            _lastHover = Time.unscaledTime;
            Cue(UiSound.Navigate);
        }

        private void OnBusRaised(UiSound _)
        {
            if (!_raising) _external = true;
        }

        private static int Rank(UiSound sound) => sound switch
        {
            UiSound.Failure => 5,
            UiSound.Purchase => 4,
            UiSound.Confirm => 3,
            UiSound.Cancel => 2,
            _ => 1
        };

        private void LateUpdate()
        {
            var pending = _pending;
            var external = _external;
            _pending = null;
            _external = false;
            if (pending == null || external) return; // someone already spoke for this frame's action
            _raising = true;
            try { UiSoundBus.Raise(pending.Value); }
            finally { _raising = false; }
            Played++;
            LastPlayed = pending.Value;
        }
    }
}
