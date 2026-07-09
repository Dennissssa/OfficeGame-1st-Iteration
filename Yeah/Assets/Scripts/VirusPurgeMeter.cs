using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 病毒清除进度演出条（纯演出，与实际游戏胜负无关）。
///
/// 行为：
///   - 游戏进行中按 increaseRatePerSecond 缓慢自动增长（0~100）
///   - 随机触发突然扣减（保证不低于 0）
///   - Fill 颜色从 colorAtZero（红）平滑过渡到 colorAtFull（绿）
///   - 触发胜利结局 → 立即跳至 100% 并停止所有行为
///   - 触发 Boss 失败结局 → 立即停止（保持当前值）
///
/// 使用方式：
///   将此脚本挂在场景中任意 GameObject 上，分配好 virusPurgeSlider 即可。
///   在 GameManager Inspector 的 "Virus Purge Meter" 栏中将本组件拖入引用。
/// </summary>
[AddComponentMenu("UI/Virus Purge Meter")]
public class VirusPurgeMeter : MonoBehaviour
{
    // ── 引用 ──────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("展示病毒清除进度的 Slider；运行时 maxValue 固定为 100")]
    public Slider virusPurgeSlider;

    [Tooltip("Slider 的填充 Image（用于变色）；留空则自动从 virusPurgeSlider.fillRect 查找")]
    public Image fillImageOverride;

    // ── 颜色 ──────────────────────────────────────────────────────────────

    [Header("Color Gradient")]
    [Tooltip("进度为 0% 时填充条的颜色（建议使用红色）")]
    public Color colorAtZero = Color.red;

    [Tooltip("进度为 100% 时填充条的颜色（建议使用绿色）")]
    public Color colorAtFull = Color.green;

    // ── 自动增长 ──────────────────────────────────────────────────────────

    [Header("Auto Increase")]
    [Tooltip("每秒自动增加的进度值（基于 0~100 范围）")]
    [Min(0f)]
    public float increaseRatePerSecond = 2f;

    // ── 随机扣减 ──────────────────────────────────────────────────────────

    [Header("Random Decrease")]
    [Tooltip("两次随机扣减事件之间最短等待（秒）")]
    [Min(0.1f)]
    public float randomDecreaseCooldownMin = 8f;

    [Tooltip("两次随机扣减事件之间最长等待（秒）；建议 ≥ Min")]
    [Min(0.1f)]
    public float randomDecreaseCooldownMax = 20f;

    [Tooltip("每次随机扣减的最小量（不会扣到负数）")]
    [Min(0f)]
    public float randomDecreaseAmountMin = 5f;

    [Tooltip("每次随机扣减的最大量；建议 ≥ Min")]
    [Min(0f)]
    public float randomDecreaseAmountMax = 20f;

    // ── 运行时私有 ────────────────────────────────────────────────────────

    const float k_Min = 0f;
    const float k_Max = 100f;

    float _value;
    bool _running;
    Image _fillImage;
    Coroutine _randomDecreaseCoroutine;

    // ── 生命周期 ──────────────────────────────────────────────────────────

    void Start()
    {
        // 查找 Fill Image
        _fillImage = fillImageOverride;
        if (_fillImage == null && virusPurgeSlider != null && virusPurgeSlider.fillRect != null)
            _fillImage = virusPurgeSlider.fillRect.GetComponent<Image>();

        // 初始化 Slider
        if (virusPurgeSlider != null)
        {
            virusPurgeSlider.minValue     = k_Min;
            virusPurgeSlider.maxValue     = k_Max;
            virusPurgeSlider.value        = k_Min;
            virusPurgeSlider.interactable = false;
        }

        _value   = k_Min;
        _running = true;

        ApplyVisuals();

        _randomDecreaseCoroutine = StartCoroutine(RandomDecreaseLoop());
    }

    void Update()
    {
        if (!_running) return;

        _value = Mathf.Min(_value + increaseRatePerSecond * Time.deltaTime, k_Max);
        ApplyVisuals();
    }

    // ── 公共接口 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 触发胜利结局时调用：立即将进度跳至 100% 并停止所有动态行为。
    /// 由 GameManager.TriggerVictory() 调用。
    /// </summary>
    public void OnVictory()
    {
        StopAllBehavior();
        _value = k_Max;
        ApplyVisuals();
    }

    /// <summary>
    /// 触发 Boss 失败结局时调用：立即停止所有动态行为，保持当前进度值。
    /// 由 GameManager.GameOver() 及 GameManager.GameOverWorkProgressFull() 调用。
    /// </summary>
    public void OnBossFail()
    {
        StopAllBehavior();
    }

    // ── 内部方法 ──────────────────────────────────────────────────────────

    void StopAllBehavior()
    {
        _running = false;
        if (_randomDecreaseCoroutine != null)
        {
            StopCoroutine(_randomDecreaseCoroutine);
            _randomDecreaseCoroutine = null;
        }
    }

    void ApplyVisuals()
    {
        if (virusPurgeSlider != null)
            virusPurgeSlider.value = _value;

        if (_fillImage != null)
        {
            float t = Mathf.Clamp01((_value - k_Min) / (k_Max - k_Min));
            _fillImage.color = Color.Lerp(colorAtZero, colorAtFull, t);
        }
    }

    IEnumerator RandomDecreaseLoop()
    {
        while (_running)
        {
            float minCD = Mathf.Max(0.1f, randomDecreaseCooldownMin);
            float maxCD = Mathf.Max(minCD, randomDecreaseCooldownMax);
            float wait = Random.Range(minCD, maxCD);
            yield return new WaitForSeconds(wait);

            if (!_running) yield break;

            float minAmt = Mathf.Max(0f, randomDecreaseAmountMin);
            float maxAmt = Mathf.Max(minAmt, randomDecreaseAmountMax);
            float amount = Random.Range(minAmt, maxAmt);

            _value = Mathf.Max(k_Min, _value - amount);
            ApplyVisuals();
        }
    }
}
