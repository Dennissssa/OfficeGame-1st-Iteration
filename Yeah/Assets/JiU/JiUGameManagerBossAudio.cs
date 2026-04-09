using UnityEngine;

namespace JiU
{
    /// <summary>
    /// Wires GameManager Boss events to AudioManager (PlaySound / PlayMusic and public EffectsSource only).
    /// Set indices for AudioManager.EffectsList / MusicList in Inspector; list order is yours on AudioManager.
    /// Index -1 skips that step.
    /// </summary>
    public class JiUGameManagerBossAudio : MonoBehaviour
    {
        [Header("EffectsList indices")]
        [Tooltip("Loop during warning until Boss arrives")]
        public int bossWarningSfxIndex = 0;

        [Tooltip("One-shot when Boss arrives (stops warning loop first)")]
        public int bossArrivedSfxIndex = 1;

        [Tooltip("Optional one-shot when Boss leaves")]
        public int bossLeftSfxIndex = -1;

        [Tooltip("One-shot on Boss-check game over (before pause)")]
        public int bossGameOverAngerSfxIndex = 2;

        [Header("MusicList indices (optional)")]
        [Tooltip("BGM during Boss stay check; -1 = no change")]
        public int musicDuringBossStayIndex = -1;

        [Tooltip("BGM after Boss leaves; -1 = no change")]
        public int musicAfterBossLeavesIndex = -1;

        void Start()
        {
            if (GameManager.Instance == null)
            {
                Debug.LogWarning("[JiUGameManagerBossAudio] GameManager.Instance is null; events not bound.", this);
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
                    // Play() right after Stop same frame can be ignored; PlayOneShot avoids relying on clip slot
                    am.EffectsSource.PlayOneShot(clip);
                }
#if UNITY_EDITOR
                else
                    Debug.LogWarning($"[JiUGameManagerBossAudio] EffectsList[{bossArrivedSfxIndex}] is null; Boss arrived SFX skipped.", this);
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
