using UnityEngine;
using UnityEngine.Events;

namespace JiU
{
    /// <summary>
    /// Uses AudioManager.EffectsList indices for clips, played on <strong>this GameObject's AudioSource</strong> (added automatically if missing),
    /// so global EffectsSource / Boss audio etc. do not fight for the same output. Optionally skip auto broken/Bait sounds.
    /// </summary>
    public class PlaySoundOnEventAudioManager : MonoBehaviour
    {
        [Header("Local playback (this object)")]
        [Tooltip("If unset, adds a dedicated AudioSource. Multiple components on one object must not share GetComponent<AudioSource>() or they steal one voice and OnFixed Stops can cut Win/Lose.")]
        public AudioSource localSfxSource;

        [Header("Optional: skip broken / Bait auto sounds")]
        [Tooltip("When enabled, OnBroken / OnBaiting / OnBaitingEnded do not play on this source; repair Win/Lose still apply")]
        public bool skipBrokenAndBaitSounds = false;

        [Header("AudioManager.EffectsList — broken")]
        [Tooltip("Play on OnBroken; -1 = off")]
        public int effectIndex = 0;

        [Tooltip("Loop broken SFX until OnFixed")]
        public bool loopBrokenSound = false;

        [Header("AudioManager.EffectsList — bait")]
        [Tooltip("Play when entering Bait; -1 = off")]
        public int baitEffectIndex = -1;

        [Tooltip("Loop bait hint until repaired or bait expires")]
        public bool loopBaitSound = false;

        [Tooltip("Play when bait timer ends naturally; -1 = off")]
        public int baitEndedEffectIndex = -1;

        [Header("AudioManager.EffectsList — repair feedback (Win / Lose)")]
        [Tooltip("When IsBroken and repair succeeds; -1 = off")]
        public int repairCorrectEffectIndex = -1;

        [Tooltip("Wrong repair (bait spam, idle spam, etc.); -1 = off")]
        public int repairWrongEffectIndex = -1;

        [Header("WorkItem binding")]
        [Tooltip("When set, subscribes to broken/fixed, bait/end, repair Win/Lose")]
        public WorkItem workItem;

        [Header("Random pitch")]
        [Range(0f, 0.5f)]
        public float pitchRandomRange = 0f;

        [Header("Other events (optional)")]
        public UnityEvent onPlayed;

        /// <summary>If bait ends naturally same frame as OnFixed and baitEnded clip just played, skip Stop this frame.</summary>
        bool _suppressNextFixedStop;

        /// <summary>Repair Win already playing on this source same frame as OnFixed: skip Stop so Win is not cut off.</summary>
        bool _suppressNextFixedStopForRepairWin;

        /// <summary>Pickup suppress: broken SFX never started; OnFixed skips Stop to avoid cutting repair feedback.</summary>
        bool _brokenPlaybackSkippedDueToPhonePickup;

        /// <summary>Pickup suppress: bait SFX never started; same OnFixed handling.</summary>
        bool _baitingPlaybackSkippedDueToPhonePickup;

        /// <summary>Debug: true when OnBroken skipped broken SFX due to pickup, until consumed on OnFixed.</summary>
        public bool DebugPeekBrokenPlaybackSkippedDueToPhonePickup => _brokenPlaybackSkippedDueToPhonePickup;

        /// <summary>Debug: true when OnBaiting skipped bait SFX due to pickup, until consumed on OnFixed.</summary>
        public bool DebugPeekBaitingPlaybackSkippedDueToPhonePickup => _baitingPlaybackSkippedDueToPhonePickup;

        void Awake()
        {
            EnsureLocalAudioSource();
        }

        AudioSource EnsureLocalAudioSource()
        {
            if (localSfxSource != null)
                return localSfxSource;

            // Each component needs its own AudioSource; sharing via GetComponent makes one object's OnFixed Stop another's Win/Lose.
            localSfxSource = gameObject.AddComponent<AudioSource>();
            localSfxSource.playOnAwake = false;
            return localSfxSource;
        }

        void Start()
        {
            if (workItem == null) return;

            workItem.OnBroken.AddListener(OnBroken);
            workItem.OnFixed.AddListener(OnFixed);
            workItem.OnBaiting.AddListener(OnBaiting);
            workItem.OnBaitingEnded.AddListener(OnBaitingEnded);
            workItem.OnRepairCorrect.AddListener(OnRepairCorrect);
            workItem.OnRepairIncorrect.AddListener(OnRepairIncorrect);
        }

        void OnDestroy()
        {
            if (workItem == null) return;

            workItem.OnBroken.RemoveListener(OnBroken);
            workItem.OnFixed.RemoveListener(OnFixed);
            workItem.OnBaiting.RemoveListener(OnBaiting);
            workItem.OnBaitingEnded.RemoveListener(OnBaitingEnded);
            workItem.OnRepairCorrect.RemoveListener(OnRepairCorrect);
            workItem.OnRepairIncorrect.RemoveListener(OnRepairIncorrect);
        }

        bool PickupSuppressBrokenBaitNow()
        {
            return GameManager.PickupAudioSuppressAppliesToBoundWorkItem(workItem);
        }

        void OnRepairCorrect()
        {
            if (repairCorrectEffectIndex < 0) return;
            var src = EnsureLocalAudioSource();
            if (src == null) return;

            src.loop = false;
            if (PlayClipFromEffectsList(repairCorrectEffectIndex))
                _suppressNextFixedStopForRepairWin = true;
        }

        void OnRepairIncorrect()
        {
            if (repairWrongEffectIndex < 0) return;

            var am = AudioManager.Instance;
            if (am == null || am.EffectsList == null || repairWrongEffectIndex >= am.EffectsList.Count) return;
            var wrongClip = am.EffectsList[repairWrongEffectIndex];
            if (wrongClip == null) return;

            var src = EnsureLocalAudioSource();
            if (src == null) return;

            // Phone on cradle, still broken, broken loop playing: use OneShot for wrong-repair to avoid swapping clip and killing the loop
            bool phoneOnHookKeepBrokenLoop = workItem != null
                && workItem.IsBroken
                && workItem.PhoneIsOnCradleForSfx
                && loopBrokenSound;

            if (phoneOnHookKeepBrokenLoop)
            {
                src.PlayOneShot(wrongClip);
                onPlayed?.Invoke();
                return;
            }

            src.loop = false;
            PlayClipFromEffectsList(repairWrongEffectIndex);
        }

        void OnBroken()
        {
            if (skipBrokenAndBaitSounds) return;
            _brokenPlaybackSkippedDueToPhonePickup = false;
            if (effectIndex < 0) return;
            if (PickupSuppressBrokenBaitNow())
            {
                _brokenPlaybackSkippedDueToPhonePickup = true;
                return;
            }

            var am = AudioManager.Instance;
            if (am == null || am.EffectsList == null || effectIndex >= am.EffectsList.Count) return;

            var src = EnsureLocalAudioSource();
            if (src == null) return;

            src.loop = loopBrokenSound;
            PlayClipFromEffectsList(effectIndex);
        }

        void OnFixed()
        {
            if (_suppressNextFixedStop)
            {
                _suppressNextFixedStop = false;
                return;
            }

            if (_suppressNextFixedStopForRepairWin)
            {
                _suppressNextFixedStopForRepairWin = false;
                return;
            }

            // With skip broken/bait, this source never started broken loop; Stop would cut Win/Lose played same frame as fix
            // (noticeable on lamp/printer-style items that rely on hit feedback rather than a loop)
            if (skipBrokenAndBaitSounds)
                return;

            if (_brokenPlaybackSkippedDueToPhonePickup)
            {
                _brokenPlaybackSkippedDueToPhonePickup = false;
                return;
            }

            if (_baitingPlaybackSkippedDueToPhonePickup)
            {
                _baitingPlaybackSkippedDueToPhonePickup = false;
                return;
            }

            StopInternal(resetLoop: true);
        }

        void OnBaiting()
        {
            if (skipBrokenAndBaitSounds) return;
            _baitingPlaybackSkippedDueToPhonePickup = false;
            if (baitEffectIndex < 0) return;
            if (PickupSuppressBrokenBaitNow())
            {
                _baitingPlaybackSkippedDueToPhonePickup = true;
                return;
            }

            var am = AudioManager.Instance;
            if (am == null || am.EffectsList == null || baitEffectIndex >= am.EffectsList.Count) return;

            var src = EnsureLocalAudioSource();
            if (src == null) return;

            src.loop = loopBaitSound;
            PlayClipFromEffectsList(baitEffectIndex);
        }

        void OnBaitingEnded()
        {
            if (skipBrokenAndBaitSounds) return;

            var am = AudioManager.Instance;
            if (am?.EffectsList == null) return;

            var src = EnsureLocalAudioSource();
            if (src == null) return;

            src.loop = false;
            src.Stop();

            bool suppressBaitEndedClip = PickupSuppressBrokenBaitNow();
            if (baitEndedEffectIndex >= 0 && baitEndedEffectIndex < am.EffectsList.Count && !suppressBaitEndedClip)
            {
                PlayClipFromEffectsList(baitEndedEffectIndex);
                _suppressNextFixedStop = true;
            }
        }

        void StopInternal(bool resetLoop)
        {
            if (localSfxSource == null) return;
            var src = localSfxSource;
            if (resetLoop)
                src.loop = false;
            src.Stop();
        }

        /// <summary>Stop this object's item SFX source.</summary>
        public void Stop()
        {
            StopInternal(resetLoop: true);
        }

        /// <summary>Play broken clip by index; looping matches loopBrokenSound.</summary>
        public void Play()
        {
            if (skipBrokenAndBaitSounds) return;
            if (effectIndex < 0) return;
            if (PickupSuppressBrokenBaitNow()) return;

            var am = AudioManager.Instance;
            if (am == null || am.EffectsList == null || effectIndex >= am.EffectsList.Count) return;

            var src = EnsureLocalAudioSource();
            if (src == null) return;

            src.loop = loopBrokenSound;
            PlayClipFromEffectsList(effectIndex);
        }

        /// <summary>Manually play bait clip by index (e.g. from UnityEvent).</summary>
        public void PlayBaitSound()
        {
            if (skipBrokenAndBaitSounds) return;
            if (baitEffectIndex < 0) return;
            if (PickupSuppressBrokenBaitNow()) return;

            var am = AudioManager.Instance;
            if (am == null || am.EffectsList == null || baitEffectIndex >= am.EffectsList.Count) return;

            var src = EnsureLocalAudioSource();
            if (src == null) return;

            if (loopBaitSound)
                src.loop = true;
            PlayClipFromEffectsList(baitEffectIndex);
        }

        /// <summary>Play EffectsList index on this local source (ignores skipBrokenAndBaitSounds; for manual UnityEvent calls).</summary>
        public void PlayAtIndex(int index)
        {
            PlayClipFromEffectsList(index);
        }

        /// <returns>True if playback started.</returns>
        bool PlayClipFromEffectsList(int index)
        {
            var src = EnsureLocalAudioSource();
            if (src == null) return false;

            var am = AudioManager.Instance;
            if (am == null || am.EffectsList == null || index < 0 || index >= am.EffectsList.Count) return false;

            var clip = am.EffectsList[index];
            if (clip == null) return false;

            if (pitchRandomRange > 0f)
                src.pitch = Random.Range(1f - pitchRandomRange, 1f + pitchRandomRange);
            else
                src.pitch = 1f;

            src.clip = clip;
            src.Play();
            onPlayed?.Invoke();
            return true;
        }

        /// <summary>
        /// After phone pickup: for components in <see cref="GameManager.PickupAudioSuppressAppliesToBoundWorkItem"/> scope,
        /// stop local broken/bait loops and clear related flags.
        /// Call after <see cref="GameManager.SuppressNonBaitBrokeItemSfxFromPhonePickup"/> is enabled.
        /// </summary>
        public static void StopBrokenBaitPlaybackForPickupScope()
        {
            var arr = Object.FindObjectsOfType<PlaySoundOnEventAudioManager>(true);
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++)
            {
                PlaySoundOnEventAudioManager m = arr[i];
                if (m.workItem == null) continue;
                if (!GameManager.PickupAudioSuppressAppliesToBoundWorkItem(m.workItem)) continue;
                m.StopBrokenBaitPlaybackDueToPhonePickupInternal();
            }
        }

        /// <summary>
        /// Phone on hook, still broken, had pickup while broken (broken SFX was stopped): restart broken loop.
        /// Called from <see cref="WorkItem.NotifyPhonePutDownForBaitFlow"/>.
        /// </summary>
        public static void ResumeBrokenLoopAfterPhonePutdownForWorkItem(WorkItem wi)
        {
            if (wi == null || !wi.IsBroken) return;
            var arr = Object.FindObjectsOfType<PlaySoundOnEventAudioManager>(true);
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i].workItem != wi) continue;
                arr[i].ResumeBrokenLoopAfterPhonePutdownInternal();
            }
        }

        void ResumeBrokenLoopAfterPhonePutdownInternal()
        {
            if (skipBrokenAndBaitSounds) return;
            if (workItem == null || !workItem.IsBroken) return;
            if (effectIndex < 0) return;
            if (PickupSuppressBrokenBaitNow()) return;

            var am = AudioManager.Instance;
            if (am == null || am.EffectsList == null || effectIndex >= am.EffectsList.Count) return;

            var src = EnsureLocalAudioSource();
            if (src == null) return;

            src.loop = loopBrokenSound;
            PlayClipFromEffectsList(effectIndex);
        }

        void StopBrokenBaitPlaybackDueToPhonePickupInternal()
        {
            if (localSfxSource != null)
            {
                localSfxSource.loop = false;
                localSfxSource.Stop();
            }

            _brokenPlaybackSkippedDueToPhonePickup = false;
            _baitingPlaybackSkippedDueToPhonePickup = false;
            _suppressNextFixedStop = false;
            _suppressNextFixedStopForRepairWin = false;
        }

        /// <summary>
        /// Called from <see cref="GameManager"/> on phone pickup/hang-up: logs pickup-suppress skip flags for each component.
        /// </summary>
        public static void DebugLogAllPhonePickupSkipFlags(string momentLabel, bool suppressNonBaitBrokeSfxActive)
        {
            var arr = Object.FindObjectsOfType<PlaySoundOnEventAudioManager>(true);
            if (arr == null || arr.Length == 0)
            {
                Debug.Log(
                    $"[PlaySoundOnEventAudioManager] {momentLabel} | no components in scene | " +
                    $"SuppressNonBaitBrokeItemSfxFromPhonePickup={suppressNonBaitBrokeSfxActive}");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(
                $"[PlaySoundOnEventAudioManager] {momentLabel} | " +
                $"SuppressNonBaitBrokeItemSfxFromPhonePickup={suppressNonBaitBrokeSfxActive} | componentCount={arr.Length}");
            for (int i = 0; i < arr.Length; i++)
            {
                PlaySoundOnEventAudioManager m = arr[i];
                string wi = m.workItem != null ? m.workItem.name : "null";
                bool inScope = GameManager.PickupAudioSuppressAppliesToBoundWorkItem(m.workItem);
                sb.AppendLine(
                    $"  [{i}] \"{m.gameObject.name}\" workItem={wi} inPickupSuppressScope={inScope} " +
                    $"_brokenPlaybackSkippedDueToPhonePickup={m._brokenPlaybackSkippedDueToPhonePickup} " +
                    $"_baitingPlaybackSkippedDueToPhonePickup={m._baitingPlaybackSkippedDueToPhonePickup}");
            }

            Debug.Log(sb.ToString().TrimEnd());
        }
    }
}
