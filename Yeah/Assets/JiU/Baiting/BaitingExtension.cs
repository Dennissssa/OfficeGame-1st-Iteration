using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace JiU.Baiting
{
    /// <summary>
    /// Baiting 扩展：通过监听 WorkItem 的 OnBaiting（及可选 OnFixed）在不修改原脚本的前提下扩展逻辑。
    /// 可挂到场景中任意 GameObject，指定要监听的 WorkItem 或自动查找。
    /// </summary>
    public class BaitingExtension : MonoBehaviour
    {
        [Header("监听的 WorkItem")]
        [Tooltip("留空则会在 Start 时自动查找场景中所有 WorkItem")]
        public WorkItem[] workItems;

        [Tooltip("勾选则 Start 时用 FindObjectsOfType 查找所有 WorkItem")]
        public bool autoFindWorkItems = true;

        [Header("诱饵持续时间（秒）")]
        [Tooltip("与 WorkItem 内 BaitSelfFix 的 3 秒保持一致，用于在约 3 秒后触发 OnBaitingEnded；若你改了 WorkItem 请同步改这里")]
        public float baitDuration = 3f;

        [Header("扩展事件")]
        [Tooltip("任意一个 WorkItem 进入诱饵状态时触发（参数：该 WorkItem）")]
        public UnityEvent<WorkItem> OnBaitingStarted;

        [Tooltip("在 baitDuration 秒后触发，表示该诱饵约在此时自愈（参数：该 WorkItem）")]
        public UnityEvent<WorkItem> OnBaitingEnded;

        [Tooltip("任意诱饵开始（无参数，便于在 Inspector 里接简单回调）")]
        public UnityEvent OnAnyBaitingStarted;

        [Tooltip("任意诱饵结束（无参数）")]
        public UnityEvent OnAnyBaitingEnded;

        [Header("可选：当前处于诱饵状态的数量")]
        [Tooltip("仅统计本扩展监听的 WorkItem 中当前 IsBaiting 的数量（只读）")]
        public int currentBaitingCount => _currentBaitingCount;

        private int _currentBaitingCount;
        private readonly List<WorkItem> _trackedItems = new List<WorkItem>();
        private readonly List<UnityEngine.Events.UnityAction> _listeners = new List<UnityEngine.Events.UnityAction>();
        private readonly List<Coroutine> _baitEndTimers = new List<Coroutine>();

        void Start()
        {
            _trackedItems.Clear();
            _listeners.Clear();
            if (workItems != null && workItems.Length > 0)
            {
                foreach (var w in workItems)
                {
                    if (w != null && !_trackedItems.Contains(w))
                        _trackedItems.Add(w);
                }
            }
            if (autoFindWorkItems)
            {
                var all = FindObjectsOfType<WorkItem>(true);
                foreach (var w in all)
                {
                    if (w != null && !_trackedItems.Contains(w))
                        _trackedItems.Add(w);
                }
            }

            foreach (WorkItem it in _trackedItems)
            {
                UnityEngine.Events.UnityAction action = () => OnWorkItemBaiting(it);
                _listeners.Add(action);
                it.OnBaiting.AddListener(action);
            }
        }

        void OnDestroy()
        {
            foreach (var c in _baitEndTimers)
            {
                if (c != null) StopCoroutine(c);
            }
            _baitEndTimers.Clear();

            int n = Mathf.Min(_trackedItems.Count, _listeners.Count);
            for (int i = 0; i < n; i++)
            {
                var item = _trackedItems[i];
                var action = _listeners[i];
                if (item != null)
                    item.OnBaiting.RemoveListener(action);
            }
            _trackedItems.Clear();
            _listeners.Clear();
        }

        private void OnWorkItemBaiting(WorkItem item)
        {
            _currentBaitingCount++;
            OnBaitingStarted?.Invoke(item);
            OnAnyBaitingStarted?.Invoke();

            var timer = StartCoroutine(BaitEndTimer(item));
            _baitEndTimers.Add(timer);
        }

        private IEnumerator BaitEndTimer(WorkItem item)
        {
            yield return new WaitForSeconds(baitDuration);
            _baitEndTimers.RemoveAll(c => c == null);
            if (item != null && item.IsBaiting == false)
            {
                // 可能已被 Fix 等提前结束，仅做数量修正
            }
            _currentBaitingCount = Mathf.Max(0, _currentBaitingCount - 1);
            OnBaitingEnded?.Invoke(item);
            OnAnyBaitingEnded?.Invoke();
        }
    }
}
