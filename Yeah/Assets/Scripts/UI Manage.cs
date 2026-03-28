using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("Work UI")]
    public Slider workSlider;
    public TMP_Text workNumberText;
    public TMP_Text timeText;

    [Tooltip("Boss ?????????? Work ?? Slider ????????????????? Slider ?? Fill Area ?????????Image ??????")]
    public RectTransform workBossMinThresholdIndicator;

    [Tooltip("??????????????")]
    [Min(1f)]
    public float workBossMinIndicatorWidth = 6f;

    [Header("Game Over UI")]
    public GameObject gameOverRoot;
    public TMP_Text gameOverTitleText;
    public TMP_Text gameOverDetailText;

    [Header("Victory UI")]
    public GameObject gameWinRoot;
    public TMP_Text gameWinPerformanceText;

    public void InitWorkSlider(float maxWork)
    {
        if (workSlider == null) return;
        workSlider.minValue = 0f;
        workSlider.maxValue = maxWork;
        workSlider.value = 0f;
        workSlider.interactable = false;
    }

    public void SetWork(float work)
    {
        if (workSlider != null)
            workSlider.value = work;

        if (workNumberText != null)
            workNumberText.text = $"WORK: {work:0}";
    }

    /// <summary>
    /// ???????? Boss ??? Work ????? Slider ?????????????0~maxWork ???? Fill Area ??????????????
    /// ???????????? Slider ?? Fill Area???? Fill ???????¨º???????????????
    /// </summary>
    public void SetWorkBossMinThresholdIndicator(float bossMinWorkThreshold, float maxWork)
    {
        if (workBossMinThresholdIndicator == null) return;

        if (workSlider == null || maxWork <= 0.0001f)
        {
            workBossMinThresholdIndicator.gameObject.SetActive(false);
            return;
        }

        workBossMinThresholdIndicator.gameObject.SetActive(true);
        float u = Mathf.Clamp01(bossMinWorkThreshold / maxWork);
        workBossMinThresholdIndicator.anchorMin = new Vector2(u, 0f);
        workBossMinThresholdIndicator.anchorMax = new Vector2(u, 1f);
        workBossMinThresholdIndicator.pivot = new Vector2(0.5f, 0.5f);
        workBossMinThresholdIndicator.sizeDelta = new Vector2(workBossMinIndicatorWidth, 0f);
        workBossMinThresholdIndicator.anchoredPosition = Vector2.zero;
    }

    public void SetTime(float t)
    {
        if (timeText != null)
            timeText.text = $"??: {Mathf.Max(0f, t):0.0}s";
    }

    public void ShowGameOver(float surviveTime, float finalWork, string reason, float performanceScore)
    {
        if (gameOverRoot != null) gameOverRoot.SetActive(true);
        if (gameOverTitleText != null) gameOverTitleText.text = "GAME OVER";

        if (gameOverDetailText != null)
        {
            gameOverDetailText.text =
                $"Survived: {surviveTime:0.0}s\n" +
                $"Final Work: {finalWork:0}\n" +
                $"Performance Score: {performanceScore:0}\n" +
                $"Reason: {reason}";
        }
    }

    public void HideGameOver()
    {
        if (gameOverRoot != null) gameOverRoot.SetActive(false);
    }

    public void ShowGameWin(float performanceScore)
    {
        if (gameWinRoot != null) gameWinRoot.SetActive(true);
        if (gameWinPerformanceText != null)
            gameWinPerformanceText.text = $"Performance Score: {performanceScore:0}";
    }

    public void HideGameWin()
    {
        if (gameWinRoot != null) gameWinRoot.SetActive(false);
    }
}
