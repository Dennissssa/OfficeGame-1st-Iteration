using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace JiU
{
    /// <summary>
    /// 入场演出：入场音效+等待 → 显隐一组物体 → Slider 填满 → 音效 → 再显隐+音效 → 开始对话。
    /// 默认使用真实时间（不受 Time.timeScale 影响），便于与暂停中的 UI 配合。
    /// </summary>
    public class IntroPerformanceFlow : MonoBehaviour
    {
        [Tooltip("留空则在本物体上添加 AudioSource")]
        public AudioSource sfxSource;

        [Tooltip("全程使用真实时间（推荐）；关闭则用 scaled 时间")]
        public bool useUnscaledTime = true;

        [Header("1) 进入场景")]
        public AudioClip sceneEnterSfx;

        [Min(0f)]
        [Tooltip("播放入场音效后，再等待的秒数")]
        public float waitAfterEnterSfxSeconds = 1f;

        [Header("2) 等待结束后的显隐")]
        public List<GameObject> disableAfterEnterWait = new List<GameObject>();
        public List<GameObject> enableAfterEnterWait = new List<GameObject>();

        [Header("3) Slider 填充")]
        public Slider progressSlider;

        [Min(0.01f)]
        [Tooltip("从最小值填到最大值所用秒数")]
        public float sliderFillDurationSeconds = 3f;

        [Tooltip("映射填充进度 0~1；不填或空曲线则线性")]
        public AnimationCurve sliderFillCurve;

        [Header("4) Slider 填满后")]
        public AudioClip sliderFilledSfx;

        [Header("5) 紧接着：显隐 + 音效（同时进行）")]
        public List<GameObject> disableWhenSliderDone = new List<GameObject>();
        public List<GameObject> enableWhenSliderDone = new List<GameObject>();
        public AudioClip withToggleSfx;

        [Header("6) 对话（在上一段 With Toggle Sfx 整段播完后才开始）")]
        [Tooltip("可为空则跳过；未配置 With Toggle Sfx 时显隐结束后立刻开始对话")]
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
