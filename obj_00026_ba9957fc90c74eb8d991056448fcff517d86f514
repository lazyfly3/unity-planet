using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityGeneration
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class CityNoiseTerrain : MonoBehaviour, ICityTerrainSampler
    {
        sealed class RoadStamp
        {
            public Vector2 Start;
            public Vector2 End;
            public float StartHeight;
            public float EndHeight;
            public float HalfWidth;
            public float BlendWidth;
        }

        sealed class RiverFeature
        {
            public readonly List<Vector2> Points = new List<Vector2>();
            public readonly List<float> WaterHeights = new List<float>();
            public float StartWidth;
            public float EndWidth;
            public float StartDepth;
            public float EndDepth;
        }

        sealed class LakeFeature
        {
            public Vector2 Center;
            public float Radius;
            public float WaterHeight;
            public float Depth;
        }

        readonly struct HeapItem
        {
            public readonly int Index;
            public readonly float Height;

            public HeapItem(int index, float height)
            {
                Index = index;
                Height = height;
            }
        }

        sealed class MinHeap
        {
            readonly List<HeapItem> items = new List<HeapItem>();

            public int Count => items.Count;

            public void Push(HeapItem item)
            {
                items.Add(item);
                int index = items.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (items[parent].Height <= item.Height)
                        break;
                    items[index] = items[parent];
                    index = parent;
                }
                items[index] = item;
            }

            public HeapItem Pop()
            {
                HeapItem result = items[0];
                HeapItem tail = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                if (items.Count == 0)
                    return result;

                int index = 0;
                while (true)
                {
                    int left = index * 2 + 1;
                    if (left >= items.Count)
                        break;
                    int right = left + 1;
                    int child = right < items.Count
                        && items[right].Height < items[left].Height
                        ? right
                        : left;
                    if (items[child].Height >= tail.Height)
                        break;
                    items[index] = items[child];
                    index = child;
                }
                items[index] = tail;
                return result;
            }
        }

        readonly List<RoadStamp> roadStamps = new List<RoadStamp>();
        readonly List<RiverFeature> rivers = new List<RiverFeature>();
        readonly List<LakeFeature> lakes = new List<LakeFeature>();

        [Header("PlanetLab Patch")]
        [SerializeField] ProceduralPlanetPreset planetPreset;
        [SerializeField, Min(20f)] float size = 400f;
        [SerializeField, Range(16, 200)] int resolution = 200;
        [SerializeField, Range(32, 128)] int sourceResolution = 64;
        [SerializeField] int terrainSeed = 7319;
        [SerializeField, Range(0f, 2f)] float heightScale = 1f;
        [SerializeField] bool selectBestPatch = true;
        [SerializeField] Vector3 patchDirection = new Vector3(0.57735f, 0.57735f, 0.57735f);
        [SerializeField] bool recenterAtOrigin = true;
        [SerializeField] PlanetTerrainSettings terrainSettings = new PlanetTerrainSettings
        {
            shapeVersion = PlanetTerrainSettings.CurrentShapeVersion,
            continentScale = 0.018f,
            continentHeight = 11f,
            detailScale = 0.065f,
            detailHeight = 3.6f,
            ridgeHeight = 7f,
            continentThreshold = 0.5f,
            continentWarp = 0.6f,
            continentSharpness = 1.25f,
            mountainMask = 0.52f,
            oceanFloorDepth = 0.58f,
            terraceStrength = 0.08f,
            generateCaves = false
        };

        [Header("Hydrology")]
        [SerializeField] bool generateHydrology = true;
        [SerializeField, Range(1, 2)] int riverCount = 2;
        [SerializeField, Min(8f)] float minimumLakeRadius = 22f;
        [SerializeField, Min(8f)] float maximumLakeRadius = 34f;
        [SerializeField, Min(0.2f)] float waterClearance = 0.8f;
        [SerializeField] Transform riversRoot;
        [SerializeField] Transform lakesRoot;

        MeshFilter meshFilter;
        MeshRenderer meshRenderer;
        MeshCollider meshCollider;
        Mesh terrainMesh;
        Material terrainMaterial;
        Material waterMaterial;
        PlanetTerrainSettings activeTerrainSettings;
        int activeSeed;
        float activeRadius = 100f;
        float originNoise;
        Vector3 tangentX;
        Vector3 tangentZ;
        float minimumHeight;
        float maximumHeight;
        float waterCoverage;
        float mountainCoverage;
        float[] baseHeightGrid;
        int cachedLayoutHash;
        bool layoutInitialized;

        public float Size => size;
        public int Resolution => resolution;
        public int TerrainSeed => activeSeed;
        public Mesh TerrainMesh => terrainMesh;
        public float Relief => maximumHeight - minimumHeight;
        public float MinimumHeight => minimumHeight;
        public float MaximumHeight => maximumHeight;
        public float WaterCoverage => waterCoverage;
        public float MountainCoverage => mountainCoverage;
        public float WaterClearance => waterClearance;
        public int RiverCount => rivers.Count;
        public int LakeCount => lakes.Count;
        public Vector3 PatchDirection => patchDirection;

        public IReadOnlyList<Vector2> GetRiverPoints(int index)
        {
            return rivers[index].Points;
        }

        public IReadOnlyList<float> GetRiverWaterHeights(int index)
        {
            return rivers[index].WaterHeights;
        }

        public Vector2 GetLakeCenter(int index)
        {
            return lakes[index].Center;
        }

        void Awake()
        {
            ResolveComponents();
        }

        public void ConfigureSize(float value)
        {
            float next = Mathf.Max(20f, value);
            if (!Mathf.Approximately(size, next))
                InvalidateLayout();
            size = next;
        }

        public void ConfigurePreset(ProceduralPlanetPreset preset)
        {
            if (planetPreset != preset)
                InvalidateLayout();
            planetPreset = preset;
        }

        public void ConfigureFeatureRoots(Transform riverParent, Transform lakeParent)
        {
            riversRoot = riverParent;
            lakesRoot = lakeParent;
        }

        public void Rebuild(Material material = null, Material water = null)
        {
            ResolveComponents();
            ResolveFeatureRoots();
            resolution = Mathf.Clamp(resolution, 16, 200);
            sourceResolution = Mathf.Clamp(sourceResolution, 32, 128);
            heightScale = Mathf.Clamp(heightScale, 0f, 2f);
            waterClearance = Mathf.Max(0.2f, waterClearance);
            if (material != null)
                terrainMaterial = material;
            if (water != null)
                waterMaterial = water;

            ResolvePlanetSettings();
            int layoutHash = CalculateLayoutHash();
            if (!layoutInitialized || cachedLayoutHash != layoutHash)
            {
                if (selectBestPatch)
                    patchDirection = FindBestPatchDirection();
                patchDirection = patchDirection.sqrMagnitude > 0.000001f
                    ? patchDirection.normalized
                    : Vector3.up;
                BuildPatchBasis();
                originNoise = SampleUncenteredHeight(Vector2.zero);
                BuildBaseHeightGrid();
                BuildHydrology();
                cachedLayoutHash = layoutHash;
                layoutInitialized = true;
            }
            else if (generateHydrology
                     && (baseHeightGrid == null
                         || rivers.Count == 0
                         || lakes.Count == 0))
            {
                // Enter Play Mode Options can preserve this component's cache
                // flag while Unity has already released its runtime-only lists.
                // Recover the deterministic layout instead of rendering a
                // terrain with silently missing rivers and lakes.
                BuildPatchBasis();
                originNoise = SampleUncenteredHeight(Vector2.zero);
                if (baseHeightGrid == null)
                    BuildBaseHeightGrid();
                BuildHydrology();
            }

            Mesh replacement = BuildMesh();
            Mesh previous = terrainMesh;
            terrainMesh = replacement;
            meshFilter.sharedMesh = terrainMesh;
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = terrainMesh;
            if (terrainMaterial != null)
                meshRenderer.sharedMaterial = terrainMaterial;

            RebuildWaterMeshes();

            if (previous != null && previous != terrainMesh)
                DestroySafely(previous);
        }

        public float SampleHeight(Vector2 worldXZ)
        {
            float ground = SampleGroundHeight(worldXZ);
            return SampleRoadHeight(worldXZ, ground);
        }

        public float SampleSurfaceHeight(Vector2 worldXZ)
        {
            CityTerrainSample sample = SampleTerrain(worldXZ);
            return sample.SurfaceHeight;
        }

        public CityTerrainSample SampleTerrain(Vector2 worldXZ)
        {
            float ground = SampleHeight(worldXZ);
            SampleWater(worldXZ, ground, out CitySurfaceKind kind, out float waterHeight);
            float surfaceHeight = kind == CitySurfaceKind.Land
                ? ground
                : Mathf.Max(ground, waterHeight);
            float normalStep = Mathf.Max(0.75f, size / resolution * 0.5f);
            float left = SampleHeight(worldXZ + Vector2.left * normalStep);
            float right = SampleHeight(worldXZ + Vector2.right * normalStep);
            float down = SampleHeight(worldXZ + Vector2.down * normalStep);
            float up = SampleHeight(worldXZ + Vector2.up * normalStep);
            Vector3 normal = new Vector3(
                left - right,
                normalStep * 2f,
                down - up).normalized;
            float grade = Mathf.Sqrt(
                Mathf.Pow((right - left) / (normalStep * 2f), 2f)
                + Mathf.Pow((up - down) / (normalStep * 2f), 2f));
            return new CityTerrainSample(
                ground,
                surfaceHeight,
                kind == CitySurfaceKind.Land ? ground : waterHeight,
                kind == CitySurfaceKind.Land
                    ? 0f
                    : Mathf.Max(0f, waterHeight - ground),
                kind == CitySurfaceKind.Land ? normal : Vector3.up,
                grade,
                kind);
        }

        public void ApplyCityPlan(
            CityGenerationResult result,
            float maximumRoadGrade,
            float blendWidth)
        {
            roadStamps.Clear();
            if (result != null && result.IsSuccess)
            {
                BuildRoadStamps(
                    result.Roads,
                    Mathf.Clamp(maximumRoadGrade, 0.02f, 0.3f),
                    Mathf.Max(0.5f, blendWidth));
            }
            Rebuild();
        }

        public void ClearCityPlan(bool rebuild = true)
        {
            if (roadStamps.Count == 0)
                return;
            roadStamps.Clear();
            if (rebuild)
                Rebuild();
        }

        void ResolvePlanetSettings()
        {
            if (planetPreset != null)
            {
                planetPreset.ClampValues();
                activeSeed = planetPreset.seed;
                activeRadius = Mathf.Max(20f, planetPreset.radius);
                activeTerrainSettings = planetPreset.terrain != null
                    ? planetPreset.terrain.Clone()
                    : new PlanetTerrainSettings();
            }
            else
            {
                activeSeed = terrainSeed;
                activeRadius = 100f;
                activeTerrainSettings = terrainSettings != null
                    ? terrainSettings.Clone()
                    : new PlanetTerrainSettings();
            }
            activeTerrainSettings.generateCaves = false;
            activeTerrainSettings.ClampValues();
        }

        Vector3 FindBestPatchDirection()
        {
            Vector3 best = patchDirection.sqrMagnitude > 0.000001f
                ? patchDirection.normalized
                : Vector3.up;
            float bestScore = float.MinValue;
            const int candidateCount = 64;
            const int samplesPerAxis = 3;
            float half = size * 0.5f;
            float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));

            for (int candidate = 0; candidate < candidateCount; candidate++)
            {
                float y = 1f - (candidate + 0.5f) * 2f / candidateCount;
                float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                float angle = candidate * goldenAngle;
                Vector3 direction = new Vector3(
                    Mathf.Cos(angle) * radius,
                    y,
                    Mathf.Sin(angle) * radius).normalized;
                BuildBasis(direction, out Vector3 axisX, out Vector3 axisZ);

                float minimum = float.MaxValue;
                float maximum = float.MinValue;
                float sum = 0f;
                float sumSquared = 0f;
                int count = 0;
                for (int z = 0; z < samplesPerAxis; z++)
                {
                    float localZ = Mathf.Lerp(-half, half, z / (samplesPerAxis - 1f));
                    for (int x = 0; x < samplesPerAxis; x++)
                    {
                        float localX = Mathf.Lerp(-half, half, x / (samplesPerAxis - 1f));
                        Vector3 spherePoint = (
                            direction * activeRadius
                            + axisX * localX
                            + axisZ * localZ).normalized * activeRadius;
                        float height = VoxelQuadSphereTerrain.GetSurfaceNoise(
                            spherePoint,
                            activeSeed,
                            activeTerrainSettings) * heightScale;
                        minimum = Mathf.Min(minimum, height);
                        maximum = Mathf.Max(maximum, height);
                        sum += height;
                        sumSquared += height * height;
                        count++;
                    }
                }
                float mean = sum / count;
                float deviation = Mathf.Sqrt(Mathf.Max(0f, sumSquared / count - mean * mean));
                float relief = maximum - minimum;
                float score = relief * 1.4f + deviation * 2f;
                if (relief >= 18f)
                    score += 20f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = direction;
                }
            }
            return best;
        }

        void BuildPatchBasis()
        {
            BuildBasis(patchDirection, out tangentX, out tangentZ);
        }

        static void BuildBasis(
            Vector3 direction,
            out Vector3 axisX,
            out Vector3 axisZ)
        {
            Vector3 reference = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.92f
                ? Vector3.forward
                : Vector3.up;
            axisX = Vector3.Cross(reference, direction).normalized;
            axisZ = Vector3.Cross(direction, axisX).normalized;
        }

        float SampleUncenteredHeight(Vector2 worldXZ)
        {
            Vector2 local = worldXZ - new Vector2(
                transform.position.x,
                transform.position.z);
            Vector3 spherePoint = (
                patchDirection * activeRadius
                + tangentX * local.x
                + tangentZ * local.y).normalized * activeRadius;
            return VoxelQuadSphereTerrain.GetSurfaceNoise(
                spherePoint,
                activeSeed,
                activeTerrainSettings) * heightScale
                + transform.position.y;
        }

        float SampleUncarvedHeight(Vector2 worldXZ)
        {
            if (baseHeightGrid != null
                && baseHeightGrid.Length
                == (sourceResolution + 1) * (sourceResolution + 1))
            {
                float half = size * 0.5f;
                float x01 = Mathf.InverseLerp(
                    transform.position.x - half,
                    transform.position.x + half,
                    worldXZ.x);
                float z01 = Mathf.InverseLerp(
                    transform.position.z - half,
                    transform.position.z + half,
                    worldXZ.y);
                if (x01 >= 0f && x01 <= 1f && z01 >= 0f && z01 <= 1f)
                {
                    float gridX = x01 * sourceResolution;
                    float gridZ = z01 * sourceResolution;
                    int x0 = Mathf.Clamp(
                        Mathf.FloorToInt(gridX),
                        0,
                        sourceResolution);
                    int z0 = Mathf.Clamp(
                        Mathf.FloorToInt(gridZ),
                        0,
                        sourceResolution);
                    int x1 = Mathf.Min(sourceResolution, x0 + 1);
                    int z1 = Mathf.Min(sourceResolution, z0 + 1);
                    float tx = gridX - x0;
                    float tz = gridZ - z0;
                    int stride = sourceResolution + 1;
                    return Mathf.Lerp(
                        Mathf.Lerp(
                            baseHeightGrid[z0 * stride + x0],
                            baseHeightGrid[z0 * stride + x1],
                            tx),
                        Mathf.Lerp(
                            baseHeightGrid[z1 * stride + x0],
                            baseHeightGrid[z1 * stride + x1],
                            tx),
                        tz);
                }
            }
            return SampleUncarvedHeightRaw(worldXZ);
        }

        float SampleUncarvedHeightRaw(Vector2 worldXZ)
        {
            float value = SampleUncenteredHeight(worldXZ);
            if (recenterAtOrigin)
                value -= originNoise - transform.position.y;
            return value;
        }

        void BuildBaseHeightGrid()
        {
            int gridSize = sourceResolution + 1;
            baseHeightGrid = new float[gridSize * gridSize];
            float half = size * 0.5f;
            float spacing = size / sourceResolution;
            for (int z = 0; z < gridSize; z++)
            {
                float worldZ = transform.position.z - half + z * spacing;
                for (int x = 0; x < gridSize; x++)
                {
                    float worldX = transform.position.x - half + x * spacing;
                    baseHeightGrid[z * gridSize + x] =
                        SampleUncarvedHeightRaw(new Vector2(worldX, worldZ));
                }
            }
        }

        int CalculateLayoutHash()
        {
            unchecked
            {
                int hash = activeSeed;
                hash = hash * 31 + Mathf.RoundToInt(size * 100f);
                hash = hash * 31 + sourceResolution;
                hash = hash * 31 + Mathf.RoundToInt(heightScale * 10000f);
                hash = hash * 31 + (selectBestPatch ? 1 : 0);
                if (!selectBestPatch)
                {
                    hash = hash * 31 + Mathf.RoundToInt(patchDirection.x * 10000f);
                    hash = hash * 31 + Mathf.RoundToInt(patchDirection.y * 10000f);
                    hash = hash * 31 + Mathf.RoundToInt(patchDirection.z * 10000f);
                }
                hash = hash * 31 + (generateHydrology ? 1 : 0);
                hash = hash * 31 + riverCount;
                hash = hash * 31 + Mathf.RoundToInt(minimumLakeRadius * 100f);
                hash = hash * 31 + Mathf.RoundToInt(maximumLakeRadius * 100f);
                hash = hash * 31 + JsonUtility
                    .ToJson(activeTerrainSettings)
                    .GetHashCode();
                return hash;
            }
        }

        void InvalidateLayout()
        {
            layoutInitialized = false;
            baseHeightGrid = null;
            rivers.Clear();
            lakes.Clear();
        }

        float SampleGroundHeight(Vector2 worldXZ)
        {
            float height = SampleUncarvedHeight(worldXZ);

            for (int i = 0; i < lakes.Count; i++)
            {
                LakeFeature lake = lakes[i];
                float distance = Vector2.Distance(worldXZ, lake.Center);
                float outer = lake.Radius + 5f;
                if (distance >= outer)
                    continue;
                float normalized = Mathf.Clamp01(distance / lake.Radius);
                float bowl = lake.WaterHeight
                    - lake.Depth * (1f - normalized * normalized)
                    - 0.18f;
                float weight = distance <= lake.Radius
                    ? 1f
                    : 1f - Mathf.SmoothStep(
                        0f,
                        1f,
                        (distance - lake.Radius) / 5f);
                height = Mathf.Lerp(height, Mathf.Min(height, bowl), weight);
            }

            for (int i = 0; i < rivers.Count; i++)
            {
                RiverFeature river = rivers[i];
                if (!TryClosestRiverPoint(
                        river,
                        worldXZ,
                        out float distance,
                        out float progress,
                        out float waterHeight))
                {
                    continue;
                }
                float width = Mathf.Lerp(river.StartWidth, river.EndWidth, progress);
                float depth = Mathf.Lerp(river.StartDepth, river.EndDepth, progress);
                float halfWidth = width * 0.5f;
                float outer = halfWidth + 4f;
                if (distance >= outer)
                    continue;
                float bed = waterHeight
                    - depth * (1f - Mathf.Clamp01(distance / halfWidth) * 0.35f);
                float weight = distance <= halfWidth
                    ? 1f
                    : 1f - Mathf.SmoothStep(
                        0f,
                        1f,
                        (distance - halfWidth) / 4f);
                height = Mathf.Lerp(height, Mathf.Min(height, bed), weight);
            }
            return height;
        }

        void SampleWater(
            Vector2 point,
            float ground,
            out CitySurfaceKind kind,
            out float waterHeight)
        {
            kind = CitySurfaceKind.Land;
            waterHeight = ground;

            for (int i = 0; i < lakes.Count; i++)
            {
                LakeFeature lake = lakes[i];
                if (Vector2.Distance(point, lake.Center) > lake.Radius)
                    continue;
                kind = CitySurfaceKind.Lake;
                waterHeight = lake.WaterHeight;
                return;
            }

            float bestDistance = float.MaxValue;
            for (int i = 0; i < rivers.Count; i++)
            {
                RiverFeature river = rivers[i];
                if (!TryClosestRiverPoint(
                        river,
                        point,
                        out float distance,
                        out float progress,
                        out float candidateHeight))
                {
                    continue;
                }
                float width = Mathf.Lerp(river.StartWidth, river.EndWidth, progress);
                if (distance > width * 0.5f || distance >= bestDistance)
                    continue;
                bestDistance = distance;
                kind = CitySurfaceKind.River;
                waterHeight = candidateHeight;
            }
        }

        void BuildHydrology()
        {
            rivers.Clear();
            lakes.Clear();
            if (!generateHydrology)
                return;

            int gridSize = resolution + 1;
            int count = gridSize * gridSize;
            float[] heights = new float[count];
            float half = size * 0.5f;
            float spacing = size / resolution;
            for (int z = 0; z < gridSize; z++)
            {
                float worldZ = transform.position.z - half + z * spacing;
                for (int x = 0; x < gridSize; x++)
                {
                    float worldX = transform.position.x - half + x * spacing;
                    heights[z * gridSize + x] =
                        SampleUncarvedHeight(new Vector2(worldX, worldZ));
                }
            }

            float[] filled = BuildPriorityFilledHeights(heights, gridSize);
            var random = new System.Random(activeSeed ^ 0x51A7C1);
            var sinks = SelectLakeSinks(heights, filled, gridSize, spacing, random);
            for (int i = 0; i < sinks.Count; i++)
            {
                int sink = sinks[i];
                int sinkX = sink % gridSize;
                int sinkZ = sink / gridSize;
                float radius = Mathf.Lerp(
                    minimumLakeRadius,
                    Mathf.Max(minimumLakeRadius, maximumLakeRadius),
                    (float)random.NextDouble());
                lakes.Add(new LakeFeature
                {
                    Center = new Vector2(
                        transform.position.x - half + sinkX * spacing,
                        transform.position.z - half + sinkZ * spacing),
                    Radius = radius,
                    WaterHeight = heights[sink] + 1.1f,
                    Depth = Mathf.Lerp(1.8f, 3.2f, (float)random.NextDouble())
                });
            }

            for (int riverIndex = 0;
                 riverIndex < Mathf.Min(riverCount, lakes.Count);
                 riverIndex++)
            {
                LakeFeature lake = lakes[riverIndex];
                Vector2 source = SelectRiverSource(
                    heights,
                    gridSize,
                    spacing,
                    lake.Center,
                    riverIndex);
                rivers.Add(BuildRiver(source, lake, riverIndex));
            }
        }

        static float[] BuildPriorityFilledHeights(float[] heights, int gridSize)
        {
            int count = heights.Length;
            var filled = new float[count];
            Array.Copy(heights, filled, count);
            var visited = new bool[count];
            var heap = new MinHeap();

            for (int z = 0; z < gridSize; z++)
            {
                AddBoundary(0, z);
                AddBoundary(gridSize - 1, z);
            }
            for (int x = 1; x < gridSize - 1; x++)
            {
                AddBoundary(x, 0);
                AddBoundary(x, gridSize - 1);
            }

            int[] offsets =
            {
                -gridSize - 1, -gridSize, -gridSize + 1,
                -1, 1,
                gridSize - 1, gridSize, gridSize + 1
            };
            while (heap.Count > 0)
            {
                HeapItem current = heap.Pop();
                int x = current.Index % gridSize;
                int z = current.Index / gridSize;
                for (int i = 0; i < offsets.Length; i++)
                {
                    int neighbor = current.Index + offsets[i];
                    if (neighbor < 0 || neighbor >= count || visited[neighbor])
                        continue;
                    int neighborX = neighbor % gridSize;
                    int neighborZ = neighbor / gridSize;
                    if (Mathf.Abs(neighborX - x) > 1
                        || Mathf.Abs(neighborZ - z) > 1)
                    {
                        continue;
                    }
                    visited[neighbor] = true;
                    filled[neighbor] = Mathf.Max(heights[neighbor], current.Height);
                    heap.Push(new HeapItem(neighbor, filled[neighbor]));
                }
            }
            return filled;

            void AddBoundary(int x, int z)
            {
                int index = z * gridSize + x;
                if (visited[index])
                    return;
                visited[index] = true;
                heap.Push(new HeapItem(index, heights[index]));
            }
        }

        List<int> SelectLakeSinks(
            float[] heights,
            float[] filled,
            int gridSize,
            float spacing,
            System.Random random)
        {
            var ranked = new List<KeyValuePair<float, int>>();
            int margin = Mathf.Max(8, Mathf.RoundToInt(30f / spacing));
            for (int z = margin; z < gridSize - margin; z += 3)
            {
                for (int x = margin; x < gridSize - margin; x += 3)
                {
                    int index = z * gridSize + x;
                    float depression = Mathf.Max(0f, filled[index] - heights[index]);
                    float jitter = (float)random.NextDouble() * 0.05f;
                    float score = depression * 6f - heights[index] + jitter;
                    ranked.Add(new KeyValuePair<float, int>(score, index));
                }
            }
            ranked.Sort((a, b) => b.Key.CompareTo(a.Key));

            var result = new List<int>();
            float minimumSeparation = size * 0.28f;
            for (int i = 0; i < ranked.Count && result.Count < riverCount; i++)
            {
                int candidate = ranked[i].Value;
                Vector2 point = GridPoint(candidate);
                bool separated = true;
                for (int j = 0; j < result.Count; j++)
                {
                    if (Vector2.Distance(point, GridPoint(result[j]))
                        < minimumSeparation)
                    {
                        separated = false;
                        break;
                    }
                }
                if (separated)
                    result.Add(candidate);
            }
            if (result.Count == 0 && ranked.Count > 0)
                result.Add(ranked[0].Value);
            return result;

            Vector2 GridPoint(int index)
            {
                float half = size * 0.5f;
                return new Vector2(
                    transform.position.x - half + index % gridSize * spacing,
                    transform.position.z - half + index / gridSize * spacing);
            }
        }

        Vector2 SelectRiverSource(
            float[] heights,
            int gridSize,
            float spacing,
            Vector2 sink,
            int riverIndex)
        {
            float half = size * 0.5f;
            int margin = Mathf.Max(5, Mathf.RoundToInt(20f / spacing));
            int best = margin * gridSize + margin;
            float bestScore = float.MinValue;
            for (int z = margin; z < gridSize - margin; z += 2)
            {
                for (int x = margin; x < gridSize - margin; x += 2)
                {
                    int index = z * gridSize + x;
                    Vector2 point = new Vector2(
                        transform.position.x - half + x * spacing,
                        transform.position.z - half + z * spacing);
                    float distance = Vector2.Distance(point, sink);
                    if (distance < size * 0.32f)
                        continue;
                    float sideBias = riverIndex % 2 == 0 ? point.x : -point.x;
                    float score = heights[index] * 2f + distance * 0.05f + sideBias * 0.01f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = index;
                    }
                }
            }
            return new Vector2(
                transform.position.x - half + best % gridSize * spacing,
                transform.position.z - half + best / gridSize * spacing);
        }

        RiverFeature BuildRiver(Vector2 source, LakeFeature lake, int riverIndex)
        {
            var river = new RiverFeature
            {
                StartWidth = 4.5f,
                EndWidth = 7.5f,
                StartDepth = 1.1f,
                EndDepth = 2f
            };
            float distance = Vector2.Distance(source, lake.Center);
            int nodes = Mathf.Clamp(Mathf.CeilToInt(distance / 7f), 12, 56);
            Vector2 direction = (lake.Center - source).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x);
            float sourceHeight = SampleUncarvedHeight(source) - 0.35f;
            float startWater = Mathf.Max(lake.WaterHeight + 1.5f, sourceHeight);
            for (int i = 0; i < nodes; i++)
            {
                float t = i / (nodes - 1f);
                float envelope = Mathf.Sin(t * Mathf.PI);
                float meander = Mathf.Sin(
                    (t * 4.5f + riverIndex * 0.37f) * Mathf.PI)
                    * Mathf.Min(18f, distance * 0.075f)
                    * envelope;
                Vector2 point = Vector2.Lerp(source, lake.Center, t)
                    + normal * meander;
                river.Points.Add(point);
                river.WaterHeights.Add(Mathf.Lerp(startWater, lake.WaterHeight, t));
            }
            return river;
        }

        Mesh BuildMesh()
        {
            int pointsPerAxis = resolution + 1;
            int vertexCount = pointsPerAxis * pointsPerAxis;
            var vertices = new Vector3[vertexCount];
            var colors = new Color[vertexCount];
            var uv = new Vector2[vertexCount];
            var triangles = new int[resolution * resolution * 6];
            var heights = new float[vertexCount];
            float half = size * 0.5f;
            minimumHeight = float.MaxValue;
            maximumHeight = float.MinValue;

            for (int z = 0; z <= resolution; z++)
            {
                float z01 = z / (float)resolution;
                float worldZ = transform.position.z + Mathf.Lerp(-half, half, z01);
                for (int x = 0; x <= resolution; x++)
                {
                    float x01 = x / (float)resolution;
                    float worldX = transform.position.x + Mathf.Lerp(-half, half, x01);
                    int index = z * pointsPerAxis + x;
                    float worldHeight = SampleHeight(new Vector2(worldX, worldZ));
                    heights[index] = worldHeight;
                    minimumHeight = Mathf.Min(minimumHeight, worldHeight);
                    maximumHeight = Mathf.Max(maximumHeight, worldHeight);
                    vertices[index] = new Vector3(
                        worldX - transform.position.x,
                        worldHeight - transform.position.y,
                        worldZ - transform.position.z);
                    uv[index] = new Vector2(x01, z01);
                }
            }

            int waterVertices = 0;
            int mountainVertices = 0;
            float colorRange = Mathf.Max(8f, maximumHeight - minimumHeight);
            for (int z = 0; z <= resolution; z++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    int index = z * pointsPerAxis + x;
                    int left = z * pointsPerAxis + Mathf.Max(0, x - 1);
                    int right = z * pointsPerAxis + Mathf.Min(resolution, x + 1);
                    int down = Mathf.Max(0, z - 1) * pointsPerAxis + x;
                    int up = Mathf.Min(resolution, z + 1) * pointsPerAxis + x;
                    float spacing = size / resolution;
                    float grade = Mathf.Sqrt(
                        Mathf.Pow((heights[right] - heights[left]) / (spacing * 2f), 2f)
                        + Mathf.Pow((heights[up] - heights[down]) / (spacing * 2f), 2f));
                    Vector2 point = new Vector2(
                        transform.position.x + vertices[index].x,
                        transform.position.z + vertices[index].z);
                    SampleWater(point, heights[index], out CitySurfaceKind kind, out _);
                    if (kind != CitySurfaceKind.Land)
                        waterVertices++;
                    if (grade > 0.28f
                        || heights[index] > minimumHeight + colorRange * 0.68f)
                    {
                        mountainVertices++;
                    }

                    float height01 = Mathf.InverseLerp(
                        minimumHeight,
                        maximumHeight,
                        heights[index]);
                    Color low = new Color(0.18f, 0.29f, 0.17f, 1f);
                    Color high = new Color(0.34f, 0.37f, 0.3f, 1f);
                    Color cliff = new Color(0.28f, 0.24f, 0.2f, 1f);
                    colors[index] = Color.Lerp(
                        Color.Lerp(low, high, height01),
                        cliff,
                        Mathf.InverseLerp(0.18f, 0.55f, grade));
                }
            }
            waterCoverage = waterVertices / (float)vertexCount;
            mountainCoverage = mountainVertices / (float)vertexCount;

            int triangleIndex = 0;
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int a = z * pointsPerAxis + x;
                    int b = a + 1;
                    int c = a + pointsPerAxis;
                    int d = c + 1;
                    triangles[triangleIndex++] = a;
                    triangles[triangleIndex++] = c;
                    triangles[triangleIndex++] = b;
                    triangles[triangleIndex++] = b;
                    triangles[triangleIndex++] = c;
                    triangles[triangleIndex++] = d;
                }
            }

            var mesh = new Mesh { name = "City PlanetLab Terrain" };
            if (vertexCount > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uv;
            mesh.colors = colors;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        void RebuildWaterMeshes()
        {
            ClearGeneratedChildren(riversRoot);
            ClearGeneratedChildren(lakesRoot);
            if (waterMaterial == null)
                return;

            for (int i = 0; i < rivers.Count; i++)
                CreateRiverObject("River_" + i, rivers[i]);
            for (int i = 0; i < lakes.Count; i++)
                CreateLakeObject("Lake_" + i, lakes[i]);
        }

        void CreateRiverObject(string name, RiverFeature river)
        {
            int count = river.Points.Count;
            var vertices = new Vector3[count * 2];
            var uv = new Vector2[count * 2];
            var triangles = new int[(count - 1) * 6];
            float accumulated = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector2 previous = river.Points[Mathf.Max(0, i - 1)];
                Vector2 next = river.Points[Mathf.Min(count - 1, i + 1)];
                Vector2 direction = (next - previous).normalized;
                Vector2 normal = new Vector2(-direction.y, direction.x);
                float t = i / (count - 1f);
                float width = Mathf.Lerp(river.StartWidth, river.EndWidth, t);
                if (i > 0)
                    accumulated += Vector2.Distance(river.Points[i - 1], river.Points[i]);
                Vector2 left = river.Points[i] - normal * width * 0.5f;
                Vector2 right = river.Points[i] + normal * width * 0.5f;
                float y = river.WaterHeights[i] + 0.04f - transform.position.y;
                vertices[i * 2] = new Vector3(
                    left.x - transform.position.x,
                    y,
                    left.y - transform.position.z);
                vertices[i * 2 + 1] = new Vector3(
                    right.x - transform.position.x,
                    y,
                    right.y - transform.position.z);
                uv[i * 2] = new Vector2(0f, accumulated * 0.08f);
                uv[i * 2 + 1] = new Vector2(1f, accumulated * 0.08f);
                if (i >= count - 1)
                    continue;
                int triangle = i * 6;
                int vertex = i * 2;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 2;
                triangles[triangle + 2] = vertex + 1;
                triangles[triangle + 3] = vertex + 1;
                triangles[triangle + 4] = vertex + 2;
                triangles[triangle + 5] = vertex + 3;
            }
            CreateWaterObject(name, vertices, uv, triangles, riversRoot);
        }

        void CreateLakeObject(string name, LakeFeature lake)
        {
            const int segments = 48;
            var vertices = new Vector3[segments + 1];
            var uv = new Vector2[segments + 1];
            var triangles = new int[segments * 3];
            vertices[0] = new Vector3(
                lake.Center.x - transform.position.x,
                lake.WaterHeight + 0.04f - transform.position.y,
                lake.Center.y - transform.position.z);
            uv[0] = Vector2.one * 0.5f;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector2 point = lake.Center
                    + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * lake.Radius;
                vertices[i + 1] = new Vector3(
                    point.x - transform.position.x,
                    lake.WaterHeight + 0.04f - transform.position.y,
                    point.y - transform.position.z);
                uv[i + 1] = new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle)) * 0.5f + Vector2.one * 0.5f;
                int next = (i + 1) % segments;
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = next + 1;
            }
            CreateWaterObject(name, vertices, uv, triangles, lakesRoot);
        }

        void CreateWaterObject(
            string name,
            Vector3[] vertices,
            Vector2[] uv,
            int[] triangles,
            Transform parent)
        {
            if (parent == null)
                return;
            var mesh = new Mesh
            {
                name = name + " Mesh",
                vertices = vertices,
                uv = uv,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = waterMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void BuildRoadStamps(
            IReadOnlyList<CityRoadSegment> roads,
            float maximumGrade,
            float blendWidth)
        {
            var nodeHeights = new Dictionary<Vector2Int, float>();
            var startKeys = new Vector2Int[roads.Count];
            var endKeys = new Vector2Int[roads.Count];

            for (int i = 0; i < roads.Count; i++)
            {
                CityRoadSegment road = roads[i];
                Vector2Int startKey = Quantize(road.Start);
                Vector2Int endKey = Quantize(road.End);
                startKeys[i] = startKey;
                endKeys[i] = endKey;
                AddNode(startKey, road.Start, road.Width);
                AddNode(endKey, road.End, road.Width);
            }

            for (int iteration = 0; iteration < 20; iteration++)
            {
                for (int i = 0; i < roads.Count; i++)
                {
                    Vector2Int startKey = startKeys[i];
                    Vector2Int endKey = endKeys[i];
                    float startHeight = nodeHeights[startKey];
                    float endHeight = nodeHeights[endKey];
                    float maximumDifference =
                        Vector2.Distance(roads[i].Start, roads[i].End)
                        * maximumGrade;
                    float difference = endHeight - startHeight;
                    float excess = Mathf.Abs(difference) - maximumDifference;
                    if (excess <= 0f)
                        continue;
                    float adjustment = excess * 0.5f * Mathf.Sign(difference);
                    nodeHeights[startKey] += adjustment;
                    nodeHeights[endKey] -= adjustment;
                }
            }

            for (int i = 0; i < roads.Count; i++)
            {
                CityRoadSegment road = roads[i];
                roadStamps.Add(new RoadStamp
                {
                    Start = road.Start,
                    End = road.End,
                    StartHeight = nodeHeights[startKeys[i]],
                    EndHeight = nodeHeights[endKeys[i]],
                    HalfWidth = road.Width * 0.5f + 0.25f,
                    BlendWidth = blendWidth
                });
            }

            void AddNode(Vector2Int key, Vector2 point, float roadWidth)
            {
                if (nodeHeights.ContainsKey(key))
                    return;
                nodeHeights[key] = SampleSmoothedGroundHeight(
                    point,
                    Mathf.Max(2f, roadWidth));
            }
        }

        float SampleSmoothedGroundHeight(Vector2 point, float radius)
        {
            float diagonal = radius * 0.70710678f;
            return (
                SampleGroundHeight(point) * 4f
                + SampleGroundHeight(point + Vector2.right * radius)
                + SampleGroundHeight(point + Vector2.left * radius)
                + SampleGroundHeight(point + Vector2.up * radius)
                + SampleGroundHeight(point + Vector2.down * radius)
                + SampleGroundHeight(point + new Vector2(diagonal, diagonal))
                + SampleGroundHeight(point + new Vector2(-diagonal, diagonal))
                + SampleGroundHeight(point + new Vector2(diagonal, -diagonal))
                + SampleGroundHeight(point + new Vector2(-diagonal, -diagonal)))
                / 12f;
        }

        float SampleRoadHeight(Vector2 point, float rawHeight)
        {
            SampleWater(point, rawHeight, out CitySurfaceKind kind, out _);
            if (kind != CitySurfaceKind.Land)
                return rawHeight;

            float weightedHeight = 0f;
            float totalWeight = 0f;
            for (int i = 0; i < roadStamps.Count; i++)
            {
                RoadStamp stamp = roadStamps[i];
                float t = ClosestSegmentParameter(point, stamp.Start, stamp.End);
                Vector2 closest = Vector2.Lerp(stamp.Start, stamp.End, t);
                float distance = Vector2.Distance(point, closest);
                float outer = stamp.HalfWidth + stamp.BlendWidth;
                if (distance >= outer)
                    continue;
                float weight = distance <= stamp.HalfWidth
                    ? 1f
                    : 1f - Mathf.SmoothStep(
                        0f,
                        1f,
                        (distance - stamp.HalfWidth) / stamp.BlendWidth);
                float target = Mathf.Lerp(
                    stamp.StartHeight,
                    stamp.EndHeight,
                    t);
                target = Mathf.Clamp(target, rawHeight - 1.5f, rawHeight + 1.5f);
                weightedHeight += target * weight;
                totalWeight += weight;
            }
            return totalWeight <= 0f
                ? rawHeight
                : Mathf.Lerp(
                    rawHeight,
                    weightedHeight / totalWeight,
                    Mathf.Clamp01(totalWeight));
        }

        bool TryClosestRiverPoint(
            RiverFeature river,
            Vector2 point,
            out float distance,
            out float progress,
            out float waterHeight)
        {
            distance = float.MaxValue;
            progress = 0f;
            waterHeight = 0f;
            if (river.Points.Count < 2)
                return false;
            for (int i = 0; i < river.Points.Count - 1; i++)
            {
                float t = ClosestSegmentParameter(
                    point,
                    river.Points[i],
                    river.Points[i + 1]);
                Vector2 closest = Vector2.Lerp(
                    river.Points[i],
                    river.Points[i + 1],
                    t);
                float candidate = Vector2.Distance(point, closest);
                if (candidate >= distance)
                    continue;
                distance = candidate;
                progress = (i + t) / (river.Points.Count - 1f);
                waterHeight = Mathf.Lerp(
                    river.WaterHeights[i],
                    river.WaterHeights[i + 1],
                    t);
            }
            return distance < float.MaxValue;
        }

        void ResolveFeatureRoots()
        {
            Transform sceneRoot = transform.parent != null ? transform.parent : transform;
            Transform features = sceneRoot.Find("TerrainFeatures");
            if (features == null)
            {
                var gameObject = new GameObject("TerrainFeatures");
                features = gameObject.transform;
                features.SetParent(sceneRoot, false);
            }
            if (riversRoot == null)
                riversRoot = FindOrCreateChild(features, "Rivers");
            if (lakesRoot == null)
                lakesRoot = FindOrCreateChild(features, "Lakes");
        }

        static Transform FindOrCreateChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null)
                return child;
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            return gameObject.transform;
        }

        static void ClearGeneratedChildren(Transform root)
        {
            if (root == null)
                return;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                MeshFilter filter = child.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    DestroySafely(filter.sharedMesh);
                DestroySafely(child.gameObject);
            }
        }

        void ResolveComponents()
        {
            if (meshFilter == null)
                meshFilter = GetComponent<MeshFilter>();
            if (meshRenderer == null)
                meshRenderer = GetComponent<MeshRenderer>();
            if (meshCollider == null)
                meshCollider = GetComponent<MeshCollider>();
        }

        void OnDestroy()
        {
            if (terrainMesh != null)
                DestroySafely(terrainMesh);
            terrainMesh = null;
            ClearGeneratedChildren(riversRoot);
            ClearGeneratedChildren(lakesRoot);
        }

        static float ClosestSegmentParameter(
            Vector2 point,
            Vector2 start,
            Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0.000001f)
                return 0f;
            return Mathf.Clamp01(
                Vector2.Dot(point - start, segment) / lengthSquared);
        }

        static Vector2Int Quantize(Vector2 point)
        {
            return new Vector2Int(
                Mathf.RoundToInt(point.x * 100f),
                Mathf.RoundToInt(point.y * 100f));
        }

        static void DestroySafely(UnityEngine.Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }
}
