using UnityEngine;

public class ForceFullscreen : MonoBehaviour
{
    public bool debug;
    public int frameRate;
    void Awake()
    {


        Screen.fullScreenMode = FullScreenMode.ExclusiveFullScreen;
        Screen.SetResolution(
            Screen.currentResolution.width,
            Screen.currentResolution.height,
            FullScreenMode.ExclusiveFullScreen,
            Screen.currentResolution.refreshRate
        );

        Apply();
        
    }

    void OnValidate() => Apply();

    void Apply()
    {
        if (!Application.isPlaying) return;

        if (!debug)
        {
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1; // let vsync drive it
        }
        else
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = frameRate;
        }
    }


}