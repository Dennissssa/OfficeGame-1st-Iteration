using UnityEngine;

public class LerpUp : MonoBehaviour
{
    public GameObject endDestination;
    public GameObject startDestination;

    public float lerpSpeed;

    [Header("Return To Start")]
    [Tooltip("勾选后，经过指定时间会改回向起始位置移动")]
    public bool returnToStartAfterDelay;

    [Tooltip("从开始移动算起，多少秒后改回起始位置")]
    [Min(0f)]
    public float returnAfterSeconds = 2f;

    float _elapsed;
    bool _returning;

    void OnEnable()
    {
        _elapsed = 0f;
        _returning = false;
        if (startDestination != null)
            transform.position = startDestination.transform.position;
    }

    void Update()
    {
        if (startDestination == null || endDestination == null) return;

        if (returnToStartAfterDelay && !_returning)
        {
            _elapsed += Time.deltaTime;
            if (_elapsed >= returnAfterSeconds)
                _returning = true;
        }

        Vector3 target = _returning
            ? startDestination.transform.position
            : endDestination.transform.position;

        transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * lerpSpeed);
    }
}
