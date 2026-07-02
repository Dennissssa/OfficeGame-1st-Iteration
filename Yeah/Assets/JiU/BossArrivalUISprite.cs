using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace JiU
{
    /// <summary>
    /// Boss warning: show Image, approach sprite, patrol animation.
    /// If BossIncomingConfig enablePreArrivalFreeze and freezeWindowBeforeArrival &gt; 0,
    /// Image and patrol defer until that pre-arrival freeze window (aligned with BlockNewHackEventsNow).
    /// On Boss arrive: first sprite; optional delay then second. If second sprite is set, GameManager Broke-fail check waits until after the swap.
    /// Animation: prefer Animator + Coming (approach) and Leaving triggers; without Animator, use BossIncomingConfig Legacy patrol clip and bossLeaveAnimationClip.
    /// </summary>
    public class BossArrivalUISprite : MonoBehaviour
    {
        [Header("UI Image")]
        [Tooltip("Image whose sprite changes (shown on warning, hidden after Boss leaves)")]
        public Image targetImage;

        [Header("Animator (recommended: Boss.controller + triggers)")]
        [Tooltip("Boss Animator (same object as Image or child). When set, triggers win over Legacy clips below.")]
        public Animator bossAnimator;

        [Tooltip("Must match Animator Parameters name")]
        public string comingTriggerParameter = "Coming";

        [Tooltip("Must match Animator Parameters name")]
        public string leavingTriggerParameter = "Leaving";

        [Tooltip("State to return to when Boss fully leaves UI (see crossFadeToIdleWhenBossLeft); next round can fire Coming from Idle")]
        public string idleStateName = "Idle";

        [Tooltip("Legacy / no Animator only. Do not enable with Animator Leaving: OnBossLeft CrossFade to Idle would interrupt Leaving.")]
        public bool crossFadeToIdleWhenBossLeft = true;

        [Tooltip("When BossIncomingConfig.bossLeaveAnimationDuration is 0, wait this many seconds after leave before hiding Image (time for Leaving state). If Config leave duration &gt; 0, this does not add on top.")]
        [Min(0f)]
        public float animatorLeaveHideDelayWhenGameHoldIsZero = 1.2f;

        [Header("Warning (approach)")]
        [Tooltip("Set active on warning start; leave sprite empty for blank base only")]
        public bool activateOnBossWarning = true;

        [Tooltip("Optional sprite during warning (omit to only activate, no sprite change)")]
        public Sprite spriteDuringApproach;

        [Header("Boss arrived -> after delay")]
        [Tooltip("Sprite when Boss arrives")]
        public Sprite spriteWhenBossHere;

        [Min(0f)]
        [Tooltip("Realtime seconds before switching from first to second sprite")]
        public float delaySecondsBeforeSecondSprite = 0.5f;

        [Tooltip("Sprite after delay; unset keeps first sprite")]
        public Sprite spriteAfterDelayWhenBossHere;

        [Header("Boss leave (no Animator only)")]
        [Tooltip("Legacy clip; with bossAnimator use Leaving trigger instead")]
        public AnimationClip bossLeaveAnimationClip;

        [Tooltip("Sprite after leave; unset restores initial sprite captured at Start")]
        public Sprite spriteWhenBossGone;

        Sprite _initialSprite;
        Coroutine _secondSpriteRoutine;
        Coroutine _bossLeftHideRoutine;
        Animation _patrolAnimation;
        bool _freezeWindowApproachApplied;

        void Start()
        {
            if (targetImage != null)
                _initialSprite = targetImage.sprite;

            if (GameManager.Instance == null) return;

            GameManager.Instance.OnBossWarningStarted.AddListener(OnBossWarningStarted);
            GameManager.Instance.OnBossArrived.AddListener(OnBossArrived);
            GameManager.Instance.OnBossLeaveStarted.AddListener(OnBossLeaveStarted);
            GameManager.Instance.OnBossLeft.AddListener(OnBossLeft);
        }

        void OnDestroy()
        {
            StopSecondSpriteRoutine();
            StopBossLeftHideRoutine();
            if (GameManager.Instance == null) return;
            GameManager.Instance.OnBossWarningStarted.RemoveListener(OnBossWarningStarted);
            GameManager.Instance.OnBossArrived.RemoveListener(OnBossArrived);
            GameManager.Instance.OnBossLeaveStarted.RemoveListener(OnBossLeaveStarted);
            GameManager.Instance.OnBossLeft.RemoveListener(OnBossLeft);
        }

        void OnBossWarningStarted()
        {
            if (targetImage == null) return;

            GameManager gm = GameManager.Instance;
            if (DeferBossVisualToFreezeWindow(gm))
            {
                _freezeWindowApproachApplied = false;
                TrySetPatrolAnimation(false);
                if (activateOnBossWarning)
                    targetImage.gameObject.SetActive(false);
                return;
            }

            ApplyApproachVisualAndPatrol();
        }

        static bool DeferBossVisualToFreezeWindow(GameManager gm)
        {
            BossIncomingConfig cfg = gm != null ? gm.bossIncomingConfig : null;
            return cfg != null && cfg.enablePreArrivalFreeze && cfg.freezeWindowBeforeArrival > 0.0001f;
        }

        void ApplyApproachVisualAndPatrol()
        {
            if (targetImage == null) return;

            if (activateOnBossWarning)
                targetImage.gameObject.SetActive(true);

            if (spriteDuringApproach != null)
                targetImage.sprite = spriteDuringApproach;

            TrySetPatrolAnimation(true);
        }

        void LateUpdate()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || targetImage == null || !gm.BossWarning)
                return;

            if (!DeferBossVisualToFreezeWindow(gm))
                return;

            if (_freezeWindowApproachApplied)
                return;

            if (!gm.BlockNewHackEventsNow())
                return;

            _freezeWindowApproachApplied = true;
            ApplyApproachVisualAndPatrol();
        }

        void OnBossArrived()
        {
            if (targetImage == null) return;

            GameManager gm = GameManager.Instance;
            if (spriteAfterDelayWhenBossHere != null && gm != null)
                gm.SetBossBrokeCheckAwaitingArrivalSprites(true);

            if (activateOnBossWarning)
                targetImage.gameObject.SetActive(true);

            TrySetPatrolAnimation(false);
            StopSecondSpriteRoutine();

            if (spriteWhenBossHere != null)
                targetImage.sprite = spriteWhenBossHere;

            if (spriteAfterDelayWhenBossHere != null)
                _secondSpriteRoutine = StartCoroutine(SecondSpriteAfterDelayRoutine());
        }

        IEnumerator SecondSpriteAfterDelayRoutine()
        {
            if (delaySecondsBeforeSecondSprite > 0f)
                yield return new WaitForSecondsRealtime(delaySecondsBeforeSecondSprite);

            if (targetImage != null && spriteAfterDelayWhenBossHere != null)
                targetImage.sprite = spriteAfterDelayWhenBossHere;

            if (GameManager.Instance != null)
                GameManager.Instance.SetBossBrokeCheckAwaitingArrivalSprites(false);

            _secondSpriteRoutine = null;
        }

        void StopSecondSpriteRoutine()
        {
            if (_secondSpriteRoutine != null)
            {
                StopCoroutine(_secondSpriteRoutine);
                _secondSpriteRoutine = null;
            }
        }

        bool BossAnimatorReady =>
            bossAnimator != null && bossAnimator.isActiveAndEnabled && bossAnimator.runtimeAnimatorController != null;

        void TrySetPatrolAnimation(bool play)
        {
            if (BossAnimatorReady)
            {
                if (play)
                {
                    bossAnimator.ResetTrigger(leavingTriggerParameter);
                    bossAnimator.SetTrigger(comingTriggerParameter);
                }
                // Do not force Idle on Boss arrive: would break Coming -> Boss Stay transitions
                return;
            }

            Transform animHost = targetImage != null ? targetImage.transform : transform;

            if (!play)
            {
                if (_patrolAnimation != null)
                    _patrolAnimation.Stop();
                return;
            }

            GameManager gm = GameManager.Instance;
            BossIncomingConfig cfg = gm != null ? gm.bossIncomingConfig : null;
            if (cfg == null || !cfg.enableBossPatrolAnimation || cfg.patrolAnimationClip == null)
                return;

            AnimationClip clip = cfg.patrolAnimationClip;

            _patrolAnimation = animHost.GetComponent<Animation>();
            if (_patrolAnimation == null)
                _patrolAnimation = animHost.gameObject.AddComponent<Animation>();

            _patrolAnimation.Stop();
            _patrolAnimation.playAutomatically = false;
            PlayLegacyAnimationClip(_patrolAnimation, clip, WrapMode.Loop);
        }

        void CrossFadeBossAnimatorToIdle()
        {
            if (!BossAnimatorReady || string.IsNullOrEmpty(idleStateName))
                return;
            bossAnimator.CrossFade(idleStateName, 0.1f, 0, 0f);
        }

        void OnBossLeaveStarted()
        {
            TryPlayBossLeaveAnimation();
        }

        void TryPlayBossLeaveAnimation()
        {
            if (BossAnimatorReady)
            {
                bossAnimator.ResetTrigger(comingTriggerParameter);
                bossAnimator.SetTrigger(leavingTriggerParameter);
                return;
            }

            if (bossLeaveAnimationClip == null || targetImage == null)
                return;

            Transform animHost = targetImage.transform;
            Animation anim = animHost.GetComponent<Animation>();
            if (anim == null)
                anim = animHost.gameObject.AddComponent<Animation>();

            _patrolAnimation = anim;

            anim.Stop();
            anim.playAutomatically = false;
            PlayLegacyAnimationClip(anim, bossLeaveAnimationClip, WrapMode.Once);
        }

        /// <summary>
        /// Legacy Animation: add clip to the component then Play by state name. State name uses InstanceID to avoid clip.name mismatches/spaces/duplicates.
        /// Clip must be Legacy or AddClip may not create a playable state.
        /// </summary>
        static void PlayLegacyAnimationClip(Animation anim, AnimationClip clip, WrapMode wrapMode)
        {
            if (anim == null || clip == null)
                return;

            if (!clip.legacy)
            {
                Debug.LogWarning(
                    "[BossArrivalUISprite] Clip is not Legacy; Legacy Animation cannot play it. In Project select the .anim, Inspector menu > Debug > enable Legacy. Clip: " + clip.name,
                    clip);
                return;
            }

            string stateName = "JiULegacy_" + clip.GetInstanceID();

            if (anim.GetClip(stateName) == null)
                anim.AddClip(clip, stateName);

            AnimationState state = anim[stateName];
            if (state == null)
            {
                Debug.LogWarning(
                    "[BossArrivalUISprite] Could not create animation state after AddClip (clip: " + clip.name + "). Ensure curves target the Image object or children and are Legacy-compatible.",
                    clip);
                return;
            }

            state.wrapMode = wrapMode;
            anim.Play(stateName);
        }

        void StopBossLeftHideRoutine()
        {
            if (_bossLeftHideRoutine != null)
            {
                StopCoroutine(_bossLeftHideRoutine);
                _bossLeftHideRoutine = null;
            }
        }

        void OnBossLeft()
        {
            _freezeWindowApproachApplied = false;
            TrySetPatrolAnimation(false);
            StopSecondSpriteRoutine();
            StopBossLeftHideRoutine();
            if (GameManager.Instance != null)
                GameManager.Instance.SetBossBrokeCheckAwaitingArrivalSprites(false);

            // Animator + Leaving: CrossFade to Idle in same flow would interrupt Leaving just triggered
            if (crossFadeToIdleWhenBossLeft && !BossAnimatorReady)
                CrossFadeBossAnimatorToIdle();

            float cfgLeaveHold = 0f;
            if (GameManager.Instance != null && GameManager.Instance.bossIncomingConfig != null)
                cfgLeaveHold = Mathf.Max(0f, GameManager.Instance.bossIncomingConfig.bossLeaveAnimationDuration);

            // When GameManager leave hold is 0, OnBossLeft and LeaveStarted same frame; Leaving never gets time to play
            if (BossAnimatorReady && cfgLeaveHold < 0.0001f && animatorLeaveHideDelayWhenGameHoldIsZero > 0f)
            {
                _bossLeftHideRoutine = StartCoroutine(HideBossImageAfterLeavingDelayRoutine(animatorLeaveHideDelayWhenGameHoldIsZero));
                return;
            }

            ApplyBossLeftVisualCleanup();
        }

        IEnumerator HideBossImageAfterLeavingDelayRoutine(float delaySeconds)
        {
            if (delaySeconds > 0f)
                yield return new WaitForSecondsRealtime(delaySeconds);
            _bossLeftHideRoutine = null;
            ApplyBossLeftVisualCleanup();
        }

        void ApplyBossLeftVisualCleanup()
        {
            if (targetImage == null) return;
            targetImage.sprite = spriteWhenBossGone != null ? spriteWhenBossGone : _initialSprite;
            targetImage.gameObject.SetActive(false);
        }

        /// <summary>Legacy hook: same as first Boss-arrived sprite (no delay coroutine; manual Inspector use)</summary>
        public void SetBossSprite()
        {
            OnBossArrived();
        }

        /// <summary>Legacy hook: same as Boss left</summary>
        public void SetNormalSprite()
        {
            OnBossLeft();
        }
    }
}
