using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 文件分类小游戏主控制器。
///
/// 场景层级建议：
///   Mini Game Panel (FileSortingGame)
///   ├── Spawn Area        (spawnArea 引用)
///   ├── Left Drop Zone    (DropZone, acceptedType = TypeA)
///   ├── Right Drop Zone   (DropZone, acceptedType = TypeB)
///   └── Wrong Drop Blocker (Image + CanvasGroup, blocksRaycasts=true; 默认 SetActive false)
///
/// File Prefab 要求：Image + SortableFile（CanvasGroup 运行时自动添加）。
/// </summary>
[AddComponentMenu("MiniGame/File Sorting Game")]
public class FileSortingGame : MonoBehaviour
{
    // ─── 场景引用 ──────────────────────────────────────────────

    [Header("Scene References")]
    [Tooltip("文件卡片刷新的矩形区域 RectTransform")]
    public RectTransform spawnArea;

    [Tooltip("左侧投放区域（DropZone 组件）")]
    public DropZone leftDropZone;

    [Tooltip("右侧投放区域（DropZone 组件）")]
    public DropZone rightDropZone;

    [Tooltip("错误投放时显示的遮挡面板（需含 CanvasGroup 且 blocksRaycasts = true）")]
    public GameObject wrongDropBlockerPanel;

    // ─── Prefab ────────────────────────────────────────────────

    [Header("File Prefab")]
    [Tooltip("需含 Image + SortableFile 组件；CanvasGroup 若未预设，运行时自动添加")]
    public GameObject filePrefab;

    [Tooltip("每张卡片的 UI 尺寸（单位：像素）")]
    public Vector2 fileCardSize = new Vector2(60f, 80f);

    // ─── Type A（红色文件）──────────────────────────────────────

    [Header("Type A  (e.g. Red)")]
    [Tooltip("Type A 可随机选取的 Sprite 列表；留空则使用纯色 typeAColor")]
    public List<Sprite> typeASprites = new List<Sprite>();
    public Color typeAColor = new Color(0.95f, 0.25f, 0.25f, 1f);

    // ─── Type B（蓝色文件）──────────────────────────────────────

    [Header("Type B  (e.g. Blue)")]
    [Tooltip("Type B 可随机选取的 Sprite 列表；留空则使用纯色 typeBColor")]
    public List<Sprite> typeBSprites = new List<Sprite>();
    public Color typeBColor = new Color(0.25f, 0.5f, 1f, 1f);

    // ─── 刷新配置 ──────────────────────────────────────────────

    [Header("Spawning")]
    [Tooltip("场内文件数量上限；未达到上限时每隔 spawnIntervalSeconds 刷新一张")]
    [Min(1)]
    public int maxFileCount = 6;

    [Tooltip("刷新间隔（秒）")]
    [Min(0.1f)]
    public float spawnIntervalSeconds = 2.5f;

    // ─── Work 减少 ─────────────────────────────────────────────

    [Header("Work Reduction")]
    [Tooltip("每次正确分类减少的 work 压力（GameManager 无 phase 配置时使用此值）")]
    [Min(0f)]
    public float workReductionPerCorrectSort = 5f;

    // ─── 错误投放遮挡 ──────────────────────────────────────────

    [Header("Wrong Drop")]
    [Tooltip("错误投放后遮挡面板显示的时长（秒）")]
    [Min(0.1f)]
    public float wrongDropBlockDuration = 1.5f;

    // ─── Debug ─────────────────────────────────────────────────

    [Header("Debug")]
    [Tooltip("开启后在 Console 打印详细日志（关闭后只保留 Error）")]
    [SerializeField] bool debugLog = false;

    // ─── 运行时状态 ────────────────────────────────────────────

    /// <summary>当前是否处于错误投放遮挡状态（SortableFile.OnBeginDrag 检测此值）。</summary>
    public bool IsBlocked => _isBlocked;

    readonly List<SortableFile> _activeFiles = new List<SortableFile>();
    bool _isBlocked;
    Coroutine _spawnCoroutine;
    Coroutine _blockCoroutine;

    // ─── 日志辅助 ──────────────────────────────────────────────

    void Log(string msg)        { if (debugLog) Debug.Log(msg, this); }
    void LogWarn(string msg)    { if (debugLog) Debug.LogWarning(msg, this); }
    void LogError(string msg)   { Debug.LogError(msg, this); }   // Error 始终显示

    // ─── 生命周期 ──────────────────────────────────────────────

    IEnumerator Start()
    {
        Log($"[FileSortingGame] Start() | GameObject={gameObject.name} activeInHierarchy={gameObject.activeInHierarchy}");

        // 引用完整性检查（Error 始终显示，不受 debugLog 控制）
        if (spawnArea == null)  LogError("[FileSortingGame] ❌ spawnArea 未赋值！");
        if (filePrefab == null) LogError("[FileSortingGame] ❌ filePrefab 未赋值！");
        if (leftDropZone == null)  LogWarn("[FileSortingGame] ⚠ leftDropZone 未赋值。");
        if (rightDropZone == null) LogWarn("[FileSortingGame] ⚠ rightDropZone 未赋值。");

        if (spawnArea == null || filePrefab == null)
        {
            LogError("[FileSortingGame] 关键引用缺失，终止初始化。");
            yield break;
        }

        if (leftDropZone != null)  leftDropZone.controller  = this;
        if (rightDropZone != null) rightDropZone.controller = this;

        if (wrongDropBlockerPanel != null)
            wrongDropBlockerPanel.SetActive(false);

        // 等一帧：Canvas 布局在 Start 期间未必完成
        yield return null;

        Log($"[FileSortingGame] spawnArea.rect={spawnArea.rect} filePrefab={filePrefab.name}");

        if (filePrefab.GetComponentInChildren<SortableFile>() == null)
        {
            LogError("[FileSortingGame] ❌ filePrefab 层级中找不到 SortableFile 组件！");
            yield break;
        }

        int initialCount = Mathf.Min(maxFileCount, 3);
        Log($"[FileSortingGame] 初始刷新 {initialCount} 张");
        for (int i = 0; i < initialCount; i++)
            SpawnFile();

        _spawnCoroutine = StartCoroutine(SpawnLoop());
        Log("[FileSortingGame] 初始化完成，SpawnLoop 已启动。");
    }

    void OnDestroy()
    {
        if (_spawnCoroutine != null) StopCoroutine(_spawnCoroutine);
        if (_blockCoroutine != null) StopCoroutine(_blockCoroutine);
    }

    // ─── 刷新循环 ──────────────────────────────────────────────

    IEnumerator SpawnLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(spawnIntervalSeconds);

            if (GameManager.Instance != null &&
                (GameManager.Instance.IsGameOver || GameManager.Instance.IsVictory))
                yield break;

            _activeFiles.RemoveAll(f => f == null);
            if (_activeFiles.Count < maxFileCount)
                SpawnFile();
        }
    }

    void SpawnFile()
    {
        if (filePrefab == null || spawnArea == null) return;

        GameObject go = Instantiate(filePrefab, spawnArea);

        RectTransform rt = go.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.sizeDelta = fileCardSize;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);

            float halfW = Mathf.Max(0f, spawnArea.rect.width  * 0.5f - fileCardSize.x * 0.5f);
            float halfH = Mathf.Max(0f, spawnArea.rect.height * 0.5f - fileCardSize.y * 0.5f);
            rt.anchoredPosition = new Vector2(
                Random.Range(-halfW, halfW),
                Random.Range(-halfH, halfH));

            Log($"[FileSortingGame] Spawn at {rt.anchoredPosition} | spawnArea={spawnArea.rect}");
        }
        else
        {
            LogWarn("[FileSortingGame] ⚠ filePrefab 根节点上没有 RectTransform！");
        }

        SortableFile file = go.GetComponentInChildren<SortableFile>();
        if (file == null)
        {
            LogError("[FileSortingGame] ❌ 实例化后层级中找不到 SortableFile，销毁。");
            Destroy(go);
            return;
        }

        file.spawnedRoot = go;

        SortableFile.FileType type = Random.value < 0.5f
            ? SortableFile.FileType.TypeA
            : SortableFile.FileType.TypeB;

        List<Sprite> sprites = type == SortableFile.FileType.TypeA ? typeASprites : typeBSprites;
        Color        color   = type == SortableFile.FileType.TypeA ? typeAColor   : typeBColor;

        file.Setup(type, sprites, color, this);
        _activeFiles.Add(file);
        Log($"[FileSortingGame] 生成成功 type={type} total={_activeFiles.Count}");
    }

    // ─── DropZone 回调 ─────────────────────────────────────────

    /// <summary>
    /// 玩家正确分类后由 SortableFile.OnEndDrag 调用。
    /// 销毁两个对象：spawnedRoot（留在 spawnArea 的空壳）和 file.gameObject（视觉节点，
    /// 拖拽时已被 reparent 到根 Canvas，不再是 spawnedRoot 的子节点）。
    /// </summary>
    public void OnCorrectDrop(SortableFile file)
    {
        _activeFiles.Remove(file);

        // spawnedRoot 是 prefab 根（可能已是空壳，因为 FileImage 在拖拽中被提升到根 Canvas）
        if (file.spawnedRoot != null && file.spawnedRoot != file.gameObject)
            Destroy(file.spawnedRoot);

        // 始终销毁视觉节点本身（即 SortableFile 所在的 GameObject）
        Destroy(file.gameObject);

        float reduction = workReductionPerCorrectSort;
        if (GameManager.Instance != null)
            reduction = GameManager.Instance.GetActiveSortWorkReduction(workReductionPerCorrectSort);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.work = Mathf.Max(0f, GameManager.Instance.work - reduction);
            if (GameManager.Instance.ui != null)
                GameManager.Instance.ui.SetWork(GameManager.Instance.work);
        }

        Log($"[FileSortingGame] 正确分类 work-={reduction}");
    }

    /// <summary>玩家错误分类后由 SortableFile.OnEndDrag 调用；文件自动回原位。</summary>
    public void OnWrongDrop(SortableFile file)
    {
        Log("[FileSortingGame] 错误分类，触发遮挡。");
        if (_blockCoroutine != null)
            StopCoroutine(_blockCoroutine);
        _blockCoroutine = StartCoroutine(WrongDropBlockRoutine());
    }

    IEnumerator WrongDropBlockRoutine()
    {
        _isBlocked = true;
        if (wrongDropBlockerPanel != null)
            wrongDropBlockerPanel.SetActive(true);

        yield return new WaitForSeconds(wrongDropBlockDuration);

        _isBlocked = false;
        if (wrongDropBlockerPanel != null)
            wrongDropBlockerPanel.SetActive(false);

        _blockCoroutine = null;
    }
}
