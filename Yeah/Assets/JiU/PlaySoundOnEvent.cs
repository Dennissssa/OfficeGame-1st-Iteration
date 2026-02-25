using UnityEngine;

namespace JiU
{
    /// <summary>
    /// 当收到“触发”时播放指定音频；修复时会立即停止播放。
    /// 若填写了“指定 WorkItem”，会在损坏时播放、修好时自动停止，无需再拖事件。
    /// </summary>
    public class PlaySoundOnEvent : MonoBehaviour
    {
        [Header("指定物件（可选）")]
        [Tooltip("若指定，则在该物件损坏时自动播放，无需再绑 OnBroken")]
        public WorkItem workItem;

        [Header("音频")]
        [Tooltip("要播放的音频片段")]
        public AudioClip clip;

        [Tooltip("不填则使用本物体上的 AudioSource，没有则自动添加一个")]
        public AudioSource audioSource;

        [Tooltip("是否随机音调（0=不随机，例如 0.9~1.1 可避免重复感）")]
        [Range(0f, 0.5f)]
        public float pitchRandomRange = 0f;

        [Tooltip("音量 0~1")]
        [Range(0f, 1f)]
        public float volumeScale = 1f;

        void Awake()
        {
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();
        }

        void Start()
        {
            if (workItem != null)
            {
                workItem.OnBroken.AddListener(Play);
                workItem.OnFixed.AddListener(Stop);  // 修好时立刻结束正在播放的损坏音效
            }
        }

        /// <summary>
        /// 立刻停止当前播放的损坏音效。由 WorkItem.OnFixed 自动调用（若已指定 workItem），也可手动绑定。
        /// </summary>
        public void Stop()
        {
            if (audioSource != null)
                audioSource.Stop();
        }

        /// <summary>
        /// 播放损坏音效（修好后会通过 Stop() 立刻结束）。由 WorkItem.OnBroken 等事件绑定调用。
        /// </summary>
        public void Play()
        {
            if (clip == null || audioSource == null) return;

            if (pitchRandomRange > 0f)
                audioSource.pitch = Random.Range(1f - pitchRandomRange, 1f + pitchRandomRange);
            else
                audioSource.pitch = 1f;

            audioSource.volume = volumeScale;
            audioSource.clip = clip;
            audioSource.Play();
        }
    }
}
