using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>Phone WorkItem interaction mode. Non-phone items ignore this.</summary>
public enum PhoneBehaviorVersion
{
    /// <summary>Like other WorkItems; off-hook during Broke/Bait re-sends PHONE:* (DFPlayer firmware).</summary>
    Version1_StandardWithPickupAudio = 0,
    /// <summary>Correct repair only after first pickup in this Broke/Bait episode.</summary>
    Version2_FirstPickupInteractionGate = 1,
    /// <summary>Legacy: on-hook wrong repair, phone Bait flow, Boss blocks phone rolls.</summary>
    Version3_LegacyHookAndBaitRules = 2,
}

[RequireComponent(typeof(Collider))]
public class WorkItem : MonoBehaviour
{
    [Header("Failure Timing (seconds)")]
    public float minTimeToBreak = 4f;
    public float maxTimeToBreak = 10f;

    [Tooltip("When off, BreakLoop will not auto enter Broke/Bait (tutorial or script control)")]
    public bool enableAutoBreak = true;

    [Header("Broke / Bait roll weights")]
    [Tooltip("Weight for Broke on next failure roll; combined with baitWeight for odds (e.g. 8 and 2 ≈ 80% Broke, 20% Bait)")]
    [Min(0f)]
    public float breakWeight = 5f;
    [Tooltip("Weight for Bait on next failure roll")]
    [Min(0f)]
    public float baitWeight = 1f;

    [Tooltip("Seconds before non-phone Bait auto-resolves (Version 3 phone Bait uses pickup flow instead).")]
    [Min(0.01f)]
    public float baitSelfFixDurationSeconds = 3f;

    [Header("Hotkey Repair (Input System)")]
    [Tooltip("When off: this object's Input System binding and key polling never call TryRepair(); Uduino / Inspector can still invoke TryRepair")]
    public bool enableHotkeyRepair = true;

    [Tooltip("Examples: <Keyboard>/1  <Keyboard>/2  <Keyboard>/numpad1  <Keyboard>/q")]
    public string repairBindingPath = "<Keyboard>/1";
    [Tooltip("Matches binding above; fallback poll for Uduino/simulated keys. With UduinoPinToKeyTrigger, point its direct-repair target at this object")]
    public KeyCode repairKeyCodeFallback = KeyCode.Alpha1;

    [Header("Optional Distance Requirement")]
    public bool requirePlayerInRange = false;
    public float interactRange = 2.0f;
    public Transform player; // If unset, auto-find Tag=Player

    [Header("Colors")]
    public Color brokenColor = new Color(1f, 0.2f, 0.2f, 1f);
    public Color baitColor = new Color(0.2f, 1f, 0.2f, 1f);

    [Header("Visual sync with logic")]
    [Tooltip("When on: each frame in Broke/Bait rewrites MaterialPropertyBlock so other scripts/Animator/material instancing cannot override tint (looks fixed but logic still broken).")]
    public bool keepVisualSyncedWithLogic = true;

    [Header("Uduino / external outputs (optional)")]
    [Tooltip("Invoked on failure; wire to Uduino outputs etc.")]
    public UnityEvent OnBroken;
    [Tooltip("I'm scared")] 
    public UnityEvent OnBaiting;
    [Tooltip("When returning to normal: player fixed Broke (Fix), or Bait timer ended naturally (OnBaitingEnded fires same frame, slightly earlier); materials / LED / stop broken SFX")]
    public UnityEvent OnFixed;
    [Tooltip("When Bait expires without player hit; LED restore etc.")]
    public UnityEvent OnBaitingEnded;

    [Tooltip("When hit counts as correct repair (before Fix); wire correct-repair SFX on PlaySoundOnEventAudioManager")]
    public UnityEvent OnRepairCorrect;
    [Tooltip("When hit counts as wrong (spam on Bait or idle wrong hit); wire wrong-repair SFX")]
    public UnityEvent OnRepairIncorrect;

    [Header("Debug")]
    public bool debugLogs = false;
    [Tooltip("Log to Console when TryRepair counts as wrong (Bait/idle/phone on-hook); shows itemName or object name")]
    public bool debugLogWrongRepair = false;
    [Tooltip("Log when Break() or Bait() returns without doing anything (e.g. still Baiting → Break skipped). 用于查教程/电话 Bait 与 Broke 竞态。")]
    public bool debugLogBreakBaitSkips = false;
    public KeyCode debugBreakKeyOldInput = KeyCode.None; // Legacy input unused (leave None)
    public string debugBreakBindingPath = "<Keyboard>/b"; // New Input: B forces break (debug)

    public bool IsBroken { get; private set; } = false;
    public bool IsBaiting {get; private set;} = false;

    /// <summary>True when this object is configured as the phone WorkItem.</summary>
    public bool IsPhoneWorkItem =>
        treatAsPhoneWorkItem
        || (!string.IsNullOrWhiteSpace(itemName) && itemName.Trim().Equals("phone", System.StringComparison.OrdinalIgnoreCase));

    /// <summary>Version 3 only: legacy hook/Bait phone rules (same as pre-version-selector behavior).</summary>
    public bool PhoneBaitRulesActive =>
        IsPhoneWorkItem
        && phoneBehaviorVersion == PhoneBehaviorVersion.Version3_LegacyHookAndBaitRules
        && (usePhoneBaitBehavior
            || (!string.IsNullOrWhiteSpace(itemName) && itemName.Trim().Equals("phone", System.StringComparison.OrdinalIgnoreCase)));

    /// <summary>For GameManager: whether phone-style Bait should decay performance score per second.</summary>
    public bool ShouldApplyPhoneBaitPerformanceDecay => IsBaiting && PhoneBaitRulesActive;

    /// <summary>For audio: under V3 phone rules, whether handset is on cradle (not off-hook).</summary>
    public bool PhoneIsOnCradleForSfx => PhoneBaitRulesActive && !_phonePhysicallyOffHook;

    /// <summary>V2/V3: off-hook suppresses Unity broken/Bait loop SFX on pickup. V1 does not.</summary>
    public bool PhoneSuppressesUnitySfxOnPickup =>
        IsPhoneWorkItem
        && phoneBehaviorVersion != PhoneBehaviorVersion.Version1_StandardWithPickupAudio;

    /// <summary>Handset off-hook (from PHONE_PICKUP / NotifyPhonePickedUp).</summary>
    public bool PhonePhysicallyOffHook => _phonePhysicallyOffHook;

    /// <summary>V2: first pickup in current Broke/Bait episode unlocked interaction.</summary>
    public bool PhoneInteractionUnlocked => _phoneInteractionUnlocked;

    private Renderer[] allRenderers;
    private MaterialPropertyBlock mpb;

    private InputAction repairAction;
    private InputAction debugBreakAction;
    bool _lastEnableHotkeyRepair;
    bool _warnedNoSupportedColorProperty;

    Coroutine _baitSelfFixCoroutine;
    Coroutine _phoneBaitAfterPickupCoroutine;
    bool _phoneLiftedDuringCurrentBait;

    /// <summary>Maintained by pickup/hang-up events: true when handset is considered off-hook.</summary>
    bool _phonePhysicallyOffHook;

    /// <summary>V2: true after first pickup during current Broke/Bait; cleared on Fix / Bait resolve / new Broke/Bait.</summary>
    bool _phoneInteractionUnlocked;

    /// <summary>Ignore TryRepair briefly after hang-up (reduce piezo vibration false triggers).</summary>
    float _phoneTryRepairSuppressedUntilTime;

    /// <summary>Had off-hook while still Broke (broken loop stopped by pickup logic); on hang-up while still broken, resume loop.</summary>
    bool _hadPhonePickupWhileBroken;

    //this doesn't really have a point but it's fun lol
    public GameObject smokeParticles;

    public string itemName;

    [Header("Phone")]
    [Tooltip("Force phone behavior even if itemName is not \"phone\".")]
    public bool treatAsPhoneWorkItem;

    [Tooltip("Version 1 = normal WorkItem + Arduino audio on pickup while Broke/Bait. Version 2 = must pick up once before correct repair. Version 3 = legacy hook/Bait rules.")]
    public PhoneBehaviorVersion phoneBehaviorVersion = PhoneBehaviorVersion.Version3_LegacyHookAndBaitRules;

    [Header("Phone · Version 3 only")]
    [Tooltip("When on (V3): no 3s self-fix; without off-hook, Bait persists. End: off-hook then hang-up, or wait phoneBaitAutoResolveSecondsAfterPickup after pickup.")]
    public bool usePhoneBaitBehavior;

    [Tooltip("Seconds after off-hook to auto-resolve Bait; 0 = only hang-up after pickup ends it (holding without hang-up keeps Bait).")]
    [Min(0f)]
    public float phoneBaitAutoResolveSecondsAfterPickup = 8f;

    [Tooltip("Seconds after hang-up to ignore TryRepair (no punish, no fix); reduces piezo false triggers when setting phone down.")]
    [Min(0f)]
    public float phonePutDownTryRepairDebounceSeconds = 0.5f;

    public GameObject tutorialBox;
    public bool isLast;
    public GameObject finalBox;
    private static readonly int[] ColorPropIds =
    {
        Shader.PropertyToID("_BaseColor"),
        Shader.PropertyToID("_Color"),
        Shader.PropertyToID("_TintColor"),
        Shader.PropertyToID("_UnlitColor"),
        Shader.PropertyToID("_MainColor"),
    };

    void Awake()
    {
        allRenderers = GetComponentsInChildren<Renderer>(true);
        mpb = new MaterialPropertyBlock();

        repairAction = new InputAction(
            name: $"{gameObject.name}_FixHotkey",
            type: InputActionType.Button,
            binding: repairBindingPath
        );

        debugBreakAction = new InputAction(
            name: $"{gameObject.name}_DebugBreak",
            type: InputActionType.Button,
            binding: debugBreakBindingPath
        );
    }

    void OnEnable()
    {
        _lastEnableHotkeyRepair = enableHotkeyRepair;
        ConfigureHotkeyRepairInput();

        debugBreakAction.Enable();
        debugBreakAction.performed += OnDebugBreakPerformed;
    }

    void OnDisable()
    {
        StopAllBaitCoroutines();

        if (repairAction != null)
        {
            repairAction.performed -= OnRepairPerformed;
            repairAction.Disable();
        }

        debugBreakAction.performed -= OnDebugBreakPerformed;
        debugBreakAction.Disable();
    }

    void ConfigureHotkeyRepairInput()
    {
        if (repairAction == null) return;

        repairAction.performed -= OnRepairPerformed;

        if (enableHotkeyRepair)
        {
            repairAction.performed += OnRepairPerformed;
            repairAction.Enable();
        }
        else
            repairAction.Disable();
    }

    void Update()
    {
        if (enableHotkeyRepair != _lastEnableHotkeyRepair)
        {
            _lastEnableHotkeyRepair = enableHotkeyRepair;
            if (isActiveAndEnabled)
                ConfigureHotkeyRepairInput();
        }

        // Fallback: Uduino/simulated keys may not fire InputAction.performed; poll keyboard in Broke/Bait when enableHotkeyRepair
        if (!enableHotkeyRepair)
            return;

        if ((IsBroken || IsBaiting) && repairKeyCodeFallback != KeyCode.None && UnityEngine.InputSystem.Keyboard.current != null)
        {
            var key = KeyCodeToKey(repairKeyCodeFallback);
            if (key != UnityEngine.InputSystem.Key.None && UnityEngine.InputSystem.Keyboard.current[key].wasPressedThisFrame)
                TryRepair();
        }
    }

    void LateUpdate()
    {
        if (!keepVisualSyncedWithLogic)
            return;

        if (IsBroken)
            ApplyTintOverride(brokenColor);
        else if (IsBaiting)
            ApplyTintOverride(baitColor);
    }

    static UnityEngine.InputSystem.Key KeyCodeToKey(KeyCode kc)
    {
        switch (kc)
        {
            case KeyCode.Alpha0: return UnityEngine.InputSystem.Key.Digit0;
            case KeyCode.Alpha1: return UnityEngine.InputSystem.Key.Digit1;
            case KeyCode.Alpha2: return UnityEngine.InputSystem.Key.Digit2;
            case KeyCode.Alpha3: return UnityEngine.InputSystem.Key.Digit3;
            case KeyCode.Alpha4: return UnityEngine.InputSystem.Key.Digit4;
            case KeyCode.Alpha5: return UnityEngine.InputSystem.Key.Digit5;
            case KeyCode.Alpha6: return UnityEngine.InputSystem.Key.Digit6;
            case KeyCode.Alpha7: return UnityEngine.InputSystem.Key.Digit7;
            case KeyCode.Alpha8: return UnityEngine.InputSystem.Key.Digit8;
            case KeyCode.Alpha9: return UnityEngine.InputSystem.Key.Digit9;
            case KeyCode.Space: return UnityEngine.InputSystem.Key.Space;
            case KeyCode.Return: return UnityEngine.InputSystem.Key.Enter;
            case KeyCode.Escape: return UnityEngine.InputSystem.Key.Escape;
            case KeyCode.A: return UnityEngine.InputSystem.Key.A;
            case KeyCode.B: return UnityEngine.InputSystem.Key.B;
            case KeyCode.C: return UnityEngine.InputSystem.Key.C;
            case KeyCode.D: return UnityEngine.InputSystem.Key.D;
            case KeyCode.E: return UnityEngine.InputSystem.Key.E;
            case KeyCode.F: return UnityEngine.InputSystem.Key.F;
            case KeyCode.G: return UnityEngine.InputSystem.Key.G;
            case KeyCode.H: return UnityEngine.InputSystem.Key.H;
            case KeyCode.I: return UnityEngine.InputSystem.Key.I;
            case KeyCode.J: return UnityEngine.InputSystem.Key.J;
            case KeyCode.K: return UnityEngine.InputSystem.Key.K;
            case KeyCode.L: return UnityEngine.InputSystem.Key.L;
            case KeyCode.M: return UnityEngine.InputSystem.Key.M;
            case KeyCode.N: return UnityEngine.InputSystem.Key.N;
            case KeyCode.O: return UnityEngine.InputSystem.Key.O;
            case KeyCode.P: return UnityEngine.InputSystem.Key.P;
            case KeyCode.Q: return UnityEngine.InputSystem.Key.Q;
            case KeyCode.R: return UnityEngine.InputSystem.Key.R;
            case KeyCode.S: return UnityEngine.InputSystem.Key.S;
            case KeyCode.T: return UnityEngine.InputSystem.Key.T;
            case KeyCode.U: return UnityEngine.InputSystem.Key.U;
            case KeyCode.V: return UnityEngine.InputSystem.Key.V;
            case KeyCode.W: return UnityEngine.InputSystem.Key.W;
            case KeyCode.X: return UnityEngine.InputSystem.Key.X;
            case KeyCode.Y: return UnityEngine.InputSystem.Key.Y;
            case KeyCode.Z: return UnityEngine.InputSystem.Key.Z;
            default: return UnityEngine.InputSystem.Key.None;
        }
    }

    void Start()
    {
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }

        if (GameManager.Instance != null)
            GameManager.Instance.RegisterItem(this);

        ClearTintOverride();
        //StartCoroutine(BaitLoop());
        StartCoroutine(BreakLoop());

        if (debugLogs)
            Debug.Log($"[WorkItem] {name} renderers found: {allRenderers.Length}");
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.UnregisterItem(this);

        StopAllBaitCoroutines();

        repairAction?.Dispose();
        debugBreakAction?.Dispose();
    }

    void StopAllBaitCoroutines()
    {
        if (_baitSelfFixCoroutine != null)
        {
            StopCoroutine(_baitSelfFixCoroutine);
            _baitSelfFixCoroutine = null;
        }

        if (_phoneBaitAfterPickupCoroutine != null)
        {
            StopCoroutine(_phoneBaitAfterPickupCoroutine);
            _phoneBaitAfterPickupCoroutine = null;
        }
    }

    /// <summary>Call from GameManager / Arduino <c>onPhonePickup</c>.</summary>
    public void NotifyPhonePickedUp()
    {
        if (!IsPhoneWorkItem)
            return;

        bool wasOffHook = _phonePhysicallyOffHook;
        _phonePhysicallyOffHook = true;

        switch (phoneBehaviorVersion)
        {
            case PhoneBehaviorVersion.Version1_StandardWithPickupAudio:
                if (IsBroken)
                    _hadPhonePickupWhileBroken = true;
                if ((IsBroken || IsBaiting) && !wasOffHook)
                    TrySendPhoneHardwareAudioForCurrentState();
                break;

            case PhoneBehaviorVersion.Version2_FirstPickupInteractionGate:
                if (IsBroken || IsBaiting)
                {
                    if (!_phoneInteractionUnlocked)
                        _phoneInteractionUnlocked = true;
                    if (IsBroken)
                        _hadPhonePickupWhileBroken = true;
                }
                break;

            case PhoneBehaviorVersion.Version3_LegacyHookAndBaitRules:
                NotifyPhonePickedUp_Version3();
                break;
        }
    }

    /// <summary>Call from GameManager / Arduino <c>onPhonePutdown</c>.</summary>
    public void NotifyPhonePutDown()
    {
        if (!IsPhoneWorkItem)
            return;

        _phonePhysicallyOffHook = false;

        switch (phoneBehaviorVersion)
        {
            case PhoneBehaviorVersion.Version1_StandardWithPickupAudio:
                break;

            case PhoneBehaviorVersion.Version2_FirstPickupInteractionGate:
            case PhoneBehaviorVersion.Version3_LegacyHookAndBaitRules:
                NotifyPhonePutDown_Version2And3();
                break;
        }
    }

    /// <summary>Legacy entry; prefer <see cref="NotifyPhonePickedUp"/>.</summary>
    public void NotifyPhonePickedUpForBaitFlow() => NotifyPhonePickedUp();

    /// <summary>Legacy entry; prefer <see cref="NotifyPhonePutDown"/>.</summary>
    public void NotifyPhonePutDownForBaitFlow() => NotifyPhonePutDown();

    void NotifyPhonePickedUp_Version3()
    {
        if (!PhoneBaitRulesActive)
            return;

        if (IsBroken)
            _hadPhonePickupWhileBroken = true;

        if (!IsBaiting)
            return;
        if (_phoneLiftedDuringCurrentBait)
            return;

        _phoneLiftedDuringCurrentBait = true;

        if (_phoneBaitAfterPickupCoroutine != null)
        {
            StopCoroutine(_phoneBaitAfterPickupCoroutine);
            _phoneBaitAfterPickupCoroutine = null;
        }

        if (phoneBaitAutoResolveSecondsAfterPickup > 0f)
            _phoneBaitAfterPickupCoroutine = StartCoroutine(PhoneBaitAutoResolveAfterPickupRoutine());
    }

    void NotifyPhonePutDown_Version2And3()
    {
        if (phoneBehaviorVersion == PhoneBehaviorVersion.Version3_LegacyHookAndBaitRules && !PhoneBaitRulesActive)
            return;

        _phoneTryRepairSuppressedUntilTime = Time.time + Mathf.Max(0f, phonePutDownTryRepairDebounceSeconds);

        if (phoneBehaviorVersion == PhoneBehaviorVersion.Version3_LegacyHookAndBaitRules
            && IsBaiting && _phoneLiftedDuringCurrentBait)
        {
            ResolveBaitLikeSelfFix();
        }

        if (IsBroken && _hadPhonePickupWhileBroken)
        {
            _hadPhonePickupWhileBroken = false;
            JiU.PlaySoundOnEventAudioManager.ResumeBrokenLoopAfterPhonePutdownForWorkItem(this);
            JiU.PlaySoundOnEvent.ResumeBrokenClipAfterPhonePutdownForWorkItem(this);
        }
    }

    void TrySendPhoneHardwareAudioForCurrentState()
    {
        if (GameManager.Instance == null) return;
        GameManager.Instance.SendPhoneAudioToArduino(IsBaiting);
    }

    void ResetPhoneEpisodeStateForNewBrokeOrBait()
    {
        if (!IsPhoneWorkItem)
            return;
        _phoneInteractionUnlocked = false;
        if (phoneBehaviorVersion == PhoneBehaviorVersion.Version3_LegacyHookAndBaitRules)
            _phonePhysicallyOffHook = false;
    }

    void ClearPhoneEpisodeStateOnResolved()
    {
        if (!IsPhoneWorkItem)
            return;
        _phoneInteractionUnlocked = false;
    }

    /// <summary>Phone items: skip random Broke/Bait rolls while Boss warning is active or Boss is present (including fallback branches that would become Bait).</summary>
    bool ShouldBlockPhoneRandomBrokeOrBaitForBoss()
    {
        if (!PhoneBaitRulesActive || GameManager.Instance == null)
            return false;
        GameManager gm = GameManager.Instance;
        return gm.BossWarning || gm.BossIsHere;
    }

    /*private IEnumerator BaitLoop()
    {
        while (true)
        {
            if (!IsBaiting)
            {
                float t = Random.Range(minTimeToBreak, maxTimeToBreak);
                yield return new WaitForSeconds(t);
                
                if (GameManager.FreezeFailures)
                    continue;
                
                Bait();
            }
        }
    }*/
    
    private IEnumerator BreakLoop()
    {
        while (true)
        {
            if (!IsBroken && !IsBaiting)
            {
                if (!enableAutoBreak)
                {
                    yield return null;
                    continue;
                }

                float minT = minTimeToBreak;
                float maxT = maxTimeToBreak;
                if (GameManager.Instance != null && GameManager.Instance.UsePhaseBreakTiming())
                {
                    minT = GameManager.Instance.GetActiveBreakIntervalMin();
                    maxT = GameManager.Instance.GetActiveBreakIntervalMax();
                }

                float t = Random.Range(minT, maxT);
                yield return new WaitForSeconds(t);

                if (GameManager.FreezeFailures)
                    continue;

                float total = breakWeight + baitWeight;
                if (total <= 0f)
                {
                    if (Random.value < 0.5f)
                    {
                        if (GameManager.Instance != null && GameManager.Instance.BlockNewHackEventsNow())
                        {
                            if (baitWeight > 0f && !ShouldBlockPhoneRandomBrokeOrBaitForBoss())
                                Bait();
                            else
                                continue;
                        }
                        else if (GameManager.Instance != null && GameManager.Instance.BlockNewBrokeDuringBossStay())
                        {
                            if (baitWeight > 0f && !ShouldBlockPhoneRandomBrokeOrBaitForBoss())
                                Bait();
                            else
                                continue;
                        }
                        else if (GameManager.Instance != null && !GameManager.Instance.CanStartNewBrokeState())
                            continue;
                        else if (ShouldBlockPhoneRandomBrokeOrBaitForBoss())
                            continue;
                        else
                            Break();
                    }
                    else
                    {
                        if (ShouldBlockPhoneRandomBrokeOrBaitForBoss())
                            continue;
                        Bait();
                    }
                }
                else
                {
                    float roll = Random.Range(0f, total);
                    if (roll < breakWeight)
                    {
                        if (GameManager.Instance != null && GameManager.Instance.BlockNewHackEventsNow())
                        {
                            if (baitWeight > 0f && !ShouldBlockPhoneRandomBrokeOrBaitForBoss())
                                Bait();
                            else
                                continue;
                        }
                        else if (GameManager.Instance != null && GameManager.Instance.BlockNewBrokeDuringBossStay())
                        {
                            if (baitWeight > 0f && !ShouldBlockPhoneRandomBrokeOrBaitForBoss())
                                Bait();
                            else
                                continue;
                        }
                        else if (GameManager.Instance != null && !GameManager.Instance.CanStartNewBrokeState())
                            continue;
                        else if (ShouldBlockPhoneRandomBrokeOrBaitForBoss())
                            continue;
                        else
                            Break();
                    }
                    else
                    {
                        if (ShouldBlockPhoneRandomBrokeOrBaitForBoss())
                            continue;
                        Bait();
                    }
                }
            }
            else
            {
                yield return null;
            }
        }
    }

    /// <summary>Try repair (hotkey or Uduino). Call from UduinoPinToKeyTrigger onTriggered or direct-repair target in Inspector.</summary>
    public void TryRepair()
    {
        //Debug.Log($"I am trying to fix {this.itemName}!");

        if (IsPhoneWorkItem && TryHandlePhoneTryRepairBeforeGeneric())
            return;

        // Only real Broke after range checks does win + Fix; Bait / idle spam only lose and return
        if (!IsBroken)
        {
            if (IsBaiting)
            {
                LogWrongRepairTry("bait_wrong_hit");
                if (GameManager.Instance != null)
                    GameManager.Instance.UltraPunishment();
                // Punishment may game-over this frame; invoking again may run Inspector hooks that hide the fail panel
                if (GameManager.Instance == null || !GameManager.Instance.IsGameOver)
                    OnRepairIncorrect?.Invoke();
                return;
            }

            LogWrongRepairTry("idle_wrong_hit");
            if (GameManager.Instance != null)
                GameManager.Instance.Punishment();
            if (GameManager.Instance == null || !GameManager.Instance.IsGameOver)
                OnRepairIncorrect?.Invoke();
            return;
        }

        if (requirePlayerInRange)
        {
            if (player == null) return;
            if (Vector3.Distance(player.position, transform.position) > interactRange) return;
        }

        OnRepairCorrect?.Invoke();
        Fix();

        if (GameManager.Instance != null)
        {
            string label = string.IsNullOrWhiteSpace(itemName) ? name : itemName.Trim();
            GameManager.Instance.DebugLogPerformanceAfterSuccessfulRepair(label);
        }
    }

    /// <returns>True if TryRepair should stop (handled or debounced).</returns>
    bool TryHandlePhoneTryRepairBeforeGeneric()
    {
        if (phoneBehaviorVersion == PhoneBehaviorVersion.Version2_FirstPickupInteractionGate)
        {
            if (Time.time < _phoneTryRepairSuppressedUntilTime)
                return true;

            if ((IsBroken || IsBaiting) && !_phoneInteractionUnlocked)
            {
                ApplyPhoneTryRepairWhileLockedPunishment();
                return true;
            }

            return false;
        }

        if (phoneBehaviorVersion == PhoneBehaviorVersion.Version3_LegacyHookAndBaitRules && PhoneBaitRulesActive)
        {
            if (Time.time < _phoneTryRepairSuppressedUntilTime)
                return true;

            if (!_phonePhysicallyOffHook)
            {
                ApplyPhoneTryRepairWhileOnHookPunishment();
                return true;
            }
        }

        return false;
    }

    void ApplyPhoneTryRepairWhileLockedPunishment()
    {
        if (IsBaiting)
        {
            LogWrongRepairTry("phone_v2_locked_bait");
            if (GameManager.Instance != null)
                GameManager.Instance.UltraPunishment();
            if (GameManager.Instance == null || !GameManager.Instance.IsGameOver)
                OnRepairIncorrect?.Invoke();
            return;
        }

        if (IsBroken)
        {
            LogWrongRepairTry("phone_v2_locked_broke");
            if (GameManager.Instance != null)
                GameManager.Instance.Punishment();
            if (GameManager.Instance == null || !GameManager.Instance.IsGameOver)
                OnRepairIncorrect?.Invoke();
            return;
        }

        LogWrongRepairTry("phone_v2_locked_idle");
        if (GameManager.Instance != null)
            GameManager.Instance.Punishment();
        if (GameManager.Instance == null || !GameManager.Instance.IsGameOver)
            OnRepairIncorrect?.Invoke();
    }

    void ApplyPhoneTryRepairWhileOnHookPunishment()
    {
        if (IsBaiting)
        {
            LogWrongRepairTry("phone_on_hook_bait");
            if (GameManager.Instance != null)
                GameManager.Instance.UltraPunishment();
            if (GameManager.Instance == null || !GameManager.Instance.IsGameOver)
                OnRepairIncorrect?.Invoke();
            return;
        }

        LogWrongRepairTry("phone_on_hook_idle");
        if (GameManager.Instance != null)
            GameManager.Instance.Punishment();
        if (GameManager.Instance == null || !GameManager.Instance.IsGameOver)
            OnRepairIncorrect?.Invoke();
    }

    void LogWrongRepairTry(string reason)
    {
        if (!debugLogWrongRepair)
            return;
        string label = string.IsNullOrWhiteSpace(itemName) ? name : itemName.Trim();
        string state = IsBaiting ? "Bait" : IsBroken ? "Broke" : "Normal";
        Debug.Log($"[WorkItem] Wrong TryRepair | item=\"{label}\" obj=\"{gameObject.name}\" state={state} reason={reason}", this);
    }

    private void OnRepairPerformed(InputAction.CallbackContext ctx)
    {
        TryRepair();
    }

    private void OnDebugBreakPerformed(InputAction.CallbackContext ctx)
    {
        Break();
    }

    public void Break()
    {
        if (IsBroken || IsBaiting)
        {
            if (debugLogBreakBaitSkips)
            {
                string label = string.IsNullOrWhiteSpace(itemName) ? name : itemName.Trim();
                Debug.LogWarning(
                    $"[WorkItem] Break() SKIPPED | item=\"{label}\" go=\"{gameObject.name}\" IsBroken={IsBroken} IsBaiting={IsBaiting} " +
                    $"(若 IsBaiting 为 true，可能仍在教程 ForceBait 中，OnBroken/串口 ANOMALY 不会发)",
                    this);
            }
            return;
        }
        _hadPhonePickupWhileBroken = false;
        ResetPhoneEpisodeStateForNewBrokeOrBait();
        IsBroken = true;

        if (GameManager.Instance != null)
        {
            if (GameManager.Instance.isTutorialing == true)
            {
                if(tutorialBox != null)
                    tutorialBox.SetActive(true);
            }
            GameManager.Instance.OnWorkItemEnteredHackedState(this);
            GameManager.Instance.ApplyWorkPressureOnItemBroke();
        }

        WarnIfTintDidNotApply(ApplyTintOverride(brokenColor), "Broke");
        OnBroken?.Invoke();

        if (debugLogs)
            Debug.Log($"[WorkItem] {name} BROKE -> tint applied");
    }

    public void Bait()
    {
        if (IsBaiting || IsBroken)
        {
            if (debugLogBreakBaitSkips)
            {
                string label = string.IsNullOrWhiteSpace(itemName) ? name : itemName.Trim();
                Debug.LogWarning(
                    $"[WorkItem] Bait() SKIPPED | item=\"{label}\" go=\"{gameObject.name}\" IsBaiting={IsBaiting} IsBroken={IsBroken}",
                    this);
            }
            return;
        }
        IsBaiting = true;
        _phoneLiftedDuringCurrentBait = false;
        ResetPhoneEpisodeStateForNewBrokeOrBait();
        StopAllBaitCoroutines();

        WarnIfTintDidNotApply(ApplyTintOverride(baitColor), "Bait");
        OnBaiting?.Invoke();

        if (debugLogs)
            Debug.Log($"[WorkItem] {name} BAIT -> tint applied");

        if (PhoneBaitRulesActive)
        {
            // Incoming Bait assumes on-hook (do not inherit previous call off-hook state)
            _phonePhysicallyOffHook = false;
            // No 3s self-fix; wait for pickup+hang-up or post-pickup timer
        }
        else
            _baitSelfFixCoroutine = StartCoroutine(BaitSelfFix());
    }

    IEnumerator BaitSelfFix()
    {
        yield return new WaitForSeconds(Mathf.Max(0.01f, baitSelfFixDurationSeconds));
        _baitSelfFixCoroutine = null;
        if (!IsBaiting)
            yield break;

        ResolveBaitLikeSelfFix();
    }

    IEnumerator PhoneBaitAutoResolveAfterPickupRoutine()
    {
        yield return new WaitForSeconds(phoneBaitAutoResolveSecondsAfterPickup);
        _phoneBaitAfterPickupCoroutine = null;
        if (!IsBaiting || !PhoneBaitRulesActive || !_phoneLiftedDuringCurrentBait)
            yield break;

        ResolveBaitLikeSelfFix();
    }

    void ResolveBaitLikeSelfFix()
    {
        if (!IsBaiting)
            return;

        StopAllBaitCoroutines();
        _phoneLiftedDuringCurrentBait = false;

        IsBaiting = false;
        ClearPhoneEpisodeStateOnResolved();
        ClearTintOverride();
        OnBaitingEnded?.Invoke();

        // Pure Bait end: Fix() would return early (no longer Broke/Bait) and skip OnFixed;
        // if OnFixed restores normal materials in Inspector, visuals could look fixed without the same event chain as hit-to-fix.
        if (!IsBroken)
            OnFixed?.Invoke();
    }

    /// <summary>
    /// 结算（胜利/失败）时由 GameManager 调用的静默复位。
    /// 清除 Broke/Bait 状态、停止协程、恢复 tint——不触发任何 UnityEvent，不播放音效，不影响 work 值。
    /// </summary>
    public void ForceResetOnMatchEnd()
    {
        if (!IsBroken && !IsBaiting) return;

        enableAutoBreak = false;
        StopAllBaitCoroutines();

        IsBroken  = false;
        IsBaiting = false;
        _phoneLiftedDuringCurrentBait = false;
        _hadPhonePickupWhileBroken    = false;
        ClearPhoneEpisodeStateOnResolved();
        ClearTintOverride();
        // 不调用 OnFixed/OnBaitingEnded，避免在结算时播放修复音效或触发其他 Inspector 绑定
    }

    public void Fix()
    {
        // Run fix logic and OnFixed only from Broke or Bait; idle keypress does nothing
        if (!IsBroken && !IsBaiting) return;
        StopAllBaitCoroutines();
        _phoneLiftedDuringCurrentBait = false;

        bool wasBroken = IsBroken;
        IsBroken = false;
        IsBaiting = false;
        ClearPhoneEpisodeStateOnResolved();
        if (wasBroken)
            _hadPhonePickupWhileBroken = false;

        if (wasBroken && GameManager.Instance != null)
        {
            GameManager.Instance.ApplyWorkPressureOnBrokeRepaired();
            // tutorialBox may point at a dialogue already Destroy()ed by ShowNextBoxForTut; treat as optional.
            if (tutorialBox != null)
                tutorialBox.SetActive(false);
            if (isLast && finalBox != null)
                finalBox.SetActive(true);
        }
            

        ClearTintOverride();
        OnFixed?.Invoke();

        if (debugLogs)
            Debug.Log($"[WorkItem] {name} FIXED -> tint cleared");
    }

    void WarnIfTintDidNotApply(bool applied, string context)
    {
        if (applied || _warnedNoSupportedColorProperty)
            return;

        _warnedNoSupportedColorProperty = true;
        Debug.LogWarning(
            $"[WorkItem] \"{name}\" entering {context}: could not write a color property on any Renderer material (need one of _BaseColor / _Color / _TintColor / _UnlitColor / _MainColor)." +
            "Logic still breaks but visuals may not change, confusing hit-to-fix feedback. Use a shader with those properties or swap materials on OnBroken.",
            this);
    }

    /// <returns>True if at least one material slot received a color write.</returns>
    bool ApplyTintOverride(Color c)
    {
        bool anySlot = false;

        for (int r = 0; r < allRenderers.Length; r++)
        {
            Renderer rend = allRenderers[r];
            if (rend == null) continue;

            bool wroteAny = false;

            int matCount = rend.sharedMaterials != null ? rend.sharedMaterials.Length : 1;
            matCount = Mathf.Max(1, matCount);

            for (int matIndex = 0; matIndex < matCount; matIndex++)
            {
                Material m = null;
                if (rend.sharedMaterials != null && matIndex < rend.sharedMaterials.Length)
                    m = rend.sharedMaterials[matIndex];

                if (m == null) continue;

                bool wroteThisSlot = false;

                for (int i = 0; i < ColorPropIds.Length; i++)
                {
                    int propId = ColorPropIds[i];
                    if (m.HasProperty(propId))
                    {
                        rend.GetPropertyBlock(mpb, matIndex);
                        mpb.SetColor(propId, c);
                        rend.SetPropertyBlock(mpb, matIndex);
                        wroteAny = true;
                        wroteThisSlot = true;
                    }
                }

                if (debugLogs && !wroteThisSlot)
                    Debug.Log($"[WorkItem] {name} renderer({rend.name}) slot({matIndex}) has NO color property.");
            }

            if (debugLogs && !wroteAny)
                Debug.LogWarning($"[WorkItem] {name} renderer({rend.name}) could not be tinted (no supported color props).");

            if (wroteAny)
                anySlot = true;
        }

        return anySlot;
    }

    private void ClearTintOverride()
    {
        for (int r = 0; r < allRenderers.Length; r++)
        {
            Renderer rend = allRenderers[r];
            if (rend == null) continue;

            int matCount = rend.sharedMaterials != null ? rend.sharedMaterials.Length : 1;
            matCount = Mathf.Max(1, matCount);

            for (int matIndex = 0; matIndex < matCount; matIndex++)
                rend.SetPropertyBlock(null, matIndex);
        }
    }
}
