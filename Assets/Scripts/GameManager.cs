using System.Collections.Generic;
using System.Collections;
using UnityEngine;

/// <summary>
/// Data structure to hold information about a specific building/level.
/// </summary>
[System.Serializable]
public class LevelData
{
    [Tooltip("The location where the player should spawn in this specific map.")]
    public Transform playerSpawnPoint;

    [Tooltip("Different fire setups/scenarios available for this map.")]
    public GameObject[] fireScenarios;

    [Tooltip("Doors that should be hot to the touch in this level.")]
    public DoorController[] hotDoors;

    [Tooltip("Target time to complete the level (in seconds).")]
    public float targetTimeSeconds = 120f;

    [Tooltip("Maximum allowed smoke damage for a Successful Evacuation.")]
    public float maxSmokeDamageAllowed = 20f;

    [Tooltip("Maximum allowed fire damage for a Successful Evacuation.")]
    public float maxFireDamageAllowed = 5f;

    [Tooltip("Minimum number of doors the player MUST close (out of those opened).")]
    public int minDoorsClosedRequired = 1;

    [Tooltip("Minimum number of doors the player MUST heat-check (out of those opened).")]
    public int minDoorsCheckedRequired = 1;

    [Tooltip("Parent GameObjects that contain the box setup for this level.")]
    public GameObject[] boxGroupRoots;
}

/// <summary>
/// Manages the initialization of levels, spawning maps, fires, and placing the player.
/// </summary>
public class GameManager : MonoBehaviour
{
    [Header("Level Configuration")]
    [Tooltip("List of all available building maps and their scenarios.")]
    public LevelData[] levels;

    [Header("Player Reference")]
    [Tooltip("The player GameObject to be teleported.")]
    public GameObject player;

    [Header("Emergency Audio/Visual Timing")]
    [Tooltip("Delay in seconds before emergency siren starts (both vignette pulse and alarm audio).")]
    [SerializeField] private float fireAlarmStartDelay = 0f;

    private void Start()
    {
        // 1. Get current level index from PlayerPrefs
        int selectedLevel = PlayerPrefs.GetInt("SelectedLevel", 0);

        // 2. Error Check Level Index
        if (levels == null || levels.Length == 0)
        {
            Debug.LogError("GameManager: No levels configured in the Inspector!");
            return;
        }

        if (selectedLevel < 0 || selectedLevel >= levels.Length)
        {
            Debug.LogWarning($"GameManager: SelectedLevel index {selectedLevel} is out of bounds. Defaulting to 0.");
            selectedLevel = 0;
        }

        LevelData currentLevel = levels[selectedLevel];

        ConfigureLevelBoxGroups(selectedLevel);
        TeleportPlayerToSpawn(currentLevel);

        // 5. Deactivate all existing fires/smoke in the scene first
        // It is highly recommended to tag all your fire-related GameObjects with the "Fire" tag.
        GameObject[] taggedFires = GameObject.FindGameObjectsWithTag("Fire");
        foreach (GameObject fire in taggedFires)
        {
            fire.SetActive(false);
        }

        // Also specifically deactivate any SmokeSimulator instances found in the scene
        SmokeSimulator[] existingSims = Object.FindObjectsOfType<SmokeSimulator>();
        foreach (SmokeSimulator sim in existingSims)
        {
            sim.gameObject.SetActive(false);
        }

        // 6. Activate all Fires defined for this specific level
        if (currentLevel.fireScenarios != null && currentLevel.fireScenarios.Length > 0)
        {
            foreach (GameObject fire in currentLevel.fireScenarios)
            {
                if (fire != null)
                {
                    fire.SetActive(true);
                }
            }
        }
        else
        {
            Debug.LogWarning($"GameManager: No fire scenarios defined for level {selectedLevel}.");
        }

        // 7. Handle Hot Doors
        if (currentLevel.hotDoors != null && currentLevel.hotDoors.Length > 0)
        {
            foreach (DoorController door in currentLevel.hotDoors)
            {
                Debug.Log("Door is hot", door);
                if (door != null)
                {
                    Debug.Log("Door is indeed hot", door);
                    door.isHot = true;
                }
            }
        }

        // GameManager controls siren timing so both visual pulse and alarm audio start together.
        StartCoroutine(StartEmergencySirenWhenReady());
    }

    private void ConfigureLevelBoxGroups(int selectedLevel)
    {
        HashSet<GameObject> allConfiguredGroups = new HashSet<GameObject>();

        for (int i = 0; i < levels.Length; i++)
        {
            GameObject[] groups = levels[i].boxGroupRoots;
            if (groups == null)
            {
                continue;
            }

            for (int j = 0; j < groups.Length; j++)
            {
                GameObject group = groups[j];
                if (group != null)
                {
                    allConfiguredGroups.Add(group);
                }
            }
        }

        foreach (GameObject group in allConfiguredGroups)
        {
            group.SetActive(false);
        }

        GameObject[] activeGroups = levels[selectedLevel].boxGroupRoots;
        if (activeGroups == null || activeGroups.Length == 0)
        {
            return;
        }

        for (int i = 0; i < activeGroups.Length; i++)
        {
            GameObject group = activeGroups[i];
            if (group != null)
            {
                group.SetActive(true);
            }
        }
    }

    private void TeleportPlayerToSpawn(LevelData currentLevel)
    {
        if (currentLevel == null || currentLevel.playerSpawnPoint == null)
        {
            Debug.LogError("GameManager: Spawn Point missing for selected level.");
            return;
        }

        if (player == null)
        {
            FirstPersonController controller = Object.FindAnyObjectByType<FirstPersonController>(FindObjectsInactive.Include);
            if (controller != null)
            {
                player = controller.gameObject;
            }
        }

        if (player == null)
        {
            Debug.LogError("GameManager: Player reference missing and no FirstPersonController found.");
            return;
        }

        CharacterController characterController = player.GetComponent<CharacterController>();
        if (characterController != null)
        {
            characterController.enabled = false;
        }

        Rigidbody rb = player.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = currentLevel.playerSpawnPoint.position;
            rb.rotation = currentLevel.playerSpawnPoint.rotation;
        }
        else
        {
            player.transform.SetPositionAndRotation(
                currentLevel.playerSpawnPoint.position,
                currentLevel.playerSpawnPoint.rotation);
        }

        if (characterController != null)
        {
            characterController.enabled = true;
        }
    }

    private IEnumerator StartEmergencySirenWhenReady()
    {
        const float maxWaitSeconds = 5f;
        float elapsed = 0f;

        while (elapsed < maxWaitSeconds)
        {
            SmokeVisionEffect smokeVision = null;
            if (player != null)
            {
                smokeVision = player.GetComponentInChildren<SmokeVisionEffect>(true);
                if (smokeVision != null && !smokeVision.gameObject.scene.IsValid())
                {
                    // Ignore prefab asset references accidentally assigned in inspector.
                    smokeVision = null;
                }
            }

            if (smokeVision == null)
            {
                smokeVision = Object.FindAnyObjectByType<SmokeVisionEffect>(FindObjectsInactive.Include);
            }

            if (smokeVision != null && smokeVision.gameObject.activeInHierarchy)
            {
                smokeVision.TriggerSirenPulse(fireAlarmStartDelay);
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        Debug.LogWarning("[GameManager] Could not find active SmokeVisionEffect in time; emergency siren was not triggered.");
    }
}
