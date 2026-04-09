using UnityEngine;

public class GyrateAd : MonoBehaviour
{
    Vector3 startLocale;
    public float gyrateAmount;
    void Start()
    {
        startLocale = transform.position;
    }

    // Update is called once per frame
    void Update()
    {
        transform.position = new Vector3(Random.Range(startLocale.x - gyrateAmount, startLocale.x + gyrateAmount), Random.Range(startLocale.y - gyrateAmount, startLocale.y + gyrateAmount), this.transform.position.z);
    }
}
