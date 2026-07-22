using System.Collections;
using SpacecraftEditor;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpacecraftDamageReceiver))]
public sealed class PirateShipLifecycle : MonoBehaviour
{
    [SerializeField, Min(0f)] float destroyDelay = 0.15f;

    SpacecraftDamageReceiver receiver;
    PirateEncounterDirector director;
    bool destructionStarted;

    void Awake()
    {
        receiver = GetComponent<SpacecraftDamageReceiver>();
    }

    void OnEnable()
    {
        receiver.Destroyed += HandleDestroyed;
    }

    public void Configure(PirateEncounterDirector owner)
    {
        director = owner;
    }

    void HandleDestroyed(SpaceDamageInfo damage)
    {
        if (destructionStarted)
            return;
        destructionStarted = true;
        PirateShipAiController ai = GetComponent<PirateShipAiController>();
        if (ai != null)
            ai.enabled = false;
        SpacecraftIfcsMotor ifcs = GetComponent<SpacecraftIfcsMotor>();
        if (ifcs != null)
            ifcs.ControlsEnabled = false;
        SpacecraftWeaponSystem weapons = GetComponent<SpacecraftWeaponSystem>();
        if (weapons != null)
            weapons.ControlsEnabled = false;
        SpaceCombatVfxPool effects = SpaceCombatVfxPool.Instance;
        if (effects != null && effects.Catalog != null)
            effects.PlayOneShot(effects.Catalog.ShipDestructionPrefab, transform.position,
                transform.rotation, 1.4f, 4f);
        director?.NotifyEnemyDestroyed(gameObject);
        StartCoroutine(DestroyAfterDelay());
    }

    IEnumerator DestroyAfterDelay()
    {
        yield return new WaitForSeconds(destroyDelay);
        Destroy(gameObject);
    }

    void OnDisable()
    {
        if (receiver != null)
            receiver.Destroyed -= HandleDestroyed;
    }
}
