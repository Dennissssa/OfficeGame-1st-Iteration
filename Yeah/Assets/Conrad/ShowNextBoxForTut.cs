using System.Collections;
using UnityEngine;

public class ShowNextBoxForTut : MonoBehaviour
{
    public GameObject nextBox;
    public float waitTime;
    void Start()
    {
        StartCoroutine(beginDialogueNext());
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    IEnumerator beginDialogueNext()
    {
        yield return new WaitForSeconds(waitTime);
        nextBox.SetActive(true);
        this.gameObject.SetActive(false);
    }
}
