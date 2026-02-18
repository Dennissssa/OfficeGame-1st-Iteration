using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("Work UI")]
    public Slider workSlider;
    public TMP_Text workNumberText; // 可选：显示具体数值
    public TMP_Text timeText;

    [Header("Boss Warning UI")]
    public GameObject bossCountdownRoot;
    public TMP_Text bossCountdownText;

    [Header("Game Over UI")]
    public GameObject gameOverRoot;
    public TMP_Text gameOverTitleText;
    public TMP_Text gameOverDetailText;

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

    public void SetTime(float t)
    {
        if (timeText != null)
            timeText.text = $"TIME: {t:0.0}s";
    }

    public void SetBossCountdown(float secondsLeft)
    {
        if (bossCountdownRoot != null) bossCountdownRoot.SetActive(true);
        if (bossCountdownText != null)
            bossCountdownText.text = $"BOSS IN: {Mathf.CeilToInt(secondsLeft)}";
    }

    public void HideBossCountdown()
    {
        if (bossCountdownRoot != null) bossCountdownRoot.SetActive(false);
    }

    public void ShowGameOver(float surviveTime, float finalWork, string reason)
    {
        if (gameOverRoot != null) gameOverRoot.SetActive(true);
        if (gameOverTitleText != null) gameOverTitleText.text = "GAME OVER";

        if (gameOverDetailText != null)
        {
            gameOverDetailText.text =
                $"Survived: {surviveTime:0.0}s\n" +
                $"Final Work: {finalWork:0}\n" +
                $"Reason: {reason}";
        }
    }

    public void HideGameOver()
    {
        if (gameOverRoot != null) gameOverRoot.SetActive(false);
    }
}
