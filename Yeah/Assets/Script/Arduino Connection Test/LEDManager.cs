using UnityEngine;
using UnityEngine.Events;
using System;
using System.Collections.Generic;
using System.IO.Ports;

/// <summary>
/// One board, multiple LEDs over one serial link to Arduino. Optional WorkItem bind: Broke/Bait colors; repair restores default.
/// </summary>
public class LEDManager : MonoBehaviour
{
    [Header("Board serial (all LEDs share one port)")]
    [Tooltip("Arduino port, e.g. COM4 (Windows) or /dev/ttyUSB0 (Mac/Linux)")]
    [SerializeField] private string portName = "COM4";

    [Tooltip("Match Arduino Serial Monitor, often 9600")]
    [SerializeField] private int baudRate = 9600;

    [Header("LED list")]
    [Tooltip("Each entry: pin, default/on color, Broke/Bait colors; optional WorkItem drives colors")]
    [SerializeField] private LEDConfig[] leds = Array.Empty<LEDConfig>();

    private SerialPort serial;
    private readonly List<WorkItemListener> workItemListeners = new List<WorkItemListener>();

    private void Start()
    {
        if (string.IsNullOrEmpty(portName))
        {
            Debug.LogWarning("[LEDManager] Port Name not set; LED control disabled.");
            return;
        }

        try
        {
            serial = new SerialPort(portName, baudRate);
            serial.ReadTimeout = 10;
            serial.Open();
            Debug.Log($"[LEDManager] Opened port: {portName}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LEDManager] Failed to open {portName}: {ex.Message}\n" +
                "Check: 1) Correct port; 2) Serial Monitor closed; 3) Port not in use.");
            return;
        }

        // Light all LEDs to configured default at game start
        for (int i = 0; i < leds.Length; i++)
            SetLEDColor(i, leds[i].OnColor);

        // WorkItem bind: Broke/Bait colors; OnFixed or OnBaitingEnded restores default
        for (int i = 0; i < leds.Length; i++)
        {
            var config = leds[i];
            if (config.WorkItem == null) continue;

            int idx = i;
            UnityAction onBroken = () => SetLEDToBrokeState(idx);
            UnityAction onBaiting = () => SetLEDToBaitState(idx);
            UnityAction onFixed = () => RestoreLEDColor(idx);
            UnityAction onBaitingEnded = () => RestoreLEDColor(idx);

            config.WorkItem.OnBroken.AddListener(onBroken);
            config.WorkItem.OnBaiting.AddListener(onBaiting);
            config.WorkItem.OnFixed.AddListener(onFixed);
            config.WorkItem.OnBaitingEnded.AddListener(onBaitingEnded);

            workItemListeners.Add(new WorkItemListener(config.WorkItem, onBroken, onBaiting, onFixed, onBaitingEnded));
        }
    }

    /// <summary>
    /// Set LED to Broke color (from bind or manual call).
    /// </summary>
    public void SetLEDToBrokeState(int index)
    {
        if (index < 0 || index >= leds.Length) return;
        SendLEDOnWithColor(index, leds[index].BrokeColor);
    }

    /// <summary>
    /// Set LED to Bait color (from bind or manual call).
    /// </summary>
    public void SetLEDToBaitState(int index)
    {
        if (index < 0 || index >= leds.Length) return;
        SendLEDOnWithColor(index, leds[index].BaitColor);
    }

    /// <summary>
    /// Restore LED to default/on color (repair or manual call).
    /// </summary>
    public void RestoreLEDColor(int index)
    {
        if (index < 0 || index >= leds.Length) return;
        SendLEDOnWithColor(index, leds[index].OnColor);
    }

    /// <summary>
    /// Set LED color, send ON command, update stored default for restore.
    /// </summary>
    public void SetLEDColor(int index, Color color)
    {
        if (index < 0 || index >= leds.Length) return;
        leds[index].OnColor = color;
        SendLEDOnWithColor(index, color);
    }

    /// <summary>
    /// Current configured on-color for LED (use with SetLEDColor for consistency).
    /// </summary>
    public Color GetLEDColor(int index)
    {
        if (index < 0 || index >= leds.Length)
            return Color.black;
        return leds[index].OnColor;
    }

    /// <summary>
    /// Send ON line: "ON pin r g b\n", pin selects LED, r/g/b 0-255.
    /// </summary>
    private void SendLEDOnWithColor(int index, Color color)
    {
        if (serial == null || !serial.IsOpen) return;

        int pin = leds[index].Pin;
        byte r = (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
        byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
        byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
        string line = $"ON {pin} {r} {g} {b}\n";

        try
        {
            serial.Write(line);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LEDManager] Write ON failed for LED {index} (pin {pin}): {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        foreach (var entry in workItemListeners)
        {
            if (entry.Item == null) continue;
            entry.Item.OnBroken.RemoveListener(entry.OnBroken);
            entry.Item.OnBaiting.RemoveListener(entry.OnBaiting);
            entry.Item.OnFixed.RemoveListener(entry.OnFixed);
            entry.Item.OnBaitingEnded.RemoveListener(entry.OnBaitingEnded);
        }
        workItemListeners.Clear();

        try
        {
            if (serial != null && serial.IsOpen)
                serial.Close();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LEDManager] Close port error: {ex.Message}");
        }
    }

    private struct WorkItemListener
    {
        public WorkItem Item;
        public UnityAction OnBroken;
        public UnityAction OnBaiting;
        public UnityAction OnFixed;
        public UnityAction OnBaitingEnded;

        public WorkItemListener(WorkItem item, UnityAction onBroken, UnityAction onBaiting, UnityAction onFixed, UnityAction onBaitingEnded)
        {
            Item = item;
            OnBroken = onBroken;
            OnBaiting = onBaiting;
            OnFixed = onFixed;
            OnBaitingEnded = onBaitingEnded;
        }
    }

    [Serializable]
    public class LEDConfig
    {
        [Header("Pin / pixel index")]
        [Tooltip("Arduino digital pin (5,6,7…) or NeoPixel index (0,1,2…); match firmware")]
        public int Pin;

        [Header("Colors")]
        [Tooltip("Default/on color: lit at start, restored on repair")]
        public Color OnColor = Color.green;

        [Tooltip("LED color when item is Broke")]
        public Color BrokeColor = new Color(1f, 0.2f, 0.2f, 1f);

        [Tooltip("LED color when item is in Bait")]
        public Color BaitColor = new Color(0.2f, 1f, 0.2f, 1f);

        [Header("WorkItem bind (optional)")]
        [Tooltip("If set, LED follows Broke/Bait/fix; else drive via SetLEDToBrokeState / SetLEDToBaitState / RestoreLEDColor")]
        public WorkItem WorkItem;
    }
}
