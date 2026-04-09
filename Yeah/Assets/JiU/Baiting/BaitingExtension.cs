using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace JiU.Baiting
{
    /// <summary>
    /// Baiting extension: listens to WorkItem OnBaiting (and optionally OnFixed) without editing core scripts.
    /// Attach to any GameObject; assign WorkItems or auto-find in scene.
    /// </summary>
    public class BaitingExtension : MonoBehaviour
    {
        [Header("WorkItems to watch")]
        [Tooltip("If empty, finds all WorkItems in scene at Start")]
        public WorkItem[] workItems;

        [Tooltip("When enabled, uses FindObjectsOfType for all WorkItems at Start")]
        public bool autoFindWorkItems = true;

        [Header("Bait duration (seconds)")]
        [Tooltip("Aligns with normal Bait and WorkItem BaitSelfFix (~3s). Phone bait may last longer: waits until IsBaiting is false before end event")]
        public float baitDuration = 3f;

        [Header("Extension events")]
        [Tooltip("Fires when any watched WorkItem enters bait (parameter: that WorkItem)")]
        public UnityEvent<WorkItem> OnBaitingStarted;

        [Tooltip("Fires after baitDuration seconds (~self-resolve time; parameter: that WorkItem)")]
        public UnityEvent<WorkItem> OnBaitingEnded;

        [Tooltip("Any bait started (no parameter; simple Inspector hooks)")]
        public UnityEvent OnAnyBaitingStarted;

        [Tooltip("Any bait ended (no parameter)")]
        public UnityEvent OnAnyBaitingEnded;

        [Header("Optional: bait count")]
        [Tooltip("Read-only: how many watched WorkItems currently have IsBaiting true")]
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
            while (item != null && item.IsBaiting)
                yield return null;

            _baitEndTimers.RemoveAll(c => c == null);
            _currentBaitingCount = Mathf.Max(0, _currentBaitingCount - 1);
            OnBaitingEnded?.Invoke(item);
            OnAnyBaitingEnded?.Invoke();
        }
    }
}
