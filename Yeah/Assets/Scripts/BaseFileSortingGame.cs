using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 文件分类小游戏抽象基类。
///
/// 处理所有与 GameManager 无关的公共逻辑：
///   · 文件生成（Spawn）与回收
///   · 拖拽边界引用
///   · 投放区（DropZone）绑定
///   · 错误投放输入遮挡（IsBlocked）
///
/// 子类只需实现：
///   · OnAfterBaseInit()  — 初始化完成后的逻辑（初始刷新、开启 SpawnLoop 等）
///   · OnCorrectDrop()    — 正确投放处理
///   · OnWrongDrop()      — 错误投放处理
///
/// 已有两个具体子类：
///   · FileSortingGame        — 原游戏流程版本（GameManager + BossWindowPerformance）
///   · StoryFileSortingGame   — IntroScene 演出版本（完全独立，无 GameManager 依赖）
/// </summary>
[AddComponentMenu("MiniGame/Base File Sorting Game")]
public abstract class BaseFileSortingGame : MonoBehaviour
{
    // ─── 场景引用 ──────────────────────────────────────────────

    [Header("Scene References")]
    [Tooltip("文件卡片刷新的矩形区域 RectTransform")]
    public RectTransform spawnArea;

    [Tooltip("左侧投放区域（DropZone 组件）")]
    public DropZone leftDropZone;

    [Tooltip("右侧投放区域（DropZone 组件）")]
    public DropZone rightDropZone;

    [Tooltip("错误投放时显示的遮挡面板（可选；blocksRaycasts = true）；多数子类已替换为独立演出对象")]
    public GameObject wrongDropBlockerPanel;

    [Tooltip("文件卡片的拖拽范围限制（通常比 spawnArea 更大）；留空则以 spawnArea 作为拖拽边界。")]
    public RectTransform dragBoundaryRect;

    // ─── Prefab ────────────────────────────────────────────────

    [Header("File Prefab")]
    [Tooltip("需含 Image + SortableFile 组件；CanvasGroup 若未预设，运行时自动添加")]
    public GameObject filePrefab;

    [Tooltip("每张卡片的 UI 尺寸（单位：像素）")]
    public Vector2 fileCardSize = new Vector2(60f, 80f);

    // ─── Type A ─────────────────────────────────────────────────

    [Header("Type A  (e.g. Red)")]
    [Tooltip("Type A 可随机选取的 Sprite 列表；留空则使用纯色 typeAColor")]
    public List<Sprite> typeASprites = new List<Sprite>();
    public Color typeAColor = new Color(0.95f, 0.25f, 0.25f, 1f);
    [Tooltip("Type A 的刷新权重；与 typeBWeight 共同决定两种文件的出现概率")]
    [Min(0f)]
    public float typeAWeight = 1f;
    [Tooltip("错误投放 Type A 文件时，替换 Wrong Block 面板上的 Sprite；留空则不替换")]
    public Sprite typeAWrongBlockSprite;

    // ─── Type B ─────────────────────────────────────────────────

    [Header("Type B  (e.g. Blue)")]
    [Tooltip("Type B 可随机选取的 Sprite 列表；留空则使用纯色 typeBColor")]
    public List<Sprite> typeBSprites = new List<Sprite>();
    public Color typeBColor = new Color(0.25f, 0.5f, 1f, 1f);
    [Tooltip("Type B 的刷新权重；与 typeAWeight 共同决定两种文件的出现概率")]
    [Min(0f)]
    public float typeBWeight = 1f;
    [Tooltip("错误投放 Type B 文件时，替换 Wrong Block 面板上的 Sprite；留空则不替换")]
    public Sprite typeBWrongBlockSprite;

    // ─── 刷新配置 ──────────────────────────────────────────────

    [Header("Spawning")]
    [Tooltip("场内文件数量上限；未达到上限时每隔 spawnIntervalSeconds 刷新一张")]
    [Min(1)]
    public int maxFileCount = 6;

    [Tooltip("刷新间隔（秒）")]
    [Min(0.1f)]
    public float spawnIntervalSeconds = 2.5f;

    // ─── 错误投放遮挡 ──────────────────────────────────────────

    [Header("Wrong Drop Block")]
    [Tooltip("错误投放后 Wrong Block 显示、并且无法拖拽文件的时长（秒）")]
    [Min(0.1f)]
    public float wrongDropBlockDuration = 1.5f;

    // ─── Debug ─────────────────────────────────────────────────

    [Header("Debug")]
    [Tooltip("开启后在 Console 打印详细日志（关闭后只保留 Error）")]
    [SerializeField] protected bool debugLog = false;

    // ─── 运行时状态 ────────────────────────────────────────────

    /// <summary>当前是否处于错误投放遮挡状态（SortableFile.OnBeginDrag 检测此值）。</summary>
    public bool IsBlocked => _isBlocked;

    protected readonly List<SortableFile> _activeFiles = new List<SortableFile>();
    bool _isBlocked;
    protected Coroutine _spawnCoroutine;
    Coroutine _blockCoroutine;
    Image _wrongBlockImage;

    // ─── 日志辅助 ──────────────────────────────────────────────

    protected void Log(string msg)      { if (debugLog) Debug.Log(msg, this); }
    protected void LogWarn(string msg)  { if (debugLog) Debug.LogWarning(msg, this); }
    protected void LogError(string msg) { Debug.LogError(msg, this); }

    // ─── 生命周期 ──────────────────────────────────────────────

    // Unity 会自动将返回 IEnumerator 的 Start 作为协程运行
    IEnumerator Start()
    {
        Log($"[{GetType().Name}] Start() | GameObject={gameObject.name} active={gameObject.activeInHierarchy}");

        if (spawnArea == null)     LogError($"[{GetType().Name}] ❌ spawnArea 未赋值！");
        if (filePrefab == null)    LogError($"[{GetType().Name}] ❌ filePrefab 未赋值！");
        if (leftDropZone == null)  LogWarn ($"[{GetType().Name}] ⚠ leftDropZone 未赋值。");
        if (rightDropZone == null) LogWarn ($"[{GetType().Name}] ⚠ rightDropZone 未赋值。");

        if (spawnArea == null || filePrefab == null)
        {
            LogError($"[{GetType().Name}] 关键引用缺失，终止初始化。");
            yield break;
        }

        // 绑定投放区
        if (leftDropZone != null)  leftDropZone.controller  = this;
        if (rightDropZone != null) rightDropZone.controller = this;

        if (wrongDropBlockerPanel != null)
            wrongDropBlockerPanel.SetActive(false);
        CacheWrongBlockImage();

        // 等一帧：Canvas 布局在 Start 期间未必完成
        yield return null;

        Log($"[{GetType().Name}] spawnArea.rect={spawnArea.rect} filePrefab={filePrefab.name}");

        if (filePrefab.GetComponentInChildren<SortableFile>() == null)
        {
            LogError($"[{GetType().Name}] ❌ filePrefab 层级中找不到 SortableFile 组件！");
            yield break;
        }

        // 交给子类完成各自初始化（初始刷新、SpawnLoop、计数器等）
        yield return OnAfterBaseInit();
    }

    protected virtual void OnDestroy()
    {
        if (_spawnCoroutine != null) StopCoroutine(_spawnCoroutine);
        if (_blockCoroutine != null) StopCoroutine(_blockCoroutine);
    }

    // ─── 子类扩展点 ─────────────────────────────────────────────

    /// <summary>
    /// 基类完成初始化（一帧延迟 + 引用检查）后由子类实现的入口。
    /// 子类在此进行初始刷新、开启 SpawnLoop 等操作。
    /// </summary>
    protected abstract IEnumerator OnAfterBaseInit();

    /// <summary>玩家正确分类后由 SortableFile.OnEndDrag 调用。子类负责奖励/演出/完成检测。</summary>
    public abstract void OnCorrectDrop(SortableFile file);

    /// <summary>玩家错误分类后由 SortableFile.OnEndDrag 调用。子类负责惩罚/演出。</summary>
    /// <param name="hitZone">玩家实际投入的 DropZone（可用于区分左/右区演出）</param>
    public abstract void OnWrongDrop(SortableFile file, DropZone hitZone);

    // ─── 刷新工具（供子类调用）─────────────────────────────────

    /// <summary>
    /// 生成一张文件卡片，随机类型，置于 spawnArea 内随机位置。
    /// 已添加到 _activeFiles 列表。
    /// 类型选择由 <see cref="ChooseFileType"/> 决定，子类可重写以改变分配逻辑。
    /// </summary>
    protected void SpawnFile()
    {
        if (filePrefab == null || spawnArea == null) return;

        GameObject go = Instantiate(filePrefab, spawnArea);

        RectTransform rt = go.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.sizeDelta  = fileCardSize;
            rt.anchorMin  = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot      = new Vector2(0.5f, 0.5f);

            float halfW = Mathf.Max(0f, spawnArea.rect.width  * 0.5f - fileCardSize.x * 0.5f);
            float halfH = Mathf.Max(0f, spawnArea.rect.height * 0.5f - fileCardSize.y * 0.5f);
            rt.anchoredPosition = new Vector2(
                Random.Range(-halfW, halfW),
                Random.Range(-halfH, halfH));

            Log($"[{GetType().Name}] Spawn at {rt.anchoredPosition}");
        }
        else
        {
            LogWarn($"[{GetType().Name}] ⚠ filePrefab 根节点上没有 RectTransform！");
        }

        SortableFile file = go.GetComponentInChildren<SortableFile>();
        if (file == null)
        {
            LogError($"[{GetType().Name}] ❌ 实例化后层级中找不到 SortableFile，销毁。");
            Destroy(go);
            return;
        }

        file.spawnedRoot = go;

        SortableFile.FileType type = ChooseFileType();

        List<Sprite> sprites = type == SortableFile.FileType.TypeA ? typeASprites : typeBSprites;
        Color        color   = type == SortableFile.FileType.TypeA ? typeAColor   : typeBColor;

        file.Setup(type, sprites, color, this);
        _activeFiles.Add(file);
        Log($"[{GetType().Name}] 生成成功 type={type} total={_activeFiles.Count}");
    }

    /// <summary>
    /// 决定下一张文件的类型。
    /// 默认实现：按 typeAWeight / typeBWeight 权重随机。
    /// <see cref="StoryFileSortingGame"/> 重写此方法改为从预设数量队列中取值。
    /// </summary>
    protected virtual SortableFile.FileType ChooseFileType()
    {
        float totalWeight = typeAWeight + typeBWeight;
        return (totalWeight <= 0f || Random.value < typeAWeight / totalWeight)
            ? SortableFile.FileType.TypeA
            : SortableFile.FileType.TypeB;
    }

    /// <summary>
    /// 销毁文件的 spawnedRoot（spawnArea 中的空壳）和视觉节点（SortableFile GameObject）。
    /// 同时将其从 _activeFiles 列表中移除。
    /// </summary>
    protected void DestroyFile(SortableFile file)
    {
        if (file == null) return;
        _activeFiles.Remove(file);

        // spawnedRoot 是 Prefab 根（拖拽时视觉节点已被提升到根 Canvas，spawnedRoot 留在 spawnArea 成空壳）
        if (file.spawnedRoot != null && file.spawnedRoot != file.gameObject)
            Destroy(file.spawnedRoot);

        // 始终销毁视觉节点本身
        Destroy(file.gameObject);
    }

    // ─── 错误投放遮挡（供子类调用）────────────────────────────

    /// <summary>
    /// 启动错误投放遮挡计时器（IsBlocked = true，持续 wrongDropBlockDuration 秒）。
    /// 遮挡期间 SortableFile.OnBeginDrag 会拒绝新拖拽。
    /// </summary>
    /// <param name="managePanel">
    /// true：同步显示/隐藏 <see cref="wrongDropBlockerPanel"/>。
    /// false：只挡输入，面板显隐由子类自行控制（例如 Story 的错误反馈演出）。
    /// </param>
    protected void StartWrongDropBlock(bool managePanel = true)
    {
        if (_blockCoroutine != null)
            StopCoroutine(_blockCoroutine);
        _blockCoroutine = StartCoroutine(WrongDropBlockRoutine(managePanel));
    }

    /// <summary>
    /// 按错误投放的文件类型替换 Wrong Block Sprite，再启动遮挡。
    /// </summary>
    protected void StartWrongDropBlock(SortableFile.FileType wrongFileType, bool managePanel = true)
    {
        ApplyWrongBlockSprite(wrongFileType);
        StartWrongDropBlock(managePanel);
    }

    IEnumerator WrongDropBlockRoutine(bool managePanel)
    {
        _isBlocked = true;
        if (managePanel)
            SetWrongBlockVisible(true);

        yield return new WaitForSecondsRealtime(wrongDropBlockDuration);

        _isBlocked = false;
        if (managePanel)
            SetWrongBlockVisible(false);
        _blockCoroutine = null;
    }

    /// <summary>按错误投放的文件类型，替换 Wrong Block 上 Image 的 Sprite。</summary>
    protected void ApplyWrongBlockSprite(SortableFile.FileType fileType)
    {
        if (_wrongBlockImage == null)
            CacheWrongBlockImage();
        if (_wrongBlockImage == null) return;

        Sprite sprite = fileType == SortableFile.FileType.TypeA
            ? typeAWrongBlockSprite
            : typeBWrongBlockSprite;

        if (sprite != null)
            _wrongBlockImage.sprite = sprite;
    }

    /// <summary>显示或隐藏 Wrong Block 面板。</summary>
    protected void SetWrongBlockVisible(bool visible)
    {
        if (wrongDropBlockerPanel != null && wrongDropBlockerPanel.activeSelf != visible)
            wrongDropBlockerPanel.SetActive(visible);
    }

    void CacheWrongBlockImage()
    {
        if (wrongDropBlockerPanel == null)
        {
            _wrongBlockImage = null;
            return;
        }

        _wrongBlockImage = wrongDropBlockerPanel.GetComponent<Image>();
        if (_wrongBlockImage == null)
            _wrongBlockImage = wrongDropBlockerPanel.GetComponentInChildren<Image>(true);
    }
}
