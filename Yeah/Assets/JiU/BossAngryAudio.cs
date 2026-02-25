using UnityEngine;

namespace JiU
{
    /// <summary>
    /// 因 Boss 检查导致游戏失败时播放一次“生气”音效。
    /// 依赖 GameManager 的 OnGameOverBossCaused 事件。失败时场景已暂停(Time.timeScale=0)，
    /// 本组件会确保使用不受时间缩放影响的播放方式，保证音效能正常播完。
    /// </summary>
    public class BossAngryAudio : MonoBehaviour
    {
        [Header("音频")]
        [Tooltip("Boss 导致失败时播放一次的生气 Clip")]
        public AudioClip angryClip;

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
            // 失败时 Time.timeScale=0，此处用 PlayOneShot 不受影响，音效会按真实时间播完
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

        /// <summary> Boss 导致失败时播放一次（由事件调用） </summary>
        public void PlayOnce()
        {
            if (angryClip == null || audioSource == null) return;
            audioSource.PlayOneShot(angryClip, volume);
        }
    }
}
