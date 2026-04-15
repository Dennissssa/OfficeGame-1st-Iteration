using System.Collections;
using UnityEngine;

namespace JiU
{
    /// <summary>
    /// One-shot "present" SFX when Boss stay is safe from instant Broke-fail (same gate as <see cref="GameManager"/> Update check).
    /// Skips playback when the player will get Boss-caused game over (angry SFX only). Uses GameManager.OnBossArrived to start scheduling only.
    /// </summary>
    public class BossPresentAudio : MonoBehaviour
    {
        [Header("Audio")]
        [Tooltip("One-shot clip when Boss is present / stay is safe")]
        public AudioClip bossPresentClip;

        [Tooltip("If unset, uses or adds AudioSource on this object")]
        public AudioSource audioSource;

        [Range(0f, 1f)]
        public float volume = 1f;

        void Awake()
        {
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();
        }

        void Start()
        {
            if (GameManager.Instance == null) return;
            GameManager.Instance.OnBossArrived.AddListener(OnBossArrivedSchedulePresent);
        }

        void OnDestroy()
        {
            if (GameManager.Instance == null) return;
            GameManager.Instance.OnBossArrived.RemoveListener(OnBossArrivedSchedulePresent);
        }

        void OnBossArrivedSchedulePresent()
        {
            var gm = GameManager.Instance;
            if (gm == null)
            {
                PlayPresentClip();
                return;
            }

            gm.StartCoroutine(PlayPresentUnlessBossInstantFailCoroutine());
        }

        IEnumerator PlayPresentUnlessBossInstantFailCoroutine()
        {
            var gm = GameManager.Instance;
            if (gm == null)
                yield break;

            while (gm.BossIsHere && !gm.IsGameOver && gm.IsBossBrokeCheckAwaitingArrivalSprites)
                yield return null;

            if (!gm.BossIsHere || gm.IsGameOver)
                yield break;

            if (gm.GetBrokenWorkItemCount() > 0)
                yield break;

            PlayPresentClip();
        }

        void PlayPresentClip()
        {
            if (bossPresentClip == null || audioSource == null) return;
            audioSource.PlayOneShot(bossPresentClip, volume);
        }
    }
}
