using UnityEngine;

namespace JiU
{
    /// <summary>
    /// Global BGM: dedicated <see cref="AudioSource"/>, <see cref="DontDestroyOnLoad"/>, separate from project AudioManager.
    /// One object with this script in the scene; only the first instance survives scene reloads.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class GlobalBackgroundMusic : MonoBehaviour
    {
        public static GlobalBackgroundMusic Instance { get; private set; }

        [Header("Optional: play on first scene")]
        [Tooltip("Auto-play after entering play mode if nothing is playing yet")]
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

        /// <summary>Switch BGM; no-op if same clip already playing.</summary>
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
