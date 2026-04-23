using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
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
        public Sprite boxSprite;
        public Sprite dialogueHead;
        public string dialogueText;
        public string dialogueName;
        public bool showZoomJoin;
        public bool showBoss;
        public bool showSamMeetingJoin;
        public bool showSam;
        public bool showAd;
        public bool showTracker;
        public bool adSpamming;
        public GameObject vocalSound;
        public bool waitsForPlayerInput;
        public bool initiatesWaitingPeriod;
        public bool showDialogue;
        public float dialogueWaitTime;
    }

    public List<dialoguePack> dialogueList;

    public GameObject dialogueGroup;
    public RawImage dialogueHead;
    public Image messageBox;
    public TextMeshProUGUI dialogueText;
    public TextMeshProUGUI dialogueName;
    public int dialogueCount;
    public GameObject bossMeetingJoin;
    public GameObject bossMeeting;
    public GameObject samMeetingJoin;
    public GameObject samMeeting;
    public GameObject adMeeting;
    public GameObject trackerWindow;

    private GameObject previousAudioSource;
    public List<GameObject> adSpam;
    public GyrateAd adScript;
    public int adSpamCount;
    public int maxAdCount;
    bool canMakeAd = true;
    public float waitAdMake;
    public bool canAdvance;

    public GameObject canvas;

    public Camera camMain;
    public int playSceneCount;

    void Start()
    {
        kb = Keyboard.current;
        ms = Mouse.current;
        camMain = Camera.main;
        //Instantiate(dialogueList[0].vocalSound,transform.position,Quaternion.identity);
    }

    void Update()
    {
        //if (ms.leftButton.wasPressedThisFrame)
        //{
            //if (dialogueCount < dialogueList.Count - 1)
            //{
                //dialogueCount++;
                //Instantiate(dialogueList[dialogueCount].vocalSound, transform.position, Quaternion.identity);
            //}
        //}
        /*if (canAdvance)
        {
            StartCoroutine(AdvanceText());
        }*/

        //if (dialogueList[dialogueCount].initiatesWaitingPeriod)
       // {
            if (canAdvance)
            {
                StartCoroutine(AdvanceText());
            }
       // }
        

        if (ms.leftButton.wasPressedThisFrame)
        {
            if ((dialogueList[dialogueCount].waitsForPlayerInput == true) || (dialogueList[dialogueCount].initiatesWaitingPeriod == true))
            {
                return;
            }
            
            else if (dialogueCount < dialogueList.Count - 1)
            {
                StopAllCoroutines();
                dialogueCount++;
                if (dialogueList[dialogueCount].vocalSound != null)
                {
                    if (previousAudioSource != null)
                    {
                        
                        Destroy(previousAudioSource.gameObject);
                        previousAudioSource = Instantiate(dialogueList[dialogueCount].vocalSound, transform.position, Quaternion.identity);
                    }
                    else
                    {
                        GameObject newAud = Instantiate(dialogueList[dialogueCount].vocalSound, transform.position, Quaternion.identity);
                        previousAudioSource = newAud;
                    }
                }
    
                if (dialogueList[dialogueCount].waitsForPlayerInput == true)
                {
                    return;
                }
                else
                {
                    canAdvance = true;
                }
            }
            else
            {
                StopAllCoroutines();
                SceneManager.LoadScene(playSceneCount);
            }
        }

        dialogueGroup.SetActive(dialogueList[dialogueCount].showDialogue);
        bossMeetingJoin.SetActive(dialogueList[dialogueCount].showZoomJoin);
        bossMeeting.SetActive(dialogueList[dialogueCount].showBoss);
        samMeetingJoin.SetActive(dialogueList[dialogueCount].showSamMeetingJoin);
        samMeeting.SetActive(dialogueList[dialogueCount].showSam);
        adMeeting.SetActive(dialogueList[dialogueCount].showAd);
        trackerWindow.SetActive(dialogueList[dialogueCount].showTracker);

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
        else
        {
            foreach (GameObject ad in adSpam)
            {
                adScript = ad.GetComponent<GyrateAd>();
                adScript.isGrowing = false; 
            }
        }

        /*if (ms.leftButton.wasPressedThisFrame)
        {
            if (dialogueList[dialogueCount].waitsForPlayerInput == true)
            {
                StartAdPanic();
            }
            else
            {
                return;
            }
        }*/
        

        dialogueText.text = dialogueList[dialogueCount].dialogueText;
        dialogueName.text = dialogueList[dialogueCount].dialogueName;
        dialogueHead.texture = dialogueList[dialogueCount].dialogueHead.texture;
        messageBox.sprite = dialogueList[dialogueCount].boxSprite;
    }

    IEnumerator MakeAd()
    {
        canMakeAd = false;
        adSpam[adSpamCount].SetActive(true);
        adSpamCount++;
        adScript = adSpam[adSpamCount].GetComponent<GyrateAd>();
        adScript.isGrowing = true;
        yield return new WaitForSeconds(waitAdMake);
        canMakeAd = true;
    }

    IEnumerator AdvanceText()
    {
        
        canAdvance = false;
        yield return new WaitForSeconds(dialogueList[dialogueCount].dialogueWaitTime);
        if (dialogueCount < dialogueList.Count - 1)
        {
            dialogueCount++;
            if (dialogueList[dialogueCount].vocalSound != null)
            {
                if (previousAudioSource != null)
                {
                        
                    Destroy(previousAudioSource.gameObject);
                    previousAudioSource = Instantiate(dialogueList[dialogueCount].vocalSound, transform.position, Quaternion.identity);
                }
                else
                {
                    GameObject newAud = Instantiate(dialogueList[dialogueCount].vocalSound, transform.position, Quaternion.identity);
                    previousAudioSource = newAud;
                }
            }
    
            if (dialogueList[dialogueCount].waitsForPlayerInput == true)
            {
                yield break;
            }
            else
            {
                canAdvance = true;
            }
        }
        else
        {
            SceneManager.LoadScene(playSceneCount);
        }
    }

    public void StartAdPanic()
    {
        dialogueCount++;
        canAdvance = true;
        if (dialogueList[dialogueCount].vocalSound != null)
        {
            if (previousAudioSource != null)
            {
                        
                Destroy(previousAudioSource.gameObject);
                previousAudioSource = Instantiate(dialogueList[dialogueCount].vocalSound, transform.position, Quaternion.identity);
            }
            else
            {
                GameObject newAud = Instantiate(dialogueList[dialogueCount].vocalSound, transform.position, Quaternion.identity);
                previousAudioSource = newAud;
            }
        }
    }
}
