using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// IntroScene 对话/剧情流程控制器。
///
/// 通过 <see cref="dialogueList"/> 配置每一步的内容、场景窗口状态与推进方式。
///
/// 三种推进模式（<see cref="AdvanceMode"/>）：
///   · AutoAdvance  — 等待 autoAdvanceDelay 秒后自动跳到下一步
///   · WaitForClick — 等待玩家鼠标左键点击
///   · WaitForEvent — 只能由代码调用 <see cref="AdvanceFromEvent"/> 推进（广告刷屏等特殊演出）
///
/// 与 IntroManager 联动：IntroManager.onStoryBegin → BeginDialogue()
/// </summary>
[AddComponentMenu("Game/Intro Controller")]
public class IntroController : MonoBehaviour
{
    // ══════════════════════════════════════════════════════════════
    // 对话步骤数据
    // ══════════════════════════════════════════════════════════════

    /// <summary>对话列表中每一步的配置数据。</summary>
    [System.Serializable]
    public class DialoguePack
    {
        [Header("Content — 内容")]
        [Tooltip("角色立绘 Sprite；对应 dialogueHead RawImage")]
        public Sprite portrait;

        [Tooltip("气泡框 Sprite；对应 messageBox Image")]
        public Sprite boxSprite;

        [Tooltip("角色名字；对应 dialogueName TMP")]
        public string speakerName;

        [TextArea(2, 6)]
        [Tooltip("对话文本；对应 dialogueText TMP")]
        public string text;

        [Header("Scene Windows — 场景窗口")]
        [Tooltip("是否显示对话框组（dialogueGroup）")]
        public bool showDialogue;
        [Tooltip("是否显示 Boss 会议加入按钮（bossMeetingJoin）")]
        public bool showBossJoin;
        [Tooltip("是否显示 Boss 会议窗口（bossMeeting）")]
        public bool showBoss;
        [Tooltip("是否显示 Boss 会议信息（bossMeetingInfo）")]
        public bool showBossMeetingInfo;
        [Tooltip("是否显示 Sam 会议加入按钮（samMeetingJoin）")]
        public bool showSamJoin;
        [Tooltip("是否显示 Sam 会议窗口（samMeeting）")]
        public bool showSam;
        [Tooltip("是否显示广告窗口（adMeeting）")]
        public bool showAd;
        [Tooltip("是否显示追踪器窗口（trackerWindow）")]
        public bool showTracker;
        [Tooltip("是否显示捡骨头动画（pickUpBoneAnim）")]
        public bool showPickUpBoneAnim;

        [Header("Ad Spam — 广告刷屏")]
        [Tooltip("此步骤期间是否持续生成新广告（依赖 adSpam 列表）")]
        public bool adSpamming;

        [Tooltip("所有广告生成完毕后自动推进到下一步（无需 AutoAdvance 计时，也无需手动调用 AdvanceFromEvent）")]
        public bool autoAdvanceWhenSpamComplete;

        [Header("Audio — 语音")]
        [Tooltip("此步骤播放的语音 Prefab（含 AudioSource）；留空则无语音")]
        public GameObject vocalSound;

        [Header("Advance Mode — 推进方式")]
        [Tooltip(
            "AutoAdvance  = 等待 autoAdvanceDelay 秒后自动推进\n" +
            "WaitForClick = 等待鼠标左键点击\n" +
            "WaitForEvent = 由代码调用 AdvanceFromEvent()（广告刷屏等特殊步骤）")]
        public AdvanceMode advanceMode = AdvanceMode.WaitForClick;

        [Tooltip("AutoAdvance 模式：等待多少秒后自动推进\nWaitForClick 模式：超过此秒数未点击时自动推进（0 = 禁用超时，永远等待点击）")]
        [Min(0f)]
        public float autoAdvanceDelay = 1.5f;

        [Header("BGM — 背景音乐")]
        [Tooltip("进入此步骤时切换全局 BGM。None = 不切换。用于 Intro 指定事件（例如广告刷屏）切到 Theme Stupid。")]
        public JiU.GlobalBackgroundMusic.Theme bgmTheme = JiU.GlobalBackgroundMusic.Theme.None;
    }

    /// <summary>对话步骤的推进方式。</summary>
    public enum AdvanceMode
    {
        /// <summary>等待 autoAdvanceDelay 秒后自动推进。</summary>
        AutoAdvance,
        /// <summary>等待玩家鼠标左键点击后推进。</summary>
        WaitForClick,
        /// <summary>等待外部调用 AdvanceFromEvent()；常用于广告刷屏等需要特殊交互的步骤。</summary>
        WaitForEvent,
    }

    // ══════════════════════════════════════════════════════════════
    // 对话列表
    // ══════════════════════════════════════════════════════════════

    [Header("Dialogue List — 对话列表")]
    [Tooltip("按顺序配置每一步的内容与状态；列表播放完毕后自动加载 nextSceneBuildIndex 场景")]
    public List<DialoguePack> dialogueList = new List<DialoguePack>();

    // ══════════════════════════════════════════════════════════════
    // 场景 UI 引用
    // ══════════════════════════════════════════════════════════════

    [Header("Dialogue UI — 对话框 UI")]
    [Tooltip("整体对话框父 GameObject；由每步的 showDialogue 控制显示")]
    public GameObject  dialogueGroup;
    [Tooltip("角色立绘 RawImage")]
    public RawImage    dialogueHead;
    [Tooltip("气泡框 Image")]
    public Image       messageBox;
    [Tooltip("对话正文 TMP")]
    public TextMeshProUGUI dialogueText;
    [Tooltip("角色名 TMP")]
    public TextMeshProUGUI dialogueName;

    [Header("Scene Windows — 场景窗口")]
    [Tooltip("Boss 会议加入按钮 GameObject")]
    public GameObject bossMeetingJoin;
    [Tooltip("Boss 会议窗口 GameObject")]
    public GameObject bossMeeting;
    [Tooltip("Boss 会议信息 GameObject")]
    public GameObject bossMeetingInfo;
    [Tooltip("Sam 会议加入按钮 GameObject")]
    public GameObject samMeetingJoin;
    [Tooltip("Sam 会议窗口 GameObject")]
    public GameObject samMeeting;
    [Tooltip("广告窗口 GameObject（整体容器）")]
    public GameObject adMeeting;
    [Tooltip("追踪器窗口 GameObject")]
    public GameObject trackerWindow;
    [Tooltip("捡骨头动画 GameObject")]
    public GameObject pickUpBoneAnim;

    // ══════════════════════════════════════════════════════════════
    // 广告刷屏
    // ══════════════════════════════════════════════════════════════

    [Header("Ad Spam — 广告刷屏")]
    [Tooltip("可被依次激活的广告 GameObject 列表")]
    public List<GameObject> adSpam = new List<GameObject>();

    [Tooltip("每次生成新广告的间隔（秒）")]
    [Min(0.1f)]
    public float adSpawnInterval = 1f;

    [Tooltip("最多同时显示多少个广告")]
    [Min(1)]
    public int maxAdCount = 5;

    // ══════════════════════════════════════════════════════════════
    // 场景切换
    // ══════════════════════════════════════════════════════════════

    [Header("Scene Transition — 场景切换")]
    [Tooltip("对话全部结束后加载的场景 Build Index")]
    public int nextSceneBuildIndex = 1;

    // ══════════════════════════════════════════════════════════════
    // 小游戏联动
    // ══════════════════════════════════════════════════════════════

    [Header("Mini Game Integration — 小游戏联动")]
    [Tooltip("勾选后忽略小游戏，场景加载即刻开始对话（测试 / 跳过小游戏专用）")]
    public bool startImmediately = false;

    [Header("Preamble — 小游戏前自述")]
    [Tooltip("小游戏出现前播放的自述台词；全部结束后隐藏 DialogueBox，再显示小游戏。之后由 BeginDialogue() 重新唤醒。")]
    public List<PreambleLine> preambleLines = new List<PreambleLine>();

    [Tooltip("播放 preamble 配音的 AudioSource；留空则使用本物体上的 AudioSource（没有则运行时添加）")]
    public AudioSource preambleAudioSource;

    /// <summary>小游戏前自述的单句配置。</summary>
    [System.Serializable]
    public class PreambleLine
    {
        [Tooltip("角色立绘；留空则不替换当前头像")]
        public Sprite portrait;

        [Tooltip("气泡框 Sprite；留空则不替换")]
        public Sprite boxSprite;

        [Tooltip("角色名")]
        public string speakerName = "Berry (You)";

        [TextArea(2, 4)]
        public string text;

        [Tooltip("配音 AudioClip；尚未就绪可留空")]
        public AudioClip vocalClip;

        [Tooltip("AutoAdvance = 等 autoAdvanceDelay 秒后自动下一句\nWaitForClick = 点击推进，delay>0 时超时也会推进")]
        public AdvanceMode advanceMode = AdvanceMode.WaitForClick;

        [Tooltip("自动推进 / 点击超时秒数。若有配音，实际等待不会短于配音时长")]
        [Min(0f)]
        public float autoAdvanceDelay = 2.5f;
    }

    // ══════════════════════════════════════════════════════════════
    // 运行时私有状态
    // ══════════════════════════════════════════════════════════════

    Mouse  _mouse;
    int    _currentIndex;
    bool   _dialogueActive;
    bool   _isAutoAdvancing;

    int    _adSpawnCount;
    bool   _canSpawnAd = true;

    GameObject _currentVocalSource;
    AudioSource _preambleAudio;

    // ══════════════════════════════════════════════════════════════
    // 生命周期
    // ══════════════════════════════════════════════════════════════

    void Start()
    {
        _mouse = Mouse.current;

        if (startImmediately)
            BeginDialogue();
        else if (dialogueGroup != null)
            dialogueGroup.SetActive(false);
    }

    void Update()
    {
        if (!_dialogueActive) return;
        if (dialogueList == null || dialogueList.Count == 0) return;

        DialoguePack current = dialogueList[_currentIndex];

        ApplyWindowVisibility(current);
        ApplyDialogueUI(current);
        HandleAdSpam(current);

        switch (current.advanceMode)
        {
            case AdvanceMode.AutoAdvance:
                if (!_isAutoAdvancing)
                {
                    _isAutoAdvancing = true;
                    StartCoroutine(AutoAdvanceRoutine(current.autoAdvanceDelay));
                }
                break;

            case AdvanceMode.WaitForClick:
                // 超时自动推进（autoAdvanceDelay > 0 时启动一次）
                if (!_isAutoAdvancing && current.autoAdvanceDelay > 0f)
                {
                    _isAutoAdvancing = true;
                    StartCoroutine(AutoAdvanceRoutine(current.autoAdvanceDelay));
                }
                // 点击可提前推进（同时取消超时协程）
                if (_mouse != null && _mouse.leftButton.wasPressedThisFrame)
                    AdvanceToNext();
                break;

            case AdvanceMode.WaitForEvent:
                // 等待外部调用 AdvanceFromEvent()
                break;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 公开接口
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 解锁对话推进逻辑，从第 0 步开始。
    /// 由 IntroManager.onStoryBegin UnityEvent 调用（小游戏规定文件分完后唤醒 DialogueBox）。
    /// </summary>
    public void BeginDialogue()
    {
        _currentIndex    = 0;
        _dialogueActive  = true;
        _isAutoAdvancing = false;

        if (dialogueHead != null)
            SetActive(dialogueHead.gameObject, true);

        if (dialogueList != null && dialogueList.Count > 0)
            EnterDialogueStep(dialogueList[0]);
    }

    /// <summary>
    /// 小游戏出现前的自述演出。播完后隐藏 DialogueBox，由调用方再显示小游戏。
    /// </summary>
    public IEnumerator PlayPreamble()
    {
        if (preambleLines == null || preambleLines.Count == 0)
            yield break;

        _mouse = Mouse.current;
        SetActive(dialogueGroup, true);

        for (int i = 0; i < preambleLines.Count; i++)
        {
            PreambleLine line = preambleLines[i];
            ApplyPreambleUI(line);
            PlayPreambleClip(line.vocalClip);
            yield return WaitForPreambleAdvance(line);
        }

        StopPreambleClip();
        SetActive(dialogueGroup, false);
    }

    /// <summary>供 Inspector UnityEvent 直接切到 Stupid 主题（也可在对话步骤上设置 bgmTheme）。</summary>
    public void SwitchBgmToStupid()
    {
        PlayGlobalBgm(JiU.GlobalBackgroundMusic.Theme.Stupid);
    }

    /// <summary>
    /// 推进一步（仅对 WaitForEvent 步骤有效）。
    /// 典型用途：广告刷屏/特殊演出结束后，由 GyrateAd、Button 或其他事件调用。
    /// </summary>
    public void AdvanceFromEvent()
    {
        if (!_dialogueActive || dialogueList == null) return;
        if (dialogueList[_currentIndex].advanceMode != AdvanceMode.WaitForEvent) return;

        AdvanceToNext();
    }

    // ══════════════════════════════════════════════════════════════
    // 内部推进逻辑
    // ══════════════════════════════════════════════════════════════

    void AdvanceToNext()
    {
        StopAllCoroutines();
        _isAutoAdvancing = false;

        if (_currentIndex < dialogueList.Count - 1)
        {
            _currentIndex++;
            EnterDialogueStep(dialogueList[_currentIndex]);
        }
        else
        {
            // 列表播放完毕 → 加载下一场景
            SceneManager.LoadScene(nextSceneBuildIndex);
        }
    }

    IEnumerator AutoAdvanceRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        _isAutoAdvancing = false;
        AdvanceToNext();
    }

    // ══════════════════════════════════════════════════════════════
    // 步骤进入：语音 + 可选 BGM 切换
    // ══════════════════════════════════════════════════════════════

    void EnterDialogueStep(DialoguePack pack)
    {
        PlayVocalSound(pack.vocalSound);
        PlayGlobalBgm(pack.bgmTheme);
    }

    static void PlayGlobalBgm(JiU.GlobalBackgroundMusic.Theme theme)
    {
        if (theme == JiU.GlobalBackgroundMusic.Theme.None) return;
        if (JiU.GlobalBackgroundMusic.Instance != null)
            JiU.GlobalBackgroundMusic.Instance.PlayTheme(theme);
    }

    void PlayVocalSound(GameObject soundPrefab)
    {
        // 销毁上一步的语音对象
        if (_currentVocalSource != null)
        {
            Destroy(_currentVocalSource);
            _currentVocalSource = null;
        }

        if (soundPrefab != null)
            _currentVocalSource = Instantiate(soundPrefab, transform.position, Quaternion.identity);
    }

    // ══════════════════════════════════════════════════════════════
    // UI 更新
    // ══════════════════════════════════════════════════════════════

    void ApplyDialogueUI(DialoguePack pack)
    {
        if (dialogueText != null)
            dialogueText.text = pack.text;

        if (dialogueName != null)
            dialogueName.text = pack.speakerName;

        if (dialogueHead != null && pack.portrait != null)
            dialogueHead.texture = pack.portrait.texture;

        if (messageBox != null && pack.boxSprite != null)
            messageBox.sprite = pack.boxSprite;
    }

    void ApplyWindowVisibility(DialoguePack pack)
    {
        SetActive(dialogueGroup,   pack.showDialogue);
        SetActive(bossMeetingJoin,       pack.showBossJoin);
        SetActive(bossMeeting,           pack.showBoss);
        SetActive(bossMeetingInfo,       pack.showBossMeetingInfo);
        SetActive(samMeetingJoin,        pack.showSamJoin);
        SetActive(samMeeting,      pack.showSam);
        SetActive(adMeeting,       pack.showAd);
        SetActive(trackerWindow,         pack.showTracker);
        SetActive(pickUpBoneAnim,        pack.showPickUpBoneAnim);
    }

    // ══════════════════════════════════════════════════════════════
    // 广告刷屏
    // ══════════════════════════════════════════════════════════════

    void HandleAdSpam(DialoguePack pack)
    {
        if (pack.adSpamming)
        {
            if (_canSpawnAd && _adSpawnCount < maxAdCount && _adSpawnCount < adSpam.Count)
                StartCoroutine(SpawnAdRoutine());
        }
        else
        {
            // 停止所有广告的 Gyrate 动画
            foreach (GameObject ad in adSpam)
            {
                if (ad == null) continue;
                GyrateAd g = ad.GetComponent<GyrateAd>();
                if (g != null) g.isGrowing = false;
            }
        }
    }

    IEnumerator SpawnAdRoutine()
    {
        _canSpawnAd = false;

        GameObject ad = adSpam[_adSpawnCount];
        ad.SetActive(true);

        GyrateAd g = ad.GetComponent<GyrateAd>();
        if (g != null) g.isGrowing = true;

        _adSpawnCount++;
        yield return new WaitForSeconds(adSpawnInterval);
        _canSpawnAd = true;

        // 所有广告都生成完毕，且当前步骤勾选了自动推进 → 直接跳下一步
        bool spamDone = _adSpawnCount >= maxAdCount || _adSpawnCount >= adSpam.Count;
        if (spamDone && dialogueList[_currentIndex].autoAdvanceWhenSpamComplete)
            AdvanceToNext();
    }

    void ApplyPreambleUI(PreambleLine line)
    {
        if (dialogueText != null)
            dialogueText.text = line.text;

        if (dialogueName != null)
            dialogueName.text = line.speakerName;

        if (dialogueHead != null)
        {
            if (line.portrait != null)
            {
                dialogueHead.texture = line.portrait.texture;
                SetActive(dialogueHead.gameObject, true);
            }
            else
            {
                SetActive(dialogueHead.gameObject, false);
            }
        }

        if (messageBox != null && line.boxSprite != null)
            messageBox.sprite = line.boxSprite;
    }

    void PlayPreambleClip(AudioClip clip)
    {
        StopPreambleClip();
        if (clip == null) return;

        if (_preambleAudio == null)
        {
            _preambleAudio = preambleAudioSource != null
                ? preambleAudioSource
                : GetComponent<AudioSource>();
            if (_preambleAudio == null)
            {
                _preambleAudio = gameObject.AddComponent<AudioSource>();
                _preambleAudio.playOnAwake = false;
            }
        }

        _preambleAudio.PlayOneShot(clip);
    }

    void StopPreambleClip()
    {
        if (_preambleAudio != null)
            _preambleAudio.Stop();
    }

    IEnumerator WaitForPreambleAdvance(PreambleLine line)
    {
        float delay = line.autoAdvanceDelay;
        if (line.vocalClip != null)
            delay = Mathf.Max(delay, line.vocalClip.length);

        float t = 0f;
        while (true)
        {
            if (line.advanceMode == AdvanceMode.WaitForClick
                && _mouse != null
                && _mouse.leftButton.wasPressedThisFrame)
                yield break;

            t += Time.deltaTime;
            bool timedOut = delay > 0f && t >= delay;
            if (timedOut && line.advanceMode != AdvanceMode.WaitForEvent)
                yield break;

            yield return null;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 工具方法
    // ══════════════════════════════════════════════════════════════

    /// <summary>仅在状态实际发生变化时调用 SetActive，避免不必要的重绘。</summary>
    static void SetActive(GameObject obj, bool active)
    {
        if (obj != null && obj.activeSelf != active)
            obj.SetActive(active);
    }
}
