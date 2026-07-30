using UnityEngine;

namespace UnityPlanet.CombatMap
{
    /// <summary>
    /// Converts a measured aircraft envelope into a conservative semantic
    /// arena scale. No value is written back to the authoring Recipe.
    /// </summary>
    public static class CombatMapScalePlanner
    {
        public static CombatMapScalePlan Build(
            AirCombatMapSettings authored,
            CombatMapFlightEnvelope envelope)
        {
            AirCombatMapSettings source = authored != null
                ? authored.ValidatedCopy()
                : AirCombatMapSettings.CreateDefault();
            var settings = source.ValidatedCopy();

            float width = Mathf.Max(
                2f,
                envelope != null ? envelope.horizontalSpan : 2f);
            float height = Mathf.Max(
                2f,
                envelope != null ? envelope.verticalSpan : 2f);
            float measuredRadius = Mathf.Max(
                source.designTurnRadius,
                envelope != null && envelope.IsUsable
                    ? envelope.turnRadius
                    : source.designTurnRadius);
            float speed = Mathf.Max(
                source.designCombatSpeed,
                envelope != null && envelope.IsUsable
                    ? envelope.measuredSpeed
                    : source.designCombatSpeed);
            float clearanceRadius = Mathf.Max(
                measuredRadius * 1.15f,
                measuredRadius + width);
            float weaponRange = source.designWeaponRange;

            float requestedSize = Mathf.Max(
                80f * width,
                16f * clearanceRadius,
                3.5f * weaponRange,
                32f * speed);
            float mapSize = RoundUp(
                Mathf.Clamp(requestedSize, 1024f, 4096f),
                128f);

            settings.mapSize = mapSize;
            settings.warningRadius = mapSize * 0.45f;
            settings.forfeitRadius = mapSize * 0.48f;
            settings.spawnDistance = Mathf.Clamp(
                Mathf.Max(
                    2.05f * weaponRange,
                    8f * clearanceRadius,
                    0.52f * mapSize),
                0.42f * mapSize,
                0.64f * mapSize);
            settings.designCombatSpeed = speed;
            settings.designTurnRadius = clearanceRadius;
            settings.vehicleWingspan = width;
            settings.mainRouteWidth = Mathf.Max(
                10f * width,
                2f * clearanceRadius + width);
            settings.canyonRouteWidth = Mathf.Max(
                8f * width,
                1.45f * clearanceRadius);
            settings.longRangeRouteWidth = Mathf.Max(
                settings.mainRouteWidth,
                1.9f * clearanceRadius);
            settings.minimumGroundClearance = Mathf.Max(
                source.minimumGroundClearance,
                1.5f * height + 2f,
                0.12f * clearanceRadius);
            settings.maximumGroundClearance = Mathf.Max(
                source.maximumGroundClearance,
                settings.minimumGroundClearance +
                2.25f * clearanceRadius);
            settings.spawnClearance = Mathf.Max(
                source.spawnClearance,
                1.5f * height + 4f,
                0.25f * clearanceRadius);
            settings.mountainHeight = Mathf.Clamp(
                Mathf.Max(12f * height, 1.5f * clearanceRadius),
                80f,
                Mathf.Min(320f, 0.14f * mapSize));
            settings.mapCenterOffset = new Vector3(
                source.mapCenterOffset.x,
                source.mapCenterOffset.y,
                mapSize * 0.5f + Mathf.Max(128f, 4f * width));
            settings.chunkSize = 256f;
            float sampleSpacing = Mathf.Clamp(0.4f * width, 4f, 8f);
            settings.chunkResolution = Mathf.Clamp(
                Mathf.CeilToInt(settings.chunkSize / sampleSpacing),
                32,
                64);
            settings.Clamp();

            return new CombatMapScalePlan
            {
                settings = settings,
                clearanceTurnRadius = clearanceRadius,
                spawnBasinDiameter = 2.8f * clearanceRadius,
                maneuverBowlDiameter = 4.2f * clearanceRadius,
                transitGateWidth = 1.65f * clearanceRadius,
                recoveryVolumeDiameter = 3.3f * clearanceRadius,
                boundaryBuffer = settings.forfeitRadius -
                                 settings.warningRadius,
                diagnostic = string.Format(
                    "机体 {0:0.0}×{1:0.0}m，盘旋半径 {2:0.0}m（安全半径 {3:0.0}m），" +
                    "自动地图 {4:0}m，走廊 {5:0}/{6:0}/{7:0}m，机动碗 {8:0}m，边界 {9:0}/{10:0}m。",
                    width,
                    height,
                    measuredRadius,
                    clearanceRadius,
                    settings.mapSize,
                    settings.mainRouteWidth,
                    settings.canyonRouteWidth,
                    settings.longRangeRouteWidth,
                    4.2f * clearanceRadius,
                    settings.warningRadius,
                    settings.forfeitRadius)
            };
        }

        static float RoundUp(float value, float interval)
        {
            return Mathf.Ceil(value / interval) * interval;
        }
    }
}
