using System.Collections;
using UnityEngine;

namespace JiU
{
    /// <summary>
    /// 当“损坏开始”时显示一个 Sprite 并在屏幕内随机移动，直到“损坏解除”时隐藏。
    /// 若填写了“指定 WorkItem”，会自动在该物件损坏时开始、修好时停止，无需再拖事件。
    /// </summary>
    public class FlyingSpriteOnBreak : MonoBehaviour
    {
        [Header("指定物件（可选）")]
        [Tooltip("若指定，则在该物件损坏时开始乱飞、修好时停止，无需再绑 OnBroken/OnFixed")]
        public WorkItem workItem;

        [Header("要乱飞的 Sprite（UI）")]
        [Tooltip("通常是 Canvas 下的 Image 的 RectTransform")]
        public RectTransform flyingSpriteRect;

        [Tooltip("不填则使用 flyingSpriteRect 所在 Canvas 的 RectTransform 作为范围")]
        public RectTransform boundsRect;

        [Header("移动参数")]
        [Tooltip("每隔多少秒随机一个新目标点")]
        public float moveInterval = 0.8f;

        [Tooltip("向目标移动的速度（像素/秒，若为 0 则每帧瞬移到新随机点）")]
        public float moveSpeed = 200f;

        [Tooltip("初始是否隐藏，StartEffect 时显示，StopEffect 时再隐藏")]
        public bool startHidden = true;

        private Canvas _canvas;
        private Coroutine _flyRoutine;
        private Vector3 _targetWorld;

        void Awake()
        {
            if (flyingSpriteRect != null && boundsRect == null)
            {
                _canvas = flyingSpriteRect.GetComponentInParent<Canvas>();
                if (_canvas != null)
                    boundsRect = _canvas.GetComponent<RectTransform>();
            }

            if (startHidden && flyingSpriteRect != null)
                flyingSpriteRect.gameObject.SetActive(false);
        }

        void Start()
        {
            if (workItem != null)
            {
                workItem.OnBroken.AddListener(StartEffect);
                workItem.OnFixed.AddListener(StopEffect);
            }
        }

        /// <summary>
        /// 开始乱飞（损坏时）。由 WorkItem.OnBroken 绑定调用。
        /// </summary>
        public void StartEffect()
        {
            if (flyingSpriteRect == null) return;

            flyingSpriteRect.gameObject.SetActive(true);
            if (_flyRoutine != null)
                StopCoroutine(_flyRoutine);
            _flyRoutine = StartCoroutine(FlyRoutine());
        }

        /// <summary>
        /// 停止并隐藏（修好时）。由 WorkItem.OnFixed 绑定调用。
        /// </summary>
        public void StopEffect()
        {
            if (_flyRoutine != null)
            {
                StopCoroutine(_flyRoutine);
                _flyRoutine = null;
            }
            if (flyingSpriteRect != null)
                flyingSpriteRect.gameObject.SetActive(false);
        }

        private IEnumerator FlyRoutine()
        {
            WaitForSeconds wait = new WaitForSeconds(moveInterval);

            while (true)
            {
                PickRandomTarget();

                if (moveSpeed <= 0f)
                {
                    SetPositionWorld(_targetWorld);
                    yield return wait;
                    continue;
                }

                float elapsed = 0f;
                Vector3 start = flyingSpriteRect.position;
                float duration = Vector3.Distance(start, _targetWorld) / moveSpeed;
                if (duration > 0.001f)
                {
                    while (elapsed < duration)
                    {
                        elapsed += Time.deltaTime;
                        float t = Mathf.Clamp01(elapsed / duration);
                        SetPositionWorld(Vector3.Lerp(start, _targetWorld, t));
                        yield return null;
                    }
                }

                SetPositionWorld(_targetWorld);
                yield return wait;
            }
        }

        private void SetPositionWorld(Vector3 worldPos)
        {
            if (flyingSpriteRect != null)
                flyingSpriteRect.position = worldPos;
        }

        private void PickRandomTarget()
        {
            if (boundsRect == null || flyingSpriteRect == null)
            {
                _targetWorld = flyingSpriteRect != null ? flyingSpriteRect.position : Vector3.zero;
                return;
            }

            Vector3[] corners = new Vector3[4];
            boundsRect.GetWorldCorners(corners);
            float minX = corners[0].x, maxX = corners[0].x, minY = corners[0].y, maxY = corners[0].y;
            for (int i = 1; i < 4; i++)
            {
                if (corners[i].x < minX) minX = corners[i].x;
                if (corners[i].x > maxX) maxX = corners[i].x;
                if (corners[i].y < minY) minY = corners[i].y;
                if (corners[i].y > maxY) maxY = corners[i].y;
            }

            _targetWorld = new Vector3(
                Random.Range(minX, maxX),
                Random.Range(minY, maxY),
                flyingSpriteRect.position.z
            );
        }
    }
}
