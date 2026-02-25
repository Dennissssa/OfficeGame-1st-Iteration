using UnityEngine;

namespace JiU
{
    /// <summary>
    /// Boss 预警开始时播放指定音频，Boss 实际到达时立刻停止。
    /// 依赖 GameManager 的 OnBossWarningStarted / OnBossArrived 事件。
    /// </summary>
    public class BossWarningAudio : MonoBehaviour
    {
        [Header("音频")]
        [Tooltip("Boss 预警阶段播放的 Clip")]
        public AudioClip warningClip;

        [Tooltip("不填则使用本物体上的 AudioSource，没有则自动添加")]
        public AudioSource audioSource;

        [Range(0f, 1f)]
        public float volume = 1f;

        [Tooltip("是否循环播放预警音（Boss 到达时会自动停）")]
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

        /// <summary> 预警开始时播放（由事件调用） </summary>
        public void PlayWarning()
        {
            if (warningClip == null || audioSource == null) return;
            audioSource.Stop();
            audioSource.clip = warningClip;
            audioSource.volume = volume;
            audioSource.loop = loop;
            audioSource.Play();
        }

        /// <summary> Boss 到达时立刻关闭（由事件调用） </summary>
        public void StopWarning()
        {
            if (audioSource != null)
                audioSource.Stop();
        }
    }
}
