using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class ScreenVignetteTint : MonoBehaviour
{
    public float maxAlpha = 0.55f; // 最红时透明度
    public float current = 0f;

    private Image img;
    private float target = 0f;
    private float speed = 1f;

    void Awake()
    {
        img = GetComponent<Image>();
        Apply();
    }

    void Update()
    {
        if (Mathf.Approximately(current, target)) return;

        current = Mathf.MoveTowards(current, target, speed * Time.deltaTime);
        Apply();
    }

    // normalized01: 0~1（红的强度）
    public void SetTarget(float normalized01, float duration)
    {
        target = Mathf.Clamp01(normalized01);
        float dist = Mathf.Abs(target - current);
        speed = (duration <= 0.0001f) ? 999f : (dist / duration);
    }

    private void Apply()
    {
        if (img == null) return;

        Color c = img.color;
        c.r = 1f; c.g = 0f; c.b = 0f;
        c.a = current * maxAlpha;
        img.color = c;
    }
}
