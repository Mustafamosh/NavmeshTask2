// CreditsController.cs
// One in the credits scene.
//
// Single back button that returns to the start menu.
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CreditsController : MonoBehaviour
{
    [Header("Buttons")]
    public Button backButton;

    [Header("Scenes")]
    [Tooltip("Must match the name in the Scene List exactly.")]
    public string startMenuSceneName = "1Start-Menu";

    void Start()
    {
        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(ReturnToStartMenu);
        }
    }

    public void ReturnToStartMenu()
    {
        if (Application.CanStreamedLevelBeLoaded(startMenuSceneName))
        {
            SceneManager.LoadScene(startMenuSceneName);
        }
        else
        {
            Debug.LogWarning("CreditsController: scene " + startMenuSceneName +
                             " is not in the Scene List.");
        }
    }
}