using System.Collections;
using System.Collections.Generic;
using UnityEngine;
// using Voxon;

/* This is the script to enable the popping of a gameObject */
// [RequireComponent(typeof(VXDynamicComponent))]
// [RequireComponent(typeof(CorrectionMesh))]
// [RequireComponent(typeof(RemoveVXComponent))]

[RequireComponent(typeof(AudioSource))]
public class CollideAndDestroyWithSound : MonoBehaviour
{
    public bool isTouched = false;
    public bool hasTouched = false;
    bool soundPlay = false;
    AudioSource audioData;
    private int state = 0;
    public Vector3 originalScaleKid, originalScaleparent;
    public Vector3 volumeCenter, volumeSize;
    // Start is called before the first frame update
    // void Awake()
    // {
    // 	this.GetComponent<AudioSource>().playOnAwake = false;

    // }
    void Start()
    {
        originalScaleparent = this.transform.parent.transform.localScale;
        originalScaleKid = this.transform.localScale;
        this.GetComponent<Collider>().isTrigger = true;
        this.isTouched = false;
        this.GetComponent<Renderer>().enabled = true;
    	audioData = this.GetComponent<AudioSource>();
        audioData.playOnAwake = false;
        volumeCenter = GameObject.Find("Voxelizer").GetComponent<InterlacedPackedVoxelizer>().volumeCenter;
        volumeSize = GameObject.Find("Voxelizer").GetComponent<InterlacedPackedVoxelizer>().volumeSize;
        state = 0;
    }

    // Update is called once per frame
    void Update()
    {
        
        if(!hasTouched)
        {
            if(isTouched && !soundPlay)
            {
                StartCoroutine(SoundAndDestroy());
                hasTouched = true;
            }
        }
    }

	void OnTriggerEnter(Collider other)
    {
        if((this.transform.position.x > (volumeCenter.x - volumeSize.x/2f)) && (this.transform.position.x < (volumeCenter.x + volumeSize.x/2f)))
        {
            if((this.transform.position.z > (volumeCenter.z - volumeSize.z/2f)) && (this.transform.position.z < (volumeCenter.z + volumeSize.z/2f)))
            {
                if((this.transform.position.y > (volumeCenter.y - volumeSize.y/2f)) && (this.transform.position.y < (volumeCenter.y + volumeSize.y/2f)))
                {
                    
                    if(other.GetComponent<Collider>().tag == "InteractiveObject")
                    {
                        this.isTouched = true;
                    }
                }
            }
        }
        
    	

    }

    IEnumerator SoundAndDestroy()
    {
    	this.transform.localScale = this.transform.localScale*1.01f;
    	yield return new WaitForSeconds(0.2f);
    	if(!soundPlay)
    	{
    		audioData.Play();
    		soundPlay = true;
    		this.GetComponent<Renderer>().enabled = false;
    	}
    	yield return new WaitForSeconds(0.5f);
        Vector3 newPosition = new Vector3(this.transform.position.x + Random.Range(-2.0f, 2.0f), this.transform.position.y, this.transform.position.z + Random.Range(-2.0f, 2.0f));
        GameObject newObject = (GameObject)Instantiate(this.gameObject, newPosition, Quaternion.identity, this.gameObject.transform.parent.transform);
        newObject.transform.localScale = originalScaleKid;
        newObject.GetComponent<CollideAndDestroyWithSound>().isTouched = false;
        newObject.GetComponent<CollideAndDestroyWithSound>().hasTouched = false;
        Destroy(this.gameObject);
    }
}
