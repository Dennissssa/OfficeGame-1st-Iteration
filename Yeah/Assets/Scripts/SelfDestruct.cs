using System.Collections;
using UnityEngine;

public class SelfDestruct : MonoBehaviour
{
    public float selfTime;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        StartCoroutine(SelfDes());
    }

    // Update is called once per frame
    IEnumerator SelfDes()
    {
        yield return new WaitForSeconds(selfTime);
        Destroy(gameObject);
    }
}
