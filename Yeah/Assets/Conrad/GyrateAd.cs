using UnityEngine;

public class GyrateAd : MonoBehaviour
{
    Vector3 startLocale;
    public float gyrateAmount;
    public Vector3 targetSize;
    public bool isGrowing;
    public float lerpSpeed;
    void Start()
    {
        startLocale = transform.position;
        isGrowing = true;
        this.transform.localScale = Vector3.zero;
    }

    // Update is called once per frame
    void Update()
    {
        transform.position = new Vector3(Random.Range(startLocale.x - gyrateAmount, startLocale.x + gyrateAmount), Random.Range(startLocale.y - gyrateAmount, startLocale.y + gyrateAmount), this.transform.position.z);
        if (isGrowing)
        {
            if (this.transform.localScale.x < targetSize.x)
            {
                this.transform.localScale = Vector3.Lerp(this.transform.localScale, targetSize, Time.deltaTime * lerpSpeed);
            }
        }
        else
        {
            if (this.transform.localScale.x > 0)
            {
                this.transform.localScale = Vector3.Lerp(this.transform.localScale, Vector3.zero, Time.deltaTime * lerpSpeed);
            }
        }
    }
}
