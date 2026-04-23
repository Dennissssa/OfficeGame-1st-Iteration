using UnityEngine;

public class LerpUp : MonoBehaviour
{
    public GameObject endDestination;
    public GameObject startDestination;

    public float lerpSpeed;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        this.transform.position = startDestination.transform.position;
    }

    // Update is called once per frame
    void Update()
    {
        this.transform.position = Vector3.Lerp(this.transform.position, endDestination.transform.position, (Time.deltaTime * lerpSpeed));
    }
}
