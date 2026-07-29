using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityGeneration
{
    public sealed class CityRoadRenderChunk
    {
        public int StableIndex { get; internal set; }
        public int RegionIndex { get; internal set; }
        public int ChunkX { get; internal set; }
        public int ChunkY { get; internal set; }
        public Vector2 Center { get; internal set; }
        public List<CityRoadModulePlacement> Roads { get; } =
            new List<CityRoadModulePlacement>();
        public List<CityPathwayModulePlacement> Pathways { get; } =
            new List<CityPathwayModulePlacement>();
    }

    public static class CityModularRoadMeshFactory
    {
        readonly struct SourceMeshKey : IEquatable<SourceMeshKey>
        {
            readonly int prefabId;
            readonly int referencePrefabId;
            readonly int widthMillimeters;
            readonly int depthMillimeters;
            readonly bool exactFit;
            readonly RoadModuleNormalizationMode normalizationMode;

            public SourceMeshKey(
                GameObject prefab,
                GameObject referencePrefab,
                float width,
                float depth,
                bool fitExactly,
                RoadModuleNormalizationMode mode)
            {
                prefabId = prefab != null ? prefab.GetInstanceID() : 0;
                referencePrefabId =
                    referencePrefab != null
                        ? referencePrefab.GetInstanceID()
                        : 0;
                widthMillimeters = Mathf.RoundToInt(width * 1000f);
                depthMillimeters = Mathf.RoundToInt(depth * 1000f);
                exactFit = fitExactly;
                normalizationMode = mode;
            }

            public bool Equals(SourceMeshKey other)
            {
                return prefabId == other.prefabId
                    && referencePrefabId == other.referencePrefabId
                    && widthMillimeters == other.widthMillimeters
                    && depthMillimeters == other.depthMillimeters
                    && exactFit == other.exactFit
                    && normalizationMode == other.normalizationMode;
            }

            public override bool Equals(object obj)
            {
                return obj is SourceMeshKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = prefabId;
                    hash = hash * 397 ^ referencePrefabId;
                    hash = hash * 397 ^ widthMillimeters;
                    hash = hash * 397 ^ depthMillimeters;
                    hash = hash * 397 ^ exactFit.GetHashCode();
                    hash = hash * 397 ^ (int)normalizationMode;
                    return hash;
                }
            }
        }

        readonly struct CalibrationKey : IEquatable<CalibrationKey>
        {
            readonly int referencePrefabId;
            readonly int targetMillimeters;

            public CalibrationKey(GameObject referencePrefab, float targetSize)
            {
                referencePrefabId =
                    referencePrefab != null
                        ? referencePrefab.GetInstanceID()
                        : 0;
                targetMillimeters = Mathf.RoundToInt(targetSize * 1000f);
            }

            public bool Equals(CalibrationKey other)
            {
                return referencePrefabId == other.referencePrefabId
                    && targetMillimeters == other.targetMillimeters;
            }

            public override bool Equals(object obj)
            {
                return obj is CalibrationKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return referencePrefabId * 397 ^ targetMillimeters;
                }
            }
        }

        sealed class SharedKitCalibration
        {
            public CityRoadGeometryCalibration Result;
            public float ReferenceSurfaceHeight;
            public readonly Dictionary<CityRoadModuleType, int>
                QuarterTurnCorrections =
                    new Dictionary<CityRoadModuleType, int>();
        }

        sealed class SocketSignature
        {
            public readonly List<Vector3> Layers = new List<Vector3>();
        }

        static readonly Dictionary<SourceMeshKey, Mesh> SourceMeshes =
            new Dictionary<SourceMeshKey, Mesh>();
        static readonly Dictionary<CalibrationKey, SharedKitCalibration>
            KitCalibrations =
                new Dictionary<CalibrationKey, SharedKitCalibration>();

        public static List<CityRoadRenderChunk> GroupRoads(
            IReadOnlyList<CityRoadModulePlacement> placements,
            int chunkSize)
        {
            var chunks = new Dictionary<(int, int, int), CityRoadRenderChunk>();
            chunkSize = Mathf.Max(1, chunkSize);
            for (int i = 0; i < placements.Count; i++)
            {
                CityRoadModulePlacement placement = placements[i];
                int chunkX = FloorDiv(placement.GridX, chunkSize);
                int chunkY = FloorDiv(placement.GridY, chunkSize);
                var key = (placement.RegionIndex, chunkX, chunkY);
                if (!chunks.TryGetValue(key, out CityRoadRenderChunk chunk))
                {
                    chunk = new CityRoadRenderChunk
                    {
                        RegionIndex = placement.RegionIndex,
                        ChunkX = chunkX,
                        ChunkY = chunkY
                    };
                    chunks.Add(key, chunk);
                }
                chunk.Roads.Add(placement);
            }
            return FinalizeChunks(chunks.Values);
        }

        public static List<CityRoadRenderChunk> GroupPathways(
            IReadOnlyList<CityPathwayModulePlacement> placements,
            int roadChunkSize,
            float pathwayUnitSize,
            float roadCellSize)
        {
            var chunks = new Dictionary<(int, int, int), CityRoadRenderChunk>();
            int unitsPerRoadCell = Mathf.Max(
                1,
                Mathf.RoundToInt(roadCellSize / pathwayUnitSize));
            int chunkUnits = Mathf.Max(1, roadChunkSize * unitsPerRoadCell);
            for (int i = 0; i < placements.Count; i++)
            {
                CityPathwayModulePlacement placement = placements[i];
                int chunkX = FloorDiv(placement.GridX, chunkUnits);
                int chunkY = FloorDiv(placement.GridY, chunkUnits);
                var key = (placement.RegionIndex, chunkX, chunkY);
                if (!chunks.TryGetValue(key, out CityRoadRenderChunk chunk))
                {
                    chunk = new CityRoadRenderChunk
                    {
                        RegionIndex = placement.RegionIndex,
                        ChunkX = chunkX,
                        ChunkY = chunkY
                    };
                    chunks.Add(key, chunk);
                }
                chunk.Pathways.Add(placement);
            }
            return FinalizeChunks(chunks.Values);
        }

        public static GameObject CreateRoadChunk(
            string name,
            CityRoadRenderChunk chunk,
            ModernCityRoadModuleLibrary library,
            Vector2 modularAxis,
            float cellSize,
            float height,
            Material material,
            Transform parent)
        {
            var instances = new List<CombineInstance>(chunk.Roads.Count);
            float basisAngle =
                CalculateGridBasisAngleFromAxisX(modularAxis);
            for (int i = 0; i < chunk.Roads.Count; i++)
            {
                CityRoadModulePlacement placement = chunk.Roads[i];
                GameObject prefab = library.GetRoadPrefab(placement.Type);
                if (prefab == null)
                    continue;
                float targetSize = cellSize * placement.FootprintCells;
                Mesh source = GetNormalizedRoadSourceMesh(
                    library,
                    prefab,
                    targetSize);
                if (source == null)
                    continue;
                int quarterTurns = placement.QuarterTurns
                    + GetEffectiveRoadQuarterTurnCorrection(
                        library,
                        placement.Type,
                        targetSize);
                instances.Add(new CombineInstance
                {
                    mesh = source,
                    subMeshIndex = 0,
                    transform = Matrix4x4.TRS(
                        new Vector3(
                            placement.Position.x,
                            height,
                            placement.Position.y),
                        Quaternion.Euler(
                            0f,
                            basisAngle + quarterTurns * 90f,
                            0f),
                        Vector3.one)
                });
            }
            return CreateCombinedObject(name, instances, material, parent);
        }

        public static GameObject CreatePathwayChunk(
            string name,
            CityRoadRenderChunk chunk,
            ModernCityRoadModuleLibrary library,
            Vector2 modularAxis,
            float unitSize,
            float height,
            Material material,
            Transform parent)
        {
            var instances = new List<CombineInstance>(chunk.Pathways.Count);
            float basisAngle =
                CalculateGridBasisAngleFromAxisX(modularAxis);
            for (int i = 0; i < chunk.Pathways.Count; i++)
            {
                CityPathwayModulePlacement placement = chunk.Pathways[i];
                GameObject prefab = library.GetPathwayPrefab(
                    placement.Variant,
                    placement.SizeX,
                    placement.SizeY,
                    out int correction);
                if (prefab == null)
                    continue;
                float width = placement.SizeX * unitSize;
                float depth = placement.SizeY * unitSize;
                int quarterTurns = correction;
                bool swapsAxes = (quarterTurns & 1) != 0;
                Mesh source = GetNormalizedSourceMesh(
                    prefab,
                    swapsAxes ? depth : width,
                    swapsAxes ? width : depth,
                    true);
                if (source == null)
                    continue;
                instances.Add(new CombineInstance
                {
                    mesh = source,
                    subMeshIndex = 0,
                    transform = Matrix4x4.TRS(
                        new Vector3(
                            placement.Position.x,
                            height,
                            placement.Position.y),
                        Quaternion.Euler(
                            0f,
                            basisAngle + quarterTurns * 90f,
                            0f),
                        Vector3.one)
                });
            }
            return CreateCombinedObject(name, instances, material, parent);
        }

        public static float CalculateGridBasisAngleFromAxisX(
            Vector2 modularAxisX)
        {
            Vector2 axisX = modularAxisX.sqrMagnitude < 0.001f
                ? Vector2.right
                : modularAxisX.normalized;
            Vector2 axisY = new Vector2(-axisX.y, axisX.x);
            // Canonical module North is local +Z and must align with the
            // grid's AxisY. AxisX is the grid's East direction.
            return -Vector2.SignedAngle(Vector2.up, axisY);
        }

        public static void ClearSourceCache()
        {
            foreach (Mesh mesh in SourceMeshes.Values)
                DestroySafely(mesh);
            SourceMeshes.Clear();
            KitCalibrations.Clear();
        }

        static List<CityRoadRenderChunk> FinalizeChunks(
            IEnumerable<CityRoadRenderChunk> chunks)
        {
            List<CityRoadRenderChunk> ordered = chunks
                .OrderBy(value => value.RegionIndex)
                .ThenBy(value => value.ChunkX)
                .ThenBy(value => value.ChunkY)
                .ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                CityRoadRenderChunk chunk = ordered[i];
                chunk.StableIndex = i;
                Vector2 sum = Vector2.zero;
                int count = 0;
                for (int road = 0; road < chunk.Roads.Count; road++)
                {
                    sum += chunk.Roads[road].Position;
                    count++;
                }
                for (int path = 0; path < chunk.Pathways.Count; path++)
                {
                    sum += chunk.Pathways[path].Position;
                    count++;
                }
                chunk.Center = count > 0 ? sum / count : Vector2.zero;
            }
            return ordered;
        }

        static GameObject CreateCombinedObject(
            string name,
            IReadOnlyList<CombineInstance> instances,
            Material material,
            Transform parent)
        {
            if (instances.Count == 0)
                return null;

            var mesh = new Mesh
            {
                name = name + " Mesh",
                indexFormat = IndexFormat.UInt32
            };
            mesh.CombineMeshes(instances.ToArray(), true, true, false);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);

            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            var collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            return gameObject;
        }

        public static bool TryValidateLibraryGeometry(
            ModernCityRoadModuleLibrary library,
            float targetCellSize,
            out CityRoadGeometryCalibration calibration,
            out string error)
        {
            calibration = null;
            error = string.Empty;
            if (library == null)
            {
                error = "Modern City road module library is missing.";
                return false;
            }
            if (!library.IsComplete(out error))
                return false;
            if (library.normalizationMode
                == RoadModuleNormalizationMode.LegacyBoundsFit)
            {
                calibration = new CityRoadGeometryCalibration
                {
                    SourceCellSize = targetCellSize,
                    UniformScale = 1f,
                    SharedSourceOrigin = Vector3.zero,
                    SourceVerticalAxis = 1
                };
                return true;
            }

            if (!TryGetSharedKitCalibration(
                    library,
                    targetCellSize,
                    out SharedKitCalibration shared,
                    out error))
            {
                return false;
            }

            CityRoadModuleType[] enabledTypes =
            {
                CityRoadModuleType.End,
                CityRoadModuleType.Straight,
                CityRoadModuleType.StraightCrossing,
                CityRoadModuleType.CurveSmall,
                CityRoadModuleType.TIntersection,
                CityRoadModuleType.CrossIntersection
            };
            Mesh referenceMesh = GetNormalizedRoadSourceMesh(
                library,
                library.roadStraight,
                targetCellSize);
            if (!TryDetectRawConnections(
                    referenceMesh,
                    targetCellSize,
                    library.SocketPositionTolerance,
                    null,
                    out CityRoadConnectionMask referenceRawConnections,
                    out SocketSignature referenceSignature,
                    out float referenceSocketError))
            {
                error = "Could not read the Road Straight socket profile.";
                return false;
            }
            CityRoadModuleDefinition straightDefinition =
                library.GetRoadDefinition(CityRoadModuleType.Straight);
            if (!TrySolveSourceCorrection(
                    referenceRawConnections,
                    straightDefinition.CanonicalConnections,
                    out int straightCorrection))
            {
                error =
                    $"Road Straight source sockets "
                    + $"{referenceRawConnections} cannot be mapped to "
                    + $"{straightDefinition.CanonicalConnections}.";
                return false;
            }
            int expectedStraightCorrection =
                ModernCityRoadModuleLibrary
                    .GetDescriptor(CityRoadModuleType.Straight)
                    .SourceQuarterTurnCorrection;
            if (straightCorrection != expectedStraightCorrection)
            {
                error =
                    $"Road Straight detected correction "
                    + $"{straightCorrection} does not match its explicit "
                    + $"descriptor {expectedStraightCorrection}.";
                return false;
            }
            shared.QuarterTurnCorrections[
                CityRoadModuleType.Straight] =
                expectedStraightCorrection;
            float maximumSocketError = referenceSocketError;
            float maximumSurfaceError = 0f;
            for (int i = 0; i < enabledTypes.Length; i++)
            {
                CityRoadModuleType type = enabledTypes[i];
                GameObject prefab = library.GetRoadPrefab(type);
                Mesh mesh = GetNormalizedRoadSourceMesh(
                    library,
                    prefab,
                    targetCellSize);
                if (mesh == null)
                {
                    error = $"Could not bake road module {type}.";
                    return false;
                }
                if (mesh.uv == null || mesh.uv.Length != mesh.vertexCount)
                {
                    error = $"Road module {type} has invalid UV data.";
                    return false;
                }
                if (mesh.normals == null
                    || mesh.normals.Length != mesh.vertexCount
                    || mesh.tangents == null
                    || mesh.tangents.Length != mesh.vertexCount)
                {
                    error =
                        $"Road module {type} has invalid normal or tangent data.";
                    return false;
                }

                float halfCell = targetCellSize * 0.5f;
                Bounds bounds = mesh.bounds;
                // A curve or end module intentionally does not occupy every
                // side of the cell. Only reject geometry that leaks outside
                // the shared cell here; the actual connected sides are
                // validated below from their socket cross-sections.
                float socketError = Mathf.Max(
                    Mathf.Max(
                        0f,
                        -halfCell - bounds.min.x,
                        bounds.max.x - halfCell),
                    Mathf.Max(
                        0f,
                        -halfCell - bounds.min.z,
                        bounds.max.z - halfCell));
                CityRoadConnectionMask maximumErrorSide =
                    CityRoadConnectionMask.None;
                CityRoadModuleDefinition definition =
                    library.GetRoadDefinition(type);
                if (!TryDetectRawConnections(
                        mesh,
                        targetCellSize,
                        library.SocketPositionTolerance,
                        referenceSignature,
                        out CityRoadConnectionMask rawConnections,
                        out _,
                        out float profileError))
                {
                    error =
                        $"Road module {type} has no valid road socket "
                        + "profile at a cell edge.";
                    return false;
                }
                if (type == CityRoadModuleType.End
                    && CountDirections(rawConnections) > 1)
                {
                    // The kit's End mesh keeps asphalt geometry up to both
                    // X edges, so height/profile data alone cannot distinguish
                    // its west-facing open road from its east-facing textured
                    // cap. This source-facing tag is explicit instead of being
                    // guessed from bounds or profile richness.
                    rawConnections =
                        (rawConnections & CityRoadConnectionMask.West) != 0
                            ? CityRoadConnectionMask.West
                            : FirstDirection(rawConnections);
                }
                if (!TrySolveSourceCorrection(
                        rawConnections,
                        definition.CanonicalConnections,
                        out int correction))
                {
                    error =
                        $"Road module {type} source sockets "
                        + $"{rawConnections} cannot be mapped to "
                        + $"{definition.CanonicalConnections}.";
                    return false;
                }
                int expectedCorrection =
                    ModernCityRoadModuleLibrary
                        .GetDescriptor(type)
                        .SourceQuarterTurnCorrection;
                if (correction != expectedCorrection)
                {
                    error =
                        $"Road module {type} detected correction "
                        + $"{correction} does not match its explicit "
                        + $"descriptor {expectedCorrection}.";
                    return false;
                }
                shared.QuarterTurnCorrections[type] =
                    expectedCorrection;
                if (profileError > socketError)
                {
                    socketError = profileError;
                    maximumErrorSide = rawConnections;
                }
                float surfaceError = Mathf.Abs(
                    bounds.max.y - shared.ReferenceSurfaceHeight);
                maximumSocketError =
                    Mathf.Max(maximumSocketError, socketError);
                maximumSurfaceError =
                    Mathf.Max(maximumSurfaceError, surfaceError);
                if (socketError > library.SocketPositionTolerance)
                {
                    error =
                        $"Road module {type} socket error "
                        + $"{socketError:F4}m exceeds "
                        + $"{library.SocketPositionTolerance:F4}m"
                        + (maximumErrorSide
                            == CityRoadConnectionMask.None
                            ? " (cell bounds)."
                            : $" at {maximumErrorSide}.");
                    return false;
                }
                if (surfaceError > library.SurfaceHeightTolerance)
                {
                    error =
                        $"Road module {type} surface height error "
                        + $"{surfaceError:F4}m exceeds "
                        + $"{library.SurfaceHeightTolerance:F4}m.";
                    return false;
                }
            }

            shared.Result.MaximumSocketError = maximumSocketError;
            shared.Result.MaximumSurfaceHeightError = maximumSurfaceError;
            calibration = shared.Result;
            return true;
        }

        static int GetEffectiveRoadQuarterTurnCorrection(
            ModernCityRoadModuleLibrary library,
            CityRoadModuleType type,
            float targetCellSize)
        {
            if (library.normalizationMode
                    == RoadModuleNormalizationMode.SharedKitCoordinates)
            {
                return ModernCityRoadModuleLibrary
                    .GetDescriptor(type)
                    .SourceQuarterTurnCorrection;
            }
            return library.GetRoadQuarterTurnCorrection(type);
        }

        static bool TryDetectRawConnections(
            Mesh mesh,
            float cellSize,
            float tolerance,
            SocketSignature reference,
            out CityRoadConnectionMask connections,
            out SocketSignature selectedReference,
            out float maximumError)
        {
            connections = CityRoadConnectionMask.None;
            selectedReference = reference;
            maximumError = 0f;
            var signatures =
                new Dictionary<CityRoadConnectionMask, SocketSignature>();
            foreach (CityRoadConnectionMask side in EnumerateDirections(
                         CityRoadConnectionMask.All))
            {
                if (TryExtractSocketSignature(
                        mesh,
                        side,
                        cellSize,
                        tolerance,
                        out SocketSignature signature))
                {
                    signatures.Add(side, signature);
                }
            }
            if (signatures.Count == 0)
                return false;

            if (selectedReference == null)
            {
                // A connected road edge contains both the road surface and
                // the raised curb/sidewalk layers. A sealed edge only has
                // the outer sidewalk layer, so choose the richest profile.
                selectedReference = signatures
                    .OrderByDescending(pair => pair.Value.Layers.Count)
                    .ThenBy(pair => (int)pair.Key)
                    .First()
                    .Value;
            }

            foreach (KeyValuePair<CityRoadConnectionMask, SocketSignature>
                     pair in signatures)
            {
                float error = CompareSocketSignatures(
                    selectedReference,
                    pair.Value);
                if (error > tolerance)
                    continue;
                connections |= pair.Key;
                maximumError = Mathf.Max(maximumError, error);
            }
            return connections != CityRoadConnectionMask.None;
        }

        static bool TrySolveSourceCorrection(
            CityRoadConnectionMask sourceConnections,
            CityRoadConnectionMask canonicalConnections,
            out int correction)
        {
            for (int turns = 0; turns < 4; turns++)
            {
                if (ModernCityRoadModuleLibrary.RotateMask(
                        sourceConnections,
                        turns)
                    == canonicalConnections)
                {
                    correction = turns;
                    return true;
                }
            }
            correction = 0;
            return false;
        }

        static int CountDirections(CityRoadConnectionMask mask)
        {
            int count = 0;
            foreach (CityRoadConnectionMask ignored
                     in EnumerateDirections(mask))
            {
                count++;
            }
            return count;
        }

        static CityRoadConnectionMask FirstDirection(
            CityRoadConnectionMask mask)
        {
            foreach (CityRoadConnectionMask direction
                     in EnumerateDirections(mask))
            {
                return direction;
            }
            return CityRoadConnectionMask.None;
        }

        static Mesh GetNormalizedRoadSourceMesh(
            ModernCityRoadModuleLibrary library,
            GameObject prefab,
            float targetSize)
        {
            if (library == null || prefab == null)
                return null;
            if (library.normalizationMode
                == RoadModuleNormalizationMode.LegacyBoundsFit)
            {
                return GetNormalizedSourceMesh(
                    prefab,
                    targetSize,
                    targetSize,
                    false);
            }

            var key = new SourceMeshKey(
                prefab,
                library.roadStraight,
                targetSize,
                targetSize,
                false,
                RoadModuleNormalizationMode.SharedKitCoordinates);
            if (SourceMeshes.TryGetValue(key, out Mesh cached)
                && cached != null)
            {
                return cached;
            }
            if (!TryGetSharedKitCalibration(
                    library,
                    targetSize,
                    out SharedKitCalibration calibration,
                    out _))
            {
                return null;
            }

            Mesh combined = BakePrefabMesh(prefab);
            if (combined == null)
                return null;
            ApplyAxisConversion(
                combined,
                calibration.Result.SourceVerticalAxis);
            Bounds sourceBounds = combined.bounds;
            Vector3 origin = calibration.Result.SharedSourceOrigin;
            Vector3 centerDelta = new Vector3(
                sourceBounds.center.x - origin.x,
                sourceBounds.min.y - origin.y,
                sourceBounds.center.z - origin.z)
                * calibration.Result.UniformScale;
            Vector3 correction = -centerDelta;
            float maxOffset = library.MaximumAutomaticOffset;
            if (Mathf.Abs(correction.x) > maxOffset
                || Mathf.Abs(correction.y) > maxOffset
                || Mathf.Abs(correction.z) > maxOffset)
            {
                DestroySafely(combined);
                return null;
            }

            TransformMeshVertices(
                combined,
                origin,
                calibration.Result.UniformScale,
                correction);
            combined.name = prefab.name + " Shared Kit Normalized";
            SourceMeshes[key] = combined;
            return combined;
        }

        static bool TryGetSharedKitCalibration(
            ModernCityRoadModuleLibrary library,
            float targetSize,
            out SharedKitCalibration calibration,
            out string error)
        {
            calibration = null;
            error = string.Empty;
            if (library.roadStraight == null)
            {
                error = "Road Straight is required as the kit reference.";
                return false;
            }

            var key = new CalibrationKey(library.roadStraight, targetSize);
            if (KitCalibrations.TryGetValue(key, out calibration))
                return true;

            Mesh reference = BakePrefabMesh(library.roadStraight);
            if (reference == null)
            {
                error = "Could not read the Road Straight reference mesh.";
                return false;
            }
            int verticalAxis = FindVerticalAxis(reference.bounds);
            ApplyAxisConversion(reference, verticalAxis);
            Bounds bounds = reference.bounds;
            float sourceCellSize = Mathf.Max(bounds.size.x, bounds.size.z);
            if (sourceCellSize <= 0.001f)
            {
                DestroySafely(reference);
                error = "Road Straight reference has an invalid cell size.";
                return false;
            }

            float uniformScale = targetSize / sourceCellSize;
            Vector3 origin = new Vector3(
                bounds.center.x,
                bounds.min.y,
                bounds.center.z);
            float surfaceHeight =
                (bounds.max.y - origin.y) * uniformScale;
            calibration = new SharedKitCalibration
            {
                ReferenceSurfaceHeight = surfaceHeight,
                Result = new CityRoadGeometryCalibration
                {
                    SourceCellSize = sourceCellSize,
                    UniformScale = uniformScale,
                    SharedSourceOrigin = origin,
                    SourceVerticalAxis = verticalAxis
                }
            };
            KitCalibrations.Add(key, calibration);
            DestroySafely(reference);
            return true;
        }

        static Mesh GetNormalizedSourceMesh(
            GameObject prefab,
            float targetWidth,
            float targetDepth,
            bool fitExactly)
        {
            var key = new SourceMeshKey(
                prefab,
                null,
                targetWidth,
                targetDepth,
                fitExactly,
                RoadModuleNormalizationMode.LegacyBoundsFit);
            if (SourceMeshes.TryGetValue(key, out Mesh cached)
                && cached != null)
            {
                return cached;
            }

            Mesh combined = BakePrefabMesh(prefab);
            if (combined == null)
                return null;
            ReorientToHorizontal(combined);
            Bounds bounds = combined.bounds;
            float sourceWidth = Mathf.Max(0.001f, bounds.size.x);
            float sourceDepth = Mathf.Max(0.001f, bounds.size.z);
            float scaleX;
            float scaleZ;
            if (fitExactly)
            {
                scaleX = targetWidth / sourceWidth;
                scaleZ = targetDepth / sourceDepth;
            }
            else
            {
                float uniform = Mathf.Min(
                    targetWidth / sourceWidth,
                    targetDepth / sourceDepth);
                scaleX = uniform;
                scaleZ = uniform;
            }
            float scaleY = Mathf.Min(scaleX, scaleZ);
            Vector3[] vertices = combined.vertices;
            Vector3 center = bounds.center;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertex = vertices[i];
                vertices[i] = new Vector3(
                    (vertex.x - center.x) * scaleX,
                    (vertex.y - bounds.min.y) * scaleY,
                    (vertex.z - center.z) * scaleZ);
            }
            combined.vertices = vertices;
            combined.RecalculateBounds();
            combined.RecalculateNormals();
            combined.RecalculateTangents();
            SourceMeshes[key] = combined;
            return combined;
        }

        static Mesh BakePrefabMesh(GameObject prefab)
        {
            if (prefab == null)
                return null;
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = "ModernCityModuleBakeSource";
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.SetActive(false);
            MeshFilter[] filters =
                instance.GetComponentsInChildren<MeshFilter>(true);
            var combines = new List<CombineInstance>();
            Matrix4x4 toRoot = instance.transform.worldToLocalMatrix;
            for (int filterIndex = 0;
                 filterIndex < filters.Length;
                 filterIndex++)
            {
                Mesh source = filters[filterIndex].sharedMesh;
                if (source == null)
                    continue;
                for (int subMesh = 0;
                     subMesh < source.subMeshCount;
                     subMesh++)
                {
                    combines.Add(new CombineInstance
                    {
                        mesh = source,
                        subMeshIndex = subMesh,
                        transform = toRoot
                            * filters[filterIndex].transform.localToWorldMatrix
                    });
                }
            }

            var combined = new Mesh
            {
                name = prefab.name + " Bake Source",
                indexFormat = IndexFormat.UInt32,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (combines.Count > 0)
                combined.CombineMeshes(combines.ToArray(), true, true, false);
            DestroySafely(instance);
            if (combined.vertexCount == 0)
            {
                DestroySafely(combined);
                return null;
            }
            combined.RecalculateBounds();
            return combined;
        }

        static void TransformMeshVertices(
            Mesh mesh,
            Vector3 sourceOrigin,
            float uniformScale,
            Vector3 correction)
        {
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] =
                    (vertices[i] - sourceOrigin) * uniformScale
                    + correction;
            }
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            EnsureSurfaceVectors(mesh);
        }

        static int FindVerticalAxis(Bounds bounds)
        {
            Vector3 size = bounds.size;
            int verticalAxis = 1;
            float minimumSize = size.y;
            if (size.z < minimumSize)
            {
                verticalAxis = 2;
                minimumSize = size.z;
            }
            if (size.x < minimumSize)
                verticalAxis = 0;
            return verticalAxis;
        }

        static void ReorientToHorizontal(Mesh mesh)
        {
            int verticalAxis = FindVerticalAxis(mesh.bounds);
            ApplyAxisConversion(mesh, verticalAxis);
        }

        static void ApplyAxisConversion(Mesh mesh, int verticalAxis)
        {
            if (verticalAxis == 1)
            {
                EnsureSurfaceVectors(mesh);
                return;
            }

            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertex = vertices[i];
                vertices[i] = verticalAxis == 2
                    ? new Vector3(vertex.x, vertex.z, -vertex.y)
                    : new Vector3(-vertex.y, vertex.x, vertex.z);
            }
            mesh.vertices = vertices;

            Vector3[] normals = mesh.normals;
            if (normals != null && normals.Length == mesh.vertexCount)
            {
                for (int i = 0; i < normals.Length; i++)
                {
                    Vector3 normal = normals[i];
                    normals[i] = verticalAxis == 2
                        ? new Vector3(normal.x, normal.z, -normal.y)
                        : new Vector3(-normal.y, normal.x, normal.z);
                }
                mesh.normals = normals;
            }

            Vector4[] tangents = mesh.tangents;
            if (tangents != null && tangents.Length == mesh.vertexCount)
            {
                for (int i = 0; i < tangents.Length; i++)
                {
                    Vector4 tangent = tangents[i];
                    Vector3 direction = verticalAxis == 2
                        ? new Vector3(tangent.x, tangent.z, -tangent.y)
                        : new Vector3(-tangent.y, tangent.x, tangent.z);
                    tangents[i] = new Vector4(
                        direction.x,
                        direction.y,
                        direction.z,
                        tangent.w);
                }
                mesh.tangents = tangents;
            }
            mesh.RecalculateBounds();
            EnsureSurfaceVectors(mesh);
        }

        static void EnsureSurfaceVectors(Mesh mesh)
        {
            if (mesh.normals == null
                || mesh.normals.Length != mesh.vertexCount)
            {
                mesh.RecalculateNormals();
            }
            if (mesh.tangents == null
                || mesh.tangents.Length != mesh.vertexCount)
            {
                mesh.RecalculateTangents();
            }
        }

        static IEnumerable<CityRoadConnectionMask> EnumerateDirections(
            CityRoadConnectionMask mask)
        {
            CityRoadConnectionMask[] directions =
            {
                CityRoadConnectionMask.North,
                CityRoadConnectionMask.East,
                CityRoadConnectionMask.South,
                CityRoadConnectionMask.West
            };
            for (int i = 0; i < directions.Length; i++)
            {
                if ((mask & directions[i]) != 0)
                    yield return directions[i];
            }
        }

        static bool TryExtractSocketSignature(
            Mesh mesh,
            CityRoadConnectionMask side,
            float cellSize,
            float tolerance,
            out SocketSignature signature)
        {
            signature = new SocketSignature();
            if (mesh == null)
                return false;

            float half = cellSize * 0.5f;
            float strip = Mathf.Max(0.02f, tolerance * 2f);
            var samples = new List<Vector2>();
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertex = vertices[i];
                float planeCoordinate;
                float targetCoordinate;
                float lateral;
                switch (side)
                {
                    case CityRoadConnectionMask.North:
                        planeCoordinate = vertex.z;
                        targetCoordinate = half;
                        lateral = vertex.x;
                        break;
                    case CityRoadConnectionMask.East:
                        planeCoordinate = vertex.x;
                        targetCoordinate = half;
                        lateral = -vertex.z;
                        break;
                    case CityRoadConnectionMask.South:
                        planeCoordinate = vertex.z;
                        targetCoordinate = -half;
                        lateral = -vertex.x;
                        break;
                    case CityRoadConnectionMask.West:
                        planeCoordinate = vertex.x;
                        targetCoordinate = -half;
                        lateral = vertex.z;
                        break;
                    default:
                        return false;
                }
                if (Mathf.Abs(planeCoordinate - targetCoordinate) <= strip)
                    samples.Add(new Vector2(vertex.y, lateral));
            }
            if (samples.Count == 0)
                return false;

            samples.Sort((first, second) =>
            {
                int height = first.x.CompareTo(second.x);
                return height != 0
                    ? height
                    : first.y.CompareTo(second.y);
            });
            const float layerTolerance = 0.001f;
            int sampleIndex = 0;
            while (sampleIndex < samples.Count)
            {
                float height = samples[sampleIndex].x;
                float minimum = samples[sampleIndex].y;
                float maximum = samples[sampleIndex].y;
                int next = sampleIndex + 1;
                while (next < samples.Count
                       && Mathf.Abs(samples[next].x - height)
                           <= layerTolerance)
                {
                    minimum = Mathf.Min(minimum, samples[next].y);
                    maximum = Mathf.Max(maximum, samples[next].y);
                    next++;
                }
                signature.Layers.Add(
                    new Vector3(height, minimum, maximum));
                sampleIndex = next;
            }
            return signature.Layers.Count > 0;
        }

        static float CompareSocketSignatures(
            SocketSignature first,
            SocketSignature second)
        {
            return Mathf.Max(
                MaximumLayerDistance(first, second),
                MaximumLayerDistance(second, first));
        }

        static float MaximumLayerDistance(
            SocketSignature source,
            SocketSignature target)
        {
            float maximum = 0f;
            for (int i = 0; i < source.Layers.Count; i++)
            {
                Vector3 sourceLayer = source.Layers[i];
                float nearest = float.MaxValue;
                for (int j = 0; j < target.Layers.Count; j++)
                {
                    Vector3 targetLayer = target.Layers[j];
                    float distance = Mathf.Max(
                        Mathf.Abs(sourceLayer.x - targetLayer.x),
                        Mathf.Abs(sourceLayer.y - targetLayer.y),
                        Mathf.Abs(sourceLayer.z - targetLayer.z));
                    nearest = Mathf.Min(nearest, distance);
                }
                maximum = Mathf.Max(maximum, nearest);
            }
            return maximum;
        }

        static int FloorDiv(int value, int divisor)
        {
            int result = value / divisor;
            if (value < 0 && value % divisor != 0)
                result--;
            return result;
        }

        static void DestroySafely(UnityEngine.Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
