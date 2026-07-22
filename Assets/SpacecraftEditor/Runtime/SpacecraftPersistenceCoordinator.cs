using System.Collections;
using UnityEngine;

namespace SpacecraftEditor
{
    [DisallowMultipleComponent]
    public sealed class SpacecraftPersistenceCoordinator : MonoBehaviour
    {
        [SerializeField] private SpacecraftApp app;
        [SerializeField] private ShipAssembly assembly;
        [SerializeField] private MonoBehaviour storeBehaviour;
        [SerializeField, Min(0.1f)] private float saveDelay = 0.5f;

        private ISpacecraftBlueprintStore store;
        private bool initialized;
        private bool dirty;
        private float saveAt;
        private SpacecraftBlueprintData pendingBlueprint;

        public string ActiveSlotId => store == null ? string.Empty : store.ActiveSlotId;

        public void Configure(SpacecraftApp owner, ShipAssembly targetAssembly, MonoBehaviour blueprintStore)
        {
            app = owner;
            assembly = targetAssembly;
            storeBehaviour = blueprintStore;
        }

        private IEnumerator Start()
        {
            yield return null;
            if (app == null)
                app = GetComponentInParent<SpacecraftApp>();
            if (assembly == null)
                assembly = GetComponentInParent<ShipAssembly>();
            if (storeBehaviour == null)
            {
                var behaviours = GetComponents<MonoBehaviour>();
                for (var index = 0; index < behaviours.Length; index++)
                {
                    if (behaviours[index] is ISpacecraftBlueprintStore)
                    {
                        storeBehaviour = behaviours[index];
                        break;
                    }
                }
            }
            store = storeBehaviour as ISpacecraftBlueprintStore;
            if (app == null || assembly == null || store == null)
            {
                Debug.LogError("Spacecraft persistence references are incomplete.", this);
                yield break;
            }

            if (store.TryLoad(out var blueprint) && !app.RestoreBlueprint(blueprint))
                Debug.LogWarning($"Ignored incompatible spacecraft blueprint in slot '{store.ActiveSlotId}'.", this);

            app.StateChanged += HandleStateChanged;
            assembly.AssemblyChanged += MarkDirty;
            initialized = true;
        }

        private void Update()
        {
            if (initialized && dirty && Time.unscaledTime >= saveAt)
                Flush();
        }

        private void HandleStateChanged()
        {
            MarkDirty();
            if (app.IsFlightMode)
                Flush();
        }

        private void MarkDirty()
        {
            if (!initialized)
                return;
            var blueprint = app == null ? pendingBlueprint : app.CaptureBlueprint();
            if (blueprint == null)
            {
                pendingBlueprint = null;
                dirty = false;
                return;
            }
            pendingBlueprint = blueprint;
            dirty = true;
            saveAt = Time.unscaledTime + saveDelay;
        }

        public void Flush()
        {
            if (!initialized || !dirty || store == null)
                return;
            var blueprint = pendingBlueprint;
            if (blueprint == null && app != null)
                blueprint = app.CaptureBlueprint();
            if (blueprint == null)
                return;
            store.Save(blueprint);
            pendingBlueprint = null;
            dirty = false;
        }

        public bool SaveCurrentBlueprint()
        {
            if (!initialized || store == null || app == null)
                return false;

            pendingBlueprint = app.CaptureBlueprint();
            if (pendingBlueprint == null)
                return false;

            dirty = true;
            Flush();
            return !dirty;
        }

        private void OnDisable()
        {
            if (app != null)
                app.StateChanged -= HandleStateChanged;
            if (assembly != null)
                assembly.AssemblyChanged -= MarkDirty;
            Flush();
        }

        private void OnApplicationQuit()
        {
            Flush();
        }
    }
}
