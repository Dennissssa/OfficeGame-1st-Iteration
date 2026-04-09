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

    [Tooltip("Boss ?????????? Work ?? Slider ????????????????? Slider ?? Fill Area ?????????Image ??????")]
    public RectTransform workBossMinThresholdIndicator;

    [Tooltip("wrok min indicator width")]
    [Min(1f)]
    public float workBossMinIndicatorWidth = 6f;

    [Header("Work Progress 视觉（归一化 = 当前值 / Slider.maxValue）")]
    [Tooltip("不填则使用 Slider → Fill Area → Fill 上的 Image")]
    public Image workSliderFillOverride;

    [Tooltip("至少 2 个阶段可在相邻阈值之间渐变 Fill 颜色；仅 1 个则整条为纯色")]
    public List<WorkProgressFillColorStage> workProgressFillColorStages = new List<WorkProgressFillColorStage>();

    [Tooltip("同一 Image 可配置多条：进度升高切换、降低自动恢复")]
    public List<WorkProgressImageSpriteKeyframe> workProgressImageSprites = new List<WorkProgressImageSpriteKeyframe>();

    readonly Dictionary<Image, (float threshold, Sprite sprite)> _workImageSpritePickScratch = new Dictionary<Image, (float, Sprite)>();

    [Header("Game Over UI")]
    public GameObject gameOverRoot;
    public TMP_Text gameOverTitleText;
    public TMP_Text gameOverDetailText;

    [Header("Work Progress 失败面板（Virus Die）")]
    [Tooltip("与 Game Over、Game Win 同级（Sibling）；开局关闭，仅当 Work 进度涨满时打开，并在本面板上显示结算文案（不打开 Game Over）。")]
    public GameObject workProgressLoseRoot;
    public TMP_Text workProgressLoseTitleText;
    public TMP_Text workProgressLoseDetailText;

    [Header("Victory UI")]
    public GameObject gameWinRoot;
    public TMP_Text gameWinPerformanceText;

    [Header("Debug（工作压力失败面板）")]
    [Tooltip("勾选后：Show/Hide WorkProgressLose 时打印 active 链、CanvasGroup，Hide 时带调用栈；帧末再打印一次以排查本帧内被关掉。")]
    public bool debugLogWorkProgressLoseFlow;

    Coroutine _debugWplEndOfFrameRoutine;

    void Awake()
    {
        if (debugLogWorkProgressLoseFlow)
            Debug.Log($"[UIManager/WPL] Awake → HideAllResultPanels（instanceID={GetInstanceID()}）", this);
        HideAllResultPanels();
    }

    /// <summary>开局与切场景时：关闭 Game Over、工作压力失败、胜利三块根节点（与 Inspector 里默认是否勾选无关）。</summary>
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

    /// <summary>按当前 Work 与 Slider.max 更新 Fill 渐变颜色与各 Image 的 Sprite。</summary>
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
            timeText.text = $"??: {Mathf.Max(0f, t):0.0}s";
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
        DebugDumpWorkProgressLoseHierarchy("同一帧 WaitForEndOfFrame 之后（排查本帧内是否被 SetActive false）");
    }

    /// <summary>
    /// 从场景根向下面板方向，依次激活未激活的父节点；否则仅 SetActive(子) 时父物体仍关闭，界面不会出现。
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
    /// 父物体 active 但 Canvas 组件被关掉时，UI 同样不可见。
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
    /// 从本物体沿父链向上：凡带有 CanvasGroup 且 alpha≈0（开局隐藏）的节点一律拉满可见。
    /// 仅处理根节点时，父物体上的 CanvasGroup 仍会把整条分支挡掉。
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
    /// 工作压力条涨满时的唯一结算入口：只显示本面板（与 Game Over 同级），不在此流程中打开 Game Over。
    /// </summary>
    public void ShowWorkProgressLose(float surviveTime, float finalWorkProgress, float performanceScore, float maxWorkProgress)
    {
        // 无条件一行：便于与 GameManager 日志里的 instanceID 对照（多 UIManager 时常出现 Awake 与结算不是同一个组件）
        Debug.Log(
            $"[UIManager] ShowWorkProgressLose 入口 | 物体=\"{gameObject.name}\" instanceID={GetInstanceID()} " +
            $"workProgressLoseRoot={(workProgressLoseRoot != null ? "\"" + workProgressLoseRoot.name + "\"" : "NULL")}",
            this);

        if (workProgressLoseRoot == null)
        {
            Debug.LogError(
                "[UIManager] ShowWorkProgressLose：workProgressLoseRoot 未赋值，无法显示面板。" +
                " 请在本 UIManager 上拖入失败面板根物体，或把 GameManager.ui 改成已配好三项结算引用的那个 UIManager。",
                this);
            return;
        }

        if (debugLogWorkProgressLoseFlow)
        {
            Debug.Log(
                $"[UIManager/WPL] ShowWorkProgressLose 开始 | instanceID={GetInstanceID()} timeScale={Time.timeScale:F2}",
                this);
            DebugDumpWorkProgressLoseHierarchy("Show 开始前（调用 Ensure 之前）");
        }

        // 若 Virus Die 挂在 Game Over 下面，先 HideGameOver 会关掉父物体，子级永远无法显示
        bool loseUnderGameOver = workProgressLoseRoot != null && gameOverRoot != null
            && workProgressLoseRoot.transform.IsChildOf(gameOverRoot.transform);
        if (debugLogWorkProgressLoseFlow)
        {
            Debug.Log(
                $"[UIManager/WPL] loseUnderGameOver={loseUnderGameOver} " +
                $"(gameOverRoot={(gameOverRoot != null ? gameOverRoot.name : "null")}) → " +
                $"{(loseUnderGameOver ? "跳过 HideGameOver" : "将调用 HideGameOver")}",
                this);
        }

        if (!loseUnderGameOver)
            HideGameOver();
        HideGameWin();

        if (debugLogWorkProgressLoseFlow)
            DebugDumpWorkProgressLoseHierarchy("HideGameOver/HideGameWin 之后、Ensure 之前");

        EnsureObjectAndAncestorsActive(workProgressLoseRoot);
        EnsureCanvasEnabledInAncestors(workProgressLoseRoot);
        EnsureCanvasGroupVisibleForResultPanel(workProgressLoseRoot);
        if (workProgressLoseRoot.transform.parent != null)
            workProgressLoseRoot.transform.SetAsLastSibling();

        if (debugLogWorkProgressLoseFlow)
        {
            DebugDumpWorkProgressLoseHierarchy("Show 全部步骤完成之后");
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
                $"[UIManager/WPL] HideWorkProgressLose() → SetActive(false) on \"{(workProgressLoseRoot != null ? workProgressLoseRoot.name : "null")}\"（见下方调用栈）",
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

/// <summary>工作压力条 Fill 在某一归一化进度点的颜色；相邻两点之间线性渐变。</summary>
[System.Serializable]
public class WorkProgressFillColorStage
{
    [Tooltip("归一化进度 0~1（当前 Work / Slider.maxValue）。达到该点及之后与下一点之间对 Fill 颜色做渐变。")]
    [Range(0f, 1f)]
    public float normalizedThreshold;

    public Color color = Color.white;
}

/// <summary>当进度达到阈值时，将指定 Image 切换为对应 Sprite；进度回退时自动回到较低阈值对应的 Sprite。</summary>
[System.Serializable]
public class WorkProgressImageSpriteKeyframe
{
    public Image targetImage;

    [Tooltip("归一化进度 0~1。当前进度 ≥ 此值时采用本 Sprite（同图多条目取阈值最高且仍 ≤ 当前进度的那条；同阈值时列表靠后的优先）。")]
    [Range(0f, 1f)]
    public float normalizedThreshold;

    public Sprite sprite;
}
