using System;
using PowerliftingSimulator.Bench;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Bench.Unity
{
    /// <summary>
    /// Procedural referee cues for the bench signals. Each command has its own
    /// pitch contour so Start, Press and Rack are distinguishable by ear; the
    /// HUD shows the spoken word at the same moment.
    /// </summary>
    public sealed class BenchAudioCues : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private AudioSource _source;
        private PowerliftingSettingsManager _settings;
        private AudioClip _start, _press, _rack, _replace, _good, _noLift, _click, _clank;

        public string LastCue { get; private set; } = string.Empty;

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _settings = GetComponent<PowerliftingSettingsManager>();
            try
            {
                _start = Tone("BenchStart", 0.34f, t => Sweep(t, 0.34f, 880f, 523f));
                _press = Tone("BenchPress", 0.22f, t => Sine(t, 1046.5f) * Mathf.Exp(-t * 14f));
                _rack = Tone("BenchRack", 0.30f, t => Sine(t, t < 0.13f ? 659.25f : 493.88f) * Mathf.Exp(-(t % 0.13f) * 9f));
                _replace = Tone("BenchReplace", 0.40f, t => Sine(t, 293.66f) * Mathf.Exp(-t * 5f));
                _good = Tone("BenchGood", 0.55f, t => Sine(t, t < 0.14f ? 523.25f : t < 0.28f ? 659.25f : 783.99f) * Mathf.Exp(-Mathf.Max(0f, t - 0.12f) * 4.5f));
                _noLift = Tone("BenchNoLift", 0.52f, t => Mathf.Clamp(Sine(t, t < 0.24f ? 220f : 164.81f) * 1.3f, -0.8f, 0.8f) * Mathf.Exp(-t * 3.8f));
                _click = Tone("BenchClick", 0.05f, t => Sine(t, 820f) * Mathf.Exp(-t * 65f));
                _clank = Tone("BenchClank", 0.24f, t => (0.5f * Sine(t, 185f) + 0.3f * Sine(t, 422f) + 0.2f * Sine(t, 768f)) * Mathf.Exp(-t * 16f));
            }
            catch (Exception)
            {
                // -noaudio batch sessions may refuse AudioClip creation.
            }
        }

        public void PlayCommand(BenchCommand command)
        {
            switch (command)
            {
                case BenchCommand.Start: Play(_start, 0.85f, "REFEREE_START"); break;
                case BenchCommand.Press: Play(_press, 0.85f, "REFEREE_PRESS"); break;
                case BenchCommand.Rack: Play(_rack, 0.80f, "REFEREE_RACK"); break;
                case BenchCommand.Replace: Play(_replace, 0.80f, "REFEREE_REPLACE"); break;
            }
        }

        public void PlayVerdict(bool good) => Play(good ? _good : _noLift, 0.8f, good ? "GOOD_LIFT" : "NO_LIFT");

        public void PlayClick() => Play(_click, 0.45f, "UI_CLICK");

        public void PlayRackClank() => Play(_clank, 0.6f, "RACK_CLANK");

        private void Play(AudioClip clip, float scale, string cue)
        {
            LastCue = cue;
            if (clip == null || _source == null)
                return;
            float volume = _settings != null ? _settings.EffectiveVolume : AudioListener.volume;
            if (volume > 0.001f)
                _source.PlayOneShot(clip, Mathf.Clamp01(scale * volume));
        }

        private static float Sine(float t, float f) => Mathf.Sin(2f * Mathf.PI * f * t);

        private static float Sweep(float t, float duration, float from, float to)
        {
            float phase = 2f * Mathf.PI * (from * t + 0.5f * (to - from) / duration * t * t);
            return Mathf.Sin(phase) * Mathf.Exp(-t * 4f) * 0.8f;
        }

        private static AudioClip Tone(string name, float duration, Func<float, float> f)
        {
            int n = Mathf.Max(64, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[n];
            for (int i = 0; i < n; i++)
                data[i] = Mathf.Clamp(f(i / (float)SampleRate) * 0.6f, -1f, 1f);
            AudioClip clip = AudioClip.Create(name, n, 1, SampleRate, false);
            clip?.SetData(data, 0);
            return clip;
        }
    }
}
