using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace JiU
{
    /// <summary>
    /// 当“损坏开始”时，每隔一小段时间在范围内随机位置生成列表中的一个随机 Prefab（实例会保留堆叠，直到修好）。
    /// 若填写了“指定 WorkItem”，会自动在该物件损坏时开始、修好时停止，无需再拖事件。
    /// </summary>
    public class FlyingSpriteOnBreak : MonoBehaviour
    {
        [Header("指定物件（可选）")]
        [Tooltip("若指定，则在该物件损坏时开始、修好时停止，无需再绑 OnBroken/OnFixed")]
        public WorkItem workItem;

        [Header("随机 Prefab（建议为带 RectTransform 的 UI）")]
        [Tooltip("每次随机 Instantiate 其中一个")]
        public List<GameObject> prefabs = new List<GameObject>();

        [Tooltip("生成实例的父节点；不填则使用下方 Bounds Rect")]
        public RectTransform spawnParent;

        [Tooltip("随机位置的屏幕/画布范围；不填则使用本物体所在 Canvas 的根 RectTransform")]
        public RectTransform boundsRect;

        [Header("刷新参数")]
        [Tooltip("每隔多少秒生成一个新的随机 Prefab")]
        public float spawnInterval = 0.8f;

        [Tooltip("新生成时置于同级最上层，叠在旧实例上面")]
        public bool bringToFront = true;

        Coroutine _spawnRoutine;
        readonly List<GameObject> _spawnedInstances = new List<GameObject>();

        void Awake()
        {
            if (boundsRect == null)
            {
                Canvas canvas = GetComponentInParent<Canvas>();
                if (canvas != null)
                    boundsRect = canvas.GetComponent<RectTransform>();
            }

            if (spawnParent == null)
                spawnParent = boundsRect;
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
        /// 开始按间隔随机生成（损坏时）。由 WorkItem.OnBroken 绑定调用。
        /// </summary>
        public void StartEffect()
        {
            if (!HasAnyPrefab()) return;
            if (spawnParent == null)
            {
                Debug.LogWarning($"{nameof(FlyingSpriteOnBreak)} on {name}: 需要指定 Bounds Rect 或把脚本放在 Canvas 下以解析父节点。", this);
                return;
            }

            if (_spawnRoutine != null)
                StopCoroutine(_spawnRoutine);
            _spawnRoutine = StartCoroutine(SpawnRoutine());
        }

        /// <summary>
        /// 停止协程并销毁本次损坏期间生成的所有实例。由 WorkItem.OnFixed 绑定调用。
        /// </summary>
        public void StopEffect()
        {
            if (_spawnRoutine != null)
            {
                StopCoroutine(_spawnRoutine);
                _spawnRoutine = null;
            }

            ClearSpawnedInstances();
        }

        void OnDestroy()
        {
            ClearSpawnedInstances();
        }

        IEnumerator SpawnRoutine()
        {
            var wait = new WaitForSeconds(Mathf.Max(0f, spawnInterval));

            while (true)
            {
                yield return wait;

                GameObject prefab = PickRandomPrefab();
                if (prefab == null) continue;

                Vector3 zRef = spawnParent.position;
                Vector3 pos = GetRandomWorldPositionInBounds(zRef.z);
                if (float.IsNaN(pos.x)) continue;

                GameObject instance = Instantiate(prefab, spawnParent);
                _spawnedInstances.Add(instance);

                RectTransform rt = instance.GetComponent<RectTransform>();
                if (rt != null)
                {
                    if (bringToFront)
                        rt.SetAsLastSibling();
                    pos.z = rt.position.z;
                    rt.position = pos;
                }
                else
                    instance.transform.position = pos;
            }
        }

        void ClearSpawnedInstances()
        {
            for (int i = 0; i < _spawnedInstances.Count; i++)
            {
                if (_spawnedInstances[i] != null)
                    Destroy(_spawnedInstances[i]);
            }
            _spawnedInstances.Clear();
        }

        bool HasAnyPrefab()
        {
            if (prefabs == null) return false;
            for (int i = 0; i < prefabs.Count; i++)
            {
                if (prefabs[i] != null) return true;
            }
            return false;
        }

        GameObject PickRandomPrefab()
        {
            if (prefabs == null || prefabs.Count == 0) return null;

            int valid = 0;
            for (int i = 0; i < prefabs.Count; i++)
            {
                if (prefabs[i] != null) valid++;
            }
            if (valid == 0) return null;

            int pick = Random.Range(0, valid);
            for (int i = 0; i < prefabs.Count; i++)
            {
                if (prefabs[i] == null) continue;
                if (pick == 0) return prefabs[i];
                pick--;
            }
            return null;
        }

        Vector3 GetRandomWorldPositionInBounds(float zWorld)
        {
            if (boundsRect == null)
                return new Vector3(float.NaN, float.NaN, zWorld);

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

            return new Vector3(
                Random.Range(minX, maxX),
                Random.Range(minY, maxY),
                zWorld
            );
        }
    }
}
