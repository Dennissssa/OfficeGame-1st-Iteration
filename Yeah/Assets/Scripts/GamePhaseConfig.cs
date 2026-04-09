using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 正常流程中某一阶段的参数。有列表时开局即应用第 0 项；进阶由规范化表现分（TotalPerformanceScore/分母）达到门槛触发，与 Boss 无关。
/// </summary>
[System.Serializable]
public class GamePhaseConfig
{
    [Tooltip("仅用于 Inspector 识别")]
    public string phaseName = "Phase";

    [Header("故障")]
    [Min(1)]
    public int maxConcurrentBrokenWorkItems = 2;

    [Tooltip("自动故障计时：下一次尝试进入 Broke/Bait 前的等待（秒）")]
    public float minBreakIntervalSeconds = 4f;
    public float maxBreakIntervalSeconds = 10f;

    [Header("Boss 检查")]
    [Tooltip("Boss 在场时不再检查工作量，仅检查是否有 Broke；此项保留兼容旧场景")]
    public float bossMinWorkThreshold = 20f;

    [Header("Work 压力条（0 空 → maxWork 满则失败）")]
    [Tooltip("误击等 Punishment：压力条每秒以外的瞬时增加")]
    public float workPunishment = 5f;
    [Tooltip("Bait 误击 UltraPunishment：压力条瞬时增加")]
    public float workUltraPunishment = 10f;
    [Tooltip("每个「正常运作」物品每秒降低的压力（条往 0 回）")]
    public float workGainPerSecondPerWorkingItem = 1f;
    [Tooltip("每个 Broke 物品每秒增加的压力")]
    public float workLossPerSecondPerBrokenItem = 3f;
    [Tooltip("物品进入 Broke 瞬间增加的压力")]
    [Min(0f)]
    public float workPressureInstantOnBroke = 5f;
    [Tooltip("玩家修好 Broke 瞬间降低的压力")]
    [Min(0f)]
    public float workPressureInstantOnBrokeRepair = 8f;

    [Header("损坏提示（仅 Broke，Bait 不提示）")]
    [Tooltip("物品进入 Broke 后延迟多少秒再在列表中生成一条提示")]
    [Min(0f)]
    public float warningShowDelayAfterBreak = 0.5f;

    [Tooltip("文案模板；必须包含占位符 {0}，将替换为 WorkItem 的 itemName（空则用物体名）")]
    public string warningMessageFormat = "{0} is broken!";

    [Header("分数升阶（离开本阶段进入下一阶段）")]
    [Range(0f, 1f)]
    [Tooltip("表现分/分母（与 BossIncomingConfig.performanceScoreNormalizationDivisor 一致）需达到该值才允许升入下一阶段；最后一档无效。填 0 时用 BossIncomingConfig.scoreThresholdForNextPhase 作为后备")]
    public float normalizedScoreRequiredForNextPhase = 0.55f;

    [Min(0f)]
    [FormerlySerializedAs("minWorkGainThisPhaseForSaturatedPromotion")]
    [Tooltip(
        "进入本阶段后 TotalPerformanceScore 相对「进入本阶段瞬间」至少再净增这么多，才允许在 norm 顶满无法用滞回时走定时饱和升阶。填 0 则用 BossIncomingConfig.defaultMinPerformanceScoreGainPerPhaseForSaturatedPromotion。")]
    public float minPerformanceScoreGainThisPhaseForSaturatedPromotion = 0f;
}
