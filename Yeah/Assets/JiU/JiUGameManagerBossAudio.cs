using System.Collections;
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

        [Tooltip("One-shot when Boss stay is safe from instant Broke-fail (same gate as GameManager); skipped on Boss die so only anger SFX plays")]
        public int bossArrivedSfxIndex = 1;

        [Tooltip("Optional one-shot when Boss leave starts (with OnBossLeaveStarted / Leaving UI), not when fully gone")]
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
            GameManager.Instance.OnBossLeaveStarted.AddListener(OnBossLeaveStarted);
            GameManager.Instance.OnBossLeft.AddListener(OnBossLeft);
            GameManager.Instance.OnGameOverBossCaused.AddListener(OnGameOverBossCaused);
        }

        void OnDestroy()
        {
            if (GameManager.Instance == null) return;
            GameManager.Instance.OnBossWarningStarted.RemoveListener(OnBossWarningStarted);
            GameManager.Instance.OnBossArrived.RemoveListener(OnBossArrived);
            GameManager.Instance.OnBossLeaveStarted.RemoveListener(OnBossLeaveStarted);
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

            var gm = GameManager.Instance;
            if (gm != null)
                gm.StartCoroutine(PlayBossArrivedOneShotIfNoInstantBossFailCoroutine(am));
            else
                TryPlayBossArrivedOneShot(am);

            if (musicDuringBossStayIndex >= 0 && musicDuringBossStayIndex < am.MusicList.Count)
                am.PlayMusic(musicDuringBossStayIndex);
        }

        void TryPlayBossArrivedOneShot(AudioManager am)
        {
            if (am?.EffectsSource == null) return;
            if (bossArrivedSfxIndex < 0 || bossArrivedSfxIndex >= am.EffectsList.Count) return;

            AudioClip clip = am.EffectsList[bossArrivedSfxIndex];
            if (clip == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[JiUGameManagerBossAudio] EffectsList[{bossArrivedSfxIndex}] is null; Boss arrived SFX skipped.", this);
#endif
                return;
            }

            am.EffectsSource.PlayOneShot(clip);
        }

        IEnumerator PlayBossArrivedOneShotIfNoInstantBossFailCoroutine(AudioManager am)
        {
            var gm = GameManager.Instance;
            if (gm == null)
            {
                TryPlayBossArrivedOneShot(am);
                yield break;
            }

            while (gm.BossIsHere && !gm.IsGameOver && gm.IsBossBrokeCheckAwaitingArrivalSprites)
                yield return null;

            if (!gm.BossIsHere || gm.IsGameOver)
                yield break;

            if (gm.GetBrokenWorkItemCount() > 0)
                yield break;

            TryPlayBossArrivedOneShot(am);
        }

        void OnBossLeaveStarted()
        {
            var am = AudioManager.Instance;
            if (am?.EffectsSource == null) return;
            if (bossLeftSfxIndex < 0 || bossLeftSfxIndex >= am.EffectsList.Count) return;
            am.PlaySound(bossLeftSfxIndex);
        }

        void OnBossLeft()
        {
            var am = AudioManager.Instance;
            if (am == null) return;

            if (musicAfterBossLeavesIndex >= 0 && musicAfterBossLeavesIndex < am.MusicList.Count)
                am.PlayMusic(musicAfterBossLeavesIndex);
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
