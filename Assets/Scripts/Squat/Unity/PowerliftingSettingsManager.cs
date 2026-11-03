using System;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    public enum PowerliftingCameraAngle
    {
        ThreeQuarterCompetition = 0,
        SideDepthReferee = 1,
        FrontHeadReferee = 2
    }

    [DisallowMultipleComponent]
    public sealed class PowerliftingSettingsManager : MonoBehaviour
    {
        private const string PrefFullscreen = "PLSim.Settings.Fullscreen";
        private const string PrefResolutionIndex = "PLSim.Settings.ResolutionIndex";
        private const string PrefVSync = "PLSim.Settings.VSync";
        private const string PrefTargetFps = "PLSim.Settings.TargetFps";
        private const string PrefMsaa = "PLSim.Settings.Msaa";
        private const string PrefSoftShadows = "PLSim.Settings.SoftShadows";
        private const string PrefMasterVolume = "PLSim.Settings.MasterVolume";
        private const string PrefAudioMuted = "PLSim.Settings.AudioMuted";
        private const string PrefCameraAngle = "PLSim.Settings.CameraAngle";
        private const string PrefTelemetry = "PLSim.Settings.Telemetry";

        public static readonly Vector2Int[] SupportedResolutions =
        {
            new Vector2Int(1920, 1080),
            new Vector2Int(1600, 900),
            new Vector2Int(1280, 720)
        };

        [SerializeField] private bool isFullscreen;
        [SerializeField] private int resolutionIndex;
        [SerializeField] private bool vSyncEnabled = true;
        [SerializeField] private int targetFrameRate = 120;
        [SerializeField] private int antiAliasingSamples = 4;
        [SerializeField] private bool softShadowsEnabled = true;
        [SerializeField] private float masterVolume = 0.8f;
        [SerializeField] private bool audioMuted;
        [SerializeField] private PowerliftingCameraAngle preferredCameraAngle = PowerliftingCameraAngle.ThreeQuarterCompetition;
        [SerializeField] private bool developerTelemetryEnabled;

        public bool IsFullscreen => isFullscreen;
        public int ResolutionIndex => resolutionIndex;
        public Vector2Int CurrentResolutionPreset => SupportedResolutions[Mathf.Clamp(resolutionIndex, 0, SupportedResolutions.Length - 1)];
        public bool VSyncEnabled => vSyncEnabled;
        public int TargetFrameRate => targetFrameRate;
        public int AntiAliasingSamples => antiAliasingSamples;
        public bool SoftShadowsEnabled => softShadowsEnabled;
        public float MasterVolume => masterVolume;
        public bool AudioMuted => audioMuted;
        public float EffectiveVolume => audioMuted ? 0f : Mathf.Clamp01(masterVolume);
        public PowerliftingCameraAngle PreferredCameraAngle => preferredCameraAngle;
        public bool DeveloperTelemetryEnabled => developerTelemetryEnabled;

        public event Action SettingsChanged;

        private void Awake()
        {
            LoadFromPrefs();
            ApplySettings();
        }

        public void LoadFromPrefs()
        {
            isFullscreen = PlayerPrefs.GetInt(PrefFullscreen, 0) != 0;
            resolutionIndex = Mathf.Clamp(PlayerPrefs.GetInt(PrefResolutionIndex, 0), 0, SupportedResolutions.Length - 1);
            vSyncEnabled = PlayerPrefs.GetInt(PrefVSync, 1) != 0;
            targetFrameRate = PlayerPrefs.GetInt(PrefTargetFps, 120);
            antiAliasingSamples = PlayerPrefs.GetInt(PrefMsaa, 4);
            softShadowsEnabled = PlayerPrefs.GetInt(PrefSoftShadows, 1) != 0;
            masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefMasterVolume, 0.8f));
            audioMuted = PlayerPrefs.GetInt(PrefAudioMuted, 0) != 0;
            preferredCameraAngle = (PowerliftingCameraAngle)Mathf.Clamp(
                PlayerPrefs.GetInt(PrefCameraAngle, (int)PowerliftingCameraAngle.ThreeQuarterCompetition),
                0,
                2);
            developerTelemetryEnabled = PlayerPrefs.GetInt(PrefTelemetry, 0) != 0;
        }

        public void SaveToPrefs()
        {
            PlayerPrefs.SetInt(PrefFullscreen, isFullscreen ? 1 : 0);
            PlayerPrefs.SetInt(PrefResolutionIndex, resolutionIndex);
            PlayerPrefs.SetInt(PrefVSync, vSyncEnabled ? 1 : 0);
            PlayerPrefs.SetInt(PrefTargetFps, targetFrameRate);
            PlayerPrefs.SetInt(PrefMsaa, antiAliasingSamples);
            PlayerPrefs.SetInt(PrefSoftShadows, softShadowsEnabled ? 1 : 0);
            PlayerPrefs.SetFloat(PrefMasterVolume, masterVolume);
            PlayerPrefs.SetInt(PrefAudioMuted, audioMuted ? 1 : 0);
            PlayerPrefs.SetInt(PrefCameraAngle, (int)preferredCameraAngle);
            PlayerPrefs.SetInt(PrefTelemetry, developerTelemetryEnabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void SetFullscreen(bool fullscreen)
        {
            isFullscreen = fullscreen;
            ApplyAndSave();
        }

        public void CycleResolutionPreset()
        {
            resolutionIndex = (resolutionIndex + 1) % SupportedResolutions.Length;
            ApplyAndSave();
        }

        public void SetResolutionPreset(int index)
        {
            resolutionIndex = Mathf.Clamp(index, 0, SupportedResolutions.Length - 1);
            ApplyAndSave();
        }

        public void SetVSync(bool enabled)
        {
            vSyncEnabled = enabled;
            ApplyAndSave();
        }

        public void CycleQualityShadowMode()
        {
            softShadowsEnabled = !softShadowsEnabled;
            ApplyAndSave();
        }

        public void SetSoftShadows(bool enabled)
        {
            softShadowsEnabled = enabled;
            ApplyAndSave();
        }

        public void CycleAntiAliasing()
        {
            antiAliasingSamples = antiAliasingSamples == 0 ? 2 : antiAliasingSamples == 2 ? 4 : 0;
            ApplyAndSave();
        }

        public void SetMasterVolume(float volume)
        {
            masterVolume = Mathf.Clamp01(volume);
            if (masterVolume > 0.001f && audioMuted)
                audioMuted = false;
            ApplyAndSave();
        }

        public void AdjustMasterVolume(float delta)
        {
            SetMasterVolume(Mathf.Clamp01(masterVolume + delta));
        }

        public void SetAudioMuted(bool muted)
        {
            audioMuted = muted;
            ApplyAndSave();
        }

        public void SetPreferredCameraAngle(PowerliftingCameraAngle angle)
        {
            preferredCameraAngle = angle;
            ApplyAndSave();
        }

        public void CyclePreferredCameraAngle()
        {
            int next = ((int)preferredCameraAngle + 1) % 3;
            SetPreferredCameraAngle((PowerliftingCameraAngle)next);
        }

        public void SetDeveloperTelemetry(bool enabled)
        {
            developerTelemetryEnabled = enabled;
            ApplyAndSave();
        }

        public void ResetToDefaults()
        {
            isFullscreen = false;
            resolutionIndex = 0;
            vSyncEnabled = true;
            targetFrameRate = 120;
            antiAliasingSamples = 4;
            softShadowsEnabled = true;
            masterVolume = 0.8f;
            audioMuted = false;
            preferredCameraAngle = PowerliftingCameraAngle.ThreeQuarterCompetition;
            developerTelemetryEnabled = false;
            ApplyAndSave();
        }

        public void ApplySettings()
        {
            AudioListener.volume = EffectiveVolume;
            if (!Application.isBatchMode)
            {
                QualitySettings.vSyncCount = vSyncEnabled ? 1 : 0;
                Application.targetFrameRate = vSyncEnabled ? -1 : targetFrameRate;
                QualitySettings.antiAliasing = antiAliasingSamples;
            }

            if (!Application.isBatchMode && !Application.isEditor)
            {
                Vector2Int res = CurrentResolutionPreset;
                FullScreenMode mode = isFullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                Screen.SetResolution(res.x, res.y, mode);
            }

            Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (Light lightSource in lights)
            {
                if (lightSource != null && lightSource.type == LightType.Directional && lightSource.shadows != LightShadows.None)
                    lightSource.shadows = softShadowsEnabled ? LightShadows.Soft : LightShadows.Hard;
            }

            SquatPhysicalPrototypeController controller = FindFirstObjectByType<SquatPhysicalPrototypeController>();
            if (controller != null)
                controller.ShowDebugGui = developerTelemetryEnabled;

            SettingsChanged?.Invoke();
        }

        private void ApplyAndSave()
        {
            ApplySettings();
            SaveToPrefs();
        }
    }
}
