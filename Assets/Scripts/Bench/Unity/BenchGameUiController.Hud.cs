using System.Collections.Generic;
using PowerliftingSimulator.Bench;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PowerliftingSimulator.Bench.Unity
{
    /// <summary>
    /// The active-lift HUD: the chief referee's signal banner (with its audio
    /// cue), the phase, what to do next, the live referee checks, the bar path
    /// and the chest pause meter, plus the camera. Everything shown is read
    /// from the last physical observation and the referee's own record.
    /// </summary>
    public sealed partial class BenchGameUiController
    {
        private const float BannerHoldSeconds = 2.5f;

        private Text _hudLoad, _hudPhase, _hudBanner, _hudInstruction, _hudFault, _hudCamera, _hudBarLabel;
        private Image _hudBannerBg, _hudBarFill, _hudPauseFill;
        private readonly Dictionary<string, Image> _lamps = new Dictionary<string, Image>(System.StringComparer.Ordinal);
        private BenchCommand _bannerCommand = BenchCommand.None;
        private float _bannerShownAt = -10f;
        private ulong _firstTouchTick;
        private bool _lockoutLogged;
        private int _chestStillTicks;

        public string DisplayedBanner => _hudBanner != null ? _hudBanner.text : string.Empty;
        public string DisplayedInstruction => _hudInstruction != null ? _hudInstruction.text : string.Empty;
        public string DisplayedPhase => _hudPhase != null ? _hudPhase.text : string.Empty;

        public void CycleCamera()
        {
            Resolve();
            _audio.PlayClick();
            _camera?.Cycle();
        }

        partial void OnCommandIssued(BenchCommand command)
        {
            _bannerCommand = command;
            _bannerShownAt = Time.unscaledTime;
            _audio.PlayCommand(command);
        }

        partial void HandleScreenHotkeys(Keyboard keyboard)
        {
            if (keyboard.cKey.wasPressedThisFrame)
                CycleCamera();
            HandleResultHotkeys(keyboard);
        }

        partial void HandleResultHotkeys(Keyboard keyboard);

        /// <summary>Touch and lockout moments, logged for the rehearsal driver and the pause meter.</summary>
        partial void UpdateSignals()
        {
            BenchObservation o = _bench.LastObservation;
            BenchReferee r = _bench.Referee;
            if (o.BarOnChest && _firstTouchTick == 0 && r.StartGiven)
            {
                _firstTouchTick = o.Tick;
                Debug.Log($"GAM69_UI touch tick={o.Tick} chest={o.BarChestForceN:F0}N");
            }
            if (!_lockoutLogged && r.PressGiven && o.ElbowsLocked && !o.BarOnChest && _bench.Controller.Phase == BenchMotionPhase.Holding)
            {
                _lockoutLogged = true;
                Debug.Log($"GAM69_UI lockout tick={o.Tick}");
            }
            _chestStillTicks = o.BarOnChest && !o.BarMoving ? _chestStillTicks + 1 : 0;
        }

        partial void RefreshHud()
        {
            BenchObservation o = _bench.LastObservation;
            BenchReferee r = _bench.Referee;
            BenchMotionPhase phase = _bench.Controller.Phase;

            _hudLoad.text = string.Format(C, "BENCH PRESS   {0:F1} kg", _bench.LoadKg);
            _hudPhase.text = PhaseLabel(phase, r);
            _hudCamera.text = "CAMERA: " + BenchCameraDirector.Label(_camera != null ? _camera.Angle : BenchCameraAngle.ThreeQuarter) + "   [C]";

            // The last signal, prominent for a moment; then the standing state.
            bool fresh = Time.unscaledTime - _bannerShownAt < BannerHoldSeconds;
            switch (fresh ? _bannerCommand : BenchCommand.None)
            {
                case BenchCommand.Start: SetBanner("START", Ok); break;
                case BenchCommand.Press: SetBanner("PRESS", Ok); break;
                case BenchCommand.Rack: SetBanner("RACK", Gold); break;
                case BenchCommand.Replace: SetBanner("REPLACE", Bad); break;
                default:
                    if (r.IsComplete) SetBanner(r.LastCommand == BenchCommand.Replace ? "REPLACE" : "RACK", r.IsGoodLift ? Gold : Bad);
                    else if (r.PressGiven) SetBanner("PRESS GIVEN", new Color(0.16f, 0.45f, 0.28f, 1f));
                    else if (r.StartGiven) SetBanner("START GIVEN", new Color(0.16f, 0.45f, 0.28f, 1f));
                    else SetBanner("AWAIT START", Idle);
                    break;
            }

            _hudInstruction.text = Instruction(phase, r, o);
            if (r.Faults.Count > 0)
            {
                BenchFaultRecord f = r.Faults[0];
                _hudFault.text = string.Format(C, "REFEREE NOTED   {0}   {1}", f.RuleReference, BenchReferee.Describe(f.Fault));
            }
            else
            {
                _hudFault.text = _bench.SpotterActive ? "SPOTTERS ON THE BAR" : string.Empty;
            }

            SetLamp("head", o.HeadInContact);
            SetLamp("shoulders", o.ShouldersInContact);
            SetLamp("buttocks", o.ButtocksInContact);
            SetLamp("lfoot", o.LeftFootPlanted);
            SetLamp("rfoot", o.RightFootPlanted);
            SetLamp("grip", o.GripLegal && o.LeftHandSlipM <= BenchRulebook.HandLateralSlipM && o.RightHandSlipM <= BenchRulebook.HandLateralSlipM);
            SetLamp("elbows", o.ElbowsLocked, Idle);
            SetLamp("still", !o.BarMoving, Idle);
            SetLamp("chest", o.BarOnChest, Idle);
            SetLamp("depth", o.BothElbowsAtDepth, Idle);

            BenchPlantModel m = _bench.Model;
            float y = _bench.Barbell.Body.position.y;
            _hudBarFill.fillAmount = Mathf.InverseLerp(m.BarTouch.y, m.BarLockout.y, y);
            _hudBarFill.color = o.BarOnChest ? Gold : Ok;
            _hudBarLabel.text = string.Format(C, "{0:F0} cm above touch", Mathf.Max(0f, y - m.BarTouch.y) * 100f);
            float pauseTicks = (float)(BenchRulebook.MotionlessWindowSeconds / Foundation.SimulationConstants.FixedDeltaTimeSeconds);
            _hudPauseFill.fillAmount = r.PressGiven ? 1f : Mathf.Clamp01(_chestStillTicks / pauseTicks);
        }

        private void SetBanner(string text, Color color)
        {
            _hudBanner.text = text;
            _hudBannerBg.color = color;
        }

        private void SetLamp(string id, bool ok, Color? offColor = null)
        {
            if (_lamps.TryGetValue(id, out Image lamp))
                lamp.color = ok ? Ok : offColor ?? Bad;
        }

        private static string PhaseLabel(BenchMotionPhase phase, BenchReferee r)
        {
            switch (phase)
            {
                case BenchMotionPhase.Racked: return "RACKED";
                case BenchMotionPhase.LiftingOff: return "LIFT-OFF";
                case BenchMotionPhase.Holding: return r.RackGiven ? "LOCKOUT · RACK GIVEN" : r.PressGiven ? "LOCKOUT" : "ARMS' LENGTH";
                case BenchMotionPhase.Lowering: return "DESCENT";
                case BenchMotionPhase.OnChest: return "ON THE CHEST";
                case BenchMotionPhase.Pressing: return "PRESSING";
                case BenchMotionPhase.Reracking: return "RERACKING";
                case BenchMotionPhase.Released: return "RACKED";
                case BenchMotionPhase.SpotterRescue: return "SPOTTERS";
                default: return phase.ToString().ToUpperInvariant();
            }
        }

        private static string Instruction(BenchMotionPhase phase, BenchReferee r, in BenchObservation o)
        {
            switch (phase)
            {
                case BenchMotionPhase.Racked: return "Press SPACE to lift the bar out of the rests.";
                case BenchMotionPhase.LiftingOff: return "Lifting off to straight arms…";
                case BenchMotionPhase.Reracking: return "Returning the bar to the rests…";
                case BenchMotionPhase.Released: return "Bar racked.";
                case BenchMotionPhase.SpotterRescue: return "The spotters are taking the bar back to the rests.";
            }
            if (r.IsComplete)
                return "Press SPACE to return the bar to the rests.";
            if (!r.StartGiven)
                return "Hold the bar MOTIONLESS on straight arms and wait for START.  (Lowering early is a fault.)";
            if (!r.PressGiven)
            {
                if (phase == BenchMotionPhase.OnChest || o.BarOnChest)
                    return "On the chest: keep the bar MOTIONLESS and wait for PRESS.  (Pressing early is a fault.)";
                return "Hold S / ↓ to lower the bar to your chest under control.";
            }
            if (phase == BenchMotionPhase.Holding)
                return "Elbows locked: hold it MOTIONLESS and wait for RACK.";
            return "Hold W / ↑ to press to locked elbows. Do not let the bar travel down.";
        }

        partial void BuildHud(RectTransform root)
        {
            RectTransform p = Rect(root, "BenchHud", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0f));
            p.GetComponent<Image>().raycastTarget = false;
            _liftPanel = p.gameObject;

            RectTransform top = Rect(p, "TopBar", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(-32f, 64f), Panel);
            _hudLoad = Label(top, "Load", string.Empty, Vector2.zero, new Vector2(480f, 50f), 24, FontStyle.Bold, Gold, TextAnchor.MiddleLeft, leftAnchor: true);
            _hudPhase = Label(top, "Phase", string.Empty, Vector2.zero, new Vector2(520f, 50f), 22, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            _hudCamera = Label(top, "CameraLabel", string.Empty, new Vector2(-24f, 0f), new Vector2(420f, 50f), 17, FontStyle.Normal, Muted, TextAnchor.MiddleRight, rightAnchor: true);

            RectTransform banner = Rect(p, "RefereeBanner", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(560f, 110f), Idle);
            _hudBannerBg = banner.GetComponent<Image>();
            Label(banner, "Who", "CHIEF REFEREE", new Vector2(0f, 36f), new Vector2(540f, 26f), 15, FontStyle.Bold, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleCenter);
            _hudBanner = Label(banner, "Signal", string.Empty, new Vector2(0f, -12f), new Vector2(540f, 70f), 54, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);

            RectTransform bottom = Rect(p, "Instruction", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(1260f, 132f), Panel);
            _hudInstruction = Label(bottom, "Text", string.Empty, new Vector2(0f, 26f), new Vector2(1220f, 56f), 25, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            _hudFault = Label(bottom, "Fault", string.Empty, new Vector2(0f, -12f), new Vector2(1220f, 28f), 18, FontStyle.Bold, Bad, TextAnchor.MiddleCenter);
            Label(bottom, "Keys", "SPACE lift off / rerack   ·   S / ↓ lower   ·   W / ↑ press   ·   ESC abort to the spotters   ·   C camera",
                new Vector2(0f, -46f), new Vector2(1220f, 26f), 15, FontStyle.Normal, Muted, TextAnchor.MiddleCenter);

            RectTransform checks = Rect(p, "RefereeChecks", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-20f, 40f), new Vector2(300f, 430f), Panel);
            Label(checks, "Header", "REFEREE CHECKS", new Vector2(0f, 190f), new Vector2(270f, 30f), 18, FontStyle.Bold, Gold, TextAnchor.MiddleCenter);
            string[,] items =
            {
                { "head", "Head on bench" }, { "shoulders", "Shoulders on bench" }, { "buttocks", "Buttocks on bench" },
                { "lfoot", "Left foot flat" }, { "rfoot", "Right foot flat" }, { "grip", "Grip held" },
                { "elbows", "Elbows locked" }, { "still", "Bar motionless" }, { "chest", "Bar on chest" }, { "depth", "Elbows at depth" }
            };
            for (int i = 0; i < items.GetLength(0); i++)
            {
                float y = 148f - i * 36f;
                RectTransform lamp = Rect(checks, "Lamp_" + items[i, 0], new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-118f, y), new Vector2(20f, 20f), Idle);
                _lamps[items[i, 0]] = lamp.GetComponent<Image>();
                Label(checks, "LampLabel_" + items[i, 0], items[i, 1], new Vector2(14f, y), new Vector2(220f, 30f), 17, FontStyle.Normal, Color.white, TextAnchor.MiddleLeft);
            }

            RectTransform gauge = Rect(p, "BarGauge", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(105f, 40f), new Vector2(170f, 430f), Panel);
            Label(gauge, "Header", "BAR PATH", new Vector2(0f, 190f), new Vector2(160f, 30f), 18, FontStyle.Bold, Gold, TextAnchor.MiddleCenter);
            Label(gauge, "Lockout", "LOCKOUT", new Vector2(-20f, 160f), new Vector2(120f, 22f), 14, FontStyle.Normal, Muted, TextAnchor.MiddleCenter);
            Label(gauge, "Chest", "CHEST", new Vector2(-20f, -160f), new Vector2(120f, 22f), 14, FontStyle.Normal, Muted, TextAnchor.MiddleCenter);
            _hudBarFill = Fill(gauge, "BarFill", new Vector2(-20f, 0f), new Vector2(40f, 290f), Ok);
            _hudPauseFill = Fill(gauge, "PauseFill", new Vector2(50f, 0f), new Vector2(22f, 290f), Gold);
            Label(gauge, "PauseLabel", "PAUSE", new Vector2(50f, -160f), new Vector2(70f, 22f), 13, FontStyle.Normal, Muted, TextAnchor.MiddleCenter);
            _hudBarLabel = Label(gauge, "BarLabel", string.Empty, new Vector2(0f, -192f), new Vector2(160f, 24f), 14, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter);
        }

        private Image Fill(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            RectTransform back = Rect(parent, name + "Back", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size, PanelLight);
            RectTransform fill = Rect(back, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, color);
            Image img = fill.GetComponent<Image>();
            img.sprite = Sprite.Create(Texture2D.whiteTexture, new UnityEngine.Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Vertical;
            img.fillOrigin = (int)Image.OriginVertical.Bottom;
            img.fillAmount = 0f;
            return img;
        }
    }
}
