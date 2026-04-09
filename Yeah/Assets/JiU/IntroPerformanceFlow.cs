using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace JiU
{
    /// <summary>
    /// Intro sequence: enter SFX + wait -> toggle objects -> fill Slider -> SFX -> toggle + SFX -> start dialogue.
    /// Uses realtime by default (ignores Time.timeScale) so it works with paused UI.
    /// </summary>
    public class IntroPerformanceFlow : MonoBehaviour
    {
        [Tooltip("If unset, adds AudioSource on this object")]
        public AudioSource sfxSource;

        [Tooltip("Use realtime for the whole sequence (recommended); off uses scaled time")]
        public bool useUnscaledTime = true;

        [Header("1) Scene enter")]
        public AudioClip sceneEnterSfx;

        [Min(0f)]
        [Tooltip("Seconds to wait after enter SFX")]
        public float waitAfterEnterSfxSeconds = 1f;

        [Header("2) Toggle after enter wait")]
        public List<GameObject> disableAfterEnterWait = new List<GameObject>();
        public List<GameObject> enableAfterEnterWait = new List<GameObject>();

        [Header("3) Slider fill")]
        public Slider progressSlider;

        [Min(0.01f)]
        [Tooltip("Seconds to animate from min to max value")]
        public float sliderFillDurationSeconds = 3f;

        [Tooltip("Maps fill progress 0~1; omit or empty curve for linear")]
        public AnimationCurve sliderFillCurve;

        [Header("4) After slider full")]
        public AudioClip sliderFilledSfx;

        [Header("5) Then: toggles + SFX (together)")]
        public List<GameObject> disableWhenSliderDone = new List<GameObject>();
        public List<GameObject> enableWhenSliderDone = new List<GameObject>();
        public AudioClip withToggleSfx;

        [Header("6) Dialogue (after With Toggle Sfx finishes)")]
        [Tooltip("Optional; omit to skip. If With Toggle Sfx is unset, dialogue starts right after toggles")]
        public DialogueController dialogueController;

        Coroutine _routine;

        void Start()
        {
            _routine = StartCoroutine(RunSequence());
        }

        void OnDestroy()
        {
            if (_routine != null)
                StopCoroutine(_routine);
        }

        void EnsureSfxSource()
        {
            if (sfxSource != null) return;
            sfxSource = GetComponent<AudioSource>();
            if (sfxSource == null)
                sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
        }

        float DeltaTime() => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        IEnumerator WaitSeconds(float seconds)
        {
            if (seconds <= 0f) yield break;
            float t = 0f;
            while (t < seconds)
            {
                t += DeltaTime();
                yield return null;
            }
        }

        IEnumerator RunSequence()
        {
            EnsureSfxSource();

            if (sceneEnterSfx != null && sfxSource != null)
                sfxSource.PlayOneShot(sceneEnterSfx);

            yield return WaitSeconds(waitAfterEnterSfxSeconds);

            SetActiveList(disableAfterEnterWait, false);
            SetActiveList(enableAfterEnterWait, true);

            if (progressSlider != null)
            {
                progressSlider.interactable = false;
                float minV = progressSlider.minValue;
                float maxV = progressSlider.maxValue;
                progressSlider.value = minV;

                float dur = Mathf.Max(0.01f, sliderFillDurationSeconds);
                float elapsed = 0f;
                bool useCurve = sliderFillCurve != null && sliderFillCurve.length > 0;

                while (elapsed < dur)
                {
                    elapsed += DeltaTime();
                    float u = Mathf.Clamp01(elapsed / dur);
                    if (useCurve)
                        u = sliderFillCurve.Evaluate(u);
                    progressSlider.value = Mathf.LerpUnclamped(minV, maxV, u);
                    yield return null;
                }

                progressSlider.value = maxV;
            }

            if (sliderFilledSfx != null && sfxSource != null)
                sfxSource.PlayOneShot(sliderFilledSfx);

            SetActiveList(disableWhenSliderDone, false);
            SetActiveList(enableWhenSliderDone, true);
            if (withToggleSfx != null && sfxSource != null)
            {
                sfxSource.PlayOneShot(withToggleSfx);
                yield return WaitSeconds(withToggleSfx.length);
            }

            if (dialogueController != null)
                dialogueController.StartDialogue();

            _routine = null;
        }

        static void SetActiveList(List<GameObject> list, bool active)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null)
                    list[i].SetActive(active);
            }
        }
    }
}
