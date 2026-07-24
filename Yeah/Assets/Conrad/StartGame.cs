using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class StartGame : MonoBehaviour
{
    [Header("场景")]
    [Tooltip("要加载的目标场景索引")]
    [SerializeField] private int targetSceneIndex = 1;

    [Header("Arduino（可选）")]
    [Tooltip("拖入场景中的 StartSceneArduinoBridge 对象。留空则仅响应键盘/鼠标。")]
    [SerializeField] private StartSceneArduinoBridge arduinoBridge;

    private Mouse ms;
    private Keyboard kb;
    private bool _hasLoaded;

    void Start()
    {
        ms = Mouse.current;
        kb = Keyboard.current;
    }

    void Update()
    {
        if (_hasLoaded) return;

        bool keyOrMouse = (ms != null && ms.leftButton.wasPressedThisFrame)
                       || (kb != null && kb.anyKey.wasPressedThisFrame);

        bool arduino = arduinoBridge != null && arduinoBridge.IsTriggered;

        if (keyOrMouse || arduino)
            LoadNextScene();
    }

    /// <summary>
    /// 供 StartSceneArduinoBridge.onArduinoTriggered 事件直接调用，
    /// 也可以在 Update 轮询中被 IsTriggered 触发。
    /// </summary>
    public void LoadNextScene()
    {
        if (_hasLoaded) return;
        _hasLoaded = true;
        SceneManager.LoadScene(targetSceneIndex);
    }
}
