using UnityEngine;
using UnityEngine.Events;
using Uduino;

namespace JiU
{
    /// <summary>
    /// On trigger (e.g. WorkItem OnBroken/OnFixed), send Uduino outputs or run custom logic.
    /// Digital, analog, custom command, or UnityEvent-only.
    /// </summary>
    public class UduinoGameEventOutput : MonoBehaviour
    {
        public enum OutputType
        {
            [Tooltip("Digital write 0/1")]
            DigitalWrite,
            [Tooltip("Analog write 0~255")]
            AnalogWrite,
            [Tooltip("Custom sendCommand(name, params)")]
            SendCommand,
            [Tooltip("NeoPixel ring: setled to ESP32")]
            SetNeoPixel,
            [Tooltip("Only invoke OnTriggered; no Uduino")]
            OnlyInvokeEvent
        }

        [System.Serializable]
        public class OutputEntry
        {
            public OutputType type = OutputType.DigitalWrite;
            [Tooltip("Digital/analog pin number")]
            public int pin = 13;
            [Tooltip("Digital: 0 or 1; analog: 0~255")]
            public int value = 1;
            [Tooltip("SendCommand name")]
            public string commandName = "led";
            [Tooltip("SendCommand params (space-separated if multiple)")]
            public string commandParam = "on";

            [Header("SetNeoPixel only")]
            [Tooltip("Ring index per hardware: 0=Skull/GPIO21, 3=Lamp/GPIO33")]
            [Range(0, 4)]
            public int ledMonitorIndex = 0;
            [Tooltip("Preset; Custom uses RGB below")]
            public LEDColorPreset neoPixelColorPreset = LEDColorPreset.Red;
            [Range(0, 255)] public int customR = 255;
            [Range(0, 255)] public int customG = 0;
            [Range(0, 255)] public int customB = 0;
        }

        [Header("On trigger (in order)")]
        [Tooltip("Add entries: digital, analog, command, or event-only")]
        public OutputEntry[] outputs = new OutputEntry[] { new OutputEntry() };

        [Header("Optional: pass value")]
        [Tooltip("With TriggerWithValue(int), use passed int in outputs")]
        public bool useTriggerValue;
        [Tooltip("Override entry value with TriggerWithValue (Digital/Analog only)")]
        public bool valueOverridesEntry;

        [Header("Optional: extra callback")]
        [Tooltip("Bind for data/UI side effects")]
        public UnityEvent onTriggered;

        [Header("Optional: int callback (pin/value etc.)")]
        public UnityEventInt onTriggeredWithInt;

        [System.Serializable]
        public class UnityEventInt : UnityEvent<int> { }

        /// <summary>
        /// Fire outputs in order to Uduino and invoke onTriggered.
        /// Callable from WorkItem OnBroken/OnFixed etc.
        /// </summary>
        public void Trigger()
        {
            TriggerWithValue(0);
        }

        /// <summary>
        /// Set NeoPixel color by monitor index (ignores outputs list).
        /// </summary>
        public void SetLED(int monitorIndex, Color color)
        {
            UduinoLEDHelper.SetLED(monitorIndex, color);
        }

        /// <summary>
        /// Set NeoPixel by preset for monitor index.
        /// </summary>
        public void SetLED(int monitorIndex, LEDColorPreset preset)
        {
            UduinoLEDHelper.SetLED(monitorIndex, preset);
        }

        /// <summary>
        /// Trigger with int; if useTriggerValue and valueOverridesEntry, overrides entry value.
        /// </summary>
        public void TriggerWithValue(int value)
        {
            if (UduinoManager.Instance == null || !UduinoManager.Instance.IsRunning())
            {
                if (outputs != null && outputs.Length > 0)
                    Debug.LogWarning("[JiU.UduinoGameEventOutput] Uduino not running; skipping hardware, events only.");
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
