using UnityEngine;

public class Toggle : MonoBehaviour
{
    public void ToggleObject()
    {
        gameObject.SetActive(!gameObject.activeSelf);
    }
}
