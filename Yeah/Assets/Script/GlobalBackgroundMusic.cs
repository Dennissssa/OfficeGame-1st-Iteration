using UnityEngine;
using UnityEngine.SceneManagement;

namespace JiU
{
    /// <summary>
    /// Global BGM: dedicated <see cref="AudioSource"/>, <see cref="DontDestroyOnLoad"/>, separate from project AudioManager.
    /// One object with this script in the scene; only the first instance survives scene reloads.
    /// Call <see cref="PlayTheme"/> from scene events (StartScene / Intro / tutorial / endings).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class GlobalBackgroundMusic : MonoBehaviour
    {
        public static GlobalBackgroundMusic Instance { get; private set; }

        public enum Theme
        {
            None = 0,
            Normal = 1,
            Stupid = 2,
            Main = 3,
            BadEnding = 4,
            GoodEnding = 5,
        }

        [Header("Optional: play on first scene")]
        [Tooltip("Auto-play after entering play mode if nothing is playing yet")]
        public AudioClip playOnStart;

        [Range(0f, 1f)]
        public float playOnStartVolume = 1f;

        [Header("Scene auto-switch")]
        [Tooltip("Loading this scene name switches to Theme.Normal (StartScene).")]
        public string startSceneName = "StartScene";

        [Tooltip("Crossfade length in seconds. 0 = instant cut. Uses unscaled time so endings still fade when timeScale is 0.")]
        [Min(0f)]
        public float fadeSeconds = 0.75f;

        [Header("Theme clips")]
        public AudioClip themeNormal;
        public AudioClip themeStupid;
        public AudioClip themeMain;
        public AudioClip themeBadEnding;
        public AudioClip themeGoodEnding;

        [Header("Theme loop")]
        public bool loopNormal = true;
        public bool loopStupid = true;
        public bool loopMain = true;
        [Tooltip("Uncheck so Bad Ending plays once, then the AudioSource stops (loop is restored on the next looping theme).")]
        public bool loopBadEnding = false;
        [Tooltip("Uncheck so Good Ending plays once, then the AudioSource stops (loop is restored on the next looping theme).")]
        public bool loopGoodEnding = false;

        [Header("Theme volume")]
        [Range(0f, 1f)] public float volumeNormal = 0.2f;
        [Range(0f, 1f)] public float volumeStupid = 0.2f;
        [Range(0f, 1f)] public float volumeMain = 0.2f;
        [Range(0f, 1f)] public float volumeBadEnding = 0.2f;
        [Range(0f, 1f)] public float volumeGoodEnding = 0.2f;

        AudioSource _a;
        AudioSource _b;
        AudioSource _front;
        AudioSource _fadeFrom;
        AudioSource _fadeTo;
        float _fadeFromStartVolume;
        float _fadeToVolume;
        float _fadeElapsed;
        bool _fading;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            _a = GetComponent<AudioSource>();
            ConfigureSource(_a);

            _b = gameObject.AddComponent<AudioSource>();
            ConfigureSource(_b);
            _b.outputAudioMixerGroup = _a.outputAudioMixerGroup;

            _front = _a;
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void Start()
        {
            if (playOnStart != null && _front != null && !_front.isPlaying)
                Play(playOnStart, GetVolume(Theme.Normal));
        }

        void Update()
        {
            if (!_fading) return;

            _fadeElapsed += Time.unscaledDeltaTime;
            float t = fadeSeconds <= 0.0001f ? 1f : Mathf.Clamp01(_fadeElapsed / fadeSeconds);

            if (_fadeTo != null)
                _fadeTo.volume = Mathf.Lerp(0f, _fadeToVolume, t);
            if (_fadeFrom != null)
                _fadeFrom.volume = Mathf.Lerp(_fadeFromStartVolume, 0f, t);

            if (t < 1f) return;

            _fading = false;
            if (_fadeFrom != null)
            {
                _fadeFrom.Stop();
                _fadeFrom.clip = null;
                _fadeFrom.volume = 0f;
            }
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this)
                Instance = null;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Instance != this) return;
            if (!string.IsNullOrEmpty(startSceneName) && scene.name == startSceneName)
                PlayTheme(Theme.Normal);
        }

        static void ConfigureSource(AudioSource source)
        {
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
        }

        /// <summary>Switch BGM by named theme. No-op if clip is missing or the same clip is already playing.</summary>
        public void PlayTheme(Theme theme)
        {
            if (theme == Theme.None) return;

            AudioClip clip = GetClip(theme);
            if (clip == null)
            {
                Debug.LogWarning($"[GlobalBackgroundMusic] Theme {theme} has no clip assigned.", this);
                return;
            }

            Play(clip, GetVolume(theme), GetLoop(theme));
        }

        public void PlayNormalTheme() => PlayTheme(Theme.Normal);
        public void PlayStupidTheme() => PlayTheme(Theme.Stupid);
        public void PlayMainTheme() => PlayTheme(Theme.Main);
        public void PlayBadEndingTheme() => PlayTheme(Theme.BadEnding);
        public void PlayGoodEndingTheme() => PlayTheme(Theme.GoodEnding);

        /// <summary>Switch BGM; no-op if same clip already playing.</summary>
        public void Play(AudioClip clip, float volume = 1f)
        {
            Play(clip, volume, true);
        }

        public void Play(AudioClip clip, float volume, bool loop)
        {
            if (clip == null || _front == null) return;

            float vol = Mathf.Clamp01(volume);

            if (_front.clip == clip && _front.isPlaying)
            {
                _front.volume = vol;
                _front.loop = loop;
                if (_fading && _fadeFrom != null)
                {
                    _fadeFrom.Stop();
                    _fadeFrom.clip = null;
                    _fading = false;
                }
                return;
            }

            AudioSource next = _front == _a ? _b : _a;
            AudioSource prev = _front;

            next.clip = clip;
            next.loop = loop;
            next.volume = 0f;
            next.Play();

            _front = next;
            _fadeFrom = prev;
            _fadeTo = next;
            _fadeToVolume = vol;
            _fadeFromStartVolume = prev.isPlaying ? prev.volume : 0f;
            _fadeElapsed = 0f;
            _fading = true;

            if (fadeSeconds <= 0.0001f)
            {
                next.volume = vol;
                prev.Stop();
                prev.clip = null;
                prev.volume = 0f;
                _fading = false;
            }
        }

        public void Stop()
        {
            _fading = false;
            if (_a != null) _a.Stop();
            if (_b != null) _b.Stop();
        }

        public void Pause()
        {
            if (_a != null) _a.Pause();
            if (_b != null) _b.Pause();
        }

        public void Resume()
        {
            if (_a != null) _a.UnPause();
            if (_b != null) _b.UnPause();
        }

        public void SetVolume(float volume)
        {
            ApplyLiveVolume(Mathf.Clamp01(volume));
        }

        public void SetThemeVolume(Theme theme, float volume)
        {
            float vol = Mathf.Clamp01(volume);
            switch (theme)
            {
                case Theme.Normal: volumeNormal = vol; break;
                case Theme.Stupid: volumeStupid = vol; break;
                case Theme.Main: volumeMain = vol; break;
                case Theme.BadEnding: volumeBadEnding = vol; break;
                case Theme.GoodEnding: volumeGoodEnding = vol; break;
                default: return;
            }

            if (_front != null && _front.clip == GetClip(theme))
                ApplyLiveVolume(vol);
        }

        void ApplyLiveVolume(float vol)
        {
            if (_fading)
            {
                _fadeToVolume = vol;
                return;
            }
            if (_front != null)
                _front.volume = vol;
        }

        public bool IsPlaying => _front != null && _front.isPlaying;

        public AudioClip CurrentClip => _front != null ? _front.clip : null;

        AudioClip GetClip(Theme theme)
        {
            switch (theme)
            {
                case Theme.Normal: return themeNormal != null ? themeNormal : playOnStart;
                case Theme.Stupid: return themeStupid;
                case Theme.Main: return themeMain;
                case Theme.BadEnding: return themeBadEnding;
                case Theme.GoodEnding: return themeGoodEnding;
                default: return null;
            }
        }

        bool GetLoop(Theme theme)
        {
            switch (theme)
            {
                case Theme.Normal: return loopNormal;
                case Theme.Stupid: return loopStupid;
                case Theme.Main: return loopMain;
                case Theme.BadEnding: return loopBadEnding;
                case Theme.GoodEnding: return loopGoodEnding;
                default: return true;
            }
        }

        float GetVolume(Theme theme)
        {
            switch (theme)
            {
                case Theme.Normal: return volumeNormal;
                case Theme.Stupid: return volumeStupid;
                case Theme.Main: return volumeMain;
                case Theme.BadEnding: return volumeBadEnding;
                case Theme.GoodEnding: return volumeGoodEnding;
                default: return playOnStartVolume;
            }
        }
    }
}
