using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace JiU
{
    /// <summary>
    /// On break start, spawns a random prefab from the list at random positions in bounds at intervals (instances stack until fixed).
    /// With a WorkItem assigned, starts on break and stops on fix without wiring events.
    /// </summary>
    public class FlyingSpriteOnBreak : MonoBehaviour
    {
        [Header("Target (optional)")]
        [Tooltip("If set, start on break / stop on fix for that item; no OnBroken/OnFixed wiring needed")]
        public WorkItem workItem;

        [Header("Random prefabs (UI with RectTransform recommended)")]
        [Tooltip("Each spawn picks one at random")]
        public List<GameObject> prefabs = new List<GameObject>();

        [Tooltip("Parent for instances; unset uses bounds Rect below")]
        public RectTransform spawnParent;

        [Tooltip("Random spawn rect; unset uses root RectTransform of Canvas containing this object")]
        public RectTransform boundsRect;

        [Header("Spawn timing")]
        [Tooltip("Seconds between spawns")]
        public float spawnInterval = 0.8f;

        [Tooltip("Bring each new instance to front among siblings")]
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
        /// Start interval spawning (on break). Bind from WorkItem.OnBroken.
        /// </summary>
        public void StartEffect()
        {
            if (!HasAnyPrefab()) return;
            if (spawnParent == null)
            {
                Debug.LogWarning($"{nameof(FlyingSpriteOnBreak)} on {name}: assign Bounds Rect or place under a Canvas to resolve parent.", this);
                return;
            }

            if (_spawnRoutine != null)
                StopCoroutine(_spawnRoutine);
            _spawnRoutine = StartCoroutine(SpawnRoutine());
        }

        /// <summary>
        /// Stop spawning and destroy instances from this break. Bind from WorkItem.OnFixed.
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
