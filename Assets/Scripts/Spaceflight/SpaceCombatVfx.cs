using System;
using SpacecraftEditor;
using UnityEngine;

[Serializable]
public sealed class SpaceWeaponVfxDefinition
{
    [SerializeField] SpaceWeaponDamageChannel damageChannel;
    [SerializeField] GameObject muzzlePrefab;
    [SerializeField] GameObject projectilePrefab;
    [SerializeField] GameObject impactPrefab;
    [SerializeField] AudioClip fireAudio;
    [SerializeField] AudioClip impactAudio;
    [SerializeField, Min(0.01f)] float muzzleScale = 1f;
    [SerializeField] Vector3 projectileScale = Vector3.one;
    [SerializeField, Min(0.01f)] float impactScale = 1f;
    [SerializeField, Min(0.02f)] float muzzleLifetime = 0.25f;

    public SpaceWeaponDamageChannel DamageChannel => damageChannel;
    public GameObject MuzzlePrefab => muzzlePrefab;
    public GameObject ProjectilePrefab => projectilePrefab;
    public GameObject ImpactPrefab => impactPrefab;
    public AudioClip FireAudio => fireAudio;
    public AudioClip ImpactAudio => impactAudio;
    public float MuzzleScale => Mathf.Max(0.01f, muzzleScale);
    public Vector3 ProjectileScale => new Vector3(
        Mathf.Max(0.01f, projectileScale.x),
        Mathf.Max(0.01f, projectileScale.y),
        Mathf.Max(0.01f, projectileScale.z));
    public float ImpactScale => Mathf.Max(0.01f, impactScale);
    public float MuzzleLifetime => Mathf.Max(0.02f, muzzleLifetime);

#if UNITY_EDITOR
    public void Configure(
        SpaceWeaponDamageChannel channel,
        GameObject muzzle,
        GameObject projectile,
        GameObject impact,
        AudioClip fire,
        AudioClip impactClip,
        float authoredMuzzleScale,
        Vector3 authoredProjectileScale,
        float authoredImpactScale,
        float authoredMuzzleLifetime)
    {
        damageChannel = channel;
        muzzlePrefab = muzzle;
        projectilePrefab = projectile;
        impactPrefab = impact;
        fireAudio = fire;
        impactAudio = impactClip;
        muzzleScale = Mathf.Max(0.01f, authoredMuzzleScale);
        projectileScale = new Vector3(
            Mathf.Max(0.01f, authoredProjectileScale.x),
            Mathf.Max(0.01f, authoredProjectileScale.y),
            Mathf.Max(0.01f, authoredProjectileScale.z));
        impactScale = Mathf.Max(0.01f, authoredImpactScale);
        muzzleLifetime = Mathf.Max(0.02f, authoredMuzzleLifetime);
    }
#endif
}

[DisallowMultipleComponent]
public sealed class SpacePooledEffect : MonoBehaviour
{
    SpaceCombatVfxPool pool;
    ParticleSystem[] particles;
    TrailRenderer[] trails;
    float releaseAt = -1f;

    public bool IsPlaying => gameObject.activeSelf;

    public void Initialize(SpaceCombatVfxPool owner)
    {
        pool = owner;
        particles = GetComponentsInChildren<ParticleSystem>(true);
        trails = GetComponentsInChildren<TrailRenderer>(true);
        gameObject.SetActive(false);
    }

    public void Activate(Transform parent)
    {
        transform.SetParent(parent == null ? pool.transform : parent, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
        releaseAt = -1f;
        gameObject.SetActive(true);
        if (trails != null)
        {
            for (int index = 0; index < trails.Length; index++)
                trails[index]?.Clear();
        }
        if (particles != null)
        {
            for (int index = 0; index < particles.Length; index++)
                particles[index]?.Play(true);
        }
    }

    public void ReleaseAfter(float seconds)
    {
        releaseAt = Time.unscaledTime + Mathf.Max(0.02f, seconds);
    }

    public void Release()
    {
        if (!gameObject.activeSelf)
            return;
        if (particles != null)
        {
            for (int index = 0; index < particles.Length; index++)
                particles[index]?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        transform.SetParent(pool.transform, false);
        gameObject.SetActive(false);
        releaseAt = -1f;
    }

    void Update()
    {
        if (releaseAt >= 0f && Time.unscaledTime >= releaseAt)
            Release();
    }
}
