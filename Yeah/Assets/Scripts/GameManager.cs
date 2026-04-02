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

    [Header("Work 压力条（0 起计，涨至 maxWork 失败；无阶段时用下列默认）")]
    public float work = 0f;
    public float maxWork = 100f;
    public float workPunishment;
    public float workUltraPunishment;
    [Tooltip("Reward() 调用时降低的压力条（向 0）")]
    public float workMashGain;
    [Tooltip("每台正常运作物品每秒降低的压力")]
    public float workGainPerSecondPerWorkingItem = 1f;
    [Tooltip("每台 Broke 每秒增加的压力")]
    public float workLossPerSecondPerBrokenItem = 3f;
    [Tooltip("无 gamePhases 时：Broke 瞬间加压")]
    public float workPressureInstantOnBroke = 5f;
    [Tooltip("无 gamePhases 时：修好 Broke 瞬间减压")]
    public float workPressureInstantOnBrokeRepair = 8f;
    [Tooltip("Boss 不再校验工作量，仅保留兼容")]
    public float bossMinWorkThreshold = 20f;

    [Header("Boss 来袭（参数见 BossIncomingConfig 组件）")]
    [Tooltip("可与 GameManager 同物体；留空则在 Awake 时 GetComponent")]
    public BossIncomingConfig bossIncomingConfig;

    [Header("教程")]
    [Tooltip("开启后：按顺序逐个 Broke WorkItem，全部修好后立即开始第一次 Boss（不等待 Boss 到来间隔）；Boss 离开后进入正常阶段流程")]
    public bool enableTutorial = true;

    [Tooltip("教程 Broke 顺序；留空则使用下方 Items 列表顺序")]
    public List<WorkItem> tutorialBreakOrder = new List<WorkItem>();

    [Header("正常流程阶段")]
    [Tooltip("开局应用第 0 项；阶段进阶由规范化表现分（TotalPerformanceScore/分母）达到各阶段阈值触发（与 Boss 无关）。无列表则保持 Inspector 默认")]
    public List<GamePhaseConfig> gamePhases = new List<GamePhaseConfig>();

    [Header("胜利条件")]
    [Tooltip("本局倒计时（秒），到 0 自动胜利（不再根据「打完所有阶段」判定）")]
    [Min(0.1f)]
    public float victoryCountdownSeconds = 300f;

    [Header("References")]
    public UIManager ui;
    public ScreenVignetteTint screenTint;

    [Header("Boss 事件（可选）")]
    public UnityEvent OnBossWarningStarted;
    public UnityEvent OnBossArrived;
    [Tooltip("在场检查时间结束、开始播放离场动画或表现时触发；之后延迟 bossLeaveAnimationDuration 再 OnBossLeft")]
    public UnityEvent OnBossLeaveStarted;
    public UnityEvent OnBossLeft;

    [Tooltip("因 Boss 检查导致游戏失败时触发")]
    public UnityEvent OnGameOverBossCaused;

    [Header("Optional")]
    public bool autoFindItemsOnStart = true;

    [Header("Debug")]
    [Tooltip("勾选后：开局 Console 打印阶段进阶所需分数说明；每次成功修好 Broke 后打印当前表现分")]
    public bool debugLogPhaseAndScore;

    [Tooltip("勾选后：工作压力条涨满并进入 GameOverWorkProgressFull 时，在 Console 打一条日志（含 work、UIManager 是否绑定），用于排查该流程是否执行")]
    public bool debugLogWorkProgressDeath = true;

    public List<WorkItem> items = new List<WorkItem>();

    [Header("损坏 / Boss 预警 UI（单面板，无 Prefab）")]
    [Tooltip("需要显示任意提示时设为 true，无提示时 false")]
    public GameObject brokenWarningPanelRoot;

    [Tooltip("损坏文案与 Boss 预警文案共用此 TMP")]
    public TextMeshProUGUI brokenWarningText;

    [Tooltip("可选：提示时切换为 brokenWarningActiveSprite，无下一则提示或进入 Boss 冻结窗时恢复默认")]
    public Image brokenWarningIconImage;

    [Tooltip("进入「新一轮」物品提示时使用的图标（连续提示下一个损坏物时不改图）")]
    public Sprite brokenWarningActiveSprite;

    [Tooltip("可选：Boss 预警前半段（非冻结窗）对此 RectTransform 做晃动；冻结窗内停止")]
    public RectTransform brokenWarningShakeTarget;

    [Tooltip("Boss 预警晃动幅度（本地像素量级）")]
    public float brokenWarningBossShakeAmplitude = 10f;

    [Tooltip("Boss 预警晃动频率")]
    public float brokenWarningBossShakeFrequency = 14f;

    [Tooltip("修好当前提示物后若还有排队中的提示：先关面板再等待此时长再显示下一则（类似对话停顿）")]
    [Min(0f)]
    public float interBrokenWarningPauseSeconds = 0.35f;

    [Header("损坏提示（教程 / 尚无阶段配置时）")]
    [Min(0f)]
    public float tutorialWarningShowDelayAfterBreak = 0.5f;

    [Tooltip("文案模板；{0} = 物品显示名")]
    public string tutorialWarningMessageFormat = "{0} is broken!";

    /// <summary>Hack 事件驱动的累计表现分（可为负）；教程段与未启用阶段计分前不变。</summary>
    public float TotalPerformanceScore => _performanceScoreRaw;

    /// <summary>0~1，为 TotalPerformanceScore / performanceScoreNormalizationDivisor（Clamp01），用于分数强触 Boss 与阶段进阶。</summary>
    public float NormalizedPerformanceScore => ComputeNormalizedPerformanceScore();

    public bool IsVictory { get; private set; }

    public bool IsGameOver => isGameOver;

    public ArduinoSerialBridge arduinoBridgeScript;

    public bool isTutorialing;
    public bool BossIsHere { get; private set; } = false;
    public bool BossWarning { get; private set; } = false;
    public float BossWarningTimeLeft { get; private set; } = 0f;

    /// <summary>Boss 在场但「Look→Peek」等到达 UI 未播完时为 true，此时不执行 Broke 即失败判定。</summary>
    bool _bossBrokeCheckAwaitingArrivalSprites;

    int _activeMaxConcurrentBroken = int.MaxValue;
    float _activeWorkPressureInstantOnBroke = 5f;
    float _activeWorkPressureInstantOnBrokeRepair = 8f;
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

    int _currentPhaseIndex;
    bool _tutorialBreakSequenceDone;
    /// <summary>是否允许 WorkItem 自动 Broke/Bait；关教程时从 Awake 起为 true，开教程时开局 false、第一次 Boss 离开后为 true。</summary>
    bool _allowRandomWorkItemFailures;
    bool _firstBossLeaveHandled;
    bool _normalPerformanceScoringActive;

    /// <summary>教程最后一个 WorkItem 修好后的第一次 Boss：跳过随机等待与冷却。</summary>
    bool _immediateBossAfterTutorial;

    /// <summary>已有至少一次 Boss 完整流程后，下一次等待 Boss 前需要经过冷却。</summary>
    bool _applyCooldownBeforeNextBossWait;

    float _performanceScoreRaw;
    float _performanceScoreRawWhenEnteredCurrentPhase;

    float _victoryCountdownRemaining;
    bool _phasePromotionArmed;
    float _timeSinceLastScorePhasePromotion = 1000f;

    /// <summary>规范化分顶到 ~1 后无法回落触发滞回，用该间隔允许再次分数升阶（秒）。</summary>
    const float MinSecondsBetweenScorePhasePromotionsWhenSaturated = 0.35f;

    float surviveTime;
    bool isGameOver;
    Coroutine _bossLoopCoroutine;
    Coroutine _tutorialCoroutine;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (bossIncomingConfig == null)
            bossIncomingConfig = GetComponent<BossIncomingConfig>();

        // 在任意 Start 之前确定，避免 WorkItem.Start 早于 GameManager.Start 时 RegisterItem 把 enableAutoBreak 锁成 false
        _allowRandomWorkItemFailures = !enableTutorial;
    }

    void OnDestroy()
    {
        ShutdownBrokenWarningSystem();
    }

    void WarnIfMultipleUIManagerInScene()
    {
        UIManager[] all = FindObjectsOfType<UIManager>(true);
        if (all == null || all.Length <= 1)
            return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(
            "[GameManager] 场景中检测到 **多个 UIManager**。GameManager 只会使用 Inspector 里 References → ui 绑定的那一个；");
        sb.AppendLine(
            "若「工作压力失败」面板只挂在另一个 Canvas/物体上的 UIManager 里且未绑定到当前 ui，ShowWorkProgressLose 会立刻 return 或显示空引用，Console 里 Awake 的 instanceID 也会与失败时不一致。");
        for (int i = 0; i < all.Length; i++)
        {
            bool isBound = ui != null && all[i] == ui;
            sb.AppendLine(
                $"  [{i}] instanceID={all[i].GetInstanceID()} 物体=\"{all[i].gameObject.name}\" " +
                $"workProgressLoseRoot={(all[i].workProgressLoseRoot != null ? "已赋值" : "未赋值")} " +
                $"是当前 GameManager.ui: {isBound}");
        }

        Debug.LogWarning(sb.ToString().TrimEnd(), this);
    }

    void Start()
    {
        work = 0f;

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

        if (ui != null)
        {
            if (ui.workProgressLoseRoot == null)
            {
                Debug.LogError(
                    "[GameManager] 当前 GameManager.ui 指向的 UIManager 上 **workProgressLoseRoot 未赋值**，工作压力满时无法显示失败面板。" +
                    $" 该 UIManager 挂在物体「{ui.gameObject.name}」instanceID={ui.GetInstanceID()}。若场景里还有另一个 UIManager，请把三项结算引用都配在同一组件上，或改 ui 引用。",
                    ui);
            }

            ui.InitWorkSlider(maxWork);
            ui.SetWork(work);
            ui.SetTime(_victoryCountdownRemaining);
            ui.HideAllResultPanels();
        }

        if (screenTint != null)
            screenTint.SetTarget(0f, 0.01f);

        FreezeFailures = false;

        _victoryCountdownRemaining = Mathf.Max(0.1f, victoryCountdownSeconds);

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

        // 与列表中所有 WorkItem 同步（覆盖先于本 Start 注册的物体、以及 autoFind 仅加入列表未走 RegisterItem 的情况）
        SetAllWorkItemsAutoBreak(_allowRandomWorkItemFailures);

        _bossLoopCoroutine = StartCoroutine(BossLoop());

        DebugLogPhasePromotionAtGameStart();

        if (brokenWarningIconImage != null)
            _defaultBrokenWarningIconSprite = brokenWarningIconImage.sprite;
        if (brokenWarningShakeTarget != null)
            _brokenWarningShakeBaseLocalPos = brokenWarningShakeTarget.localPosition;
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
            phaseBlock = "未配置 gamePhases → 无阶段配置、无分数升阶。";
        else if (gamePhases.Count <= 1)
            phaseBlock = $"gamePhases 仅 1 条 → 不会因分数升阶。当前阶段索引={_currentPhaseIndex}。";
        else
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"阶段数={gamePhases.Count}，当前阶段索引={_currentPhaseIndex}（0 起）。");
            sb.AppendLine("各阶升阶（离开当前索引 → 下一索引）规范化门槛与滞回（norm = TotalPerformanceScore / 分母）：");
            for (int i = 0; i < gamePhases.Count - 1; i++)
            {
                float t = GetPromotionThresholdForLeavingPhase(i);
                sb.AppendLine(
                    $"  索引 {i}→{i + 1}：norm ≥ {t:F3}（约原始分 ≥ {t * div:F1} / {div:F1}）；滞回需先低于 {t - hyst:F3} 再升破 {t:F3}。");
            }

            float defSat = bossIncomingConfig != null
                ? bossIncomingConfig.defaultMinPerformanceScoreGainPerPhaseForSaturatedPromotion
                : 0f;
            sb.AppendLine(
                $"若 norm 顶满且无法用滞回升阶：每阶需「进入该阶后原始分净增」≥ 该阶配置（0 则用 Boss 默认 {defSat:F1}）；且间隔 ≥{MinSecondsBetweenScorePhasePromotionsWhenSaturated:F2}s 才升一阶。Boss 默认填 0 则关闭饱和定时连升。");
            phaseBlock = sb.ToString().TrimEnd();
        }

        float thr0 = gamePhases != null && gamePhases.Count > 1
            ? GetPromotionThresholdForLeavingPhase(0)
            : GetFallbackScorePhasePromotionThreshold();
        float rawAtThr0 = thr0 * div;

        Debug.Log(
            "[GameManager/PhaseScore] ========== 开局：阶段进阶与分数 ==========\n" +
            phaseBlock + "\n" +
            $"归一化：norm = Clamp01(TotalPerformanceScore / 分母)；分母={div:F1}，当前原始分={TotalPerformanceScore:F2}，norm={NormalizedPerformanceScore:F3}。\n" +
            $"第 0 阶升阶阈值 norm≥{thr0:F3} 约等于原始分≥{rawAtThr0:F1}。\n" +
            $"当前计分开关 _normalPerformanceScoringActive = {_normalPerformanceScoringActive}" +
            (enableTutorial ? "（开教程时教程结束前为 false）" : "") + "\n" +
            "==========================================",
            this);
    }

    /// <summary>玩家成功修好 Broke 后由 WorkItem 调用，用于调试输出当前分数。</summary>
    public void DebugLogPerformanceAfterSuccessfulRepair(string repairedItemDisplayName)
    {
        if (!debugLogPhaseAndScore)
            return;

        int phaseCount = gamePhases != null ? gamePhases.Count : 0;
        string phaseStr = phaseCount > 0
            ? $"阶段 {_currentPhaseIndex + 1}/{phaseCount}（内部索引 {_currentPhaseIndex}，末档为 {phaseCount - 1}）"
            : "无阶段列表";
        Debug.Log(
            $"[GameManager/Score] 修好「{repairedItemDisplayName}」→ " +
            $"原始分={TotalPerformanceScore:F2} | 规范化={NormalizedPerformanceScore:F3} | {phaseStr}",
            this);
    }

    void Update()
    {
        if (isGameOver || IsVictory) return;
        if (_tutorialBreakSequenceDone)
        {
            isTutorialing = false;
        }
        surviveTime += Time.deltaTime;

        _victoryCountdownRemaining -= Time.deltaTime;
        if (_victoryCountdownRemaining <= 0f)
        {
            TriggerVictory();
            return;
        }

        UpdatePhaseAdvanceByScore();

        TickBossWarningUiFreezeAndShake();

        int working = 0;
        int broken = 0;
        int baiting = 0;

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == null) continue;

            WorkItem wi = items[i];
            if (wi.IsBroken)
            {
                broken++;
                if (!_itemBrokenWarningDelays.ContainsKey(wi))
                    StartBrokenWarningForItem(wi);
            }
            else
            {
                if (_itemBrokenWarningDelays.ContainsKey(wi))
                    ClearBrokenWarningForItem(wi);
                if (wi.IsBaiting) baiting++;
                else working++;
            }
        }

        work += broken * workLossPerSecondPerBrokenItem * Time.deltaTime;

        work -= working * workGainPerSecondPerWorkingItem * Time.deltaTime;

        work = Mathf.Clamp(work, 0f, maxWork);

        if (_normalPerformanceScoringActive)
        {
            BossIncomingConfig.PhaseScoreSettings ps = GetPhaseScoreSettings();
            _performanceScoreRaw -= broken * ps.scoreDecayPerSecond * Time.deltaTime;
        }

        if (BossIsHere && !_bossBrokeCheckAwaitingArrivalSprites && broken > 0)
        {
            GameOver("Boss saw hacked items!");
            return;
        }

        if (work >= maxWork)
        {
            GameOverWorkProgressFull();
            return;
        }

        if (ui != null)
        {
            ui.SetWork(work);
            ui.SetTime(_victoryCountdownRemaining);
        }
    }

    IEnumerator TutorialBreakSequenceCoroutine()
    {
        yield return null;

        List<WorkItem> order = tutorialBreakOrder != null && tutorialBreakOrder.Count > 0
            ? tutorialBreakOrder
            : items;

        for (int i = 0; i < order.Count; i++)
        {
            WorkItem wi = order[i];
            if (wi == null) continue;

            while (wi.IsBaiting)
                yield return null;

            wi.Break();

            yield return new WaitUntil(() => !wi.IsBroken && !wi.IsBaiting);
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
            float rMin = cfg != null ? cfg.randomTriggerMinTime : 10f;
            float rMax = cfg != null ? cfg.randomTriggerMaxTime : 25f;

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

            float randomWait = _immediateBossAfterTutorial ? 0f : Random.Range(rMin, rMax);
            if (_immediateBossAfterTutorial)
                _immediateBossAfterTutorial = false;

            float elapsed = 0f;
            while (elapsed < randomWait && !isGameOver && !IsVictory)
            {
                if (cfg != null && cfg.enableScoreForceTrigger && NormalizedPerformanceScore < cfg.scoreTriggerThreshold)
                    break;
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (isGameOver || IsVictory) yield break;

            BossWarning = true;
            BossWarningTimeLeft = warnDur;
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
            _bossBrokeCheckAwaitingArrivalSprites = false;
            OnBossArrived?.Invoke();

            if (screenTint != null)
                screenTint.SetTarget(0.35f, 0.15f);

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
                leaveHold -= Time.deltaTime;
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
        if (_currentPhaseIndex >= gamePhases.Count - 1)
            return;

        float thr = GetPromotionThresholdForLeavingPhase(_currentPhaseIndex);
        float h = GetScorePhasePromotionHysteresis();
        float norm = NormalizedPerformanceScore;

        _timeSinceLastScorePhasePromotion += Time.deltaTime;

        if (norm < thr - h)
            _phasePromotionArmed = true;

        const float normSaturated = 0.998f;
        bool saturated = norm >= normSaturated;

        bool promoteByHysteresis = _phasePromotionArmed && norm >= thr;

        float minPerfGainForSaturated = GetMinPerformanceScoreGainThisPhaseForSaturatedPromotion(_currentPhaseIndex);
        float perfSinceEnteredPhase = TotalPerformanceScore - _performanceScoreRawWhenEnteredCurrentPhase;
        bool promoteBySaturatedInterval = saturated && norm >= thr
            && minPerfGainForSaturated > 0.0001f
            && perfSinceEnteredPhase >= minPerfGainForSaturated
            && _timeSinceLastScorePhasePromotion >= MinSecondsBetweenScorePhasePromotionsWhenSaturated;

        if (!promoteByHysteresis && !promoteBySaturatedInterval)
            return;

        _currentPhaseIndex++;
        ApplyPhaseConfig(gamePhases[_currentPhaseIndex]);
        _phasePromotionArmed = false;
        _timeSinceLastScorePhasePromotion = 0f;
    }

    /// <summary>离开 gamePhases[phaseIndex] 进入下一阶段所需的规范化分阈值。</summary>
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
    /// 滞回「武装」与当前阶阈值对齐：仅当 norm 低于（阈值−滞回）后才允许再次升破该阶阈值。
    /// 避免开局 norm 与阈值关系异常时误升阶；教程结束开启计分时同步一次。
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
        _phasePromotionArmed = NormalizedPerformanceScore < thr - h;
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

    /// <summary>为 true 时 WorkItem 自动故障间隔使用阶段配置，否则使用各 WorkItem 自身 Inspector 数值。</summary>
    public bool UsePhaseBreakTiming()
    {
        if (gamePhases == null || gamePhases.Count == 0)
            return false;
        if (!enableTutorial)
            return true;
        return _tutorialBreakSequenceDone;
    }

    /// <summary>供 WorkItem 自动故障：当前是否还能新增一个 Broke（不含 Bait）。</summary>
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

    /// <summary>Boss 到达前冻结窗口内禁止产生新的 Hack（Broke）。</summary>
    public bool BlockNewHackEventsNow()
    {
        if (bossIncomingConfig == null || !bossIncomingConfig.enablePreArrivalFreeze || !BossWarning)
            return false;
        float w = Mathf.Max(0f, bossIncomingConfig.freezeWindowBeforeArrival);
        return BossWarningTimeLeft <= w;
    }

    /// <summary>Boss 在场检查（Stay）期间不允许新增 Broke；Bait 仍可由 WorkItem 概率触发。</summary>
    public bool BlockNewBrokeDuringBossStay()
    {
        if (isGameOver || IsVictory) return false;
        return BossIsHere;
    }

    /// <summary>由 BossArrivalUISprite 调用：配置了第二张到达图时，在切到 Peek 之前设为 true，切换完成后设为 false。</summary>
    public void SetBossBrokeCheckAwaitingArrivalSprites(bool awaiting)
    {
        _bossBrokeCheckAwaitingArrivalSprites = awaiting;
    }

    /// <summary>WorkItem 进入 Hacked（Broke）时调用，按阶段加上 baseScore。</summary>
    public void OnWorkItemEnteredHackedState(WorkItem item)
    {
        if (!_normalPerformanceScoringActive || item == null) return;
        BossIncomingConfig.PhaseScoreSettings ps = GetPhaseScoreSettings();
        _performanceScoreRaw += ps.baseScore;
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

        ShutdownBrokenWarningSystem();

        if (ui != null)
            ui.ShowGameWin(TotalPerformanceScore);

        Time.timeScale = 0f;
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

    /// <summary>WorkItem 进入 Broke 时调用，瞬时增加压力条。</summary>
    public void ApplyWorkPressureOnItemBroke()
    {
        if (isGameOver || IsVictory) return;
        work += _activeWorkPressureInstantOnBroke;
        ClampWorkProgressAndMaybeLose();
    }

    /// <summary>玩家修好 Broke 时由 WorkItem 调用，瞬时降低压力条。</summary>
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
        if (work >= maxWork)
            GameOverWorkProgressFull();
    }

    void GameOverWorkProgressFull()
    {
        if (isGameOver || IsVictory)
        {
            if (debugLogWorkProgressDeath)
                Debug.Log("[GameManager] GameOverWorkProgressFull 未执行：当前已是 Game Over 或胜利（例如本帧先触发了其它失败）。", this);
            return;
        }

        isGameOver = true;

        if (debugLogWorkProgressDeath)
        {
            Debug.Log(
                "[GameManager] 工作压力满 → 已触发死亡 (GameOverWorkProgressFull)。 " +
                $"work={work:F2}/{maxWork:F2}, survive={surviveTime:F1}s, performanceScore={TotalPerformanceScore:F1}, " +
                $"UIManager={(ui != null ? "已绑定" : "未绑定 null")}",
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

        ShutdownBrokenWarningSystem();

        if (ui != null)
        {
            if (debugLogWorkProgressDeath)
                Debug.Log(
                    "[GameManager] GameOverWorkProgressFull → 调用 ShowWorkProgressLose | " +
                    $"UIManager.instanceID={ui.GetInstanceID()} GameObject=\"{ui.gameObject.name}\" scene={ui.gameObject.scene.name}",
                    ui);
            ui.ShowWorkProgressLose(surviveTime, work, TotalPerformanceScore, maxWork);
            if (debugLogWorkProgressDeath)
                Debug.Log("[GameManager] ShowWorkProgressLose 已返回（未抛异常）", ui);
        }
#if UNITY_EDITOR
        else
            Debug.LogWarning("[GameManager] GameOverWorkProgressFull：UIManager (ui) 未赋值，无法显示工作压力失败面板。", this);
#endif

        Time.timeScale = 0f;
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

    void StartBrokenWarningForItem(WorkItem item)
    {
        if (item == null || brokenWarningPanelRoot == null || brokenWarningText == null)
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

        ShutdownBrokenWarningSystem();

        if (ui != null)
            ui.ShowGameOver(surviveTime, work, reason, TotalPerformanceScore);

        Time.timeScale = 0f;
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
