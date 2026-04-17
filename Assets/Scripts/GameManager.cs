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

        // // 4. Teleport Player
        // if (player != null && currentLevel.playerSpawnPoint != null)
        // {
        //     // Reset velocity if player has a Rigidbody or CharacterController to avoid physics glitches during teleport
        //     CharacterController cc = player.GetComponent<CharacterController>();
        //     if (cc != null) cc.enabled = false; // Temporarily disable to move

        //     player.transform.position = currentLevel.playerSpawnPoint.position;
        //     player.transform.rotation = currentLevel.playerSpawnPoint.rotation;

        //     if (cc != null) cc.enabled = true;
        // }
        // else
        // {
        //     Debug.LogError("GameManager: Player reference or Spawn Point missing!");
        // }

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
    }
}
