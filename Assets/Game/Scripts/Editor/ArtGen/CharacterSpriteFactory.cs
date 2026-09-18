using System.Collections.Generic;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// Builds one character family's frames from a <see cref="CharacterProfile"/>.
    ///
    /// Construction follows FINAL_ART_PRODUCTION_SPEC section 4.1: a top-down three-quarter arcade perspective where
    /// the top of the head and shoulders read, the torso is legible and the feet anchor the body. Shapes are laid
    /// down as flat material masses first, then shaded in discrete ramp steps against the north-west key light, then
    /// given a selective exterior outline. That order matters — outlining before shading would flatten the silhouette.
    ///
    /// Facings are built from three drawn aspects (front, three-quarter, back) plus a side aspect, mirrored for the
    /// left half of the compass. Spec 4.1 permits mirroring where handedness stays readable, which holds here because
    /// the carried weapon is a separate WeaponPivot sprite rather than baked into the body.
    /// </summary>
    public static class CharacterSpriteFactory
    {
        /// <summary>Frames per state, chosen inside the spec 5.2 ranges.</summary>
        public static int FrameCount(VisualState state) => state switch
        {
            VisualState.Idle => 2,
            VisualState.Move => 4,
            VisualState.Attack => 4,
            VisualState.Hit => 2,
            VisualState.Special => 4,
            VisualState.Death => 5,
            _ => 2
        };

        public static PixelCanvas Build(CharacterProfile p, Facing8 facing, VisualState state, int frame)
        {
            var c = new PixelCanvas(p.CanvasWidth, p.CanvasHeight);
            var mirrored = facing is Facing8.NW or Facing8.W or Facing8.SW;
            var aspect = AspectOf(facing);

            // Animation offsets, kept to the 1-2 px shifts spec 5.4 asks for.
            var (bodyLift, lean, armSwing, collapse) = Pose(state, frame, p);

            DrawBody(c, p, aspect, bodyLift, lean, armSwing, collapse, state, frame);

            if (collapse > 0) ApplyCollapse(c, p, collapse);

            var emissive = CollectEmissive(c, p);
            c.SelectiveOutline(p.Outline, emissive);

            return mirrored ? c.MirroredX() : c;
        }

        /// <summary>Which drawn aspect a facing uses. Left-side facings reuse the right-side drawing, mirrored.</summary>
        private enum Aspect { Front, ThreeQuarter, Side, BackThreeQuarter, Back }

        private static Aspect AspectOf(Facing8 f) => f switch
        {
            Facing8.S => Aspect.Front,
            Facing8.SE or Facing8.SW => Aspect.ThreeQuarter,
            Facing8.E or Facing8.W => Aspect.Side,
            Facing8.NE or Facing8.NW => Aspect.BackThreeQuarter,
            _ => Aspect.Back
        };

        private static (int lift, int lean, int swing, int collapse) Pose(VisualState state, int frame, CharacterProfile p)
        {
            switch (state)
            {
                case VisualState.Idle:
                    return (frame == 1 ? -1 : 0, 0, 0, 0);
                case VisualState.Move:
                    // Feet plant rather than glide: the body rises on the passing frames only.
                    return (frame is 1 or 3 ? 1 : 0, 0, frame switch { 1 => 2, 3 => -2, _ => 0 }, 0);
                case VisualState.Attack:
                    // Anticipation pulls back, strike pushes forward (spec 5.5).
                    return (0, frame switch { 0 => -1, 1 => -2, 2 => 3, _ => 1 }, frame >= 2 ? 3 : -2, 0);
                case VisualState.Hit:
                    return (0, frame == 0 ? -2 : -1, -1, 0);
                case VisualState.Special:
                    return (frame is 1 or 2 ? -1 : 0, 0, frame is 1 or 2 ? 2 : 0, 0);
                case VisualState.Death:
                    return (0, 0, 0, frame);
                default:
                    return (0, 0, 0, 0);
            }
        }

        private static void DrawBody(PixelCanvas c, CharacterProfile p, Aspect aspect,
            int lift, int lean, int swing, int collapse, VisualState state, int frame)
        {
            var cx = p.CanvasWidth / 2;
            var lean_ = p.ForwardLean + lean;

            var torsoTop = p.TorsoTop + lift;
            var torsoBottom = p.TorsoBottom + lift;
            var legBottom = p.LegBottom;

            var shoulderHalf = p.ShoulderHalfWidth + p.BulkBoost;
            var torsoHalf = p.TorsoHalfWidth + p.BulkBoost / 2;
            var hipHalf = p.HipHalfWidth;
            // A visible waist is what separates a body from a slab; the spec's torso/hip widths imply one.
            var waistHalf = Mathf.Max(2, Mathf.Min(torsoHalf, hipHalf) - 1);

            if (aspect == Aspect.Side)
            {
                shoulderHalf = Mathf.Max(3, shoulderHalf - 3);
                torsoHalf = Mathf.Max(3, torsoHalf - 2);
                waistHalf = Mathf.Max(2, waistHalf - 1);
            }
            else if (aspect is Aspect.ThreeQuarter or Aspect.BackThreeQuarter)
            {
                shoulderHalf = Mathf.Max(3, shoulderHalf - 1);
            }

            // --- backpack / apparatus ---
            // From the front it peeks out behind the shoulders; from behind it is the dominant read. Drawing it
            // before the torso for front views and after for back views is what makes N and S tell apart at a glance.
            var backView = aspect is Aspect.Back or Aspect.BackThreeQuarter;
            void DrawBackpack()
            {
                if (p.BackpackSize <= 0) return;
                // From the side the pack is seen edge-on: a narrow slab behind the back, not the full width, or it
                // reads as a board bolted to the character.
                var bw = aspect == Aspect.Side
                    ? Mathf.Max(3, p.BackpackSize / 3)
                    : backView ? p.BackpackSize : p.BackpackSize + 2;
                var bh = Mathf.Max(5, (backView ? p.BackpackSize : p.BackpackSize) - 1);
                var bx = aspect == Aspect.Side ? cx - torsoHalf - bw : cx - bw / 2;
                var by = torsoBottom + 3;
                c.Rect(bx, by, bw, bh, p.Secondary.Base);
                c.ShadeForm(bx, by, bw, bh, p.Secondary.Base, p.Secondary);
                c.Line(bx + 1, by + bh - 2, bx + bw - 2, by + bh - 2, RuinPalette.Darken(p.Secondary.Base, 0.45f));
                if (backView)
                {
                    // Straps and a lashed bedroll, so the back view carries its own detail.
                    c.Line(bx + 2, by, bx + 2, by + bh - 1, RuinPalette.Darken(p.Accent.Base, 0.2f));
                    c.Line(bx + bw - 3, by, bx + bw - 3, by + bh - 1, RuinPalette.Darken(p.Accent.Base, 0.2f));
                    c.Rect(bx + 1, by + bh - 3, bw - 2, 2, RuinPalette.Darken(p.Primary.Base, 0.2f));
                }
            }

            if (!backView) DrawBackpack();

            // --- legs: two separated columns so the gap reads at 1x ---
            var waistY = torsoBottom + 2;
            if (!p.WideLowBody)
            {
                var stride = state == VisualState.Move ? Mathf.Abs(swing) : 0;
                // A leg narrower than 3 px reads as a stilt at 1x. Width comes from the hips, with a 2 px gap kept
                // between them so the separation survives the outline pass.
                var legHalfWidth = Mathf.Max(3, hipHalf - 1);
                var gap = 1;
                var leftX = cx - gap - legHalfWidth;
                var rightX = cx + gap;
                var leftBottom = legBottom + (state == VisualState.Move && frame == 1 ? 1 : 0);
                var rightBottom = legBottom + (state == VisualState.Move && frame == 3 ? 1 : 0);

                if (p.Machine)
                {
                    // Machine legs: straight plated struts.
                    c.Rect(leftX, leftBottom, legHalfWidth, waistY - leftBottom, p.Secondary.Base);
                    c.ShadeForm(leftX, leftBottom, legHalfWidth, waistY - leftBottom, p.Secondary.Base, p.Secondary);
                    c.Rect(rightX, rightBottom, legHalfWidth, waistY - rightBottom, p.Secondary.Base);
                    c.ShadeForm(rightX, rightBottom, legHalfWidth, waistY - rightBottom, p.Secondary.Base, p.Secondary);
                }
                else
                {
                    c.Rect(leftX, leftBottom, legHalfWidth, waistY - leftBottom, p.Primary.Base);
                    c.ShadeForm(leftX, leftBottom, legHalfWidth, waistY - leftBottom, p.Primary.Base, p.Primary);
                    c.Rect(rightX, rightBottom, legHalfWidth, waistY - rightBottom, p.Primary.Base);
                    c.ShadeForm(rightX, rightBottom, legHalfWidth, waistY - rightBottom, p.Primary.Base, p.Primary);
                }

                // Boots: a darker, slightly wider mass that anchors the sprite to the ground.
                var bootRamp = RuinPalette.RampOf(RuinPalette.Darken(p.Secondary.Base, 0.4f));
                c.Rect(leftX - 1, leftBottom, legHalfWidth + 2, 3, bootRamp.Base);
                c.ShadeForm(leftX - 1, leftBottom, legHalfWidth + 2, 3, bootRamp.Base, bootRamp);
                c.Rect(rightX - 1, rightBottom, legHalfWidth + 2, 3, bootRamp.Base);
                c.ShadeForm(rightX - 1, rightBottom, legHalfWidth + 2, 3, bootRamp.Base, bootRamp);
                if (stride > 0) c.Rect(rightX - 1 + stride / 2, rightBottom, 1, 1, bootRamp.Shadow);
            }
            else
            {
                // Wide low body: a chassis/skirt mass instead of legs.
                var chassisHalf = shoulderHalf + 4;
                c.Taper(cx, waistY, legBottom, chassisHalf - 2, chassisHalf, p.Secondary.Base);
                c.ShadeForm(cx - chassisHalf, legBottom, chassisHalf * 2, waistY - legBottom + 1, p.Secondary.Base, p.Secondary);
            }

            // --- torso: shoulders taper to waist, then hips flare slightly ---
            var chestTop = torsoTop - 1;
            var torsoX = cx + lean_ / 2;
            if (p.WideLowBody)
            {
                c.Taper(torsoX, chestTop, waistY, shoulderHalf, shoulderHalf + 2, p.Primary.Base);
                c.ShadeForm(torsoX - shoulderHalf - 2, waistY, (shoulderHalf + 2) * 2 + 1, chestTop - waistY + 1, p.Primary.Base, p.Primary);
            }
            else
            {
                var chestHalf = p.Hunched ? torsoHalf + 1 : torsoHalf;
                // Chest block down to the waist.
                c.Taper(torsoX, chestTop, waistY + 2, chestHalf, waistHalf, p.Primary.Base);
                // Hip block from the waist to the legs.
                c.Taper(torsoX, waistY + 2, waistY - 1, waistHalf, hipHalf, p.Primary.Base);
                c.ShadeForm(torsoX - chestHalf - 1, waistY - 1, (chestHalf + 1) * 2 + 1, chestTop - waistY + 3, p.Primary.Base, p.Primary);
            }

            // --- shoulder caps: small pauldrons that cap the shoulder line, never exceeding it ---
            if (!p.WideLowBody)
            {
                // Kept deliberately small: a cap wider than the torso reads as a mitten, not a shoulder.
                var capR = Mathf.Clamp(1 + p.BulkBoost / 2, 2, 3);
                var capY = chestTop - 1;
                foreach (var side in new[] { -1, 1 })
                {
                    var sxc = torsoX + side * (shoulderHalf - capR);
                    c.Ellipse(sxc, capY, capR + 0.5f, capR, p.Secondary.Base);
                    c.ShadeForm(sxc - capR - 1, capY - capR, capR * 2 + 3, capR * 2 + 1, p.Secondary.Base, p.Secondary);
                }
            }

            // Chest harness: a value break across the torso so it never reads as one flat slab.
            var beltY = waistY + 1;
            var beltHalf = Mathf.Max(2, waistHalf);
            c.Rect(torsoX - beltHalf, beltY, beltHalf * 2, 2, p.Accent.Base);
            c.ShadeForm(torsoX - beltHalf, beltY, beltHalf * 2, 2, p.Accent.Base, p.Accent);

            // --- jacket / harness detail (spec 6: layered jacket, utility harness, pouches) ---
            if (!p.WideLowBody && !p.Machine)
            {
                var collarY = chestTop - 2;
                // Collar: a darker band under the head that separates head from torso.
                c.Line(torsoX - torsoHalf + 1, collarY, torsoX + torsoHalf - 1, collarY, RuinPalette.Darken(p.Primary.Base, 0.4f));

                if (aspect is Aspect.Front or Aspect.ThreeQuarter)
                {
                    // Jacket opening: a vertical value break down the chest.
                    c.Line(torsoX, beltY + 2, torsoX, collarY - 1, RuinPalette.Darken(p.Primary.Base, 0.42f));
                    // Diagonal utility strap across the chest.
                    c.Line(torsoX - torsoHalf + 1, beltY + 3, torsoX + torsoHalf - 2, collarY - 1,
                        RuinPalette.Darken(p.Accent.Base, 0.25f));
                    // Two small pouches on the belt line.
                    c.Rect(torsoX - beltHalf + 1, beltY - 2, 2, 2, RuinPalette.Darken(p.Secondary.Base, 0.2f));
                    c.Rect(torsoX + beltHalf - 3, beltY - 2, 2, 2, RuinPalette.Darken(p.Secondary.Base, 0.2f));
                }
                else if (aspect is Aspect.Back or Aspect.BackThreeQuarter)
                {
                    // Back view: a yoke seam instead of the jacket opening, so front and back differ at a glance.
                    c.Line(torsoX - torsoHalf + 1, collarY - 3, torsoX + torsoHalf - 1, collarY - 3,
                        RuinPalette.Darken(p.Primary.Base, 0.4f));
                }
            }

            if (p.WarningStripes && aspect is Aspect.Front or Aspect.ThreeQuarter)
            {
                for (var x = torsoX - torsoHalf + 1; x < torsoX + torsoHalf - 1; x += 3)
                    c.Line(x, chestTop - 4, x + 1, chestTop - 2, RuinPalette.WarningOchre);
            }

            // --- arms: upper arm from the shoulder cap, forearm angled by the pose ---
            var armY = chestTop - 2;
            var reach = 5 + swing;
            foreach (var side in new[] { -1, 1 })
            {
                var ax = torsoX + side * (shoulderHalf - 1);
                var elbowY = armY - reach / 2;
                var handY = armY - reach;
                var handX = ax + side * (aspect == Aspect.Side ? 2 : 1);

                // The sleeve is lifted off the jacket value so the arm separates from the torso at 1x — with both at
                // the same value the limb disappears into the body, which is what the first pass got wrong.
                var sleeveBase = side < 0
                    ? RuinPalette.Lighten(p.Primary.Base, 0.18f)
                    : RuinPalette.Darken(p.Primary.Base, 0.18f);
                var ramp = RuinPalette.RampOf(sleeveBase);

                c.Line(ax, armY, ax, elbowY, ramp.Base, p.ArmThickness);
                c.Line(ax, elbowY, handX, handY, ramp.Base, Mathf.Max(2, p.ArmThickness - 1));
                c.ShadeForm(ax - p.ArmThickness, handY - 1, p.ArmThickness * 2 + 2, armY - handY + 3, ramp.Base, ramp);

                // Glove: a small dark mass so the limb terminates rather than fading out.
                var gloveRamp = RuinPalette.RampOf(RuinPalette.Darken(p.Secondary.Base, 0.35f));
                c.Dot(handX, handY, gloveRamp.Base, Mathf.Max(2, p.ArmThickness - 1));
                c.ShadeForm(handX - 2, handY - 2, 5, 5, gloveRamp.Base, gloveRamp);
            }

            if (p.BladeArm)
            {
                // Elongated blade forearm — the Tunnel Stalker's signature silhouette.
                var bx = cx + shoulderHalf + 2;
                c.Line(bx, armY - reach, bx + 5, armY - reach - 7, RuinPalette.PaleSteel, 2);
                c.Line(bx + 5, armY - reach - 7, bx + 7, armY - reach - 11, RuinPalette.Lighten(RuinPalette.PaleSteel, 0.3f));
            }

            if (p.OvergrownArm)
            {
                // Asymmetric mutated limb: one shoulder mass reads much heavier than the other.
                var ox = cx + shoulderHalf;
                c.Ellipse(ox + 2, armY - 2, 5, 6, p.Accent.Base);
                c.ShadeForm(ox - 4, armY - 9, 12, 14, p.Accent.Base, p.Accent);
            }

            if (p.FrontShield)
            {
                // Shield dominates the frontal silhouette in every facing that can see it.
                var sw = shoulderHalf * 2 + 2;
                var sy = torsoBottom;
                var sx = cx - sw / 2;
                if (aspect == Aspect.Side) { sw = 5; sx = cx + torsoHalf; }
                if (aspect is Aspect.Front or Aspect.ThreeQuarter or Aspect.Side)
                {
                    c.Rect(sx, sy, sw, torsoTop - torsoBottom + 3, p.Secondary.Base);
                    c.ShadeForm(sx, sy, sw, torsoTop - torsoBottom + 3, p.Secondary.Base, p.Secondary);
                    c.RectOutline(sx, sy, sw, torsoTop - torsoBottom + 3, RuinPalette.Darken(p.Secondary.Base, 0.5f));
                    c.Line(sx + 1, sy + 2, sx + sw - 2, sy + 2, RuinPalette.WarningOchre);
                }
            }

            if (p.CarriedWeaponLength > 0 && aspect != Aspect.Back)
            {
                // A long weapon carried in silhouette (Shooter, Sniper). The equipped weapon sprite is separate; this
                // is body-language mass so the archetype reads before any weapon is drawn.
                var wy = armY - reach + 1;
                var wx = cx + shoulderHalf + 1;
                c.Line(wx, wy, wx + p.CarriedWeaponLength, wy - p.CarriedWeaponLength / 3, RuinPalette.DarkSteel, 2);
                c.Dot(wx + p.CarriedWeaponLength, wy - p.CarriedWeaponLength / 3, RuinPalette.Darken(RuinPalette.DarkSteel, 0.3f), 2);
            }

            // --- neck + head: the head sits ON the shoulders, slightly overlapping, never floating ---
            var headY = chestTop + p.HeadRadiusY - (p.Hunched ? 3 : 1);
            var headX = torsoX + (aspect == Aspect.Side ? 2 : aspect is Aspect.ThreeQuarter ? 1 : 0);

            if (!p.Machine)
            {
                c.Rect(headX - 2, chestTop - 1, 4, 3, RuinPalette.Darken(p.Skin.Base, 0.45f)); // neck in shadow
                c.Ellipse(headX, headY, p.HeadRadiusX, p.HeadRadiusY, p.Skin.Base);
                c.ShadeForm(headX - p.HeadRadiusX, headY - p.HeadRadiusY, p.HeadRadiusX * 2 + 1, p.HeadRadiusY * 2 + 1,
                    p.Skin.Base, p.Skin);
            }
            else
            {
                c.Rect(headX - 2, chestTop - 1, 4, 2, RuinPalette.Darken(p.Secondary.Base, 0.5f));
                c.Rect(headX - p.HeadRadiusX, headY - p.HeadRadiusY, p.HeadRadiusX * 2, p.HeadRadiusY * 2, p.Secondary.Base);
                c.ShadeForm(headX - p.HeadRadiusX, headY - p.HeadRadiusY, p.HeadRadiusX * 2, p.HeadRadiusY * 2,
                    p.Secondary.Base, p.Secondary);
            }

            if (backView) DrawBackpack();
            DrawHeadGear(c, p, headX, headY, aspect);

            if (p.AntennaHeight > 0)
            {
                c.Line(headX, headY + p.HeadRadiusY, headX, headY + p.HeadRadiusY + p.AntennaHeight, RuinPalette.DarkSteel);
                c.Set(headX, headY + p.HeadRadiusY + p.AntennaHeight, p.Emissive);
                // Crown/mast spread for Scrap King style silhouettes.
                if (p.AntennaHeight >= 5)
                {
                    c.Line(headX - 3, headY + p.HeadRadiusY + 2, headX - 4, headY + p.HeadRadiusY + p.AntennaHeight - 1, RuinPalette.DarkSteel);
                    c.Line(headX + 3, headY + p.HeadRadiusY + 2, headX + 4, headY + p.HeadRadiusY + p.AntennaHeight - 1, RuinPalette.DarkSteel);
                }
            }

            // --- emissive core ---
            if (p.CoreRadius > 0 && aspect != Aspect.Back)
            {
                var coreY = (torsoTop + torsoBottom) / 2 + 1;
                c.Ellipse(cx, coreY, p.CoreRadius + 1, p.CoreRadius + 1, RuinPalette.Darken(p.Emissive, 0.55f));
                c.Ellipse(cx, coreY, p.CoreRadius, p.CoreRadius, p.Emissive);
                if (state == VisualState.Special || (state == VisualState.Attack && frame >= 2))
                    c.Ellipse(cx, coreY, p.CoreRadius - 1, p.CoreRadius - 1, RuinPalette.Lighten(p.Emissive, 0.45f));
            }

            // --- wear pass (spec 2.2: clusters, never checkerboard speckle) ---
            if (p.Grime > 0f)
                c.Grime(cx - shoulderHalf - 2, legBottom, (shoulderHalf + 2) * 2, torsoTop - legBottom + 4,
                    RuinPalette.Darken(p.Primary.Base, 0.3f), p.Seed, p.Grime);
        }

        private static void DrawHeadGear(PixelCanvas c, CharacterProfile p, int hx, int hy, Aspect aspect)
        {
            switch (p.HeadGear)
            {
                case 1: // hood / cloth
                    c.Ellipse(hx, hy + 1, p.HeadRadiusX + 1, p.HeadRadiusY, p.Primary.Base);
                    c.Rect(hx - p.HeadRadiusX - 1, hy - 1, (p.HeadRadiusX + 1) * 2, 2, p.Primary.Shadow);
                    c.ShadeForm(hx - p.HeadRadiusX - 2, hy - 2, (p.HeadRadiusX + 2) * 2, p.HeadRadiusY * 2 + 3, p.Primary.Base, p.Primary);
                    break;
                case 2: // helmet
                    c.Ellipse(hx, hy + 2, p.HeadRadiusX + 1, p.HeadRadiusY - 1, p.Secondary.Base);
                    c.ShadeForm(hx - p.HeadRadiusX - 1, hy, (p.HeadRadiusX + 1) * 2, p.HeadRadiusY + 2, p.Secondary.Base, p.Secondary);
                    break;
                case 3: // visor / optic
                    c.Ellipse(hx, hy + 2, p.HeadRadiusX + 1, p.HeadRadiusY - 1, p.Secondary.Base);
                    c.ShadeForm(hx - p.HeadRadiusX - 1, hy, (p.HeadRadiusX + 1) * 2, p.HeadRadiusY + 2, p.Secondary.Base, p.Secondary);
                    if (aspect is not (Aspect.Back or Aspect.BackThreeQuarter))
                    {
                        c.Rect(hx - p.HeadRadiusX + 1, hy, p.HeadRadiusX * 2 - 2, 2, RuinPalette.Darken(p.Emissive, 0.6f));
                        c.Rect(hx - p.HeadRadiusX + 1, hy + 1, p.HeadRadiusX * 2 - 2, 1, p.Emissive);
                    }
                    break;
                case 4: // respirator / mask
                    c.Ellipse(hx, hy + 2, p.HeadRadiusX, p.HeadRadiusY - 1, p.Primary.Base);
                    if (aspect is not (Aspect.Back or Aspect.BackThreeQuarter))
                    {
                        c.Rect(hx - 2, hy - 2, 4, 3, p.Secondary.Base);
                        c.ShadeForm(hx - 2, hy - 2, 4, 3, p.Secondary.Base, p.Secondary);
                        c.Set(hx - 1, hy - 1, RuinPalette.Darken(p.Secondary.Base, 0.6f));
                        c.Set(hx + 1, hy - 1, RuinPalette.Darken(p.Secondary.Base, 0.6f));
                    }
                    break;
            }
        }

        /// <summary>
        /// Death: the body sinks and spreads rather than shrinking away, so identity survives until gameplay
        /// considers it dead (spec 5.4). Each row is compressed toward the ground and widened slightly, which reads
        /// as a slump; deleting rows — the first attempt — left a floating head and legs.
        /// </summary>
        private static void ApplyCollapse(PixelCanvas c, CharacterProfile p, int stage)
        {
            if (stage <= 0) return;
            var bounds = c.ContentBounds();
            if (bounds.height <= 3) return;

            var frames = FrameCount(VisualState.Death) - 1;
            var t = Mathf.Clamp01((float)stage / Mathf.Max(1, frames));
            var squash = Mathf.Lerp(1f, 0.32f, t);

            // Vertical squash only. Spreading horizontally as well leaves one-pixel gaps between source columns,
            // and the selective-outline pass then treats every gap edge as silhouette and darkens the whole sprite
            // into a smear — which is exactly what the first version did.
            var source = new List<(int x, int y, Color32 c)>();
            for (var y = bounds.yMin; y <= bounds.yMax; y++)
            for (var x = bounds.xMin; x <= bounds.xMax; x++)
                if (c.IsOpaque(x, y)) source.Add((x, y, c.Get(x, y)));

            foreach (var (x, y, _) in source) c.Erase(x, y);

            // Lowest source row wins each target row, so the body settles downward and stays solid.
            foreach (var (x, y, col) in source)
            {
                var ny = bounds.yMin + Mathf.RoundToInt((y - bounds.yMin) * squash);
                if (!c.IsOpaque(x, ny)) c.Set(x, ny, RuinPalette.Darken(col, 0.12f * stage));
            }

            // Close any single-row seams the squash opened up.
            for (var y = bounds.yMin + 1; y < bounds.yMin + Mathf.CeilToInt(bounds.height * squash); y++)
            for (var x = bounds.xMin; x <= bounds.xMax; x++)
                if (!c.IsOpaque(x, y) && c.IsOpaque(x, y - 1) && c.IsOpaque(x, y + 1))
                    c.Set(x, y, c.Get(x, y - 1));
        }

        private static HashSet<int> CollectEmissive(PixelCanvas c, CharacterProfile p)
        {
            var set = new HashSet<int>();
            for (var y = 0; y < c.Height; y++)
            for (var x = 0; x < c.Width; x++)
            {
                if (!c.IsOpaque(x, y)) continue;
                var col = c.Get(x, y);
                if (IsNear(col, p.Emissive) || IsNear(col, RuinPalette.Lighten(p.Emissive, 0.45f)))
                    set.Add(y * c.Width + x);
            }
            return set;
        }

        private static bool IsNear(Color32 a, Color32 b) =>
            Mathf.Abs(a.r - b.r) < 12 && Mathf.Abs(a.g - b.g) < 12 && Mathf.Abs(a.b - b.b) < 12;
    }
}
