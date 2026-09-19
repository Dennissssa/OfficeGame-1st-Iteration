using UnityEngine;

namespace JiU
{
    /// <summary>
    /// Plays warning audio when Boss warning starts; stops when Boss arrives.
    /// Uses GameManager OnBossWarningStarted / OnBossArrived.
    /// </summary>
    public class BossWarningAudio : MonoBehaviour
    {
        [Header("Audio")]
        [Tooltip("Clip during Boss warning phase")]
        public AudioClip warningClip;

        [Tooltip("If unset, uses or adds AudioSource on this object")]
        public AudioSource audioSource;

        [Range(0f, 1f)]
        public float volume = 1f;

        [Tooltip("Loop warning clip (stops when Boss arrives)")]
        public bool loop = true;

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

            GameManager.Instance.OnBossWarningStarted.AddListener(PlayWarning);
            GameManager.Instance.OnBossArrived.AddListener(StopWarning);
        }

        void OnDestroy()
        {
            if (GameManager.Instance == null) return;
            GameManager.Instance.OnBossWarningStarted.RemoveListener(PlayWarning);
            GameManager.Instance.OnBossArrived.RemoveListener(StopWarning);
        }

        /// <summary>Start warning playback (event).</summary>
        public void PlayWarning()
        {
            if (warningClip == null || audioSource == null) return;
            audioSource.Stop();
            audioSource.clip = warningClip;
            audioSource.volume = volume;
            audioSource.loop = loop;
            audioSource.Play();
        }

        /// <summary>Stop when Boss arrives (event).</summary>
        public void StopWarning()
        {
            if (audioSource != null)
                audioSource.Stop();
        }
    }
}
