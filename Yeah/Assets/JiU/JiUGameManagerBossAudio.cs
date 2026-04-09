using UnityEngine;

namespace JiU
{
    /// <summary>
    /// 将 GameManager 的 Boss 事件接到 AudioManager（仅使用 PlaySound / PlayMusic 与公开的 EffectsSource）。
    /// 在 Inspector 里填好「AudioManager.EffectsList / MusicList 的下标」；列表顺序由你在 AudioManager 上自行排列。
    /// 下标为 -1 表示该步不播放。
    /// </summary>
    public class JiUGameManagerBossAudio : MonoBehaviour
    {
        [Header("EffectsList 下标")]
        [Tooltip("预警阶段循环播放，直到 Boss 到达")]
        public int bossWarningSfxIndex = 0;

        [Tooltip("Boss 实际到达时播放一次（会先停掉预警音）")]
        public int bossArrivedSfxIndex = 1;

        [Tooltip("Boss 离开场景时可选播放一次")]
        public int bossLeftSfxIndex = -1;

        [Tooltip("因 Boss 检查失败时播放一次（在暂停前触发）")]
        public int bossGameOverAngerSfxIndex = 2;

        [Header("MusicList 下标（可选）")]
        [Tooltip("Boss 在场检查期间背景音乐；-1 不换曲")]
        public int musicDuringBossStayIndex = -1;

        [Tooltip("Boss 离开后恢复的音乐；-1 不换曲")]
        public int musicAfterBossLeavesIndex = -1;

        void Start()
        {
            if (GameManager.Instance == null)
            {
                Debug.LogWarning("[JiUGameManagerBossAudio] GameManager.Instance 为空，事件未绑定。", this);
                return;
            }

            GameManager.Instance.OnBossWarningStarted.AddListener(OnBossWarningStarted);
            GameManager.Instance.OnBossArrived.AddListener(OnBossArrived);
            GameManager.Instance.OnBossLeft.AddListener(OnBossLeft);
            GameManager.Instance.OnGameOverBossCaused.AddListener(OnGameOverBossCaused);
        }

        void OnDestroy()
        {
            if (GameManager.Instance == null) return;
            GameManager.Instance.OnBossWarningStarted.RemoveListener(OnBossWarningStarted);
            GameManager.Instance.OnBossArrived.RemoveListener(OnBossArrived);
            GameManager.Instance.OnBossLeft.RemoveListener(OnBossLeft);
            GameManager.Instance.OnGameOverBossCaused.RemoveListener(OnGameOverBossCaused);
        }

        void OnBossWarningStarted()
        {
            var am = AudioManager.Instance;
            if (am == null || am.EffectsSource == null) return;
            if (bossWarningSfxIndex < 0 || bossWarningSfxIndex >= am.EffectsList.Count) return;

            am.EffectsSource.loop = true;
            am.PlaySound(bossWarningSfxIndex);
        }

        void OnBossArrived()
        {
            var am = AudioManager.Instance;
            if (am == null || am.EffectsSource == null) return;

            am.EffectsSource.loop = false;
            am.EffectsSource.Stop();

            if (bossArrivedSfxIndex >= 0 && bossArrivedSfxIndex < am.EffectsList.Count)
            {
                AudioClip clip = am.EffectsList[bossArrivedSfxIndex];
                if (clip != null)
                {
                    // 同一帧 Stop 后立刻 Play() 在部分情况下会被忽略；PlayOneShot 不依赖 clip 槽位，更稳
                    am.EffectsSource.PlayOneShot(clip);
                }
#if UNITY_EDITOR
                else
                    Debug.LogWarning($"[JiUGameManagerBossAudio] EffectsList[{bossArrivedSfxIndex}] 为空，Boss 到达音未播放。", this);
#endif
            }

            if (musicDuringBossStayIndex >= 0 && musicDuringBossStayIndex < am.MusicList.Count)
                am.PlayMusic(musicDuringBossStayIndex);
        }

        void OnBossLeft()
        {
            var am = AudioManager.Instance;
            if (am == null) return;

            if (musicAfterBossLeavesIndex >= 0 && musicAfterBossLeavesIndex < am.MusicList.Count)
                am.PlayMusic(musicAfterBossLeavesIndex);

            if (am.EffectsSource != null &&
                bossLeftSfxIndex >= 0 && bossLeftSfxIndex < am.EffectsList.Count)
                am.PlaySound(bossLeftSfxIndex);
        }

        void OnGameOverBossCaused()
        {
            var am = AudioManager.Instance;
            if (am == null || am.EffectsSource == null) return;
            if (bossGameOverAngerSfxIndex < 0 || bossGameOverAngerSfxIndex >= am.EffectsList.Count) return;

            am.PlaySound(bossGameOverAngerSfxIndex);
        }
    }
}
