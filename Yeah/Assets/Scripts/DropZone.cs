using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 文件分类小游戏的投放区域。
/// 投放判定已移至 SortableFile.OnEndDrag（矩形重叠检测），此组件只负责悬停高亮。
/// acceptedType 与 controller 仍由 FileSortingGame 和 SortableFile 读取。
/// </summary>
[RequireComponent(typeof(Image))]
[AddComponentMenu("MiniGame/Drop Zone")]
public class DropZone : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Tooltip("此区域接受的文件类型（TypeA = 红色，TypeB = 蓝色；左右区域各设一种）")]
    public SortableFile.FileType acceptedType = SortableFile.FileType.TypeA;

    [HideInInspector] public FileSortingGame controller;

    Image _bg;
    Color _baseColor;

    void Awake()
    {
        _bg = GetComponent<Image>();
        if (_bg != null)
            _baseColor = _bg.color;
    }

    // ─── 悬停高亮（拖拽时 CanvasGroup.blocksRaycasts=false，射线可穿透文件命中此区域）

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_bg == null) return;
        _bg.color = new Color(
            Mathf.Min(1f, _baseColor.r * 1.4f),
            Mathf.Min(1f, _baseColor.g * 1.4f),
            Mathf.Min(1f, _baseColor.b * 1.4f),
            _baseColor.a);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_bg != null)
            _bg.color = _baseColor;
    }
}
