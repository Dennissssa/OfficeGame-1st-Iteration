using UnityEngine;

namespace JiU
{
    /// <summary>
    /// One-shot "angry" SFX when Boss check causes game over.
    /// Uses GameManager.OnGameOverBossCaused. Game may be paused (Time.timeScale=0);
    /// playback ignores time scale so the clip can finish.
    /// </summary>
    public class BossAngryAudio : MonoBehaviour
    {
        [Header("Audio")]
        [Tooltip("One-shot angry clip on Boss-caused game over")]
        public AudioClip angryClip;

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
            // Game over may use timeScale=0; PlayOneShot still plays in real time
            audioSource.ignoreListenerPause = true;
        }

        void Start()
        {
            if (GameManager.Instance == null) return;
            GameManager.Instance.OnGameOverBossCaused.AddListener(PlayOnce);
        }

        void OnDestroy()
        {
            if (GameManager.Instance == null) return;
            GameManager.Instance.OnGameOverBossCaused.RemoveListener(PlayOnce);
        }

        /// <summary>Play once on Boss-caused game over (event).</summary>
        public void PlayOnce()
        {
            if (angryClip == null || audioSource == null) return;
            audioSource.PlayOneShot(angryClip, volume);
        }
    }
}
