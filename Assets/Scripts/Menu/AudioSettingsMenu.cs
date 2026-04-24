using UnityEngine;
using UnityEngine.UI;

public class AudioSettingsMenu : MonoBehaviour
{
    private const string FovPref = "Settings.FOV";
    private const string SensitivityPref = "Settings.Sensitivity";
    private const string MasterVolumePref = "Audio.Master";

    [Header("Settings Sliders")]
    [SerializeField] private Slider fovSlider;
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private Slider volumeSlider;

    private bool isBinding;
    private FirstPersonController playerController;
    private Coroutine delayedSyncRoutine;

    private void Awake()
    {
        AutoResolveReferences();
        BindSliderEvents();
        playerController = FindAnyObjectByType<FirstPersonController>();
    }

    private void OnEnable()
    {
        SyncFromAudioManager();
        delayedSyncRoutine = StartCoroutine(DelayedSync());
    }

    private void OnDestroy()
    {
        StopDelayedSyncIfRunning();
        UnbindSliderEvents();
    }

    private void OnDisable()
    {
        StopDelayedSyncIfRunning();
    }

    public void SyncFromAudioManager()
    {
        isBinding = true;
        if (fovSlider != null)
        {
            float fov = ResolveCurrentFov();
            fovSlider.minValue = 45f;
            fovSlider.maxValue = 100f;
            fovSlider.wholeNumbers = false;
            fovSlider.SetValueWithoutNotify(fov);
        }

        if (sensitivitySlider != null)
        {
            float sensitivity = ResolveCurrentSensitivity();
            sensitivitySlider.minValue = 0.1f;
            sensitivitySlider.maxValue = 10f;
            sensitivitySlider.wholeNumbers = false;
            sensitivitySlider.SetValueWithoutNotify(sensitivity);
        }

        if (volumeSlider != null)
        {
            float defaultVolume = GameAudioManager.Instance != null ? GameAudioManager.Instance.MasterVolume : 1f;
            float volume = PlayerPrefs.GetFloat(MasterVolumePref, defaultVolume);
            volumeSlider.minValue = 0f;
            volumeSlider.maxValue = 100f;
            volumeSlider.wholeNumbers = false;
            volumeSlider.SetValueWithoutNotify(volume * 100f);
        }

        isBinding = false;
    }

    private void HandleFovChanged(float value)
    {
        if (isBinding)
        {
            return;
        }

        EnsurePlayerController();
        playerController?.ApplySettingsFov(value);
        PlayerPrefs.SetFloat(FovPref, value);
        PlayerPrefs.Save();
    }

    private void HandleSensitivityChanged(float value)
    {
        if (isBinding)
        {
            return;
        }

        EnsurePlayerController();
        playerController?.ApplySettingsSensitivity(value);
        PlayerPrefs.SetFloat(SensitivityPref, value);
        PlayerPrefs.Save();
    }

    private void HandleVolumeChanged(float value)
    {
        if (isBinding)
        {
            return;
        }

        GameAudioManager.Instance?.SetMasterVolume(Mathf.Clamp01(value / 100f), save: true);
    }

    private void BindSliderEvents()
    {
        if (fovSlider != null)
        {
            fovSlider.onValueChanged.AddListener(HandleFovChanged);
        }

        if (sensitivitySlider != null)
        {
            sensitivitySlider.onValueChanged.AddListener(HandleSensitivityChanged);
        }

        if (volumeSlider != null)
        {
            volumeSlider.onValueChanged.AddListener(HandleVolumeChanged);
        }
    }

    private void UnbindSliderEvents()
    {
        if (fovSlider != null)
        {
            fovSlider.onValueChanged.RemoveListener(HandleFovChanged);
        }

        if (sensitivitySlider != null)
        {
            sensitivitySlider.onValueChanged.RemoveListener(HandleSensitivityChanged);
        }

        if (volumeSlider != null)
        {
            volumeSlider.onValueChanged.RemoveListener(HandleVolumeChanged);
        }
    }

    private void AutoResolveReferences()
    {
        if (fovSlider == null)
        {
            fovSlider = FindSliderByName("FOV");
        }

        if (sensitivitySlider == null)
        {
            sensitivitySlider = FindSliderByName("Sensitivity");
        }

        if (volumeSlider == null)
        {
            volumeSlider = FindSliderByName("Volume");
        }
    }

    private Slider FindSliderByName(string objectName)
    {
        Slider[] sliders = GetComponentsInChildren<Slider>(true);
        for (int i = 0; i < sliders.Length; i++)
        {
            if (sliders[i] != null && sliders[i].name == objectName)
            {
                return sliders[i];
            }
        }
        return null;
    }

    private void EnsurePlayerController()
    {
        if (playerController == null)
        {
            playerController = FindAnyObjectByType<FirstPersonController>();
        }
    }

    private float ResolveCurrentFov()
    {
        EnsurePlayerController();
        return PlayerPrefs.GetFloat(FovPref, playerController != null ? playerController.fov : 60f);
    }

    private float ResolveCurrentSensitivity()
    {
        EnsurePlayerController();
        return PlayerPrefs.GetFloat(SensitivityPref, playerController != null ? playerController.mouseSensitivity : 2f);
    }

    private System.Collections.IEnumerator DelayedSync()
    {
        // Run one frame later so runtime singletons/controllers can finish Awake.
        yield return null;
        SyncFromAudioManager();
    }

    private void StopDelayedSyncIfRunning()
    {
        if (delayedSyncRoutine != null)
        {
            StopCoroutine(delayedSyncRoutine);
            delayedSyncRoutine = null;
        }
    }
}
