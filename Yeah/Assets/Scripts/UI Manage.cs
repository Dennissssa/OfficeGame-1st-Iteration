using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("Work UI")]
    public Slider workSlider;
    public TMP_Text workNumberText;
    public TMP_Text timeText;

    [Tooltip("Optional marker on the work Slider for Boss minimum work threshold (same track as Fill).")]
    public RectTransform workBossMinThresholdIndicator;

    [Tooltip("wrok min indicator width")]
    [Min(1f)]
    public float workBossMinIndicatorWidth = 6f;

    [Header("Work Progress visuals (normalized = value / Slider.maxValue)")]
    [Tooltip("If unset, uses the Image on Slider > Fill Area > Fill")]
    public Image workSliderFillOverride;

    [Tooltip("With 2+ stages, Fill color lerps between adjacent thresholds; with 1 stage, solid color")]
    public List<WorkProgressFillColorStage> workProgressFillColorStages = new List<WorkProgressFillColorStage>();

    [Tooltip("Multiple entries per Image: sprite swaps as progress rises; reverts when progress falls")]
    public List<WorkProgressImageSpriteKeyframe> workProgressImageSprites = new List<WorkProgressImageSpriteKeyframe>();

    readonly Dictionary<Image, (float threshold, Sprite sprite)> _workImageSpritePickScratch = new Dictionary<Image, (float, Sprite)>();

    [Header("Game Over UI")]
    public GameObject gameOverRoot;
    public TMP_Text gameOverTitleText;
    public TMP_Text gameOverDetailText;

    [Header("Work Progress lose panel (Virus Die)")]
    [Tooltip("Sibling to Game Over / Game Win; starts hidden; opens when work bar fills; shows outcome text here (does not open Game Over).")]
    public GameObject workProgressLoseRoot;
    public TMP_Text workProgressLoseTitleText;
    public TMP_Text workProgressLoseDetailText;

    [Header("Victory UI")]
    public GameObject gameWinRoot;
    public TMP_Text gameWinPerformanceText;

    [Header("Debug (work pressure lose panel)")]
    [Tooltip("When enabled, logs active chain and CanvasGroups on Show/Hide WorkProgressLose; Hide includes stack trace; end-of-frame log to catch same-frame deactivation.")]
    public bool debugLogWorkProgressLoseFlow;

    Coroutine _debugWplEndOfFrameRoutine;

    void Awake()
    {
        if (debugLogWorkProgressLoseFlow)
            Debug.Log($"[UIManager/WPL] Awake -> HideAllResultPanels (instanceID={GetInstanceID()})", this);
        HideAllResultPanels();
    }

    /// <summary>On start / scene load: hide Game Over, work-pressure lose, and win roots (regardless of default active state in Inspector).</summary>
    public void HideAllResultPanels()
    {
        if (debugLogWorkProgressLoseFlow)
            Debug.Log("[UIManager/WPL] HideAllResultPanels()", this);
        HideGameOver();
        HideWorkProgressLose();
        HideGameWin();
    }

    public void InitWorkSlider(float maxWork)
    {
        if (workSlider == null) return;
        workSlider.minValue = 0f;
        workSlider.maxValue = maxWork;
        workSlider.value = 0f;
        workSlider.interactable = false;
        RefreshWorkProgressVisuals(workSlider.value);
    }

    public void SetWork(float work)
    {
        if (workSlider != null)
            workSlider.value = work;

        if (workNumberText != null)
            workNumberText.text = $"WORK: {work:0}";

        RefreshWorkProgressVisuals(work);
    }

    /// <summary>Updates Fill gradient colors and per-Image sprites from current work and Slider.max.</summary>
    public void RefreshWorkProgressVisuals(float currentWork)
    {
        float max = workSlider != null ? workSlider.maxValue : 1f;
        if (max <= 0.0001f)
            max = 1f;
        float u = Mathf.Clamp01(currentWork / max);
        ApplyWorkProgressFillGradient(u);
        ApplyWorkProgressImageSprites(u);
    }

    void ApplyWorkProgressFillGradient(float normalizedWork)
    {
        Image fill = workSliderFillOverride;
        if (fill == null && workSlider != null && workSlider.fillRect != null)
            fill = workSlider.fillRect.GetComponent<Image>();
        if (fill == null || workProgressFillColorStages == null || workProgressFillColorStages.Count == 0)
            return;

        var sorted = new List<WorkProgressFillColorStage>(workProgressFillColorStages);
        sorted.Sort((a, b) => a.normalizedThreshold.CompareTo(b.normalizedThreshold));

        if (sorted.Count == 1)
        {
            fill.color = sorted[0].color;
            return;
        }

        float u = normalizedWork;
        if (u <= sorted[0].normalizedThreshold)
        {
            fill.color = sorted[0].color;
            return;
        }

        float lastT = sorted[sorted.Count - 1].normalizedThreshold;
        if (u >= lastT)
        {
            fill.color = sorted[sorted.Count - 1].color;
            return;
        }

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            float t0 = sorted[i].normalizedThreshold;
            float t1 = sorted[i + 1].normalizedThreshold;
            if (u < t0)
                continue;
            if (u > t1)
                continue;

            float span = t1 - t0;
            float lerp = span > 1e-6f ? (u - t0) / span : 0f;
            fill.color = Color.Lerp(sorted[i].color, sorted[i + 1].color, lerp);
            return;
        }
    }

    void ApplyWorkProgressImageSprites(float normalizedWork)
    {
        if (workProgressImageSprites == null || workProgressImageSprites.Count == 0)
            return;

        _workImageSpritePickScratch.Clear();
        const float eps = 1e-5f;

        for (int i = 0; i < workProgressImageSprites.Count; i++)
        {
            WorkProgressImageSpriteKeyframe kf = workProgressImageSprites[i];
            if (kf.targetImage == null || kf.sprite == null)
                continue;
            if (kf.normalizedThreshold > normalizedWork + eps)
                continue;

            if (!_workImageSpritePickScratch.TryGetValue(kf.targetImage, out var pick)
                || kf.normalizedThreshold >= pick.threshold)
            {
                _workImageSpritePickScratch[kf.targetImage] = (kf.normalizedThreshold, kf.sprite);
            }
        }

        foreach (var kv in _workImageSpritePickScratch)
            kv.Key.sprite = kv.Value.sprite;
    }


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
            timeText.text = $"TIME: {Mathf.Max(0f, t):0.0}s";
    }

    public void ShowGameOver(float surviveTime, float finalWork, string reason, float performanceScore)
    {
        HideWorkProgressLose();
        HideGameWin();
        EnsureObjectAndAncestorsActive(gameOverRoot);
        EnsureCanvasEnabledInAncestors(gameOverRoot);
        EnsureCanvasGroupVisibleForResultPanel(gameOverRoot);
        if (gameOverRoot != null)
        {
            gameOverRoot.SetActive(true);
            if (gameOverRoot.transform.parent != null)
                gameOverRoot.transform.SetAsLastSibling();
        }
        if (gameOverTitleText != null) gameOverTitleText.text = "GAME OVER";

        if (gameOverDetailText != null)
        {
            gameOverDetailText.text = $"Performance Score: {performanceScore:0}\n";
        }
    }

    public void HideGameOver()
    {
        if (gameOverRoot != null) gameOverRoot.SetActive(false);
    }

    static string HierarchyPath(Transform t)
    {
        if (t == null) return "";
        if (t.parent == null) return t.name;
        return HierarchyPath(t.parent) + "/" + t.name;
    }

    void DebugDumpWorkProgressLoseHierarchy(string step)
    {
        if (!debugLogWorkProgressLoseFlow || workProgressLoseRoot == null)
            return;

        var sb = new StringBuilder();
        sb.AppendLine($"[UIManager/WPL] {step} | loseRoot=\"{workProgressLoseRoot.name}\" path={HierarchyPath(workProgressLoseRoot.transform)}");
        Transform tr = workProgressLoseRoot.transform;
        int depth = 0;
        while (tr != null)
        {
            GameObject go = tr.gameObject;
            Canvas c = go.GetComponent<Canvas>();
            CanvasGroup cg = go.GetComponent<CanvasGroup>();
            sb.Append("  ").Append(' ', depth * 2)
                .Append(go.name)
                .Append(" | activeSelf=").Append(go.activeSelf)
                .Append(" activeInHierarchy=").Append(go.activeInHierarchy);
            if (c != null) sb.Append(" Canvas.enabled=").Append(c.enabled);
            if (cg != null) sb.Append(" CG.alpha=").Append(cg.alpha.ToString("F3"));
            sb.AppendLine();
            tr = tr.parent;
            depth++;
        }

        if (workProgressLoseTitleText != null)
        {
            GameObject g = workProgressLoseTitleText.gameObject;
            sb.Append("  [TMP Title] ").Append(g.name)
                .Append(" activeSelf=").Append(g.activeSelf)
                .Append(" activeInHierarchy=").Append(g.activeInHierarchy)
                .Append(" path=").AppendLine(HierarchyPath(g.transform));
        }
        else sb.AppendLine("  [TMP Title] null");

        if (workProgressLoseDetailText != null)
        {
            GameObject g = workProgressLoseDetailText.gameObject;
            sb.Append("  [TMP Detail] ").Append(g.name)
                .Append(" activeSelf=").Append(g.activeSelf)
                .Append(" activeInHierarchy=").Append(g.activeInHierarchy)
                .Append(" path=").AppendLine(HierarchyPath(g.transform));
        }
        else sb.AppendLine("  [TMP Detail] null");

        Debug.Log(sb.ToString(), this);
    }

    IEnumerator DebugWorkProgressLoseEndOfFrameCheck()
    {
        yield return new WaitForEndOfFrame();
        _debugWplEndOfFrameRoutine = null;
        if (workProgressLoseRoot == null)
            yield break;
        DebugDumpWorkProgressLoseHierarchy("After WaitForEndOfFrame same frame (check if SetActive false this frame)");
    }

    /// <summary>
    /// Activates inactive parents from root toward the panel; otherwise activating only the child leaves parents off and UI stays hidden.
    /// </summary>
    static void EnsureObjectAndAncestorsActive(GameObject leaf)
    {
        if (leaf == null) return;

        Transform t = leaf.transform.parent;
        var inactiveParents = new List<GameObject>();
        while (t != null)
        {
            if (!t.gameObject.activeSelf)
                inactiveParents.Add(t.gameObject);
            t = t.parent;
        }

        for (int i = inactiveParents.Count - 1; i >= 0; i--)
            inactiveParents[i].SetActive(true);

        leaf.SetActive(true);
    }

    /// <summary>
    /// If a parent is active but its Canvas is disabled, UI under it is still invisible.
    /// </summary>
    static void EnsureCanvasEnabledInAncestors(GameObject leaf)
    {
        if (leaf == null) return;
        Transform t = leaf.transform;
        while (t != null)
        {
            Canvas c = t.GetComponent<Canvas>();
            if (c != null && !c.enabled)
                c.enabled = true;
            t = t.parent;
        }
    }

    /// <summary>
    /// Walk parents: any CanvasGroup with alpha ~0 (startup-hidden) is forced visible.
    /// Fixing only the leaf leaves parent CanvasGroups blocking the whole branch.
    /// </summary>
    static void EnsureCanvasGroupVisibleForResultPanel(GameObject leaf)
    {
        if (leaf == null) return;
        Transform t = leaf.transform;
        while (t != null)
        {
            var cg = t.GetComponent<CanvasGroup>();
            if (cg != null && cg.alpha < 0.01f)
            {
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
            t = t.parent;
        }
    }

    /// <summary>
    /// Sole resolution when work bar fills: show this panel only (sibling to Game Over); does not open Game Over.
    /// </summary>
    public void ShowWorkProgressLose(float surviveTime, float finalWorkProgress, float performanceScore, float maxWorkProgress)
    {
        // Always log one line: match instanceID with GameManager logs (multiple UIManagers may differ between Awake and settle)
        Debug.Log(
            $"[UIManager] ShowWorkProgressLose entry | object=\"{gameObject.name}\" instanceID={GetInstanceID()} " +
            $"workProgressLoseRoot={(workProgressLoseRoot != null ? "\"" + workProgressLoseRoot.name + "\"" : "NULL")}",
            this);

        if (workProgressLoseRoot == null)
        {
            Debug.LogError(
                "[UIManager] ShowWorkProgressLose: workProgressLoseRoot is not assigned; cannot show panel. " +
                "Assign the lose panel root on this UIManager, or point GameManager.ui at the UIManager that has all three outcome references.",
                this);
            return;
        }

        if (debugLogWorkProgressLoseFlow)
        {
            Debug.Log(
                $"[UIManager/WPL] ShowWorkProgressLose start | instanceID={GetInstanceID()} timeScale={Time.timeScale:F2}",
                this);
            DebugDumpWorkProgressLoseHierarchy("Before Show (before Ensure calls)");
        }

        // If Virus Die is under Game Over, HideGameOver first would disable the parent and the child never shows
        bool loseUnderGameOver = workProgressLoseRoot != null && gameOverRoot != null
            && workProgressLoseRoot.transform.IsChildOf(gameOverRoot.transform);
        if (debugLogWorkProgressLoseFlow)
        {
            Debug.Log(
                $"[UIManager/WPL] loseUnderGameOver={loseUnderGameOver} " +
                $"(gameOverRoot={(gameOverRoot != null ? gameOverRoot.name : "null")}) → " +
                $"{(loseUnderGameOver ? "skip HideGameOver" : "will call HideGameOver")}",
                this);
        }

        if (!loseUnderGameOver)
            HideGameOver();
        HideGameWin();

        if (debugLogWorkProgressLoseFlow)
            DebugDumpWorkProgressLoseHierarchy("After HideGameOver/HideGameWin, before Ensure");

        EnsureObjectAndAncestorsActive(workProgressLoseRoot);
        EnsureCanvasEnabledInAncestors(workProgressLoseRoot);
        EnsureCanvasGroupVisibleForResultPanel(workProgressLoseRoot);
        if (workProgressLoseRoot.transform.parent != null)
            workProgressLoseRoot.transform.SetAsLastSibling();

        if (debugLogWorkProgressLoseFlow)
        {
            DebugDumpWorkProgressLoseHierarchy("After Show steps complete");
            if (_debugWplEndOfFrameRoutine != null)
            {
                StopCoroutine(_debugWplEndOfFrameRoutine);
                _debugWplEndOfFrameRoutine = null;
            }
            _debugWplEndOfFrameRoutine = StartCoroutine(DebugWorkProgressLoseEndOfFrameCheck());
        }

        if (workProgressLoseTitleText != null)
            workProgressLoseTitleText.text = "WORK OVERLOAD";

        if (workProgressLoseDetailText != null)
        {
            workProgressLoseDetailText.text = $"Performance Score: {performanceScore:0}\n";
        }
    }

    public void HideWorkProgressLose()
    {
        if (debugLogWorkProgressLoseFlow)
        {
            Debug.LogWarning(
                $"[UIManager/WPL] HideWorkProgressLose() -> SetActive(false) on \"{(workProgressLoseRoot != null ? workProgressLoseRoot.name : "null")}\" (see stack below)",
                this);
        }
        if (workProgressLoseRoot != null) workProgressLoseRoot.SetActive(false);
    }

    public void ShowGameWin(float performanceScore)
    {
        HideGameOver();
        HideWorkProgressLose();
        if (gameWinRoot != null) gameWinRoot.SetActive(true);
        if (gameWinPerformanceText != null)
            gameWinPerformanceText.text = $"Performance Score: {performanceScore:0}";
    }

    public void HideGameWin()
    {
        if (gameWinRoot != null) gameWinRoot.SetActive(false);
    }
}

/// <summary>Fill color at one normalized work point; linear blend between adjacent points.</summary>
[System.Serializable]
public class WorkProgressFillColorStage
{
    [Tooltip("Normalized progress 0~1 (work / Slider.maxValue). Between this threshold and the next, Fill color lerps.")]
    [Range(0f, 1f)]
    public float normalizedThreshold;

    public Color color = Color.white;
}

/// <summary>When progress crosses a threshold, swap the Image sprite; falling progress restores the lower-threshold sprite.</summary>
[System.Serializable]
public class WorkProgressImageSpriteKeyframe
{
    public Image targetImage;

    [Tooltip("Normalized 0~1. At progress >= threshold use this sprite (per Image: highest threshold still <= progress wins; ties favor later list entries).")]
    [Range(0f, 1f)]
    public float normalizedThreshold;

    public Sprite sprite;
}
