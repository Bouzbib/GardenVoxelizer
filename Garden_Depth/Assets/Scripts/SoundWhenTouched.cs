using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class SoundWhenTouched : MonoBehaviour
{
    public bool playOnlyOnce = true;

    private AudioSource audioData;
    private bool hasPlayed = false;
    public Vector3 volumeCenter, volumeSize;


    void Start()
    {
        audioData = GetComponent<AudioSource>();
        audioData.playOnAwake = false;

        volumeCenter = GameObject.Find("Voxelizer").GetComponent<InterlacedPackedVoxelizer>().volumeCenter;
        volumeSize = GameObject.Find("Voxelizer").GetComponent<InterlacedPackedVoxelizer>().volumeSize;

        Collider col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if((this.transform.position.x > (volumeCenter.x - volumeSize.x/2f)) && (this.transform.position.x < (volumeCenter.x + volumeSize.x/2f)))
        {
            if((this.transform.position.z > (volumeCenter.z - volumeSize.z/2f)) && (this.transform.position.z < (volumeCenter.z + volumeSize.z/2f)))
            {
                if((this.transform.position.y > (volumeCenter.y - volumeSize.y/2f)) && (this.transform.position.y < (volumeCenter.y + volumeSize.y/2f)))
                {
                    
                    if (other.CompareTag("InteractiveObject"))
                    {
                        if (playOnlyOnce && hasPlayed)
                            return;

                        audioData.Play();
                        hasPlayed = true;
                    }
                }
            }
        }
        
    }
}
