using System.Collections.Generic;
using SpacecraftEditor;
using UnityEngine;

[CreateAssetMenu(menuName = "Spaceflight/Combat VFX Catalog", fileName = "SpaceCombatVfxCatalog")]
public sealed class SpaceCombatVfxCatalog : ScriptableObject
{
    [SerializeField] SpaceWeaponVfxDefinition[] weapons;
    [SerializeField] GameObject asteroidDestructionPrefab;
    [SerializeField] GameObject shipDestructionPrefab;

    public GameObject AsteroidDestructionPrefab => asteroidDestructionPrefab;
    public GameObject ShipDestructionPrefab => shipDestructionPrefab;

    public SpaceWeaponVfxDefinition Find(SpaceWeaponDamageChannel channel)
    {
        if (weapons == null)
            return null;
        for (int index = 0; index < weapons.Length; index++)
        {
            if (weapons[index] != null && weapons[index].DamageChannel == channel)
                return weapons[index];
        }
        return null;
    }

    public IEnumerable<GameObject> EnumeratePrefabs()
    {
        if (weapons != null)
        {
            for (int index = 0; index < weapons.Length; index++)
            {
                SpaceWeaponVfxDefinition definition = weapons[index];
                if (definition == null)
                    continue;
                if (definition.MuzzlePrefab != null)
                    yield return definition.MuzzlePrefab;
                if (definition.ProjectilePrefab != null)
                    yield return definition.ProjectilePrefab;
                if (definition.ImpactPrefab != null)
                    yield return definition.ImpactPrefab;
            }
        }
        if (asteroidDestructionPrefab != null)
            yield return asteroidDestructionPrefab;
        if (shipDestructionPrefab != null)
            yield return shipDestructionPrefab;
    }

#if UNITY_EDITOR
    public void Configure(
        SpaceWeaponVfxDefinition[] definitions,
        GameObject asteroidExplosion,
        GameObject shipExplosion)
    {
        weapons = definitions;
        asteroidDestructionPrefab = asteroidExplosion;
        shipDestructionPrefab = shipExplosion;
    }
#endif
}
