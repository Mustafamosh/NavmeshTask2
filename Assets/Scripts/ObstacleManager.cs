using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class ObstacleManager : MonoBehaviour
{
    [Header("Placement Mode")]
    [SerializeField] private bool placementMode = false;
    [SerializeField] private TextMeshProUGUI placementModeText = null;

    private GameObject[] highlightObjects;
    private Coroutine toggleFeedbackCoroutine;

    private void Start()
    {
        highlightObjects = FindHighlightObjects();
        SetHighlightsActive(false);
    }

    private void Update()
    {
        if (!placementMode)
            return;

        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            GameObject clickedObject = hit.collider != null ? hit.collider.gameObject : null;
            if (clickedObject != null)
            {
                Transform highlightRoot = FindHighlightRoot(clickedObject.transform);
                if (highlightRoot != null)
                {
                    Debug.Log($"Highlight clicked: {highlightRoot.name}");
                    ToggleObstacleChildren(highlightRoot.gameObject, clickedObject);
                }
            }
        }
    }

    public void ActivatePlacementMode()
    {
        SetPlacementMode(true);
    }

    public void DeactivatePlacementMode()
    {
        SetPlacementMode(false);
    }

    public void TogglePlacementMode()
    {
        SetPlacementMode(!placementMode);
    }

    public void SetPlacementMode(bool enabled)
    {
        placementMode = enabled;
        SetHighlightsActive(placementMode);
        UpdatePlacementModeText();
        Debug.Log($"Placement mode {(placementMode ? "enabled" : "disabled")}");
    }

    private void UpdatePlacementModeText()
    {
        if (placementModeText == null)
            return;

        placementModeText.text = placementMode ? "ON" : "OFF";
    }

    private GameObject[] FindHighlightObjects()
    {
        GameObject[] allObjects = FindObjectsByType<GameObject>();
        System.Collections.Generic.List<GameObject> found = new System.Collections.Generic.List<GameObject>();

        foreach (GameObject obj in allObjects)
        {
            if (obj == null)
                continue;

            if (IsHighlight(obj))
            {
                found.Add(obj);
            }
        }

        return found.ToArray();
    }

    private void SetHighlightsActive(bool active)
    {
        if (highlightObjects == null)
            return;

        foreach (GameObject highlight in highlightObjects)
        {
            if (highlight != null)
                SetHighlightVisualsActive(highlight, active);
        }
    }

    private void SetHighlightVisualsActive(GameObject highlight, bool active)
    {
        if (highlight == null)
            return;

        foreach (Renderer renderer in highlight.GetComponents<Renderer>())
        {
            renderer.enabled = active;
        }

        foreach (Collider collider in highlight.GetComponents<Collider>())
        {
            collider.enabled = active;
        }

        foreach (ParticleSystem ps in highlight.GetComponents<ParticleSystem>())
        {
            var emission = ps.emission;
            emission.enabled = active;
        }
    }

    private Transform FindHighlightRoot(Transform current)
    {
        while (current != null)
        {
            if (IsHighlight(current.gameObject))
                return current;

            current = current.parent;
        }

        return null;
    }

    private void ToggleObstacleChildren(GameObject highlightRoot, GameObject clickedObject)
    {
        bool anyChildActive = false;

        foreach (Transform child in highlightRoot.transform)
        {
            if (child.gameObject.activeSelf)
                anyChildActive = true;
        }

        bool newState = !anyChildActive;

        foreach (Transform child in highlightRoot.transform)
        {
            child.gameObject.SetActive(newState);
        }

        string obstacleName = GetObstacleDisplayName(highlightRoot.name);
        string action = newState ? "enabled" : "disabled";
        string details = $"Obstacle {action}: {obstacleName}";

        if (toggleFeedbackCoroutine != null)
            StopCoroutine(toggleFeedbackCoroutine);

        toggleFeedbackCoroutine = StartCoroutine(PlayToggleFeedback(highlightRoot.transform));

        SimulationLogger.LogEvent(
            "EVENT-" + obstacleName.Replace(" ", "-"),
            obstacleName,
            details,
            SimulationLogger.GetSimulationTime(),
            -1,
            SensorType.Obstacle
        );
    }

    private IEnumerator PlayToggleFeedback(Transform target)
    {
        if (target == null)
            yield break;

        Vector3 originalScale = target.localScale;
        Vector3 pulseScale = originalScale * 1.12f;

        target.localScale = pulseScale;
        yield return new WaitForSeconds(0.08f);
        target.localScale = originalScale;
        yield return new WaitForSeconds(0.08f);
        target.localScale = pulseScale;
        yield return new WaitForSeconds(0.08f);
        target.localScale = originalScale;

        toggleFeedbackCoroutine = null;
    }

    private string GetObstacleDisplayName(string sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
            return "Obstacle 1";

        string trimmedName = sourceName.Trim();
        if (trimmedName.Equals("Highlight", System.StringComparison.OrdinalIgnoreCase))
            return "Obstacle 1";

        if (trimmedName.StartsWith("Highlight", System.StringComparison.OrdinalIgnoreCase))
        {
            string suffix = trimmedName.Substring("Highlight".Length).Trim();
            if (string.IsNullOrEmpty(suffix))
                return "Obstacle 1";

            if (suffix.StartsWith("(") && suffix.EndsWith(")"))
            {
                string numberText = suffix.Substring(1, suffix.Length - 2).Trim();
                if (int.TryParse(numberText, out int suffixNumber))
                    return "Obstacle " + (suffixNumber + 1);
            }

            if (int.TryParse(suffix, out int numericSuffix))
                return "Obstacle " + (numericSuffix + 1);
        }

        return trimmedName;
    }

    private bool IsHighlight(GameObject obj)
    {
        if (obj == null)
            return false;

        return HasTag(obj, "Highlight") || obj.name.Contains("Highlight") || obj.name.Contains("highlight");
    }

    private bool HasTag(GameObject obj, string tag)
    {
        if (obj == null || string.IsNullOrEmpty(tag))
            return false;

        try
        {
            return obj.CompareTag(tag);
        }
        catch (UnityEngine.UnityException)
        {
            return false;
        }
    }
}
