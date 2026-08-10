using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityPlanet.CityPcg
{
    /// <summary>
    /// Marks renderers that belong to one persistent, recursively sliceable
    /// urban section.  It lets a parent ruin keep its rigidbody/topple motion
    /// while only the currently hit child section is replaced.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UrbanSliceVisual : MonoBehaviour
    {
    }

    /// <summary>
    /// Runtime plane clipping for readable building meshes. Exterior submeshes
    /// retain the exact authored materials and shaders. Only the newly exposed
    /// cap receives the structural-interior material.
    /// </summary>
    internal static class UrbanRuntimeMeshSlicer
    {
        const float DistanceEpsilon = 0.0005f;
        const float CapMergeDistance = 0.015f;

        struct SliceVertex
        {
            public Vector3 position;
            public Vector3 normal;
            public Vector4 tangent;
            public Vector2 uv0;
            public Vector2 uv2;
            public Color32 color;

            public static SliceVertex Lerp(
                SliceVertex first,
                SliceVertex second,
                float amount)
            {
                Vector3 blendedNormal = Vector3.Lerp(
                    first.normal,
                    second.normal,
                    amount);
                if (blendedNormal.sqrMagnitude > 0.000001f)
                    blendedNormal.Normalize();
                Vector4 blendedTangent = Vector4.Lerp(
                    first.tangent,
                    second.tangent,
                    amount);
                Vector3 tangentDirection = new Vector3(
                    blendedTangent.x,
                    blendedTangent.y,
                    blendedTangent.z);
                if (tangentDirection.sqrMagnitude > 0.000001f)
                {
                    tangentDirection.Normalize();
                    blendedTangent.x = tangentDirection.x;
                    blendedTangent.y = tangentDirection.y;
                    blendedTangent.z = tangentDirection.z;
                }
                Color blendedColor = Color.Lerp(
                    first.color,
                    second.color,
                    amount);
                return new SliceVertex
                {
                    position = Vector3.Lerp(
                        first.position,
                        second.position,
                        amount),
                    normal = blendedNormal,
                    tangent = blendedTangent,
                    uv0 = Vector2.Lerp(first.uv0, second.uv0, amount),
                    uv2 = Vector2.Lerp(first.uv2, second.uv2, amount),
                    color = blendedColor
                };
            }
        }

        sealed class MeshBuilder
        {
            readonly List<Vector3> positions = new List<Vector3>(512);
            readonly List<Vector3> normals = new List<Vector3>(512);
            readonly List<Vector4> tangents = new List<Vector4>(512);
            readonly List<Vector2> uv0 = new List<Vector2>(512);
            readonly List<Vector2> uv2 = new List<Vector2>(512);
            readonly List<Color32> colors = new List<Color32>(512);
            readonly List<int>[] triangles;
            readonly bool hasNormals;
            readonly bool hasTangents;
            readonly bool hasUv0;
            readonly bool hasUv2;
            readonly bool hasColors;

            public int TriangleCount { get; private set; }

            public MeshBuilder(
                int subMeshCount,
                bool sourceHasNormals,
                bool sourceHasTangents,
                bool sourceHasUv0,
                bool sourceHasUv2,
                bool sourceHasColors)
            {
                triangles = new List<int>[Mathf.Max(1, subMeshCount)];
                for (int index = 0; index < triangles.Length; index++)
                    triangles[index] = new List<int>(192);
                hasNormals = sourceHasNormals;
                hasTangents = sourceHasTangents;
                hasUv0 = sourceHasUv0;
                hasUv2 = sourceHasUv2;
                hasColors = sourceHasColors;
            }

            public void AddTriangle(
                SliceVertex first,
                SliceVertex second,
                SliceVertex third,
                int subMesh)
            {
                subMesh = Mathf.Clamp(subMesh, 0, triangles.Length - 1);
                int start = positions.Count;
                AddVertex(first);
                AddVertex(second);
                AddVertex(third);
                triangles[subMesh].Add(start);
                triangles[subMesh].Add(start + 1);
                triangles[subMesh].Add(start + 2);
                TriangleCount++;
            }

            void AddVertex(SliceVertex vertex)
            {
                positions.Add(vertex.position);
                normals.Add(vertex.normal);
                tangents.Add(vertex.tangent);
                uv0.Add(vertex.uv0);
                uv2.Add(vertex.uv2);
                colors.Add(vertex.color);
            }

            public Mesh Build(string meshName, int outputSubMeshCount = -1)
            {
                if (TriangleCount <= 0)
                    return null;
                int usedSubMeshCount = outputSubMeshCount > 0
                    ? Mathf.Min(outputSubMeshCount, triangles.Length)
                    : triangles.Length;
                var mesh = new Mesh
                {
                    name = meshName,
                    indexFormat = positions.Count > 65535
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16
                };
                mesh.MarkDynamic();
                mesh.SetVertices(positions);
                if (hasNormals)
                    mesh.SetNormals(normals);
                if (hasTangents)
                    mesh.SetTangents(tangents);
                if (hasUv0)
                    mesh.SetUVs(0, uv0);
                if (hasUv2)
                    mesh.SetUVs(1, uv2);
                if (hasColors)
                    mesh.SetColors(colors);
                mesh.subMeshCount = usedSubMeshCount;
                for (int index = 0; index < usedSubMeshCount; index++)
                    mesh.SetTriangles(triangles[index], index, false);
                if (!hasNormals)
                    mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        struct CapPoint
        {
            public SliceVertex vertex;
            public Vector2 projected;
        }

        public static int CreateHalfRendererCopies(
            Renderer[] sources,
            Transform parent,
            Plane worldPlane,
            bool keepPositive,
            Material capMaterial,
            List<Mesh> ownedMeshes,
            string namePrefix)
        {
            if (sources == null || parent == null)
                return 0;
            int created = 0;
            for (int index = 0; index < sources.Length; index++)
            {
                Renderer source = sources[index];
                if (source == null || !source.enabled ||
                    !source.gameObject.activeInHierarchy ||
                    IsSecondaryLodRenderer(source))
                {
                    continue;
                }

                Mesh sourceMesh = ResolveMesh(source, ownedMeshes);
                if (sourceMesh == null || sourceMesh.vertexCount < 3)
                    continue;

                bool ownsOutput = false;
                bool hasCap = false;
                Mesh outputMesh = ResolveHalfMesh(
                    sourceMesh,
                    source.transform,
                    worldPlane,
                    keepPositive,
                    out ownsOutput,
                    out hasCap);
                if (outputMesh == null)
                    continue;
                if (ownsOutput)
                    ownedMeshes?.Add(outputMesh);

                Material[] materials = ResolveOutputMaterials(
                    source.sharedMaterials,
                    sourceMesh.subMeshCount,
                    hasCap,
                    capMaterial);
                CreateRendererCopy(
                    source,
                    outputMesh,
                    materials,
                    parent,
                    (namePrefix ?? "SliceVisual_") + source.name);
                created++;
            }
            return created;
        }

        static Mesh ResolveMesh(Renderer source, List<Mesh> ownedMeshes)
        {
            if (source is MeshRenderer)
            {
                MeshFilter filter = source.GetComponent<MeshFilter>();
                return filter != null ? filter.sharedMesh : null;
            }
            if (source is SkinnedMeshRenderer skinned)
            {
                var baked = new Mesh
                {
                    name = "UrbanSliceBaked_" + source.name
                };
                skinned.BakeMesh(baked);
                ownedMeshes?.Add(baked);
                return baked;
            }
            return null;
        }

        static Mesh ResolveHalfMesh(
            Mesh source,
            Transform sourceTransform,
            Plane worldPlane,
            bool keepPositive,
            out bool ownsOutput,
            out bool hasCap)
        {
            ownsOutput = false;
            hasCap = false;
            if (source == null || sourceTransform == null)
                return null;

            Vector3[] sourcePositions;
            try
            {
                sourcePositions = source.vertices;
            }
            catch (UnityException)
            {
                return null;
            }
            if (sourcePositions == null || sourcePositions.Length < 3)
                return null;

            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            for (int index = 0; index < sourcePositions.Length; index++)
            {
                float distance = worldPlane.GetDistanceToPoint(
                    sourceTransform.TransformPoint(sourcePositions[index]));
                minimum = Mathf.Min(minimum, distance);
                maximum = Mathf.Max(maximum, distance);
            }
            if (keepPositive && maximum < -DistanceEpsilon)
                return null;
            if (!keepPositive && minimum > DistanceEpsilon)
                return null;
            if ((keepPositive && minimum >= -DistanceEpsilon) ||
                (!keepPositive && maximum <= DistanceEpsilon))
            {
                return source;
            }
            // Never pretend that an intersected, non-readable mesh is a valid
            // half. Runtime static batching produces exactly this kind of mesh:
            // returning it here duplicates the whole city batch and assigns one
            // building material to all of its submeshes. Formal city generation
            // now excludes destructible hierarchies from batching; unsupported
            // replacement art therefore fails closed instead of changing its
            // texture, emission and gloss after a cut.
            if (!source.isReadable)
                return null;

            Mesh sliced = SliceReadableMesh(
                source,
                sourceTransform,
                worldPlane,
                keepPositive,
                out hasCap);
            ownsOutput = sliced != null;
            return sliced;
        }

        static Mesh SliceReadableMesh(
            Mesh source,
            Transform sourceTransform,
            Plane worldPlane,
            bool keepPositive,
            out bool hasCap)
        {
            hasCap = false;
            Vector3[] positions = source.vertices;
            Vector3[] normals = source.normals;
            Vector4[] tangents = source.tangents;
            Vector2[] uv0 = source.uv;
            Vector2[] uv2 = source.uv2;
            Color32[] colors = source.colors32;
            bool sourceHasNormals = normals != null &&
                                    normals.Length == positions.Length;
            bool sourceHasTangents = tangents != null &&
                                     tangents.Length == positions.Length;
            bool sourceHasUv0 = uv0 != null && uv0.Length == positions.Length;
            bool sourceHasUv2 = uv2 != null && uv2.Length == positions.Length;
            bool sourceHasColors = colors != null &&
                                   colors.Length == positions.Length;
            int originalSubMeshCount = Mathf.Max(1, source.subMeshCount);
            var builder = new MeshBuilder(
                originalSubMeshCount + 1,
                sourceHasNormals,
                sourceHasTangents,
                sourceHasUv0,
                sourceHasUv2,
                sourceHasColors);
            var intersections = new List<SliceVertex>(128);

            for (int subMesh = 0;
                 subMesh < originalSubMeshCount;
                 subMesh++)
            {
                if (source.GetTopology(subMesh) != MeshTopology.Triangles)
                    continue;
                int[] indices = source.GetTriangles(subMesh);
                for (int triangle = 0;
                     triangle + 2 < indices.Length;
                     triangle += 3)
                {
                    SliceVertex first = ReadVertex(
                        indices[triangle], positions, normals, tangents,
                        uv0, uv2, colors);
                    SliceVertex second = ReadVertex(
                        indices[triangle + 1], positions, normals, tangents,
                        uv0, uv2, colors);
                    SliceVertex third = ReadVertex(
                        indices[triangle + 2], positions, normals, tangents,
                        uv0, uv2, colors);
                    float firstDistance = worldPlane.GetDistanceToPoint(
                        sourceTransform.TransformPoint(first.position));
                    float secondDistance = worldPlane.GetDistanceToPoint(
                        sourceTransform.TransformPoint(second.position));
                    float thirdDistance = worldPlane.GetDistanceToPoint(
                        sourceTransform.TransformPoint(third.position));

                    CollectIntersection(
                        first, firstDistance,
                        second, secondDistance,
                        intersections);
                    CollectIntersection(
                        second, secondDistance,
                        third, thirdDistance,
                        intersections);
                    CollectIntersection(
                        third, thirdDistance,
                        first, firstDistance,
                        intersections);

                    var polygon = new List<SliceVertex>(4)
                    {
                        first,
                        second,
                        third
                    };
                    var distances = new List<float>(4)
                    {
                        firstDistance,
                        secondDistance,
                        thirdDistance
                    };
                    ClipPolygon(
                        polygon,
                        distances,
                        keepPositive,
                        out List<SliceVertex> clipped);
                    for (int vertex = 1;
                         vertex + 1 < clipped.Count;
                         vertex++)
                    {
                        builder.AddTriangle(
                            clipped[0],
                            clipped[vertex],
                            clipped[vertex + 1],
                            subMesh);
                    }
                }
            }

            if (intersections.Count >= 3)
            {
                hasCap = AddConvexCap(
                    builder,
                    originalSubMeshCount,
                    intersections,
                    sourceTransform,
                    worldPlane.normal,
                    keepPositive);
            }
            return builder.Build(
                source.name + (keepPositive ? "_SlicePositive" : "_SliceNegative"),
                originalSubMeshCount + (hasCap ? 1 : 0));
        }

        static SliceVertex ReadVertex(
            int index,
            Vector3[] positions,
            Vector3[] normals,
            Vector4[] tangents,
            Vector2[] uv0,
            Vector2[] uv2,
            Color32[] colors)
        {
            return new SliceVertex
            {
                position = positions[index],
                normal = normals != null && normals.Length == positions.Length
                    ? normals[index]
                    : Vector3.zero,
                tangent = tangents != null && tangents.Length == positions.Length
                    ? tangents[index]
                    : new Vector4(1f, 0f, 0f, 1f),
                uv0 = uv0 != null && uv0.Length == positions.Length
                    ? uv0[index]
                    : Vector2.zero,
                uv2 = uv2 != null && uv2.Length == positions.Length
                    ? uv2[index]
                    : Vector2.zero,
                color = colors != null && colors.Length == positions.Length
                    ? colors[index]
                    : new Color32(255, 255, 255, 255)
            };
        }

        static void CollectIntersection(
            SliceVertex first,
            float firstDistance,
            SliceVertex second,
            float secondDistance,
            List<SliceVertex> intersections)
        {
            bool firstPositive = firstDistance > DistanceEpsilon;
            bool firstNegative = firstDistance < -DistanceEpsilon;
            bool secondPositive = secondDistance > DistanceEpsilon;
            bool secondNegative = secondDistance < -DistanceEpsilon;
            if (!((firstPositive && secondNegative) ||
                  (firstNegative && secondPositive)))
            {
                return;
            }
            float denominator = firstDistance - secondDistance;
            if (Mathf.Abs(denominator) < 0.000001f)
                return;
            float amount = Mathf.Clamp01(firstDistance / denominator);
            intersections.Add(SliceVertex.Lerp(first, second, amount));
        }

        static void ClipPolygon(
            List<SliceVertex> input,
            List<float> inputDistances,
            bool keepPositive,
            out List<SliceVertex> output)
        {
            output = new List<SliceVertex>(4);
            if (input == null || input.Count == 0)
                return;
            for (int index = 0; index < input.Count; index++)
            {
                int next = (index + 1) % input.Count;
                SliceVertex currentVertex = input[index];
                SliceVertex nextVertex = input[next];
                float currentDistance = inputDistances[index];
                float nextDistance = inputDistances[next];
                bool currentInside = keepPositive
                    ? currentDistance >= -DistanceEpsilon
                    : currentDistance <= DistanceEpsilon;
                bool nextInside = keepPositive
                    ? nextDistance >= -DistanceEpsilon
                    : nextDistance <= DistanceEpsilon;
                if (currentInside)
                    output.Add(currentVertex);
                if (currentInside == nextInside)
                    continue;
                float denominator = currentDistance - nextDistance;
                if (Mathf.Abs(denominator) < 0.000001f)
                    continue;
                float amount = Mathf.Clamp01(currentDistance / denominator);
                output.Add(SliceVertex.Lerp(
                    currentVertex,
                    nextVertex,
                    amount));
            }
        }

        static bool AddConvexCap(
            MeshBuilder builder,
            int capSubMesh,
            List<SliceVertex> intersections,
            Transform sourceTransform,
            Vector3 worldNormal,
            bool keepPositive)
        {
            worldNormal.Normalize();
            Vector3 reference = Mathf.Abs(Vector3.Dot(worldNormal, Vector3.up)) > 0.92f
                ? Vector3.right
                : Vector3.up;
            Vector3 axisU = Vector3.Cross(reference, worldNormal).normalized;
            Vector3 axisV = Vector3.Cross(worldNormal, axisU).normalized;
            var points = new List<CapPoint>(intersections.Count);
            float mergeDistanceSquared =
                CapMergeDistance * CapMergeDistance;
            for (int index = 0; index < intersections.Count; index++)
            {
                SliceVertex vertex = intersections[index];
                Vector3 world = sourceTransform.TransformPoint(vertex.position);
                Vector2 projected = new Vector2(
                    Vector3.Dot(world, axisU),
                    Vector3.Dot(world, axisV));
                bool duplicate = false;
                for (int existing = 0; existing < points.Count; existing++)
                {
                    if ((points[existing].projected - projected).sqrMagnitude <=
                        mergeDistanceSquared)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate)
                {
                    points.Add(new CapPoint
                    {
                        vertex = vertex,
                        projected = projected
                    });
                }
            }
            if (points.Count < 3)
                return false;

            points.Sort((left, right) =>
            {
                int x = left.projected.x.CompareTo(right.projected.x);
                return x != 0
                    ? x
                    : left.projected.y.CompareTo(right.projected.y);
            });
            var hull = new List<CapPoint>(points.Count * 2);
            for (int index = 0; index < points.Count; index++)
            {
                while (hull.Count >= 2 && Cross(
                           hull[hull.Count - 2].projected,
                           hull[hull.Count - 1].projected,
                           points[index].projected) <= 0.000001f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(points[index]);
            }
            int lowerCount = hull.Count;
            for (int index = points.Count - 2; index >= 0; index--)
            {
                while (hull.Count > lowerCount && Cross(
                           hull[hull.Count - 2].projected,
                           hull[hull.Count - 1].projected,
                           points[index].projected) <= 0.000001f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(points[index]);
            }
            if (hull.Count > 1)
                hull.RemoveAt(hull.Count - 1);
            if (hull.Count < 3)
                return false;

            Vector3 capWorldNormal = keepPositive
                ? -worldNormal
                : worldNormal;
            Vector3 capLocalNormal = sourceTransform.localToWorldMatrix
                .transpose.MultiplyVector(capWorldNormal).normalized;
            Vector3 localTangentDirection = sourceTransform
                .InverseTransformDirection(axisU).normalized;
            Vector4 capTangent = new Vector4(
                localTangentDirection.x,
                localTangentDirection.y,
                localTangentDirection.z,
                1f);
            bool reverse = Vector3.Dot(capWorldNormal, worldNormal) < 0f;
            for (int index = 1; index + 1 < hull.Count; index++)
            {
                CapPoint first = hull[0];
                CapPoint second = reverse ? hull[index + 1] : hull[index];
                CapPoint third = reverse ? hull[index] : hull[index + 1];
                builder.AddTriangle(
                    CapVertex(first, capLocalNormal, capTangent),
                    CapVertex(second, capLocalNormal, capTangent),
                    CapVertex(third, capLocalNormal, capTangent),
                    capSubMesh);
            }
            return true;
        }

        static SliceVertex CapVertex(
            CapPoint point,
            Vector3 normal,
            Vector4 tangent)
        {
            SliceVertex vertex = point.vertex;
            vertex.normal = normal;
            vertex.tangent = tangent;
            vertex.uv0 = point.projected * 0.035f;
            vertex.uv2 = vertex.uv0;
            vertex.color = new Color32(255, 255, 255, 255);
            return vertex;
        }

        static float Cross(Vector2 origin, Vector2 first, Vector2 second)
        {
            Vector2 a = first - origin;
            Vector2 b = second - origin;
            return a.x * b.y - a.y * b.x;
        }

        static Material[] ResolveOutputMaterials(
            Material[] source,
            int sourceSubMeshCount,
            bool hasCap,
            Material capMaterial)
        {
            int outputCount = Mathf.Max(1, sourceSubMeshCount) +
                              (hasCap ? 1 : 0);
            var output = new Material[outputCount];
            Material exteriorFallback = ResolveFirstUsableMaterial(source) ??
                                        capMaterial;
            for (int index = 0;
                 index < Mathf.Max(1, sourceSubMeshCount);
                 index++)
            {
                if (source != null && source.Length > 0)
                    output[index] = source[Mathf.Min(index, source.Length - 1)];
                if (output[index] == null)
                    output[index] = exteriorFallback;
            }
            if (hasCap)
                output[output.Length - 1] = capMaterial ?? exteriorFallback;
            return output;
        }

        static Material ResolveFirstUsableMaterial(Material[] source)
        {
            if (source == null)
                return null;
            for (int index = 0; index < source.Length; index++)
                if (source[index] != null)
                    return source[index];
            return null;
        }

        static void CreateRendererCopy(
            Renderer source,
            Mesh mesh,
            Material[] materials,
            Transform parent,
            string objectName)
        {
            var visual = new GameObject(objectName);
            visual.layer = source.gameObject.layer;
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = parent.InverseTransformPoint(
                source.transform.position);
            visual.transform.localRotation = Quaternion.Inverse(parent.rotation) *
                                             source.transform.rotation;
            Vector3 parentScale = parent.lossyScale;
            Vector3 sourceScale = source.transform.lossyScale;
            visual.transform.localScale = new Vector3(
                SafeScale(sourceScale.x, parentScale.x),
                SafeScale(sourceScale.y, parentScale.y),
                SafeScale(sourceScale.z, parentScale.z));
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer target = visual.AddComponent<MeshRenderer>();
            target.shadowCastingMode = source.shadowCastingMode;
            target.receiveShadows = source.receiveShadows;
            target.lightProbeUsage = source.lightProbeUsage;
            target.reflectionProbeUsage = source.reflectionProbeUsage;
            target.probeAnchor = source.probeAnchor;
            target.motionVectorGenerationMode = source.motionVectorGenerationMode;
            target.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
            // A sliced half is runtime geometry and the upper half immediately
            // starts moving. Reusing the source building's baked-lightmap atlas
            // entry makes that moving facade sample lighting from its old world
            // position, washing the authored texture into a flat grey panel.
            // Keep the source probe settings, but detach every slice from static
            // baked/realtime lightmaps so both halves use dynamic probe lighting.
            target.lightmapIndex = -1;
            target.lightmapScaleOffset = new Vector4(1f, 1f, 0f, 0f);
            target.realtimeLightmapIndex = -1;
            target.realtimeLightmapScaleOffset = new Vector4(1f, 1f, 0f, 0f);
            target.sortingLayerID = source.sortingLayerID;
            target.sortingOrder = source.sortingOrder;
            target.sharedMaterials = materials;
            CopyRendererPropertyBlocks(source, target, materials.Length);
            target.enabled = true;
            visual.AddComponent<UrbanSliceVisual>();
        }

        static void CopyRendererPropertyBlocks(
            Renderer source,
            Renderer target,
            int materialCount)
        {
            if (source == null || target == null || !source.HasPropertyBlock())
                return;

            // Dark City art may carry per-instance tint/emission/texture data in
            // a MaterialPropertyBlock.  Reusing sharedMaterials alone therefore
            // produces a correctly referenced material that still looks blank
            // after the source renderer is replaced by a sliced renderer.
            var properties = new MaterialPropertyBlock();
            source.GetPropertyBlock(properties);
            if (!properties.isEmpty)
                target.SetPropertyBlock(properties);

            int sourceSlotCount = source.sharedMaterials != null
                ? source.sharedMaterials.Length
                : 0;
            int slots = Mathf.Min(materialCount, sourceSlotCount);
            for (int index = 0; index < slots; index++)
            {
                properties.Clear();
                source.GetPropertyBlock(properties, index);
                if (!properties.isEmpty)
                    target.SetPropertyBlock(properties, index);
            }
        }

        static float SafeScale(float source, float parent)
        {
            return Mathf.Abs(parent) > 0.00001f ? source / parent : source;
        }

        static bool IsSecondaryLodRenderer(Renderer renderer)
        {
            if (renderer == null)
                return false;
            LODGroup group = renderer.GetComponentInParent<LODGroup>();
            if (group == null)
                return false;
            LOD[] lods = group.GetLODs();
            for (int lod = 1; lod < lods.Length; lod++)
            {
                Renderer[] renderers = lods[lod].renderers;
                for (int index = 0; index < renderers.Length; index++)
                    if (renderers[index] == renderer)
                        return true;
            }
            return false;
        }

        public static bool TryGetLocalRendererBounds(
            Transform root,
            out Bounds localBounds)
        {
            localBounds = new Bounds(Vector3.zero, Vector3.zero);
            if (root == null)
                return false;
            UrbanSliceVisual[] visuals =
                root.GetComponentsInChildren<UrbanSliceVisual>(true);
            bool initialized = false;
            for (int index = 0; index < visuals.Length; index++)
            {
                UrbanSliceVisual marker = visuals[index];
                if (marker == null || !marker.gameObject.activeInHierarchy)
                    continue;
                Renderer renderer = marker.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled)
                    continue;
                Bounds bounds = renderer.bounds;
                Vector3 min = bounds.min;
                Vector3 max = bounds.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 world = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    Vector3 local = root.InverseTransformPoint(world);
                    if (!initialized)
                    {
                        localBounds = new Bounds(local, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(local);
                    }
                }
            }
            return initialized;
        }
    }
}
