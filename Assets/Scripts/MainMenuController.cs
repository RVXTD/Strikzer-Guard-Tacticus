
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    public GameObject settingsPanel;

    public void PlayGame()
    {
        Debug.Log("Play button clicked!");
        SceneManager.LoadScene("Game");
    }

    public void OpenSettings()
    {
        Debug.Log("Settings button clicked!");

        if (settingsPanel == null)
        {
            Debug.LogError("Settings Panel is missing!");
            return;
        }

        settingsPanel.SetActive(true);
        Debug.Log("Settings panel opened!");
    }

    public void CloseSettings()
    {
        settingsPanel.SetActive(false);
    }

    public void QuitGame()
    {
        Debug.Log("Quit button clicked!");
        Application.Quit();
    }
}
