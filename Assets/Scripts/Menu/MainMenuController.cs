using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    private void Start()
    {
        // Automatically hook up this script to the Button so you don't even have to use the Inspector.
        Button btn = GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.AddListener(PlayGame);
        }
    }

    public void PlayGame()
    {
        Debug.Log("Loading B scene...");
        SceneManager.LoadScene("B");
    }

    public void QuitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
