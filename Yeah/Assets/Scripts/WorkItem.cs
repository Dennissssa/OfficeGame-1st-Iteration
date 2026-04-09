using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

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
    public KeyCode debugBreakKeyOldInput = KeyCode.None; // Legacy input unused (leave None)
    public string debugBreakBindingPath = "<Keyboard>/b"; // New Input: B forces break (debug)

    public bool IsBroken { get; private set; } = false;
    public bool IsBaiting {get; private set;} = false;

    /// <summary>Whether Bait uses phone rules (Inspector toggle or itemName equals "phone", case-insensitive).</summary>
    public bool PhoneBaitRulesActive =>
        usePhoneBaitBehavior
        || (!string.IsNullOrWhiteSpace(itemName) && itemName.Trim().Equals("phone", System.StringComparison.OrdinalIgnoreCase));

    /// <summary>For GameManager: whether phone-style Bait should decay performance score per second.</summary>
    public bool ShouldApplyPhoneBaitPerformanceDecay => IsBaiting && PhoneBaitRulesActive;

    /// <summary>For audio: under phone rules, whether handset is on cradle (not off-hook). Always false for non-phone items.</summary>
    public bool PhoneIsOnCradleForSfx => PhoneBaitRulesActive && !_phonePhysicallyOffHook;

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

    /// <summary>Ignore TryRepair briefly after hang-up (reduce piezo vibration false triggers).</summary>
    float _phoneTryRepairSuppressedUntilTime;

    /// <summary>Had off-hook while still Broke (broken loop stopped by pickup logic); on hang-up while still broken, resume loop.</summary>
    bool _hadPhonePickupWhileBroken;

    //this doesn't really have a point but it's fun lol
    public GameObject smokeParticles;

    public string itemName;

    [Header("Phone · Bait (distinct from normal Bait)")]
    [Tooltip("When on: no 3s self-fix; without off-hook, Bait persists. End: off-hook then hang-up, or wait phoneBaitAutoResolveSecondsAfterPickup after pickup.")]
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

    /// <summary>Bind from Arduino <c>onPhonePickup</c>: updates off-hook state and advances phone Bait flow when applicable.</summary>
    public void NotifyPhonePickedUpForBaitFlow()
    {
        if (!PhoneBaitRulesActive)
            return;

        _phonePhysicallyOffHook = true;

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

    /// <summary>Bind from Arduino <c>onPhonePutdown</c>: on-hook state, TryRepair debounce; ends phone Bait if was lifted this Bait.</summary>
    public void NotifyPhonePutDownForBaitFlow()
    {
        if (!PhoneBaitRulesActive)
            return;

        _phonePhysicallyOffHook = false;
        _phoneTryRepairSuppressedUntilTime = Time.time + Mathf.Max(0f, phonePutDownTryRepairDebounceSeconds);

        if (IsBaiting && _phoneLiftedDuringCurrentBait)
            ResolveBaitLikeSelfFix();

        if (IsBroken && _hadPhonePickupWhileBroken)
        {
            _hadPhonePickupWhileBroken = false;
            JiU.PlaySoundOnEventAudioManager.ResumeBrokenLoopAfterPhonePutdownForWorkItem(this);
            JiU.PlaySoundOnEvent.ResumeBrokenClipAfterPhonePutdownForWorkItem(this);
        }
    }

    /// <summary>电话物体：Boss 预警或 Boss 在场期间不进行随机的 Broke / Bait（含原为兜底而转 Bait 的分支）。</summary>
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

        // Phone: before generic logic — ignore TryRepair entirely during post-hang-up debounce (no false punish)
        if (PhoneBaitRulesActive)
        {
            if (Time.time < _phoneTryRepairSuppressedUntilTime)
                return;

            // On-hook: any TryRepair counts as wrong repair (Ultra on Bait, else Punishment)
            if (!_phonePhysicallyOffHook)
            {
                ApplyPhoneTryRepairWhileOnHookPunishment();
                return;
            }
        }

        // Only real Broke after range checks does win + Fix; Bait / idle spam only lose and return
        if (!IsBroken)
        {
            if (IsBaiting)
            {
                if (GameManager.Instance != null)
                    GameManager.Instance.UltraPunishment();
                // Punishment may game-over this frame; invoking again may run Inspector hooks that hide the fail panel
                if (GameManager.Instance == null || !GameManager.Instance.IsGameOver)
                    OnRepairIncorrect?.Invoke();
                return;
            }

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

    void ApplyPhoneTryRepairWhileOnHookPunishment()
    {
        if (IsBaiting)
        {
            if (GameManager.Instance != null)
                GameManager.Instance.UltraPunishment();
            if (GameManager.Instance == null || !GameManager.Instance.IsGameOver)
                OnRepairIncorrect?.Invoke();
            return;
        }

        if (GameManager.Instance != null)
            GameManager.Instance.Punishment();
        if (GameManager.Instance == null || !GameManager.Instance.IsGameOver)
            OnRepairIncorrect?.Invoke();
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
        if (IsBroken || IsBaiting) return;
        _hadPhonePickupWhileBroken = false;
        IsBroken = true;

        if (GameManager.Instance != null)
        {
            if (GameManager.Instance.isTutorialing == true)
            {
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
        if (IsBaiting || IsBroken) return;
        IsBaiting = true;
        _phoneLiftedDuringCurrentBait = false;
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
        yield return new WaitForSeconds(3);
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
        ClearTintOverride();
        OnBaitingEnded?.Invoke();

        // Pure Bait end: Fix() would return early (no longer Broke/Bait) and skip OnFixed;
        // if OnFixed restores normal materials in Inspector, visuals could look fixed without the same event chain as hit-to-fix.
        if (!IsBroken)
            OnFixed?.Invoke();
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
        if (wasBroken)
            _hadPhonePickupWhileBroken = false;

        if (wasBroken && GameManager.Instance != null)
        {
            GameManager.Instance.ApplyWorkPressureOnBrokeRepaired();
            tutorialBox.SetActive(false);
            if (isLast)
            {
                finalBox.SetActive(true);
            }
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
