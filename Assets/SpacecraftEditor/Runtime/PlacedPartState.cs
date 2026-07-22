using System;
using UnityEngine;

namespace SpacecraftEditor
{
    [Serializable]
    public sealed class PlacedPartState
    {
        public string runtimeId;
        public string partId;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public float uniformScale = 1f;
        public string mirrorGroupId;
        public string materialId;
        public KeyCode activationKey = KeyCode.None;
        public int weaponGroup;

        public PlacedPartState Clone()
        {
            return new PlacedPartState
            {
                runtimeId = runtimeId,
                partId = partId,
                localPosition = localPosition,
                localRotation = localRotation,
                uniformScale = uniformScale,
                mirrorGroupId = mirrorGroupId,
                materialId = materialId,
                activationKey = activationKey,
                weaponGroup = weaponGroup
            };
        }
    }

    public struct AssemblyMetrics
    {
        public float totalMass;
        public Vector3 localCenterOfMass;
        public Vector3 localResultantForce;
        public Vector3 localResultantTorque;

        public float Acceleration => totalMass > 0.001f ? localResultantForce.magnitude / totalMass : 0f;
    }
}
