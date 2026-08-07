using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.IcePlanet
{
    public enum IcePlanetCombatAnchorRole
    {
        PlayerApproach,
        EnemySpawn,
        Objective,
        Patrol,
        Vantage,
        Extraction
    }

    public enum IcePlanetCombatVisualRole
    {
        BoundaryCliff,
        CoverRock,
        IceCrystal,
        Ruin,
        Wall,
        Tower,
        Bridge,
        IcePatch,
        Light,
        SnowFx
    }

    [Serializable]
    public sealed class IcePlanetCombatLayoutElement
    {
        public string stableId;
        public IcePlanetCombatVisualRole role;
        public Vector2 position;
        public float yawDegrees;
        public Vector3 targetSize;
        public bool blocksMovement;

        public IcePlanetCombatLayoutElement(
            string valueStableId,
            IcePlanetCombatVisualRole valueRole,
            Vector2 valuePosition,
            float valueYawDegrees,
            Vector3 valueTargetSize,
            bool valueBlocksMovement)
        {
            stableId = valueStableId;
            role = valueRole;
            position = valuePosition;
            yawDegrees = valueYawDegrees;
            targetSize = valueTargetSize;
            blocksMovement = valueBlocksMovement;
        }
    }

    [Serializable]
    public sealed class IcePlanetCombatLayoutAnchor
    {
        public string stableId;
        public IcePlanetCombatAnchorRole role;
        public Vector2 position;
        public float yawDegrees;
        public int groupIndex;

        public IcePlanetCombatLayoutAnchor(
            string valueStableId,
            IcePlanetCombatAnchorRole valueRole,
            Vector2 valuePosition,
            float valueYawDegrees,
            int valueGroupIndex)
        {
            stableId = valueStableId;
            role = valueRole;
            position = valuePosition;
            yawDegrees = valueYawDegrees;
            groupIndex = valueGroupIndex;
        }
    }

    public sealed class IcePlanetCombatLayoutPlan
    {
        readonly List<IcePlanetCombatLayoutElement> elements =
            new List<IcePlanetCombatLayoutElement>();
        readonly List<IcePlanetCombatLayoutAnchor> anchors =
            new List<IcePlanetCombatLayoutAnchor>();

        public string MissionId { get; private set; }
        public int Seed { get; private set; }
        public Vector2 ArenaSize { get; private set; }
        public float ApproachDistance { get; private set; }
        public IReadOnlyList<IcePlanetCombatLayoutElement> Elements =>
            elements;
        public IReadOnlyList<IcePlanetCombatLayoutAnchor> Anchors =>
            anchors;

        public IcePlanetCombatLayoutPlan(
            string missionId,
            int seed,
            Vector2 arenaSize,
            float approachDistance)
        {
            MissionId = missionId ?? string.Empty;
            Seed = seed;
            ArenaSize = arenaSize;
            ApproachDistance = approachDistance;
        }

        public void AddElement(IcePlanetCombatLayoutElement element)
        {
            if (element != null)
                elements.Add(element);
        }

        public void AddAnchor(IcePlanetCombatLayoutAnchor anchor)
        {
            if (anchor != null)
                anchors.Add(anchor);
        }

        public uint CalculateSignature()
        {
            unchecked
            {
                uint hash = 2166136261u;
                HashString(ref hash, MissionId);
                HashInt(ref hash, Seed);
                HashInt(ref hash, Mathf.RoundToInt(ArenaSize.x * 100f));
                HashInt(ref hash, Mathf.RoundToInt(ArenaSize.y * 100f));
                for (int index = 0; index < elements.Count; index++)
                {
                    IcePlanetCombatLayoutElement value = elements[index];
                    HashString(ref hash, value.stableId);
                    HashInt(ref hash, (int)value.role);
                    HashInt(ref hash, Mathf.RoundToInt(value.position.x * 100f));
                    HashInt(ref hash, Mathf.RoundToInt(value.position.y * 100f));
                    HashInt(ref hash, Mathf.RoundToInt(value.yawDegrees * 10f));
                }
                for (int index = 0; index < anchors.Count; index++)
                {
                    IcePlanetCombatLayoutAnchor value = anchors[index];
                    HashString(ref hash, value.stableId);
                    HashInt(ref hash, (int)value.role);
                    HashInt(ref hash, Mathf.RoundToInt(value.position.x * 100f));
                    HashInt(ref hash, Mathf.RoundToInt(value.position.y * 100f));
                    HashInt(ref hash, value.groupIndex);
                }
                return hash;
            }
        }

        static void HashString(ref uint hash, string value)
        {
            string normalized = value ?? string.Empty;
            for (int index = 0; index < normalized.Length; index++)
            {
                hash ^= normalized[index];
                hash *= 16777619u;
            }
        }

        static void HashInt(ref uint hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                hash *= 16777619u;
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class IcePlanetCombatAnchor : MonoBehaviour
    {
        [SerializeField] string stableId;
        [SerializeField] IcePlanetCombatAnchorRole role;
        [SerializeField] int groupIndex;

        public string StableId => stableId;
        public IcePlanetCombatAnchorRole Role => role;
        public int GroupIndex => groupIndex;

        public void Configure(
            string valueStableId,
            IcePlanetCombatAnchorRole valueRole,
            int valueGroupIndex)
        {
            stableId = valueStableId ?? string.Empty;
            role = valueRole;
            groupIndex = valueGroupIndex;
        }
    }

    [DisallowMultipleComponent]
    public sealed class IcePlanetGeneratedElement : MonoBehaviour
    {
        [SerializeField] string stableId;
        [SerializeField] IcePlanetCombatVisualRole role;

        public string StableId => stableId;
        public IcePlanetCombatVisualRole Role => role;

        public void Configure(
            string valueStableId,
            IcePlanetCombatVisualRole valueRole)
        {
            stableId = valueStableId ?? string.Empty;
            role = valueRole;
        }
    }

    [DisallowMultipleComponent]
    public sealed class IcePlanetCombatArea : MonoBehaviour
    {
        readonly List<IcePlanetCombatAnchor> anchors =
            new List<IcePlanetCombatAnchor>();

        [SerializeField] string planetId;
        [SerializeField] string missionId;
        [SerializeField] int missionSeed;
        [SerializeField] uint layoutSignature;
        [SerializeField] Vector3 localCenter;
        [SerializeField] Vector2 arenaSize;

        public string PlanetId => planetId;
        public string MissionId => missionId;
        public int MissionSeed => missionSeed;
        public uint LayoutSignature => layoutSignature;
        public Vector3 WorldCenter => transform.TransformPoint(localCenter);
        public Vector2 ArenaSize => arenaSize;
        public IReadOnlyList<IcePlanetCombatAnchor> Anchors => anchors;

        public void Configure(
            string valuePlanetId,
            IcePlanetCombatLayoutPlan plan,
            Vector3 valueWorldCenter)
        {
            planetId = valuePlanetId ?? string.Empty;
            missionId = plan != null ? plan.MissionId : string.Empty;
            missionSeed = plan != null ? plan.Seed : 0;
            layoutSignature = plan != null ? plan.CalculateSignature() : 0u;
            localCenter = transform.InverseTransformPoint(valueWorldCenter);
            arenaSize = plan != null ? plan.ArenaSize : Vector2.zero;
            anchors.Clear();
        }

        public void RegisterAnchor(IcePlanetCombatAnchor anchor)
        {
            if (anchor != null && !anchors.Contains(anchor))
                anchors.Add(anchor);
        }

        public void GetAnchors(
            IcePlanetCombatAnchorRole role,
            List<IcePlanetCombatAnchor> results)
        {
            if (results == null)
                return;
            results.Clear();
            for (int index = 0; index < anchors.Count; index++)
            {
                IcePlanetCombatAnchor value = anchors[index];
                if (value != null && value.Role == role)
                    results.Add(value);
            }
        }
    }
}
