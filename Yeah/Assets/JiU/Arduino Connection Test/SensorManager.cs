using UnityEngine;
using System;
using System.IO.Ports;
using System.Collections.Generic;

/// <summary>
/// Multiple piezo sensors over serial: values above threshold map to virtual keys
/// polled via GetKeyDown(KeyCode).
/// </summary>
public class SensorManager : MonoBehaviour
{
    [Header("Sensor setup")]
    [Tooltip("Per sensor: port, baud, threshold, mapped key")]
    [SerializeField] private SensorConfig[] sensors = Array.Empty<SensorConfig>();

    // Virtual keys fired this frame when sensor exceeded threshold (GetKeyDown-style)
    private HashSet<KeyCode> keysDownThisFrame = new HashSet<KeyCode>();

    private void Update()
    {
        keysDownThisFrame.Clear();

        for (int i = 0; i < sensors.Length; i++)
        {
            var config = sensors[i];
            if (config.Serial == null || !config.Serial.IsOpen)
                continue;

            if (config.Serial.BytesToRead <= 0)
                continue;

            try
            {
                string line = config.Serial.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                line = line.Trim();
                if (!int.TryParse(line, out int value))
                    continue;

                if (value > config.Threshold)
                    keysDownThisFrame.Add(config.MappedKey);
            }
            catch (TimeoutException) { /* ignore timeout */ }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SensorManager] Sensor {i} read error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// True if this key was "pressed" this frame (sensor over threshold), similar to Input.GetKeyDown.
    /// </summary>
    public bool GetKeyDown(KeyCode key)
    {
        return keysDownThisFrame.Contains(key);
    }

    /// <summary>
    /// True if any mapped key fired this frame.
    /// </summary>
    public bool AnyKeyDown()
    {
        return keysDownThisFrame.Count > 0;
    }

    private void Start()
    {
        foreach (var config in sensors)
        {
            if (string.IsNullOrEmpty(config.PortName))
                continue;
            try
            {
                config.Serial = new SerialPort(config.PortName, config.BaudRate);
                config.Serial.ReadTimeout = 10;
                config.Serial.Open();
                Debug.Log($"[SensorManager] Opened sensor port: {config.PortName}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SensorManager] Failed to open {config.PortName}: {ex.Message}\n" +
                    "Check: 1) Correct port (Device Manager / Arduino IDE); 2) Serial Monitor closed; 3) Port not in use by another app.");
            }
        }
    }

    private void OnDestroy()
    {
        foreach (var config in sensors)
        {
            try
            {
                if (config.Serial != null && config.Serial.IsOpen)
                    config.Serial.Close();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SensorManager] Close port error: {ex.Message}");
            }
        }
    }

    [Serializable]
    public class SensorConfig
    {
        [Header("Serial")]
        [Tooltip("e.g. COM3 (Windows) or /dev/ttyUSB0 (Mac/Linux)")]
        public string PortName = "COM3";

        [Tooltip("Match Arduino Serial Monitor, often 9600")]
        public int BaudRate = 9600;

        [Header("Trigger")]
        [Tooltip("Fire mapped key when received value exceeds this")]
        public int Threshold = 512;

        [Tooltip("Virtual key to fire; poll with SensorManager.GetKeyDown(this key)")]
        public KeyCode MappedKey = KeyCode.Space;

        [NonSerialized] public SerialPort Serial;
    }
}
