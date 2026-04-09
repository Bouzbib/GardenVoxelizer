using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/* This is the script to enable the alternate swiping, making the floor move below the fingers indefinitely */

public class CollideAndMove : MonoBehaviour
{
	public bool availableToCollide;

	private Vector3 originalPosPlane;
	private Vector3 originalFingerPos;
	private GameObject objectOfInterest;
	public bool index, middle;
    public string previous;
    private float scale = 0.005f;
    public float threshold = 0.08f;
    private string nextOne;

    private Vector3 oldIndex, oldMiddle;

    private Vector3 normA, normB, bisector, midPoint;
    public int countSwitch;
    private int newCountSwitch;

    private GameObject indexObject, middleObject, palmObject, thumbObject, middleTip, indexTip, indexSnd;
    private Vector3 originPosition;
    private int state;
    public bool gettingToLimitX, gettingToLimitZ;
    private float originY;
    public float magicNumber = 40f;
    private GameObject terrainToReplicate;
    // Start is called before the first frame update
    void Start()
    {
        // this.GetComponent<MeshCollider>().convex = true;
        this.GetComponent<Collider>().isTrigger = true;
        availableToCollide = true;

        originY = this.transform.position.y;
        gettingToLimitZ = true;
        gettingToLimitX = true;
        terrainToReplicate = GameObject.Find("GameManager").GetComponent<AlwaysPutAPlane>().terrainToReplicate;
        state = 1;

    }

    void OnEnable()
    {
        this.GetComponent<Collider>().isTrigger = true;
        availableToCollide = true;
        indexObject = GameObject.Find("1_index_first");
        middleObject = GameObject.Find("1_middle_first");
        thumbObject = GameObject.Find("1_thumb_first");
        palmObject = GameObject.Find("1_palm_base");
        indexTip = GameObject.Find("1_index_tip");
        indexSnd = GameObject.Find("1_index_third");
        middleTip = GameObject.Find("1_middle_tip");
    }

    // Update is called once per frame
    void Update()
    {
        switch(state)
        {
            case 1:
                indexObject = GameObject.Find("1_index_first");
                middleObject = GameObject.Find("1_middle_first");
                thumbObject = GameObject.Find("1_thumb_first");
                palmObject = GameObject.Find("1_palm_base");
                indexTip = GameObject.Find("1_index_tip");
                indexSnd = GameObject.Find("1_index_third");
                middleTip = GameObject.Find("1_middle_tip");
                state = 2;
                StartCoroutine(CountSwitches());
                break;

            case 2:

                RunTheRest();

                if(Mathf.Abs(this.transform.position.x) >= 9.5f)
                {
                    if(!gettingToLimitX)
                    {
                        Vector3 originPlane = new Vector3(Mathf.Sign(this.transform.position.x)*8f*(-1), originY, Mathf.Sign(this.transform.position.z)*8f*(-1));
                        GameObject newPlane = Instantiate(terrainToReplicate, originPlane, Quaternion.identity);
                        newPlane.SetActive(true);
                        gettingToLimitX = true;
                    }
                    else
                    {
                        if(Mathf.Abs(this.transform.position.x) > 9.6f)
                        {
                            Destroy(this.gameObject);
                        }
                    }
                }
                else
                {
                    gettingToLimitX = false;
                }

                if(Mathf.Abs(this.transform.position.z) >= 9.5f)
                {
                    if(!gettingToLimitZ)
                    {
                        if(!gettingToLimitX)
                        {
                            Vector3 originPlane = new Vector3(Mathf.Sign(this.transform.position.x)*8f*(-1), originY, Mathf.Sign(this.transform.position.z)*8f*(-1));
                            GameObject newPlane = Instantiate(terrainToReplicate, originPlane, Quaternion.identity);
                            newPlane.SetActive(true);
                            gettingToLimitZ = true;
                        }
                        
                    }
                    else
                    {
                        if(Mathf.Abs(this.transform.position.z) > 9.6f)
                        {
                            Destroy(this.gameObject);
                        }
                    }
                   
                }
                else
                {
                    gettingToLimitZ = false;
                }
                break;
        }
        
    }

    void RunTheRest()
    {

        if(!index && !middle)
        {
            availableToCollide = true;
        }
        else
        {
            availableToCollide = false;
        }

        normA = (indexObject.transform.position - thumbObject.transform.position).normalized;
        normB = (middleObject.transform.position - palmObject.transform.position).normalized;

        midPoint = indexObject.transform.position - (indexObject.transform.position - middleObject.transform.position)/2;
        bisector = (normA + normB).normalized;

            // By debugging values, we find that a dot of 2 in a good threshold to detect switches
        // Debug.Log(Vector3.Dot(Vector3.ProjectOnPlane(indexTip.transform.position, this.gameObject.transform.up) - Vector3.ProjectOnPlane(middleTip.transform.position, this.gameObject.transform.up), bisector));
        
        // if(Vector3.SignedAngle(this.transform.up, indexTip.transform.position - indexSnd.transform.position, Vector3.up) > 140)
        // WORKED PRETTY WELL WITH 1 FOR BISECTORS
        Debug.Log("Count:" + countSwitch + " ; Distance : " + Vector3.Distance(indexTip.transform.position, oldIndex));
        // if(Vector3.Distance(indexTip.transform.position, oldIndex) > 0.8f)
        // {
            if(Vector3.Dot(Vector3.ProjectOnPlane(indexTip.transform.position, this.gameObject.transform.up) - Vector3.ProjectOnPlane(middleTip.transform.position, this.gameObject.transform.up), bisector) > 1f)
            {
                countSwitch = newCountSwitch + 1;
                if(countSwitch > 12)
                {
                    countSwitch = 12;
                }
                // this.GetComponent<Renderer>().material.color = Color.red;
            }
            else
            {
                newCountSwitch = countSwitch + 1;
                if(newCountSwitch > 12)
                {
                    newCountSwitch = 12;
                }
                // this.GetComponent<Renderer>().material.color = Color.green;
            }
        // }

    }

    void OnTriggerEnter(Collider other)
    {
        if(other.GetComponent<Collider>().name == "1_index_tip")
    	{
    		index = true;
            if(!middle)
            {
                // originalPosPlane = this.transform.position;
                originalFingerPos = other.transform.gameObject.transform.localPosition;
                previous = "1_index_tip";
                StartCoroutine("SwitchFinger", other.transform.gameObject);


            }

            // this.gameObject.GetComponent<Renderer>().material.color = Color.blue;
            
    	}

    	if(other.GetComponent<Collider>().name == "1_middle_tip")
    	{
    		middle = true;
            if(!index)
            {
                // originalPosPlane = this.transform.position;
                originalFingerPos = other.transform.gameObject.transform.localPosition;
                previous = "1_middle_tip";
    	        StartCoroutine("SwitchFinger", other.transform.gameObject);
            }
    	}

    }

    void OnTriggerStay(Collider other)
    {
    	if(previous == "1_index_tip")
    	{
	    	objectOfInterest = indexTip;
    	}
    	if(previous == "1_middle_tip")
    	{
	    	objectOfInterest = middleTip;

    	}
        if(!availableToCollide && (countSwitch > 4))
        {
            float orientation = -1; //Mathf.Sign(Vector3.Dot((originalFingerPos - objectOfInterest.transform.localPosition), bisector));
            // Debug.Log(orientation);
            Vector3 movement = bisector*orientation*scale;
            // indexTip.transform.gameObject.tag = "InteractiveObject";
            if(Mathf.Sqrt(Mathf.Pow(objectOfInterest.transform.position.x - originalFingerPos.x, 2) + Mathf.Pow(objectOfInterest.transform.position.z - originalFingerPos.z, 2)) > threshold)
            {
                // this.transform.position = new Vector3(this.transform.position.x + movement.x, this.transform.position.y, this.transform.position.z + movement.z);
                this.transform.Translate(new Vector3(movement.x,0,movement.z));
            }
        }

        

    }


    void OnTriggerExit(Collider other)
    {
    	if((other.GetComponent<Collider>().name == "1_middle_tip") && (middle == true))
    	{
    		middle = false;
    	}
    	if((other.GetComponent<Collider>().name == "1_index_tip") && (index == true))
    	{
    		index = false;
    		// indexTip.transform.gameObject.tag = "InteractiveObject";
    	}
        oldMiddle = middleTip.transform.position;
        oldIndex = indexTip.transform.position; 
    	
    }

    IEnumerator SwitchFinger(GameObject collidingObject)
    {
    	// Limit the time it can slide with the one finger
        yield return new WaitForSeconds(0.2f);
        if(!availableToCollide)
        {
            if(previous == "1_index_tip")
            {
                if(middle)
                {
                    nextOne = "1_middle_tip";
                }
            }

            if(previous == "1_middle_tip")
            {
                if(index)
                {
                    nextOne = "1_index_tip";
                }
            }
            // originalPosPlane = this.transform.position;
            if(nextOne != null)
            {
                originalFingerPos = GameObject.Find(nextOne).transform.position;
                StartCoroutine("SwitchFinger", GameObject.Find(nextOne));
            }
            
            previous = nextOne;
        }
        oldMiddle = middleTip.transform.position;
        oldIndex = indexTip.transform.position; 

    }

    IEnumerator CountSwitches()
    {
        
        yield return new WaitForSeconds(1.5f);
        scale = threshold*countSwitch/magicNumber;
        countSwitch = 0;
        newCountSwitch = 0;
        StartCoroutine(CountSwitches());
    }

    // void OnDrawGizmos()
    // {
    //     if(palmObject != null)
    //     {
    //         Gizmos.color = Color.blue;
    //         Gizmos.DrawLine(midPoint, midPoint + bisector*5);
    //         Gizmos.color = Color.red;
    //         Gizmos.DrawLine(Vector3.ProjectOnPlane(midPoint, this.gameObject.transform.up), Vector3.ProjectOnPlane(midPoint, this.gameObject.transform.up) + bisector*5);
        
    //         // Gizmos.color = Color.red;
    //         // Gizmos.DrawLine(indexObject.transform.position, thumbObject.transform.position);
    //         // Gizmos.color = Color.blue;
    //         // Gizmos.DrawLine(indexTip.transform.position, indexSnd.transform.position);
    //         // Debug.Log(Vector3.SignedAngle(this.transform.up, indexTip.transform.position - indexSnd.transform.position, Vector3.up));
    //     }
    // }
}
