using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 挂在带 <see cref="Button"/> 的物体上：点击后按 Build Settings 中的场景索引加载场景。
/// 也可不自动绑定：把本组件挂在任意物体上，在 Button 的 On Click 里拖此物体并选 <see cref="LoadTargetScene"/>。
/// </summary>
[DisallowMultipleComponent]
public class LoadSceneByBuildIndexButton : MonoBehaviour
{
    [SerializeField]
    [Tooltip("File → Build Settings 里从上到下的顺序，从 0 开始")]
    int sceneBuildIndex;

    [SerializeField]
    [Tooltip("若游戏曾把 Time.timeScale 设为 0，加载前恢复为 1")]
    bool resetTimeScaleBeforeLoad = true;

    [SerializeField]
    [Tooltip("为 true 时在本物体上查找 Button 并自动注册点击")]
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

    /// <summary>供 UI Button → On Click () 绑定。</summary>
    public void LoadTargetScene()
    {
        if (sceneBuildIndex < 0 || sceneBuildIndex >= SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogWarning(
                $"{nameof(LoadSceneByBuildIndexButton)} on {name}: 无效的场景索引 {sceneBuildIndex}（Build Settings 中共有 {SceneManager.sceneCountInBuildSettings} 个场景）。",
                this);
            return;
        }

        if (resetTimeScaleBeforeLoad)
            Time.timeScale = 1f;

        SceneManager.LoadScene(sceneBuildIndex);
    }
}
