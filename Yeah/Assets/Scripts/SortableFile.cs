using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 可拖拽的文件卡片，用于文件分类小游戏。
/// 需要挂在含有 Image 组件的 GameObject 上；CanvasGroup 会在运行时自动添加（如未预设）。
/// 投放判定基于矩形重叠，不依赖 IDropHandler，兼容嵌套 Canvas 结构。
/// </summary>
[RequireComponent(typeof(Image))]
[AddComponentMenu("MiniGame/Sortable File")]
public class SortableFile : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public enum FileType { TypeA, TypeB }

    [HideInInspector] public FileType fileType;
    [HideInInspector] public FileSortingGame controller;

    /// <summary>
    /// 由 FileSortingGame 在 Instantiate 后赋值为整个 Prefab 的根 GameObject。
    /// 正确投放时销毁此对象（而非仅销毁挂有 SortableFile 的子节点）。
    /// </summary>
    [HideInInspector] public GameObject spawnedRoot;

    Image _image;
    RectTransform _rt;
    CanvasGroup _cg;
    Canvas _rootCanvas;

    Transform _homeParent;
    int _homeSiblingIndex;
    Vector2 _homeAnchoredPos;
    bool _droppedOnZone;
    bool _isDragging;

    void Awake()
    {
        _image = GetComponent<Image>();
        _rt    = GetComponent<RectTransform>();
        _cg    = GetComponent<CanvasGroup>();
        if (_cg == null)
            _cg = gameObject.AddComponent<CanvasGroup>();
    }

    /// <summary>由 FileSortingGame 在 Instantiate 后调用，完成初始化。</summary>
    public void Setup(FileType type, List<Sprite> possibleSprites, Color fallbackColor, FileSortingGame ctrl)
    {
        fileType   = type;
        controller = ctrl;

        // 防御性初始化：Awake 应已执行，但以防万一
        if (_image == null) _image = GetComponent<Image>();
        if (_image == null)
        {
            Debug.LogError("[SortableFile] 未找到 Image 组件！", this);
            return;
        }

        if (possibleSprites != null && possibleSprites.Count > 0)
        {
            _image.sprite = possibleSprites[Random.Range(0, possibleSprites.Count)];
            _image.color  = Color.white;
        }
        else
        {
            _image.sprite = null;
            _image.color  = fallbackColor;
        }
    }

    // ─── 拖拽事件 ───────────────────────────────────────────────

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (controller != null && controller.IsBlocked)
            return;

        _homeParent        = transform.parent;
        _homeSiblingIndex  = transform.GetSiblingIndex();
        _homeAnchoredPos   = _rt.anchoredPosition;
        _droppedOnZone     = false;
        _isDragging        = true;

        // 提升到根 Canvas，避免被 spawnArea / 内层 Canvas 裁剪遮挡
        Canvas c = GetComponentInParent<Canvas>();
        _rootCanvas = c != null ? c.rootCanvas : null;
        if (_rootCanvas != null)
            transform.SetParent(_rootCanvas.transform, true);

        _cg.alpha          = 0.8f;
        _cg.blocksRaycasts = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_isDragging) return;
        float scale = _rootCanvas != null ? _rootCanvas.scaleFactor : 1f;
        _rt.anchoredPosition += eventData.delta / scale;

        // 将卡片限制在拖拽边界内（优先用 dragBoundaryRect，未设置时退回 spawnArea）
        if (controller != null && _rootCanvas != null)
        {
            RectTransform boundary = controller.dragBoundaryRect != null
                ? controller.dragBoundaryRect
                : controller.spawnArea;
            if (boundary != null)
                _rt.anchoredPosition = ClampToSpawnArea(_rt.anchoredPosition, boundary);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!_isDragging) return;
        _isDragging = false;

        _cg.alpha          = 1f;
        _cg.blocksRaycasts = true;

        // ─── 投放判定（矩形重叠，不依赖 IDropHandler）───────────────
        // 避免嵌套 Canvas / GraphicRaycaster 干扰 IDropHandler 触发链
        if (controller != null && !_droppedOnZone)
        {
            DropZone hit = null;

            if (controller.leftDropZone != null)
            {
                RectTransform lrt = controller.leftDropZone.GetComponent<RectTransform>();
                if (lrt != null && OverlapsInScreenSpace(_rt, lrt))
                    hit = controller.leftDropZone;
            }

            if (hit == null && controller.rightDropZone != null)
            {
                RectTransform rrt = controller.rightDropZone.GetComponent<RectTransform>();
                if (rrt != null && OverlapsInScreenSpace(_rt, rrt))
                    hit = controller.rightDropZone;
            }

            if (hit != null)
            {
                _droppedOnZone = true; // 无论对错都标记，阻止下方 return-to-home 逻辑

                if (fileType == hit.acceptedType)
                {
                    controller.OnCorrectDrop(this);
                }
                else
                {
                    controller.OnWrongDrop(this); // 错误投放：销毁文件 + 触发遮挡
                }
                return;
            }
        }

        // 未命中任何区域，或投放错误 → 回到 spawnArea 但保留当前屏幕位置（桌面文件风格）
        if (!_droppedOnZone && _homeParent != null)
        {
            // worldPositionStays=true：Unity 自动将当前世界坐标换算为 homeParent 下的 anchoredPosition
            // 不重置 anchoredPosition，文件停在用户松手的位置
            transform.SetParent(_homeParent, true);
        }
    }

    // ─── 工具 ────────────────────────────────────────────────────

    /// <summary>
    /// 将拖拽时（已提升到根 Canvas）的 anchoredPosition 限制在 spawnArea 内。
    /// 以卡片的 pivot 为基准，确保整张卡片都在边界内。
    /// </summary>
    Vector2 ClampToSpawnArea(Vector2 pos, RectTransform spawnArea)
    {
        RectTransform canvasRT = _rootCanvas.GetComponent<RectTransform>();
        if (canvasRT == null) return pos;

        // 将 spawnArea 四角从世界坐标转到根 Canvas 本地坐标
        Vector3[] corners = new Vector3[4];
        spawnArea.GetWorldCorners(corners);
        Vector2 bMin = canvasRT.InverseTransformPoint(corners[0]); // bottom-left
        Vector2 bMax = canvasRT.InverseTransformPoint(corners[2]); // top-right

        // 卡片尺寸（sizeDelta 不随 reparent 改变）
        float w  = _rt.rect.width;
        float h  = _rt.rect.height;
        float px = _rt.pivot.x;
        float py = _rt.pivot.y;

        // pivot 点允许的范围（让整张卡片始终在边界内）
        float xMin = bMin.x + px * w;
        float xMax = bMax.x - (1f - px) * w;
        float yMin = bMin.y + py * h;
        float yMax = bMax.y - (1f - py) * h;

        return new Vector2(
            Mathf.Clamp(pos.x, Mathf.Min(xMin, xMax), Mathf.Max(xMin, xMax)),
            Mathf.Clamp(pos.y, Mathf.Min(yMin, yMax), Mathf.Max(yMin, yMax)));
    }

    /// <summary>用屏幕空间 World Corners 判断两个 RectTransform 是否有重叠区域。</summary>
    static bool OverlapsInScreenSpace(RectTransform a, RectTransform b)
    {
        if (a == null || b == null) return false;

        var ca = new Vector3[4];
        var cb = new Vector3[4];
        a.GetWorldCorners(ca);
        b.GetWorldCorners(cb);

        // corners: 0=BL 1=TL 2=TR 3=BR
        var ra = new Rect(ca[0].x, ca[0].y, ca[2].x - ca[0].x, ca[2].y - ca[0].y);
        var rb = new Rect(cb[0].x, cb[0].y, cb[2].x - cb[0].x, cb[2].y - cb[0].y);

        return ra.Overlaps(rb);
    }
}
