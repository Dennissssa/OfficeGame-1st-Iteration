using UnityEngine;

namespace JiU
{
    /// <summary>
    /// One-shot "present" SFX when Boss actually arrives.
    /// Uses GameManager.OnBossArrived.
    /// </summary>
    public class BossPresentAudio : MonoBehaviour
    {
        [Header("Audio")]
        [Tooltip("One-shot clip when Boss is present / arrives")]
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
            GameManager.Instance.OnBossArrived.AddListener(PlayOnce);
        }

        void OnDestroy()
        {
            if (GameManager.Instance == null) return;
            GameManager.Instance.OnBossArrived.RemoveListener(PlayOnce);
        }

        /// <summary>Play once when Boss arrives (event).</summary>
        public void PlayOnce()
        {
            if (bossPresentClip == null || audioSource == null) return;
            audioSource.PlayOneShot(bossPresentClip, volume);
        }
    }
}
