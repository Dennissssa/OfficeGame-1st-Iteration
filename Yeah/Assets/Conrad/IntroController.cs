using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class IntroController : MonoBehaviour
{
    //public List<Sprite> bossSprites = new List<Sprite>();
    //public List<Sprite> samSprites = new List<Sprite>();
    //public List<Sprite> creatorSprites = new List<Sprite>();
    //public List<Sprite> playerSprites = new List<Sprite>();
    
    //public List<string> dialogue = new List<string>();

    //public RawImage dialogueHead;

    //public TextMeshProUGUI dialogueText;
    //public TextMeshProUGUI dialogueName;

    Keyboard kb;
    Mouse ms;

    //public int dialogueCount = 0;

    [System.Serializable] public class dialoguePack
    {
        public Sprite dialogueHead;
        public string dialogueText;
        public string dialogueName;
        public bool showBoss;
        public bool showSam;
        public bool showAd;
        public bool adSpamming;
        public GameObject vocalSound;
    }

    public List<dialoguePack> dialogueList;

    public RawImage dialogueHead;
    public TextMeshProUGUI dialogueText;
    public TextMeshProUGUI dialogueName;
    public int dialogueCount;
    public GameObject bossMeeting;
    public GameObject samMeeting;
    public GameObject adMeeting;

    public List<GameObject> adSpam;
    public int adSpamCount;
    public int maxAdCount;
    bool canMakeAd = true;
    public float waitAdMake;

    public GameObject canvas;

    public Camera camMain;

    void Start()
    {
        kb = Keyboard.current;
        ms = Mouse.current;
        camMain = Camera.main;
        Instantiate(dialogueList[0].vocalSound,transform.position,Quaternion.identity);
    }

    void Update()
    {
        if (ms.leftButton.wasPressedThisFrame)
        {
            if (dialogueCount < dialogueList.Count - 1)
            {
                dialogueCount++;
                Instantiate(dialogueList[dialogueCount].vocalSound, transform.position, Quaternion.identity);
            }
        }

        bossMeeting.SetActive(dialogueList[dialogueCount].showBoss);
        samMeeting.SetActive(dialogueList[dialogueCount].showSam);
        adMeeting.SetActive(dialogueList[dialogueCount].showAd);

        if (dialogueList[dialogueCount].adSpamming)
        {
            if (canMakeAd)
            {
                if (adSpamCount <= maxAdCount)
                {
                   StartCoroutine(MakeAd());
                }
            }
        }

        dialogueText.text = dialogueList[dialogueCount].dialogueText;
        dialogueName.text = dialogueList[dialogueCount].dialogueName;
        dialogueHead.texture = dialogueList[dialogueCount].dialogueHead.texture;
    }

    IEnumerator MakeAd()
    {
        canMakeAd = false;
        adSpam[adSpamCount].SetActive(true);
        adSpamCount++;
        yield return new WaitForSeconds(waitAdMake);
        canMakeAd = true;
    }
}
