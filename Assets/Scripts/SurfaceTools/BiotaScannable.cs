using UnityEngine;

public enum BiotaDiscoveryType
{
    Plant = 0,
    Creature = 1
}

[DisallowMultipleComponent]
public sealed class BiotaScannable : MonoBehaviour
{
    [SerializeField] BiotaDiscoveryType discoveryType;
    [SerializeField] string stableId;
    [SerializeField] string displayName;
    [SerializeField, TextArea] string description;

    public BiotaDiscoveryType DiscoveryType => discoveryType;
    public string StableId => stableId ?? string.Empty;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName)
        ? gameObject.name
        : displayName;
    public string Description => description ?? string.Empty;

    public void Configure(
        BiotaDiscoveryType type,
        string id,
        string name,
        string details = null)
    {
        discoveryType = type;
        stableId = NormalizeId(id, type, gameObject.name);
        displayName = string.IsNullOrWhiteSpace(name)
            ? gameObject.name
            : name.Trim();
        description = details != null ? details.Trim() : string.Empty;
    }

    public static BiotaScannable Ensure(
        GameObject target,
        BiotaDiscoveryType type,
        string id,
        string name,
        string details = null)
    {
        if (target == null)
            return null;
        BiotaScannable value = target.GetComponent<BiotaScannable>();
        if (value == null)
            value = target.AddComponent<BiotaScannable>();
        value.Configure(type, id, name, details);
        return value;
    }

    public static string NormalizeId(
        string id,
        BiotaDiscoveryType type,
        string fallback)
    {
        string value = string.IsNullOrWhiteSpace(id)
            ? fallback
            : id.Trim();
        value = value.Replace('\n', '_').Replace('\r', '_');
        string prefix = type == BiotaDiscoveryType.Plant
            ? "plant:"
            : "creature:";
        return value.StartsWith(prefix, System.StringComparison.Ordinal)
            ? value
            : prefix + value;
    }
}
