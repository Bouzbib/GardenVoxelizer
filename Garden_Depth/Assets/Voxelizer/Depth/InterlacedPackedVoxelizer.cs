using System.Collections.Generic;
using UnityEngine;
using System.Collections;

public class InterlacedPackedVoxelizer : MonoBehaviour
{

	public enum SliceAxis { X, Y, Z }

	[Header("Slicing Axis")]
	public SliceAxis sliceAxis = SliceAxis.Y;


    [Header("Volume")]
    public Vector3 volumeCenter = Vector3.zero;
    public Vector3 volumeSize = new Vector3(0.4f, 0.4f, 0.3f);
    public int nx = 256;
    public int ny = 256;
    public int nz = 192;

    [Header("Mode")]
    public bool interlaced = true;
    public bool sweepUpOnly = false;
    public bool sweepDownOnly = false;
    public bool rebuildEveryFrame = false;

    [Header("Projector Resolution")]
	public int projectorX = 912;
	public int projectorY = 1140;

    [Header("Debug Materials")]
	public Material voxelSliceDebugMat;
	public Material packedRgbDebugMat;
	public Shader voxelColorCaptureShader;

	[Header("Pack Debug")]
	public Material packBitDebugMat;
	public int debugChannel = 0;
	public int debugBit = 0;

	[Header("Slice Debug")]
	[Range(0, 1)] public float debugSlice01 = 0.0f;
	public bool autoSweepDebugSlice = false;
	public float debugSliceSpeed = 0.2f;

    [Header("Compute")]
    public ComputeShader voxelizeCompute;
    public ComputeShader clearVoxelCompute;
    public ComputeShader packCompute;

    [Header("Display")]
    public Material projectorPackedMat;   // NEW packed passthrough shader
    public Camera syncCamera;

    [Header("Clock")]
    public SquareVideoClock squareClock;  // used only for sineValue / direction

    [Header("Debug")]
    public bool logPackInfo = false;
    public int displayedPack = 0;
    public int nextPack = 0;
    public int displayedSliceBase = 0;
    public bool isMechanicalUp = true;

    [Header("Object discovery")]
    public bool autoFindVoxelizableObjects = true;

    private Camera depthCam;
    private RenderTexture voxelRT;
    private RenderTexture packedRT;
    private RenderTexture depthFrontRT;
    private RenderTexture depthBackRT;
    private RenderTexture projectorFrameCurrent;
	private int composeKernel;

    private int voxelKernel;
    private int clearKernel;
    private int packKernel;

    private readonly int slicesPerPack = 24;
    private int packCount;

    private int[] packToSliceOffset;
    private int[] listOrder;

    private bool volumeDirty = true;

    private List<VoxelizableObject> voxelObjects = new List<VoxelizableObject>();

    private SliceAxis lastSliceAxis;
	private Vector2Int lastPlaneRes;
	private int lastSliceCount;
	private RenderTexture colorRT;

	// public GameObject objToLoad;

    void Start()
    {
        if (autoFindVoxelizableObjects)
            voxelObjects = new List<VoxelizableObject>(FindObjectsOfType<VoxelizableObject>());
        else if (voxelObjects == null)
            voxelObjects = new List<VoxelizableObject>();

        lastSliceAxis = sliceAxis;
		lastPlaneRes = GetPlaneResolution();
		lastSliceCount = GetSliceCount();

        for (int i = 0; i < voxelObjects.Count; i++)
        {
            voxelObjects[i].objectID = i + 1;
            // voxelObjects[i].shellThickness = GetAxisWorldSize() / Mathf.Max(1, GetSliceCount()) * 1.2f;
            voxelObjects[i].shellThickness = GetAxisWorldSize() / Mathf.Max(1, GetSliceCount()) * 1.75f;
        }

        CreateDepthCamera();
        CreateDepthTextures();
        CreateColorTexture();
        CreateVoxelTexture();
        CreatePackedTexture();
        CreateProjectorFrameTexture();

        if (packBitDebugMat != null)
		{
		    packBitDebugMat.SetTexture("_MainTex", packedRT);   // or _PackedTex if your shader uses that name
		    packBitDebugMat.SetFloat("_Channel", debugChannel);
		    packBitDebugMat.SetFloat("_Bit", debugBit);         // or _BitIndex depending on shader
		}

        voxelKernel = voxelizeCompute.FindKernel("CSMain");
        clearKernel = clearVoxelCompute.FindKernel("Clear");
        packKernel = packCompute.FindKernel("Pack24Color");
        composeKernel = packCompute.FindKernel("ComposeCurrentProjectorFrame");

        packCount = Mathf.Max(1, GetSliceCount() / slicesPerPack);
        BuildPackLookup();

        if (squareClock != null)
            squareClock.packCount = packCount;

        if (projectorPackedMat != null)
            projectorPackedMat.SetTexture("_PackedTex", projectorFrameCurrent);

        nextPack = 0;
        displayedPack = 0;

        // StartCoroutine(LoadObjects());
    }

    void OnEnable()
    {
        Camera.onPostRender += OnCameraPostRender;
    }

    void OnDisable()
    {
        Camera.onPostRender -= OnCameraPostRender;
        ReleaseResources();
    }

    void LateUpdate()
	{
	    bool axisChanged =
	        lastSliceAxis != sliceAxis ||
	        lastPlaneRes != GetPlaneResolution() ||
	        lastSliceCount != GetSliceCount();

	    if (axisChanged)
	    {
	        RecreateAxisDependentTextures();
	        packCount = Mathf.Max(1, GetSliceCount() / slicesPerPack);
	        BuildPackLookup();
	        nextPack = 0;
	        displayedPack = 0;
	        volumeDirty = true;

	        lastSliceAxis = sliceAxis;
	        lastPlaneRes = GetPlaneResolution();
	        lastSliceCount = GetSliceCount();

	        if (squareClock != null)
	        {
	        	if(!squareClock.sinusIsMaster)
	        	{
	        		squareClock.packCount = packCount;
	        		squareClock.NotifyDisplayedPack(displayedPack);
	        	}
	            
	        }
	    }

	    BuildPackLookup();

	    if (squareClock != null)
	        isMechanicalUp = squareClock.sineValue > 0.0f;

	    if (rebuildEveryFrame)
	        volumeDirty = true;

	    if (volumeDirty)
	    {
	        RebuildVoxelVolume();
	        volumeDirty = false;
	    }

	    if (autoSweepDebugSlice)
	        debugSlice01 = Mathf.Repeat(Time.time * debugSliceSpeed, 1.0f);

	    if (voxelSliceDebugMat != null)
	    {
	        voxelSliceDebugMat.SetTexture("_VoxelTex", voxelRT);
	        voxelSliceDebugMat.SetFloat("_Slice01", debugSlice01);
	    }

	    if (packedRgbDebugMat != null)
	        packedRgbDebugMat.SetTexture("_MainTex", packedRT);

	    if (packBitDebugMat != null)
	    {
	        packBitDebugMat.SetTexture("_MainTex", packedRT);
	        packBitDebugMat.SetFloat("_Channel", debugChannel);
	        packBitDebugMat.SetFloat("_Bit", debugBit);
	    }
	}

    void OnCameraPostRender(Camera cam)
    {
        if (syncCamera != null && cam != syncCamera)
            return;

        if (syncCamera == null && !cam.CompareTag("MainCamera"))
            return;

        displayedPack = nextPack;
        displayedSliceBase = packToSliceOffset[displayedPack];

        ClearPackedRT();
        DispatchPack(displayedPack, isMechanicalUp);
		ComposeCurrentPackFrame(displayedPack);

        if (projectorPackedMat != null)
        {
            projectorPackedMat.SetTexture("_PackedTex", projectorFrameCurrent);
        }

        if (squareClock != null)
        {
            if(!squareClock.sinusIsMaster)
        	{
        		squareClock.currentPack = displayedPack;
				squareClock.globalSlice = displayedSliceBase;
				squareClock.NotifyDisplayedPack(displayedPack);
        	}
        	else
        	{                
                if(squareClock.VisualMasterPack == 0)
                {
                    displayedPack = 0;
                }
        	}
        }

        if (logPackInfo)
        {
            Debug.Log(
                $"DisplayedPack={displayedPack} SliceBase={displayedSliceBase} MechUp={isMechanicalUp}"
            );
        }

        nextPack++;
        if (nextPack >= packCount)
        {
            nextPack = 0;
            if (!rebuildEveryFrame)
                volumeDirty = true;
        }
    }

    public void MarkVolumeDirty()
    {
        volumeDirty = true;
    }

    void BuildPackLookup()
	{
	    packCount = Mathf.Max(1, GetSliceCount() / slicesPerPack);

	    if (packToSliceOffset == null || packToSliceOffset.Length != packCount)
	        packToSliceOffset = new int[packCount];

	    if (listOrder == null || listOrder.Length != packCount)
	        listOrder = new int[packCount];

	    if (interlaced)
	    {
	        int half = (packCount + 1) / 2;
	        for (int i = 0; i < half; i++)
	        {
	            int a = i;
	            int b = packCount - 1 - i;

	            packToSliceOffset[a] = slicesPerPack * i * 2;
	            listOrder[a] = i;

	            if (b != a)
	            {
	                packToSliceOffset[b] = slicesPerPack * i * 2 + 1;
	                listOrder[b] = i;
	            }
	        }
	    }
	    else
	    {
	        for (int i = 0; i < packCount; i++)
	        {
	            packToSliceOffset[i] = slicesPerPack * i;
	            listOrder[i] = i;
	        }
	    }
	}

    void RebuildVoxelVolume()
    {
        ClearVoxelVolume();

        foreach (var obj in voxelObjects)
        {
            if (obj == null || obj.objectRenderer == null)
                continue;

            RenderDepthObject(obj, back: false);
            RenderDepthObject(obj, back: true);
            RenderColorObject(obj);

            RunVoxelization(obj);
        }
    }

    void ClearVoxelVolume()
	{
	    Vector2Int plane = GetPlaneResolution();
	    int slices = GetSliceCount();

	    clearVoxelCompute.SetTexture(clearKernel, "VoxelColorVolume", voxelRT);
	    clearVoxelCompute.SetInts("VolumeResolution", plane.x, plane.y, slices);

	    clearVoxelCompute.Dispatch(
	        clearKernel,
	        Mathf.CeilToInt(plane.x / 8f),
	        Mathf.CeilToInt(plane.y / 8f),
	        Mathf.CeilToInt(slices / 4f)
	    );
	}

    void DispatchPack(int packIndex, bool mechUp)
	{
	    Vector2Int plane = GetPlaneResolution();
	    int sliceOffset = packToSliceOffset[packIndex];

	    packCompute.SetTexture(packKernel, "VoxelColorVolume", voxelRT);
	    packCompute.SetTexture(packKernel, "PackedTex", packedRT);

	    packCompute.SetInt("SliceOffset", sliceOffset);
	    packCompute.SetInt("VolumeDepth", GetSliceCount());

	    packCompute.SetInt("Interlaced", interlaced ? 1 : 0);
	    packCompute.SetInt("SweepDir", mechUp ? 1 : 0);
	    packCompute.SetInt("SweepUpOnly", sweepUpOnly ? 1 : 0);
	    packCompute.SetInt("SweepDownOnly", sweepDownOnly ? 1 : 0);
	    packCompute.SetFloat("ColorThreshold", 0.5f);

	    packCompute.Dispatch(
	        packKernel,
	        Mathf.CeilToInt(plane.x / 8f),
	        Mathf.CeilToInt(plane.y / 8f),
	        1
	    );
	}

    void ClearPackedRT()
    {
        RenderTexture active = RenderTexture.active;
        RenderTexture.active = packedRT;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = active;
    }

    void CreateDepthCamera()
    {
        GameObject go = new GameObject("Voxel Depth Camera");
        go.hideFlags = HideFlags.HideAndDontSave;

        depthCam = go.AddComponent<Camera>();
        depthCam.enabled = false;
        depthCam.clearFlags = CameraClearFlags.SolidColor;
        depthCam.backgroundColor = Color.black;
        depthCam.cullingMask = LayerMask.GetMask("Voxelize");
        depthCam.useOcclusionCulling = false;
    }

    void CreateDepthTextures()
	{
	    Vector2Int r = GetPlaneResolution();

	    depthFrontRT = new RenderTexture(r.x, r.y, 0, RenderTextureFormat.RFloat);
	    depthFrontRT.filterMode = FilterMode.Point;
	    depthFrontRT.wrapMode = TextureWrapMode.Clamp;
	    depthFrontRT.Create();

	    depthBackRT = new RenderTexture(r.x, r.y, 0, RenderTextureFormat.RFloat);
	    depthBackRT.filterMode = FilterMode.Point;
	    depthBackRT.wrapMode = TextureWrapMode.Clamp;
	    depthBackRT.Create();
	}

    void CreateVoxelTexture()
	{
	    Vector2Int plane = GetPlaneResolution();
	    int slices = GetSliceCount();

	    voxelRT = new RenderTexture(plane.x, plane.y, 0, RenderTextureFormat.ARGBHalf);
	    voxelRT.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
	    voxelRT.volumeDepth = slices;
	    voxelRT.enableRandomWrite = true;
	    voxelRT.filterMode = FilterMode.Point;
	    voxelRT.wrapMode = TextureWrapMode.Clamp;
	    voxelRT.Create();
	}

    void CreatePackedTexture()
	{
	    Vector2Int plane = GetPlaneResolution();

	    packedRT = new RenderTexture(plane.x, plane.y, 0, RenderTextureFormat.ARGBFloat);
	    packedRT.enableRandomWrite = true;
	    packedRT.filterMode = FilterMode.Point;
	    packedRT.wrapMode = TextureWrapMode.Clamp;
	    packedRT.Create();
	}

    void CreateProjectorFrameTexture()
	{
	    projectorFrameCurrent = new RenderTexture(projectorX, projectorY, 0, RenderTextureFormat.ARGB32);
	    projectorFrameCurrent.enableRandomWrite = true;
	    projectorFrameCurrent.filterMode = FilterMode.Point;
	    projectorFrameCurrent.wrapMode = TextureWrapMode.Clamp;
	    projectorFrameCurrent.Create();
	}

    void RenderDepthObject(VoxelizableObject obj, bool back)
	{
	    Vector3 axis = GetAxisVector();
	    Vector3 up = GetAxisUp();
	    Vector2 planeSize = GetPlaneWorldSize();
	    float axisSize = GetAxisWorldSize();

	    depthCam.orthographic = true;
	    depthCam.orthographicSize = planeSize.y * 0.5f;
	    depthCam.aspect = planeSize.x / Mathf.Max(1e-6f, planeSize.y);

	    float margin = axisSize * 0.1f;
	    Vector3 camPos = volumeCenter - axis * (axisSize * 0.5f + margin);

	    depthCam.transform.position = camPos;
	    depthCam.transform.rotation = Quaternion.LookRotation(axis, up);

	    depthCam.nearClipPlane = 0.001f;
	    depthCam.farClipPlane = axisSize + 2f * margin;
	    depthCam.cullingMask = LayerMask.GetMask("Voxelize");
	    depthCam.targetTexture = back ? depthBackRT : depthFrontRT;

	    foreach (var o in voxelObjects)
	    {
	        if (o != null && o.objectRenderer != null)
	        {
	            o.objectRenderer.enabled = false;
	            o.objectRenderer.localBounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
	        }
	    }

	    obj.objectRenderer.enabled = true;

	    Shader depthShader = Shader.Find(back ? "Hidden/DepthOnlyLinearBack_depth" : "Hidden/DepthOnlyLinear_depth");

	    Vector3 volumeMin = volumeCenter - volumeSize * 0.5f;
	    Shader.SetGlobalVector("_VolumeMinWS", volumeMin);
	    Shader.SetGlobalVector("_VolumeSizeWS", volumeSize);
	    Shader.SetGlobalInt("_SliceAxis", (int)sliceAxis);

	    depthCam.RenderWithShader(depthShader, "");

	    foreach (var o in voxelObjects)
	    {
	        if (o != null && o.objectRenderer != null)
	            o.objectRenderer.enabled = true;
	    }
	}

    void ComposeCurrentPackFrame(int packIndex)
	{
	    Vector2Int plane = GetPlaneResolution();
		packCompute.SetInt("voxelResX", plane.x);
		packCompute.SetInt("voxelResY", plane.y);
		packCompute.SetInt("voxelResZ", GetSliceCount());

	    packCompute.SetInt("projResX", projectorX);
	    packCompute.SetInt("projResY", projectorY);
	    packCompute.SetInt("currentComposePack", packIndex);
	    packCompute.SetInt("packCount", packCount);
	    packCompute.SetInt("sweepUpOnly", sweepUpOnly ? 1 : 0);
	    packCompute.SetInt("sweepDownOnly", sweepDownOnly ? 1 : 0);
	    packCompute.SetInt("interlaceMode", interlaced ? 1 : 0);

	    packCompute.SetTexture(composeKernel, "PackedTex", packedRT);
	    packCompute.SetTexture(composeKernel, "projectorFrameCurrent", projectorFrameCurrent);

	    packCompute.Dispatch(
	        composeKernel,
	        Mathf.CeilToInt(projectorX / 8f),
	        Mathf.CeilToInt(projectorY / 8f),
	        1
	    );
	}

    void RunVoxelization(VoxelizableObject obj)
	{
	    Vector3 min = volumeCenter - volumeSize * 0.5f;
	    Vector2Int plane = GetPlaneResolution();
	    int slices = GetSliceCount();

	    voxelizeCompute.SetTexture(voxelKernel, "VoxelColorVolume", voxelRT);
	    voxelizeCompute.SetTexture(voxelKernel, "FrontDepthTex", depthFrontRT);
	    voxelizeCompute.SetTexture(voxelKernel, "BackDepthTex", depthBackRT);
	    voxelizeCompute.SetTexture(voxelKernel, "SurfaceColorTex", colorRT);

	    voxelizeCompute.SetVector("VolumeMin", min);
	    voxelizeCompute.SetVector("VolumeSize", volumeSize);
	    voxelizeCompute.SetInts("VolumeResolution", plane.x, plane.y, slices);
	    voxelizeCompute.SetInt("SliceAxis", (int)sliceAxis);
	    voxelizeCompute.SetInt("VoxelMode", (int)obj.voxelType);

	    float shellVoxelSize = GetAxisWorldSize() / Mathf.Max(1, slices);
	    voxelizeCompute.SetFloat("ShellThickness", Mathf.Max(obj.shellThickness, shellVoxelSize * 0.75f));
	    voxelizeCompute.SetInt("WriteNeighborSlices", 0);

	    // Fallback only
	    Color fallback = Color.white;

	    if (obj.objectRenderer != null)
	    {
	        Material m = obj.objectRenderer.sharedMaterial;
	        if (m != null)
	        {
	            if (m.HasProperty("_BaseColor")) fallback = m.GetColor("_BaseColor");
	            else if (m.HasProperty("_Color")) fallback = m.GetColor("_Color");
	        }
	    }

	    voxelizeCompute.SetVector("ObjectColor", new Vector4(fallback.r, fallback.g, fallback.b, 1f));

	    // Lower threshold helps prevent unnecessary fallback on dark textured areas
	    voxelizeCompute.SetFloat("ColorWriteThreshold", 0.001f);

	    voxelizeCompute.Dispatch(
	        voxelKernel,
	        Mathf.CeilToInt(plane.x / 8f),
	        Mathf.CeilToInt(plane.y / 8f),
	        Mathf.CeilToInt(slices / 4f)
	    );
	}

    void ReleaseResources()
    {
        if (depthCam != null)
            DestroyImmediate(depthCam.gameObject);

        if (voxelRT != null) voxelRT.Release();
        if (packedRT != null) packedRT.Release();
        if (depthFrontRT != null) depthFrontRT.Release();
        if (depthBackRT != null) depthBackRT.Release();
        if (colorRT != null) colorRT.Release();
        if (projectorFrameCurrent != null) projectorFrameCurrent.Release();
    }

    Vector3 GetAxisVector()
	{
	    switch (sliceAxis)
	    {
	        case SliceAxis.X: return Vector3.right;
	        case SliceAxis.Y: return Vector3.up;
	        default: return Vector3.forward;
	    }
	}

	Vector3 GetAxisUp()
	{
	    // choose a stable up vector not parallel to forward
	    switch (sliceAxis)
	    {
	        case SliceAxis.X: return Vector3.up;
	        case SliceAxis.Y: return Vector3.forward;
	        default: return Vector3.up;
	    }
	}

	int GetSliceCount()
	{
	    // switch (sliceAxis)
	    // {
	    //     case SliceAxis.X: return nx;
	    //     case SliceAxis.Y: return ny;
	    //     default: return nz;
	    // }
	    return nz;
	}

	Vector2Int GetPlaneResolution()
	{
	    // switch (sliceAxis)
	    // {
	    //     case SliceAxis.X: return new Vector2Int(nz, ny); // plane = ZY
	    //     case SliceAxis.Y: return new Vector2Int(nx, nz); // plane = XZ
	    //     default: return new Vector2Int(nx, ny);          // plane = XY
	    // }
	    return new Vector2Int(nx, ny);
	}

	Vector2 GetPlaneWorldSize()
	{
	    // switch (sliceAxis)
	    // {
	    //     case SliceAxis.X: return new Vector2(volumeSize.z, volumeSize.y);
	    //     case SliceAxis.Y: return new Vector2(volumeSize.x, volumeSize.z);
	    //     default: return new Vector2(volumeSize.x, volumeSize.y);
	    // }
	    return new Vector2(volumeSize.x, volumeSize.y);
	}

	float GetAxisWorldSize()
	{
	    switch (sliceAxis)
	    {
	        case SliceAxis.X: return volumeSize.x;
	        case SliceAxis.Y: return volumeSize.y;
	        default: return volumeSize.z;
	    }
	}

	float GetAxisMin()
	{
	    switch (sliceAxis)
	    {
	        case SliceAxis.X: return volumeCenter.x - volumeSize.x * 0.5f;
	        case SliceAxis.Y: return volumeCenter.y - volumeSize.y * 0.5f;
	        default: return volumeCenter.z - volumeSize.z * 0.5f;
	    }
	}

	void RecreateAxisDependentTextures()
	{
	    if (depthFrontRT != null) depthFrontRT.Release();
	    if (depthBackRT != null) depthBackRT.Release();
	    if (voxelRT != null) voxelRT.Release();
	    if (packedRT != null) packedRT.Release();
	    if (colorRT != null) colorRT.Release();
		

	    CreateDepthTextures();
	    CreateColorTexture();
	    CreateVoxelTexture();
	    CreatePackedTexture();

	    if (projectorPackedMat != null)
	        projectorPackedMat.SetTexture("_PackedTex", projectorFrameCurrent);

	    
	}

	void CreateColorTexture()
	{
	    Vector2Int r = GetPlaneResolution();

	    colorRT = new RenderTexture(r.x, r.y, 0, RenderTextureFormat.ARGB32);
	    colorRT.enableRandomWrite = false;
	    colorRT.filterMode = FilterMode.Point;
	    colorRT.wrapMode = TextureWrapMode.Clamp;
	    colorRT.Create();
	}

	void RenderColorObject(VoxelizableObject obj)
	{
	    Vector3 axis = GetAxisVector();
	    Vector3 up = GetAxisUp();
	    Vector2 planeSize = GetPlaneWorldSize();
	    float axisSize = GetAxisWorldSize();

	    depthCam.orthographic = true;
	    depthCam.orthographicSize = planeSize.y * 0.5f;
	    depthCam.aspect = planeSize.x / Mathf.Max(1e-6f, planeSize.y);

	    float margin = axisSize * 0.1f;
	    Vector3 camPos = volumeCenter - axis * (axisSize * 0.5f + margin);

	    depthCam.transform.position = camPos;
	    depthCam.transform.rotation = Quaternion.LookRotation(axis, up);

	    depthCam.nearClipPlane = 0.001f;
	    depthCam.farClipPlane = axisSize + 2f * margin;
	    depthCam.cullingMask = LayerMask.GetMask("Voxelize");
	    depthCam.targetTexture = colorRT;
	    depthCam.clearFlags = CameraClearFlags.SolidColor;
	    depthCam.backgroundColor = Color.black;

	    foreach (var o in voxelObjects)
	    {
	        if (o != null && o.objectRenderer != null)
	            o.objectRenderer.enabled = false;
	    }
	    // render only the wanted object
	    obj.objectRenderer.enabled = true;

	    depthCam.RenderWithShader(voxelColorCaptureShader, "");

	    // restore the others
	    foreach (var o in voxelObjects)
	    {
	        if (o != null && o.objectRenderer != null)
	            o.objectRenderer.enabled = true;
	    }
	}

    void OnGUI()
    {
  //       GUI.matrix = Matrix4x4.Scale(Vector3.one * 2f);

  //       GUI.Label(
  //           new Rect(20, 20, 900, 40),
  //           $"DisplayedPack={displayedPack}/{packCount - 1} | NextPack={nextPack} | SliceBase={displayedSliceBase} | MechUp={isMechanicalUp} | Interlaced={interlaced}"
  //       );

  //       int sliceIndex = Mathf.Clamp(Mathf.RoundToInt(debugSlice01 * (nz - 1)), 0, nz - 1);

		// GUI.Label(
		//     new Rect(20, 60, 1000, 40),
		//     $"DebugSlice01={debugSlice01:F3}  SliceIndex={sliceIndex}/{nz - 1}  DisplayedPack={displayedPack}  SliceBase={displayedSliceBase}  MechUp={isMechanicalUp}"
		// );
		GUI.matrix = Matrix4x4.Scale(Vector3.one * 2f);

        float fps = 1f / Time.unscaledDeltaTime;
        GUI.Label(
            new Rect(20, 20, 600, 60),
            $"FPS: {fps:F1}   dt: {Time.unscaledDeltaTime * 1000f:F2} ms  Offset{squareClock.offsetPhase}"
        );
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(volumeCenter, volumeSize);
    }

    void OnDrawGizmosSelected()
	{
	    if (!Application.isPlaying || packCount <= 0)
	        return;

	    Vector3 axis = GetAxisVector();
	    float axisSize = GetAxisWorldSize();
	    float axisMin = GetAxisMin();
	    int sliceCount = GetSliceCount();

	    float sliceStep = axisSize / Mathf.Max(1, sliceCount);

	    float packStart = axisMin + packToSliceOffset[Mathf.Clamp(displayedPack, 0, packCount - 1)] * sliceStep;
	    float packSpan = interlaced ? (2f * slicesPerPack * sliceStep) : (slicesPerPack * sliceStep);
	    float packEnd = packStart + packSpan;

	    float packCenterPos = 0.5f * (packStart + packEnd);

	    Vector3 packCenter = volumeCenter;
	    float currentAxisCenter =
	        (sliceAxis == SliceAxis.X) ? volumeCenter.x :
	        (sliceAxis == SliceAxis.Y) ? volumeCenter.y :
	                                     volumeCenter.z;

	    packCenter += axis * (packCenterPos - currentAxisCenter);

	    Vector3 packSize = volumeSize;
	    if (sliceAxis == SliceAxis.X) packSize.x = packEnd - packStart;
	    if (sliceAxis == SliceAxis.Y) packSize.y = packEnd - packStart;
	    if (sliceAxis == SliceAxis.Z) packSize.z = packEnd - packStart;

	    Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
	    Gizmos.DrawWireCube(packCenter, packSize);

	    int debugSliceIndex = Mathf.Clamp(Mathf.RoundToInt(debugSlice01 * (sliceCount - 1)), 0, sliceCount - 1);
	    float debugSlicePos = axisMin + (debugSliceIndex + 0.5f) * sliceStep;

	    Vector3 sliceCenter = volumeCenter;
	    sliceCenter += axis * (debugSlicePos - currentAxisCenter);

	    Vector3 sliceSize = volumeSize;
	    if (sliceAxis == SliceAxis.X) sliceSize.x = 0.001f;
	    if (sliceAxis == SliceAxis.Y) sliceSize.y = 0.001f;
	    if (sliceAxis == SliceAxis.Z) sliceSize.z = 0.001f;

	    Gizmos.color = Color.green; 
	    Gizmos.DrawWireCube(sliceCenter, sliceSize);

	    Gizmos.color = Color.cyan;
	    Gizmos.DrawWireCube(volumeCenter, volumeSize);
	}

	// IEnumerator LoadObjects()
	// {
	// 	yield return new WaitForSeconds(1.0f);
	// 	objToLoad.SetActive(true);
	// }

    public RenderTexture GetPackedTexture() => packedRT;
    public RenderTexture GetVoxelTexture() => voxelRT;
}