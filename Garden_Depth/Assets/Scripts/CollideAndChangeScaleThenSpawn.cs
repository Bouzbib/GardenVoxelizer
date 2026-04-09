using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/* This is the mushroom script, that scales down and spwans objects
The object should be centered Y-wise around the ground! (to make disappear on the ground, otherwise it's at its center)
 */ 

public class CollideAndChangeScaleThenSpawn : MonoBehaviour
{
	public bool isTouched = false;
	public bool availableToTouch = true;
	public GameObject touchingObject;
	private Vector3 originPosition;
    private Quaternion originRotation;
	private Vector3 originalScale;
    // Start is called before the first frame update
    void Start()
    {
        this.GetComponent<Collider>().isTrigger = true;
        originalScale = this.transform.localScale;
        originRotation = this.transform.rotation;
        availableToTouch = true;
        isTouched = false;
        touchingObject = GameObject.Find("1_index_tip");
    }

    // Update is called once per frame
    void Update()
    {
        touchingObject = GameObject.Find("1_index_tip");
        if(isTouched)
        {
        	availableToTouch = false;
        	Vector3 pushingDownFrom = touchingObject.transform.localPosition - originPosition;
            float factor = Mathf.Abs(pushingDownFrom.y*originalScale.z);
        	this.transform.localScale = new Vector3(this.transform.localScale.x, this.transform.localScale.y, this.transform.localScale.z - factor);

        	if(this.transform.localScale.z < 0.01f*originalScale.z)
        	{
        		for(int i = 0; i < 2; i++)
        		{
        			Vector3 newPosition = new Vector3(this.transform.position.x + Random.Range(-2.0f, 2.0f), this.transform.position.y, this.transform.position.z + Random.Range(-2.0f, 2.0f));
        			GameObject newObject = (GameObject)Instantiate(this.gameObject, newPosition, originRotation, this.transform.parent.gameObject.transform);
        			newObject.transform.localScale = originalScale;
        		}
        		Destroy(this.gameObject);
        	}
        }
    }

    void OnTriggerEnter(Collider other)
    {
    	if(availableToTouch)
    	{
    		if(other.GetComponent<Collider>().tag == "InteractiveObject")
	    	{
	    		// touchingObject = GameObject.Find(other.GetComponent<Collider>().name);
                this.isTouched = true;
                originPosition = touchingObject.transform.localPosition;
	    	}
	    	
		}
    }


    void OnTriggerExit(Collider other)
    {
		if(other.GetComponent<Collider>().tag == "InteractiveObject")
    	{
    		this.isTouched = false;
    		availableToTouch = true;
    		this.transform.localScale = originalScale;
    	}
    }


}
