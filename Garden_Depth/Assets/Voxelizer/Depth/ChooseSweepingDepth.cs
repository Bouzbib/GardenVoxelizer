using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ChooseSweepingDepth : MonoBehaviour
{
	InterlacedPackedVoxelizer voxelizer;
    // Start is called before the first frame update
    void Start()
    {
        if(this.GetComponent<InterlacedPackedVoxelizer>() != null)
        {
            voxelizer = this.GetComponent<InterlacedPackedVoxelizer>();
        }
        
    }

    // Update is called once per frame
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.B))
        {
        	voxelizer.sweepUpOnly = !voxelizer.sweepUpOnly;
        }
        if(Input.GetKeyDown(KeyCode.N))
        {
        	voxelizer.sweepDownOnly = !voxelizer.sweepDownOnly;
        }
    }
}
