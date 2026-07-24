using UnityEngine;

public enum SpaceflightCameraMode
{
    ThirdPerson,
    Cockpit
}

public enum SpaceflightHudPresentationMode
{
    ThirdPerson,
    Cockpit
}

public enum SpaceflightRadarContactKind
{
    Planet,
    Combatant,
    Asteroid
}

public struct SpaceflightRadarContact
{
    public Vector3 localDirection;
    public float distance;
    public Color color;
    public SpaceflightRadarContactKind kind;
    public bool selected;
}

public struct SpaceflightTelemetrySnapshot
{
    public bool valid;
    public float speed;
    public float targetSpeed;
    public float closingSpeed;
    public Vector3 acceleration;
    public Vector3 appliedLocalForce;
    public float shipMass;
    public SpaceflightVelocityReference velocityReference;
    public bool cinematicTransit;
    public SpaceflightInteractionMode interactionMode;
    public SpaceflightScaleLayer scaleLayer;
    public bool highSpeedTravel;
    public Vector3 kilometerLayerVelocity;
    public float hullRatio;
    public float boostRatio;
    public float speedLimit;
    public float controlAuthority;
    public SpacecraftEditor.SpacecraftAssistMode assistMode;
    public int weaponGroup;
    public int ammunition;
    public int ammunitionCapacity;
    public float capacitorRatio;
    public float heatRatio;
    public string mountLabel;
    public string lockLabel;
    public string targetName;
    public double targetDistance;
    public SpaceWeaponTargetKind targetKind;
}
