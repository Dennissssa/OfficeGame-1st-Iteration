using UnityEngine;

namespace JiU
{
    /// <summary>
    /// Plays the assigned clip on trigger; stops immediately when repaired.
    /// If a WorkItem is assigned, plays on break and stops on fix without wiring events manually.
    /// </summary>
    public class PlaySoundOnEvent : MonoBehaviour
    {
        [Header("Target object (optional)")]
        [Tooltip("If set, auto-plays when that item breaks; no need to bind OnBroken")]
        public WorkItem workItem;

        [Header("Audio")]
        [Tooltip("Clip to play")]
        public AudioClip clip;

        [Tooltip("If unset, uses AudioSource on this object, or adds one")]
        public AudioSource audioSource;

        [Tooltip("Random pitch (0 = off; e.g. 0.9~1.1 reduces repetition)")]
        [Range(0f, 0.5f)]
        public float pitchRandomRange = 0f;

        [Tooltip("Volume 0~1")]
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
                workItem.OnFixed.AddListener(Stop); // Stop broken SFX immediately on fix
            }
        }

        /// <summary>
        /// Stops the broken SFX immediately. Auto-called from WorkItem.OnFixed when workItem is set; can bind manually.
        /// </summary>
        public void Stop()
        {
            if (audioSource != null)
                audioSource.Stop();
        }

        /// <summary>
        /// After phone pickup, <see cref="GameManager"/> stops broken-clip playback for WorkItems in suppress scope.
        /// </summary>
        public static void StopForPhonePickupAudioScope()
        {
            var arr = Object.FindObjectsOfType<PlaySoundOnEvent>(true);
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++)
            {
                PlaySoundOnEvent p = arr[i];
                if (p.workItem == null) continue;
                if (!GameManager.PickupAudioSuppressAppliesToBoundWorkItem(p.workItem)) continue;
                p.Stop();
            }
        }

        /// <summary>Pairs with <see cref="PlaySoundOnEventAudioManager.ResumeBrokenLoopAfterPhonePutdownForWorkItem"/> when only this component plays the broken clip.</summary>
        public static void ResumeBrokenClipAfterPhonePutdownForWorkItem(WorkItem wi)
        {
            if (wi == null || !wi.IsBroken) return;
            var arr = Object.FindObjectsOfType<PlaySoundOnEvent>(true);
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i].workItem != wi) continue;
                arr[i].Play();
            }
        }

        /// <summary>
        /// Plays broken SFX (Stop() ends it on fix). Bind from WorkItem.OnBroken etc.
        /// </summary>
        public void Play()
        {
            // Pickup suppress only blocks starting broken SFX (this component is the broken clip); repairs use other paths
            if (workItem != null && GameManager.PickupAudioSuppressAppliesToBoundWorkItem(workItem))
                return;
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
