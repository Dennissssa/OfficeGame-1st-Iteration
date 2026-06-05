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
                if (fileType == hit.acceptedType)
                {
                    _droppedOnZone = true;
                    controller.OnCorrectDrop(this);
                    return; // 已销毁，不需要返回原位
                }
                else
                {
                    controller.OnWrongDrop(this);
                    // 继续执行下方：文件返回原位
                }
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
