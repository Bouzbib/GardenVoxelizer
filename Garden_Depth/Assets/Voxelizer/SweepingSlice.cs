using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SweepingSlice : MonoBehaviour
{
	LayeredSliceRasterVoxelizer voxelizer;
    // Start is called before the first frame update
    void Start()
    {
        if(this.GetComponent<LayeredSliceRasterVoxelizer>() != null)
        {
            voxelizer = this.GetComponent<LayeredSliceRasterVoxelizer>();
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
        if(Input.GetKeyDown(KeyCode.Space))
        {
        	voxelizer.interlaceMode = !voxelizer.interlaceMode;
        }

    }
}
