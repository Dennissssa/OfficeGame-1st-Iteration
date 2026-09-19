using System.Collections;
using TMPro;
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

    [Header("输入")]
    [Tooltip("勾选后可用鼠标左键触发开场。现场游玩请关闭，避免误触；主要靠 Arduino 触发。")]
    [SerializeField] private bool enableMouseClick = true;
    [Tooltip("勾选后可用任意键盘按键触发开场。现场游玩如只需 Arduino，可一并关闭。")]
    [SerializeField] private bool enableKeyboard = true;

    [Header("密码演出")]
    [Tooltip("用于显示自动输入密码（*）的 TMP。开场时应为空。")]
    [SerializeField] private TMP_Text loadingText;
    [Tooltip("每个密码位显示的字符。")]
    [SerializeField] private string passwordCharacter = "*";
    [Tooltip("自动输入多少个密码字符。")]
    [SerializeField] private int passwordLength = 18;
    [Tooltip("每个 * 出现的间隔（秒）。")]
    [SerializeField] private float characterInterval = 0.08f;
    [Tooltip("全部 * 输入完成后、再加载场景前的停顿（秒）。")]
    [SerializeField] private float holdAfterTyping = 0.45f;

    [Header("音效")]
    [Tooltip("密码演出开始时播放的音效。当前使用 title_typing。")]
    [SerializeField] private AudioClip typingSfx;
    [Tooltip("演出音效音量。")]
    [Range(0f, 1f)]
    [SerializeField] private float typingSfxVolume = 1f;

    private Mouse ms;
    private Keyboard kb;
    private AudioSource _audio;
    private bool _hasStarted;

    void Start()
    {
        ms = Mouse.current;
        kb = Keyboard.current;

        _audio = GetComponent<AudioSource>();
        if (_audio == null)
            _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0f;

        if (loadingText != null)
            loadingText.text = string.Empty;
    }

    void Update()
    {
        if (_hasStarted) return;

        bool mouse = enableMouseClick && ms != null && ms.leftButton.wasPressedThisFrame;
        bool key = enableKeyboard && kb != null && kb.anyKey.wasPressedThisFrame;
        bool arduino = arduinoBridge != null && arduinoBridge.IsTriggered;

        if (mouse || key || arduino)
            LoadNextScene();
    }

    /// <summary>
    /// 供 StartSceneArduinoBridge.onArduinoTriggered 事件直接调用，
    /// 也可以在 Update 轮询中被 IsTriggered 触发。
    /// 先播放自动输入密码演出，再加载下一场景。
    /// </summary>
    public void LoadNextScene()
    {
        if (_hasStarted) return;
        _hasStarted = true;
        StartCoroutine(PlayPasswordThenLoad());
    }

    private IEnumerator PlayPasswordThenLoad()
    {
        if (_audio != null && typingSfx != null)
            _audio.PlayOneShot(typingSfx, typingSfxVolume);

        if (loadingText != null)
        {
            loadingText.text = string.Empty;

            int count = Mathf.Max(0, passwordLength);
            string glyph = string.IsNullOrEmpty(passwordCharacter) ? "*" : passwordCharacter;

            for (int i = 0; i < count; i++)
            {
                loadingText.text += glyph;
                if (characterInterval > 0f)
                    yield return new WaitForSeconds(characterInterval);
            }

            if (holdAfterTyping > 0f)
                yield return new WaitForSeconds(holdAfterTyping);
        }

        SceneManager.LoadScene(targetSceneIndex);
    }
}
