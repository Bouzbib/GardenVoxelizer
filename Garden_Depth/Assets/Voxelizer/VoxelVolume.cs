using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VoxelVolume : MonoBehaviour
{
	[Header("World Volume Size")]
	public Vector3 volumeSize = new Vector3(10,10,10);
	public Vector3 volumeCenter = new Vector3(0,0,0);

	public Vector3 VolumeMin => transform.position - volumeSize * 0.5f;
	public Vector3 VolumeMax => transform.position + volumeSize * 0.5f;

    [Header("Resolution")]
    public int resolutionX = 128;
    public int resolutionY = 128;
    public int resolutionZ = 192; // your 192 slices

    [Header("Debug")]
    public bool clearOnStart = true;

    public ComputeBuffer voxelBuffer;
    public int totalVoxels;

    void Awake()
    {
        Initialize();
    }

    void Initialize()
    {

        totalVoxels = resolutionX * resolutionY * resolutionZ;
        this.transform.position = volumeCenter;

        voxelBuffer = new ComputeBuffer(totalVoxels, sizeof(uint));

        if (clearOnStart)
            Clear();
    }

    public void Clear()
    {
        uint[] empty = new uint[totalVoxels];
        voxelBuffer.SetData(empty);
    }

    void OnDestroy()
    {
        if (voxelBuffer != null)
        {
            voxelBuffer.Release();
        }
    }

    void OnDrawGizmos()
	{
	    Gizmos.color = Color.green;
	    Gizmos.DrawWireCube(transform.position, volumeSize);
	}

}
