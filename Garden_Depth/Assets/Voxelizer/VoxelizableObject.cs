using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VoxelizableObject : MonoBehaviour
{
    public enum VoxelType
    {
        Solid = 0,
        Hollow = 1
    }

    [Header("Voxelization")]
    public VoxelType voxelType = VoxelType.Solid;

    [Tooltip("Only used if Hollow")]
    public float shellThickness = 0.01f;

    public int objectID;

    [Header("Texture / Color")]
    public bool overrideTexture = false;
    public Texture mainTextureOverride;
    public bool overrideColor = false;
    public Color colorOverride = Color.white;

    [Header("Animation")]
    public bool alwaysDirtyWhenAnimated = true;

    [HideInInspector] public Renderer objectRenderer;
    [HideInInspector] public Animator animatorComponent;
    [HideInInspector] public Animation animationComponent;

    void Awake()
    {
        Vector3 positionOrigin = transform.position;
        transform.localScale = new Vector3(transform.localScale.x, transform.localScale.y, transform.localScale.z);
        objectRenderer = GetComponent<Renderer>();
        animatorComponent = GetComponentInParent<Animator>();
        animationComponent = GetComponentInParent<Animation>();
        transform.position = positionOrigin;
    }

    void OnEnable()
    {
        LayeredSliceRasterVoxelizer.Register(this);
    }

    void OnDisable()
    {
        LayeredSliceRasterVoxelizer.Unregister(this);
    }

    void OnValidate()
    {
        if (objectRenderer == null)
            objectRenderer = GetComponent<Renderer>();

        if (animatorComponent == null)
            animatorComponent = GetComponentInParent<Animator>();

        if (animationComponent == null)
            animationComponent = GetComponentInParent<Animation>();

        if (LayeredSliceRasterVoxelizer.Instance != null && objectRenderer != null)
            LayeredSliceRasterVoxelizer.Instance.RefreshTrackedObjects();
    }

    public bool IsAnimatedNow()
    {
        if (!alwaysDirtyWhenAnimated)
            return false;

        if (animatorComponent != null && animatorComponent.enabled)
            return true;

        if (animationComponent != null && animationComponent.enabled && animationComponent.isPlaying)
            return true;

        return false;
    }
}
