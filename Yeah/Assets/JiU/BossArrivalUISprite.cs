using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace JiU
{
    /// <summary>
    /// Boss 预警：将 Image 显示、换「靠近」图、巡逻动画。
    /// 若 BossIncomingConfig 开启 enablePreArrivalFreeze 且 freezeWindowBeforeArrival &gt; 0，
    /// 则 Image 与巡逻动画推迟到该「到达前冻结窗口」内再出现（与 BlockNewHackEventsNow 一致）。
    /// Boss 刚到：换第一张图；延迟后可换第二张。若配置了第二张图，GameManager 的 Broke 失败判定会延迟到切到第二张图之后。
    /// 动画：推荐挂 Animator + Trigger「Coming」（预警靠近）与「Leaving」（离场）；未挂 Animator 时仍可用 BossIncomingConfig 的 Legacy 巡逻片段与 bossLeaveAnimationClip。
    /// </summary>
    public class BossArrivalUISprite : MonoBehaviour
    {
        [Header("UI Image")]
        [Tooltip("要更换 Sprite 的 Image 组件（其 GameObject 会在预警时显示、Boss 离开后隐藏）")]
        public Image targetImage;

        [Header("Animator（推荐：Boss.controller + Trigger）")]
        [Tooltip("指定 Boss 的 Animator（可与 Image 同物体或子物体）。赋值后优先用 Trigger，不再用下方 Legacy 片段。")]
        public Animator bossAnimator;

        [Tooltip("与 Animator 窗口 Parameters 里名称一致")]
        public string comingTriggerParameter = "Coming";

        [Tooltip("与 Animator 窗口 Parameters 里名称一致")]
        public string leavingTriggerParameter = "Leaving";

        [Tooltip("用于 Boss 完全离开 UI 时回到默认状态（见 crossFadeToIdleWhenBossLeft），便于下一轮从 Idle 再触发 Coming")]
        public string idleStateName = "Idle";

        [Tooltip("OnBossLeft 时在 Animator 上 CrossFade 回 Idle。关闭则完全交给状态机自己从 Boss Stay / Leaving 回到 Idle")]
        public bool crossFadeToIdleWhenBossLeft = true;

        [Header("预警（靠近）")]
        [Tooltip("预警开始时设为 true；若只想显示空底图可不填下方 Sprite")]
        public bool activateOnBossWarning = true;

        [Tooltip("可选：预警阶段显示的 Sprite（不填则只 Active，不改图）")]
        public Sprite spriteDuringApproach;

        [Header("Boss 刚到 → 延迟后")]
        [Tooltip("Boss 到达瞬间切换的 Sprite")]
        public Sprite spriteWhenBossHere;

        [Min(0f)]
        [Tooltip("从第一张切到第二张前等待的秒数（真实时间）")]
        public float delaySecondsBeforeSecondSprite = 0.5f;

        [Tooltip("延迟结束后切换的 Sprite；不填则保持第一张")]
        public Sprite spriteAfterDelayWhenBossHere;

        [Header("Boss 离开（仅未使用 Animator 时）")]
        [Tooltip("Legacy 片段；已挂 bossAnimator 时改由 Leaving Trigger 驱动")]
        public AnimationClip bossLeaveAnimationClip;

        [Tooltip("离开后恢复的 Sprite；不填则用 Start 时记录的初始 Sprite")]
        public Sprite spriteWhenBossGone;

        Sprite _initialSprite;
        Coroutine _secondSpriteRoutine;
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
                // 不在「Boss 到达」时强制 Idle：否则会立刻打断 Boss Coming → Boss Stay 等连线
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
        /// Legacy Animation 必须把片段加入列表后再按「状态名」Play。状态名用 InstanceID，避免与 clip.name 不一致、空格、重名导致 Play(clip.name) 找不到。
        /// 片段必须勾选 Legacy，否则 AddClip 后也可能没有可播状态。
        /// </summary>
        static void PlayLegacyAnimationClip(Animation anim, AnimationClip clip, WrapMode wrapMode)
        {
            if (anim == null || clip == null)
                return;

            if (!clip.legacy)
            {
                Debug.LogWarning(
                    "[BossArrivalUISprite] 动画片段不是 Legacy，Legacy 的 Animation 组件无法播放。请在 Project 中选中该 .anim → Inspector 右上角 ⋮ → Debug → 勾选 Legacy。片段：" + clip.name,
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
                    "[BossArrivalUISprite] AddClip 后仍无法创建动画状态（片段：" + clip.name + "）。请确认曲线绑定在带 Image 的物体或其子物体上，且与 Legacy Animation 兼容。",
                    clip);
                return;
            }

            state.wrapMode = wrapMode;
            anim.Play(stateName);
        }

        void OnBossLeft()
        {
            _freezeWindowApproachApplied = false;
            TrySetPatrolAnimation(false);
            StopSecondSpriteRoutine();
            if (GameManager.Instance != null)
                GameManager.Instance.SetBossBrokeCheckAwaitingArrivalSprites(false);
            if (crossFadeToIdleWhenBossLeft)
                CrossFadeBossAnimatorToIdle();
            if (targetImage == null) return;

            targetImage.sprite = spriteWhenBossGone != null ? spriteWhenBossGone : _initialSprite;
            targetImage.gameObject.SetActive(false);
        }

        /// <summary>兼容旧事件绑定：等同 Boss 到达第一张图逻辑（不启动延迟协程，仅供 Inspector 手动调用）</summary>
        public void SetBossSprite()
        {
            OnBossArrived();
        }

        /// <summary>兼容旧事件绑定：等同 Boss 离开</summary>
        public void SetNormalSprite()
        {
            OnBossLeft();
        }
    }
}
