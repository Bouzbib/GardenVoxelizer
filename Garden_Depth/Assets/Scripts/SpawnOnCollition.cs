using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpawnOnCollition : MonoBehaviour
{
    public GameObject garden;
    public GameObject[] flowerTypes;
    public GameObject ObjectToSpawn;
    private Vector2 posOnPlane;
    private bool isTouched;
    public GameObject fingerObject;
    private float distanceMeasured;
    public float threshold;
    private Vector3 spawnPosition;
    private int objectCount;
    private int collisionNumber;
    public bool randomized;
    private int numObjectToLoad;

    // Start is called before the first frame update
    void Start()
    {
        if(garden == null)
        {
            garden = GameObject.Find("Garden");
        }
        garden.SetActive(true);
        flowerTypes= new GameObject[garden.transform.childCount];
        this.GetComponent<MeshRenderer>().enabled=false;
        
        for(int i=0 ; i < flowerTypes.Length; i++)
        {
            flowerTypes[i]=garden.transform.GetChild(i).gameObject;
        }
        garden.SetActive(false);
    objectCount = 0;
    }

    // Update is called once per frame
    void Update()
    {

        if(fingerObject == null)
        {
            fingerObject = GameObject.FindGameObjectWithTag("InteractiveObject");
        }
        else
        {
            Vector2 posFinger = new Vector2(fingerObject.transform.position.x, fingerObject.transform.position.z);
            distanceMeasured = Vector2.Distance(posOnPlane, posFinger);
        }
        
        // Debug.Log(distanceMeasured);
    }
    void OnCollisionEnter(Collision collision)
    {
        Debug.Log("CLICK");
        if(isTouched==false){
            if (collision.gameObject.CompareTag("InteractiveObject"))
            {
                if(objectCount == 0)
                {
                    if(randomized)
                    {
                        numObjectToLoad = Random.Range(0, flowerTypes.Length);
                    }
                    else
                    {
                        numObjectToLoad = collisionNumber%flowerTypes.Length;
                    }
                    // Debug.Log("Entering here");
                    isTouched=true;
                    StartCoroutine(DistanceTime());
                    ContactPoint contact = collision.contacts[0];
                    spawnPosition= new Vector3 (contact.point.x, -3.5f, contact.point.z);
                    ObjectToSpawn=flowerTypes[numObjectToLoad];
                    // ObjectToSpawn=flowerTypes[collisionNumber%flowerTypes.Length];
                    GameObject newFlower = (GameObject)Instantiate(ObjectToSpawn, spawnPosition, Quaternion.identity, this.transform.parent.transform);
                    newFlower.GetComponent<BloomOrangeFlower>().targetSize = Vector3.Scale(newFlower.transform.localScale, garden.transform.localScale);
                    newFlower.transform.localScale = newFlower.GetComponent<BloomOrangeFlower>().targetSize/5.4f;
                    posOnPlane= new Vector2 (spawnPosition.x, spawnPosition.z);
                    objectCount = 1;
                    collisionNumber=collisionNumber+1;
                }
                
               
                
                
            }
        }
    }
    void OnCollisionStay(Collision collision){
        if( collision.gameObject.CompareTag("InteractiveObject"))
        {
            ContactPoint contact = collision.contacts[0];
            Debug.Log(collision.contacts[0]);
        }
    }
    IEnumerator DistanceTime()
    {
        //yield return new WaitForSeconds(0.5f);
        
        
        yield return new WaitUntil(() => (distanceMeasured > threshold));
        // Debug.Log("turningoff touched");
        // Debug.Log(distanceMeasured);
        isTouched = false;
        objectCount = 0;

    }

    void OnDrawGizmos()
    {
        // Draw a yellow sphere at the transform's position
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(new Vector3(posOnPlane.x, -1.2f, posOnPlane.y), 0.1f);
    }


}

