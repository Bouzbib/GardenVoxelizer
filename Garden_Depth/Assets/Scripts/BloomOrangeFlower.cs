using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BloomOrangeFlower : MonoBehaviour
{
    public Vector3 targetSize;
    public float growFactor;
    public bool finishedGrowing = false;
    public float delay = 6f;
    private bool firstBloom = false;

    // Start is called before the first frame update
    void Start()
    {

       if(this.GetComponent<Animator>() == null)
       {
        this.gameObject.AddComponent<Animator>();
       }
       this.GetComponent<Animator>().enabled=false;
    }

    // Update is called once per frame
    void Update()
    {
        if(!firstBloom)
        {
            if(this.transform.localScale.x<targetSize.x)
            {
                this.transform.localScale = this.transform.localScale * growFactor;
                // Debug.Log("GROWING");
                // Debug.Log("deltatime: " + Time.deltaTime*growFactor);
            }
            else
            {
                finishedGrowing = true;
                firstBloom = true;
                this.GetComponent<Animator>().enabled=true;
            }
        }
        

        if(finishedGrowing)
        {
            // call function
            StartCoroutine(Disappear());
            finishedGrowing = false;
        }
    }

    IEnumerator Disappear()
    {
        // Debug.Log("I'm waiting");
        yield return new WaitForSeconds(delay);
        Destroy(this.gameObject);
        // Debug.Log("6 seconds have passed");

    }
}
