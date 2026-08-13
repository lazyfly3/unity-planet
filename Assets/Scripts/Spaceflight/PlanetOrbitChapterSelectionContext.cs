using UnityEngine;

public static class PlanetOrbitChapterSelectionContext
{
    public static string PlanetId { get; private set; }
    public static string MissionId { get; private set; }
    public static string MissionName { get; private set; }
    public static Vector3 LandingDirection { get; private set; }
    public static int MissionSeed { get; private set; }
    public static int PlanetDifficultyIndex { get; private set; }
    public static PlanetMissionEnvironmentKind EnvironmentKind
    {
        get;
        private set;
    }

    public static bool HasSelection =>
        !string.IsNullOrWhiteSpace(PlanetId) &&
        !string.IsNullOrWhiteSpace(MissionId);

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetForPlaySession()
    {
        Clear();
    }

    public static void Set(
        string planetId,
        string missionId,
        string missionName,
        Vector3 landingDirection,
        int missionSeed)
    {
        Set(
            planetId,
            missionId,
            missionName,
            landingDirection,
            missionSeed,
            PlanetMissionEnvironmentKind.Urban,
            0);
    }

    public static void Set(
        string planetId,
        string missionId,
        string missionName,
        Vector3 landingDirection,
        int missionSeed,
        PlanetMissionEnvironmentKind environmentKind)
    {
        Set(
            planetId,
            missionId,
            missionName,
            landingDirection,
            missionSeed,
            environmentKind,
            0);
    }

    public static void Set(
        string planetId,
        string missionId,
        string missionName,
        Vector3 landingDirection,
        int missionSeed,
        PlanetMissionEnvironmentKind environmentKind,
        int planetDifficultyIndex)
    {
        PlanetId = planetId ?? string.Empty;
        MissionId = missionId ?? string.Empty;
        MissionName = missionName ?? string.Empty;
        LandingDirection = landingDirection.sqrMagnitude > 0.001f
            ? landingDirection.normalized
            : Vector3.up;
        MissionSeed = missionSeed;
        // Natural chapter maps were retired. Canonicalizing here also migrates
        // old callers or stale serialized values before the loading scene can
        // choose a battlefield implementation.
        EnvironmentKind = PlanetMissionEnvironmentKind.Urban;
        PlanetDifficultyIndex = Mathf.Max(0, planetDifficultyIndex);
    }

    public static void Clear()
    {
        PlanetId = string.Empty;
        MissionId = string.Empty;
        MissionName = string.Empty;
        LandingDirection = Vector3.up;
        MissionSeed = 0;
        EnvironmentKind = PlanetMissionEnvironmentKind.Urban;
        PlanetDifficultyIndex = 0;
    }
}
