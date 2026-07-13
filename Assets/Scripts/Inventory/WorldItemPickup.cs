using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class WorldItemPickup : MonoBehaviour
{
    [SerializeField] InventoryItem item;
    [SerializeField, Min(1)] int amount = 1;

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerInventory inventory = other.GetComponentInParent<PlayerInventory>();
        if (inventory == null || item == null)
            return;

        int remaining = inventory.Add(item, amount);
        amount = remaining;
        if (remaining == 0)
            Destroy(gameObject);
    }
}
