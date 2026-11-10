using System.Collections.Generic;
using System.Text;
using PowerliftingSimulator.Bench;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PowerliftingSimulator.Bench.Unity
{
    /// <summary>
    /// The attempt result: GOOD LIFT / NO LIFT exactly as the referee recorded
    /// it, each fault with its rule and its measured cause, the attempt's
    /// measured numbers, then retry or return to the gym.
    ///
    /// The result appears once the bar is back in the rests (by the lifter or
    /// the spotters). If the lifter never reracks, it appears anyway a fixed
    /// time after the referee's final signal.
    /// </summary>
    public sealed partial class BenchGameUiController
    {
        private const float ResultFallbackSeconds = 15f;

        private Image[] _resultLamps;
        private Text _resultHeadline, _resultReasons, _resultMetrics;
        private Image _resultBannerBg;
        private float _refereeCompleteAt = -1f;
        private const float VerdictAfterResultSeconds = 0.6f;
        private bool _clankPlayed, _verdictPlayed;
        private float _verdictDueAt = -1f;

        public string DisplayedResultHeadline => _resultHeadline != null ? _resultHeadline.text : string.Empty;
        public string DisplayedResultReasons => _resultReasons != null ? _resultReasons.text : string.Empty;
        public string DisplayedResultMetrics => _resultMetrics != null ? _resultMetrics.text : string.Empty;
        public bool RetryRequested { get; private set; }

        /// <summary>A fresh attempt at the same load: reloads the bench scene (new plant, referee and record).</summary>
        public void Retry() => Retry("api");

        private void Retry(string source)
        {
            Resolve();
            _audio.PlayClick();
            RetryRequested = true;
            Debug.Log($"GAM69_UI retry source={source}");
            if (_bench != null)
                _bench.ReloadWithLoad(_bench.LoadKg);
        }

        partial void HandleResultHotkeys(Keyboard keyboard)
        {
            if (Screen != BenchScreen.Result)
                return;
            if (keyboard.rKey.wasPressedThisFrame)
                Retry("key-R");
            else if (keyboard.gKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame)
                ReturnToGym();
        }

        partial void ResultDue(ref bool due)
        {
            if (_bench.Referee.IsComplete && _refereeCompleteAt < 0f)
                _refereeCompleteAt = Time.unscaledTime;
            if (_bench.AttemptFinished && !_clankPlayed)
            {
                _clankPlayed = true;
                _audio.PlayRackClank();
            }
            due = _bench.AttemptFinished ||
                (_refereeCompleteAt >= 0f && Time.unscaledTime - _refereeCompleteAt > ResultFallbackSeconds);
        }

        partial void OnResultShown()
        {
            FillResult();
            // The verdict sounds a moment after the rack clank, not over it.
            if (_verdictDueAt < 0f)
                _verdictDueAt = Time.unscaledTime + VerdictAfterResultSeconds;
        }

        partial void RefreshResult()
        {
            if (string.IsNullOrEmpty(_resultHeadline.text))
                FillResult();
            if (!_verdictPlayed && _verdictDueAt >= 0f && Time.unscaledTime >= _verdictDueAt)
            {
                _verdictPlayed = true;
                _audio.PlayVerdict(_bench.Referee.IsGoodLift);
                Debug.Log($"GAM69_UI verdict={(_bench.Referee.IsGoodLift ? "GOOD_LIFT" : "NO_LIFT")} faults={_bench.Referee.Faults.Count}");
                foreach (BenchFaultRecord f in _bench.Referee.Faults)
                    Debug.Log($"GAM69_UI fault {f.RuleReference} {f.Fault} tick={f.Tick} {f.Detail}");
            }
        }

        private void FillResult()
        {
            if (_bench == null || !_bench.IsInitialized)
                return;
            BenchReferee r = _bench.Referee;
            bool good = r.IsGoodLift;
            _resultHeadline.text = good ? "GOOD LIFT" : "NO LIFT";
            _resultBannerBg.color = good ? new Color(0.93f, 0.93f, 0.93f, 1f) : Bad;
            _resultHeadline.color = good ? new Color(0.08f, 0.08f, 0.10f, 1f) : Color.white;
            foreach (Image lamp in _resultLamps)
                lamp.color = good ? Color.white : Bad;

            var reasons = new StringBuilder();
            if (good)
            {
                reasons.AppendLine("START → PRESS → RACK, each given and obeyed.");
                reasons.AppendLine("Head, shoulders and buttocks stayed on the bench; feet flat on the platform; grip held.");
                reasons.Append("Bar touched the chest, was held motionless, and was pressed to locked elbows.");
            }
            else
            {
                var seen = new HashSet<BenchFault>();
                foreach (BenchFaultRecord f in r.Faults)
                {
                    if (seen.Add(f.Fault))
                        reasons.AppendLine(string.Format(C, "{0}   {1}   ({2})", f.RuleReference, BenchReferee.Describe(f.Fault), f.Detail));
                }
                if (r.Faults.Count == 0)
                    reasons.Append(r.IsComplete ? "Elbows were not locked out before the attempt ended." : "The attempt was not completed.");
            }
            _resultReasons.text = reasons.ToString().TrimEnd();
            _resultMetrics.text = Metrics(r);
        }

        private string Metrics(BenchReferee r)
        {
            ulong start = 0, press = 0, rack = 0;
            foreach (BenchCommandRecord c in r.Commands)
            {
                if (c.Command == BenchCommand.Start) start = c.Tick;
                else if (c.Command == BenchCommand.Press) press = c.Tick;
                else if (c.Command == BenchCommand.Rack) rack = c.Tick;
            }
            float startY = float.NaN, minY = float.PositiveInfinity, peakChest = 0f, peakUp = 0f;
            ulong touch = 0;
            foreach (BenchTelemetrySample s in _bench.Telemetry.Samples)
            {
                if (s.Tick == start) startY = s.BarPosition.y;
                if (start == 0 || s.Tick < start || (rack != 0 && s.Tick > rack))
                    continue;
                minY = Mathf.Min(minY, s.BarPosition.y);
                peakChest = Mathf.Max(peakChest, s.BarChestN);
                if (press != 0 && s.Tick > press)
                    peakUp = Mathf.Max(peakUp, s.BarVelocity.y);
                if (touch == 0 && s.BarChestN >= BenchRulebook.ChestTouchForceN)
                    touch = s.Tick;
            }
            double dt = Foundation.SimulationConstants.FixedDeltaTimeSeconds;
            var b = new StringBuilder();
            b.AppendLine(string.Format(C, "Load {0:F1} kg  ·  dynamic bar, physically pressed, judged on IPF 2026 bench rules", _bench.LoadKg));
            if (start != 0 && !float.IsNaN(startY) && !float.IsInfinity(minY))
                b.AppendLine(string.Format(C, "Bar travel {0:F1} cm", (startY - minY) * 100f));
            if (touch != 0 && press != 0)
                b.AppendLine(string.Format(C, "Chest pause {0:F2} s  ·  peak chest contact {1:F0} N", (press - touch) * dt, peakChest));
            if (press != 0 && rack != 0)
                b.AppendLine(string.Format(C, "PRESS to RACK {0:F2} s  ·  peak bar speed {1:F2} m/s", (rack - press) * dt, peakUp));
            b.Append("Signals: ");
            foreach (BenchCommandRecord c in r.Commands)
                b.Append(string.Format(C, "{0} at {1:F2} s   ", c.Command.ToString().ToUpperInvariant(), c.TimeSeconds));
            return b.ToString().TrimEnd();
        }

        partial void BuildResult(RectTransform root)
        {
            RectTransform p = Rect(root, "BenchResult", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180f, 720f), Panel);
            _resultPanel = p.gameObject;
            RectTransform banner = Rect(p, "Verdict", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 270f), new Vector2(1100f, 120f), Idle);
            _resultBannerBg = banner.GetComponent<Image>();
            _resultHeadline = Label(banner, "Headline", string.Empty, Vector2.zero, new Vector2(1080f, 110f), 72, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            _resultLamps = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                RectTransform lamp = Rect(p, "Lamp" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-130f + i * 130f, 155f), new Vector2(84f, 84f), Idle);
                _resultLamps[i] = lamp.GetComponent<Image>();
            }
            Label(p, "LampsCaption", "chief referee decision", new Vector2(0f, 98f), new Vector2(600f, 24f), 15, FontStyle.Italic, Muted, TextAnchor.MiddleCenter);
            _resultReasons = Label(p, "Reasons", string.Empty, new Vector2(0f, 10f), new Vector2(1080f, 140f), 20, FontStyle.Bold, Color.white, TextAnchor.UpperCenter);
            _resultMetrics = Label(p, "Metrics", string.Empty, new Vector2(0f, -150f), new Vector2(1080f, 150f), 17, FontStyle.Normal, Muted, TextAnchor.UpperCenter);
            MakeButton(p, "Retry", "RETRY  [R]", new Vector2(-220f, -300f), new Vector2(360f, 76f), Red, 24, () => Retry("button"));
            MakeButton(p, "Gym", "RETURN TO GYM  [G]", new Vector2(220f, -300f), new Vector2(360f, 76f), Slate, 22, ReturnToGym);
        }
    }
}
