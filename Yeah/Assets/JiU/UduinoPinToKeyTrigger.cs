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

        [Header("可选：自定义回调")]
        [Tooltip("达到阈值时额外调用，可不填")]
        public UnityEvent onTriggered;

        private float _lastTriggerTime = -999f;
        private bool _lastConditionMet;

        void Start()
        {
            if (UduinoManager.Instance == null)
            {
                Debug.LogWarning("[JiU.UduinoPinToKeyTrigger] 场景中未找到 UduinoManager，请确保已添加 Uduino 并连接设备。");
                return;
            }

            if (useAnalogRead)
                UduinoManager.Instance.pinMode(pinNumber, PinMode.Input);
            else
                UduinoManager.Instance.pinMode(pinNumber, PinMode.Input_pullup);
        }

        void Update()
        {
            if (UduinoManager.Instance == null || !UduinoManager.Instance.IsRunning())
                return;

            int value = useAnalogRead
                ? UduinoManager.Instance.analogRead(pinNumber)
                : UduinoManager.Instance.digitalRead(pinNumber);

            bool conditionMet = CheckCondition(value);

            if (conditionMet && !_lastConditionMet)
            {
                if (Time.time - _lastTriggerTime >= cooldownSeconds)
                {
                    _lastTriggerTime = Time.time;
                    TriggerKey();
                    onTriggered?.Invoke();
                }
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
