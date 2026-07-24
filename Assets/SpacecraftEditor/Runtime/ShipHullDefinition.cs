using UnityEngine;

namespace SpacecraftEditor
{
    [CreateAssetMenu(menuName = "Spacecraft/Ship Hull Definition", fileName = "ShipHullDefinition")]
    public sealed class ShipHullDefinition : ScriptableObject
    {
        [SerializeField] private string hullId;
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private GameObject modelPrefab;
        [SerializeField] private Sprite thumbnail;
        [SerializeField] private Mesh collisionMesh;
        [SerializeField] private float baseMass = 12000f;
        [SerializeField] private Vector3 dimensions = new Vector3(3f, 2.2f, 6f);
        [SerializeField] private ShipFlightProfile flightProfile;

        public string HullId => hullId;
        public string DisplayName => displayName;
        public string Description => description;
        public GameObject ModelPrefab => modelPrefab;
        public Sprite Thumbnail => thumbnail;
        public Mesh CollisionMesh => collisionMesh;
        public float BaseMass => baseMass;
        public Vector3 Dimensions => dimensions;
        public ShipFlightProfile FlightProfile => flightProfile ?? ShipFlightProfile.CreateForHull(hullId);

        void OnEnable()
        {
            if (flightProfile == null)
                flightProfile = ShipFlightProfile.CreateForHull(hullId);
        }

#if UNITY_EDITOR
        public void Configure(
            string id,
            string label,
            string hullDescription,
            GameObject model,
            Sprite icon,
            Mesh colliderMesh,
            float mass,
            Vector3 size)
        {
            hullId = id;
            displayName = label;
            description = hullDescription;
            modelPrefab = model;
            thumbnail = icon;
            collisionMesh = colliderMesh;
            baseMass = Mathf.Max(0.01f, mass);
            dimensions = new Vector3(
                Mathf.Max(0.01f, size.x),
                Mathf.Max(0.01f, size.y),
                Mathf.Max(0.01f, size.z));
            flightProfile = ShipFlightProfile.CreateForHull(hullId);
        }
#endif
    }
}
