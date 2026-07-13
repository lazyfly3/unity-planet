using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SphereCollider), typeof(SpriteRenderer))]
public sealed class WorldItem : MonoBehaviour
{
    [Header("Item")]
    [SerializeField] string itemId;
    [SerializeField] string displayName;
    [SerializeField] Sprite icon;
    [SerializeField, Min(1)] int maxStack = 99;
    [SerializeField, Min(1)] int amount = 1;

    [Header("World Display")]
    [SerializeField] bool faceCamera = true;
    [SerializeField, Min(0.05f)] float colliderRadius = 0.35f;

    InventoryItem runtimeDefinition;
    SpriteRenderer spriteRenderer;
    SphereCollider pickupCollider;
    Camera mainCamera;

    public string ItemId => ResolveItemId();
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? GetBaseObjectName() : displayName;
    public Sprite Icon => icon;
    public int MaxStack => Mathf.Max(1, maxStack);
    public int Amount => Mathf.Max(1, amount);

    void Reset()
    {
        ConfigureComponents();
    }

    void Awake()
    {
        ConfigureComponents();
        runtimeDefinition = InventoryItem.GetOrCreateRuntime(
            ItemId, DisplayName, icon, MaxStack);
        mainCamera = Camera.main;
    }

    void OnValidate()
    {
        maxStack = Mathf.Max(1, maxStack);
        amount = Mathf.Max(1, amount);
        colliderRadius = Mathf.Max(0.05f, colliderRadius);
        ConfigureComponents();
    }

    void LateUpdate()
    {
        if (!faceCamera)
            return;

        if (mainCamera == null)
            mainCamera = Camera.main;
        if (mainCamera != null)
            transform.rotation = mainCamera.transform.rotation;
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerInventory inventory = other.GetComponentInParent<PlayerInventory>();
        if (inventory == null || runtimeDefinition == null)
            return;

        amount = inventory.Add(runtimeDefinition, Amount);
        if (amount == 0)
            Destroy(gameObject);
    }

    void ConfigureComponents()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        if (pickupCollider == null)
            pickupCollider = GetComponent<SphereCollider>();

        if (spriteRenderer != null)
            spriteRenderer.sprite = icon;
        if (pickupCollider != null)
        {
            pickupCollider.isTrigger = true;
            pickupCollider.radius = colliderRadius;
        }
    }

    string ResolveItemId()
    {
        return string.IsNullOrWhiteSpace(itemId) ? GetBaseObjectName() : itemId.Trim();
    }

    string GetBaseObjectName()
    {
        return gameObject.name.Replace("(Clone)", string.Empty).Trim();
    }
}
