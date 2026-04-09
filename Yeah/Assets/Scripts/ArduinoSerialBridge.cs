using UnityEngine;
using UnityEngine.Events;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;

/// <summary>
/// Two-way serial bridge between Unity and the Arduino Uno  —  v3
///
/// ── Objects ───────────────────────────────────────────────────
///   1  Skull    (A0)  piezo + LED1
///   2  Phone    (A1)  piezo + microswitch + DFPlayer  <- special
///   3  Monitor  (A2)  piezo only
///   4  Speaker  (A4)  piezo only
///   5  Printer  (A5)  piezo + L298N motor
///
/// ── Arduino -> Unity  (piezo / phone hits) ─────────────────────
///   "HIT_1" ... "HIT_5"   fires onHit[0]...onHit[4] on the main thread.
///     HIT_2 specifically means the player struck the phone in
///     ANOMALY state — wire onHit[1] to your phone WorkItem's Fix method.
///   "PHONE_PICKUP"       fires onPhonePickup  (stop ringtone in Unity)
///   "PHONE_PUTDOWN"      fires onPhonePutdown (optional bookkeeping)
///
/// ── Unity -> Arduino  (Skull LED) ─────────────────────────────
///   Attach the Skull WorkItem to the single LEDBinding entry.
///     OnBroken       -> LED1:ANOMALY   (yellow)
///     OnBaiting      -> LED1:BAIT      (flashing red)
///     OnFixed        -> LED1:NORMAL    (red)
///     OnBaitingEnded -> LED1:NORMAL    (red)
///
/// ── Unity -> Arduino  (Phone state) ────────────────────────────
///   Attach the Phone WorkItem to phoneBinding.
///     OnBroken       -> PHONE:ANOMALY  (start ringing + anomaly flow)
///     OnBaiting      -> PHONE:BAIT     (start ringing + bait flow)
///     OnFixed        -> PHONE:NORMAL   (reset Arduino phone state)
///     OnBaitingEnded -> PHONE:NORMAL
///
/// ── Unity -> Arduino  (Printer motor) ──────────────────────────
///   Attach the Printer WorkItem to printerWorkItem.
///     OnBroken       -> PRINTER:ON
///     OnFixed        -> PRINTER:OFF
///
/// ── Setup ──────────────────────────────────────────────────────
///   1. Attach this script to any persistent GameObject.
///   2. Set portName to your Arduino's port.
///   3. Add one LEDBinding and drag in the Skull WorkItem.
///   4. Drag the Phone WorkItem into the Phone Binding slot.
///   5. Drag the Printer WorkItem into the Printer Binding slot.
///   6. Wire onHit[0]...onHit[4] to the Fix methods of each WorkItem.
///   7. Wire onPhonePickup to whatever stops the ringtone in Unity.
/// </summary>
public class ArduinoSerialBridge : MonoBehaviour
{
    // ── Configuration ─────────────────────────────────────────

    private const string DefaultPortName = "/dev/cu.usbserial-1130"; // Windows: "COM3"
    private const int    DefaultBaudRate = 9600;
    private const int    ReadTimeoutMs   = 100;
    private const int    WriteTimeoutMs  = 100;
    private const int    NumObjects      = 5;   // HIT_1 ... HIT_5

    // ── Inspector ─────────────────────────────────────────────

    [Header("Serial Settings")]
    [Tooltip("Arduino port. Windows: COM3   Mac: /dev/cu.usbserial-...")]
    [SerializeField] private string portName = DefaultPortName;
    [SerializeField] private int    baudRate = DefaultBaudRate;

    [Header("Piezo Hit Events  (index 0 = Skull ... index 4 = Printer)")]
    [Tooltip("Wire each slot to the Fix/Clear method of its WorkItem.\n" +
             "index 1 (Phone) fires when the player hits the phone in ANOMALY state.")]
    public UnityEvent[] onHit = new UnityEvent[NumObjects];

    [Header("Phone Events  (fired by incoming PHONE_PICKUP / PHONE_PUTDOWN)")]
    [Tooltip("Wire to whatever stops the ringtone in Unity.")]
    public UnityEvent onPhonePickup;
    [Tooltip("Optional — fires when the phone is placed back down.")]
    public UnityEvent onPhonePutdown;

    [Header("LED -> WorkItem Bindings  (Skull only in v3)")]
    [Tooltip("One entry: drag in the Skull WorkItem.")]
    [SerializeField] private LEDBinding[] ledBindings = Array.Empty<LEDBinding>();

    [Header("Phone -> WorkItem Binding")]
    [Tooltip("Drag in the Phone WorkItem. Its events drive PHONE: commands.")]
    [SerializeField] private WorkItem phoneWorkItem;

    [Header("Printer -> WorkItem Binding")]
    [Tooltip("Drag in the Printer WorkItem. OnBroken starts motor, OnFixed stops it.")]
    [SerializeField] private WorkItem printerWorkItem;

    [Header("Debug")]
    [Tooltip("勾选后：串口收到 PHONE_PICKUP / PHONE_PUTDOWN 时在 Console 打印一条日志（主线程 Invoke 前）。")]
    [SerializeField] private bool debugLogPhonePickupPutdown = true;

    // ── Private state ─────────────────────────────────────────

    private SerialPort _serial;
    private Thread     _readThread;
    private bool       _isRunning;

    // Flags set by background thread, consumed by main-thread Update()
    // 勿写成 readonly volatile：C# 不允许同字段兼具二者（CS0678）。
    private volatile bool[] _hitPending = new bool[NumObjects];
    private volatile bool            _phonePickupPending  = false;
    private volatile bool            _phonePutdownPending = false;

    // Listener reference lists for clean removal on destroy
    private readonly List<BoundListener> _boundListeners = new List<BoundListener>();
    private UnityAction _printerOnBroken, _printerOnFixed;
    private UnityAction _phoneOnBroken, _phoneOnBaiting, _phoneOnFixed, _phoneOnBaitingEnded;

    // ── Lifecycle ─────────────────────────────────────────────

    private void Start()
    {
        OpenSerial();
        if (_serial == null || !_serial.IsOpen) return;

        BindLEDs();
        BindPhone();
        BindPrinter();

        // Initialise hardware to a known state
        foreach (var binding in ledBindings)
            SendLED(binding.LedIndex, LedCommand.Normal);

        SendPhone(PhoneCommand.Normal);
        SendPrinter(false);
    }

    private void Update()
    {
        // All Unity API calls must happen on the main thread.
        // Background thread only sets volatile flags; Update() acts on them.

        for (int i = 0; i < NumObjects; i++)
        {
            if (!_hitPending[i]) continue;
            _hitPending[i] = false;
            onHit[i]?.Invoke();
        }

        if (_phonePickupPending)
        {
            _phonePickupPending = false;
            if (debugLogPhonePickupPutdown)
                Debug.Log("[ArduinoSerialBridge] 电话摘机 PHONE_PICKUP", this);
            onPhonePickup?.Invoke();
        }

        if (_phonePutdownPending)
        {
            _phonePutdownPending = false;
            if (debugLogPhonePickupPutdown)
                Debug.Log("[ArduinoSerialBridge] 电话挂机 PHONE_PUTDOWN", this);
            onPhonePutdown?.Invoke();
        }
    }

    private void OnDestroy()
    {
        UnbindLEDs();
        UnbindPhone();
        UnbindPrinter();

        _isRunning = false;
        _readThread?.Join(500);

        try
        {
            if (_serial != null && _serial.IsOpen)
                _serial.Close();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ArduinoSerialBridge] Port close error: {ex.Message}");
        }
    }

    // ── Serial setup ──────────────────────────────────────────

    private void OpenSerial()
    {
        if (string.IsNullOrEmpty(portName))
        {
            Debug.LogWarning("[ArduinoSerialBridge] No port configured.");
            return;
        }

        try
        {
            _serial = new SerialPort(portName, baudRate)
            {
                ReadTimeout  = ReadTimeoutMs,
                WriteTimeout = WriteTimeoutMs,
                NewLine      = "\n"
            };
            _serial.Open();

            _isRunning  = true;
            _readThread = new Thread(ReadLoop) { IsBackground = true };
            _readThread.Start();

            Debug.Log($"[ArduinoSerialBridge] Opened {portName} at {baudRate} baud.");
        }
        catch (Exception ex)
        {
            Debug.LogError(
                $"[ArduinoSerialBridge] Could not open '{portName}': {ex.Message}\n" +
                "Check the port name and close the Arduino Serial Monitor if it is open.");
        }
    }

    // ── LED binding (Skull) ───────────────────────────────────

    private void BindLEDs()
    {
        foreach (var binding in ledBindings)
        {
            if (binding.WorkItem == null) continue;

            int idx = binding.LedIndex;

            UnityAction onBroken       = () => SendLED(idx, LedCommand.Anomaly);
            UnityAction onBaiting      = () => SendLED(idx, LedCommand.Bait);
            UnityAction onFixed        = () => SendLED(idx, LedCommand.Normal);
            UnityAction onBaitingEnded = () => SendLED(idx, LedCommand.Normal);

            binding.WorkItem.OnBroken.AddListener(onBroken);
            binding.WorkItem.OnBaiting.AddListener(onBaiting);
            binding.WorkItem.OnFixed.AddListener(onFixed);
            binding.WorkItem.OnBaitingEnded.AddListener(onBaitingEnded);

            _boundListeners.Add(new BoundListener(
                binding.WorkItem, onBroken, onBaiting, onFixed, onBaitingEnded));
        }
    }

    private void UnbindLEDs()
    {
        foreach (var entry in _boundListeners)
        {
            if (entry.WorkItem == null) continue;
            entry.WorkItem.OnBroken.RemoveListener(entry.OnBroken);
            entry.WorkItem.OnBaiting.RemoveListener(entry.OnBaiting);
            entry.WorkItem.OnFixed.RemoveListener(entry.OnFixed);
            entry.WorkItem.OnBaitingEnded.RemoveListener(entry.OnBaitingEnded);
        }
        _boundListeners.Clear();
    }

    // ── Phone binding ─────────────────────────────────────────

    private void BindPhone()
    {
        if (phoneWorkItem == null) return;

        _phoneOnBroken       = () => SendPhone(PhoneCommand.Anomaly);
        _phoneOnBaiting      = () => SendPhone(PhoneCommand.Bait);
        _phoneOnFixed        = () => SendPhone(PhoneCommand.Normal);
        _phoneOnBaitingEnded = () => SendPhone(PhoneCommand.Normal);

        phoneWorkItem.OnBroken.AddListener(_phoneOnBroken);
        phoneWorkItem.OnBaiting.AddListener(_phoneOnBaiting);
        phoneWorkItem.OnFixed.AddListener(_phoneOnFixed);
        phoneWorkItem.OnBaitingEnded.AddListener(_phoneOnBaitingEnded);
    }

    private void UnbindPhone()
    {
        if (phoneWorkItem == null) return;
        if (_phoneOnBroken       != null) phoneWorkItem.OnBroken.RemoveListener(_phoneOnBroken);
        if (_phoneOnBaiting      != null) phoneWorkItem.OnBaiting.RemoveListener(_phoneOnBaiting);
        if (_phoneOnFixed        != null) phoneWorkItem.OnFixed.RemoveListener(_phoneOnFixed);
        if (_phoneOnBaitingEnded != null) phoneWorkItem.OnBaitingEnded.RemoveListener(_phoneOnBaitingEnded);
    }

    // ── Printer binding ───────────────────────────────────────

    private void BindPrinter()
    {
        if (printerWorkItem == null) return;

        _printerOnBroken = () => SendPrinter(true);
        _printerOnFixed  = () => SendPrinter(false);

        printerWorkItem.OnBroken.AddListener(_printerOnBroken);
        printerWorkItem.OnFixed.AddListener(_printerOnFixed);
    }

    private void UnbindPrinter()
    {
        if (printerWorkItem == null) return;
        if (_printerOnBroken != null) printerWorkItem.OnBroken.RemoveListener(_printerOnBroken);
        if (_printerOnFixed  != null) printerWorkItem.OnFixed.RemoveListener(_printerOnFixed);
    }

    // ── Public send API ───────────────────────────────────────

    /// <summary>Send an LED state command to the Arduino (Skull only in v3).</summary>
    public void SendLED(int ledIndex, string command)
    {
        SendRaw($"LED{ledIndex}:{command}");
    }

    /// <summary>Send a phone state command: PhoneCommand.Normal / Bait / Anomaly.</summary>
    public void SendPhone(string command)
    {
        SendRaw($"PHONE:{command}");
    }

    /// <summary>Send a printer motor command. true = ON, false = OFF.</summary>
    public void SendPrinter(bool on)
    {
        SendRaw(on ? "PRINTER:ON" : "PRINTER:OFF");
    }

    /// <summary>Send a system reset command to return all hardware components to their default state.</summary>
    public void ResetSystem()
    {
        SendRaw("SYSTEM:RESET");
    }

    private void SendRaw(string msg)
    {
        if (_serial == null || !_serial.IsOpen) return;
        try
        {
            _serial.WriteLine(msg);
            Debug.Log($"[ArduinoSerialBridge] Sent: {msg}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ArduinoSerialBridge] Write error: {ex.Message}");
        }
    }

    // ── Background read thread ────────────────────────────────

    private void ReadLoop()
    {
        while (_isRunning)
        {
            try
            {
                string line = _serial.ReadLine().Trim();

                // Piezo / phone anomaly hit
                if (line.StartsWith("HIT_") &&
                    int.TryParse(line.Substring(4), out int hitNumber))
                {
                    int arrayIndex = hitNumber - 1;   // HIT_1 -> index 0, HIT_2 -> index 1, ...
                    if (arrayIndex >= 0 && arrayIndex < NumObjects)
                        _hitPending[arrayIndex] = true;
                }
                // Phone picked up — Unity should stop the ringtone
                else if (line == "PHONE_PICKUP")
                {
                    _phonePickupPending = true;
                }
                // Phone put down — optional bookkeeping
                else if (line == "PHONE_PUTDOWN")
                {
                    _phonePutdownPending = true;
                }
            }
            catch (TimeoutException) { /* expected during idle */ }
            catch (Exception ex)
            {
                Debug.LogError($"[ArduinoSerialBridge] Read error: {ex.Message}");
                _isRunning = false;
            }
        }
    }

    // ── Command string constants ──────────────────────────────

    /// <summary>LED state strings matching the Arduino protocol.</summary>
    public static class LedCommand
    {
        public const string Normal  = "NORMAL";
        public const string Anomaly = "ANOMALY";
        public const string Bait    = "BAIT";
    }

    /// <summary>Phone state strings matching the Arduino protocol.</summary>
    public static class PhoneCommand
    {
        public const string Normal  = "NORMAL";
        public const string Bait    = "BAIT";
        public const string Anomaly = "ANOMALY";
    }

    // ── Data classes ──────────────────────────────────────────

    /// <summary>Binds one NeoPixel LED ring to a WorkItem (Skull in v3).</summary>
    [Serializable]
    public class LEDBinding
    {
        [Tooltip("1 = LED ring on pin 2 (Skull)")]
        public int LedIndex = 1;

        [Tooltip("WorkItem this LED should follow.")]
        public WorkItem WorkItem;
    }

    /// <summary>Stores listener delegates so they can be cleanly removed on destroy.</summary>
    private class BoundListener
    {
        public readonly WorkItem    WorkItem;
        public readonly UnityAction OnBroken;
        public readonly UnityAction OnBaiting;
        public readonly UnityAction OnFixed;
        public readonly UnityAction OnBaitingEnded;

        public BoundListener(WorkItem item,
                             UnityAction onBroken,
                             UnityAction onBaiting,
                             UnityAction onFixed,
                             UnityAction onBaitingEnded)
        {
            WorkItem       = item;
            OnBroken       = onBroken;
            OnBaiting      = onBaiting;
            OnFixed        = onFixed;
            OnBaitingEnded = onBaitingEnded;
        }
    }
}
