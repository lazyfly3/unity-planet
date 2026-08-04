using UnityEngine;

namespace UnityPlanet.SpaceStation
{
    [DisallowMultipleComponent]
    public sealed class SpaceStationAutomaticDoor : MonoBehaviour
    {
        [SerializeField] Transform[] panels = new Transform[0];
        [SerializeField] Vector3[] openWorldOffsets = new Vector3[0];
        [SerializeField] Transform sensedActor;
        [SerializeField, Min(0.5f)] float activationDistance = 2.6f;
        [SerializeField, Min(0.1f)] float movementSpeed = 2.8f;
        [SerializeField, Min(0f)] float closeDelay = 1.25f;

        Vector3[] closedWorldPositions;
        Vector3[] openWorldPositions;
        Collider[][] panelColliders;
        float lastActorDetectionTime = float.NegativeInfinity;
        bool collidersEnabled = true;

        public bool IsOpen { get; private set; }
        public bool IsActorInRange => IsSensedActorInRange();

        public void Configure(
            Transform[] configuredPanels,
            Vector3[] configuredOpenWorldOffsets,
            Transform actor,
            float configuredActivationDistance = 2.6f,
            float configuredMovementSpeed = 2.8f,
            float configuredCloseDelay = 1.25f)
        {
            panels = configuredPanels;
            openWorldOffsets = configuredOpenWorldOffsets;
            sensedActor = actor;
            activationDistance = configuredActivationDistance;
            movementSpeed = configuredMovementSpeed;
            closeDelay = configuredCloseDelay;
        }

        void Awake()
        {
            CacheDoorState();
        }

        void Update()
        {
            if (closedWorldPositions == null || closedWorldPositions.Length == 0)
            {
                return;
            }

            if (sensedActor == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                sensedActor = player != null ? player.transform : null;
            }

            bool actorInRange = IsSensedActorInRange();
            if (actorInRange)
            {
                lastActorDetectionTime = Time.time;
            }

            bool shouldOpen = actorInRange || Time.time - lastActorDetectionTime < closeDelay;
            if (shouldOpen)
            {
                SetPanelColliders(false);
            }

            bool reachedTarget = MovePanels(shouldOpen);
            IsOpen = shouldOpen && reachedTarget;

            if (!shouldOpen && reachedTarget)
            {
                SetPanelColliders(true);
            }
        }

        void CacheDoorState()
        {
            int panelCount = Mathf.Min(panels != null ? panels.Length : 0, openWorldOffsets != null ? openWorldOffsets.Length : 0);
            closedWorldPositions = new Vector3[panelCount];
            openWorldPositions = new Vector3[panelCount];
            panelColliders = new Collider[panelCount][];

            for (int i = 0; i < panelCount; i++)
            {
                Transform panel = panels[i];
                if (panel == null)
                {
                    continue;
                }

                closedWorldPositions[i] = panel.position;
                openWorldPositions[i] = panel.position + openWorldOffsets[i];
                panelColliders[i] = panel.GetComponentsInChildren<Collider>(true);
            }
        }

        bool MovePanels(bool open)
        {
            bool reachedTarget = true;
            for (int i = 0; i < closedWorldPositions.Length; i++)
            {
                Transform panel = panels[i];
                if (panel == null)
                {
                    continue;
                }

                Vector3 target = open ? openWorldPositions[i] : closedWorldPositions[i];
                panel.position = Vector3.MoveTowards(panel.position, target, movementSpeed * Time.deltaTime);
                reachedTarget &= (panel.position - target).sqrMagnitude < 0.000001f;
            }

            return reachedTarget;
        }

        bool IsSensedActorInRange()
        {
            if (sensedActor == null)
            {
                return false;
            }

            Vector3 offset = sensedActor.position - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= activationDistance * activationDistance;
        }

        void SetPanelColliders(bool enabled)
        {
            if (panelColliders == null || collidersEnabled == enabled)
            {
                return;
            }

            foreach (Collider[] colliders in panelColliders)
            {
                if (colliders == null)
                {
                    continue;
                }

                foreach (Collider collider in colliders)
                {
                    if (collider != null)
                    {
                        collider.enabled = enabled;
                    }
                }
            }

            collidersEnabled = enabled;
        }
    }
}
