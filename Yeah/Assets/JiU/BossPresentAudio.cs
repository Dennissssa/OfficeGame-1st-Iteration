using UnityEngine;

namespace JiU
{
    /// <summary>
    /// Boss 实际到达时播放一次指定的“在场”音效。
    /// 依赖 GameManager 的 OnBossArrived 事件。
    /// </summary>
    public class BossPresentAudio : MonoBehaviour
    {
        [Header("音频")]
        [Tooltip("Boss 在场时播放一次的 Clip")]
        public AudioClip bossPresentClip;

        [Tooltip("不填则使用本物体上的 AudioSource，没有则自动添加")]
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

        /// <summary> Boss 到达时播放一次（由事件调用） </summary>
        public void PlayOnce()
        {
            if (bossPresentClip == null || audioSource == null) return;
            audioSource.PlayOneShot(bossPresentClip, volume);
        }
    }
}
