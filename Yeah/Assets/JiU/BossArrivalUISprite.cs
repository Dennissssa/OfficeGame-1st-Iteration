using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace JiU
{
    /// <summary>
    /// Boss 预警：将 Image 显示、换「靠近」图、巡逻动画。
    /// 若 BossIncomingConfig 开启 enablePreArrivalFreeze 且 freezeWindowBeforeArrival &gt; 0，
    /// 则 Image 与巡逻动画推迟到该「到达前冻结窗口」内再出现（与 BlockNewHackEventsNow 一致）。
    /// Boss 刚到：换第一张图；延迟后可换第二张。离开：恢复 Sprite 并 SetActive(false)。
    /// </summary>
    public class BossArrivalUISprite : MonoBehaviour
    {
        [Header("UI Image")]
        [Tooltip("要更换 Sprite 的 Image 组件（其 GameObject 会在预警时显示、Boss 离开后隐藏）")]
        public Image targetImage;

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

        [Header("Boss 离开")]
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
            GameManager.Instance.OnBossLeft.AddListener(OnBossLeft);
        }

        void OnDestroy()
        {
            StopSecondSpriteRoutine();
            if (GameManager.Instance == null) return;
            GameManager.Instance.OnBossWarningStarted.RemoveListener(OnBossWarningStarted);
            GameManager.Instance.OnBossArrived.RemoveListener(OnBossArrived);
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

        void TrySetPatrolAnimation(bool play)
        {
            Transform animHost = targetImage != null ? targetImage.transform : transform;

            if (!play)
            {
                if (_patrolAnimation != null)
                {
                    _patrolAnimation.Stop();
                    _patrolAnimation.clip = null;
                }

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

            // 老版本 Unity 没有 Play(AnimationClip)，用默认 clip + Play()，兼容 Legacy Animation。
            _patrolAnimation.clip = clip;
            _patrolAnimation.wrapMode = WrapMode.Loop;
            _patrolAnimation.Play();
        }

        void OnBossLeft()
        {
            _freezeWindowApproachApplied = false;
            TrySetPatrolAnimation(false);
            StopSecondSpriteRoutine();
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
