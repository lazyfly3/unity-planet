using System;
using System.Collections.Generic;
using SpacecraftEditor;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SpacecraftWeaponSystem : MonoBehaviour
{
    [SerializeField] ShipAssembly assembly;
    [SerializeField] Rigidbody shipBody;
    [SerializeField] InterstellarFlightRuntime flightRuntime;
    [SerializeField] MonoBehaviour commandSourceComponent;
    [SerializeField] Transform targetingView;
    [SerializeField] SpaceCombatVfxPool vfxPool;
    [SerializeField, Min(1)] int projectilePoolSize = 96;
    [SerializeField, Min(1f)] float capacitorCapacity = 100f;
    [SerializeField, Min(0f)] float capacitorRegeneration = 20f;
    [SerializeField, Min(0.1f)] float crosshairLaunchClearance = 2f;
    [SerializeField, Min(0f)] float damageMultiplier = 1f;
    [SerializeField] bool controlsEnabled = true;

    SpacecraftProjectile[] projectiles;
    Material kineticMaterial;
    Material energyMaterial;
    Mesh projectileMesh;
    Transform projectileRoot;
    ISpacecraftWeaponCommandSource commandSource;
    SpaceCombatant ownerCombatant;
    ISpaceWeaponTarget currentTarget;
    readonly Dictionary<WeaponPart, Vector3> aimDirections = new Dictionary<WeaponPart, Vector3>();
    readonly RaycastHit[] lineOfSightHits = new RaycastHit[16];
    int projectileCursor;
    int selectedGroup = 1;
    float capacitor;
    uint shotSequence;
    float nextTargetSearchTime;
    bool selectedGroupHasGimbal;
    bool targetLockRequested;

    public bool ControlsEnabled { get => controlsEnabled; set => controlsEnabled = value; }
    public float DamageMultiplier => damageMultiplier;
    public int SelectedGroup => selectedGroup;
    public float Capacitor => capacitor;
    public float CapacitorCapacity => capacitorCapacity;
    public float CapacitorRatio => capacitorCapacity <= 0f ? 0f : capacitor / capacitorCapacity;
    public int SelectedAmmunition { get; private set; }
    public int SelectedAmmunitionCapacity { get; private set; }
    public float SelectedHeat { get; private set; }
    public bool GroupOneAvailable { get; private set; }
    public bool GroupTwoAvailable { get; private set; }
    public ISpaceWeaponTarget CurrentTarget => currentTarget;
    public bool HasTargetLock => IsTargetAlive(currentTarget);
    public SpaceWeaponTargetKind CurrentTargetKind => HasTargetLock
        ? currentTarget.TargetKind
        : SpaceWeaponTargetKind.None;
    public bool HasGimbalLock => HasTargetLock && selectedGroupHasGimbal;
    public bool TargetLockEnabled => targetLockRequested && selectedGroupHasGimbal;
    public bool SelectedGroupHasGimbal => selectedGroupHasGimbal;
    public string SelectedMountLabel { get; private set; } = "FIXED";

    void Awake()
    {
        if (assembly == null)
            assembly = GetComponent<ShipAssembly>();
        if (shipBody == null)
            shipBody = GetComponent<Rigidbody>();
        if (flightRuntime == null)
            flightRuntime = FindObjectOfType<InterstellarFlightRuntime>();
        if (flightRuntime != null)
        {
            flightRuntime.OriginShifted += HandleOriginShift;
            flightRuntime.UniverseRelocated += HandleUniverseRelocated;
        }
        ownerCombatant = GetComponent<SpaceCombatant>();
        if (commandSourceComponent == null)
            commandSourceComponent = GetComponent<PlayerSpacecraftWeaponInput>();
        SetCommandSource(commandSourceComponent);
        if (vfxPool == null)
            vfxPool = SpaceCombatVfxPool.Instance ?? FindObjectOfType<SpaceCombatVfxPool>() ??
                      gameObject.AddComponent<SpaceCombatVfxPool>();
        capacitor = capacitorCapacity;
        BuildPool();
    }

    void Update()
    {
        if (commandSource != null)
        {
            SpacecraftFireCommand command = commandSource.FireCommand;
            selectedGroup = ResolveAvailableGroup(command.selectedGroup);
            targetLockRequested = command.targetLockEnabled;
        }
        RefreshTelemetry();
    }

    int ResolveAvailableGroup(int requestedGroup)
    {
        requestedGroup = Mathf.Clamp(requestedGroup, 1, 2);
        if (assembly == null)
            return requestedGroup;

        int fallbackGroup = 0;
        for (int index = 0; index < assembly.Weapons.Count; index++)
        {
            WeaponPart weapon = assembly.Weapons[index];
            if (weapon == null || weapon.Weapon == null)
                continue;
            if (weapon.FireGroup == requestedGroup)
                return requestedGroup;
            if (fallbackGroup == 0)
                fallbackGroup = weapon.FireGroup;
        }
        return fallbackGroup == 0 ? requestedGroup : fallbackGroup;
    }

    void FixedUpdate()
    {
        float step = Time.fixedDeltaTime;
        capacitor = Mathf.Min(capacitorCapacity, capacitor + capacitorRegeneration * step);
        if (assembly == null)
            return;

        UpdateTargetAndAim(step);
        for (int index = 0; index < assembly.Weapons.Count; index++)
            assembly.Weapons[index]?.TickCooling(step);

        bool fire = controlsEnabled && commandSource != null && commandSource.FireCommand.fireHeld;
        if (!fire)
            return;

        for (int index = 0; index < assembly.Weapons.Count; index++)
        {
            WeaponPart weapon = assembly.Weapons[index];
            if (weapon == null || weapon.FireGroup != selectedGroup)
                continue;
            if (!weapon.TryBeginShot(Time.fixedTime, capacitor, out float cost, out Transform muzzle))
                continue;
            capacitor = Mathf.Max(0f, capacitor - cost);
            Fire(weapon, muzzle);
        }
    }

    void Fire(WeaponPart weapon, Transform muzzle)
    {
        SpacecraftWeaponDefinition profile = weapon.Weapon;
        if (profile == null || muzzle == null)
            return;
        bool useTrackedMuzzle = profile.MountMode == SpaceWeaponMountMode.Gimbaled && HasTargetLock;
        Vector3 launchPosition;
        Vector3 baseDirection;
        if (useTrackedMuzzle)
        {
            launchPosition = muzzle.position;
            baseDirection = ResolveShotDirection(weapon, muzzle, profile);
        }
        else
        {
            ResolveCrosshairShotPose(out launchPosition, out baseDirection);
        }
        Vector3 forward = ApplySpread(baseDirection, profile.SpreadDegrees, ++shotSequence);
        Vector3 inheritedVelocity = shipBody == null
            ? Vector3.zero
            : useTrackedMuzzle
                ? shipBody.GetPointVelocity(muzzle.position)
                : shipBody.velocity;
        Vector3 velocity = inheritedVelocity + forward * profile.ProjectileSpeed;
        SpacecraftProjectile projectile = AcquireProjectile();
        SpaceWeaponVfxDefinition vfx = vfxPool == null || vfxPool.Catalog == null
            ? null
            : vfxPool.Catalog.Find(profile.DamageChannel);
        projectile.Launch(
            transform,
            ownerCombatant,
            launchPosition + forward * 0.08f,
            velocity,
            profile.Range,
            profile.Damage * damageMultiplier,
            profile.ImpactImpulse,
            profile.DamageChannel,
            profile.DamageChannel == SpaceWeaponDamageChannel.Energy ? energyMaterial : kineticMaterial,
            profile.MountSize,
            vfxPool,
            vfx);

        if (vfxPool != null && vfx != null)
        {
            float mountScale = 0.85f + (int)profile.MountSize * 0.2f;
            vfxPool.PlayOneShot(vfx.MuzzlePrefab, launchPosition, Quaternion.LookRotation(forward),
                vfx.MuzzleScale * mountScale, vfx.MuzzleLifetime);
            vfxPool.PlayAudio(vfx.FireAudio, launchPosition, 0.65f);
        }

        if (shipBody != null && profile.RecoilImpulse > 0f)
            shipBody.AddForceAtPosition(-forward * profile.RecoilImpulse, muzzle.position, ForceMode.Impulse);
    }

    void RefreshTelemetry()
    {
        SelectedAmmunition = 0;
        SelectedAmmunitionCapacity = 0;
        SelectedHeat = 0f;
        GroupOneAvailable = false;
        GroupTwoAvailable = false;
        int heatCount = 0;
        if (assembly == null)
            return;
        for (int index = 0; index < assembly.Weapons.Count; index++)
        {
            WeaponPart weapon = assembly.Weapons[index];
            if (weapon == null)
                continue;
            if (weapon.FireGroup == 1)
                GroupOneAvailable = true;
            else if (weapon.FireGroup == 2)
                GroupTwoAvailable = true;
            if (weapon.FireGroup != selectedGroup)
                continue;
            if (weapon.Weapon != null && weapon.Weapon.UsesAmmunition)
            {
                SelectedAmmunition += weapon.CurrentAmmunition;
                SelectedAmmunitionCapacity += weapon.Weapon.AmmunitionCapacity;
            }
            SelectedHeat += weapon.Heat;
            heatCount++;
        }
        if (heatCount > 0)
            SelectedHeat /= heatCount;
        SelectedMountLabel = ResolveSelectedMountLabel();
    }

    public bool SetCommandSource(MonoBehaviour source)
    {
        if (source != null && !(source is ISpacecraftWeaponCommandSource))
        {
            Debug.LogError($"{source.name} does not implement {nameof(ISpacecraftWeaponCommandSource)}.", source);
            return false;
        }
        commandSourceComponent = source;
        commandSource = source as ISpacecraftWeaponCommandSource;
        return commandSource != null;
    }

    public void SetDamageMultiplier(float value)
    {
        damageMultiplier = Mathf.Max(0f, value);
    }

    public void SetTargetingView(Transform view)
    {
        targetingView = view;
    }

    void UpdateTargetAndAim(float deltaTime)
    {
        if (assembly == null)
            return;
        bool hasSelectedWeapon = false;
        float maximumRange = 0f;
        float cone = 18f;
        selectedGroupHasGimbal = false;
        for (int index = 0; index < assembly.Weapons.Count; index++)
        {
            WeaponPart weapon = assembly.Weapons[index];
            SpacecraftWeaponDefinition profile = weapon == null ? null : weapon.Weapon;
            if (profile == null || weapon.FireGroup != selectedGroup)
                continue;
            hasSelectedWeapon = true;
            maximumRange = Mathf.Max(maximumRange, profile.Range);
            if (profile.MountMode == SpaceWeaponMountMode.Gimbaled)
                selectedGroupHasGimbal = true;
        }

        if (!hasSelectedWeapon || !selectedGroupHasGimbal || !targetLockRequested)
            currentTarget = null;
        else
        {
            if (!IsTargetValid(currentTarget, maximumRange, cone))
                currentTarget = null;
            if (Time.fixedTime >= nextTargetSearchTime)
            {
                currentTarget = FindBestTarget(maximumRange, cone);
                nextTargetSearchTime = Time.fixedTime + 0.1f;
            }
        }

        for (int index = 0; index < assembly.Weapons.Count; index++)
        {
            WeaponPart weapon = assembly.Weapons[index];
            SpacecraftWeaponDefinition profile = weapon == null ? null : weapon.Weapon;
            if (profile == null)
                continue;
            Transform muzzle = weapon.PrimaryMuzzle;
            Vector3 baseForward = weapon.RestFireDirection;
            Vector3 desired = baseForward;
            if (profile.MountMode == SpaceWeaponMountMode.Gimbaled && HasTargetLock)
                desired = CalculateInterceptDirection(muzzle.position, currentTarget, profile.ProjectileSpeed, baseForward);
            desired = ClampToCone(baseForward, desired, profile.GimbalConeDegrees);
            if (!aimDirections.TryGetValue(weapon, out Vector3 current) || current.sqrMagnitude < 0.5f)
                current = baseForward;
            float step = profile.MountMode == SpaceWeaponMountMode.Gimbaled
                ? profile.GimbalTrackingDegreesPerSecond * deltaTime
                : 720f * deltaTime;
            Vector3 aimDirection = Vector3.RotateTowards(current, desired, step * Mathf.Deg2Rad, 0f).normalized;
            aimDirections[weapon] = aimDirection;
            if (profile.MountMode == SpaceWeaponMountMode.Gimbaled)
                weapon.ApplyAimDirection(aimDirection);
            else
                weapon.ResetAimVisual();
        }
    }

    ISpaceWeaponTarget FindBestTarget(float range, float coneDegrees)
    {
        if (ownerCombatant == null)
            ownerCombatant = GetComponent<SpaceCombatant>();
        SpaceWeaponTargetRegistry.RemoveInvalidEntries();
        ISpaceWeaponTarget combatant = FindBestTargetOfKind(
            SpaceWeaponTargetKind.Combatant, range, coneDegrees);
        return combatant ?? FindBestTargetOfKind(SpaceWeaponTargetKind.Asteroid, range, coneDegrees);
    }

    ISpaceWeaponTarget FindBestTargetOfKind(
        SpaceWeaponTargetKind kind,
        float range,
        float coneDegrees)
    {
        ResolveTargetingRay(out Vector3 origin, out Vector3 forward);
        float cosine = Mathf.Cos(coneDegrees * Mathf.Deg2Rad);
        float bestAlignment = -1f;
        float bestDistance = float.PositiveInfinity;
        ISpaceWeaponTarget best = null;
        IReadOnlyList<ISpaceWeaponTarget> candidates = SpaceWeaponTargetRegistry.Active;
        for (int index = 0; index < candidates.Count; index++)
        {
            ISpaceWeaponTarget candidate = candidates[index];
            if (!IsTargetAlive(candidate) || !candidate.IsTargetable || candidate.TargetKind != kind)
                continue;
            if (candidate.TargetKind == SpaceWeaponTargetKind.Combatant &&
                (!(candidate is SpaceCombatant combatant) || ownerCombatant == null ||
                 !ownerCombatant.IsHostileTo(combatant)))
                continue;
            Vector3 offset = candidate.AimPosition - origin;
            float distance = Vector3.Distance(transform.position, candidate.AimPosition);
            if (distance <= 0.01f || distance > range)
                continue;
            float viewDistance = offset.magnitude;
            if (viewDistance <= 0.01f)
                continue;
            float alignment = Vector3.Dot(forward, offset / viewDistance);
            if (alignment < cosine || !HasLineOfSight(origin, candidate, viewDistance))
                continue;
            if (alignment > bestAlignment + 0.0001f ||
                (Mathf.Abs(alignment - bestAlignment) <= 0.0001f && distance < bestDistance))
            {
                bestAlignment = alignment;
                bestDistance = distance;
                best = candidate;
            }
        }
        return best;
    }

    bool IsTargetValid(ISpaceWeaponTarget target, float range, float coneDegrees)
    {
        if (!IsTargetAlive(target) || !target.IsTargetable)
            return false;
        if (target.TargetKind == SpaceWeaponTargetKind.Combatant &&
            (!(target is SpaceCombatant combatant) || ownerCombatant == null ||
             !ownerCombatant.IsHostileTo(combatant)))
            return false;
        float distance = Vector3.Distance(transform.position, target.AimPosition);
        if (distance <= 0.01f || distance > range)
            return false;
        ResolveTargetingRay(out Vector3 origin, out Vector3 forward);
        Vector3 offset = target.AimPosition - origin;
        float viewDistance = offset.magnitude;
        if (viewDistance <= 0.01f)
            return false;
        float cosine = Mathf.Cos(coneDegrees * Mathf.Deg2Rad);
        return Vector3.Dot(forward, offset / viewDistance) >= cosine &&
               HasLineOfSight(origin, target, viewDistance);
    }

    void ResolveTargetingRay(out Vector3 origin, out Vector3 forward)
    {
        Transform view = targetingView == null ? transform : targetingView;
        origin = view.position;
        forward = view.forward;
    }

    void ResolveCrosshairShotPose(out Vector3 position, out Vector3 forward)
    {
        ResolveTargetingRay(out Vector3 origin, out forward);
        float depth = 0.5f;
        if (shipBody != null)
        {
            float shipDepth = Vector3.Dot(shipBody.worldCenterOfMass - origin, forward);
            depth = Mathf.Max(0.25f, shipDepth) + crosshairLaunchClearance;
        }
        position = origin + forward * depth;
    }

    bool HasLineOfSight(Vector3 origin, ISpaceWeaponTarget candidate, float distance)
    {
        Vector3 direction = (candidate.AimPosition - origin).normalized;
        int count = Physics.RaycastNonAlloc(origin, direction, lineOfSightHits, distance,
            ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        Collider nearestCollider = null;
        for (int index = 0; index < count; index++)
        {
            Collider collider = lineOfSightHits[index].collider;
            if (collider == null || (ownerCombatant != null && ownerCombatant.Owns(collider.transform)))
                continue;
            if (lineOfSightHits[index].distance >= nearest)
                continue;
            nearest = lineOfSightHits[index].distance;
            nearestCollider = collider;
        }
        return nearestCollider != null && candidate.Owns(nearestCollider.transform);
    }

    Vector3 ResolveShotDirection(WeaponPart weapon, Transform muzzle, SpacecraftWeaponDefinition profile)
    {
        if (profile.MountMode != SpaceWeaponMountMode.Gimbaled)
            return muzzle.forward.normalized;
        return aimDirections.TryGetValue(weapon, out Vector3 direction) && direction.sqrMagnitude > 0.5f
            ? direction.normalized
            : muzzle.forward.normalized;
    }

    static Vector3 CalculateInterceptDirection(
        Vector3 origin,
        ISpaceWeaponTarget target,
        float projectileSpeed,
        Vector3 fallback)
    {
        Vector3 offset = target.AimPosition - origin;
        Vector3 velocity = target.Velocity;
        float speedSquared = projectileSpeed * projectileSpeed;
        float a = velocity.sqrMagnitude - speedSquared;
        float b = 2f * Vector3.Dot(offset, velocity);
        float c = offset.sqrMagnitude;
        float time = 0f;
        if (Mathf.Abs(a) < 0.0001f)
        {
            if (Mathf.Abs(b) > 0.0001f)
                time = Mathf.Max(0f, -c / b);
        }
        else
        {
            float discriminant = b * b - 4f * a * c;
            if (discriminant >= 0f)
            {
                float root = Mathf.Sqrt(discriminant);
                float first = (-b - root) / (2f * a);
                float second = (-b + root) / (2f * a);
                time = first > 0f ? first : second > 0f ? second : 0f;
            }
        }
        Vector3 direction = offset + velocity * time;
        return direction.sqrMagnitude < 0.0001f ? fallback : direction.normalized;
    }

    static Vector3 ClampToCone(Vector3 axis, Vector3 direction, float coneDegrees)
    {
        axis.Normalize();
        direction.Normalize();
        float angle = Vector3.Angle(axis, direction);
        if (angle <= coneDegrees)
            return direction;
        return Vector3.RotateTowards(axis, direction, coneDegrees * Mathf.Deg2Rad, 0f).normalized;
    }

    static bool IsTargetAlive(ISpaceWeaponTarget target)
    {
        return SpaceWeaponTargetRegistry.IsUnityObjectAlive(target);
    }

    string ResolveSelectedMountLabel()
    {
        if (assembly == null)
            return "FIXED";
        bool fixedMount = false;
        bool gimbaled = false;
        for (int index = 0; index < assembly.Weapons.Count; index++)
        {
            WeaponPart weapon = assembly.Weapons[index];
            if (weapon == null || weapon.FireGroup != selectedGroup || weapon.Weapon == null)
                continue;
            if (weapon.Weapon.MountMode == SpaceWeaponMountMode.Gimbaled)
                gimbaled = true;
            else
                fixedMount = true;
        }
        return fixedMount && gimbaled ? "MIXED" : gimbaled ? "GIMBAL" : "FIXED";
    }

    SpacecraftProjectile AcquireProjectile()
    {
        for (int index = 0; index < projectiles.Length; index++)
        {
            int candidateIndex = (projectileCursor + index) % projectiles.Length;
            if (!projectiles[candidateIndex].IsActive)
            {
                projectileCursor = (candidateIndex + 1) % projectiles.Length;
                return projectiles[candidateIndex];
            }
        }
        SpacecraftProjectile result = projectiles[projectileCursor];
        projectileCursor = (projectileCursor + 1) % projectiles.Length;
        result.Release();
        return result;
    }

    void BuildPool()
    {
        projectilePoolSize = Mathf.Max(8, projectilePoolSize);
        projectileMesh = CreateOctahedron();
        kineticMaterial = CreateProjectileMaterial("KineticProjectile", new Color(1f, 0.68f, 0.12f), 3.2f);
        energyMaterial = CreateProjectileMaterial("EnergyProjectile", new Color(0.08f, 0.85f, 1f), 4.5f);
        projectileRoot = new GameObject("ProjectilePool").transform;
        projectileRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        projectiles = new SpacecraftProjectile[projectilePoolSize];
        for (int index = 0; index < projectiles.Length; index++)
        {
            var instance = new GameObject("Projectile_" + index.ToString("00"), typeof(MeshFilter), typeof(MeshRenderer));
            instance.transform.SetParent(projectileRoot, false);
            instance.GetComponent<MeshFilter>().sharedMesh = projectileMesh;
            SpacecraftProjectile projectile = instance.AddComponent<SpacecraftProjectile>();
            projectile.Initialize(instance.GetComponent<MeshRenderer>());
            projectiles[index] = projectile;
        }
    }

    static Material CreateProjectileMaterial(string name, Color color, float emission)
    {
        Shader shader = Shader.Find("Standard");
        var material = new Material(shader) { name = name, color = color };
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color * emission);
        material.SetFloat("_Glossiness", 0.5f);
        return material;
    }

    static Mesh CreateOctahedron()
    {
        var mesh = new Mesh { name = "PooledProjectileMesh" };
        mesh.vertices = new[]
        {
            Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
        };
        mesh.triangles = new[]
        {
            0,4,3, 0,2,4, 0,5,2, 0,3,5,
            1,3,4, 1,4,2, 1,2,5, 1,5,3
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Vector3 ApplySpread(Vector3 forward, float degrees, uint sequence)
    {
        if (degrees <= 0.0001f)
            return forward.normalized;
        float a = Hash01(sequence * 0x9E3779B9u) * Mathf.PI * 2f;
        float radius = Mathf.Sqrt(Hash01(sequence ^ 0xA511E9B3u)) * Mathf.Tan(degrees * Mathf.Deg2Rad);
        Vector3 right = Vector3.Cross(Mathf.Abs(forward.y) > 0.95f ? Vector3.forward : Vector3.up, forward).normalized;
        Vector3 up = Vector3.Cross(forward, right).normalized;
        return (forward + right * (Mathf.Cos(a) * radius) + up * (Mathf.Sin(a) * radius)).normalized;
    }

    static float Hash01(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return (value & 0x00FFFFFFu) / 16777216f;
    }

    void OnDestroy()
    {
        if (flightRuntime != null)
        {
            flightRuntime.OriginShifted -= HandleOriginShift;
            flightRuntime.UniverseRelocated -= HandleUniverseRelocated;
        }
        if (projectileMesh != null)
            Destroy(projectileMesh);
        if (kineticMaterial != null)
            Destroy(kineticMaterial);
        if (energyMaterial != null)
            Destroy(energyMaterial);
        if (projectileRoot != null)
            Destroy(projectileRoot.gameObject);
    }

    void HandleOriginShift(Vector3 shift)
    {
        if (projectiles == null)
            return;
        for (int index = 0; index < projectiles.Length; index++)
        {
            if (projectiles[index] != null && projectiles[index].IsActive)
                projectiles[index].ShiftPosition(shift);
        }
        if (vfxPool != null)
            vfxPool.ShiftActiveEffects(shift);
    }

    void HandleUniverseRelocated(DoubleVector3 previousPosition, DoubleVector3 currentPosition)
    {
        ReleaseTransientCombatObjects();
    }

    public void ReleaseTransientCombatObjects()
    {
        if (projectiles != null)
        {
            for (int index = 0; index < projectiles.Length; index++)
            {
                if (projectiles[index] != null && projectiles[index].IsActive)
                    projectiles[index].Release();
            }
        }
        currentTarget = null;
        aimDirections.Clear();
        if (vfxPool != null)
            vfxPool.ReleaseAllActiveEffects();
    }
}

[DisallowMultipleComponent]
public sealed class SpacecraftProjectile : MonoBehaviour
{
    readonly RaycastHit[] hitBuffer = new RaycastHit[12];
    MeshRenderer meshRenderer;
    Transform owner;
    SpaceCombatant ownerCombatant;
    Vector3 velocity;
    float remainingDistance;
    float damage;
    float impactImpulse;
    float radius;
    SpaceWeaponDamageChannel channel;
    SpaceCombatVfxPool vfxPool;
    SpaceWeaponVfxDefinition vfx;
    SpacePooledEffect projectileVisual;

    public bool IsActive => gameObject.activeSelf;

    public void Initialize(MeshRenderer renderer)
    {
        meshRenderer = renderer;
        gameObject.SetActive(false);
    }

    public void Launch(
        Transform source,
        SpaceCombatant sourceCombatant,
        Vector3 position,
        Vector3 shotVelocity,
        float range,
        float shotDamage,
        float impulse,
        SpaceWeaponDamageChannel damageChannel,
        Material material,
        SpaceWeaponMountSize size,
        SpaceCombatVfxPool effects,
        SpaceWeaponVfxDefinition effectDefinition)
    {
        owner = source;
        ownerCombatant = sourceCombatant;
        velocity = shotVelocity;
        remainingDistance = Mathf.Max(1f, range);
        damage = shotDamage;
        impactImpulse = impulse;
        channel = damageChannel;
        radius = 0.025f * (int)size;
        transform.position = position;
        transform.rotation = Quaternion.LookRotation(shotVelocity.normalized);
        transform.localScale = new Vector3(radius * 2f, radius * 2f, radius * 7f);
        vfxPool = effects;
        vfx = effectDefinition;
        if (meshRenderer != null)
        {
            meshRenderer.sharedMaterial = material;
            meshRenderer.enabled = vfx == null || vfx.ProjectilePrefab == null;
        }
        gameObject.SetActive(true);
        if (vfxPool != null && vfx != null && vfx.ProjectilePrefab != null)
        {
            projectileVisual = vfxPool.Acquire(vfx.ProjectilePrefab, transform);
            if (projectileVisual != null)
            {
                float mountScale = 0.55f + (int)size * 0.15f;
                projectileVisual.transform.localScale = vfx.ProjectileScale * mountScale;
            }
        }
    }

    void FixedUpdate()
    {
        float distance = velocity.magnitude * Time.fixedDeltaTime;
        if (distance <= 0.0001f || remainingDistance <= 0f)
        {
            Release();
            return;
        }

        Vector3 direction = velocity.normalized;
        int count = Physics.SphereCastNonAlloc(
            transform.position,
            radius,
            direction,
            hitBuffer,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore);
        int hitIndex = FindNearestValidHit(count);
        if (hitIndex >= 0)
        {
            RaycastHit hit = hitBuffer[hitIndex];
            ApplyHit(hit, direction);
            Release();
            return;
        }

        transform.position += velocity * Time.fixedDeltaTime;
        remainingDistance -= distance;
        if (remainingDistance <= 0f)
            Release();
    }

    int FindNearestValidHit(int count)
    {
        int result = -1;
        float nearest = float.PositiveInfinity;
        for (int index = 0; index < count; index++)
        {
            Collider collider = hitBuffer[index].collider;
            if (collider == null || (owner != null && collider.transform.IsChildOf(owner)))
                continue;
            SpaceCombatant targetCombatant = collider.GetComponentInParent<SpaceCombatant>();
            if (ownerCombatant != null && targetCombatant != null &&
                ownerCombatant.Faction != SpaceCombatFaction.Neutral &&
                ownerCombatant.Faction == targetCombatant.Faction)
                continue;
            if (hitBuffer[index].distance < nearest)
            {
                nearest = hitBuffer[index].distance;
                result = index;
            }
        }
        return result;
    }

    void ApplyHit(RaycastHit hit, Vector3 direction)
    {
        ISpaceDamageable damageable = FindDamageable(hit.collider == null ? null : hit.collider.transform);
        Vector3 impulse = direction * impactImpulse;
        if (damageable != null)
        {
            damageable.ApplyDamage(new SpaceDamageInfo(
                damage,
                hit.point,
                impulse,
                SpaceDamageType.Projectile,
                channel,
                owner == null ? null : owner.gameObject));
        }
        else if (hit.rigidbody != null)
        {
            hit.rigidbody.AddForceAtPosition(impulse, hit.point, ForceMode.Impulse);
        }
        if (vfxPool != null && vfx != null)
        {
            vfxPool.PlayOneShot(vfx.ImpactPrefab, hit.point, Quaternion.LookRotation(hit.normal),
                vfx.ImpactScale, 2f);
            vfxPool.PlayAudio(vfx.ImpactAudio, hit.point, 0.8f);
        }
    }

    static ISpaceDamageable FindDamageable(Transform start)
    {
        Transform current = start;
        while (current != null)
        {
            var found = current.GetComponent(typeof(ISpaceDamageable)) as ISpaceDamageable;
            if (found != null)
                return found;
            current = current.parent;
        }
        return null;
    }

    public void Release()
    {
        gameObject.SetActive(false);
        if (projectileVisual != null)
        {
            projectileVisual.Release();
            projectileVisual = null;
        }
        owner = null;
        ownerCombatant = null;
        velocity = Vector3.zero;
        remainingDistance = 0f;
        vfxPool = null;
        vfx = null;
    }

    public void ShiftPosition(Vector3 shift)
    {
        if (IsActive)
            transform.position -= shift;
    }
}
