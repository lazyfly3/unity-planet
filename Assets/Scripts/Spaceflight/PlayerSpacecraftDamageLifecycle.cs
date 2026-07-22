using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpacecraftDamageReceiver))]
public sealed class PlayerSpacecraftDamageLifecycle : MonoBehaviour
{
    [SerializeField, Min(0f)] float recoveryDelay = 1.5f;

    SpacecraftDamageReceiver receiver;
    InterstellarShipController controller;
    Coroutine recoveryRoutine;

    void Awake()
    {
        receiver = GetComponent<SpacecraftDamageReceiver>();
        controller = GetComponent<InterstellarShipController>();
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (manager != null)
            receiver.SetIntegrity(Mathf.Max(1f, manager.SpacecraftHullIntegrity));
    }

    void OnEnable()
    {
        receiver.Destroyed += HandleDestroyed;
    }

    void OnDisable()
    {
        receiver.Destroyed -= HandleDestroyed;
        if (recoveryRoutine != null)
        {
            StopCoroutine(recoveryRoutine);
            recoveryRoutine = null;
        }
    }

    void HandleDestroyed(SpaceDamageInfo damage)
    {
        SpaceCombatVfxPool effects = SpaceCombatVfxPool.Instance;
        if (effects != null && effects.Catalog != null)
            effects.PlayOneShot(effects.Catalog.ShipDestructionPrefab, transform.position,
                transform.rotation, 1.6f, 4f);
        if (controller != null)
            controller.ControlsEnabled = false;
        if (recoveryRoutine == null)
            recoveryRoutine = StartCoroutine(Recover());
    }

    IEnumerator Recover()
    {
        yield return new WaitForSecondsRealtime(recoveryDelay);
        GalaxyTravelManager.Instance?.RecoverFromSpacecraftDestruction();
        recoveryRoutine = null;
    }
}
