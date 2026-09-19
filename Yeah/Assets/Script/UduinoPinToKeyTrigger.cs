using System;
using UnityEngine;
using UnityEngine.Events;
using Uduino;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace JiU
{
    /// <summary>
    /// Reads a Uduino pin; when threshold is met, fires a configurable virtual key
    /// to drive existing input logic (e.g. WorkItem repair hotkey).
    /// </summary>
    public class UduinoPinToKeyTrigger : MonoBehaviour
    {
        [Header("Pin and read mode")]
        [Tooltip("Pin to read (ESP32: digital or analog, e.g. 34, 35; A0 may map to 36 etc.)")]
        public int pinNumber = 34;

        [Tooltip("Use analogRead when on, digitalRead when off")]
        public bool useAnalogRead = true;

        [Header("Trigger condition")]
        [Tooltip("Analog ~0~4095 on ESP32; digital 0 or 1")]
        public float thresholdValue = 512f;

        public enum TriggerMode
        {
            [Tooltip("Fire when value >= threshold")]
            AboveOrEqual,
            [Tooltip("Fire when value <= threshold")]
            BelowOrEqual,
            [Tooltip("Fire when value == threshold (common for digital)")]
            Equal
        }

        [Tooltip("How threshold is evaluated")]
        public TriggerMode triggerMode = TriggerMode.AboveOrEqual;

        [Header("Key to simulate")]
        [Tooltip("Key to synthesize on trigger; match WorkItem repair hotkey in Inspector (e.g. 1, 2, B, Q)")]
        public KeyCode triggerKeyCode = KeyCode.Alpha1;

        [Header("Debounce")]
        [Tooltip("Seconds after trigger before another can fire")]
        public float cooldownSeconds = 0.3f;

        [Header("Staggered reads (multiple pins)")]
        [Tooltip("With several triggers, stagger reads by slot to reduce serial contention. 0 = first, 1 = second… 0 can auto-assign")]
        public int readSlotIndex = 0;

        [Header("Debug")]
        [Tooltip("Log reads, condition, triggers, cooldown/slot to Console")]
        public bool debugLogs = false;
        [Tooltip("Frames between value logs when debugLogs is on")]
        public int debugLogIntervalFrames = 30;

        [Header("Optional callback")]
        [Tooltip("Invoked when threshold fires; optional")]
        public UnityEvent onTriggered;

        [Header("Direct repair targets (recommended with WorkItem)")]
        [Tooltip("On trigger, call TryRepair() on these items only")]
        public WorkItem[] directRepairTargets = Array.Empty<WorkItem>();
        [Tooltip("When on, simulates the key above so all WorkItems bound to that key get repair; with direct targets, prefer off to avoid wrong repairs")]
        public bool simulateKey = true;

        private float _lastTriggerTime = -999f;
        private bool _lastConditionMet;
        private int _lastReadValue = -1;
        private static readonly System.Collections.Generic.List<UduinoPinToKeyTrigger> s_allTriggers = new System.Collections.Generic.List<UduinoPinToKeyTrigger>();
        private float _lastDebugLogTime;
        private float _lastInvalidReadLogTime;
        private bool _hasLoggedStartup;

        void Start()
        {
            // Startup log even without Debug to confirm script is running
            Debug.Log($"[JiU.UduinoPinToKeyTrigger] Started: {gameObject.name} Pin={pinNumber} ({(useAnalogRead ? "analog" : "digital")}) " +
                $"threshold={thresholdValue}. Enable Debug Logs in Inspector for detailed reads/triggers.");

            if (UduinoManager.Instance == null)
            {
                Debug.LogWarning("[JiU.UduinoPinToKeyTrigger] UduinoManager not found in scene; add Uduino and connect device.");
                return;
            }

            if (useAnalogRead)
                UduinoManager.Instance.pinMode(pinNumber, PinMode.Input);
            else
                UduinoManager.Instance.pinMode(pinNumber, PinMode.Input_pullup);

            if (debugLogs)
            {
                Debug.Log($"[JiU.UduinoPinToKeyTrigger] {gameObject.name} Pin={pinNumber} ({(useAnalogRead ? "analog" : "digital")}) " +
                    $"threshold={thresholdValue} mode={triggerMode} key={triggerKeyCode} cooldown={cooldownSeconds}s");
            }
        }

        void OnEnable()
        {
            lock (s_allTriggers)
            {
                if (!s_allTriggers.Contains(this))
                    s_allTriggers.Add(this);
            }
        }

        void OnDisable()
        {
            lock (s_allTriggers)
            {
                s_allTriggers.Remove(this);
            }
        }

        void Update()
        {
            if (UduinoManager.Instance == null || !UduinoManager.Instance.IsRunning())
            {
                // ~1s log when not connected, without needing Debug Logs
                if (Time.time - _lastDebugLogTime > 1f)
                {
                    _lastDebugLogTime = Time.time;
                    Debug.Log($"[JiU.UduinoPinToKeyTrigger] {gameObject.name} Pin={pinNumber} Uduino not running or not connected; skipping read. Enter Play and connect device.");
                }
                return;
            }

            // First frame Uduino is connected
            if (!_hasLoggedStartup)
            {
                _hasLoggedStartup = true;
                Debug.Log($"[JiU.UduinoPinToKeyTrigger] {gameObject.name} Pin={pinNumber} Uduino connected; reading each frame. Debug Logs shows values.");
            }

            int total = s_allTriggers.Count;
            int myIndex = s_allTriggers.IndexOf(this);
            int slot = (readSlotIndex >= 0 && readSlotIndex < total) ? readSlotIndex : myIndex;
            bool doReadThisFrame = (total <= 1) || (slot >= 0 && (Time.frameCount % total) == slot);

            if (debugLogs && total > 1 && doReadThisFrame && Time.frameCount % (total * 60) == slot)
                Debug.Log($"[JiU.UduinoPinToKeyTrigger] {gameObject.name} Pin={pinNumber} read slot={slot}/{total}, this frame is my turn");

            int value;
            if (doReadThisFrame)
            {
                value = useAnalogRead
                    ? UduinoManager.Instance.analogRead(pinNumber)
                    : UduinoManager.Instance.digitalRead(pinNumber);
                _lastReadValue = value;
                if (debugLogs && debugLogIntervalFrames > 0 && (Time.frameCount % debugLogIntervalFrames == 0))
                    Debug.Log($"[JiU.UduinoPinToKeyTrigger] {gameObject.name} Pin={pinNumber} read value={value} (threshold={thresholdValue})");
                if (debugLogs && value == -1 && Time.time - _lastInvalidReadLogTime > 2f)
                {
                    _lastInvalidReadLogTime = Time.time;
                    Debug.LogWarning($"[JiU.UduinoPinToKeyTrigger] {gameObject.name} Pin={pinNumber} read -1; board not ready or serial no response");
                }
            }
            else
            {
                value = _lastReadValue >= 0 ? _lastReadValue : 0;
            }

            bool conditionMet = CheckCondition(value);

            if (conditionMet && !_lastConditionMet)
            {
                if (Time.time - _lastTriggerTime >= cooldownSeconds)
                {
                    _lastTriggerTime = Time.time;
                    if (debugLogs)
                        Debug.Log($"[JiU.UduinoPinToKeyTrigger] {gameObject.name} Pin={pinNumber} TRIGGER value={value}" + (simulateKey ? $" -> simulate key {triggerKeyCode}" : " -> direct repair only"));
                    if (simulateKey)
                        TriggerKey();
                    onTriggered?.Invoke();
                    foreach (var w in directRepairTargets)
                    {
                        if (w != null)
                            w.TryRepair();
                    }
                }
                else if (debugLogs)
                    Debug.Log($"[JiU.UduinoPinToKeyTrigger] {gameObject.name} Pin={pinNumber} condition met but cooldown ({cooldownSeconds - (Time.time - _lastTriggerTime):F2}s left)");
            }

            _lastConditionMet = conditionMet;
        }

        private bool CheckCondition(int value)
        {
            float v = value;
            switch (triggerMode)
            {
                case TriggerMode.AboveOrEqual: return v >= thresholdValue;
                case TriggerMode.BelowOrEqual: return v <= thresholdValue;
                case TriggerMode.Equal: return Mathf.Approximately(v, thresholdValue);
                default: return false;
            }
        }

        private void TriggerKey()
        {
            Key key = KeyCodeToInputSystemKey(triggerKeyCode);
            var keyboard = Keyboard.current;
            if (keyboard != null && key != Key.None)
            {
                try
                {
                    // QueueStateEvent + KeyboardState (no PressKey/ReleaseKey)
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[JiU.UduinoPinToKeyTrigger] Key simulation failed: " + e.Message);
                }
            }
            else
            {
                if (keyboard == null)
                    Debug.LogWarning("[JiU.UduinoPinToKeyTrigger] No keyboard device; cannot simulate key.");
            }
        }

        private static Key KeyCodeToInputSystemKey(KeyCode kc)
        {
            switch (kc)
            {
                case KeyCode.Alpha0: return Key.Digit0;
                case KeyCode.Alpha1: return Key.Digit1;
                case KeyCode.Alpha2: return Key.Digit2;
                case KeyCode.Alpha3: return Key.Digit3;
                case KeyCode.Alpha4: return Key.Digit4;
                case KeyCode.Alpha5: return Key.Digit5;
                case KeyCode.Alpha6: return Key.Digit6;
                case KeyCode.Alpha7: return Key.Digit7;
                case KeyCode.Alpha8: return Key.Digit8;
                case KeyCode.Alpha9: return Key.Digit9;
                case KeyCode.Keypad0: return Key.Numpad0;
                case KeyCode.Keypad1: return Key.Numpad1;
                case KeyCode.Keypad2: return Key.Numpad2;
                case KeyCode.Keypad3: return Key.Numpad3;
                case KeyCode.Keypad4: return Key.Numpad4;
                case KeyCode.Keypad5: return Key.Numpad5;
                case KeyCode.Keypad6: return Key.Numpad6;
                case KeyCode.Keypad7: return Key.Numpad7;
                case KeyCode.Keypad8: return Key.Numpad8;
                case KeyCode.Keypad9: return Key.Numpad9;
                case KeyCode.A: return Key.A;
                case KeyCode.B: return Key.B;
                case KeyCode.C: return Key.C;
                case KeyCode.D: return Key.D;
                case KeyCode.E: return Key.E;
                case KeyCode.F: return Key.F;
                case KeyCode.G: return Key.G;
                case KeyCode.H: return Key.H;
                case KeyCode.I: return Key.I;
                case KeyCode.J: return Key.J;
                case KeyCode.K: return Key.K;
                case KeyCode.L: return Key.L;
                case KeyCode.M: return Key.M;
                case KeyCode.N: return Key.N;
                case KeyCode.O: return Key.O;
                case KeyCode.P: return Key.P;
                case KeyCode.Q: return Key.Q;
                case KeyCode.R: return Key.R;
                case KeyCode.S: return Key.S;
                case KeyCode.T: return Key.T;
                case KeyCode.U: return Key.U;
                case KeyCode.V: return Key.V;
                case KeyCode.W: return Key.W;
                case KeyCode.X: return Key.X;
                case KeyCode.Y: return Key.Y;
                case KeyCode.Z: return Key.Z;
                case KeyCode.Space: return Key.Space;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.Escape: return Key.Escape;
                default: return Key.None;
            }
        }
    }
}
