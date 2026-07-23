using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public static bool FreezeFailures { get; private set; } = false;

    /// <summary>
    /// True after phone off-hook (Arduino <c>PHONE_PICKUP</c>).
    /// Works with <see cref="phoneWorkItemForPickupAudioSuppressScope"/>: only suppresses <strong>broken and Bait</strong> start SFX (including Bait end start);
    /// repair correct/wrong, <c>PlayAtIndex</c>, etc. are unaffected. If a WorkItem is set, scope is that item only; if null, applies globally.
    /// </summary>
    public static bool SuppressNonBaitBrokeItemSfxFromPhonePickup { get; private set; }

    /// <summary>
    /// When off-hook and <see cref="SuppressNonBaitBrokeItemSfxFromPhonePickup"/> is true, whether suppress-broken/Bait audio applies to components bound to <paramref name="boundWorkItem"/>.
    /// </summary>
    public static bool PickupAudioSuppressAppliesToBoundWorkItem(WorkItem boundWorkItem)
    {
        if (!SuppressNonBaitBrokeItemSfxFromPhonePickup)
            return false;
        if (Instance == null)
            return false;
        WorkItem scope = Instance.phoneWorkItemForPickupAudioSuppressScope;
        if (scope == null)
            return true;
        return boundWorkItem != null && boundWorkItem == scope;
    }

    [Header("Work pressure (from 0; reaching maxWork fails; defaults below when no phases)")]
    public float work = 0f;
    public float maxWork = 100f;
    public float workPunishment;
    public float workUltraPunishment;
    [Tooltip("每秒基础 work 增加量（无阶段配置时的默认值）")]
    [Min(0f)]
    public float workBaseIncreasePerSecond = 0f;

    [Tooltip("文件分类小游戏每次正确分类减少的 work 压力（无阶段配置时的默认值）")]
    [Min(0f)]
    public float workReductionPerCorrectSort = 5f;
    [Tooltip("Work pressure reduced toward 0 when Reward() is called")]
    public float workMashGain;
    [Tooltip("Work pressure reduced per second per working item")]
    public float workGainPerSecondPerWorkingItem = 1f;
    [Tooltip("Work pressure added per second per Broke item")]
    public float workLossPerSecondPerBrokenItem = 3f;
    [Tooltip("When no gamePhases: instant work pressure spike on Broke")]
    public float workPressureInstantOnBroke = 5f;
    [Tooltip("When no gamePhases: instant work pressure relief when Broke is repaired")]
    public float workPressureInstantOnBrokeRepair = 8f;
    [Tooltip("Boss no longer validates work amount; kept for compatibility")]
    public float bossMinWorkThreshold = 20f;

    [Header("Work-based Boss Trigger")]
    [Tooltip("归一化 work 值（0~1）超过此值时开始以概率触发 Boss；work 到满（maxWork）则直接触发")]
    [Range(0f, 1f)]
    public float bossWorkTriggerNormalizedMin = 0.5f;

    [Tooltip("work 在触发区间内时每秒触发 Boss 的概率（0 = 不触发，1 ≈ 1 秒内必触发，可超过 1 加快概率）")]
    [Min(0f)]
    public float bossWorkTriggerProbabilityPerSecond = 0.3f;

    [Tooltip("Boss 到场后 work 条渐变归零所需时间（秒）；0 = 立即归零")]
    [Min(0f)]
    public float bossArrivalWorkResetDuration = 1.5f;

    [Header("Boss incoming (see BossIncomingConfig component)")]
    [Tooltip("Can live on same GameObject as GameManager; if empty, GetComponent in Awake")]
    public BossIncomingConfig bossIncomingConfig;

    [Header("Tutorial")]
    [Tooltip("When enabled: Broke WorkItems in order; after all repaired, first Boss starts immediately (no random wait); after Boss leaves, normal phase flow")]
    public bool enableTutorial = true;

    [Tooltip("Tutorial Broke order; if empty, uses Items list order below")]
    public List<WorkItem> tutorialBreakOrder = new List<WorkItem>();
    public int phonePlacementInt;

    [Tooltip("Tutorial: after player repairs current broken item, wait this many seconds before next break; no wait after last item. 0 = break next immediately")]
    [Min(0f)]
    public float tutorialDelayAfterRepairBeforeNextBreak = 1f;

    public GameObject phoneTutStarterObject;

    [Tooltip("在 Console 输出电话教程与教程 Break 顺序：末句门闩、每次 Break 前工位 IsBroken/IsBaiting。用于查 ForceBait 与教程 Broke 竞态、串口 OnBroken 未触发。")]
    [SerializeField] bool debugLogTutorialPhoneBreakFlow;

    [Tooltip("When enableTutorial: each reference is SetActive(false) once, when the first Boss warning starts after the tutorial sequence ends.")]
    public GameObject[] deactivateOnFirstBossWarningAfterTutorial;

    [Header("Virus die (work overload / work bar full)")]
    [Tooltip("If set: after cleanup and pause, this plays first; work-progress lose panel opens when the clip duration elapses (realtime). Leave null = open panel immediately like before.")]
    public AudioClip virusDiePrePanelClip;

    [Tooltip("Plays virusDiePrePanelClip; if null, uses first AudioSource on this GameObject (add one if needed).")]
    public AudioSource virusDiePrePanelAudioSource;

    [Header("Victory Cinematic (plays after close-up, before result panel; leave all lists empty to skip)")]
    [Tooltip("AudioSource used to play cinematic clips (shared by both victory and boss-fail). If null, falls back to the first AudioSource on this GameObject.")]
    public AudioSource cinematicAudioSource;

    [Tooltip("Victory: audio clips played in sequence before the win panel opens. Null entries are skipped.")]
    public List<AudioClip> victoryCinematicClips = new List<AudioClip>();

    [Tooltip("Victory: each entry swaps the target Image's sprite when the cinematic starts (before audio begins).")]
    public List<CinematicSpriteSwap> victoryCinematicSpriteSwaps = new List<CinematicSpriteSwap>();

    [Header("Boss Fail Cinematic (plays after close-up, before result panel; leave all lists empty to skip)")]
    [Tooltip("Boss fail: audio clips played in sequence before the game-over panel opens. Null entries are skipped.")]
    public List<AudioClip> bossFailCinematicClips = new List<AudioClip>();

    [Tooltip("Boss fail: GameObjects to Destroy when the cinematic starts.")]
    public List<GameObject> bossFailCinematicDestroyObjects = new List<GameObject>();

    [Tooltip("Boss fail: GameObjects to SetActive(true) when the cinematic starts.")]
    public List<GameObject> bossFailCinematicActivateObjects = new List<GameObject>();

    Coroutine _virusDieRevealPanelRoutine;

    [Header("Normal phase flow")]
    [Tooltip("Apply index 0 at start; phase advance when normalized performance score (TotalPerformanceScore/divisor) hits thresholds (independent of Boss). Empty list keeps Inspector defaults")]
    public List<GamePhaseConfig> gamePhases = new List<GamePhaseConfig>();

    [Header("Victory condition")]
    [Tooltip("演出倒计时起始秒数（固定数字）；归零时触发胜利。与实际游玩时间 surviveTime 独立，两者均在教程结束后开始。")]
    [Min(0f)]
    public float performanceCountdownSeconds = 300f;

    [Header("Victory countdown · near-end warning (optional)")]
    [Tooltip("Show once when remaining seconds first drop to or below threshold; like Boss warning, prefer separate UI so it does not fight broken-item TMP")]
    public bool enableVictoryNearEndWarning = true;

    [Tooltip("Trigger when remaining seconds ≤ this value; 0 disables")]
    [Min(0f)]
    public float nearEndWarningWhenRemainingSeconds = 30f;

    [TextArea(2, 5)]
    [Tooltip("Warning message text; multi-line in Inspector")]
    public string nearEndWarningMessage = "Time is almost up!";

    [Tooltip("Root GameObject for warning panel; can duplicate broken-warning layout in scene and assign here")]
    public GameObject nearEndWarningPanelRoot;

    [Tooltip("TMP for warning; can be on same object as root")]
    public TextMeshProUGUI nearEndWarningText;

    [Header("References")]
    public UIManager ui;
    public ScreenVignetteTint screenTint;

    [Header("Virus Purge Meter (演出用进度条)")]
    [Tooltip("病毒清除进度演出条；胜利时跳至100%，Boss失败时立即停止。留空则忽略。")]
    public VirusPurgeMeter virusPurgeMeter;

    [Header("Boss events (optional)")]
    public UnityEvent OnBossWarningStarted;
    public UnityEvent OnBossArrived;
    [Tooltip("Invoked when stay check ends and leave animation/behavior starts; OnBossLeft fires after bossLeaveAnimationDuration")]
    public UnityEvent OnBossLeaveStarted;
    public UnityEvent OnBossLeft;

    [Tooltip("Invoked when game over is caused by Boss check")]
    public UnityEvent OnGameOverBossCaused;

    [Header("Optional")]
    public bool autoFindItemsOnStart = true;

    [Header("Debug")]
    [Tooltip("When enabled: log phase promotion score info at start; log current performance after each successful Broke repair")]
    public bool debugLogPhaseAndScore;

    [Tooltip("When enabled: log when work bar fills and GameOverWorkProgressFull runs (work, UIManager bound), for debugging that path")]
    public bool debugLogWorkProgressDeath = true;

    public List<WorkItem> items = new List<WorkItem>();

    [Header("Broken / Boss warning UI (single panel, no prefab)")]
    [Tooltip("Set active when any warning should show; inactive when none")]
    public GameObject brokenWarningPanelRoot;

    [Tooltip("TMP shared by broken-item text and Boss warning text")]
    public TextMeshProUGUI brokenWarningText;

    [Tooltip("Optional: use brokenWarningActiveSprite while warning shows; restore default when no next warning or Boss freeze window")]
    public Image brokenWarningIconImage;

    [Tooltip("Icon when starting a new warning chain for an item (unchanged when chaining to next broken item)")]
    public Sprite brokenWarningActiveSprite;

    [Tooltip("Optional: shake this RectTransform during early Boss warning (not in freeze window); stops in freeze")]
    public RectTransform brokenWarningShakeTarget;

    [Tooltip("Boss warning shake amplitude (local pixels scale)")]
    public float brokenWarningBossShakeAmplitude = 10f;

    [Tooltip("Boss warning shake frequency")]
    public float brokenWarningBossShakeFrequency = 14f;

    [Tooltip("After fixing warned item, if queue has more: hide panel, wait this long, then show next (dialog pause)")]
    [Min(0f)]
    public float interBrokenWarningPauseSeconds = 0.35f;

    [Header("Broken warnings (tutorial / no phase config yet)")]
    [Min(0f)]
    public float tutorialWarningShowDelayAfterBreak = 0.5f;

    [Tooltip("Message format; {0} = item display name")]
    public string tutorialWarningMessageFormat = "{0} is broken!";

    /// <summary>Cumulative performance score from hack events (can be negative); unchanged during tutorial until phase scoring is active.</summary>
    public float TotalPerformanceScore => _performanceScoreRaw;

    /// <summary>0–1: TotalPerformanceScore / performanceScoreNormalizationDivisor (Clamp01); used for score-forced Boss and phase promotion.</summary>
    public float NormalizedPerformanceScore => ComputeNormalizedPerformanceScore();

    public bool IsVictory { get; private set; }

    public bool IsGameOver => isGameOver;

    public ArduinoSerialBridge arduinoBridgeScript;
    [Tooltip("Use this slot when the scene uses ArduinoSerialBridgeWithMonitor instead of ArduinoSerialBridge.")]
    public ArduinoSerialBridgeWithMonitor arduinoBridgeWithMonitorScript;

    [Header("Phone pickup · SFX scope")]
    [Tooltip("Assign the phone WorkItem. V2/V3: off-hook suppresses broken/Bait loop SFX on pickup; hang-up while still Broke resumes loop. V1: no suppress; pickup may re-send PHONE:* for Arduino audio. Empty = all IsPhoneWorkItem in scene.")]
    public WorkItem phoneWorkItemForPickupAudioSuppressScope;

    [Header("Debug · phone pickup and PlaySoundOnEventAudioManager")]
    [Tooltip("When enabled: on pickup/hang-up, log all PlaySoundOnEventAudioManager _brokenPlaybackSkippedDueToPhonePickup / _baitingPlaybackSkippedDueToPhonePickup values")]
    [SerializeField] bool debugLogPhonePickupPlaySoundSkipFlags = true;

    public bool isTutorialing;
    public bool BossIsHere { get; private set; } = false;
    public bool BossWarning { get; private set; } = false;
    public float BossWarningTimeLeft { get; private set; } = 0f;

    /// <summary>True while Boss is present but arrival UI (e.g. Look→Peek) is not finished; Broke instant-fail is not evaluated yet.</summary>
    bool _bossBrokeCheckAwaitingArrivalSprites;

    /// <summary>While true, Boss is here but arrival UI is not done; Broke instant-fail is not evaluated yet.</summary>
    public bool IsBossBrokeCheckAwaitingArrivalSprites => _bossBrokeCheckAwaitingArrivalSprites;

    /// <summary>Count of WorkItems currently in Broke (Hacked) state.</summary>
    public int GetBrokenWorkItemCount()
    {
        int broken = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null && items[i].IsBroken)
                broken++;
        }
        return broken;
    }

    int _activeMaxConcurrentBroken = int.MaxValue;
    float _activeWorkPressureInstantOnBroke = 5f;
    float _activeWorkPressureInstantOnBrokeRepair = 8f;
    float _activeSortWorkReduction = 5f;
    float _activeBreakMin = 4f;
    float _activeBreakMax = 10f;
    float _activeWarningShowDelayAfterBreak = 0.5f;
    string _activeWarningMessageFormat = "{0} is broken!";

    readonly Dictionary<WorkItem, Coroutine> _itemBrokenWarningDelays = new Dictionary<WorkItem, Coroutine>();
    readonly List<WorkItem> _brokenWarningReadyQueue = new List<WorkItem>();

    WorkItem _displayedBrokenWarningItem;
    WorkItem _resumeBrokenWarningAfterBoss;
    bool _brokenWarningChainFresh = true;

    Sprite _defaultBrokenWarningIconSprite;
    Vector3 _brokenWarningShakeBaseLocalPos;
    Coroutine _brokenWarningInterPauseRoutine;
    Coroutine _bossWarningShakeRoutine;
    bool _bossWarningUiInFreezeHide;
    string _bossApproachWarningMessage;

    /// <summary>From first tutorial break until first Boss arrives: do not restore default broken-warning icon (including in freeze window).</summary>
    bool _holdBrokenWarningIconActiveUntilFirstBossArrived;

    int _currentPhaseIndex;
    bool _tutorialBreakSequenceDone;
    /// <summary>Whether WorkItems may auto Broke/Bait; true from Awake when tutorial off, false at start when tutorial on until first Boss leaves.</summary>
    bool _allowRandomWorkItemFailures;
    bool _firstBossLeaveHandled;
    bool _normalPerformanceScoringActive;

    /// <summary>First Boss after last tutorial WorkItem is repaired: skip random wait and cooldown.</summary>
    bool _immediateBossAfterTutorial;

    /// <summary>After at least one full Boss cycle, apply cooldown before the next Boss wait.</summary>
    bool _applyCooldownBeforeNextBossWait;

    float _performanceScoreRaw;
    float _performanceScoreRawWhenEnteredCurrentPhase;

    float _victoryCountdownRemaining;
    bool _victoryNearEndWarningShown;
    bool _phasePromotionArmed;
    float _timeSinceLastScorePhasePromotion = 1000f;

    /// <summary>When normalized score stays near 1, hysteresis cannot re-arm; min seconds between saturated score-based promotions.</summary>
    const float MinSecondsBetweenScorePhasePromotionsWhenSaturated = 0.35f;

    [Header("Gameplay Duration")]
    [Tooltip("游戏实际进行的时长（秒）；归零时触发胜利结算。演出时间 performanceCountdownSeconds 建议设置更长以配合演出效果。两者均在教程结束后开始倒数。")]
    [Min(0f)]
    public float surviveTime = 180f;

    /// <summary>Runtime countdown initialized from <see cref="surviveTime"/>; when it reaches 0 victory is triggered.</summary>
    float _gameTimeRemaining;
    bool isGameOver;
    Coroutine _bossLoopCoroutine;
    Coroutine _tutorialCoroutine;
    Coroutine _victoryCinematicCoroutine;
    Coroutine _bossFailCinematicCoroutine;

    /// <summary>Set true by <see cref="RegisterPhoneTutorialLastLineCompleted"/> from the last phone tutorial <c>ShowNextBoxForTut</c> line.</summary>
    bool _phoneTutorialLastLineCompleted;

    /// <summary>When tutorial is on and tutorial break sequence not done: victory countdown paused; ticks after tutorial ends.</summary>
    bool VictoryCountdownPausedForTutorial => enableTutorial && !_tutorialBreakSequenceDone;

    /// <summary>Optional: first line in the phone tutorial chain; clears the last-line gate (same as when <see cref="phoneTutStarterObject"/> is shown).</summary>
    public void RegisterPhoneTutorialStartLine()
    {
        _phoneTutorialLastLineCompleted = false;
        if (debugLogTutorialPhoneBreakFlow)
            Debug.Log("[GameManager/Tutorial] RegisterPhoneTutorialStartLine → 已重置 _phoneTutorialLastLineCompleted = false", this);
    }

    /// <summary>Call when the last phone tutorial line has finished (after gates, <c>waitTime</c>, and next panel). Unblocks the tutorial coroutine so the phone can <c>Break()</c>.</summary>
    public void RegisterPhoneTutorialLastLineCompleted()
    {
        _phoneTutorialLastLineCompleted = true;
        if (debugLogTutorialPhoneBreakFlow)
            Debug.Log(
                "[GameManager/Tutorial] RegisterPhoneTutorialLastLineCompleted → 门闩打开，教程协程将继续；下一将 tutorial Break() 的工位请见下一条 [GameManager/Tutorial] 即将 tutorial Break 日志。",
                this);
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (bossIncomingConfig == null)
            bossIncomingConfig = GetComponent<BossIncomingConfig>();

        // Set before any Start so WorkItem.Start before GameManager.Start does not lock enableAutoBreak false via RegisterItem
        _allowRandomWorkItemFailures = !enableTutorial;
    }

    void OnDestroy()
    {
        if (Instance == this)
            SuppressNonBaitBrokeItemSfxFromPhonePickup = false;
        ShutdownBrokenWarningSystem();
    }

    /// <summary>Call from Arduino <c>onPhonePickup</c> or other UnityEvents.</summary>
    public void PhonePickup_SetSuppressNonBaitBrokeSfx()
    {
        NotifyPhonePickupHookToWorkItems();

        if (!AnyPhoneSuppressesUnitySfxOnPickup())
            return;

        SuppressNonBaitBrokeItemSfxFromPhonePickup = true;
        JiU.PlaySoundOnEventAudioManager.StopBrokenBaitPlaybackForPickupScope();
        JiU.PlaySoundOnEvent.StopForPhonePickupAudioScope();
        if (debugLogPhonePickupPlaySoundSkipFlags)
            JiU.PlaySoundOnEventAudioManager.DebugLogAllPhonePickupSkipFlags("after PHONE_PICKUP (off-hook)", true);
    }

    /// <summary>Call from Arduino <c>onPhonePutdown</c> or other UnityEvents.</summary>
    public void PhonePutdown_ClearSuppressNonBaitBrokeSfx()
    {
        NotifyPhonePutdownHookToWorkItems();
        SuppressNonBaitBrokeItemSfxFromPhonePickup = false;
        if (debugLogPhonePickupPlaySoundSkipFlags)
            JiU.PlaySoundOnEventAudioManager.DebugLogAllPhonePickupSkipFlags("after PHONE_PUTDOWN (on-hook)", false);
    }

    bool AnyPhoneSuppressesUnitySfxOnPickup()
    {
        if (phoneWorkItemForPickupAudioSuppressScope != null)
            return phoneWorkItemForPickupAudioSuppressScope.PhoneSuppressesUnitySfxOnPickup;

        for (int i = 0; i < items.Count; i++)
        {
            WorkItem wi = items[i];
            if (wi != null && wi.IsPhoneWorkItem && wi.PhoneSuppressesUnitySfxOnPickup)
                return true;
        }

        return false;
    }

    void NotifyPhonePickupHookToWorkItems()
    {
        if (phoneWorkItemForPickupAudioSuppressScope != null)
        {
            phoneWorkItemForPickupAudioSuppressScope.NotifyPhonePickedUp();
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            WorkItem wi = items[i];
            if (wi != null && wi.IsPhoneWorkItem)
                wi.NotifyPhonePickedUp();
        }
    }

    void NotifyPhonePutdownHookToWorkItems()
    {
        if (phoneWorkItemForPickupAudioSuppressScope != null)
        {
            phoneWorkItemForPickupAudioSuppressScope.NotifyPhonePutDown();
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            WorkItem wi = items[i];
            if (wi != null && wi.IsPhoneWorkItem)
                wi.NotifyPhonePutDown();
        }
    }

    /// <summary>
    /// Sends PHONE:ANOMALY or PHONE:BAIT to whichever Arduino bridge is assigned.
    /// NOTE: Version1 no longer calls this on pickup; the bridge now sends PHONE:ANOMALY
    /// immediately on Break()/Bait() so Arduino always has the correct state.
    /// Kept for potential Inspector-wired or external callers.
    /// </summary>
    public void SendPhoneAudioToArduino(bool isBaiting)
    {
        if (arduinoBridgeScript != null)
            arduinoBridgeScript.SendPhoneAudioForWorkItemState(isBaiting);
        else if (arduinoBridgeWithMonitorScript != null)
            arduinoBridgeWithMonitorScript.SendPhoneAudioForWorkItemState(isBaiting);
    }

    /// <summary>When a match ends (any outcome), tell Arduino to reset all peripherals to defaults.</summary>
    void NotifyArduinoSystemResetOnMatchEnd()
    {
        if (arduinoBridgeScript != null)
            arduinoBridgeScript.ResetSystem();
        if (arduinoBridgeWithMonitorScript != null)
            arduinoBridgeWithMonitorScript.ResetSystem();
    }

    /// <summary>
    /// 结算时（胜利/失败/work满）统一清理所有 WorkItem 的运行状态：
    /// 禁用自动 break、静默复位 Broke/Bait 状态与 tint，停止 Bait 协程。
    /// 不触发 OnFixed 等 UnityEvent，不影响 work 值，不播放音效。
    /// Arduino 硬件侧已由 NotifyArduinoSystemResetOnMatchEnd() 发 SYSTEM:RESET 处理。
    /// </summary>
    void CleanupAllWorkItemsOnMatchEnd()
    {
        SetAllWorkItemsAutoBreak(false);
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null)
                items[i].ForceResetOnMatchEnd();
        }

        // 强制停止所有 broken/bait 音频 loop（Time.timeScale=0 不会自动停止 AudioSource）
        JiU.PlaySoundOnEventAudioManager.StopAllOnMatchEnd();
        JiU.PlaySoundOnEvent.StopAllOnMatchEnd();
    }

    void WarnIfMultipleUIManagerInScene()
    {
        UIManager[] all = FindObjectsOfType<UIManager>(true);
        if (all == null || all.Length <= 1)
            return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(
            "[GameManager] **Multiple UIManager** instances in scene. GameManager only uses the one assigned under References → ui in Inspector;");
        sb.AppendLine(
            "if the work-pressure lose panel lives on another Canvas/object's UIManager and is not assigned to this ui, ShowWorkProgressLose may return immediately or hit null refs; Awake instanceID in Console may differ from the one used on fail.");
        for (int i = 0; i < all.Length; i++)
        {
            bool isBound = ui != null && all[i] == ui;
            sb.AppendLine(
                $"  [{i}] instanceID={all[i].GetInstanceID()} object=\"{all[i].gameObject.name}\" " +
                $"workProgressLoseRoot={(all[i].workProgressLoseRoot != null ? "assigned" : "null")} " +
                $"is this GameManager.ui: {isBound}");
        }

        Debug.LogWarning(sb.ToString().TrimEnd(), this);
    }

    void Start()
    {
        work = 0f;
        SuppressNonBaitBrokeItemSfxFromPhonePickup = false;

        WarnIfMultipleUIManagerInScene();

        if (autoFindItemsOnStart && items.Count == 0)
        {
            items.Clear();
            items.AddRange(FindObjectsOfType<WorkItem>());
        }

        _activeMaxConcurrentBroken = int.MaxValue;
        _activeBreakMin = 4f;
        _activeBreakMax = 10f;
        _performanceScoreRaw = 0f;
        _performanceScoreRawWhenEnteredCurrentPhase = 0f;
        _activeWarningShowDelayAfterBreak = Mathf.Max(0f, tutorialWarningShowDelayAfterBreak);
        _activeWarningMessageFormat = string.IsNullOrEmpty(tutorialWarningMessageFormat)
            ? "{0} is broken!"
            : tutorialWarningMessageFormat;

        _victoryCountdownRemaining = performanceCountdownSeconds;
        _gameTimeRemaining = surviveTime;

        if (ui != null)
        {
            ui.InitWorkSlider(maxWork);
            ui.SetWork(work);
            ui.SetTime(_victoryCountdownRemaining);
            ui.HideAllResultPanels();
        }

        if (screenTint != null)
            screenTint.SetTarget(0f, 0.01f);

        FreezeFailures = false;

        if (gamePhases != null && gamePhases.Count > 0)
        {
            _currentPhaseIndex = 0;
            ApplyPhaseConfig(gamePhases[0]);
            _normalPerformanceScoringActive = !enableTutorial;
            SyncPhasePromotionArmedToCurrentThreshold();
        }
        else
        {
            _normalPerformanceScoringActive = false;
            _activeWorkPressureInstantOnBroke = workPressureInstantOnBroke;
            _activeWorkPressureInstantOnBrokeRepair = workPressureInstantOnBrokeRepair;
            _activeSortWorkReduction = workReductionPerCorrectSort;
        }

        if (enableTutorial)
        {
            SetAllWorkItemsAutoBreak(false);
            _tutorialCoroutine = StartCoroutine(TutorialBreakSequenceCoroutine());
        }
        else
        {
            _tutorialBreakSequenceDone = true;
            _allowRandomWorkItemFailures = true;
        }

        // Sync all WorkItems in list (covers objects registered before this Start and autoFind without RegisterItem)
        SetAllWorkItemsAutoBreak(_allowRandomWorkItemFailures);

        _bossLoopCoroutine = StartCoroutine(BossLoop());

        DebugLogPhasePromotionAtGameStart();

        if (brokenWarningIconImage != null)
            _defaultBrokenWarningIconSprite = brokenWarningIconImage.sprite;
        if (brokenWarningShakeTarget != null)
            _brokenWarningShakeBaseLocalPos = brokenWarningShakeTarget.localPosition;

        HideVictoryNearEndWarning();
    }

    void DebugLogPhasePromotionAtGameStart()
    {
        if (!debugLogPhaseAndScore)
            return;

        float hyst = GetScorePhasePromotionHysteresis();
        float div = bossIncomingConfig != null
            ? Mathf.Max(1e-4f, bossIncomingConfig.performanceScoreNormalizationDivisor)
            : 1000f;

        string phaseBlock;
        if (gamePhases == null || gamePhases.Count == 0)
            phaseBlock = "No gamePhases configured → no phase config, no score promotion.";
        else if (gamePhases.Count <= 1)
            phaseBlock = $"gamePhases has only 1 entry → no score promotion. Current phase index={_currentPhaseIndex}.";
        else
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Phase count={gamePhases.Count}, current phase index={_currentPhaseIndex} (0-based).");
            sb.AppendLine("Promotion thresholds and hysteresis per step (leave index → next; norm = TotalPerformanceScore / divisor):");
            for (int i = 0; i < gamePhases.Count - 1; i++)
            {
                float t = GetPromotionThresholdForLeavingPhase(i);
                sb.AppendLine(
                    $"  index {i}→{i + 1}: norm ≥ {t:F3} (~raw score ≥ {t * div:F1} / {div:F1}); hysteresis: drop below {t - hyst:F3} then rise past {t:F3}.");
            }

            float defSat = bossIncomingConfig != null
                ? bossIncomingConfig.defaultMinPerformanceScoreGainPerPhaseForSaturatedPromotion
                : 0f;
            sb.AppendLine(
                $"If norm is saturated and hysteresis cannot promote: each phase needs raw score gain since entering phase ≥ per-phase config (0 uses Boss default {defSat:F1}); and ≥{MinSecondsBetweenScorePhasePromotionsWhenSaturated:F2}s between steps. Boss default 0 disables saturated timed chain promotion.");
            phaseBlock = sb.ToString().TrimEnd();
        }

        float thr0 = gamePhases != null && gamePhases.Count > 1
            ? GetPromotionThresholdForLeavingPhase(0)
            : GetFallbackScorePhasePromotionThreshold();
        float rawAtThr0 = thr0 * div;

        Debug.Log(
            "[GameManager/PhaseScore] ========== start: phase promotion & score ==========\n" +
            phaseBlock + "\n" +
            $"Normalize: norm = Clamp01(TotalPerformanceScore / divisor); divisor={div:F1}, raw={TotalPerformanceScore:F2}, norm={NormalizedPerformanceScore:F3}.\n" +
            $"Phase 0 promotion threshold norm≥{thr0:F3} ≈ raw≥{rawAtThr0:F1}.\n" +
            $"Scoring active _normalPerformanceScoringActive = {_normalPerformanceScoringActive}" +
            (enableTutorial ? " (false until tutorial ends when tutorial on)" : "") + "\n" +
            "==========================================",
            this);
    }

    /// <summary>Called by WorkItem after successful Broke repair; logs current score for debugging.</summary>
    public void DebugLogPerformanceAfterSuccessfulRepair(string repairedItemDisplayName)
    {
        if (!debugLogPhaseAndScore)
            return;

        int phaseCount = gamePhases != null ? gamePhases.Count : 0;
        string phaseStr = phaseCount > 0
            ? $"phase {_currentPhaseIndex + 1}/{phaseCount} (index {_currentPhaseIndex}, last={phaseCount - 1})"
            : "no phase list";
        Debug.Log(
            $"[GameManager/Score] repaired \"{repairedItemDisplayName}\" → " +
            $"raw={TotalPerformanceScore:F2} | norm={NormalizedPerformanceScore:F3} | {phaseStr}",
            this);
    }

    void Update()
    {
        if (isGameOver || IsVictory) return;
        if (_tutorialBreakSequenceDone)
        {
            isTutorialing = false;
            _gameTimeRemaining -= Time.deltaTime;   // 实际游戏时长倒数
        }

        if (!VictoryCountdownPausedForTutorial)
            _victoryCountdownRemaining -= Time.deltaTime;   // 演出展示倒数（纯显示）

        if (!_victoryNearEndWarningShown && enableVictoryNearEndWarning && nearEndWarningWhenRemainingSeconds > 0f
            && _tutorialBreakSequenceDone
            && _gameTimeRemaining > 0f && _gameTimeRemaining <= nearEndWarningWhenRemainingSeconds
            && (nearEndWarningPanelRoot != null || nearEndWarningText != null))
        {
            TryShowVictoryNearEndWarning();
        }

        if (_gameTimeRemaining <= 0f && _tutorialBreakSequenceDone)
        {
            HideVictoryNearEndWarning();
            TriggerVictory();
            return;
        }

        UpdatePhaseAdvanceByScore();

        TickBossWarningUiFreezeAndShake();

        int working = 0;
        int broken = 0;
        int baiting = 0;
        int phoneBaitDecay = 0;

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == null) continue;

            WorkItem wi = items[i];
            if (wi.IsBroken)
            {
                broken++;
                if (!_itemBrokenWarningDelays.ContainsKey(wi) && !SuppressBrokenItemWarningDuringTutorial())
                    StartBrokenWarningForItem(wi);
            }
            else
            {
                if (_itemBrokenWarningDelays.ContainsKey(wi))
                    ClearBrokenWarningForItem(wi);
                if (wi.IsBaiting)
                {
                    baiting++;
                    if (wi.ShouldApplyPhoneBaitPerformanceDecay)
                        phoneBaitDecay++;
                }
                else working++;
            }
        }

        work += broken * workLossPerSecondPerBrokenItem * Time.deltaTime;
        work -= working * workGainPerSecondPerWorkingItem * Time.deltaTime;
        work += workBaseIncreasePerSecond * Time.deltaTime;

        work = Mathf.Clamp(work, 0f, maxWork);

        if (_normalPerformanceScoringActive)
        {
            BossIncomingConfig.PhaseScoreSettings ps = GetPhaseScoreSettings();
            _performanceScoreRaw -= broken * ps.scoreDecayPerSecond * Time.deltaTime;
            float phoneBaitDecayPerItemPerSec = ps.scoreDecayPerSecond * Mathf.Max(0f, ps.phoneBaitScoreDecayMultiplier);
            _performanceScoreRaw -= phoneBaitDecay * phoneBaitDecayPerItemPerSec * Time.deltaTime;
        }

        if (BossIsHere && !_bossBrokeCheckAwaitingArrivalSprites && broken > 0)
        {
            GameOver("Boss saw hacked items!");
            return;
        }

        if (ui != null)
        {
            ui.SetWork(work);
            ui.SetTime(_victoryCountdownRemaining);
        }
    }

    static bool HasNextNonNullTutorialWorkItem(List<WorkItem> order, int currentIndex)
    {
        if (order == null)
            return false;
        for (int j = currentIndex + 1; j < order.Count; j++)
        {
            if (order[j] != null)
                return true;
        }
        return false;
    }

    static string DebugLabelForWorkItem(WorkItem wi)
    {
        if (wi == null) return "(null)";
        string n = wi.itemName;
        if (!string.IsNullOrWhiteSpace(n))
            return n.Trim();
        return wi.name;
    }

    IEnumerator TutorialBreakSequenceCoroutine()
    {
        yield return null;

        List<WorkItem> order = tutorialBreakOrder != null && tutorialBreakOrder.Count > 0
            ? tutorialBreakOrder
            : items;

        bool appliedPreBossIconHold = false;
        for (int i = 0; i < order.Count; i++)
        {
            WorkItem wi = order[i];
            if (wi == null) continue;

            while (wi.IsBaiting)
                yield return null;

            if (!appliedPreBossIconHold)
            {
                appliedPreBossIconHold = true;
                BeginPreFirstBossBrokenWarningIconHold();
            }

            if (debugLogTutorialPhoneBreakFlow)
            {
                string lab = DebugLabelForWorkItem(wi);
                int lastIdx = Mathf.Max(0, order.Count - 1);
                Debug.Log(
                    $"[GameManager/Tutorial] 即将 tutorial Break() | orderIndex={i}/{lastIdx} item={lab} " +
                    $"IsBroken={wi.IsBroken} IsBaiting={wi.IsBaiting} — 若 IsBaiting 为 true，WorkItem.Break() 将直接 return（无 OnBroken/无串口 ANOMALY）",
                    this);
            }

            wi.Break();
            yield return new WaitUntil(() => !wi.IsBroken && !wi.IsBaiting);

            float gap = Mathf.Max(0f, tutorialDelayAfterRepairBeforeNextBreak);
            if (HasNextNonNullTutorialWorkItem(order, i))
            {
                if (i == phonePlacementInt - 1)
                {
                    if (phoneTutStarterObject != null)
                    {
                        _phoneTutorialLastLineCompleted = false;
                        if (debugLogTutorialPhoneBreakFlow)
                        {
                            WorkItem nextWi = i + 1 < order.Count ? order[i + 1] : null;
                            string nextLab = DebugLabelForWorkItem(nextWi);
                            Debug.Log(
                                $"[GameManager/Tutorial] 电话教程段：已打开 phoneTutStarterObject，等待末句 ShowNextBox (RegisterPhoneTutorialLastLineCompleted)。" +
                                $"下一轮将 Break 的工位应为: {nextLab} (下标 {i + 1})",
                                this);
                        }
                        phoneTutStarterObject.SetActive(true);
                        yield return new WaitUntil(() => _phoneTutorialLastLineCompleted);
                        if (debugLogTutorialPhoneBreakFlow)
                            Debug.Log("[GameManager/Tutorial] 已收到末句门闩，继续教程循环 → 下一轮将对该工位 Break()", this);
                    }
                }
                else if (gap > 0f)
                {
                    yield return new WaitForSeconds(gap);
                }
            }

        }

        _immediateBossAfterTutorial = true;
        _tutorialBreakSequenceDone = true;
        _tutorialCoroutine = null;

        if (gamePhases != null && gamePhases.Count > 0)
        {
            _normalPerformanceScoringActive = true;
            SyncPhasePromotionArmedToCurrentThreshold();
            _timeSinceLastScorePhasePromotion = 0f;
        }
    }

    void DeactivateTutorialUiObjectsForFirstBossWarning(bool firstBossAfterTutorial)
    {
        if (!enableTutorial || !firstBossAfterTutorial)
            return;
        if (deactivateOnFirstBossWarningAfterTutorial == null || deactivateOnFirstBossWarningAfterTutorial.Length == 0)
            return;

        for (int i = 0; i < deactivateOnFirstBossWarningAfterTutorial.Length; i++)
        {
            GameObject go = deactivateOnFirstBossWarningAfterTutorial[i];
            if (go != null)
                go.SetActive(false);
        }
    }

    IEnumerator BossLoop()
    {
        while (!isGameOver && !IsVictory)
        {
            if (enableTutorial && !_tutorialBreakSequenceDone)
            {
                yield return null;
                continue;
            }

            BossIncomingConfig cfg = bossIncomingConfig;
            if (cfg != null)
                cfg.SanitizeAllTriggerTimingRanges();

            float cooldown = cfg != null ? cfg.cooldownDuration : 0f;
            float warnDur = cfg != null
                ? Random.Range(cfg.bossWarningDurationMin, cfg.bossWarningDurationMax)
                : Random.Range(2f, 4f);
            float stayDur = cfg != null ? cfg.bossStayDuration : 6f;

            if (_applyCooldownBeforeNextBossWait)
            {
                if (cooldown > 0f)
                {
                    float cdLeft = cooldown;
                    while (cdLeft > 0f && !isGameOver && !IsVictory)
                    {
                        cdLeft -= Time.deltaTime;
                        yield return null;
                    }
                }
            }

            if (isGameOver || IsVictory) yield break;

            bool firstBossAfterTutorial = _immediateBossAfterTutorial;
            if (_immediateBossAfterTutorial)
            {
                _immediateBossAfterTutorial = false;
            }
            else
            {
                // 等待 work 条触发 Boss：进入阈值区间后按概率触发；到满则直接触发
                while (!isGameOver && !IsVictory)
                {
                    float norm = maxWork > 0.0001f ? Mathf.Clamp01(work / maxWork) : 0f;
                    if (norm >= 1f)
                        break; // work 满 → 立即触发
                    if (norm >= bossWorkTriggerNormalizedMin)
                    {
                        float p = bossWorkTriggerProbabilityPerSecond * Time.deltaTime;
                        if (Random.value < p)
                            break;
                    }
                    yield return null;
                }
            }

            if (isGameOver || IsVictory) yield break;

            BossWarning = true;
            BossWarningTimeLeft = warnDur;
            DeactivateTutorialUiObjectsForFirstBossWarning(firstBossAfterTutorial);
            OnBossWarningPhaseStarted();
            OnBossWarningStarted?.Invoke();

            if (screenTint != null)
                screenTint.SetTarget(1f, warnDur);

            while (BossWarningTimeLeft > 0f && !isGameOver && !IsVictory)
            {
                BossWarningTimeLeft -= Time.deltaTime;
                yield return null;
            }

            BossWarning = false;
            BossWarningTimeLeft = 0f;
            OnBossWarningPhaseEnded();

            if (isGameOver || IsVictory) yield break;

            BossIsHere = true;
            EndPreFirstBossBrokenWarningIconHold();
            _bossBrokeCheckAwaitingArrivalSprites = false;
            OnBossArrived?.Invoke();

            if (screenTint != null)
                screenTint.SetTarget(0.35f, 0.15f);

            // Boss 到场后将 work 条渐变归零（与 Boss 停留同步进行）
            if (bossArrivalWorkResetDuration > 0.001f)
                StartCoroutine(GradualWorkResetCoroutine(bossArrivalWorkResetDuration));
            else
            {
                work = 0f;
                if (ui != null) ui.SetWork(work);
            }

            float stay = stayDur;
            while (stay > 0f && !isGameOver && !IsVictory)
            {
                stay -= Time.deltaTime;
                yield return null;
            }

            if (isGameOver || IsVictory) yield break;

            OnBossLeaveStarted?.Invoke();

            float leaveHold = cfg != null ? Mathf.Max(0f, cfg.bossLeaveAnimationDuration) : 0f;
            while (leaveHold > 0f && !isGameOver && !IsVictory)
            {
                leaveHold -= Time.unscaledDeltaTime;
                yield return null;
            }

            if (isGameOver || IsVictory) yield break;

            BossIsHere = false;
            _bossBrokeCheckAwaitingArrivalSprites = false;
            OnBossLeft?.Invoke();

            if (screenTint != null)
                screenTint.SetTarget(0f, 0.6f);

            if (isGameOver || IsVictory) yield break;

            HandleBossLeftPhaseTransition();
            _applyCooldownBeforeNextBossWait = true;
        }
    }

    void HandleBossLeftPhaseTransition()
    {
        if (!_firstBossLeaveHandled)
        {
            _firstBossLeaveHandled = true;
            if (enableTutorial)
            {
                _allowRandomWorkItemFailures = true;
                SetAllWorkItemsAutoBreak(true);
            }

            return;
        }
    }

    void UpdatePhaseAdvanceByScore()
    {
        if (gamePhases == null || gamePhases.Count <= 1)
            return;
        if (!_normalPerformanceScoringActive)
            return;

        _timeSinceLastScorePhasePromotion += Time.deltaTime;

        // Allow multiple phase promotions in one frame (avoids getting stuck when norm already passed the next threshold but hysteresis never re-arms)
        while (_currentPhaseIndex < gamePhases.Count - 1)
        {
            float thr = GetPromotionThresholdForLeavingPhase(_currentPhaseIndex);
            float h = GetScorePhasePromotionHysteresis();
            float norm = NormalizedPerformanceScore;
            float lowerBound = thr - h;

            // If threshold <= hysteresis, thr−h <= 0 makes norm < lowerBound never true → could never arm; fall back to arming when norm >= thr only
            if (lowerBound > 1e-4f)
            {
                if (norm < lowerBound)
                    _phasePromotionArmed = true;
            }
            else
                _phasePromotionArmed = true;

            const float normSaturated = 0.998f;
            bool saturated = norm >= normSaturated;

            bool promoteByHysteresis = lowerBound > 1e-4f
                ? _phasePromotionArmed && norm >= thr
                : norm >= thr;

            float minPerfGainForSaturated = GetMinPerformanceScoreGainThisPhaseForSaturatedPromotion(_currentPhaseIndex);
            float perfSinceEnteredPhase = TotalPerformanceScore - _performanceScoreRawWhenEnteredCurrentPhase;
            bool promoteBySaturatedInterval = saturated && norm >= thr
                && minPerfGainForSaturated > 0.0001f
                && perfSinceEnteredPhase >= minPerfGainForSaturated
                && _timeSinceLastScorePhasePromotion >= MinSecondsBetweenScorePhasePromotionsWhenSaturated;

            if (!promoteByHysteresis && !promoteBySaturatedInterval)
                break;

            _currentPhaseIndex++;
            ApplyPhaseConfig(gamePhases[_currentPhaseIndex]);
            // New phase: allow crossing the next threshold immediately without score dipping below (threshold − hysteresis) first (avoids deadlock with stacked thresholds)
            _phasePromotionArmed = true;
            _timeSinceLastScorePhasePromotion = 0f;
        }
    }

    /// <summary>Normalized score threshold to leave gamePhases[phaseIndex] for the next phase.</summary>
    float GetPromotionThresholdForLeavingPhase(int phaseIndex)
    {
        if (gamePhases == null || phaseIndex < 0 || phaseIndex >= gamePhases.Count)
            return GetFallbackScorePhasePromotionThreshold();

        GamePhaseConfig c = gamePhases[phaseIndex];
        if (c == null)
            return GetFallbackScorePhasePromotionThreshold();

        if (c.normalizedScoreRequiredForNextPhase <= 0.0001f)
            return GetFallbackScorePhasePromotionThreshold();

        return Mathf.Clamp01(c.normalizedScoreRequiredForNextPhase);
    }

    float GetMinPerformanceScoreGainThisPhaseForSaturatedPromotion(int phaseIndex)
    {
        if (gamePhases == null || phaseIndex < 0 || phaseIndex >= gamePhases.Count)
            return 0f;
        GamePhaseConfig c = gamePhases[phaseIndex];
        if (c == null)
            return 0f;

        float perPhase = Mathf.Max(0f, c.minPerformanceScoreGainThisPhaseForSaturatedPromotion);
        if (perPhase > 0.0001f)
            return perPhase;

        if (bossIncomingConfig != null)
            return Mathf.Max(0f, bossIncomingConfig.defaultMinPerformanceScoreGainPerPhaseForSaturatedPromotion);

        return 0f;
    }

    float GetFallbackScorePhasePromotionThreshold()
    {
        if (bossIncomingConfig != null)
            return Mathf.Clamp01(bossIncomingConfig.scoreThresholdForNextPhase);
        return 0.55f;
    }

    /// <summary>
    /// Align hysteresis "armed" state with current threshold: norm must drop below (threshold − hysteresis) before it can promote past threshold again.
    /// Avoids mistaken promotion when norm vs threshold is odd at start; call when tutorial ends and scoring turns on.
    /// </summary>
    void SyncPhasePromotionArmedToCurrentThreshold()
    {
        if (gamePhases == null || gamePhases.Count <= 1)
        {
            _phasePromotionArmed = false;
            return;
        }

        if (_currentPhaseIndex >= gamePhases.Count - 1)
        {
            _phasePromotionArmed = false;
            return;
        }

        float thr = GetPromotionThresholdForLeavingPhase(_currentPhaseIndex);
        float h = GetScorePhasePromotionHysteresis();
        float lowerBound = thr - h;
        if (lowerBound <= 1e-4f)
            _phasePromotionArmed = true;
        else
            _phasePromotionArmed = NormalizedPerformanceScore < lowerBound;
    }

    float GetScorePhasePromotionHysteresis()
    {
        if (bossIncomingConfig != null)
            return Mathf.Clamp(bossIncomingConfig.scorePhasePromotionHysteresis, 0.005f, 0.3f);
        return 0.03f;
    }

    void ApplyPhaseConfig(GamePhaseConfig c)
    {
        if (c == null) return;

        _performanceScoreRawWhenEnteredCurrentPhase = _performanceScoreRaw;

        _activeMaxConcurrentBroken = Mathf.Max(1, c.maxConcurrentBrokenWorkItems);
        _activeBreakMin = c.minBreakIntervalSeconds;
        _activeBreakMax = Mathf.Max(c.minBreakIntervalSeconds, c.maxBreakIntervalSeconds);
        bossMinWorkThreshold = c.bossMinWorkThreshold;
        workBaseIncreasePerSecond = c.workBaseIncreasePerSecond;
        _activeSortWorkReduction = c.workReductionPerCorrectSort;
        workPunishment = c.workPunishment;
        workUltraPunishment = c.workUltraPunishment;
        workGainPerSecondPerWorkingItem = c.workGainPerSecondPerWorkingItem;
        workLossPerSecondPerBrokenItem = c.workLossPerSecondPerBrokenItem;
        float instB = Mathf.Max(0f, c.workPressureInstantOnBroke);
        _activeWorkPressureInstantOnBroke = instB > 0.0001f ? instB : workPressureInstantOnBroke;
        float instR = Mathf.Max(0f, c.workPressureInstantOnBrokeRepair);
        _activeWorkPressureInstantOnBrokeRepair = instR > 0.0001f ? instR : workPressureInstantOnBrokeRepair;
        _activeWarningShowDelayAfterBreak = Mathf.Max(0f, c.warningShowDelayAfterBreak);
        _activeWarningMessageFormat = string.IsNullOrEmpty(c.warningMessageFormat)
            ? "{0} is broken!"
            : c.warningMessageFormat;

    }

    /// <summary>When true, WorkItem auto-break intervals use phase config; otherwise each WorkItem's Inspector values.</summary>
    public bool UsePhaseBreakTiming()
    {
        if (gamePhases == null || gamePhases.Count == 0)
            return false;
        if (!enableTutorial)
            return true;
        return _tutorialBreakSequenceDone;
    }

    /// <summary>For WorkItem auto-failures: whether another Broke (not Bait) may start.</summary>
    public bool CanStartNewBrokeState()
    {
        if (isGameOver || IsVictory) return false;

        int broken = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null && items[i].IsBroken) broken++;
        }

        bool inBossArrivalPhase = BossWarning || BossIsHere;
        if (inBossArrivalPhase && bossIncomingConfig != null && bossIncomingConfig.enableBossPhaseHackLimit)
            return broken < Mathf.Max(1, bossIncomingConfig.maxHackedItemsDuringBoss);

        return broken < _activeMaxConcurrentBroken;
    }

    /// <summary>No new Hack (Broke) during pre-arrival freeze window before Boss.</summary>
    public bool BlockNewHackEventsNow()
    {
        if (bossIncomingConfig == null || !bossIncomingConfig.enablePreArrivalFreeze || !BossWarning)
            return false;
        float w = Mathf.Max(0f, bossIncomingConfig.freezeWindowBeforeArrival);
        return BossWarningTimeLeft <= w;
    }

    /// <summary>No new Broke during Boss stay check; Bait may still roll from WorkItems.</summary>
    public bool BlockNewBrokeDuringBossStay()
    {
        if (isGameOver || IsVictory) return false;
        return BossIsHere;
    }

    /// <summary>BossArrivalUISprite: set true before switching to Peek when second arrival sprite is configured; false after transition.</summary>
    public void SetBossBrokeCheckAwaitingArrivalSprites(bool awaiting)
    {
        _bossBrokeCheckAwaitingArrivalSprites = awaiting;
    }

    /// <summary>When WorkItem enters Hacked (Broke); adds phase baseScore and triggers Boss window hack performance.</summary>
    public void OnWorkItemEnteredHackedState(WorkItem item)
    {
        if (item == null) return;

        if (_normalPerformanceScoringActive)
        {
            BossIncomingConfig.PhaseScoreSettings ps = GetPhaseScoreSettings();
            _performanceScoreRaw += ps.baseScore;
        }

        BossWindowPerformance.Instance?.TriggerHack();
    }

    public BossIncomingConfig.PhaseScoreSettings GetPhaseScoreSettings()
    {
        const float defBase = 100f;
        const float defDecay = 5f;
        if (bossIncomingConfig == null || bossIncomingConfig.phaseScoreSettings == null
            || bossIncomingConfig.phaseScoreSettings.Length == 0)
            return new BossIncomingConfig.PhaseScoreSettings
            {
                baseScore = defBase,
                scoreDecayPerSecond = defDecay,
                phoneBaitScoreDecayMultiplier = 1f,
                performancePenaltyIdleWrongHit = 15f,
                performancePenaltyBaitWrongHit = 30f
            };

        int idx = Mathf.Clamp(_currentPhaseIndex, 0, bossIncomingConfig.phaseScoreSettings.Length - 1);
        return bossIncomingConfig.phaseScoreSettings[idx];
    }

    float ComputeNormalizedPerformanceScore()
    {
        float div = bossIncomingConfig != null
            ? Mathf.Max(1e-4f, bossIncomingConfig.performanceScoreNormalizationDivisor)
            : 1000f;
        return Mathf.Clamp01(TotalPerformanceScore / div);
    }

    public float GetActiveBreakIntervalMin() => _activeBreakMin;
    public float GetActiveBreakIntervalMax() => _activeBreakMax;

    void SetAllWorkItemsAutoBreak(bool on)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null)
                items[i].enableAutoBreak = on;
        }
    }

    void TriggerVictory()
    {
        if (isGameOver || IsVictory) return;
        IsVictory = true;
        SuppressNonBaitBrokeItemSfxFromPhonePickup = false;
        NotifyArduinoSystemResetOnMatchEnd();
        CleanupAllWorkItemsOnMatchEnd();

        if (_bossLoopCoroutine != null)
        {
            StopCoroutine(_bossLoopCoroutine);
            _bossLoopCoroutine = null;
        }
        if (_tutorialCoroutine != null)
        {
            StopCoroutine(_tutorialCoroutine);
            _tutorialCoroutine = null;
        }

        BossWarning = false;
        BossIsHere = false;
        _bossBrokeCheckAwaitingArrivalSprites = false;
        FreezeFailures = false;

        if (screenTint != null)
            screenTint.SetTarget(0f, 0.1f);

        HideVictoryNearEndWarning();
        ShutdownBrokenWarningSystem();

        virusPurgeMeter?.OnVictory();

        Time.timeScale = 0f;
        _victoryCinematicCoroutine = StartCoroutine(VictoryCinematicThenPanelCoroutine());
    }

    void TryShowVictoryNearEndWarning()
    {
        if (_victoryNearEndWarningShown) return;
        if (nearEndWarningPanelRoot == null && nearEndWarningText == null)
            return;

        _victoryNearEndWarningShown = true;
        string msg = nearEndWarningMessage != null ? nearEndWarningMessage : "";
        if (nearEndWarningText != null)
        {
            nearEndWarningText.text = msg;
            nearEndWarningText.ForceMeshUpdate();
        }

        if (nearEndWarningPanelRoot != null)
            nearEndWarningPanelRoot.SetActive(true);
        else if (nearEndWarningText != null)
            nearEndWarningText.gameObject.SetActive(true);
    }

    void HideVictoryNearEndWarning()
    {
        if (nearEndWarningPanelRoot != null)
            nearEndWarningPanelRoot.SetActive(false);
        else if (nearEndWarningText != null)
            nearEndWarningText.gameObject.SetActive(false);
    }

    public void Punishment()
    {
        work += workPunishment;
        ApplyWrongRepairPerformancePenalty(baitWrongHit: false);
        ClampWorkProgressAndMaybeLose();
    }

    public void UltraPunishment()
    {
        work += workUltraPunishment;
        ApplyWrongRepairPerformancePenalty(baitWrongHit: true);
        ClampWorkProgressAndMaybeLose();
    }

    void ApplyWrongRepairPerformancePenalty(bool baitWrongHit)
    {
        if (!_normalPerformanceScoringActive || isGameOver || IsVictory)
            return;

        BossIncomingConfig.PhaseScoreSettings ps = GetPhaseScoreSettings();
        float deduct = baitWrongHit ? ps.performancePenaltyBaitWrongHit : ps.performancePenaltyIdleWrongHit;
        if (deduct <= 0f)
            return;

        _performanceScoreRaw -= deduct;
    }

    public void Reward()
    {
        work -= workMashGain;
        work = Mathf.Max(0f, work);
    }

    /// <summary>返回当前 phase 激活的分类减少值；GameManager 未就绪时返回 fallback。</summary>
    public float GetActiveSortWorkReduction(float fallback = 5f)
    {
        return gamePhases != null && gamePhases.Count > 0
            ? _activeSortWorkReduction
            : fallback;
    }

    /// <summary>When WorkItem enters Broke; instant work bar spike.</summary>
    public void ApplyWorkPressureOnItemBroke()
    {
        if (isGameOver || IsVictory) return;
        work += _activeWorkPressureInstantOnBroke;
        ClampWorkProgressAndMaybeLose();
    }

    /// <summary>When player repairs Broke; called from WorkItem, instant work bar relief.</summary>
    public void ApplyWorkPressureOnBrokeRepaired()
    {
        if (isGameOver || IsVictory) return;
        work -= _activeWorkPressureInstantOnBrokeRepair;
        work = Mathf.Max(0f, work);
    }

    void ClampWorkProgressAndMaybeLose()
    {
        if (isGameOver || IsVictory) return;
        work = Mathf.Clamp(work, 0f, maxWork);
    }

    void GameOverWorkProgressFull()
    {
        if (isGameOver || IsVictory)
        {
            if (debugLogWorkProgressDeath)
                Debug.Log("[GameManager] GameOverWorkProgressFull skipped: already game over or victory (e.g. another fail this frame).", this);
            return;
        }

        isGameOver = true;
        SuppressNonBaitBrokeItemSfxFromPhonePickup = false;
        NotifyArduinoSystemResetOnMatchEnd();
        CleanupAllWorkItemsOnMatchEnd();

        if (debugLogWorkProgressDeath)
        {
            Debug.Log(
                "[GameManager] Work bar full → death (GameOverWorkProgressFull). " +
                $"work={work:F2}/{maxWork:F2}, elapsed={surviveTime - Mathf.Max(0f, _gameTimeRemaining):F1}s, performanceScore={TotalPerformanceScore:F1}, " +
                $"UIManager={(ui != null ? "bound" : "null")}",
                this);
        }

        if (_bossLoopCoroutine != null)
        {
            StopCoroutine(_bossLoopCoroutine);
            _bossLoopCoroutine = null;
        }

        if (_tutorialCoroutine != null)
        {
            StopCoroutine(_tutorialCoroutine);
            _tutorialCoroutine = null;
        }

        BossWarning = false;
        BossIsHere = false;
        _bossBrokeCheckAwaitingArrivalSprites = false;
        FreezeFailures = false;

        if (screenTint != null)
            screenTint.SetTarget(0f, 0.1f);

        HideVictoryNearEndWarning();
        ShutdownBrokenWarningSystem();

        virusPurgeMeter?.OnBossFail();

        if (ui != null)
        {
            if (virusDiePrePanelClip != null && virusDiePrePanelClip.length > 0.0001f)
            {
                Time.timeScale = 0f;

                if (_virusDieRevealPanelRoutine != null)
                {
                    StopCoroutine(_virusDieRevealPanelRoutine);
                    _virusDieRevealPanelRoutine = null;
                }

                AudioSource src = virusDiePrePanelAudioSource;
                if (src == null)
                    src = GetComponent<AudioSource>();
                if (src == null)
                {
                    Debug.LogWarning(
                        "[GameManager] virusDiePrePanelClip is set but no AudioSource: assign virusDiePrePanelAudioSource or add AudioSource on GameManager. Showing lose panel immediately.",
                        this);
                    if (debugLogWorkProgressDeath)
                        Debug.Log("[GameManager] GameOverWorkProgressFull → ShowWorkProgressLose (no stinger source)", ui);
                    ui.ShowWorkProgressLose(surviveTime - Mathf.Max(0f, _gameTimeRemaining), work, TotalPerformanceScore, maxWork);
                }
                else
                {
                    src.ignoreListenerPause = true;
                    src.playOnAwake = false;
                    src.PlayOneShot(virusDiePrePanelClip);
                    if (debugLogWorkProgressDeath)
                        Debug.Log(
                            "[GameManager] GameOverWorkProgressFull → stinger then delayed ShowWorkProgressLose | " +
                            $"clipLen={virusDiePrePanelClip.length:F2}s",
                            this);
                    _virusDieRevealPanelRoutine = StartCoroutine(
                        VirusDieRevealPanelAfterStingerRealtime(surviveTime - Mathf.Max(0f, _gameTimeRemaining), work, TotalPerformanceScore, maxWork, virusDiePrePanelClip.length));
                }
            }
            else
            {
                if (debugLogWorkProgressDeath)
                    Debug.Log(
                        "[GameManager] GameOverWorkProgressFull → calling ShowWorkProgressLose | " +
                        $"UIManager.instanceID={ui.GetInstanceID()} GameObject=\"{ui.gameObject.name}\" scene={ui.gameObject.scene.name}",
                        ui);
                ui.ShowWorkProgressLose(surviveTime - Mathf.Max(0f, _gameTimeRemaining), work, TotalPerformanceScore, maxWork);
                if (debugLogWorkProgressDeath)
                    Debug.Log("[GameManager] ShowWorkProgressLose returned (no exception)", ui);

                Time.timeScale = 0f;
            }
        }
        else
            Time.timeScale = 0f;
#if UNITY_EDITOR
        if (ui == null)
            Debug.LogWarning("[GameManager] GameOverWorkProgressFull: UIManager (ui) not assigned; cannot show work-pressure lose panel.", this);
#endif
    }

    IEnumerator VirusDieRevealPanelAfterStingerRealtime(float surviveTime, float finalWork, float performanceScore, float maxWorkProgress, float waitSeconds)
    {
        if (waitSeconds > 0f)
            yield return new WaitForSecondsRealtime(waitSeconds);

        _virusDieRevealPanelRoutine = null;

        if (ui != null)
            ui.ShowWorkProgressLose(surviveTime, finalWork, performanceScore, maxWorkProgress);
    }

    public void RegisterItem(WorkItem item)
    {
        if (item == null) return;
        if (!items.Contains(item)) items.Add(item);
        item.enableAutoBreak = _allowRandomWorkItemFailures;
    }

    public void UnregisterItem(WorkItem item)
    {
        if (item == null) return;
        items.Remove(item);
        ClearBrokenWarningForItem(item);
    }

    static string WorkItemDisplayName(WorkItem w)
    {
        if (w == null) return "";
        if (!string.IsNullOrWhiteSpace(w.itemName)) return w.itemName.Trim();
        return w.name;
    }

    bool SuppressBrokenItemWarningDuringTutorial()
    {
        return enableTutorial && !_tutorialBreakSequenceDone;
    }

    void BeginPreFirstBossBrokenWarningIconHold()
    {
        if (brokenWarningIconImage == null || brokenWarningActiveSprite == null)
            return;
        _holdBrokenWarningIconActiveUntilFirstBossArrived = true;
        brokenWarningIconImage.sprite = brokenWarningActiveSprite;
    }

    void EndPreFirstBossBrokenWarningIconHold()
    {
        if (!_holdBrokenWarningIconActiveUntilFirstBossArrived)
            return;
        _holdBrokenWarningIconActiveUntilFirstBossArrived = false;
        RestoreBrokenWarningIconDefault();
    }

    void StartBrokenWarningForItem(WorkItem item)
    {
        if (item == null || brokenWarningPanelRoot == null || brokenWarningText == null)
            return;
        if (SuppressBrokenItemWarningDuringTutorial())
            return;
        if (_itemBrokenWarningDelays.ContainsKey(item))
            return;

        Coroutine c = StartCoroutine(ItemBrokenWarningDelayRoutine(item));
        _itemBrokenWarningDelays[item] = c;
    }

    IEnumerator ItemBrokenWarningDelayRoutine(WorkItem item)
    {
        float delay = Mathf.Max(0f, _activeWarningShowDelayAfterBreak);
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        _itemBrokenWarningDelays.Remove(item);

        if (item == null || !item.IsBroken)
            yield break;

        if (BossWarning)
        {
            EnqueueBrokenWarningItem(item);
            yield break;
        }

        if (_displayedBrokenWarningItem != null)
        {
            EnqueueBrokenWarningItem(item);
            yield break;
        }

        ShowBrokenItemWarning(item, forceWarningIcon: _brokenWarningChainFresh);
    }

    void EnqueueBrokenWarningItem(WorkItem item)
    {
        if (item == null || !item.IsBroken) return;
        if (_brokenWarningReadyQueue.Contains(item)) return;
        _brokenWarningReadyQueue.Add(item);
    }

    void RemoveBrokenWarningItemFromQueue(WorkItem item)
    {
        if (item == null) return;
        _brokenWarningReadyQueue.Remove(item);
    }

    WorkItem PopNextBrokenWarningFromQueue()
    {
        while (_brokenWarningReadyQueue.Count > 0)
        {
            WorkItem w = _brokenWarningReadyQueue[0];
            _brokenWarningReadyQueue.RemoveAt(0);
            if (w != null && w.IsBroken)
                return w;
        }

        return null;
    }

    string FormatBrokenWarningTextForItem(WorkItem item)
    {
        string name = WorkItemDisplayName(item);
        try
        {
            return string.Format(_activeWarningMessageFormat, name);
        }
        catch (System.FormatException)
        {
            return $"{name} is broken!";
        }
    }

    void ShowBrokenItemWarning(WorkItem item, bool forceWarningIcon)
    {
        if (item == null || brokenWarningText == null || brokenWarningPanelRoot == null)
            return;

        _displayedBrokenWarningItem = item;
        brokenWarningText.text = FormatBrokenWarningTextForItem(item);
        brokenWarningText.ForceMeshUpdate();
        brokenWarningPanelRoot.SetActive(true);

        if (forceWarningIcon && brokenWarningIconImage != null && brokenWarningActiveSprite != null)
            brokenWarningIconImage.sprite = brokenWarningActiveSprite;

        if (forceWarningIcon)
            _brokenWarningChainFresh = false;
    }

    void RestoreBrokenWarningIconDefault()
    {
        if (_holdBrokenWarningIconActiveUntilFirstBossArrived)
            return;
        if (brokenWarningIconImage != null && _defaultBrokenWarningIconSprite != null)
            brokenWarningIconImage.sprite = _defaultBrokenWarningIconSprite;
    }

    void HideBrokenWarningPanelFullyIdle()
    {
        if (brokenWarningPanelRoot != null)
            brokenWarningPanelRoot.SetActive(false);
        RestoreBrokenWarningIconDefault();
        _brokenWarningChainFresh = true;
    }

    void HandleDisplayedBrokenWarningResolved()
    {
        _displayedBrokenWarningItem = null;

        if (BossWarning)
            return;

        WorkItem next = PopNextBrokenWarningFromQueue();
        if (next != null)
        {
            if (_brokenWarningInterPauseRoutine != null)
                StopCoroutine(_brokenWarningInterPauseRoutine);
            _brokenWarningInterPauseRoutine = StartCoroutine(InterBrokenWarningPauseThenShowNext(next));
        }
        else
        {
            HideBrokenWarningPanelFullyIdle();
        }
    }

    IEnumerator InterBrokenWarningPauseThenShowNext(WorkItem next)
    {
        if (brokenWarningPanelRoot != null)
            brokenWarningPanelRoot.SetActive(false);

        float pause = Mathf.Max(0f, interBrokenWarningPauseSeconds);
        if (pause > 0f)
            yield return new WaitForSeconds(pause);

        _brokenWarningInterPauseRoutine = null;

        if (BossWarning)
        {
            if (next != null && next.IsBroken)
                EnqueueBrokenWarningItem(next);
            yield break;
        }

        if (next == null || !next.IsBroken)
        {
            WorkItem alt = PopNextBrokenWarningFromQueue();
            if (alt != null)
                ShowBrokenItemWarning(alt, forceWarningIcon: false);
            else
                HideBrokenWarningPanelFullyIdle();
            yield break;
        }

        ShowBrokenItemWarning(next, forceWarningIcon: false);
    }

    void OnBossWarningPhaseStarted()
    {
        _resumeBrokenWarningAfterBoss = _displayedBrokenWarningItem;
        _displayedBrokenWarningItem = null;

        BossIncomingConfig cfg = bossIncomingConfig;
        if (cfg != null && cfg.enableITGuyWarning && !string.IsNullOrWhiteSpace(cfg.itGuyWarningMessage))
            _bossApproachWarningMessage = cfg.itGuyWarningMessage.Trim();
        else
            _bossApproachWarningMessage = "Boss approaching!";

        _bossWarningUiInFreezeHide = false;
        StopBossWarningShakeRoutine();
        ResetBrokenWarningShakeLocalPosition();

        if (brokenWarningText != null)
        {
            brokenWarningText.text = _bossApproachWarningMessage;
            brokenWarningText.ForceMeshUpdate();
        }

        if (BlockNewHackEventsNow())
            EnterBossWarningFreezeUiMode();
        else
        {
            brokenWarningPanelRoot?.SetActive(true);
            StartBossWarningShakeRoutineIfConfigured();
        }
    }

    void TickBossWarningUiFreezeAndShake()
    {
        if (!BossWarning)
        {
            if (_bossWarningUiInFreezeHide)
                _bossWarningUiInFreezeHide = false;
            return;
        }

        bool freeze = BlockNewHackEventsNow();
        if (freeze)
        {
            if (!_bossWarningUiInFreezeHide)
                EnterBossWarningFreezeUiMode();
        }
        else
        {
            if (_bossWarningUiInFreezeHide)
                ExitBossWarningFreezeUiMode();
        }
    }

    void EnterBossWarningFreezeUiMode()
    {
        _bossWarningUiInFreezeHide = true;
        StopBossWarningShakeRoutine();
        ResetBrokenWarningShakeLocalPosition();
        brokenWarningPanelRoot?.SetActive(false);
        RestoreBrokenWarningIconDefault();
    }

    void ExitBossWarningFreezeUiMode()
    {
        _bossWarningUiInFreezeHide = false;
        if (brokenWarningText != null && _bossApproachWarningMessage != null)
        {
            brokenWarningText.text = _bossApproachWarningMessage;
            brokenWarningText.ForceMeshUpdate();
        }

        brokenWarningPanelRoot?.SetActive(true);
    }

    void OnBossWarningPhaseEnded()
    {
        StopBossWarningShakeRoutine();
        ResetBrokenWarningShakeLocalPosition();
        _bossWarningUiInFreezeHide = false;

        if (_resumeBrokenWarningAfterBoss != null && _resumeBrokenWarningAfterBoss.IsBroken)
        {
            ShowBrokenItemWarning(_resumeBrokenWarningAfterBoss, forceWarningIcon: true);
            _resumeBrokenWarningAfterBoss = null;
        }
        else
        {
            _resumeBrokenWarningAfterBoss = null;
            WorkItem next = PopNextBrokenWarningFromQueue();
            if (next != null)
            {
                _brokenWarningChainFresh = true;
                ShowBrokenItemWarning(next, forceWarningIcon: true);
            }
            else
            {
                HideBrokenWarningPanelFullyIdle();
            }
        }
    }

    void StartBossWarningShakeRoutineIfConfigured()
    {
        if (brokenWarningShakeTarget == null) return;
        if (_bossWarningShakeRoutine != null) return;
        _bossWarningShakeRoutine = StartCoroutine(BossWarningShakeRoutine());
    }

    void StopBossWarningShakeRoutine()
    {
        if (_bossWarningShakeRoutine != null)
        {
            StopCoroutine(_bossWarningShakeRoutine);
            _bossWarningShakeRoutine = null;
        }

        ResetBrokenWarningShakeLocalPosition();
    }

    void ResetBrokenWarningShakeLocalPosition()
    {
        if (brokenWarningShakeTarget != null)
            brokenWarningShakeTarget.localPosition = _brokenWarningShakeBaseLocalPos;
    }

    IEnumerator BossWarningShakeRoutine()
    {
        RectTransform rt = brokenWarningShakeTarget;
        if (rt == null)
        {
            _bossWarningShakeRoutine = null;
            yield break;
        }

        float amp = Mathf.Max(0f, brokenWarningBossShakeAmplitude);
        float freq = Mathf.Max(0.01f, brokenWarningBossShakeFrequency);
        Vector3 basePos = _brokenWarningShakeBaseLocalPos;

        while (BossWarning && !BlockNewHackEventsNow() && !isGameOver && !IsVictory)
        {
            float t = Time.unscaledTime * freq;
            rt.localPosition = basePos + new Vector3(
                Mathf.Sin(t) * amp,
                Mathf.Sin(t * 1.71f) * amp * 0.55f,
                0f);
            yield return null;
        }

        rt.localPosition = basePos;
        _bossWarningShakeRoutine = null;
    }

    void ShutdownBrokenWarningSystem()
    {
        StopBossWarningShakeRoutine();
        if (_brokenWarningInterPauseRoutine != null)
        {
            StopCoroutine(_brokenWarningInterPauseRoutine);
            _brokenWarningInterPauseRoutine = null;
        }

        foreach (KeyValuePair<WorkItem, Coroutine> kv in _itemBrokenWarningDelays)
        {
            if (kv.Value != null)
                StopCoroutine(kv.Value);
        }

        _itemBrokenWarningDelays.Clear();
        _brokenWarningReadyQueue.Clear();
        _displayedBrokenWarningItem = null;
        _resumeBrokenWarningAfterBoss = null;
        _bossWarningUiInFreezeHide = false;
        _bossApproachWarningMessage = null;
        _holdBrokenWarningIconActiveUntilFirstBossArrived = false;

        if (brokenWarningPanelRoot != null)
            brokenWarningPanelRoot.SetActive(false);
        RestoreBrokenWarningIconDefault();
        ResetBrokenWarningShakeLocalPosition();
        _brokenWarningChainFresh = true;
    }

    void ClearBrokenWarningForItem(WorkItem item)
    {
        if (item == null)
            return;

        if (_itemBrokenWarningDelays.TryGetValue(item, out Coroutine delayC))
        {
            if (delayC != null)
                StopCoroutine(delayC);
            _itemBrokenWarningDelays.Remove(item);
        }

        RemoveBrokenWarningItemFromQueue(item);

        if (_resumeBrokenWarningAfterBoss == item)
            _resumeBrokenWarningAfterBoss = null;

        if (_displayedBrokenWarningItem == item)
            HandleDisplayedBrokenWarningResolved();
    }

    public void GameOver(string reason)
    {
        if (isGameOver || IsVictory) return;
        isGameOver = true;
        SuppressNonBaitBrokeItemSfxFromPhonePickup = false;
        NotifyArduinoSystemResetOnMatchEnd();
        CleanupAllWorkItemsOnMatchEnd();

        if (_bossLoopCoroutine != null)
        {
            StopCoroutine(_bossLoopCoroutine);
            _bossLoopCoroutine = null;
        }
        if (_tutorialCoroutine != null)
        {
            StopCoroutine(_tutorialCoroutine);
            _tutorialCoroutine = null;
        }

        BossWarning = false;
        BossIsHere = false;
        _bossBrokeCheckAwaitingArrivalSprites = false;

        FreezeFailures = false;

        bool bossCaused = reason != null && reason.Contains("Boss");
        if (bossCaused)
            OnGameOverBossCaused?.Invoke();

        if (screenTint != null)
            screenTint.SetTarget(0f, 0.1f);

        HideVictoryNearEndWarning();
        ShutdownBrokenWarningSystem();

        virusPurgeMeter?.OnBossFail();

        Time.timeScale = 0f;
        _bossFailCinematicCoroutine = StartCoroutine(BossFailCinematicThenPanelCoroutine(surviveTime - Mathf.Max(0f, _gameTimeRemaining), work, reason));
    }

    /// <summary>Boss 到场后将 work 条在 duration 秒内平滑插值至 0。</summary>
    IEnumerator GradualWorkResetCoroutine(float duration)
    {
        float startWork = work;
        float elapsed = 0f;
        while (elapsed < duration && !isGameOver && !IsVictory)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            work = Mathf.Lerp(startWork, 0f, t);
            if (ui != null) ui.SetWork(work);
            yield return null;
        }
        if (!isGameOver && !IsVictory)
        {
            work = 0f;
            if (ui != null) ui.SetWork(work);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Cinematic helpers
    // ─────────────────────────────────────────────────────────────────────────

    static bool HasAnyValidClip(List<AudioClip> clips)
    {
        if (clips == null) return false;
        for (int i = 0; i < clips.Count; i++)
            if (clips[i] != null) return true;
        return false;
    }

    AudioSource GetOrWarnCinematicAudioSource()
    {
        if (cinematicAudioSource != null)
            return cinematicAudioSource;
        AudioSource src = GetComponent<AudioSource>();
        if (src == null)
            Debug.LogWarning(
                "[GameManager] Cinematic clips are set but cinematicAudioSource is not assigned " +
                "and there is no AudioSource on this GameObject; audio will be skipped.", this);
        return src;
    }

    /// <summary>Plays each non-null clip in sequence using real-time waits (works even with timeScale = 0).</summary>
    IEnumerator PlayCinematicClipsRealtime(List<AudioClip> clips)
    {
        if (clips == null || clips.Count == 0) yield break;
        AudioSource src = GetOrWarnCinematicAudioSource();
        if (src == null) yield break;
        src.ignoreListenerPause = true;
        for (int i = 0; i < clips.Count; i++)
        {
            AudioClip clip = clips[i];
            if (clip == null) continue;
            src.PlayOneShot(clip);
            yield return new WaitForSecondsRealtime(clip.length);
        }
    }

    void ApplyCinematicSpriteSwaps(List<CinematicSpriteSwap> swaps)
    {
        if (swaps == null) return;
        for (int i = 0; i < swaps.Count; i++)
        {
            CinematicSpriteSwap s = swaps[i];
            if (s != null && s.targetImage != null && s.newSprite != null)
                s.targetImage.sprite = s.newSprite;
        }
    }

    void ApplyCinematicDestroyAndActivate(List<GameObject> destroys, List<GameObject> activates)
    {
        if (destroys != null)
            for (int i = 0; i < destroys.Count; i++)
                if (destroys[i] != null) Destroy(destroys[i]);
        if (activates != null)
            for (int i = 0; i < activates.Count; i++)
                if (activates[i] != null) activates[i].SetActive(true);
    }

    /// <summary>
    /// Victory pre-panel cinematic: swaps sprites immediately, then plays clips in sequence.
    /// If no content is configured, opens the win panel straight away.
    /// Runs with timeScale = 0; uses WaitForSecondsRealtime for audio timing.
    /// </summary>
    IEnumerator VictoryCinematicThenPanelCoroutine()
    {
        bool hasClips = HasAnyValidClip(victoryCinematicClips);
        bool hasSwaps = victoryCinematicSpriteSwaps != null && victoryCinematicSpriteSwaps.Count > 0;

        if (hasClips || hasSwaps)
        {
            ApplyCinematicSpriteSwaps(victoryCinematicSpriteSwaps);
            if (hasClips)
                yield return PlayCinematicClipsRealtime(victoryCinematicClips);
        }

        _victoryCinematicCoroutine = null;
        if (ui != null)
            ui.ShowGameWin(TotalPerformanceScore);
    }

    /// <summary>
    /// Boss-fail pre-panel cinematic: destroys/activates objects immediately, then plays clips in sequence.
    /// If no content is configured, opens the game-over panel straight away.
    /// Runs with timeScale = 0; uses WaitForSecondsRealtime for audio timing.
    /// </summary>
    IEnumerator BossFailCinematicThenPanelCoroutine(float capturedSurviveTime, float capturedWork, string reason)
    {
        bool hasClips = HasAnyValidClip(bossFailCinematicClips);
        bool hasDestroys = bossFailCinematicDestroyObjects != null && bossFailCinematicDestroyObjects.Count > 0;
        bool hasActivates = bossFailCinematicActivateObjects != null && bossFailCinematicActivateObjects.Count > 0;

        if (hasClips || hasDestroys || hasActivates)
        {
            ApplyCinematicDestroyAndActivate(bossFailCinematicDestroyObjects, bossFailCinematicActivateObjects);
            if (hasClips)
                yield return PlayCinematicClipsRealtime(bossFailCinematicClips);
        }

        _bossFailCinematicCoroutine = null;
        if (ui != null)
            ui.ShowGameOver(capturedSurviveTime, capturedWork, reason, TotalPerformanceScore);
    }

    public void Restart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void Quit()
    {
        Time.timeScale = 1f;
        Application.Quit();
    }
}

/// <summary>
/// A UI Image/Sprite pair used by the pre-panel cinematic sequence.
/// The target Image's sprite is swapped to newSprite when the cinematic starts.
/// </summary>
[System.Serializable]
public class CinematicSpriteSwap
{
    [Tooltip("The UI Image component whose sprite will be replaced when the cinematic starts.")]
    public Image targetImage;

    [Tooltip("Sprite to assign to the Image when the cinematic starts.")]
    public Sprite newSprite;
}
