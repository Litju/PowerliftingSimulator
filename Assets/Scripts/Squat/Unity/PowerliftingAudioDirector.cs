using System;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    [DisallowMultipleComponent]
    public sealed class PowerliftingAudioDirector : MonoBehaviour
    {
        private const int SampleRate = 44100;

        [SerializeField] private SquatPhysicalPrototypeController squatController;
        [SerializeField] private PowerliftingSettingsManager settingsManager;

        private AudioSource _sfxSource;
        private static AudioClip _uiClickClip;
        private static AudioClip _plateClankClip;
        private static AudioClip _footstepClip;
        private static AudioClip _squatCommandClip;
        private static AudioClip _depthReachedClip;
        private static AudioClip _rackCommandClip;
        private static AudioClip _goodLiftClip;
        private static AudioClip _noLiftClip;

        private SquatPlayerGamePhase _lastGamePhase = SquatPlayerGamePhase.LOAD;
        private SquatState _lastSquatState = SquatState.SETUP;
        private bool _lastHadSquatCommand;
        private bool _depthCuePlayedThisAttempt;
        private bool _verdictCuePlayedThisAttempt;
        private float _lastLoadKg = -1f;
        private string _lastPlayedCueName = "NONE";

        public string LastPlayedCueName => _lastPlayedCueName;

        private void Awake()
        {
            if (squatController == null)
                squatController = FindFirstObjectByType<SquatPhysicalPrototypeController>();
            if (settingsManager == null)
                settingsManager = FindFirstObjectByType<PowerliftingSettingsManager>();

            if (FindFirstObjectByType<AudioListener>() == null)
            {
                Camera mainCam = Camera.main ?? FindFirstObjectByType<Camera>();
                if (mainCam != null)
                    mainCam.gameObject.AddComponent<AudioListener>();
                else
                    gameObject.AddComponent<AudioListener>();
            }

            _sfxSource = GetComponent<AudioSource>();
            if (_sfxSource == null)
                _sfxSource = gameObject.AddComponent<AudioSource>();

            _sfxSource.playOnAwake = false;
            _sfxSource.spatialBlend = 0f;
            _sfxSource.volume = 0.85f;

            BuildProceduralClips();
        }

        private void Update()
        {
            if (squatController == null)
                squatController = FindFirstObjectByType<SquatPhysicalPrototypeController>();
            if (squatController == null || !squatController.IsInitialized)
                return;

            if (_lastLoadKg < 0f)
                _lastLoadKg = squatController.CurrentLoadKg;
            else if (!Mathf.Approximately(_lastLoadKg, squatController.CurrentLoadKg))
            {
                _lastLoadKg = squatController.CurrentLoadKg;
                PlayPlateLoad();
            }

            SquatPlayerGamePhase phase = squatController.PlayerGamePhase;
            if (phase != _lastGamePhase)
            {
                switch (phase)
                {
                    case SquatPlayerGamePhase.SETUP:
                        _depthCuePlayedThisAttempt = false;
                        _verdictCuePlayedThisAttempt = false;
                        PlayUiClick();
                        break;
                    case SquatPlayerGamePhase.UNRACK:
                        PlayPlateLoad();
                        break;
                    case SquatPlayerGamePhase.WALKOUT_STEP_1:
                    case SquatPlayerGamePhase.WALKOUT_STEP_2:
                        PlayFootstep();
                        break;
                    case SquatPlayerGamePhase.RERACK:
                        PlayPlateLoad();
                        break;
                }
                _lastGamePhase = phase;
            }

            SquatAttemptOrchestrator orchestrator = squatController.AttemptOrchestrator;
            bool hasSquatCommand = orchestrator != null && orchestrator.HasSquatCommand;
            if (hasSquatCommand && !_lastHadSquatCommand)
            {
                _depthCuePlayedThisAttempt = false;
                _verdictCuePlayedThisAttempt = false;
                PlaySquatCommand();
            }
            _lastHadSquatCommand = hasSquatCommand;

            if (squatController.Adapter != null)
            {
                SquatState state = squatController.Adapter.State;
                if (state != _lastSquatState)
                {
                    if ((state == SquatState.BOTTOM || state == SquatState.REVERSAL) && !_depthCuePlayedThisAttempt)
                    {
                        _depthCuePlayedThisAttempt = true;
                        PlayDepthChime();
                    }
                    else if (state == SquatState.LOCKOUT || state == SquatState.RACK_COMMAND)
                    {
                        PlayRackCommand();
                    }
                    _lastSquatState = state;
                }
            }

            SquatAttemptRecord record = squatController.AttemptRecord;
            if (record != null && !_verdictCuePlayedThisAttempt)
            {
                _verdictCuePlayedThisAttempt = true;
                bool isGoodLift = record.TerminalReason == SquatAttemptTerminalReason.PHYSICAL_LOCKOUT &&
                    record.RuleOutcome == SquatJudgmentOutcome.GOOD_LIFT &&
                    record.PhysicalFailureOutcome != SquatFailureResultKind.PHYSICAL_FAILURE;
                PlayVerdictCue(isGoodLift);
            }
            else if (record == null)
            {
                _verdictCuePlayedThisAttempt = false;
            }
        }

        public void PlayUiClick() => PlayClip(_uiClickClip, 0.45f, "UI_CLICK");

        public void PlayPlateLoad() => PlayClip(_plateClankClip, 0.70f, "PLATE_CLANK");

        public void PlayFootstep() => PlayClip(_footstepClip, 0.55f, "WALKOUT_STEP");

        public void PlaySquatCommand() => PlayClip(_squatCommandClip, 0.80f, "REFEREE_SQUAT");

        public void PlayDepthChime() => PlayClip(_depthReachedClip, 0.55f, "DEPTH_CHIME");

        public void PlayRackCommand() => PlayClip(_rackCommandClip, 0.75f, "REFEREE_RACK");

        public void PlayVerdictCue(bool goodLift) =>
            PlayClip(goodLift ? _goodLiftClip : _noLiftClip, 0.85f, goodLift ? "GOOD_LIFT" : "NO_LIFT");

        private void PlayClip(AudioClip clip, float scale, string cueName)
        {
            _lastPlayedCueName = cueName;
            if (clip == null || _sfxSource == null)
                return;

            float volume = settingsManager != null ? settingsManager.EffectiveVolume : AudioListener.volume;
            if (volume <= 0.001f)
                return;

            _sfxSource.PlayOneShot(clip, Mathf.Clamp01(scale * volume));
        }

        private void BuildProceduralClips()
        {
            if (_uiClickClip != null)
                return;

            try
            {
                _uiClickClip = CreateToneClip("UiClick", 0.055f, t =>
                {
                    float env = Mathf.Exp(-t * 65f);
                    return Mathf.Sin(2f * Mathf.PI * 820f * t) * env * 0.45f;
                });

                _plateClankClip = CreateToneClip("PlateClank", 0.24f, t =>
                {
                    float env = Mathf.Exp(-t * 16f);
                    float metallic =
                        0.45f * Mathf.Sin(2f * Mathf.PI * 185f * t) +
                        0.30f * Mathf.Sin(2f * Mathf.PI * 422f * t) +
                        0.18f * Mathf.Sin(2f * Mathf.PI * 768f * t) +
                        0.07f * Mathf.Sin(2f * Mathf.PI * 1310f * t);
                    return metallic * env * 0.6f;
                });

                _footstepClip = CreateToneClip("Footstep", 0.14f, t =>
                {
                    float env = Mathf.Exp(-t * 28f);
                    return (0.7f * Mathf.Sin(2f * Mathf.PI * 92f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 160f * t)) * env * 0.55f;
                });

                _squatCommandClip = CreateToneClip("RefereeSquat", 0.28f, t =>
                {
                    float freq = t < 0.11f ? 587.33f : 880f;
                    float localT = t < 0.11f ? t : t - 0.11f;
                    float env = Mathf.Exp(-localT * 11f);
                    return (0.75f * Mathf.Sin(2f * Mathf.PI * freq * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * freq * 2f * t)) * env * 0.55f;
                });

                _depthReachedClip = CreateToneClip("DepthChime", 0.16f, t =>
                {
                    float env = Mathf.Exp(-t * 18f);
                    return Mathf.Sin(2f * Mathf.PI * 1046.5f * t) * env * 0.45f;
                });

                _rackCommandClip = CreateToneClip("RefereeRack", 0.25f, t =>
                {
                    float env = Mathf.Exp(-t * 10f);
                    return (0.7f * Mathf.Sin(2f * Mathf.PI * 659.25f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 987.77f * t)) * env * 0.55f;
                });

                _goodLiftClip = CreateToneClip("GoodLiftVerdict", 0.55f, t =>
                {
                    float freq = t < 0.14f ? 523.25f : t < 0.28f ? 659.25f : 783.99f;
                    float env = Mathf.Exp(-Mathf.Max(0f, t - 0.12f) * 4.5f);
                    return (0.7f * Mathf.Sin(2f * Mathf.PI * freq * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * freq * 1.5f * t)) * env * 0.55f;
                });

                _noLiftClip = CreateToneClip("NoLiftVerdict", 0.52f, t =>
                {
                    float freq = t < 0.24f ? 220f : 164.81f;
                    float env = Mathf.Exp(-t * 3.8f);
                    float raw = Mathf.Sin(2f * Mathf.PI * freq * t) + 0.45f * Mathf.Sin(2f * Mathf.PI * freq * 2f * t);
                    return Mathf.Clamp(raw, -0.8f, 0.8f) * env * 0.50f;
                });
            }
            catch (Exception)
            {
                // Safe fallback when running in -noaudio batch environments where AudioClip creation may be disabled.
            }
        }

        private static AudioClip CreateToneClip(string name, float durationSeconds, Func<float, float> generator)
        {
            int sampleCount = Mathf.Max(64, Mathf.RoundToInt(durationSeconds * SampleRate));
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / SampleRate;
                samples[i] = Mathf.Clamp(generator(t), -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(name, sampleCount, 1, SampleRate, false);
            if (clip != null)
                clip.SetData(samples, 0);
            return clip;
        }
    }
}
