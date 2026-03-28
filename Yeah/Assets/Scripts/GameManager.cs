using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public static bool FreezeFailures { get; private set; } = false;

    [Header("Work Value（无阶段配置时作为默认值；有阶段时由阶段覆盖）")]
    public float work = 0f;
    public float maxWork = 100f;
    public float workPunishment;
    public float workUltraPunishment;
    public float workMashGain;
    public float workGainPerSecondPerWorkingItem = 1f;
    public float workLossPerSecondPerBrokenItem = 3f;
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
    [Tooltip("开局应用第 0 项；阶段进阶仅由规范化表现分达到 BossIncomingConfig 阈值触发（与 Boss 无关）。无列表则保持 Inspector 默认")]
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
    public UnityEvent OnBossLeft;

    [Tooltip("因 Boss 检查导致游戏失败时触发")]
    public UnityEvent OnGameOverBossCaused;

    [Header("Optional")]
    public bool autoFindItemsOnStart = true;

    [Header("Debug")]
    [Tooltip("勾选后：开局 Console 打印阶段进阶所需分数说明；每次成功修好 Broke 后打印当前表现分")]
    public bool debugLogPhaseAndScore;

    public List<WorkItem> items = new List<WorkItem>();

    [Header("损坏提示 UI（槽位 + 滑入）")]
    [Tooltip("含 TextMeshProUGUI 的条目预制体，将作为子物体生成到下方列表中的槽位下")]
    public GameObject warningEntryPrefab;

    [Tooltip("场景中 UI 上的空 RectTransform 槽位；始终使用列表中从前到后第一个「未被占用」的槽位（含延迟中的预留）")]
    public List<RectTransform> warningSlotRects = new List<RectTransform>();

    [Tooltip("滑入起点 = 预制体默认 anchoredPosition + 该偏移（槽位本地空间）。例如 (400,0) 从右侧、(0,-80) 从下方")]
    public Vector2 warningSlideInFromOffset = new Vector2(320f, 0f);

    [Tooltip("滑入动画时长（秒）；0 则直接出现在终点")]
    [Min(0f)]
    public float warningSlideInDuration = 0.35f;

    [Tooltip("0~1 进度曲线；留空或无关键帧时用线性")]
    public AnimationCurve warningSlideInEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("损坏提示（教程 / 尚无阶段配置时）")]
    [Min(0f)]
    public float tutorialWarningShowDelayAfterBreak = 0.5f;

    [Tooltip("文案模板；{0} = 物品显示名")]
    public string tutorialWarningMessageFormat = "{0} is broken!";

    /// <summary>Hack 事件驱动的累计表现分（可为负）；教程段与未启用阶段计分前不变。</summary>
    public float TotalPerformanceScore => _performanceScoreRaw;

    /// <summary>0~1 规范化表现分，用于分数强触 Boss 与自动阶段进阶。</summary>
    public float NormalizedPerformanceScore => ComputeNormalizedPerformanceScore();

    public bool IsVictory { get; private set; }

    public bool IsGameOver => isGameOver;

    public ArduinoSerialBridge arduinoBridgeScript;

    public bool BossIsHere { get; private set; } = false;
    public bool BossWarning { get; private set; } = false;
    public float BossWarningTimeLeft { get; private set; } = 0f;

    int _activeMaxConcurrentBroken = int.MaxValue;
    float _activeBreakMin = 4f;
    float _activeBreakMax = 10f;
    float _activeWarningShowDelayAfterBreak = 0.5f;
    string _activeWarningMessageFormat = "{0} is broken!";

    sealed class BrokenWarningState
    {
        public Coroutine DelayRoutine;
        public Coroutine SlideRoutine;
        public GameObject Instance;
        /// <summary>延迟等待期间即占用，避免多条抢同一槽位。</summary>
        public RectTransform ReservedSlot;
    }

    sealed class ItGuyWarningState
    {
        public Coroutine DelayRoutine;
        public Coroutine SlideRoutine;
        public GameObject Instance;
        public RectTransform ReservedSlot;
    }

    readonly Dictionary<WorkItem, BrokenWarningState> _brokenWarnings = new Dictionary<WorkItem, BrokenWarningState>();
    ItGuyWarningState _itGuyWarning;

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

    float _victoryCountdownRemaining;
    bool _phasePromotionArmed = true;
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
        ClearItGuyWarning();
        ClearAllBrokenWarnings();
    }

    void Start()
    {
        work = maxWork;

        if (autoFindItemsOnStart && items.Count == 0)
        {
            items.Clear();
            items.AddRange(FindObjectsOfType<WorkItem>());
        }

        _activeMaxConcurrentBroken = int.MaxValue;
        _activeBreakMin = 4f;
        _activeBreakMax = 10f;
        _performanceScoreRaw = 0f;
        _activeWarningShowDelayAfterBreak = Mathf.Max(0f, tutorialWarningShowDelayAfterBreak);
        _activeWarningMessageFormat = string.IsNullOrEmpty(tutorialWarningMessageFormat)
            ? "{0} is broken!"
            : tutorialWarningMessageFormat;

        if (ui != null)
        {
            ui.InitWorkSlider(maxWork);
            ui.SetWork(work);
            ui.SetWorkBossMinThresholdIndicator(bossMinWorkThreshold, maxWork);
            ui.SetTime(_victoryCountdownRemaining);
            ui.HideGameOver();
            ui.HideGameWin();
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
        }
        else
            _normalPerformanceScoringActive = false;

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
    }

    void DebugLogPhasePromotionAtGameStart()
    {
        if (!debugLogPhaseAndScore)
            return;

        float half = 500f;
        if (bossIncomingConfig != null && bossIncomingConfig.performanceNormalizedHalfRange > 0f)
            half = Mathf.Max(1f, bossIncomingConfig.performanceNormalizedHalfRange);

        float thr = GetScorePhasePromotionThreshold();
        float hyst = GetScorePhasePromotionHysteresis();
        float rawDeltaFromNeutral = (thr - 0.5f) * 2f * half;

        string phaseBlock;
        if (gamePhases == null || gamePhases.Count == 0)
            phaseBlock = "未配置 gamePhases → 无阶段配置、无分数升阶。";
        else if (gamePhases.Count <= 1)
            phaseBlock = $"gamePhases 仅 1 条 → 不会因分数升阶。当前阶段索引={_currentPhaseIndex}。";
        else
            phaseBlock =
                $"阶段数={gamePhases.Count}，当前阶段索引={_currentPhaseIndex}（0 起）。\n" +
                $"升阶条件：规范化表现分 NormalizedPerformanceScore ≥ {thr:F3}。\n" +
                $"滞回：分数需先低于 {thr - hyst:F3} 后，再次升破 {thr:F3} 才会进入下一阶段（防阈值抖动）。\n" +
                $"若规范化分已顶到 1.0（Clamp 上限），无法再回落触发滞回，则每间隔 ≥{MinSecondsBetweenScorePhasePromotionsWhenSaturated:F2}s 且仍 ≥ 阈值时可继续升阶。";

        Debug.Log(
            "[GameManager/PhaseScore] ========== 开局：阶段进阶与分数 ==========\n" +
            phaseBlock + "\n" +
            $"归一化：norm = Clamp01(0.5 + raw / (2×{half:F1}))；raw 为 TotalPerformanceScore（原始分）。\n" +
            $"从 raw=0（norm=0.5）若只做加分、忽略衰减，约需净增 raw ≈ {rawDeltaFromNeutral:F1} 才能使「未截断前」的 norm 到达阈值 {thr:F3}（多物衰减时实际要更高）。\n" +
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

        surviveTime += Time.deltaTime;

        _victoryCountdownRemaining -= Time.deltaTime;
        if (_victoryCountdownRemaining <= 0f)
        {
            TriggerVictory();
            return;
        }

        UpdatePhaseAdvanceByScore();

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
                if (!_brokenWarnings.ContainsKey(wi))
                    StartBrokenWarningForItem(wi);
            }
            else
            {
                if (_brokenWarnings.ContainsKey(wi))
                    ClearBrokenWarningForItem(wi);
                if (wi.IsBaiting) baiting++;
                else working++;
            }
        }

        if (work < maxWork)
        {
            work += working * workGainPerSecondPerWorkingItem * Time.deltaTime;
            if (work > maxWork) work = maxWork;
        }

        work -= broken * workLossPerSecondPerBrokenItem * Time.deltaTime;
        if (work < 0f) work = 0f;

        if (_normalPerformanceScoringActive)
        {
            BossIncomingConfig.PhaseScoreSettings ps = GetPhaseScoreSettings();
            _performanceScoreRaw -= broken * ps.scoreDecayPerSecond * Time.deltaTime;
        }

        if (BossIsHere)
        {
            if (broken > 0)
                GameOver("Boss saw hacked items!");
            else if (work < bossMinWorkThreshold)
                GameOver("Work is too low!");
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
            _normalPerformanceScoringActive = true;
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
            OnBossWarningStarted?.Invoke();
            StartItGuyWarningIfEnabled();

            if (screenTint != null)
                screenTint.SetTarget(1f, warnDur);

            while (BossWarningTimeLeft > 0f && !isGameOver && !IsVictory)
            {
                BossWarningTimeLeft -= Time.deltaTime;
                yield return null;
            }

            BossWarning = false;
            BossWarningTimeLeft = 0f;
            ClearItGuyWarning();

            if (isGameOver || IsVictory) yield break;

            BossIsHere = true;
            OnBossArrived?.Invoke();

            if (screenTint != null)
                screenTint.SetTarget(0.35f, 0.15f);

            float stay = stayDur;
            while (stay > 0f && !isGameOver && !IsVictory)
            {
                stay -= Time.deltaTime;
                yield return null;
            }

            BossIsHere = false;
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

        float thr = GetScorePhasePromotionThreshold();
        float h = GetScorePhasePromotionHysteresis();
        float norm = NormalizedPerformanceScore;

        _timeSinceLastScorePhasePromotion += Time.deltaTime;

        if (norm < thr - h)
            _phasePromotionArmed = true;

        const float normSaturated = 0.998f;
        bool saturated = norm >= normSaturated;

        bool promoteByHysteresis = _phasePromotionArmed && norm >= thr;
        bool promoteBySaturatedInterval = saturated && norm >= thr
            && _timeSinceLastScorePhasePromotion >= MinSecondsBetweenScorePhasePromotionsWhenSaturated;

        if (!promoteByHysteresis && !promoteBySaturatedInterval)
            return;

        _currentPhaseIndex++;
        ApplyPhaseConfig(gamePhases[_currentPhaseIndex]);
        _phasePromotionArmed = false;
        _timeSinceLastScorePhasePromotion = 0f;
    }

    float GetScorePhasePromotionThreshold()
    {
        if (bossIncomingConfig != null)
            return Mathf.Clamp01(bossIncomingConfig.scoreThresholdForNextPhase);
        return 0.55f;
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

        _activeMaxConcurrentBroken = Mathf.Max(1, c.maxConcurrentBrokenWorkItems);
        _activeBreakMin = c.minBreakIntervalSeconds;
        _activeBreakMax = Mathf.Max(c.minBreakIntervalSeconds, c.maxBreakIntervalSeconds);
        bossMinWorkThreshold = c.bossMinWorkThreshold;
        workPunishment = c.workPunishment;
        workUltraPunishment = c.workUltraPunishment;
        workGainPerSecondPerWorkingItem = c.workGainPerSecondPerWorkingItem;
        workLossPerSecondPerBrokenItem = c.workLossPerSecondPerBrokenItem;
        _activeWarningShowDelayAfterBreak = Mathf.Max(0f, c.warningShowDelayAfterBreak);
        _activeWarningMessageFormat = string.IsNullOrEmpty(c.warningMessageFormat)
            ? "{0} is broken!"
            : c.warningMessageFormat;

        if (ui != null)
            ui.SetWorkBossMinThresholdIndicator(bossMinWorkThreshold, maxWork);
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
        float halfRange = 500f;
        if (bossIncomingConfig != null && bossIncomingConfig.performanceNormalizedHalfRange > 0f)
            halfRange = Mathf.Max(1f, bossIncomingConfig.performanceNormalizedHalfRange);
        return Mathf.Clamp01(0.5f + _performanceScoreRaw / (2f * halfRange));
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
        FreezeFailures = false;

        if (screenTint != null)
            screenTint.SetTarget(0f, 0.1f);

        ClearItGuyWarning();
        ClearAllBrokenWarnings();

        if (ui != null)
            ui.ShowGameWin(TotalPerformanceScore);

        Time.timeScale = 0f;
    }

    public void Punishment()
    {
        work -= workPunishment;
        ApplyWrongRepairPerformancePenalty(baitWrongHit: false);
    }

    public void UltraPunishment()
    {
        work -= workUltraPunishment;
        ApplyWrongRepairPerformancePenalty(baitWrongHit: true);
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
        work += workMashGain;
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
        if (item == null || warningEntryPrefab == null || warningSlotRects == null || warningSlotRects.Count == 0)
            return;

        var state = new BrokenWarningState();
        _brokenWarnings[item] = state;
        TryReserveFirstFreeWarningSlot(state);
        state.DelayRoutine = StartCoroutine(BrokenWarningDelayRoutine(item, state));
    }

    /// <summary>槽位是否被任意损坏提示或 Sam 提示占用（exceptBroken 为当前 Broken 状态时可忽略其自身预留）。</summary>
    bool IsWarningSlotOccupied(RectTransform slot, BrokenWarningState exceptBroken = null)
    {
        foreach (KeyValuePair<WorkItem, BrokenWarningState> kv in _brokenWarnings)
        {
            BrokenWarningState st = kv.Value;
            if (st == exceptBroken) continue;
            if (st.ReservedSlot == slot) return true;
            if (st.Instance != null && st.Instance.transform.parent == slot) return true;
        }

        if (_itGuyWarning != null)
        {
            if (_itGuyWarning.ReservedSlot == slot) return true;
            if (_itGuyWarning.Instance != null && _itGuyWarning.Instance.transform.parent == slot) return true;
        }

        return false;
    }

    /// <summary>从列表头开始找第一个空闲槽并写入 state.ReservedSlot；已有预留则不再改。</summary>
    bool TryReserveFirstFreeWarningSlot(BrokenWarningState state)
    {
        if (state == null || warningSlotRects == null || warningSlotRects.Count == 0)
            return false;
        if (state.ReservedSlot != null)
            return true;

        for (int i = 0; i < warningSlotRects.Count; i++)
        {
            RectTransform s = warningSlotRects[i];
            if (s == null) continue;
            if (IsWarningSlotOccupied(s, state))
                continue;
            state.ReservedSlot = s;
            return true;
        }

        return false;
    }

    bool TryReserveFirstFreeItGuySlot(ItGuyWarningState state)
    {
        if (state == null || warningSlotRects == null || warningSlotRects.Count == 0)
            return false;
        if (state.ReservedSlot != null)
            return true;

        for (int i = 0; i < warningSlotRects.Count; i++)
        {
            RectTransform s = warningSlotRects[i];
            if (s == null) continue;
            if (IsWarningSlotOccupied(s, null))
                continue;
            state.ReservedSlot = s;
            return true;
        }

        return false;
    }

    IEnumerator BrokenWarningDelayRoutine(WorkItem item, BrokenWarningState state)
    {
        float delay = Mathf.Max(0f, _activeWarningShowDelayAfterBreak);
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        state.DelayRoutine = null;

        if (item == null || !item.IsBroken)
        {
            _brokenWarnings.Remove(item);
            yield break;
        }

        while (state.ReservedSlot == null)
        {
            if (!TryReserveFirstFreeWarningSlot(state))
                yield return null;
        }

        RectTransform slot = state.ReservedSlot;
        if (warningEntryPrefab == null || slot == null)
        {
            _brokenWarnings.Remove(item);
            yield break;
        }

        GameObject entry = Instantiate(warningEntryPrefab, slot);
        state.Instance = entry;

        RectTransform entryRt = entry.GetComponent<RectTransform>();
        if (entryRt != null)
        {
            Vector2 endPos = entryRt.anchoredPosition;
            Vector2 startPos = endPos + warningSlideInFromOffset;
            entryRt.anchoredPosition = startPos;

            TMP_Text tmp = entry.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                try
                {
                    tmp.text = string.Format(_activeWarningMessageFormat, WorkItemDisplayName(item));
                }
                catch (System.FormatException)
                {
                    tmp.text = $"{WorkItemDisplayName(item)} is broken!";
                }

                tmp.ForceMeshUpdate();
            }

            float dur = warningSlideInDuration;
            if (dur <= 0f)
                entryRt.anchoredPosition = endPos;
            else
                state.SlideRoutine = StartCoroutine(SlideWarningEntryRoutine(entryRt, startPos, endPos, state));
        }
        else
        {
            TMP_Text tmp = entry.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                try
                {
                    tmp.text = string.Format(_activeWarningMessageFormat, WorkItemDisplayName(item));
                }
                catch (System.FormatException)
                {
                    tmp.text = $"{WorkItemDisplayName(item)} is broken!";
                }

                tmp.ForceMeshUpdate();
            }
        }
    }

    IEnumerator SlideWarningEntryRoutine(RectTransform rt, Vector2 from, Vector2 to, BrokenWarningState state)
    {
        float dur = Mathf.Max(0.0001f, warningSlideInDuration);
        bool useCurve = warningSlideInEase != null && warningSlideInEase.length > 0;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);
            if (useCurve)
                u = warningSlideInEase.Evaluate(u);
            rt.anchoredPosition = Vector2.LerpUnclamped(from, to, u);
            yield return null;
        }

        rt.anchoredPosition = to;
        state.SlideRoutine = null;
    }

    void StartItGuyWarningIfEnabled()
    {
        BossIncomingConfig cfg = bossIncomingConfig;
        if (cfg == null || !cfg.enableITGuyWarning) return;
        if (string.IsNullOrWhiteSpace(cfg.itGuyWarningMessage)) return;
        if (warningEntryPrefab == null || warningSlotRects == null || warningSlotRects.Count == 0) return;

        ClearItGuyWarning();
        _itGuyWarning = new ItGuyWarningState();
        _itGuyWarning.DelayRoutine = StartCoroutine(ItGuyWarningDelayRoutine(cfg.itGuyWarningMessage.Trim(), _itGuyWarning));
    }

    IEnumerator ItGuyWarningDelayRoutine(string message, ItGuyWarningState state)
    {
        float delay = Mathf.Max(0f, _activeWarningShowDelayAfterBreak);
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        state.DelayRoutine = null;

        while (state.ReservedSlot == null)
        {
            if (!TryReserveFirstFreeItGuySlot(state))
                yield return null;
        }

        RectTransform slot = state.ReservedSlot;
        if (warningEntryPrefab == null || slot == null)
        {
            ClearItGuyWarning();
            yield break;
        }

        GameObject entry = Instantiate(warningEntryPrefab, slot);
        state.Instance = entry;

        RectTransform entryRt = entry.GetComponent<RectTransform>();
        if (entryRt != null)
        {
            Vector2 endPos = entryRt.anchoredPosition;
            Vector2 startPos = endPos + warningSlideInFromOffset;
            entryRt.anchoredPosition = startPos;

            TMP_Text tmp = entry.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                tmp.text = message;
                tmp.ForceMeshUpdate();
            }

            float dur = warningSlideInDuration;
            if (dur <= 0f)
                entryRt.anchoredPosition = endPos;
            else
                state.SlideRoutine = StartCoroutine(SlideItGuyWarningEntryRoutine(entryRt, startPos, endPos, state));
        }
        else
        {
            TMP_Text tmp = entry.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                tmp.text = message;
                tmp.ForceMeshUpdate();
            }
        }
    }

    IEnumerator SlideItGuyWarningEntryRoutine(RectTransform rt, Vector2 from, Vector2 to, ItGuyWarningState state)
    {
        float dur = Mathf.Max(0.0001f, warningSlideInDuration);
        bool useCurve = warningSlideInEase != null && warningSlideInEase.length > 0;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);
            if (useCurve)
                u = warningSlideInEase.Evaluate(u);
            rt.anchoredPosition = Vector2.LerpUnclamped(from, to, u);
            yield return null;
        }

        rt.anchoredPosition = to;
        state.SlideRoutine = null;
    }

    void ClearItGuyWarning()
    {
        if (_itGuyWarning == null) return;

        if (_itGuyWarning.DelayRoutine != null)
        {
            StopCoroutine(_itGuyWarning.DelayRoutine);
            _itGuyWarning.DelayRoutine = null;
        }

        if (_itGuyWarning.SlideRoutine != null)
        {
            StopCoroutine(_itGuyWarning.SlideRoutine);
            _itGuyWarning.SlideRoutine = null;
        }

        if (_itGuyWarning.Instance != null)
        {
            Destroy(_itGuyWarning.Instance);
            _itGuyWarning.Instance = null;
        }

        _itGuyWarning.ReservedSlot = null;
        _itGuyWarning = null;
    }

    void ClearBrokenWarningForItem(WorkItem item)
    {
        if (item == null || !_brokenWarnings.TryGetValue(item, out BrokenWarningState state))
            return;

        if (state.DelayRoutine != null)
        {
            StopCoroutine(state.DelayRoutine);
            state.DelayRoutine = null;
        }

        if (state.SlideRoutine != null)
        {
            StopCoroutine(state.SlideRoutine);
            state.SlideRoutine = null;
        }

        if (state.Instance != null)
        {
            Destroy(state.Instance);
            state.Instance = null;
        }

        state.ReservedSlot = null;
        _brokenWarnings.Remove(item);
    }

    void ClearAllBrokenWarnings()
    {
        if (_brokenWarnings.Count == 0) return;
        var keys = new List<WorkItem>(_brokenWarnings.Keys);
        for (int i = 0; i < keys.Count; i++)
            ClearBrokenWarningForItem(keys[i]);
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

        FreezeFailures = false;

        bool bossCaused = reason != null && (reason.Contains("Boss") || reason.Contains("Work is too low"));
        if (bossCaused)
            OnGameOverBossCaused?.Invoke();

        if (screenTint != null)
            screenTint.SetTarget(0f, 0.1f);

        ClearItGuyWarning();
        ClearAllBrokenWarnings();

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
