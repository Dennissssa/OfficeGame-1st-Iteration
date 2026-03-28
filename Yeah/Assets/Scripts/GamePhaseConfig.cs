using UnityEngine;

/// <summary>
/// 正常流程中某一阶段的参数。有列表时开局即应用第 0 项；进阶由规范化表现分达到 BossIncomingConfig 阈值触发，与 Boss 无关。
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
    public float bossMinWorkThreshold = 20f;

    [Header("惩罚 / 工作量")]
    public float workPunishment = 5f;
    public float workUltraPunishment = 10f;
    public float workGainPerSecondPerWorkingItem = 1f;
    public float workLossPerSecondPerBrokenItem = 3f;

    [Header("损坏提示（仅 Broke，Bait 不提示）")]
    [Tooltip("物品进入 Broke 后延迟多少秒再在列表中生成一条提示")]
    [Min(0f)]
    public float warningShowDelayAfterBreak = 0.5f;

    [Tooltip("文案模板；必须包含占位符 {0}，将替换为 WorkItem 的 itemName（空则用物体名）")]
    public string warningMessageFormat = "{0} is broken!";
}
