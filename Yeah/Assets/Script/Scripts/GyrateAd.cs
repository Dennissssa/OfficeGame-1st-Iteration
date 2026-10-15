using UnityEngine;

public class GyrateAd : MonoBehaviour
{
    Vector3 startLocale;
    public float gyrateAmount;
    public Vector3 targetSize;
    public bool isGrowing;
    public float lerpSpeed;

    Vector3 _scaleFrom;
    Vector3 _scaleTo;
    float _scaleT;
    bool _scaling;
    bool _wasGrowing;
    bool _holdThisFrame;

    void OnEnable()
    {
        if (!Application.isPlaying) return;

        startLocale = transform.position;
        isGrowing = true;
        _wasGrowing = true;
        BeginScale(Vector3.zero, targetSize);
    }

    void BeginScale(Vector3 from, Vector3 to)
    {
        _scaleFrom = from;
        _scaleTo = to;
        _scaleT = 0f;
        _scaling = true;
        _holdThisFrame = true;
        transform.localScale = from;
    }

    void Update()
    {
        if (isGrowing != _wasGrowing)
        {
            _wasGrowing = isGrowing;
            BeginScale(transform.localScale, isGrowing ? targetSize : Vector3.zero);
        }

        if (gyrateAmount > 0.0001f)
        {
            transform.position = new Vector3(
                Random.Range(startLocale.x - gyrateAmount, startLocale.x + gyrateAmount),
                Random.Range(startLocale.y - gyrateAmount, startLocale.y + gyrateAmount),
                transform.position.z);
        }
    }

    void LateUpdate()
    {
        if (!_scaling) return;

        // 出现的那一帧先停在起始尺寸。场景加载第一帧的 deltaTime 往往会很大，
        // 如果当场就开始播，动画会在第一张画面之前结束。
        if (_holdThisFrame)
        {
            _holdThisFrame = false;
            transform.localScale = _scaleFrom;
            return;
        }

        float duration = Mathf.Max(0.35f, 6f / Mathf.Max(lerpSpeed, 0.01f));
        _scaleT += Mathf.Min(Time.unscaledDeltaTime, 0.05f) / duration;
        float u = Mathf.Clamp01(_scaleT);
        float eased = 1f - (1f - u) * (1f - u);
        transform.localScale = Vector3.LerpUnclamped(_scaleFrom, _scaleTo, eased);
        if (u >= 1f)
        {
            transform.localScale = _scaleTo;
            _scaling = false;
        }
    }
}
