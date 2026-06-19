using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 让 UI Panel 像 macOS 窗口一样：可在指定边界内自由拖动，点击时自动置顶。
///
/// 使用方法：
///   1. 将此组件挂在窗口的根 RectTransform 上。
///   2. 在 Inspector 中将标题栏 RectTransform 拖入 dragHandle（推荐）；
///      若留空，整个窗口均可拖动（此时窗口内的拖拽操作会被优先截获）。
///   3. 将限制区域的 RectTransform 拖入 boundaryRect；留空则不限制。
///   4. 确保父级 Canvas 有 GraphicRaycaster。
///
/// 与小游戏兼容性说明：
///   窗口内有自己拖拽逻辑的子元素（如 SortableFile）会自行消耗拖拽事件，
///   不会触发窗口移动；点击这些子元素仍会通过事件冒泡触发窗口置顶。
/// </summary>
[AddComponentMenu("UI/Draggable Window")]
public class DraggableWindow : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("拖拽设置")]
    [Tooltip("仅允许从此 RectTransform（如标题栏）内拖动窗口。\n" +
             "留空：整个窗口面板均可拖动（但内容区若有其他拖拽组件，它们会优先）。")]
    public RectTransform dragHandle;

    [Tooltip("窗口移动的边界 RectTransform（窗口不会超出此范围）。留空：不限制。")]
    public RectTransform boundaryRect;

    // ─── 内部状态 ─────────────────────────────────────────────

    RectTransform _rt;
    bool _isDragging;

    void Awake()
    {
        _rt = GetComponent<RectTransform>();
    }

    // ─── 置顶：点击窗口任意位置（包括内部子元素的冒泡点击）都会置顶 ────

    public void OnPointerDown(PointerEventData eventData)
    {
        transform.SetAsLastSibling();
    }

    // ─── 拖拽开始：验证拖拽把手 ──────────────────────────────────

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (dragHandle != null)
        {
            // 只有从 dragHandle 区域（或其子节点）开始的拖拽才移动窗口
            GameObject pressed = eventData.pointerPressRaycast.gameObject;
            if (!IsDescendantOrSelf(pressed, dragHandle))
            {
                _isDragging = false;
                return;
            }
        }

        _isDragging = true;
    }

    // ─── 拖拽中：在父级坐标系计算增量，兼容任意 Canvas 类型和缩放 ────

    public void OnDrag(PointerEventData eventData)
    {
        if (!_isDragging) return;

        RectTransform parentRT = _rt.parent as RectTransform;
        if (parentRT == null) return;

        Camera cam = eventData.pressEventCamera; // Screen Space Overlay 时为 null，正确

        // 将屏幕空间 delta 转换为父级本地坐标系的 delta
        // （两次独立转换后相减，精确处理父级缩放/旋转）
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentRT, eventData.position - eventData.delta, cam, out Vector2 prevLocal);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentRT, eventData.position, cam, out Vector2 curLocal);

        Vector2 newPos = _rt.anchoredPosition + (curLocal - prevLocal);

        if (boundaryRect != null)
            newPos = ClampToBoundary(newPos);

        _rt.anchoredPosition = newPos;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _isDragging = false;
    }

    // ─── 工具方法 ─────────────────────────────────────────────

    /// <summary>判断 go 是否是 ancestor（或 ancestor 自身）的子孙节点。</summary>
    static bool IsDescendantOrSelf(GameObject go, RectTransform ancestor)
    {
        if (go == null || ancestor == null) return false;
        Transform t = go.transform;
        while (t != null)
        {
            if (t == ancestor) return true;
            t = t.parent;
        }
        return false;
    }

    /// <summary>
    /// 将目标 anchoredPosition 限制在 boundaryRect 范围内。
    /// 考虑窗口自身的 pivot，确保整个窗口可见区域都在边界内。
    /// </summary>
    Vector2 ClampToBoundary(Vector2 pos)
    {
        RectTransform parentRT = _rt.parent as RectTransform;
        if (parentRT == null) return pos;

        // 将边界四角从世界坐标转换到父级本地坐标
        Vector3[] corners = new Vector3[4];
        boundaryRect.GetWorldCorners(corners);
        Vector2 bMin = parentRT.InverseTransformPoint(corners[0]); // bottom-left
        Vector2 bMax = parentRT.InverseTransformPoint(corners[2]); // top-right

        // anchoredPosition 是 pivot 点在父级坐标系中的位置。
        // 根据窗口尺寸和 pivot 推算 pivot 点允许的范围：
        //   pivot.x = 0   → pivot 在左边，左边距 = 0，右边距 = w
        //   pivot.x = 0.5 → pivot 在中间，左右各 w/2
        //   pivot.x = 1   → pivot 在右边，左边距 = w，右边距 = 0
        float w  = _rt.rect.width;
        float h  = _rt.rect.height;
        float px = _rt.pivot.x;
        float py = _rt.pivot.y;

        float xMin = bMin.x + px * w;
        float xMax = bMax.x - (1f - px) * w;
        float yMin = bMin.y + py * h;
        float yMax = bMax.y - (1f - py) * h;

        // 若窗口比边界还大，Clamp 会返回 min（退化处理，不报错）
        return new Vector2(
            Mathf.Clamp(pos.x, Mathf.Min(xMin, xMax), Mathf.Max(xMin, xMax)),
            Mathf.Clamp(pos.y, Mathf.Min(yMin, yMax), Mathf.Max(yMin, yMax)));
    }
}
