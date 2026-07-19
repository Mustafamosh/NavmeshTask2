// StartMenuController.cs
// One in the 1Start-Menu scene.
//
// Start   loads the main menu scene where agent settings are chosen.
// Credits loads the credits scene. Safe to leave unbuilt, the button logs and does nothing.
// Back    leaves the application and returns the participant to the landing page.
//         In a standalone build this opens the URL in the default browser and quits.
//         In the editor it only opens the URL, since quitting play mode mid click is unhelpful.
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class StartMenuController : MonoBehaviour
{
    [Header("Buttons")]
    public Button startButton;
    public Button creditsButton;
    public Button backButton;

    [Header("Scenes")]
    [Tooltip("The settings menu with the agent sliders.")]
    public string mainMenuSceneName = "2Main Menu";

    [Tooltip("Leave as is until the credits scene exists and is added to the Scene List.")]
    public string creditsSceneName = "4Credits";

    [Header("Landing page")]
    [Tooltip("The GitHub Pages URL the participants were given.")]
    public string landingPageUrl = "https://yourusername.github.io/yourrepo/";

    void Start()
    {
        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(OpenMainMenu);
        }

        if (creditsButton != null)
        {
            creditsButton.onClick.RemoveAllListeners();
            creditsButton.onClick.AddListener(OpenCredits);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(ReturnToLandingPage);
        }
    }

    public void OpenMainMenu()
    {
        LoadIfPossible(mainMenuSceneName);
    }

    public void OpenCredits()
    {
        LoadIfPossible(creditsSceneName);
    }

    public void ReturnToLandingPage()
    {
        if (!string.IsNullOrEmpty(landingPageUrl))
            Application.OpenURL(landingPageUrl);

#if UNITY_EDITOR
        Debug.Log("Back pressed. In a build the application would quit here.");
#else
        Application.Quit();
#endif
    }

    // Checks the scene is actually in the Scene List before trying to load it, so a
    // missing scene logs a clear warning instead of throwing a runtime error.
    void LoadIfPossible(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("StartMenuController: no scene name set for this button.");
            return;
        }

        if (Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.LogWarning("StartMenuController: scene " + sceneName +
                             " is not in the Scene List yet, so the button did nothing.");
        }
    }
}