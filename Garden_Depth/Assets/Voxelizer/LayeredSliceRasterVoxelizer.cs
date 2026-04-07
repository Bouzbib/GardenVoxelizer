using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class LayeredSliceRasterVoxelizer : MonoBehaviour
{
    public enum SliceAxis { X, Y, Z }

    [Header("Slicing Axis")]
    public SliceAxis sliceAxis = SliceAxis.Y;

    [Header("Scene / volume")]
    public VoxelVolume volume;
    public LayerMask voxelizableLayers = ~0;

    [Header("Raster + packing")]
    public Shader sliceRasterShader;
    public ComputeShader packingCompute;
    public Renderer projectorQuadRenderer;
    public Material packDebugMat;
    public SquareVideoClock squareClock;
    public Camera syncCamera;

    // int lastFramePublished = -1;

    [Header("Resolution")]
    public int internalX = 320;
    public int internalY = 320;
    public int internalZ = 192;
    public int projectorX = 912;
    public int projectorY = 1140;

    [Header("Build")]
    public bool rebuildEveryFrame = false;
    public bool autoRenderOnEnable = true;

    [Header("Slice shell")]
    [Tooltip("World-space thickness of the rendered slice slab.")]
    public float sliceThicknessWorld = 0.0675f;
    public Color fallbackColor = Color.white;

    [Header("Sweep gating")]
    public bool sweepUpOnly = false;
    public bool sweepDownOnly = false;
    public bool interlaceMode = true;

    const int PACK_SLICES = 24;

    [Header("Slice Debug")]
    public bool debugSlices = true;
    [Range(0, 191)] public int debugSliceIndex = 0;
    public bool debugHollowSlices = false;
    public Material sliceDebugMat;
    [Range(0, 7)] public int debugPack;

    [Header("Culling")]
    [Tooltip("Ignore voxelizable objects whose bounds are farther than this margin outside the voxel volume.")]
    public float outsideVolumeMargin = 0.15f;

    [Tooltip("If true, objects too far outside the voxel volume are ignored entirely.")]
    public bool ignoreObjectsOutsideVolume = true;

    [Header("Auto Setup")]
    public bool autoAddVoxelizableOnVoxelizeLayer = true;
    public string voxelizeLayerName = "Voxelize";


    // [Header("Interactive Resolution")]
    // // public bool useLowerResolutionWhileDirty = false;
    // public int interactiveX = 320;
    // public int interactiveY = 320;
    // public int interactiveZ = 192;
    // // public float settleDelaySeconds = 0.01f;

    readonly HashSet<int> seenRendererIds = new HashSet<int>(256);
    readonly List<int> removedRendererIds = new List<int>(256);
    readonly Dictionary<int, Renderer> trackedRenderers = new Dictionary<int, Renderer>(256);

    public static readonly HashSet<VoxelizableObject> ActiveVoxelObjects = new HashSet<VoxelizableObject>();
    public static LayeredSliceRasterVoxelizer Instance;


    bool rebuildQueued = false;
    // bool usingInteractiveResolution = false;
    float nextRebuildTime = -1f;
    float lastDirtyTime = -1f;

    int fullResX, fullResY, fullResZ;

    int packCount;
    int kClearPackedIntermediateA, kClearPackedIntermediateB, kClearPackedIntermediateC;
    int kClearHollow3D;
    int kPackSolidSlicesToSurfacePacks;
    int kSeedHollowFromSlices;
    int kDilateHollowStep;
    int kPackHollowToPacks;
    int kSolidFillColumns;
    int kCombineFinal;
    int kCompose;
    int kComposeCurrent;

    Camera sliceCamera;
    MaterialPropertyBlock mpb;

    RenderTexture tempSlice;
    RenderTexture solidSliceArray;
    RenderTexture hollowSliceArray;

    RenderTexture packsSolidSurfaceR;
    RenderTexture packsSolidSurfaceG;
    RenderTexture packsSolidSurfaceB;
    RenderTexture packsSolidFilledR;
    RenderTexture packsSolidFilledG;
    RenderTexture packsSolidFilledB;
    RenderTexture packsHollowPackedR;
    RenderTexture packsHollowPackedG;
    RenderTexture packsHollowPackedB;
    RenderTexture hollowStepsRT;
    RenderTexture hollowOccRRT;
    RenderTexture hollowOccGRT;
    RenderTexture hollowOccBRT;
    RenderTexture voxelPacksR;
    RenderTexture voxelPacksG;
    RenderTexture voxelPacksB;

    RenderTexture projectorFrames;
    RenderTexture projectorFrameCurrent;
    RenderTexture debugFrameCurrent;

    public int[] packToSliceOffset;
    int pack;

    int isMechanicalUp;

    readonly Dictionary<int, Bounds> lastRendererBounds = new Dictionary<int, Bounds>(256);

    bool hasDirtySliceRange = false;
    int dirtySliceMin = int.MaxValue;
    int dirtySliceMax = int.MinValue;

    readonly Dictionary<int, Matrix4x4> lastTransforms = new Dictionary<int, Matrix4x4>(256);
    readonly List<VoxelizableObject> voxelObjects = new List<VoxelizableObject>(256);

    int maxThicknessThisFrame = 1;

    int lastComposedProjectorPack = -1;
    int lastComposedDebugPack = -1;
    bool projectorFrameDirty = true;
    bool debugFrameDirty = true;
    // int[] mechToDisplayPack = { 0, 1, 2, 3, 4, 5, 6, 7 };
    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        fullResX = internalX;
        fullResY = internalY;
        fullResZ = internalZ;

        Initialize();
        if (autoRenderOnEnable)
            RebuildNow(true);
    }

    void OnEnable()
    {
        Initialize();
        if (autoRenderOnEnable)
            RebuildNow(true);

        Camera.onPostRender += HandlePostRender;
    }

    void OnDisable()
    {
        Camera.onPostRender -= HandlePostRender;
    }

    void Initialize()
    {
        if (volume == null)
            volume = GetComponent<VoxelVolume>();
        if (volume == null)
            return;

        packCount = Mathf.Max(1, (internalZ + (PACK_SLICES - 1)) / PACK_SLICES);
        packToSliceOffset = new int[packCount];

        for(int i = 0; i < packCount/2; i++)
        {
            if(!interlaceMode)
            {
                packToSliceOffset[i] = 24*i;
                packToSliceOffset[packCount-1 - i] = 24*(packCount-1 - i);
            }
            else
            {
                packToSliceOffset[i] = 24*i*2;
                packToSliceOffset[packCount-1-i] =  24*i*2+1;

            }
        }

        if (packingCompute != null)
        {
            kClearPackedIntermediateA = packingCompute.FindKernel("ClearPackedIntermediateA");
            kClearPackedIntermediateB = packingCompute.FindKernel("ClearPackedIntermediateB");
            kClearPackedIntermediateC = packingCompute.FindKernel("ClearPackedIntermediateC");

            kClearHollow3D = packingCompute.FindKernel("ClearHollow3D");
            kPackSolidSlicesToSurfacePacks = packingCompute.FindKernel("PackSolidSlicesToSurfacePacks");
            kSeedHollowFromSlices = packingCompute.FindKernel("SeedHollowFromSlices");
            kDilateHollowStep = packingCompute.FindKernel("DilateHollowStep");
            kPackHollowToPacks = packingCompute.FindKernel("PackHollowToPacks");
            kSolidFillColumns = packingCompute.FindKernel("SolidFillColumns");
            kCombineFinal = packingCompute.FindKernel("CombineFinal");
            kCompose = packingCompute.FindKernel("ComposeAllProjectorFrames");
            kComposeCurrent = packingCompute.FindKernel("ComposeCurrentProjectorFrame");
        }

        if (mpb == null)
            mpb = new MaterialPropertyBlock();

        AutoAddVoxelizableComponents();
        EnsureCamera();
        EnsureTextures();

        if (squareClock != null)
            squareClock.packCount = packCount;
    }

     // =========================
    // AXIS HELPERS
    // =========================

    Vector3 GetAxisVector()
    {
        switch (sliceAxis)
        {
            case SliceAxis.X: return Vector3.right;
            case SliceAxis.Y: return Vector3.up;
            default: return Vector3.forward;
        }
    }

    float GetAxisValue(Vector3 v)
    {
        switch (sliceAxis)
        {
            case SliceAxis.X: return v.x;
            case SliceAxis.Y: return v.y;
            default: return v.z;
        }
    }

    Vector2 GetPlaneSize(Vector3 size)
    {
        switch (sliceAxis)
        {
            case SliceAxis.X: return new Vector2(size.z, size.y);
            case SliceAxis.Y: return new Vector2(size.x, size.z);
            default: return new Vector2(size.x, size.y);
        }
    }

    void HandlePostRender(Camera cam)
    {
        if (cam != syncCamera) return;

        int packToShow = pack;

        if (squareClock != null)
        {
            if (squareClock.sinusIsMaster)
            {
                // pack = mechToDisplayPack[Mathf.Clamp(squareClock.VisualMasterPack, 0, packCount - 1)];
                // pack = Mathf.Clamp(squareClock.VisualMasterPack, 0, packCount - 1);
                // pack = squareClock.VisualMasterPack;

                pack = (pack + 1) % packCount;
                packToShow = pack;
                
                if(squareClock.VisualMasterPack == 0)
                {
                    pack = 0;
                }
                
                // squareClock.NotifyDisplayedPack(packToShow);
            }
            else
            {
                pack = (pack + 1) % packCount;
                packToShow = pack;
                squareClock.currentPack = packToShow;
                squareClock.NotifyDisplayedPack(packToShow);
            }
        }

        // bool use2DPath = (packDebugMat != null && packDebugMat.name == "debugMaterial_2D");

        // if (use2DPath)
        // {
        if (packToShow != lastComposedProjectorPack) //
        {
            ComposeCurrentPackFrame(packToShow, projectorFrameCurrent);
            lastComposedProjectorPack = packToShow;
            projectorFrameDirty = false;
        }

        if (packDebugMat != null && (debugFrameDirty || debugPack != lastComposedDebugPack))
        {
            ComposeCurrentPackFrame(debugPack, debugFrameCurrent);
            lastComposedDebugPack = debugPack;
            debugFrameDirty = false;
        }

        if (projectorQuadRenderer != null && projectorQuadRenderer.sharedMaterial != null)
        {
            projectorQuadRenderer.sharedMaterial.SetTexture("_MainTex", projectorFrameCurrent);
        }

        if (packDebugMat != null)
        {
            packDebugMat.SetTexture("_MainTex", debugFrameCurrent);
        }
        // }
        // else
        // {
        //     if (projectorQuadRenderer != null && projectorQuadRenderer.sharedMaterial != null)
        //     {
        //         projectorQuadRenderer.sharedMaterial.SetInt("_PackIndex", packToShow);
        //         projectorQuadRenderer.sharedMaterial.SetTexture("_Frames", projectorFrames);
        //     }

        //     if (packDebugMat != null)
        //     {
        //         packDebugMat.SetInt("_PackIndex", debugPack);
        //         packDebugMat.SetTexture("_Frames", projectorFrames);
        //     }
        // }
        
    }


    void EnsureCamera()
    {
        if (sliceCamera != null)
            return;

        var go = new GameObject("__LayeredSliceRasterCamera");
        go.hideFlags = HideFlags.HideAndDontSave;
        go.transform.SetParent(transform, false);
        sliceCamera = go.AddComponent<Camera>();
        sliceCamera.enabled = false;
        sliceCamera.orthographic = true;
        sliceCamera.clearFlags = CameraClearFlags.SolidColor;
        sliceCamera.backgroundColor = Color.black;
        sliceCamera.allowHDR = false;
        sliceCamera.allowMSAA = false;
        sliceCamera.forceIntoRenderTexture = true;
        sliceCamera.cullingMask = voxelizableLayers;
        sliceCamera.nearClipPlane = 0.01f;
        sliceCamera.farClipPlane = 100f;
    }

    void EnsureTextures()
    {
        tempSlice = EnsureRT2D(tempSlice, internalX, internalY, RenderTextureFormat.ARGB32, "TempSlice");
        solidSliceArray = EnsureRT2DArray(solidSliceArray, internalX, internalY, internalZ, RenderTextureFormat.ARGB32, "SolidSliceArray");
        hollowSliceArray = EnsureRT2DArray(hollowSliceArray, internalX, internalY, internalZ, RenderTextureFormat.ARGB32, "HollowSliceArray");

        packsSolidSurfaceR = EnsureRT3D(packsSolidSurfaceR, internalX, internalY, packCount, RenderTextureFormat.RInt, "packsSolidSurfaceR");
        packsSolidSurfaceG = EnsureRT3D(packsSolidSurfaceG, internalX, internalY, packCount, RenderTextureFormat.RInt, "packsSolidSurfaceG");
        packsSolidSurfaceB = EnsureRT3D(packsSolidSurfaceB, internalX, internalY, packCount, RenderTextureFormat.RInt, "packsSolidSurfaceB");

        packsSolidFilledR = EnsureRT3D(packsSolidFilledR, internalX, internalY, packCount, RenderTextureFormat.RInt, "packsSolidFilledR");
        packsSolidFilledG = EnsureRT3D(packsSolidFilledG, internalX, internalY, packCount, RenderTextureFormat.RInt, "packsSolidFilledG");
        packsSolidFilledB = EnsureRT3D(packsSolidFilledB, internalX, internalY, packCount, RenderTextureFormat.RInt, "packsSolidFilledB");

        packsHollowPackedR = EnsureRT3D(packsHollowPackedR, internalX, internalY, packCount, RenderTextureFormat.RInt, "packsHollowPackedR");
        packsHollowPackedG = EnsureRT3D(packsHollowPackedG, internalX, internalY, packCount, RenderTextureFormat.RInt, "packsHollowPackedG");
        packsHollowPackedB = EnsureRT3D(packsHollowPackedB, internalX, internalY, packCount, RenderTextureFormat.RInt, "packsHollowPackedB");

        hollowStepsRT = EnsureRT3D(hollowStepsRT, internalX, internalY, internalZ, RenderTextureFormat.RInt, "hollowSteps");
        hollowOccRRT = EnsureRT3D(hollowOccRRT, internalX, internalY, internalZ, RenderTextureFormat.RInt, "hollowOccR");
        hollowOccGRT = EnsureRT3D(hollowOccGRT, internalX, internalY, internalZ, RenderTextureFormat.RInt, "hollowOccG");
        hollowOccBRT = EnsureRT3D(hollowOccBRT, internalX, internalY, internalZ, RenderTextureFormat.RInt, "hollowOccB");

        voxelPacksR = EnsureRT3D(voxelPacksR, internalX, internalY, packCount, RenderTextureFormat.RInt, "VoxelPacksR");
        voxelPacksG = EnsureRT3D(voxelPacksG, internalX, internalY, packCount, RenderTextureFormat.RInt, "VoxelPacksG");
        voxelPacksB = EnsureRT3D(voxelPacksB, internalX, internalY, packCount, RenderTextureFormat.RInt, "VoxelPacksB");

        projectorFrames = EnsureRT2DArray(projectorFrames, projectorX, projectorY, packCount, RenderTextureFormat.ARGB32, "ProjectorFrames");
        projectorFrameCurrent = EnsureRT2D_UAV(projectorFrameCurrent, projectorX, projectorY, RenderTextureFormat.ARGB32, "ProjectorFrameCurrent");
        debugFrameCurrent = EnsureRT2D_UAV(debugFrameCurrent, projectorX, projectorY, RenderTextureFormat.ARGB32, "DebugFrameCurrent");
    }

    RenderTexture EnsureRT2D(RenderTexture rt, int w, int h, RenderTextureFormat fmt, string name)
    {
        if (rt != null && rt.width == w && rt.height == h && rt.format == fmt && rt.dimension == UnityEngine.Rendering.TextureDimension.Tex2D)
            return rt;
        if (rt != null) rt.Release();

        rt = new RenderTexture(w, h, 0, fmt)
        {
            name = name,
            enableRandomWrite = false,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false,
            dimension = UnityEngine.Rendering.TextureDimension.Tex2D
        };
        rt.Create();
        return rt;
    }

    RenderTexture EnsureRT2D_UAV(RenderTexture rt, int w, int h, RenderTextureFormat fmt, string name)
    {
        if (rt != null && rt.width == w && rt.height == h && rt.format == fmt &&
            rt.dimension == UnityEngine.Rendering.TextureDimension.Tex2D)
            return rt;

        if (rt != null) rt.Release();

        rt = new RenderTexture(w, h, 0, fmt)
        {
            name = name,
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false,
            dimension = UnityEngine.Rendering.TextureDimension.Tex2D
        };
        rt.Create();
        return rt;
    }

    RenderTexture EnsureRT2DArray(RenderTexture rt, int w, int h, int d, RenderTextureFormat fmt, string name)
    {
        if (rt != null && rt.width == w && rt.height == h && rt.volumeDepth == d && rt.format == fmt && rt.dimension == UnityEngine.Rendering.TextureDimension.Tex2DArray)
            return rt;
        if (rt != null) rt.Release();
        rt = new RenderTexture(w, h, 0, fmt)
        {
            name = name,
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false,
            dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
            volumeDepth = d
        };
        rt.Create();
        return rt;
    }

    RenderTexture EnsureRT3D(RenderTexture rt, int w, int h, int d, RenderTextureFormat fmt, string name)
    {
        if (rt != null && rt.width == w && rt.height == h && rt.volumeDepth == d && rt.format == fmt && rt.dimension == UnityEngine.Rendering.TextureDimension.Tex3D)
            return rt;
        if (rt != null) rt.Release();
        rt = new RenderTexture(w, h, 0, fmt)
        {
            name = name,
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false,
            dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
            volumeDepth = d
        };
        rt.Create();
        return rt;
    }

    void Update()
    {
        // if (Time.frameCount == lastFramePublished) return;
        // lastFramePublished = Time.frameCount;

        isMechanicalUp = squareClock.sineValue > 0 ? 1 : 0;
        if (Input.GetKeyDown(KeyCode.X))
        {
            sliceAxis = SliceAxis.X;
            RebuildNow();
        }
        if (Input.GetKeyDown(KeyCode.Y))
        {
            sliceAxis = SliceAxis.Y;
            RebuildNow();
        }
        if (Input.GetKeyDown(KeyCode.Z))
        {
            sliceAxis = SliceAxis.Z;
            RebuildNow();
        }

        if (volume == null || packingCompute == null || sliceRasterShader == null)
            return;

        if (sliceDebugMat != null)
        {
            sliceDebugMat.SetInt("_SliceIndex", Mathf.Clamp(debugSliceIndex, 0, internalZ - 1));
            sliceDebugMat.SetTexture("_SliceArray", debugHollowSlices ? hollowSliceArray : solidSliceArray);
        }


        if (rebuildEveryFrame)
        {
            RebuildNow(false);
            // return;
        }


        if (AnyTrackedTransformChanged()) // rebuildOnTransformChanges && 
        {
            RebuildDirtySlicesOnly();
            // rebuildQueued = true;
            // lastDirtyTime = Time.unscaledTime;
            // nextRebuildTime = Time.unscaledTime;

            // if (useLowerResolutionWhileDirty && !usingInteractiveResolution)
            // {
            //     SetInteractiveResolution();
            //     // RebuildNow(false);
            //     RebuildDirtySlicesOnly();
            //     usingInteractiveResolution = true;
            // }
        }

        // if (rebuildQueued && Time.unscaledTime >= nextRebuildTime)
        // {
        //     rebuildQueued = false;
        //     // RebuildNow(false);
        //     RebuildDirtySlicesOnly();
        // }

        // if (usingInteractiveResolution && !rebuildQueued && lastDirtyTime > 0f && Time.unscaledTime >= lastDirtyTime + settleDelaySeconds)
        // {
        //     RestoreFullResolution();
        //     RebuildNow(false);
        //     usingInteractiveResolution = false;
        // }
    }

    public void RefreshTrackedObjects()
    {
        GatherVoxelObjects(true);
    }

    bool IsRendererNearVolume(Renderer r)
    {
        if (r == null || volume == null)
            return false;

        Bounds b = r.bounds;

        Vector3 min = volume.VolumeMin - Vector3.one * outsideVolumeMargin;
        Vector3 max = volume.VolumeMax + Vector3.one * outsideVolumeMargin;

        bool overlaps =
            b.max.x >= min.x && b.min.x <= max.x &&
            b.max.y >= min.y && b.min.y <= max.y &&
            b.max.z >= min.z && b.min.z <= max.z;

        return overlaps;
    }

    bool AnyTrackedTransformChanged()
    {

        // Detect new objects that were registered but not tracked yet
        foreach (var v in ActiveVoxelObjects)
        {
            if (v == null || !v.isActiveAndEnabled)
                continue;

            var r = GetRenderer(v);
            if (r == null)
                continue;

            int id = r.GetInstanceID();

            if (!trackedRenderers.ContainsKey(id))
            {
                trackedRenderers[id] = r;
                lastTransforms[id] = r.localToWorldMatrix;
                lastRendererBounds[id] = r.bounds;

                MarkDirtySliceRangeFromBounds(r.bounds);

                voxelObjects.Add(v);

                r.transform.hasChanged = false;

                return true; // force rebuild
            }
        }
        if (voxelObjects.Count == 0 && trackedRenderers.Count == 0)
            GatherVoxelObjects(true);

        bool changed = false;
        ClearDirtySliceRange();

        seenRendererIds.Clear();
        removedRendererIds.Clear();

        for (int i = voxelObjects.Count - 1; i >= 0; i--)
        {
            var v = voxelObjects[i];

            if (v == null || !v.isActiveAndEnabled)
            {
                voxelObjects.RemoveAt(i);
                changed = true;
                continue;
            }

            var r = GetRenderer(v);
            if (r == null)
            {
                voxelObjects.RemoveAt(i);
                changed = true;
                continue;
            }

            int id = r.GetInstanceID();
            seenRendererIds.Add(id);

            if (!trackedRenderers.ContainsKey(id))
            {
                trackedRenderers[id] = r;
                lastTransforms[id] = r.localToWorldMatrix;
                lastRendererBounds[id] = r.bounds;
                MarkDirtySliceRangeFromBounds(r.bounds);
                r.transform.hasChanged = false;
                changed = true;
                continue;
            }

            Bounds currentBounds = r.bounds;
            Bounds previousBounds = lastRendererBounds.TryGetValue(id, out var oldB) ? oldB : currentBounds;

            // NEW: animated objects force dirty
            if (v.IsAnimatedNow())
            {
                MarkDirtyFromRendererBounds(r, previousBounds, currentBounds);

                lastTransforms[id] = r.localToWorldMatrix;
                lastRendererBounds[id] = currentBounds;
                r.transform.hasChanged = false;
                changed = true;
                continue;
            }

            Matrix4x4 m = r.localToWorldMatrix;

            bool matrixChanged = !lastTransforms.TryGetValue(id, out var prevM) || prevM != m;
            bool boundsChanged = !lastRendererBounds.TryGetValue(id, out var prevB) || !BoundsApproximatelyEqual(prevB, currentBounds);

            if (matrixChanged || boundsChanged)
            {
                MarkDirtyFromRendererBounds(r, previousBounds, currentBounds);

                lastTransforms[id] = m;
                lastRendererBounds[id] = currentBounds;
                changed = true;
            }

            r.transform.hasChanged = false;
        }

        foreach (var kv in trackedRenderers)
        {
            if (!seenRendererIds.Contains(kv.Key))
                removedRendererIds.Add(kv.Key);
        }

        for (int i = 0; i < removedRendererIds.Count; i++)
        {
            int id = removedRendererIds[i];

            if (trackedRenderers.TryGetValue(id, out var oldRenderer) && oldRenderer != null)
                MarkDirtySliceRangeFromBounds(oldRenderer.bounds);

            trackedRenderers.Remove(id);
            lastTransforms.Remove(id);
            lastRendererBounds.Remove(id);
            changed = true;
        }

        return changed;
    }

    bool BoundsApproximatelyEqual(Bounds a, Bounds b, float eps = 0.0001f)
    {
        return
            Vector3.SqrMagnitude(a.center - b.center) <= eps * eps &&
            Vector3.SqrMagnitude(a.size - b.size) <= eps * eps;
    }

    // void SetInteractiveResolution()
    // {
    //     int newX = Mathf.Max(8, interactiveX);
    //     int newY = Mathf.Max(8, interactiveY);
    //     int newZ = Mathf.Max(8, interactiveZ);

    //     if (internalX == newX && internalY == newY && internalZ == newZ)
    //         return;

    //     internalX = newX;
    //     internalY = newY;
    //     internalZ = newZ;

    //     packCount = Mathf.Max(1, (internalZ + (PACK_SLICES - 1)) / PACK_SLICES);

    //     EnsureTextures();
    //     if (squareClock != null)
    //         squareClock.packCount = packCount;

    //     // old dirty indices were based on old resolution
    //     hasDirtySliceRange = true;
    //     dirtySliceMin = 0;
    //     dirtySliceMax = internalZ - 1;

    //     projectorFrameDirty = true;
    //     debugFrameDirty = true;
    //     lastComposedProjectorPack = -1;
    //     lastComposedDebugPack = -1;
    // }

    void RestoreFullResolution()
    {
        if (internalX == fullResX && internalY == fullResY && internalZ == fullResZ)
            return;

        internalX = fullResX;
        internalY = fullResY;
        internalZ = fullResZ;

        packCount = Mathf.Max(1, (internalZ + (PACK_SLICES - 1)) / PACK_SLICES);

        EnsureTextures();
        if (squareClock != null)
            squareClock.packCount = packCount;

        hasDirtySliceRange = true;
        dirtySliceMin = 0;
        dirtySliceMax = internalZ - 1;
    }

    Renderer GetRenderer(VoxelizableObject v)
    {
        if (v == null) return null;

        var r = v.objectRenderer != null ? v.objectRenderer : v.GetComponent<Renderer>();
        if (r == null || !r.enabled) return null;

        if (((1 << r.gameObject.layer) & voxelizableLayers.value) == 0)
            return null;

        if (ignoreObjectsOutsideVolume && !IsRendererNearVolume(r))
            return null;

        return r;
    }

    void GatherVoxelObjects(bool forceRefresh = false)
    {
        if (!forceRefresh && voxelObjects.Count > 0)
            return;

        voxelObjects.Clear();
        trackedRenderers.Clear();
        lastTransforms.Clear();
        lastRendererBounds.Clear();

         foreach (var v in ActiveVoxelObjects)
        {
            if (v == null || !v.isActiveAndEnabled)
                continue;

            var r = GetRenderer(v);
            if (r == null)
                continue;

            voxelObjects.Add(v);

            int id = r.GetInstanceID();
            trackedRenderers[id] = r;
            lastTransforms[id] = r.localToWorldMatrix;
            lastRendererBounds[id] = r.bounds;
            
            if (r is SkinnedMeshRenderer smr)
            {
                smr.updateWhenOffscreen = true;
            }

            r.transform.hasChanged = false;
        }
    }

    public void RebuildNow(bool forceRefreshObjects = false)
    {
        Initialize();
        if (volume == null || packingCompute == null || sliceRasterShader == null)
            return;

        GatherVoxelObjects(forceRefreshObjects);
        ConfigureSliceCamera();
        PreparePerRendererProperties();
        RenderSlicesForVoxelType(0, solidSliceArray);
        RenderSlicesForVoxelType(1, hollowSliceArray);
        PackAndCompose();
    }

    void ConfigureSliceCamera()
    {
        Vector3 min = volume.VolumeMin;
        Vector3 max = volume.VolumeMax;
        Vector3 center = 0.5f * (min + max);
        Vector3 size = max - min;

        sliceCamera.cullingMask = voxelizableLayers;
        
        Vector3 axis = GetAxisVector();
        Vector3 up = (sliceAxis == SliceAxis.Y) ? Vector3.forward : Vector3.up;

        float axisSize = GetAxisValue(size);
        float camBackOffset = axisSize * 0.5f + 1f;

        sliceCamera.transform.position = center - axis * camBackOffset;
        sliceCamera.transform.rotation = Quaternion.LookRotation(axis, up);

        Vector2 plane = GetPlaneSize(size);

        sliceCamera.orthographicSize = plane.y * 0.5f;
        sliceCamera.aspect = plane.x / Mathf.Max(1e-6f, plane.y);

        sliceCamera.nearClipPlane = 0.01f;
        sliceCamera.farClipPlane = 2f * axisSize + 2f;
        
        sliceCamera.orthographic = true;
        sliceCamera.backgroundColor = Color.black;
    }

    float GetMinVoxelWorldSize()
    {
        Vector3 size = volume.VolumeMax - volume.VolumeMin;
        float sx = size.x / Mathf.Max(1, internalX);
        float sy = size.y / Mathf.Max(1, internalY);
        float sz = size.z / Mathf.Max(1, internalZ);
        return Mathf.Min(sx, Mathf.Min(sy, sz));
    }

    void ClearDirtySliceRange()
    {
        hasDirtySliceRange = false;
        dirtySliceMin = int.MaxValue;
        dirtySliceMax = int.MinValue;
    }

    void MarkDirtySliceRangeFromBounds(Bounds worldBounds)
    {
        if (volume == null) return;

        float minA = GetAxisValue(volume.VolumeMin);
        float maxA = GetAxisValue(volume.VolumeMax);

        float bMin = GetAxisValue(worldBounds.min);
        float bMax = GetAxisValue(worldBounds.max);

        if (bMax < minA || bMin > maxA)
            return;

        float t0 = Mathf.InverseLerp(minA, maxA, bMin);
        float t1 = Mathf.InverseLerp(minA, maxA, bMax);

        int s0 = Mathf.Clamp(Mathf.FloorToInt(t0 * internalZ) - 1, 0, internalZ - 1);
        int s1 = Mathf.Clamp(Mathf.CeilToInt (t1 * internalZ) + 1, 0, internalZ - 1);

        if (!hasDirtySliceRange)
        {
            dirtySliceMin = s0;
            dirtySliceMax = s1;
            hasDirtySliceRange = true;
        }
        else
        {
            dirtySliceMin = Mathf.Min(dirtySliceMin, s0);
            dirtySliceMax = Mathf.Max(dirtySliceMax, s1);
        }
    }

    void MarkDirtyFromRendererBounds(Renderer r, Bounds previousBounds, Bounds currentBounds)
    {
        MarkDirtySliceRangeFromBounds(previousBounds);
        MarkDirtySliceRangeFromBounds(currentBounds);
    }

    void PreparePerRendererProperties()
    {
        maxThicknessThisFrame = 1;
        float voxelWorld = Mathf.Max(1e-6f, GetMinVoxelWorldSize());

        for (int i = 0; i < voxelObjects.Count; i++)
        {
            var v = voxelObjects[i];
            var r = GetRenderer(v);
            if (r == null) continue;

            var sharedMats = r.sharedMaterials;
            int materialCount = (sharedMats != null && sharedMats.Length > 0) ? sharedMats.Length : 1;

            // -------- Voxel type / shell --------
            int voxelType = (int)v.voxelType;
            float thicknessVox = 1f;

            if (voxelType == 1)
            {
                thicknessVox = Mathf.Clamp(Mathf.Ceil(v.shellThickness / voxelWorld), 1f, 255f);
                maxThicknessThisFrame = Mathf.Max(maxThicknessThisFrame, Mathf.RoundToInt(thicknessVox));
            }

            for (int matIndex = 0; matIndex < materialCount; matIndex++)
            {
                Material srcMat = (sharedMats != null && matIndex < sharedMats.Length) ? sharedMats[matIndex] : null;

                // -------- Color --------
                Color c = fallbackColor;

                if (v.overrideColor)
                {
                    c = v.colorOverride;
                }
                else if (srcMat != null)
                {
                    if (srcMat.HasProperty("_BaseColor"))
                        c = srcMat.GetColor("_BaseColor");
                    else if (srcMat.HasProperty("_Color"))
                        c = srcMat.GetColor("_Color");
                }

                // -------- Texture --------
                Texture tex = null;

                if (v.overrideTexture && v.mainTextureOverride != null)
                {
                    tex = v.mainTextureOverride;
                }
                else if (srcMat != null)
                {
                    if (srcMat.HasProperty("_BaseMap"))
                        tex = srcMat.GetTexture("_BaseMap");
                    else if (srcMat.HasProperty("_MainTex"))
                        tex = srcMat.GetTexture("_MainTex");
                }

                mpb.Clear();
                r.GetPropertyBlock(mpb, matIndex);

                mpb.SetColor("_ObjectColor", c);
                mpb.SetFloat("_ObjectVoxelType", voxelType);
                mpb.SetFloat("_ShellThicknessNorm", thicknessVox / 255f);
                mpb.SetTexture("_MainTex", tex != null ? tex : Texture2D.whiteTexture);

                r.SetPropertyBlock(mpb, matIndex);
            }
        }
    }

    void RenderSlicesForVoxelType(int targetVoxelType, RenderTexture dstArray, int zStart, int zEnd)
    {
        Shader.SetGlobalVector("_VolumeMinWS", volume.VolumeMin);
        Shader.SetGlobalVector("_VolumeMaxWS", volume.VolumeMax);
        Shader.SetGlobalColor("_FallbackSliceColor", fallbackColor);
        Shader.SetGlobalFloat("_TargetVoxelType", targetVoxelType);

        float axisMin = GetAxisValue(volume.VolumeMin);
        float axisMax = GetAxisValue(volume.VolumeMax);

        float dz = (axisMax - axisMin) / Mathf.Max(1, internalZ);
        float halfThickness = Mathf.Max(1e-5f, Mathf.Max(sliceThicknessWorld, dz) * 0.5f);

        zStart = Mathf.Clamp(zStart, 0, internalZ - 1);
        zEnd   = Mathf.Clamp(zEnd,   0, internalZ - 1);

        for (int z = zStart; z <= zEnd; z++)
        {
            float slicePos = axisMin + (z + 0.5f) * dz;

            Shader.SetGlobalFloat("_SlicePos", slicePos);
            Shader.SetGlobalInt("_SliceAxis", (int)sliceAxis);
            Shader.SetGlobalFloat("_SliceHalfThickness", halfThickness);

            sliceCamera.targetTexture = tempSlice;
            sliceCamera.RenderWithShader(sliceRasterShader, "RenderType");
            Graphics.CopyTexture(tempSlice, 0, 0, dstArray, z, 0);
        }

        sliceCamera.targetTexture = null;
    }

    void RenderSlicesForVoxelType(int targetVoxelType, RenderTexture dstArray)
    {
        RenderSlicesForVoxelType(targetVoxelType, dstArray, 0, internalZ - 1);
    }

    void RebuildDirtySlicesOnly()
    {
        Initialize();
        if (volume == null || packingCompute == null || sliceRasterShader == null)
            return;

        GatherVoxelObjects(true);
        ConfigureSliceCamera();
        PreparePerRendererProperties();

        if (hasDirtySliceRange)
        {
            RenderSlicesForVoxelType(0, solidSliceArray, dirtySliceMin, dirtySliceMax);
            RenderSlicesForVoxelType(1, hollowSliceArray, dirtySliceMin, dirtySliceMax);
        }
        else
        {
            RenderSlicesForVoxelType(0, solidSliceArray);
            RenderSlicesForVoxelType(1, hollowSliceArray);
        }

        PackAndCompose();
        ClearDirtySliceRange();
    }

    void PackAndCompose()
    {
        packingCompute.SetInt("isMechanicalUp", isMechanicalUp);
        packingCompute.SetInt("voxelResX", internalX);
        packingCompute.SetInt("voxelResY", internalY);
        packingCompute.SetInt("voxelResZ", internalZ);
        packingCompute.SetInt("packCount", packCount);
        packingCompute.SetInt("projResX", projectorX);
        packingCompute.SetInt("projResY", projectorY);
        packingCompute.SetInt("sweepUpOnly", sweepUpOnly ? 1 : 0);
        packingCompute.SetInt("sweepDownOnly", sweepDownOnly ? 1 : 0);
        packingCompute.SetInt("interlaceMode", interlaceMode ? 1 : 0);
        packingCompute.SetInt("maxThicknessVox", maxThicknessThisFrame);

        int gx = Mathf.CeilToInt(internalX / 8f);
        int gy = Mathf.CeilToInt(internalY / 8f);
        int gp = packCount;
        int gz = Mathf.CeilToInt(internalZ / 4f);

        packingCompute.SetTexture(kClearPackedIntermediateA, "packsSolidSurfaceR", packsSolidSurfaceR);
        packingCompute.SetTexture(kClearPackedIntermediateA, "packsSolidSurfaceG", packsSolidSurfaceG);
        packingCompute.SetTexture(kClearPackedIntermediateA, "packsSolidSurfaceB", packsSolidSurfaceB);
        packingCompute.SetTexture(kClearPackedIntermediateA, "packsSolidFilledR", packsSolidFilledR);
        packingCompute.SetTexture(kClearPackedIntermediateA, "packsSolidFilledG", packsSolidFilledG);
        packingCompute.SetTexture(kClearPackedIntermediateA, "packsSolidFilledB", packsSolidFilledB);
        packingCompute.SetTexture(kClearPackedIntermediateB, "packsHollowPackedR", packsHollowPackedR);
        packingCompute.SetTexture(kClearPackedIntermediateB, "packsHollowPackedG", packsHollowPackedG);
        packingCompute.SetTexture(kClearPackedIntermediateB, "packsHollowPackedB", packsHollowPackedB);
        packingCompute.SetTexture(kClearPackedIntermediateC, "voxelPacksR", voxelPacksR);
        packingCompute.SetTexture(kClearPackedIntermediateC, "voxelPacksG", voxelPacksG);
        packingCompute.SetTexture(kClearPackedIntermediateC, "voxelPacksB", voxelPacksB);
        packingCompute.Dispatch(kClearPackedIntermediateA, gx, gy, gp);
        packingCompute.Dispatch(kClearPackedIntermediateB, gx, gy, gp);
        packingCompute.Dispatch(kClearPackedIntermediateC, gx, gy, gp);

        packingCompute.SetTexture(kClearHollow3D, "hollowSteps", hollowStepsRT);
        packingCompute.SetTexture(kClearHollow3D, "hollowOccR", hollowOccRRT);
        packingCompute.SetTexture(kClearHollow3D, "hollowOccG", hollowOccGRT);
        packingCompute.SetTexture(kClearHollow3D, "hollowOccB", hollowOccBRT);
        packingCompute.Dispatch(kClearHollow3D, gx, gy, gz);

        packingCompute.SetTexture(kPackSolidSlicesToSurfacePacks, "solidSliceArray", solidSliceArray);
        packingCompute.SetTexture(kPackSolidSlicesToSurfacePacks, "packsSolidSurfaceR", packsSolidSurfaceR);
        packingCompute.SetTexture(kPackSolidSlicesToSurfacePacks, "packsSolidSurfaceG", packsSolidSurfaceG);
        packingCompute.SetTexture(kPackSolidSlicesToSurfacePacks, "packsSolidSurfaceB", packsSolidSurfaceB);
        packingCompute.Dispatch(kPackSolidSlicesToSurfacePacks, gx, gy, gp);

        packingCompute.SetTexture(kSeedHollowFromSlices, "hollowSliceArray", hollowSliceArray);
        packingCompute.SetTexture(kSeedHollowFromSlices, "hollowSteps", hollowStepsRT);
        packingCompute.SetTexture(kSeedHollowFromSlices, "hollowOccR", hollowOccRRT);
        packingCompute.SetTexture(kSeedHollowFromSlices, "hollowOccG", hollowOccGRT);
        packingCompute.SetTexture(kSeedHollowFromSlices, "hollowOccB", hollowOccBRT);
        packingCompute.Dispatch(kSeedHollowFromSlices, gx, gy, gz);

        for (int i = 0; i < Mathf.Max(0, maxThicknessThisFrame - 1); i++)
        {
            packingCompute.SetTexture(kDilateHollowStep, "hollowSteps", hollowStepsRT);
            packingCompute.SetTexture(kDilateHollowStep, "hollowOccR", hollowOccRRT);
            packingCompute.SetTexture(kDilateHollowStep, "hollowOccG", hollowOccGRT);
            packingCompute.SetTexture(kDilateHollowStep, "hollowOccB", hollowOccBRT);
            packingCompute.Dispatch(kDilateHollowStep, gx, gy, gz);
        }

        packingCompute.SetTexture(kPackHollowToPacks, "hollowOccR", hollowOccRRT);
        packingCompute.SetTexture(kPackHollowToPacks, "hollowOccG", hollowOccGRT);
        packingCompute.SetTexture(kPackHollowToPacks, "hollowOccB", hollowOccBRT);
        packingCompute.SetTexture(kPackHollowToPacks, "packsHollowPackedR", packsHollowPackedR);
        packingCompute.SetTexture(kPackHollowToPacks, "packsHollowPackedG", packsHollowPackedG);
        packingCompute.SetTexture(kPackHollowToPacks, "packsHollowPackedB", packsHollowPackedB);
        packingCompute.Dispatch(kPackHollowToPacks, gx, gy, gp);

        packingCompute.SetTexture(kSolidFillColumns, "packsSolidSurfaceR", packsSolidSurfaceR);
        packingCompute.SetTexture(kSolidFillColumns, "packsSolidSurfaceG", packsSolidSurfaceG);
        packingCompute.SetTexture(kSolidFillColumns, "packsSolidSurfaceB", packsSolidSurfaceB);
        packingCompute.SetTexture(kSolidFillColumns, "packsSolidFilledR", packsSolidFilledR);
        packingCompute.SetTexture(kSolidFillColumns, "packsSolidFilledG", packsSolidFilledG);
        packingCompute.SetTexture(kSolidFillColumns, "packsSolidFilledB", packsSolidFilledB);
        packingCompute.Dispatch(kSolidFillColumns, gx, gy, 1);

        packingCompute.SetTexture(kCombineFinal, "packsSolidFilledR_Read", packsSolidFilledR);
        packingCompute.SetTexture(kCombineFinal, "packsSolidFilledG_Read", packsSolidFilledG);
        packingCompute.SetTexture(kCombineFinal, "packsSolidFilledB_Read", packsSolidFilledB);
        packingCompute.SetTexture(kCombineFinal, "packsHollowPackedR_Read", packsHollowPackedR);
        packingCompute.SetTexture(kCombineFinal, "packsHollowPackedG_Read", packsHollowPackedG);
        packingCompute.SetTexture(kCombineFinal, "packsHollowPackedB_Read", packsHollowPackedB);
        packingCompute.SetTexture(kCombineFinal, "voxelPacksR", voxelPacksR);
        packingCompute.SetTexture(kCombineFinal, "voxelPacksG", voxelPacksG);
        packingCompute.SetTexture(kCombineFinal, "voxelPacksB", voxelPacksB);
        packingCompute.Dispatch(kCombineFinal, gx, gy, gp);

        // if(packDebugMat.name == "Unlit_PackDebugArray")
        // {
        //     packingCompute.SetTexture(kCompose, "voxelPacksR", voxelPacksR);
        //     packingCompute.SetTexture(kCompose, "voxelPacksG", voxelPacksG);
        //     packingCompute.SetTexture(kCompose, "voxelPacksB", voxelPacksB);
        //     packingCompute.SetTexture(kCompose, "projectorFrames", projectorFrames);

        //     int gxP = Mathf.CeilToInt(projectorX / 8f);
        //     int gyP = Mathf.CeilToInt(projectorY / 8f);
        //     packingCompute.Dispatch(kCompose, gxP, gyP, packCount);
        // }

        projectorFrameDirty = true;
        debugFrameDirty = true;
        lastComposedProjectorPack = -1;
        lastComposedDebugPack = -1;
        
    }

    void ComposeCurrentPackFrame(int packIndex, RenderTexture target)
    {
        if (packingCompute == null) return;

        int gxP = Mathf.CeilToInt(projectorX / 8f);
        int gyP = Mathf.CeilToInt(projectorY / 8f);

        packingCompute.SetInt("voxelResX", internalX);
        packingCompute.SetInt("voxelResY", internalY);
        packingCompute.SetInt("voxelResZ", internalZ);
        packingCompute.SetInt("packCount", packCount);
        packingCompute.SetInt("projResX", projectorX);
        packingCompute.SetInt("projResY", projectorY);
        packingCompute.SetInt("sweepUpOnly", sweepUpOnly ? 1 : 0);
        packingCompute.SetInt("sweepDownOnly", sweepDownOnly ? 1 : 0);
        packingCompute.SetInt("interlaceMode", interlaceMode ? 1 : 0);
        packingCompute.SetInt("currentComposePack", Mathf.Clamp(packIndex, 0, packCount - 1));

        packingCompute.SetTexture(kComposeCurrent, "voxelPacksR", voxelPacksR);
        packingCompute.SetTexture(kComposeCurrent, "voxelPacksG", voxelPacksG);
        packingCompute.SetTexture(kComposeCurrent, "voxelPacksB", voxelPacksB);
        packingCompute.SetTexture(kComposeCurrent, "projectorFrameCurrent", target);

        packingCompute.Dispatch(kComposeCurrent, gxP, gyP, 1);
    }
    public static void Register(VoxelizableObject v)
    {
        if (v == null) return;
        ActiveVoxelObjects.Add(v);

        if (Instance != null)
        {
            var r = v.GetComponent<Renderer>();
            if (r != null)
                Instance.MarkDirtySliceRangeFromBounds(r.bounds);

            Instance.GatherVoxelObjects(true);   // force refresh immediately

            Instance.rebuildQueued = true;
            Instance.lastDirtyTime = Time.unscaledTime;
            Instance.nextRebuildTime = Time.unscaledTime;
        }
    }

    public static void Unregister(VoxelizableObject v)
    {
        if (v == null) return;
        ActiveVoxelObjects.Remove(v);
    }

    void AutoAddVoxelizableComponents()
    {
        if (!autoAddVoxelizableOnVoxelizeLayer)
            return;

        int voxelizeLayer = LayerMask.NameToLayer(voxelizeLayerName);
        if (voxelizeLayer < 0)
            return;

        Renderer[] renderers = FindObjectsOfType<Renderer>(true);

        foreach (var r in renderers)
        {
            if (r == null) continue;
            if (r.gameObject.layer != voxelizeLayer) continue;
            if (r.GetComponent<VoxelizableObject>() != null) continue;

            r.gameObject.AddComponent<VoxelizableObject>();
        }
    }

    void OnDestroy()
    {
        if (sliceCamera != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(sliceCamera.gameObject);
            else Destroy(sliceCamera.gameObject);
#else
            Destroy(sliceCamera.gameObject);
#endif
        }

        ReleaseRT(tempSlice);
        ReleaseRT(solidSliceArray);
        ReleaseRT(hollowSliceArray);
        ReleaseRT(packsSolidSurfaceR);
        ReleaseRT(packsSolidSurfaceG);
        ReleaseRT(packsSolidSurfaceB);
        ReleaseRT(packsSolidFilledR);
        ReleaseRT(packsSolidFilledG);
        ReleaseRT(packsSolidFilledB);
        ReleaseRT(packsHollowPackedR);
        ReleaseRT(packsHollowPackedG);
        ReleaseRT(packsHollowPackedB);
        ReleaseRT(hollowStepsRT);
        ReleaseRT(hollowOccRRT);
        ReleaseRT(hollowOccGRT);
        ReleaseRT(hollowOccBRT);
        ReleaseRT(voxelPacksR);
        ReleaseRT(voxelPacksG);
        ReleaseRT(voxelPacksB);

        ReleaseRT(projectorFrames);
        ReleaseRT(projectorFrameCurrent);
        ReleaseRT(debugFrameCurrent);
    }

    void ReleaseRT(RenderTexture rt)
    {
        if (rt != null) rt.Release();
    }

    void OnGUI()
    {
        GUI.matrix = Matrix4x4.Scale(Vector3.one * 2f);

        float fps = 1f / Time.unscaledDeltaTime;
        GUI.Label(
            new Rect(20, 20, 600, 60),
            $"FPS: {fps:F1} VPACK: {lastComposedProjectorPack}  CPACK: {squareClock.VisualMasterPack} DiffPack: {lastComposedProjectorPack - squareClock.VisualMasterPack} "
        ); //   dt: {Time.unscaledDeltaTime * 1000f:F2} ms 
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
            return;

        int debugNumber = packToSliceOffset[squareClock.currentPack];
        // int debugNumber = packToSliceOffset[mechClock.CurrentPack];
        
        
        // Slice plane

         Vector3 center = volume.volumeCenter;
        Vector3 size = volume.volumeSize;

        // Axis helpers
        Vector3 axis =
            (sliceAxis == SliceAxis.X) ? Vector3.right :
            (sliceAxis == SliceAxis.Y) ? Vector3.up :
                                        Vector3.forward;

        float axisSize =
            (sliceAxis == SliceAxis.X) ? size.x :
            (sliceAxis == SliceAxis.Y) ? size.y :
                                        size.z;

        float axisMin =
            (sliceAxis == SliceAxis.X) ? center.x - size.x * 0.5f :
            (sliceAxis == SliceAxis.Y) ? center.y - size.y * 0.5f :
                                        center.z - size.z * 0.5f;

        
        float t = (debugNumber + 0.5f) / internalZ;
        float slicePos = axisMin + t * axisSize;

        Vector3 sliceCenter = center;
        sliceCenter += axis * (slicePos - GetAxisValue(center));

        Vector3 sliceSize = size;
        if (sliceAxis == SliceAxis.X) sliceSize.x = 0.001f;
        if (sliceAxis == SliceAxis.Y) sliceSize.y = 0.001f;
        if (sliceAxis == SliceAxis.Z) sliceSize.z = 0.001f;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(sliceCenter, sliceSize);

        // =========================
        // DEBUG SLICE (GREEN)
        // =========================

        float tDebug = (debugSliceIndex + 0.5f) / internalZ;
        float debugPos = axisMin + tDebug * axisSize;

        Vector3 debugCenter = center;
        debugCenter += axis * (debugPos - GetAxisValue(center));

        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(debugCenter, sliceSize);

        // =========================
        // PACK VOLUME (ORANGE)
        // =========================

        float sliceStep = axisSize / internalZ;

        float packStart = axisMin + packToSliceOffset[squareClock.currentPack] * sliceStep;
        float packEnd = packStart + (packToSliceOffset[1] - packToSliceOffset[0]) * sliceStep;

        float packCenterPos = (packStart + packEnd) * 0.5f;

        Vector3 packCenter = center;
        packCenter += axis * (packCenterPos - GetAxisValue(center));

        Vector3 packSize = size;
        if (sliceAxis == SliceAxis.X) packSize.x = (packEnd - packStart);
        if (sliceAxis == SliceAxis.Y) packSize.y = (packEnd - packStart);
        if (sliceAxis == SliceAxis.Z) packSize.z = (packEnd - packStart);

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        Gizmos.DrawWireCube(packCenter, packSize);
    }
}

