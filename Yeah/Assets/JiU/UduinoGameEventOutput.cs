using UnityEngine;
using UnityEngine.Events;
using Uduino;

namespace JiU
{
    /// <summary>
    /// 当“触发”被调用时（例如 WorkItem 的 OnBroken/OnFixed），向 Uduino 发送输出或触发自定义逻辑。
    /// 支持：数字输出、模拟输出、自定义命令；也可只触发 UnityEvent 做数据/逻辑修改。
    /// </summary>
    public class UduinoGameEventOutput : MonoBehaviour
    {
        public enum OutputType
        {
            [Tooltip("数字输出：引脚 0/1")]
            DigitalWrite,
            [Tooltip("模拟输出：引脚 0~255")]
            AnalogWrite,
            [Tooltip("自定义命令：sendCommand(命令名, 参数)")]
            SendCommand,
            [Tooltip("NeoPixel 灯环：发送 setled 命令到 ESP32")]
            SetNeoPixel,
            [Tooltip("仅触发下面的 OnTriggered，不发给 Uduino")]
            OnlyInvokeEvent
        }

        [System.Serializable]
        public class OutputEntry
        {
            public OutputType type = OutputType.DigitalWrite;
            [Tooltip("数字/模拟引脚号")]
            public int pin = 13;
            [Tooltip("数字：0 或 1；模拟：0~255")]
            public int value = 1;
            [Tooltip("SendCommand 时的命令名")]
            public string commandName = "led";
            [Tooltip("SendCommand 时的参数（可多个用空格隔开）")]
            public string commandParam = "on";

            [Header("SetNeoPixel 专用")]
            [Tooltip("灯环索引，与硬件映射一致：0=Skull/GPIO21, 3=Lamp/GPIO33")]
            [Range(0, 4)]
            public int ledMonitorIndex = 0;
            [Tooltip("预设颜色；选 Custom 时用下方 RGB")]
            public LEDColorPreset neoPixelColorPreset = LEDColorPreset.Red;
            [Range(0, 255)] public int customR = 255;
            [Range(0, 255)] public int customG = 0;
            [Range(0, 255)] public int customB = 0;
        }

        [Header("触发时执行（按顺序）")]
        [Tooltip("可添加多条：数字输出、模拟输出、自定义命令 或 仅触发事件")]
        public OutputEntry[] outputs = new OutputEntry[] { new OutputEntry() };

        [Header("可选：传递数值")]
        [Tooltip("若使用 TriggerWithValue(int)，这里会收到传入的数值并参与输出")]
        public bool useTriggerValue;
        [Tooltip("用 TriggerWithValue 的值替代上面条目的 value（仅对 Digital/Analog 有效）")]
        public bool valueOverridesEntry;

        [Header("可选：触发时额外回调")]
        [Tooltip("可绑到其他脚本修改数据或做 UI 等")]
        public UnityEvent onTriggered;

        [Header("可选：带一个 int 的回调（用于传 pin/数值等）")]
        public UnityEventInt onTriggeredWithInt;

        [System.Serializable]
        public class UnityEventInt : UnityEvent<int> { }

        /// <summary>
        /// 无参触发：按配置的 outputs 依次发送到 Uduino，并调用 onTriggered。
        /// 可由 WorkItem.OnBroken / OnFixed 等 UnityEvent 调用。
        /// </summary>
        public void Trigger()
        {
            TriggerWithValue(0);
        }

        /// <summary>
        /// 代码调用：设置指定 monitor 的 NeoPixel 颜色（不依赖 outputs 配置）。
        /// </summary>
        public void SetLED(int monitorIndex, Color color)
        {
            UduinoLEDHelper.SetLED(monitorIndex, color);
        }

        /// <summary>
        /// 代码调用：使用预设颜色设置指定 monitor 的 NeoPixel。
        /// </summary>
        public void SetLED(int monitorIndex, LEDColorPreset preset)
        {
            UduinoLEDHelper.SetLED(monitorIndex, preset);
        }

        /// <summary>
        /// 带一个 int 的触发：若 useTriggerValue 且 valueOverridesEntry，会用 value 覆盖条目的 value。
        /// </summary>
        public void TriggerWithValue(int value)
        {
            if (UduinoManager.Instance == null || !UduinoManager.Instance.IsRunning())
            {
                if (outputs != null && outputs.Length > 0)
                    Debug.LogWarning("[JiU.UduinoGameEventOutput] Uduino 未运行，跳过硬件输出，仅执行事件。");
            }

            int overrideVal = useTriggerValue && valueOverridesEntry ? value : -1;

            if (outputs != null)
            {
                for (int i = 0; i < outputs.Length; i++)
                {
                    ExecuteEntry(outputs[i], overrideVal >= 0 ? overrideVal : outputs[i].value);
                }
            }

            onTriggered?.Invoke();
            if (useTriggerValue)
                onTriggeredWithInt?.Invoke(value);
        }

        private void ExecuteEntry(OutputEntry e, int value)
        {
            switch (e.type)
            {
                case OutputType.DigitalWrite:
                    int v = Mathf.Clamp(value, 0, 255);
                    UduinoManager.Instance.digitalWrite(e.pin, v <= 0 ? 0 : 255);
                    break;
                case OutputType.AnalogWrite:
                    int a = Mathf.Clamp(value, 0, 255);
                    UduinoManager.Instance.analogWrite(e.pin, a);
                    break;
                case OutputType.SendCommand:
                    if (!string.IsNullOrEmpty(e.commandName))
                        UduinoManager.Instance.sendCommand(e.commandName, e.commandParam);
                    break;
                case OutputType.SetNeoPixel:
                    int nr, ng, nb;
                    UduinoLEDHelper.PresetToRGB(e.neoPixelColorPreset, e.customR, e.customG, e.customB, out nr, out ng, out nb);
                    UduinoLEDHelper.SetLED(e.ledMonitorIndex, nr, ng, nb);
                    break;
                case OutputType.OnlyInvokeEvent:
                    break;
            }
        }
    }
}
