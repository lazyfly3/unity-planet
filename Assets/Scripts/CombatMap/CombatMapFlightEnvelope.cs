using System;
using UnityEngine;

namespace UnityPlanet.CombatMap
{
    [Serializable]
    public sealed class CombatMapFlightEnvelope
    {
        public string blueprintFingerprint = string.Empty;
        public Bounds localBounds = new Bounds(Vector3.zero, Vector3.one);
        public float horizontalSpan;
        public float verticalSpan;
        public float hullRadius;
        public bool measurementCompleted;
        public bool usedFallback;
        public bool sourceAircraftUnchanged;
        public float measuredSpeed;
        public float turnRadius;
        public float turnDuration;
        public float altitudeLoss;
        public float closureError;
        public float circleFitRms;
        [Range(0f, 1f)] public float confidence;
        public int sampleCount;
        public string diagnostic = string.Empty;

        public bool IsUsable =>
            turnRadius > 0f &&
            measuredSpeed > 0f &&
            horizontalSpan > 0f &&
            sourceAircraftUnchanged;
    }

    [Serializable]
    public sealed class CombatMapScalePlan
    {
        public AirCombatMapSettings settings;
        public float clearanceTurnRadius;
        public float spawnBasinDiameter;
        public float maneuverBowlDiameter;
        public float transitGateWidth;
        public float recoveryVolumeDiameter;
        public float boundaryBuffer;
        public string diagnostic = string.Empty;
    }
}
