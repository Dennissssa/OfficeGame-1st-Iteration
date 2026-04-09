using UnityEngine;

namespace JiU
{
    /// <summary>
    /// 全局背景音乐：独立 <see cref="AudioSource"/>，<see cref="DontDestroyOnLoad"/>，与项目内 AudioManager 无关。
    /// 场景中放一个带本脚本的物体即可；重复进入场景时只保留第一个实例。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class GlobalBackgroundMusic : MonoBehaviour
    {
        public static GlobalBackgroundMusic Instance { get; private set; }

        [Header("可选：首场景自动播")]
        [Tooltip("进入游戏后自动播放（仅当当前没有在播）")]
        public AudioClip playOnStart;

        [Range(0f, 1f)]
        public float playOnStartVolume = 1f;

        AudioSource _source;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = true;
        }

        void Start()
        {
            if (playOnStart != null && _source != null && !_source.isPlaying)
                Play(playOnStart, playOnStartVolume);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>切换 BGM；与当前为同一 Clip 且已在播则不变。</summary>
        public void Play(AudioClip clip, float volume = 1f)
        {
            if (clip == null || _source == null) return;

            if (_source.clip == clip && _source.isPlaying)
            {
                _source.volume = Mathf.Clamp01(volume);
                return;
            }

            _source.clip = clip;
            _source.volume = Mathf.Clamp01(volume);
            _source.Play();
        }

        public void Stop()
        {
            if (_source != null)
                _source.Stop();
        }

        public void Pause()
        {
            if (_source != null)
                _source.Pause();
        }

        public void Resume()
        {
            if (_source != null)
                _source.UnPause();
        }

        public void SetVolume(float volume)
        {
            if (_source != null)
                _source.volume = Mathf.Clamp01(volume);
        }

        public bool IsPlaying => _source != null && _source.isPlaying;

        public AudioClip CurrentClip => _source != null ? _source.clip : null;
    }
}
