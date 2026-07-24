using UnityEngine;

[DisallowMultipleComponent]
public sealed class HarvestableResource : MonoBehaviour
{
    [Header("Reward")]
    [Tooltip("Drag a prefab containing the WorldItem component here.")]
    [SerializeField] WorldItem rewardItemPrefab;

    [Header("Harvesting")]
    [SerializeField, Min(1)] int minimumHarvestClicks = 1;
    [SerializeField, Min(1)] int maximumHarvestClicks = 3;
    [SerializeField, Min(0.1f)] float interactionDistance = 6f;
    [SerializeField] bool preserveAuthoredColliders;

    [Header("Feedback")]
    [SerializeField] bool showProgressFeedback = true;
    [SerializeField, Range(0.5f, 1f)] float hitScaleMultiplier = 0.85f;
    [SerializeField, Min(0.01f)] float hitPulseDuration = 0.15f;
    [SerializeField, Min(0.1f)] float progressFeedbackDuration = 0.8f;
    [SerializeField] AudioClip harvestHitSound;
    [SerializeField] AudioClip harvestCompleteSound;

    static int cachedRaycastFrame = -1;
    static HarvestableResource cachedRaycastTarget;
    static float cachedRaycastDistance;

    PlayerInventory playerInventory;
    BuildingPlacer buildingPlacer;
    int requiredHarvestClicks;
    int completedHarvestClicks;
    Vector3 normalScale;
    float pulseTimeRemaining;
    float feedbackVisibleUntil;
    string feedbackText;
    GUIStyle feedbackStyle;
    string stableResourceId;

    public int RequiredHarvestClicks => requiredHarvestClicks;
    public int CompletedHarvestClicks => completedHarvestClicks;
    public int RemainingHarvestClicks => Mathf.Max(0, requiredHarvestClicks - completedHarvestClicks);

    public void AssignStableResourceId(string resourceId)
    {
        stableResourceId = resourceId;
    }

    void Reset()
    {
        EnsureHarvestCollider();
    }

    void Awake()
    {
        EnsureHarvestCollider();
        normalScale = transform.localScale;
    }

    void OnEnable()
    {
        normalScale = transform.localScale;
        RollRequiredClicks();
    }

    void OnDisable()
    {
        transform.localScale = normalScale;
    }

    void OnValidate()
    {
        minimumHarvestClicks = Mathf.Max(1, minimumHarvestClicks);
        maximumHarvestClicks = Mathf.Max(minimumHarvestClicks, maximumHarvestClicks);
        interactionDistance = Mathf.Max(0.1f, interactionDistance);
        hitPulseDuration = Mathf.Max(0.01f, hitPulseDuration);
        progressFeedbackDuration = Mathf.Max(0.1f, progressFeedbackDuration);
        EnsureHarvestCollider();
    }

    void Update()
    {
        UpdateHitPulse();

        if (!Input.GetMouseButtonDown(0)
            || InventoryUI.BlocksGameplayInput
            || !SurfaceToolInputRouter.CanWorldInteractionUsePrimary)
            return;

        HarvestableResource target = GetResourceUnderCrosshair(out float hitDistance);
        if (target != this)
            return;

        if (hitDistance > interactionDistance)
        {
            ShowStatusFeedback("Too Far");
            return;
        }

        TryHarvest();
    }

    void TryHarvest()
    {
        if (rewardItemPrefab == null)
        {
            Debug.LogError($"{name}: HarvestableResource requires a WorldItem prefab.", this);
            return;
        }

        if (!ResolvePlayerInventory())
        {
            Debug.LogError($"{name}: No PlayerInventory was found.", this);
            return;
        }

        if (buildingPlacer != null && buildingPlacer.IsBuildMode)
            return;

        completedHarvestClicks = Mathf.Min(completedHarvestClicks + 1, requiredHarvestClicks);
        ShowHarvestFeedback();
        if (completedHarvestClicks < requiredHarvestClicks)
            return;

        InventoryItem rewardDefinition = rewardItemPrefab.Definition;
        int rewardAmount = rewardItemPrefab.Amount;
        if (!playerInventory.CanAdd(rewardDefinition, rewardAmount))
        {
            ShowStatusFeedback("Inventory Full");
            Debug.LogWarning($"{name}: Inventory has no room for {rewardAmount} x {rewardDefinition.DisplayName}.", this);
            return;
        }

        int remaining = playerInventory.Add(rewardDefinition, rewardAmount);
        if (remaining == 0)
        {
            GetComponentInParent<VoxelQuadSphereWorld>()?.MarkResourceHarvested(stableResourceId);
            PlayFeedbackSound(harvestCompleteSound);
            PlanetSurfacePropInstance generatedInstance = GetComponentInParent<PlanetSurfacePropInstance>();
            Destroy(generatedInstance != null ? generatedInstance.gameObject : gameObject);
        }
    }

    bool ResolvePlayerInventory()
    {
        if (playerInventory != null)
            return true;

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            playerInventory = mainCamera.GetComponentInParent<PlayerInventory>();

        if (playerInventory == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                playerInventory = player.GetComponentInParent<PlayerInventory>();
        }

        if (playerInventory != null)
            buildingPlacer = playerInventory.GetComponent<BuildingPlacer>();

        return playerInventory != null;
    }

    void RollRequiredClicks()
    {
        minimumHarvestClicks = Mathf.Max(1, minimumHarvestClicks);
        maximumHarvestClicks = Mathf.Max(minimumHarvestClicks, maximumHarvestClicks);
        requiredHarvestClicks = Random.Range(minimumHarvestClicks, maximumHarvestClicks + 1);
        completedHarvestClicks = 0;
    }

    void ShowHarvestFeedback()
    {
        pulseTimeRemaining = hitPulseDuration;
        transform.localScale = normalScale * hitScaleMultiplier;
        feedbackText = $"Harvest: {completedHarvestClicks} / {requiredHarvestClicks}";
        feedbackVisibleUntil = Time.unscaledTime + progressFeedbackDuration;
        PlayFeedbackSound(harvestHitSound);
    }

    void ShowStatusFeedback(string message)
    {
        feedbackText = message;
        feedbackVisibleUntil = Time.unscaledTime + progressFeedbackDuration;
    }

    void UpdateHitPulse()
    {
        if (pulseTimeRemaining <= 0f)
            return;

        pulseTimeRemaining = Mathf.Max(0f, pulseTimeRemaining - Time.unscaledDeltaTime);
        float progress = 1f - pulseTimeRemaining / hitPulseDuration;
        float scale = Mathf.Lerp(hitScaleMultiplier, 1f, progress);
        transform.localScale = normalScale * scale;
    }

    void PlayFeedbackSound(AudioClip clip)
    {
        if (clip != null)
            AudioSource.PlayClipAtPoint(clip, transform.position);
    }

    void OnGUI()
    {
        if (!showProgressFeedback || Time.unscaledTime >= feedbackVisibleUntil || string.IsNullOrEmpty(feedbackText))
            return;

        if (feedbackStyle == null)
        {
            feedbackStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18
            };
        }

        const float width = 220f;
        const float height = 38f;
        Rect rect = new Rect(
            (Screen.width - width) * 0.5f,
            Screen.height * 0.5f + 35f,
            width,
            height);
        GUI.Label(rect, feedbackText, feedbackStyle);
    }

    void EnsureHarvestCollider()
    {
        Collider[] existingColliders = GetComponentsInChildren<Collider>(true);
        bool isSceneInstance = gameObject.scene.IsValid();
        // Prefab assets can receive OnValidate while entering Play Mode. Never delete their authored colliders.
        if (!isSceneInstance && existingColliders.Length > 0)
            return;

        if (preserveAuthoredColliders && existingColliders.Length > 0)
            return;
        if (!Application.isPlaying && existingColliders.Length > 0)
            return;

        foreach (Collider existing in existingColliders)
        {
            existing.enabled = false;
            if (Application.isPlaying && isSceneInstance)
                Destroy(existing);
            else
                DestroyImmediate(existing);
        }

        bool addedMeshCollider = false;
        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>(true);
        if (meshFilters.Length <= 8)
        {
            foreach (MeshFilter filter in meshFilters)
            {
                if (filter == null || filter.sharedMesh == null || filter.sharedMesh.vertexCount < 4)
                    continue;
                MeshCollider meshCollider = filter.gameObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = filter.sharedMesh;
                meshCollider.convex = true;
                addedMeshCollider = true;
            }
        }
        if (addedMeshCollider)
            return;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        foreach (Renderer modelRenderer in renderers)
        {
            Bounds bounds = modelRenderer.localBounds;
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 rendererCorner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                        Vector3 worldCorner = modelRenderer.transform.TransformPoint(rendererCorner);
                        Vector3 localCorner = transform.InverseTransformPoint(worldCorner);
                        minimum = Vector3.Min(minimum, localCorner);
                        maximum = Vector3.Max(maximum, localCorner);
                    }
                }
            }
        }

        Vector3 colliderCenter = (minimum + maximum) * 0.5f;
        Vector3 colliderSize = Vector3.Max(maximum - minimum, Vector3.one * 0.05f);
        if (meshFilters.Length > 8)
        {
            CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.center = colliderCenter;
            if (colliderSize.x >= colliderSize.y && colliderSize.x >= colliderSize.z)
            {
                capsule.direction = 0;
                capsule.radius = Mathf.Max(0.05f, Mathf.Min(colliderSize.y, colliderSize.z) * 0.38f);
                capsule.height = Mathf.Max(capsule.radius * 2f, colliderSize.x * 0.9f);
            }
            else if (colliderSize.z >= colliderSize.x && colliderSize.z >= colliderSize.y)
            {
                capsule.direction = 2;
                capsule.radius = Mathf.Max(0.05f, Mathf.Min(colliderSize.x, colliderSize.y) * 0.38f);
                capsule.height = Mathf.Max(capsule.radius * 2f, colliderSize.z * 0.9f);
            }
            else
            {
                capsule.direction = 1;
                capsule.radius = Mathf.Max(0.05f, Mathf.Min(colliderSize.x, colliderSize.z) * 0.38f);
                capsule.height = Mathf.Max(capsule.radius * 2f, colliderSize.y * 0.9f);
            }
            capsule.isTrigger = false;
        }
        else
        {
            BoxCollider box = gameObject.AddComponent<BoxCollider>();
            box.center = colliderCenter;
            box.size = Vector3.Max(Vector3.Scale(colliderSize, Vector3.one * 0.82f), Vector3.one * 0.05f);
            box.isTrigger = false;
        }
    }

    static HarvestableResource GetResourceUnderCrosshair(out float hitDistance)
    {
        if (cachedRaycastFrame != Time.frameCount)
        {
            cachedRaycastFrame = Time.frameCount;
            cachedRaycastTarget = null;
            cachedRaycastDistance = float.PositiveInfinity;

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                Ray ray = mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
                if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                {
                    cachedRaycastTarget = hit.collider.GetComponentInParent<HarvestableResource>();
                    cachedRaycastDistance = hit.distance;
                }
            }
        }

        hitDistance = cachedRaycastDistance;
        return cachedRaycastTarget;
    }
}
