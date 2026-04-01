using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Boss 来袭与表现分相关参数，集中在一个组件上便于在 Inspector 中配置。
/// </summary>
public class BossIncomingConfig : MonoBehaviour
{
    [System.Serializable]
    public struct PhaseScoreSettings
    {
        public float baseScore;
        public float scoreDecayPerSecond;

        [Tooltip("未损坏且非 Bait 时误击（走 Punishment）额外扣除的原始表现分")]
        [Min(0f)]
        public float performancePenaltyIdleWrongHit;

        [Tooltip("处于 Bait 时误击（走 UltraPunishment）额外扣除的原始表现分")]
        [Min(0f)]
        public float performancePenaltyBaitWrongHit;
    }

    [Header("── Trigger Timing ──")]
    [Tooltip("每次 Boss 离开后，在此之前不可再次触发 Boss（含分数强触）")]
    [Min(0f)]
    public float cooldownDuration = 5f;

    [Tooltip("冷却结束后，随机触发的最短等待（秒）")]
    [Min(0f)]
    public float randomTriggerMinTime = 10f;

    [Tooltip("冷却结束后，随机触发的最长等待（秒）")]
    [Min(0f)]
    public float randomTriggerMaxTime = 25f;

    [Tooltip("启用后：当规范化表现分（TotalPerformanceScore/分母）低于阈值时立即触发 Boss（与随机计时并行，先发生者生效）")]
    public bool enableScoreForceTrigger;

    [Range(0f, 1f)]
    [Tooltip("规范化表现分低于该值（0~1，即表现分/分母）时强触 Boss")]
    public float scoreTriggerThreshold = 0.35f;

    [Tooltip("单次 Boss 预警最短等待（秒）")]
    [Min(0f)]
    public float bossWarningDurationMin = 2f;

    [Tooltip("单次 Boss 预警最长等待（秒）；每轮在 Min~Max 间随机")]
    [Min(0f)]
    public float bossWarningDurationMax = 5f;

    [Tooltip("Boss 在场检查持续秒数")]
    [Min(0f)]
    public float bossStayDuration = 6f;

    [Tooltip("检查阶段结束后、触发 OnBossLeft 前的离场表现时长（秒）。此期间 BossIsHere 仍为 true（仍按在场规则判定）。填 0 则与旧版一致立即离场。若使用 Legacy 离开动画，建议 ≥ 片段长度。")]
    [Min(0f)]
    public float bossLeaveAnimationDuration = 0f;

    [Header("── Warning System ──")]
    [Tooltip("预警开始时通过「物品损坏」同款槽位 UI 显示 Sam 提示")]
    public bool enableITGuyWarning = true;

    [Tooltip("Sam 提示文案（无占位符）")]
    public string itGuyWarningMessage = "Sam: Boss incoming!";

    [Tooltip("在 BossArrivalUISprite 的 Image 上播放巡逻动画（预警阶段）")]
    public bool enableBossPatrolAnimation;

    [Tooltip("须为 Legacy AnimationClip（Project 中选 Clip → Inspector ⋮ → Debug → 勾选 Legacy），并由 Legacy Animation 播放；片段应绑定在 Target Image 所在物体上")]
    public AnimationClip patrolAnimationClip;

    [Header("── Boss Phase Hack Limit ──")]
    [Tooltip("预警或 Boss 在场期间限制同时处于 Hacked(Broke) 的物品数量上限")]
    public bool enableBossPhaseHackLimit;

    [Min(1)]
    public int maxHackedItemsDuringBoss = 1;

    [Tooltip("Boss 正式到达前的最后若干秒内禁止产生新的 Hack(Broke)")]
    public bool enablePreArrivalFreeze;

    [Min(0f)]
    public float freezeWindowBeforeArrival = 1.5f;

    [Header("── Performance Score ──")]
    [Tooltip("按阶段：Broke 加分/衰减；误击（Punishment / UltraPunishment）再扣 performancePenalty*")]
    public PhaseScoreSettings[] phaseScoreSettings = new PhaseScoreSettings[]
    {
        new PhaseScoreSettings { baseScore = 100f, scoreDecayPerSecond = 5f, performancePenaltyIdleWrongHit = 15f, performancePenaltyBaitWrongHit = 30f },
        new PhaseScoreSettings { baseScore = 80f, scoreDecayPerSecond = 8f, performancePenaltyIdleWrongHit = 20f, performancePenaltyBaitWrongHit = 40f },
        new PhaseScoreSettings { baseScore = 60f, scoreDecayPerSecond = 12f, performancePenaltyIdleWrongHit = 25f, performancePenaltyBaitWrongHit = 50f },
    };

    [Min(1e-4f)]
    [FormerlySerializedAs("performanceNormalizedHalfRange")]
    [Tooltip("表现分「总分母」：规范化 norm = Clamp01(TotalPerformanceScore / 此值)。阶段进阶与 Boss 强触阈值均为 0~1 的相对比例。")]
    public float performanceScoreNormalizationDivisor = 1000f;

    [Header("── Phase Difficulty ──")]
    [Range(0f, 1f)]
    [Tooltip("各阶段升阶阈值优先使用 GamePhaseConfig；此项为表现分/分母 的比例后备（对应阶段填 0 时用）")]
    public float scoreThresholdForNextPhase = 0.55f;

    [Range(0.01f, 0.25f)]
    [Tooltip("每一阶单独计算滞回：分数需先低于「该阶阈值−滞回」后再次升破该阶阈值才会从该阶升出（防阈值抖动）")]
    public float scorePhasePromotionHysteresis = 0.03f;

    [Min(0f)]
    [FormerlySerializedAs("defaultMinWorkGainPerPhaseForSaturatedPromotion")]
    [Tooltip(
        "当 GamePhase 的 minPerformanceScoreGainThisPhaseForSaturatedPromotion 为 0 时，用此值：进入当前阶段后 TotalPerformanceScore 至少再净增这么多，才允许在 norm 顶满时按时间间隔升阶。设为 0 则关闭全局饱和定时升阶。")]
    public float defaultMinPerformanceScoreGainPerPhaseForSaturatedPromotion = 10f;

    /// <summary>随机区间无效时回退为 max(min, 0)。</summary>
    public void SanitizeRandomTriggerRange()
    {
        if (randomTriggerMaxTime < randomTriggerMinTime)
            randomTriggerMaxTime = randomTriggerMinTime;
    }

    public void SanitizeBossWarningDurationRange()
    {
        if (bossWarningDurationMax < bossWarningDurationMin)
            bossWarningDurationMax = bossWarningDurationMin;
    }

    public void SanitizeAllTriggerTimingRanges()
    {
        SanitizeRandomTriggerRange();
        SanitizeBossWarningDurationRange();
    }
}
