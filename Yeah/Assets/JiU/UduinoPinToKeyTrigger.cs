using System;
using UnityEngine;
using UnityEngine.Events;
using Uduino;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace JiU
{
    /// <summary>
    /// 从 Uduino 读取指定引脚数值，当达到设定阈值时触发一个可编辑的“虚拟按键”，
    /// 从而驱动游戏内已有的按键逻辑（如 WorkItem 的修理键）。
    /// </summary>
    public class UduinoPinToKeyTrigger : MonoBehaviour
    {
        [Header("引脚与读取方式")]
        [Tooltip("要读取的引脚编号（ESP32 上可用数字引脚或模拟引脚，如 34、35、A0 对应 36 等）")]
        public int pinNumber = 34;

        [Tooltip("勾选为模拟读取(analogRead)，不勾选为数字读取(digitalRead)")]
        public bool useAnalogRead = true;

        [Header("触发条件")]
        [Tooltip("模拟值范围 0~4095（ESP32），数字值为 0 或 1")]
        public float thresholdValue = 512f;

        public enum TriggerMode
        {
            [Tooltip("数值 >= 阈值时触发")]
            AboveOrEqual,
            [Tooltip("数值 <= 阈值时触发")]
            BelowOrEqual,
            [Tooltip("数值 == 阈值时触发（数字引脚常用）")]
            Equal
        }

        [Tooltip("满足条件时如何触发")]
        public TriggerMode triggerMode = TriggerMode.AboveOrEqual;

        [Header("要模拟的按键")]
        [Tooltip("触发时模拟按下的键，与 Inspector 里 WorkItem 的 Hotkey 对应，例如 1、2、B、Q")]
        public KeyCode triggerKeyCode = KeyCode.Alpha1;

        [Header("防抖")]
        [Tooltip("触发后多少秒内不再重复触发（避免同一帧或短时间重复）")]
        public float cooldownSeconds = 0.3f;

        [Header("多引脚时（错帧读取）")]
        [Tooltip("同一场景有多个 UduinoPinToKeyTrigger 时，用读槽错开读取，避免串口冲突。0=第一个，1=第二个… 留 0 也可自动分配")]
        public int readSlotIndex = 0;

        [Header("Debug（运行游戏时排查用）")]
        [Tooltip("勾选后在 Console 输出：当前读值、是否满足条件、是否触发、冷却/槽位等")]
        public bool debugLogs = false;
        [Tooltip("每多少帧打印一次当前读值，避免刷屏（仅当 debugLogs 开启时生效）")]
        public int debugLogIntervalFrames = 30;

        [Header("可选：自定义回调")]
        [Tooltip("达到阈值时额外调用，可不填")]
        public UnityEvent onTriggered;

        private float _lastTriggerTime = -999f;
        private bool _lastConditionMet;
        private int _lastReadValue = -1;
        private static readonly System.Collections.Generic.List<UduinoPinToKeyTrigger> s_allTriggers = new System.Collections.Generic.List<UduinoPinToKeyTrigger>();
        private float _lastDebugLogTime;
        private float _lastInvalidReadLogTime;
        private bool _hasLoggedStartup;

        void Start()
        {
            // 无论是否勾选 Debug，都打一条启动日志，方便确认脚本在运行
            Debug.Log($"[JiU.UduinoPinToKeyTrigger] 已启动: {gameObject.name} Pin={pinNumber} ({ (useAnalogRead ? "模拟" : "数字") }) " +
                $"阈值={thresholdValue}。勾选 Inspector 里「Debug Logs」可看详细读值与触发。");

            if (UduinoManager.Instance == null)
            {
                Debug.LogWarning("[JiU.UduinoPinToKeyTrigger] 场景中未找到 UduinoManager，请确保已添加 Uduino 并连接设备。");
                return;
            }

            if (useAnalogRead)
                UduinoManager.Instance.pinMode(pinNumber, PinMode.Input);
            else
                UduinoManager.Instance.pinMode(pinNumber, PinMode.Input_pullup);

            if (debugLogs)
            {
                Debug.Log($"[JiU.UduinoPinToKeyTrigger] {gameObject.name} Pin={pinNumber} ({ (useAnalogRead ? "模拟" : "数字") }) " +
                    $"阈值={thresholdValue} 模式={triggerMode} 按键={triggerKeyCode} 冷却={cooldownSeconds}s");
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
                // 未连接时每隔约 1 秒打一次，不依赖 Debug Logs 勾选，方便发现「运行游戏时没连上」
                if (Time.time - _lastDebugLogTime > 1f)
                {
                    _lastDebugLogTime = Time.time;
                    Debug.Log($"[JiU.UduinoPinToKeyTrigger] {gameObject.name} Pin={pinNumber} Uduino 未运行或未连接，跳过读取。请确认已点 Play 且设备已连接。");
                }
                return;
            }

            // 首次进入「已连接」状态时打一条，确认脚本在跑且 Uduino 已就绪
            if (!_hasLoggedStartup)
            {
                _hasLoggedStartup = true;
                Debug.Log($"[JiU.UduinoPinToKeyTrigger] {gameObject.name} Pin={pinNumber} Uduino 已连接，开始按帧读取。勾选 Debug Logs 可看具体 value。");
            }

            int total = s_allTriggers.Count;
            int myIndex = s_allTriggers.IndexOf(this);
            int slot = (readSlotIndex >= 0 && readSlotIndex < total) ? readSlotIndex : myIndex;
            bool doReadThisFrame = (total <= 1) || (slot >= 0 && (Time.frameCount % total) == slot);

            if (debugLogs && total > 1 && doReadThisFrame && Time.frameCount % (total * 60) == slot)
                Debug.Log($"[JiU.UduinoPinToKeyTrigger] {gameObject.name} Pin={pinNumber} 当前槽位 slot={slot}/{total}，本帧轮到我读");

            int value;
            if (doReadThisFrame)
            {
                value = useAnalogRead
                    ? UduinoManager.Instance.analogRead(pinNumber)
                    : UduinoManager.Instance.digitalRead(pinNumber);
                _lastReadValue = value;
                if (debugLogs && debugLogIntervalFrames > 0 && (Time.frameCount % debugLogIntervalFrames == 0))
                    Debug.Log($"[JiU.UduinoPinToKeyTrigger] {gameObject.name} Pin={pinNumber} 读到 value={value} (阈值={thresholdValue})");
                if (debugLogs && value == -1 && Time.time - _lastInvalidReadLogTime > 2f)
                {
                    _lastInvalidReadLogTime = Time.time;
                    Debug.LogWarning($"[JiU.UduinoPinToKeyTrigger] {gameObject.name} Pin={pinNumber} 读回 -1，可能未就绪或串口无响应");
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
                        Debug.Log($"[JiU.UduinoPinToKeyTrigger] {gameObject.name} Pin={pinNumber} 触发! value={value} -> 模拟按键 {triggerKeyCode}");
                    TriggerKey();
                    onTriggered?.Invoke();
                }
                else if (debugLogs)
                    Debug.Log($"[JiU.UduinoPinToKeyTrigger] {gameObject.name} Pin={pinNumber} 条件已满足但冷却中，跳过 (还需 {cooldownSeconds - (Time.time - _lastTriggerTime):F2}s)");
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
                    // 使用 QueueStateEvent + KeyboardState 模拟按下（不依赖 PressKey/ReleaseKey）
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[JiU.UduinoPinToKeyTrigger] 模拟按键失败: " + e.Message);
                }
            }
            else
            {
                if (keyboard == null)
                    Debug.LogWarning("[JiU.UduinoPinToKeyTrigger] 当前没有键盘设备，无法模拟按键。");
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
