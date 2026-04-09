using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Move : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if(Input.GetKey(KeyCode.RightArrow))
        {
        	Vector3 newPos = new Vector3(0.05f, 0,0);
        	this.transform.Translate(newPos);
        }
        if(Input.GetKey(KeyCode.LeftArrow))
        {
        	Vector3 newPos = new Vector3(-0.05f, 0,0);
        	this.transform.Translate(newPos);
        }

        if(Input.GetKey(KeyCode.UpArrow))
        {
        	Vector3 newPos = new Vector3(0,0,0.05f);
        	this.transform.Translate(newPos);
        }
        if(Input.GetKey(KeyCode.DownArrow))
        {
        	Vector3 newPos = new Vector3(0,0,-0.05f);
        	this.transform.Translate(newPos);
        }
    }
}
