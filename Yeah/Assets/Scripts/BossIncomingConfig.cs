using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Boss arrival and performance-score settings in one component for Inspector tuning.
/// </summary>
public class BossIncomingConfig : MonoBehaviour
{
    [System.Serializable]
    public struct PhaseScoreSettings
    {
        public float baseScore;
        public float scoreDecayPerSecond;

        [Tooltip("Phone-style Bait (WorkItem phone rules): per-item score decay per second = scoreDecayPerSecond × this. 1 = same as Broke; 0 = none; 2 = double.")]
        [Min(0f)]
        public float phoneBaitScoreDecayMultiplier;

        [Tooltip("Extra raw performance score lost on wrong hit when not broken and not bait (Punishment)")]
        [Min(0f)]
        public float performancePenaltyIdleWrongHit;

        [Tooltip("Extra raw performance score lost on wrong hit while in bait (UltraPunishment)")]
        [Min(0f)]
        public float performancePenaltyBaitWrongHit;
    }

    [Header("── Trigger Timing ──")]
    [Tooltip("After each Boss leaves, Boss cannot trigger again until this cooldown elapses (includes score force trigger)")]
    [Min(0f)]
    public float cooldownDuration = 5f;

    [Tooltip("Min random wait (seconds) after cooldown before Boss can trigger")]
    [Min(0f)]
    public float randomTriggerMinTime = 10f;

    [Tooltip("Max random wait (seconds) after cooldown before Boss can trigger")]
    [Min(0f)]
    public float randomTriggerMaxTime = 25f;

    [Tooltip("When enabled, Boss triggers immediately if normalized performance (TotalPerformanceScore/divisor) falls below threshold (runs in parallel with random timer; first wins)")]
    public bool enableScoreForceTrigger;

    [Range(0f, 1f)]
    [Tooltip("Force Boss when normalized score (0~1 = score/divisor) is below this")]
    public float scoreTriggerThreshold = 0.35f;

    [Tooltip("Min Boss warning duration (seconds) per round")]
    [Min(0f)]
    public float bossWarningDurationMin = 2f;

    [Tooltip("Max Boss warning duration (seconds); each round picks randomly between Min and Max")]
    [Min(0f)]
    public float bossWarningDurationMax = 5f;

    [Tooltip("How long Boss presence check lasts (seconds)")]
    [Min(0f)]
    public float bossStayDuration = 6f;

    [Tooltip("After check phase, leave animation time (seconds) before OnBossLeft; BossIsHere stays true. With Animator Leaving, prefer >= clip length; 0 hides UI same frame as OnBossLeft—use BossArrivalUISprite.animatorLeaveHideDelayWhenGameHoldIsZero if needed.")]
    [Min(0f)]
    public float bossLeaveAnimationDuration = 0f;

    [Header("── Warning System ──")]
    [Tooltip("On warning start, show Sam hint in the same UI slot as item-broken warnings")]
    public bool enableITGuyWarning = true;

    [Tooltip("Sam hint text (no placeholders)")]
    public string itGuyWarningMessage = "Sam: Boss incoming!";

    [Tooltip("Play patrol animation on BossArrivalUISprite Image during warning")]
    public bool enableBossPatrolAnimation;

    [Tooltip("Must be a Legacy AnimationClip (Project: select clip, Inspector menu > Debug > Legacy), played by Legacy Animation; curves should target the Image object")]
    public AnimationClip patrolAnimationClip;

    [Header("── Boss Phase Hack Limit ──")]
    [Tooltip("Cap concurrent Hacked(Broke) items during warning or while Boss is present")]
    public bool enableBossPhaseHackLimit;

    [Min(1)]
    public int maxHackedItemsDuringBoss = 1;

    [Tooltip("Block new Hack(Broke) in the last N seconds before Boss actually arrives")]
    public bool enablePreArrivalFreeze;

    [Min(0f)]
    public float freezeWindowBeforeArrival = 1.5f;

    [Header("── Performance Score ──")]
    [Tooltip("Per phase: Broke score gain/decay; wrong hits (Punishment / UltraPunishment) subtract performancePenalty*")]
    public PhaseScoreSettings[] phaseScoreSettings = new PhaseScoreSettings[]
    {
        new PhaseScoreSettings { baseScore = 100f, scoreDecayPerSecond = 5f, phoneBaitScoreDecayMultiplier = 0.5f, performancePenaltyIdleWrongHit = 15f, performancePenaltyBaitWrongHit = 30f },
        new PhaseScoreSettings { baseScore = 80f, scoreDecayPerSecond = 8f, phoneBaitScoreDecayMultiplier = 0.5f, performancePenaltyIdleWrongHit = 20f, performancePenaltyBaitWrongHit = 40f },
        new PhaseScoreSettings { baseScore = 60f, scoreDecayPerSecond = 12f, phoneBaitScoreDecayMultiplier = 0.5f, performancePenaltyIdleWrongHit = 25f, performancePenaltyBaitWrongHit = 50f },
    };

    [Min(1e-4f)]
    [FormerlySerializedAs("performanceNormalizedHalfRange")]
    [Tooltip("Performance score divisor: norm = Clamp01(TotalPerformanceScore / this). Phase promotion and Boss force thresholds are 0~1 relative to this.")]
    public float performanceScoreNormalizationDivisor = 1000f;

    [Header("── Phase Difficulty ──")]
    [Range(0f, 1f)]
    [Tooltip("Phase promotion thresholds prefer GamePhaseConfig; this is fallback normalized score/divisor when a phase field is 0")]
    public float scoreThresholdForNextPhase = 0.55f;

    [Range(0.01f, 0.25f)]
    [Tooltip("Per-phase hysteresis: score must dip below (threshold − hysteresis) then rise past threshold again to promote out of that phase (reduces threshold chatter)")]
    public float scorePhasePromotionHysteresis = 0.03f;

    [Min(0f)]
    [FormerlySerializedAs("defaultMinWorkGainPerPhaseForSaturatedPromotion")]
    [Tooltip(
        "When GamePhase minPerformanceScoreGainThisPhaseForSaturatedPromotion is 0, use this: after entering a phase, TotalPerformanceScore must gain this much more before timed promotion at saturated norm. 0 disables global saturated timed promotion.")]
    public float defaultMinPerformanceScoreGainPerPhaseForSaturatedPromotion = 10f;

    /// <summary>When random range is invalid, clamp max to at least min (and non-negative).</summary>
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
