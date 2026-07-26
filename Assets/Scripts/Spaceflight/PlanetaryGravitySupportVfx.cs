using SpacecraftEditor;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlanetaryGravitySupportVfx : MonoBehaviour
{
    [SerializeField] SpacecraftIfcsMotor ifcsMotor;
    [SerializeField] ParticleSystem supportParticles;
    [SerializeField] Bounds localShipBounds;

    public void Configure(
        SpacecraftIfcsMotor motor,
        Bounds shipBounds)
    {
        ifcsMotor = motor;
        localShipBounds = shipBounds;
        EnsurePresentation();
        SetLoad(0f);
    }

    void Update()
    {
        float load = ifcsMotor != null
            && ifcsMotor.Telemetry.planetaryFlightActive
            ? ifcsMotor.Telemetry.gravitySupportLoad
            : 0f;
        SetLoad(load);
    }

    void EnsurePresentation()
    {
        if (supportParticles != null)
            return;

        Transform existing = transform.Find("GravitySupportPresentation");
        GameObject root = existing != null
            ? existing.gameObject
            : new GameObject("GravitySupportPresentation");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = new Vector3(
            localShipBounds.center.x,
            localShipBounds.min.y - 0.1f,
            localShipBounds.center.z);
        root.transform.localRotation =
            Quaternion.Euler(90f, 0f, 0f);
        // Do not use ?? with UnityEngine.Object. A destroyed native component
        // can remain a non-null managed reference and produce an unbound
        // ParticleSystem module.
        supportParticles = root.GetComponent<ParticleSystem>();
        if (supportParticles == null)
            supportParticles = root.AddComponent<ParticleSystem>();
        if (supportParticles == null)
        {
            Debug.LogWarning(
                "Unable to create the planetary gravity support particle system.",
                this);
            return;
        }

        var main = supportParticles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace =
            ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.18f, 0.8f, 1f, 0.08f),
            new Color(0.35f, 0.95f, 1f, 0.28f));
        main.maxParticles = 160;

        var emission = supportParticles.emission;
        emission.rateOverTime = 0f;
        var shape = supportParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(
            Mathf.Max(0.5f, localShipBounds.size.x * 0.72f),
            Mathf.Max(0.5f, localShipBounds.size.z * 0.55f),
            0.05f);
    }

    void SetLoad(float load)
    {
        if (supportParticles == null)
            return;
        load = Mathf.Clamp01(load);
        var emission = supportParticles.emission;
        emission.rateOverTime = Mathf.Lerp(0f, 72f, load);
        if (load > 0.01f && !supportParticles.isPlaying)
            supportParticles.Play(true);
        else if (load <= 0.01f && supportParticles.isPlaying)
            supportParticles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmitting);
    }

    void OnDisable()
    {
        SetLoad(0f);
    }
}
