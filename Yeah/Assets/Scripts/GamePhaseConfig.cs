using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Parameters for one phase of normal play. With a list, index 0 applies at start; promotion uses normalized performance (TotalPerformanceScore/divisor), independent of Boss.
/// </summary>
[System.Serializable]
public class GamePhaseConfig
{
    [Tooltip("Label for Inspector only")]
    public string phaseName = "Phase";

    [Header("Failures")]
    [Min(1)]
    public int maxConcurrentBrokenWorkItems = 2;

    [Tooltip("Auto-failure timing: wait (seconds) before next Broke/Bait attempt")]
    public float minBreakIntervalSeconds = 4f;
    public float maxBreakIntervalSeconds = 10f;

    [Header("Boss check")]
    [Tooltip("While Boss is present, skip work-amount check and only check for Broke; kept for legacy scenes")]
    public float bossMinWorkThreshold = 20f;

    [Header("Work pressure bar (0 empty → maxWork full = lose)")]
    [Tooltip("Punishment (wrong hits etc.): instant work bar spike beyond per-second drift")]
    public float workPunishment = 5f;
    [Tooltip("UltraPunishment on bait wrong hit: instant work bar spike")]
    public float workUltraPunishment = 10f;
    [Tooltip("Per working item: work bar decrease per second (toward 0)")]
    public float workGainPerSecondPerWorkingItem = 1f;
    [Tooltip("Per broken item: work bar increase per second")]
    public float workLossPerSecondPerBrokenItem = 3f;
    [Tooltip("Instant work bar spike when item enters Broke")]
    [Min(0f)]
    public float workPressureInstantOnBroke = 5f;
    [Tooltip("Instant work bar drop when player repairs Broke")]
    [Min(0f)]
    public float workPressureInstantOnBrokeRepair = 8f;

    [Header("Broken warnings (Broke only, not bait)")]
    [Tooltip("Seconds after Broke before a list warning entry is created")]
    [Min(0f)]
    public float warningShowDelayAfterBreak = 0.5f;

    [Tooltip("Message template; must include {0} for WorkItem itemName (falls back to GameObject name)")]
    public string warningMessageFormat = "{0} is broken!";

    [Header("Score promotion (leave this phase → next)")]
    [Range(0f, 1f)]
    [Tooltip("Normalized score/divisor (same as BossIncomingConfig.performanceScoreNormalizationDivisor) required to promote; last tier unused. 0 falls back to BossIncomingConfig.scoreThresholdForNextPhase")]
    public float normalizedScoreRequiredForNextPhase = 0.55f;

    [Min(0f)]
    [FormerlySerializedAs("minWorkGainThisPhaseForSaturatedPromotion")]
    [Tooltip(
        "After entering this phase, TotalPerformanceScore must gain this much vs entry snapshot before saturated timed promotion when norm is maxed and hysteresis cannot apply. 0 uses BossIncomingConfig.defaultMinPerformanceScoreGainPerPhaseForSaturatedPromotion.")]
    public float minPerformanceScoreGainThisPhaseForSaturatedPromotion = 0f;
}
