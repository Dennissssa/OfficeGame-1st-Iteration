using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// On a <see cref="Button"/> object: click loads scene by build index from Build Settings.
/// Or disable auto-wire: put on any object and call <see cref="LoadTargetScene"/> from Button On Click.
/// </summary>
[DisallowMultipleComponent]
public class LoadSceneByBuildIndexButton : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Build order in File > Build Settings, zero-based")]
    int sceneBuildIndex;

    [SerializeField]
    [Tooltip("If Time.timeScale was 0, set to 1 before load")]
    bool resetTimeScaleBeforeLoad = true;

    [SerializeField]
    [Tooltip("When true, finds Button on this object and wires onClick")]
    bool autoWireButtonOnSameObject = true;

    Button _button;

    void Awake()
    {
        if (!autoWireButtonOnSameObject) return;
        _button = GetComponent<Button>();
        if (_button != null)
            _button.onClick.AddListener(LoadTargetScene);
    }

    void OnDestroy()
    {
        if (_button != null)
            _button.onClick.RemoveListener(LoadTargetScene);
    }

    /// <summary>For UI Button On Click () binding.</summary>
    public void LoadTargetScene()
    {
        if (sceneBuildIndex < 0 || sceneBuildIndex >= SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogWarning(
                $"{nameof(LoadSceneByBuildIndexButton)} on {name}: invalid scene index {sceneBuildIndex} (Build Settings has {SceneManager.sceneCountInBuildSettings} scenes).",
                this);
            return;
        }

        if (resetTimeScaleBeforeLoad)
            Time.timeScale = 1f;

        SceneManager.LoadScene(sceneBuildIndex);
    }
}
