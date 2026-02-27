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

    [Header("Broke / Bait 触发概率")]
    [Tooltip("下一次故障时触发 Broke 的权重，与 Bait 权重共同决定概率。例如 8 和 2 表示约 80% Broke、20% Bait")]
    [Min(0f)]
    public float breakWeight = 5f;
    [Tooltip("下一次故障时触发 Bait 的权重")]
    [Min(0f)]
    public float baitWeight = 1f;

    [Header("Hotkey Repair (Input System)")]
    [Tooltip("Examples: <Keyboard>/1  <Keyboard>/2  <Keyboard>/numpad1  <Keyboard>/q")]
    public string repairBindingPath = "<Keyboard>/1";

    [Header("Optional Distance Requirement")]
    public bool requirePlayerInRange = false;
    public float interactRange = 2.0f;
    public Transform player; // 不填会自动找 Tag=Player

    [Header("Colors")]
    public Color brokenColor = new Color(1f, 0.2f, 0.2f, 1f);
    public Color baitColor = new Color(0.2f, 1f, 0.2f, 1f);

    [Header("Uduino / 外部输出（可选）")]
    [Tooltip("故障发生时触发，可连到 Uduino 输出等")]
    public UnityEvent OnBroken;
    [Tooltip("I'm scared")] 
    public UnityEvent OnBaiting;
    [Tooltip("修好时触发（玩家击打修好时），可连到 Uduino / LED 恢复等")]
    public UnityEvent OnFixed;
    [Tooltip("Bait 时间到自行结束时触发（玩家未击打），可连到 LED 恢复等")]
    public UnityEvent OnBaitingEnded;

    [Header("Debug")]
    public bool debugLogs = false;
    public KeyCode debugBreakKeyOldInput = KeyCode.None; // 旧Input不用（留空）
    public string debugBreakBindingPath = "<Keyboard>/b"; // 新Input：按B强制故障（排查用）

    public bool IsBroken { get; private set; } = false;
    public bool IsBaiting {get; private set;} = false;

    private Renderer[] allRenderers;
    private MaterialPropertyBlock mpb;

    private InputAction repairAction;
    private InputAction debugBreakAction;

    //this doesn't really have a point but it's fun lol
    public GameObject smokeParticles;

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
        repairAction.Enable();
        repairAction.performed += OnRepairPerformed;

        debugBreakAction.Enable();
        debugBreakAction.performed += OnDebugBreakPerformed;
    }

    void OnDisable()
    {
        repairAction.performed -= OnRepairPerformed;
        repairAction.Disable();

        debugBreakAction.performed -= OnDebugBreakPerformed;
        debugBreakAction.Disable();
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

        repairAction?.Dispose();
        debugBreakAction?.Dispose();
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
            if (!IsBroken || !IsBaiting)
            {
                float t = Random.Range(minTimeToBreak, maxTimeToBreak);
                yield return new WaitForSeconds(t);

                // Boss 预警开始到离开期间：不产生新故障
                if (GameManager.FreezeFailures)
                    continue;

                // 按 Inspector 中的 breakWeight / baitWeight 决定本次是 Broke 还是 Bait
                float total = breakWeight + baitWeight;
                if (total <= 0f)
                {
                    if (Random.value < 0.5f) Break();
                    else Bait();
                }
                else
                {
                    float roll = Random.Range(0f, total);
                    if (roll < breakWeight)
                        Break();
                    else
                        Bait();
                }
            }
            else
            {
                yield return null;
            }
        }
    }

    private void TryRepair()
    {
        if (!IsBroken)
        {
            if (IsBaiting && GameManager.Instance != null)
                GameManager.Instance.UltraPunishment();
            else if (GameManager.Instance != null)
                GameManager.Instance.Punishment();
        }
        if (requirePlayerInRange)
        {
            if (player == null) return;
            if (Vector3.Distance(player.position, transform.position) > interactRange) return;
        }
        Fix();
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
        if (IsBroken) return;
        IsBroken = true;

        ApplyTintOverride(brokenColor);
        OnBroken?.Invoke();

        if (debugLogs)
            Debug.Log($"[WorkItem] {name} BROKE -> tint applied");
    }

    public void Bait()
    {
        if (IsBaiting) return;
        IsBaiting = true;
        
        ApplyTintOverride(baitColor);
        OnBaiting?.Invoke();
        
        if (debugLogs)
            Debug.Log($"[WorkItem] {name} BAIT -> tint applied");
        StartCoroutine(BaitSelfFix());
    }

    IEnumerator BaitSelfFix()
    {
        yield return new WaitForSeconds(3);
        if (IsBaiting)
        {
            IsBaiting = false;
            ClearTintOverride();
            OnBaitingEnded?.Invoke();
        }
        Fix();
    }

    public void Fix()
    {
        // 仅当当前处于 Broke 或 Bait 时才执行修好逻辑并触发 OnFixed；正常状态下按键不触发
        if (!IsBroken && !IsBaiting) return;
        IsBroken = false;
        IsBaiting = false;

        ClearTintOverride();
        OnFixed?.Invoke();

        if (debugLogs)
            Debug.Log($"[WorkItem] {name} FIXED -> tint cleared");
    }

    private void ApplyTintOverride(Color c)
    {
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
        }
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
