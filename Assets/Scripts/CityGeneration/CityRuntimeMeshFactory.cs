using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGeneration
{
    public static class CityRuntimeMeshFactory
    {
        public static Material CreateMaterial(string name, Color color, float smoothness)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Diffuse");

            var material = new Material(shader)
            {
                name = name,
                color = color
            };
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", smoothness);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            material.enableInstancing = true;
            return material;
        }

        public static Material CreateTransparentMaterial(
            string name,
            Color color,
            float smoothness)
        {
            Material material = CreateMaterial(name, color, smoothness);
            if (!material.HasProperty("_Mode"))
                return material;

            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt(
                "_DstBlend",
                (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }

        public static Mesh CreateGroundMesh(float size)
        {
            float half = size * 0.5f;
            return CreateFlatMesh(
                new[]
                {
                    new Vector2(-half, -half),
                    new Vector2(half, -half),
                    new Vector2(half, half),
                    new Vector2(-half, half)
                },
                0f,
                "City Ground Mesh");
        }

        public static GameObject CreateFlatObject(
            string name,
            IReadOnlyList<Vector2> footprint,
            float elevation,
            Material material,
            Transform parent)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateFlatMesh(footprint, elevation, name + " Mesh");
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return gameObject;
        }

        public static GameObject CreateExtrudedObject(
            string name,
            IReadOnlyList<Vector2> footprint,
            float baseHeight,
            float height,
            Material material,
            Transform parent)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateExtrudedMesh(
                footprint,
                baseHeight,
                height,
                name + " Mesh");
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return gameObject;
        }

        public static GameObject CreateElevatedCityPlatform(
            string name,
            IReadOnlyList<Vector2> footprint,
            float topHeight,
            float slabThickness,
            float supportSpacing,
            float supportWidth,
            float minimumSupportHeight,
            ICityTerrainSampler terrain,
            Material material,
            Transform parent,
            out int supportCount,
            out float maximumSupportHeight)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateElevatedCityPlatformMesh(
                footprint,
                topHeight,
                slabThickness,
                supportSpacing,
                supportWidth,
                minimumSupportHeight,
                terrain,
                name + " Mesh",
                out supportCount,
                out maximumSupportHeight);
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            var collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            return gameObject;
        }

        public static GameObject CreateElevatedCityPlatforms(
            string name,
            IReadOnlyList<List<Vector2>> footprints,
            float topHeight,
            float slabThickness,
            float supportSpacing,
            float supportWidth,
            float minimumSupportHeight,
            ICityTerrainSampler terrain,
            Material material,
            Transform parent,
            out int supportCount,
            out float maximumSupportHeight)
        {
            if (footprints == null || footprints.Count == 0)
                throw new ArgumentException(
                    "At least one platform footprint is required.",
                    nameof(footprints));

            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateMergedElevatedCityPlatformMesh(
                footprints,
                topHeight,
                slabThickness,
                supportSpacing,
                supportWidth,
                minimumSupportHeight,
                terrain,
                name + " Mesh",
                out supportCount,
                out maximumSupportHeight);
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            var collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            return gameObject;
        }

        public static GameObject CreatePrefabBuildingOnPlane(
            string name,
            GameObject prefab,
            IReadOnlyList<Vector2> footprint,
            float platformTopHeight,
            Material fallbackMaterial,
            bool overrideMaterials,
            Transform parent)
        {
            const float modelSurfaceClearance = 0.03f;

            var container = new GameObject(name);
            container.transform.SetParent(parent, false);
            GameObject instance = UnityEngine.Object.Instantiate(
                prefab,
                container.transform);
            instance.name = prefab.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            Vector2 center = Average(footprint);
            Vector2 primaryAxis = FindLongestEdgeDirection(footprint);
            Vector2 secondaryAxis =
                new Vector2(-primaryAxis.y, primaryAxis.x);
            ProjectSize(
                footprint,
                primaryAxis,
                secondaryAxis,
                out float targetWidth,
                out float targetDepth);
            targetWidth = Mathf.Max(2f, targetWidth * 0.88f);
            targetDepth = Mathf.Max(2f, targetDepth * 0.88f);

            float yaw = -Mathf.Atan2(primaryAxis.y, primaryAxis.x)
                * Mathf.Rad2Deg;
            instance.transform.localRotation =
                Quaternion.Euler(0f, yaw, 0f);
            Bounds sourceBounds = CalculateRendererBounds(instance);
            float scale = Mathf.Min(
                targetWidth / Mathf.Max(0.01f, sourceBounds.size.x),
                targetDepth / Mathf.Max(0.01f, sourceBounds.size.z));
            scale = Mathf.Clamp(scale, 0.0001f, 100f);
            instance.transform.localScale = Vector3.one * scale;

            Bounds fittedBounds = CalculateRendererBounds(instance);
            Vector3 targetBase = parent != null
                ? parent.TransformPoint(new Vector3(
                    center.x,
                    platformTopHeight + modelSurfaceClearance,
                    center.y))
                : new Vector3(
                    center.x,
                    platformTopHeight + modelSurfaceClearance,
                    center.y);
            instance.transform.position += new Vector3(
                targetBase.x - fittedBounds.center.x,
                targetBase.y - fittedBounds.min.y,
                targetBase.z - fittedBounds.center.z);

            if (fallbackMaterial != null)
            {
                Renderer[] renderers =
                    instance.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Material[] materials = renderers[i].sharedMaterials;
                    for (int materialIndex = 0;
                         materialIndex < materials.Length;
                         materialIndex++)
                    {
                        if (overrideMaterials
                            || materials[materialIndex] == null)
                        {
                            materials[materialIndex] = fallbackMaterial;
                        }
                    }
                    renderers[i].sharedMaterials = materials;
                }
            }

            return container;
        }

        public static GameObject CreateTerrainRoadObject(
            string name,
            CityRoadSegment road,
            Func<Vector2, float> sampleHeight,
            float elevationOffset,
            float sampleSpacing,
            Material material,
            Transform parent)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateTerrainRoadMesh(
                road,
                sampleHeight,
                elevationOffset,
                sampleSpacing,
                name + " Mesh");
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return gameObject;
        }

        public static GameObject CreateAdaptiveTerrainRoadObject(
            string name,
            CityRoadSegment road,
            ICityTerrainSampler terrain,
            float elevationOffset,
            float sampleSpacing,
            Material material,
            Transform parent,
            out int supportPileCount)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateAdaptiveTerrainRoadMesh(
                road,
                terrain,
                elevationOffset,
                sampleSpacing,
                name + " Mesh",
                out supportPileCount);
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return gameObject;
        }

        public static GameObject CreateTerrainFlatObject(
            string name,
            IReadOnlyList<Vector2> footprint,
            Func<Vector2, float> sampleHeight,
            float elevationOffset,
            Material material,
            Transform parent)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateSampledFlatMesh(
                footprint,
                sampleHeight,
                elevationOffset,
                name + " Mesh");
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return gameObject;
        }

        public static GameObject CreateTerrainBuildingObject(
            string name,
            IReadOnlyList<Vector2> footprint,
            Func<Vector2, float> sampleHeight,
            float height,
            Material material,
            Transform parent)
        {
            float minimum = float.MaxValue;
            float maximum = float.MinValue;
            Vector2 center = Vector2.zero;
            for (int i = 0; i < footprint.Count; i++)
            {
                float sampled = sampleHeight(footprint[i]);
                minimum = Mathf.Min(minimum, sampled);
                maximum = Mathf.Max(maximum, sampled);
                center += footprint[i];
            }
            center /= footprint.Count;
            maximum = Mathf.Max(maximum, sampleHeight(center));

            float buriedBase = minimum - Mathf.Max(1.5f, maximum - minimum + 0.5f);
            float totalHeight = maximum + 0.08f + height - buriedBase;
            return CreateExtrudedObject(
                name,
                footprint,
                buriedBase,
                totalHeight,
                material,
                parent);
        }

        public static GameObject CreateTerrainPrefabBuildingObject(
            string name,
            GameObject prefab,
            IReadOnlyList<Vector2> footprint,
            Func<Vector2, float> sampleHeight,
            Material foundationMaterial,
            bool overrideMaterials,
            Transform parent)
        {
            const float foundationTopOffset = 0.08f;
            const float modelSurfaceClearance = 0.03f;

            Vector2 center = Vector2.zero;
            for (int i = 0; i < footprint.Count; i++)
                center += footprint[i];
            center /= Mathf.Max(1, footprint.Count);
            float padHeight = sampleHeight(center);

            GameObject container = CreateExtrudedObject(
                name,
                footprint,
                padHeight - 1.5f,
                1.5f + foundationTopOffset,
                foundationMaterial,
                parent);
            GameObject instance = UnityEngine.Object.Instantiate(
                prefab,
                container.transform);
            instance.name = prefab.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            Vector2 primaryAxis = FindLongestEdgeDirection(footprint);
            Vector2 secondaryAxis = new Vector2(-primaryAxis.y, primaryAxis.x);
            ProjectSize(
                footprint,
                primaryAxis,
                secondaryAxis,
                out float targetWidth,
                out float targetDepth);
            targetWidth = Mathf.Max(2f, targetWidth * 0.88f);
            targetDepth = Mathf.Max(2f, targetDepth * 0.88f);

            float yaw = -Mathf.Atan2(primaryAxis.y, primaryAxis.x)
                * Mathf.Rad2Deg;
            instance.transform.localRotation =
                Quaternion.Euler(0f, yaw, 0f);
            Bounds sourceBounds = CalculateRendererBounds(instance);
            float scale = Mathf.Min(
                targetWidth / Mathf.Max(0.01f, sourceBounds.size.x),
                targetDepth / Mathf.Max(0.01f, sourceBounds.size.z));
            scale = Mathf.Clamp(scale, 0.0001f, 100f);
            instance.transform.localScale = Vector3.one * scale;

            Bounds fittedBounds = CalculateRendererBounds(instance);
            Vector3 targetBase = parent != null
                ? parent.TransformPoint(new Vector3(
                    center.x,
                    padHeight
                        + foundationTopOffset
                        + modelSurfaceClearance,
                    center.y))
                : new Vector3(
                    center.x,
                    padHeight
                        + foundationTopOffset
                        + modelSurfaceClearance,
                    center.y);
            Vector3 correction = new Vector3(
                targetBase.x - fittedBounds.center.x,
                targetBase.y - fittedBounds.min.y,
                targetBase.z - fittedBounds.center.z);
            instance.transform.position += correction;

            if (foundationMaterial != null)
            {
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Material[] materials = renderers[i].sharedMaterials;
                    for (int materialIndex = 0;
                         materialIndex < materials.Length;
                         materialIndex++)
                    {
                        if (overrideMaterials || materials[materialIndex] == null)
                            materials[materialIndex] = foundationMaterial;
                    }
                    renderers[i].sharedMaterials = materials;
                }
            }

            return container;
        }

        public static GameObject CreateAdaptivePrefabBuildingObject(
            string name,
            GameObject prefab,
            IReadOnlyList<Vector2> footprint,
            ICityTerrainSampler terrain,
            Material foundationMaterial,
            bool overrideMaterials,
            Transform parent,
            out bool isSloped,
            out bool isWater,
            out int supportPileCount)
        {
            const float foundationThickness = 0.42f;
            const float modelSurfaceClearance = 0.03f;
            const float maximumTiltDegrees = 12f;

            Vector2 center = Average(footprint);
            CityTerrainSample centerSample = terrain.SampleTerrain(center);
            isWater = centerSample.IsWater;

            Vector3 averageNormal = Vector3.zero;
            float maximumWaterHeight = centerSample.WaterHeight;
            for (int i = 0; i < footprint.Count; i++)
            {
                CityTerrainSample sample = terrain.SampleTerrain(footprint[i]);
                averageNormal += sample.Normal;
                if (sample.IsWater)
                {
                    isWater = true;
                    maximumWaterHeight = Mathf.Max(
                        maximumWaterHeight,
                        sample.WaterHeight);
                }
            }
            averageNormal += centerSample.Normal * 2f;
            averageNormal = averageNormal.sqrMagnitude > 0.000001f
                ? averageNormal.normalized
                : Vector3.up;

            Vector3 platformNormal = isWater
                ? Vector3.up
                : ClampSurfaceNormal(averageNormal, maximumTiltDegrees);
            isSloped = !isWater
                && Vector3.Angle(Vector3.up, platformNormal) > 1f;
            float centerHeight = isWater
                ? maximumWaterHeight + GetWaterClearance(terrain)
                : centerSample.GroundHeight + 0.08f;
            Vector3 planeCenter = new Vector3(center.x, centerHeight, center.y);

            var container = new GameObject(name);
            container.transform.SetParent(parent, false);
            var filter = container.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateSupportedPlatformMesh(
                footprint,
                terrain,
                planeCenter,
                platformNormal,
                foundationThickness,
                isWater,
                name + " Foundation Mesh",
                out supportPileCount);
            var foundationRenderer = container.AddComponent<MeshRenderer>();
            foundationRenderer.sharedMaterial = foundationMaterial;
            foundationRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.On;
            foundationRenderer.receiveShadows = true;

            GameObject instance = UnityEngine.Object.Instantiate(
                prefab,
                container.transform);
            instance.name = prefab.name;
            instance.transform.localScale = Vector3.one;

            Vector2 primaryAxis = FindLongestEdgeDirection(footprint);
            Vector2 secondaryAxis = new Vector2(-primaryAxis.y, primaryAxis.x);
            ProjectSize(
                footprint,
                primaryAxis,
                secondaryAxis,
                out float targetWidth,
                out float targetDepth);
            targetWidth = Mathf.Max(2f, targetWidth * 0.88f);
            targetDepth = Mathf.Max(2f, targetDepth * 0.88f);

            float yaw = -Mathf.Atan2(primaryAxis.y, primaryAxis.x)
                * Mathf.Rad2Deg;
            // Measure the original horizontal footprint before tilt. Using the
            // world AABB of a tall, tilted tower makes its height contribute to
            // width and produces inconsistent scaling.
            instance.transform.rotation = Quaternion.identity;
            instance.transform.position = Vector3.zero;
            Bounds sourceBounds = CalculateRendererBounds(instance);
            float scale = Mathf.Min(
                targetWidth / Mathf.Max(0.01f, sourceBounds.size.x),
                targetDepth / Mathf.Max(0.01f, sourceBounds.size.z));
            scale = Mathf.Clamp(scale, 0.0001f, 100f);
            instance.transform.localScale = Vector3.one * scale;

            Quaternion tilt = Quaternion.FromToRotation(
                Vector3.up,
                platformNormal);
            instance.transform.rotation =
                tilt * Quaternion.Euler(0f, yaw, 0f);
            instance.transform.position =
                planeCenter + platformNormal * modelSurfaceClearance;

            if (foundationMaterial != null)
            {
                Renderer[] renderers =
                    instance.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Material[] materials = renderers[i].sharedMaterials;
                    for (int materialIndex = 0;
                         materialIndex < materials.Length;
                         materialIndex++)
                    {
                        if (overrideMaterials || materials[materialIndex] == null)
                            materials[materialIndex] = foundationMaterial;
                    }
                    renderers[i].sharedMaterials = materials;
                }
            }

            return container;
        }

        static Mesh CreateFlatMesh(
            IReadOnlyList<Vector2> footprint,
            float elevation,
            string meshName)
        {
            int count = footprint.Count;
            var vertices = new Vector3[count];
            for (int i = 0; i < count; i++)
                vertices[i] = new Vector3(footprint[i].x, elevation, footprint[i].y);

            if (!CityPolygonGeometry.TryTriangulate(
                    footprint,
                    out List<int> capTriangles))
            {
                throw new ArgumentException(
                    "Flat footprint could not be triangulated.",
                    nameof(footprint));
            }
            var triangles = new int[capTriangles.Count];
            for (int i = 0; i < capTriangles.Count; i += 3)
            {
                triangles[i] = capTriangles[i + 2];
                triangles[i + 1] = capTriangles[i + 1];
                triangles[i + 2] = capTriangles[i];
            }

            var mesh = new Mesh
            {
                name = meshName,
                vertices = vertices,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreateSampledFlatMesh(
            IReadOnlyList<Vector2> footprint,
            Func<Vector2, float> sampleHeight,
            float elevationOffset,
            string meshName)
        {
            int count = footprint.Count;
            var vertices = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Vector2 point = footprint[i];
                vertices[i] = new Vector3(
                    point.x,
                    sampleHeight(point) + elevationOffset,
                    point.y);
            }

            bool counterClockwise = CityPolygonGeometry.SignedArea(footprint) > 0f;
            var triangles = new int[(count - 2) * 3];
            for (int i = 0; i < count - 2; i++)
            {
                int triangle = i * 3;
                triangles[triangle] = 0;
                triangles[triangle + 1] = counterClockwise ? i + 2 : i + 1;
                triangles[triangle + 2] = counterClockwise ? i + 1 : i + 2;
            }

            var mesh = new Mesh
            {
                name = meshName,
                vertices = vertices,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreateTerrainRoadMesh(
            CityRoadSegment road,
            Func<Vector2, float> sampleHeight,
            float elevationOffset,
            float sampleSpacing,
            string meshName)
        {
            float length = Vector2.Distance(road.Start, road.End);
            int segmentCount = Mathf.Max(
                1,
                Mathf.CeilToInt(length / Mathf.Max(1f, sampleSpacing)));
            Vector2 direction = (road.End - road.Start).normalized;
            Vector2 normal =
                new Vector2(-direction.y, direction.x) * (road.Width * 0.5f);
            var vertices = new Vector3[(segmentCount + 1) * 2];
            var triangles = new int[segmentCount * 6];

            for (int i = 0; i <= segmentCount; i++)
            {
                float t = i / (float)segmentCount;
                Vector2 center = Vector2.Lerp(road.Start, road.End, t);
                Vector2 left = center + normal;
                Vector2 right = center - normal;
                vertices[i * 2] = new Vector3(
                    left.x,
                    sampleHeight(left) + elevationOffset,
                    left.y);
                vertices[i * 2 + 1] = new Vector3(
                    right.x,
                    sampleHeight(right) + elevationOffset,
                    right.y);
            }

            for (int i = 0; i < segmentCount; i++)
            {
                int vertex = i * 2;
                int triangle = i * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 2;
                triangles[triangle + 2] = vertex + 1;
                triangles[triangle + 3] = vertex + 1;
                triangles[triangle + 4] = vertex + 2;
                triangles[triangle + 5] = vertex + 3;
            }

            var mesh = new Mesh
            {
                name = meshName,
                vertices = vertices,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreateAdaptiveTerrainRoadMesh(
            CityRoadSegment road,
            ICityTerrainSampler terrain,
            float elevationOffset,
            float sampleSpacing,
            string meshName,
            out int supportPileCount)
        {
            float length = Vector2.Distance(road.Start, road.End);
            int segmentCount = Mathf.Max(
                1,
                Mathf.CeilToInt(length / Mathf.Max(1f, sampleSpacing)));
            Vector2 direction = (road.End - road.Start).normalized;
            Vector2 normal =
                new Vector2(-direction.y, direction.x) * (road.Width * 0.5f);
            var vertices = new List<Vector3>((segmentCount + 1) * 2 + 80);
            var triangles = new List<int>(segmentCount * 6 + 120);
            var deckCenters = new Vector3[segmentCount + 1];
            var samples = new CityTerrainSample[segmentCount + 1];

            for (int i = 0; i <= segmentCount; i++)
            {
                float t = i / (float)segmentCount;
                Vector2 center = Vector2.Lerp(road.Start, road.End, t);
                Vector2 left = center + normal;
                Vector2 right = center - normal;
                CityTerrainSample centerSample = terrain.SampleTerrain(center);
                samples[i] = centerSample;
                float deckHeight = centerSample.IsWater
                    ? centerSample.WaterHeight + GetWaterClearance(terrain)
                    : centerSample.GroundHeight + elevationOffset;
                deckCenters[i] = new Vector3(center.x, deckHeight, center.y);
                vertices.Add(new Vector3(left.x, deckHeight, left.y));
                vertices.Add(new Vector3(right.x, deckHeight, right.y));
            }

            float maximumStep = length / segmentCount * 0.1f;
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 1; i <= segmentCount; i++)
                {
                    float minimumHeight = deckCenters[i - 1].y - maximumStep;
                    if (deckCenters[i].y < minimumHeight)
                        deckCenters[i].y = minimumHeight;
                }
                for (int i = segmentCount - 1; i >= 0; i--)
                {
                    float minimumHeight = deckCenters[i + 1].y - maximumStep;
                    if (deckCenters[i].y < minimumHeight)
                        deckCenters[i].y = minimumHeight;
                }
            }
            for (int i = 0; i <= segmentCount; i++)
            {
                Vector3 left = vertices[i * 2];
                Vector3 right = vertices[i * 2 + 1];
                left.y = deckCenters[i].y;
                right.y = deckCenters[i].y;
                vertices[i * 2] = left;
                vertices[i * 2 + 1] = right;
            }

            for (int i = 0; i < segmentCount; i++)
            {
                int vertex = i * 2;
                triangles.Add(vertex);
                triangles.Add(vertex + 2);
                triangles.Add(vertex + 1);
                triangles.Add(vertex + 1);
                triangles.Add(vertex + 2);
                triangles.Add(vertex + 3);
            }

            supportPileCount = 0;
            float nextPileDistance = 0f;
            for (int i = 0; i <= segmentCount; i++)
            {
                float along = length * i / segmentCount;
                bool needsSupport = samples[i].IsWater
                    || deckCenters[i].y - samples[i].GroundHeight > 0.65f;
                if (!needsSupport || along + 0.01f < nextPileDistance)
                    continue;
                AddBoxColumn(
                    vertices,
                    triangles,
                    new Vector2(deckCenters[i].x, deckCenters[i].z),
                    samples[i].GroundHeight - 0.2f,
                    deckCenters[i].y - 0.06f,
                    Mathf.Clamp(road.Width * 0.12f, 0.35f, 0.65f));
                supportPileCount++;
                nextPileDistance = along + 7f;
            }

            var mesh = new Mesh { name = meshName };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreateMergedElevatedCityPlatformMesh(
            IReadOnlyList<List<Vector2>> footprints,
            float topHeight,
            float slabThickness,
            float supportSpacing,
            float supportWidth,
            float minimumSupportHeight,
            ICityTerrainSampler terrain,
            string meshName,
            out int supportCount,
            out float maximumSupportHeight)
        {
            slabThickness = Mathf.Max(0.1f, slabThickness);
            supportSpacing = Mathf.Max(2f, supportSpacing);
            supportWidth = Mathf.Max(0.2f, supportWidth);
            minimumSupportHeight = Mathf.Max(0f, minimumSupportHeight);
            float underside = topHeight - slabThickness;
            var vertices = new List<Vector3>(1024);
            var triangles = new List<int>(2048);
            var edgeCounts = new Dictionary<string, int>();
            var edgeGeometry = new Dictionary<string, Vector2[]>();
            var supportPoints = new List<Vector2>();
            var supportKeys = new HashSet<Vector2Int>();

            for (int regionIndex = 0;
                 regionIndex < footprints.Count;
                 regionIndex++)
            {
                IReadOnlyList<Vector2> footprint = footprints[regionIndex];
                if (!CityPolygonGeometry.TryTriangulate(
                        footprint,
                        out List<int> capTriangles))
                {
                    throw new ArgumentException(
                        "A platform footprint could not be triangulated.",
                        nameof(footprints));
                }

                int count = footprint.Count;
                int vertexStart = vertices.Count;
                for (int i = 0; i < count; i++)
                {
                    vertices.Add(new Vector3(
                        footprint[i].x,
                        underside,
                        footprint[i].y));
                }
                for (int i = 0; i < count; i++)
                {
                    vertices.Add(new Vector3(
                        footprint[i].x,
                        topHeight,
                        footprint[i].y));
                }
                for (int i = 0; i < capTriangles.Count; i += 3)
                {
                    int a = vertexStart + capTriangles[i];
                    int b = vertexStart + capTriangles[i + 1];
                    int c = vertexStart + capTriangles[i + 2];
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(vertexStart + count + capTriangles[i + 2]);
                    triangles.Add(vertexStart + count + capTriangles[i + 1]);
                    triangles.Add(vertexStart + count + capTriangles[i]);
                }

                for (int i = 0; i < count; i++)
                {
                    Vector2 start = footprint[i];
                    Vector2 end = footprint[(i + 1) % count];
                    string edgeKey = MakeEdgeKey(start, end);
                    edgeCounts.TryGetValue(edgeKey, out int edgeCount);
                    edgeCounts[edgeKey] = edgeCount + 1;
                    if (!edgeGeometry.ContainsKey(edgeKey))
                        edgeGeometry.Add(edgeKey, new[] { start, end });

                    AddSupportPoint(start);
                    int intervals = Mathf.Max(
                        1,
                        Mathf.CeilToInt(
                            Vector2.Distance(start, end) / supportSpacing));
                    for (int sample = 1; sample < intervals; sample++)
                    {
                        AddSupportPoint(Vector2.Lerp(
                            start,
                            end,
                            sample / (float)intervals));
                    }
                }

                Vector2 minimum = footprint[0];
                Vector2 maximum = footprint[0];
                for (int pointIndex = 1;
                     pointIndex < footprint.Count;
                     pointIndex++)
                {
                    minimum = Vector2.Min(minimum, footprint[pointIndex]);
                    maximum = Vector2.Max(maximum, footprint[pointIndex]);
                }
                float firstX =
                    Mathf.Ceil(minimum.x / supportSpacing) * supportSpacing;
                float firstY =
                    Mathf.Ceil(minimum.y / supportSpacing) * supportSpacing;
                for (float y = firstY;
                     y <= maximum.y + 0.001f;
                     y += supportSpacing)
                {
                    for (float x = firstX;
                         x <= maximum.x + 0.001f;
                         x += supportSpacing)
                    {
                        AddSupportPoint(new Vector2(x, y));
                    }
                }
            }

            foreach (KeyValuePair<string, int> edge in edgeCounts)
            {
                if (edge.Value != 1)
                    continue;
                Vector2[] segment = edgeGeometry[edge.Key];
                AddSideWall(segment[0], segment[1]);
            }

            supportCount = 0;
            maximumSupportHeight = 0f;
            for (int i = 0; i < supportPoints.Count; i++)
            {
                Vector2 point = supportPoints[i];
                float ground = terrain == null
                    ? 0f
                    : terrain.SampleTerrain(point).GroundHeight;
                float supportHeight = underside - ground;
                if (supportHeight < minimumSupportHeight)
                    continue;
                AddBoxColumn(
                    vertices,
                    triangles,
                    point,
                    ground - 0.25f,
                    underside,
                    supportWidth);
                supportCount++;
                maximumSupportHeight = Mathf.Max(
                    maximumSupportHeight,
                    supportHeight);
            }

            var mesh = new Mesh { name = meshName };
            if (vertices.Count > ushort.MaxValue)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;

            void AddSupportPoint(Vector2 point)
            {
                bool inside = false;
                for (int i = 0; i < footprints.Count; i++)
                {
                    if (CityPolygonGeometry.ContainsPoint(
                            footprints[i],
                            point))
                    {
                        inside = true;
                        break;
                    }
                }
                if (!inside)
                    return;
                var key = new Vector2Int(
                    Mathf.RoundToInt(point.x * 100f),
                    Mathf.RoundToInt(point.y * 100f));
                if (supportKeys.Add(key))
                    supportPoints.Add(point);
            }

            void AddSideWall(Vector2 start, Vector2 end)
            {
                int vertex = vertices.Count;
                vertices.Add(new Vector3(start.x, underside, start.y));
                vertices.Add(new Vector3(end.x, underside, end.y));
                vertices.Add(new Vector3(end.x, topHeight, end.y));
                vertices.Add(new Vector3(start.x, topHeight, start.y));
                triangles.Add(vertex);
                triangles.Add(vertex + 2);
                triangles.Add(vertex + 1);
                triangles.Add(vertex);
                triangles.Add(vertex + 3);
                triangles.Add(vertex + 2);
            }

            string MakeEdgeKey(Vector2 first, Vector2 second)
            {
                int firstX = Mathf.RoundToInt(first.x * 4f);
                int firstY = Mathf.RoundToInt(first.y * 4f);
                int secondX = Mathf.RoundToInt(second.x * 4f);
                int secondY = Mathf.RoundToInt(second.y * 4f);
                bool swap = firstX > secondX
                    || firstX == secondX && firstY > secondY;
                return swap
                    ? $"{secondX}:{secondY}|{firstX}:{firstY}"
                    : $"{firstX}:{firstY}|{secondX}:{secondY}";
            }
        }

        static Mesh CreateElevatedCityPlatformMesh(
            IReadOnlyList<Vector2> footprint,
            float topHeight,
            float slabThickness,
            float supportSpacing,
            float supportWidth,
            float minimumSupportHeight,
            ICityTerrainSampler terrain,
            string meshName,
            out int supportCount,
            out float maximumSupportHeight)
        {
            if (!CityPolygonGeometry.TryTriangulate(
                    footprint,
                    out List<int> capTriangles))
            {
                throw new ArgumentException(
                    "Platform footprint could not be triangulated.",
                    nameof(footprint));
            }

            slabThickness = Mathf.Max(0.1f, slabThickness);
            supportSpacing = Mathf.Max(2f, supportSpacing);
            supportWidth = Mathf.Max(0.2f, supportWidth);
            minimumSupportHeight = Mathf.Max(0f, minimumSupportHeight);
            float underside = topHeight - slabThickness;
            int count = footprint.Count;
            var vertices = new List<Vector3>(count * 2 + 256);
            var triangles = new List<int>(
                capTriangles.Count * 2 + count * 6 + 512);

            for (int i = 0; i < count; i++)
            {
                vertices.Add(new Vector3(
                    footprint[i].x,
                    underside,
                    footprint[i].y));
            }
            for (int i = 0; i < count; i++)
            {
                vertices.Add(new Vector3(
                    footprint[i].x,
                    topHeight,
                    footprint[i].y));
            }

            for (int i = 0; i < capTriangles.Count; i += 3)
            {
                int a = capTriangles[i];
                int b = capTriangles[i + 1];
                int c = capTriangles[i + 2];
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(count + c);
                triangles.Add(count + b);
                triangles.Add(count + a);
            }
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                triangles.Add(i);
                triangles.Add(count + next);
                triangles.Add(next);
                triangles.Add(i);
                triangles.Add(count + i);
                triangles.Add(count + next);
            }

            var supportPoints = new List<Vector2>();
            var supportKeys = new HashSet<Vector2Int>();
            for (int i = 0; i < count; i++)
            {
                Vector2 start = footprint[i];
                Vector2 end = footprint[(i + 1) % count];
                AddSupportPoint(start);
                int edgeIntervals = Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        Vector2.Distance(start, end) / supportSpacing));
                for (int sample = 1; sample < edgeIntervals; sample++)
                {
                    AddSupportPoint(Vector2.Lerp(
                        start,
                        end,
                        sample / (float)edgeIntervals));
                }
            }

            float minimumX = float.MaxValue;
            float maximumX = float.MinValue;
            float minimumY = float.MaxValue;
            float maximumY = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                minimumX = Mathf.Min(minimumX, footprint[i].x);
                maximumX = Mathf.Max(maximumX, footprint[i].x);
                minimumY = Mathf.Min(minimumY, footprint[i].y);
                maximumY = Mathf.Max(maximumY, footprint[i].y);
            }
            float firstX =
                Mathf.Ceil(minimumX / supportSpacing) * supportSpacing;
            float firstY =
                Mathf.Ceil(minimumY / supportSpacing) * supportSpacing;
            for (float y = firstY;
                 y <= maximumY + 0.001f;
                 y += supportSpacing)
            {
                for (float x = firstX;
                     x <= maximumX + 0.001f;
                     x += supportSpacing)
                {
                    AddSupportPoint(new Vector2(x, y));
                }
            }

            supportCount = 0;
            maximumSupportHeight = 0f;
            for (int i = 0; i < supportPoints.Count; i++)
            {
                Vector2 point = supportPoints[i];
                float ground = terrain == null
                    ? 0f
                    : terrain.SampleTerrain(point).GroundHeight;
                float supportHeight = underside - ground;
                if (supportHeight < minimumSupportHeight)
                    continue;
                AddBoxColumn(
                    vertices,
                    triangles,
                    point,
                    ground - 0.25f,
                    underside,
                    supportWidth);
                supportCount++;
                maximumSupportHeight = Mathf.Max(
                    maximumSupportHeight,
                    supportHeight);
            }

            var mesh = new Mesh { name = meshName };
            if (vertices.Count > ushort.MaxValue)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;

            void AddSupportPoint(Vector2 point)
            {
                if (!CityPolygonGeometry.ContainsPoint(footprint, point))
                    return;
                var key = new Vector2Int(
                    Mathf.RoundToInt(point.x * 100f),
                    Mathf.RoundToInt(point.y * 100f));
                if (supportKeys.Add(key))
                    supportPoints.Add(point);
            }
        }

        static Mesh CreateSupportedPlatformMesh(
            IReadOnlyList<Vector2> footprint,
            ICityTerrainSampler terrain,
            Vector3 planeCenter,
            Vector3 planeNormal,
            float thickness,
            bool forcePiles,
            string meshName,
            out int supportPileCount)
        {
            int count = footprint.Count;
            var vertices = new List<Vector3>(count * 2 + 80);
            var triangles = new List<int>((count - 2) * 6 + count * 6 + 120);
            var topHeights = new float[count];

            for (int i = 0; i < count; i++)
            {
                Vector2 point = footprint[i];
                float top = PlaneHeight(planeCenter, planeNormal, point);
                topHeights[i] = top;
                vertices.Add(new Vector3(point.x, top - thickness, point.y));
            }
            for (int i = 0; i < count; i++)
            {
                Vector2 point = footprint[i];
                vertices.Add(new Vector3(point.x, topHeights[i], point.y));
            }

            bool counterClockwise =
                CityPolygonGeometry.SignedArea(footprint) > 0f;
            for (int i = 0; i < count - 2; i++)
            {
                AddTriangle(triangles, 0, i + 1, i + 2, !counterClockwise);
                AddTriangle(
                    triangles,
                    count,
                    count + i + 1,
                    count + i + 2,
                    counterClockwise);
            }
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                if (counterClockwise)
                {
                    AddTriangle(triangles, i, next, count + next, false);
                    AddTriangle(triangles, i, count + next, count + i, false);
                }
                else
                {
                    AddTriangle(triangles, i, count + next, next, false);
                    AddTriangle(triangles, i, count + i, count + next, false);
                }
            }

            supportPileCount = 0;
            var pilePoints = new List<Vector2>(footprint.Count + 1);
            for (int i = 0; i < footprint.Count; i++)
                pilePoints.Add(footprint[i]);
            pilePoints.Add(Average(footprint));

            for (int i = 0; i < pilePoints.Count; i++)
            {
                Vector2 point = pilePoints[i];
                CityTerrainSample sample = terrain.SampleTerrain(point);
                float top = PlaneHeight(planeCenter, planeNormal, point) - thickness;
                if (!forcePiles && top - sample.GroundHeight < 0.55f)
                    continue;
                AddBoxColumn(
                    vertices,
                    triangles,
                    point,
                    sample.GroundHeight - 0.25f,
                    top,
                    0.48f);
                supportPileCount++;
            }

            var mesh = new Mesh { name = meshName };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddBoxColumn(
            List<Vector3> vertices,
            List<int> triangles,
            Vector2 center,
            float bottom,
            float top,
            float width)
        {
            if (top <= bottom + 0.02f)
                return;
            float half = width * 0.5f;
            int start = vertices.Count;
            vertices.Add(new Vector3(center.x - half, bottom, center.y - half));
            vertices.Add(new Vector3(center.x + half, bottom, center.y - half));
            vertices.Add(new Vector3(center.x + half, bottom, center.y + half));
            vertices.Add(new Vector3(center.x - half, bottom, center.y + half));
            vertices.Add(new Vector3(center.x - half, top, center.y - half));
            vertices.Add(new Vector3(center.x + half, top, center.y - half));
            vertices.Add(new Vector3(center.x + half, top, center.y + half));
            vertices.Add(new Vector3(center.x - half, top, center.y + half));

            int[] faces =
            {
                0, 4, 1, 1, 4, 5,
                1, 5, 2, 2, 5, 6,
                2, 6, 3, 3, 6, 7,
                3, 7, 0, 0, 7, 4,
                4, 7, 5, 5, 7, 6,
                0, 1, 3, 1, 2, 3
            };
            for (int i = 0; i < faces.Length; i++)
                triangles.Add(start + faces[i]);
        }

        static float PlaneHeight(
            Vector3 planeCenter,
            Vector3 planeNormal,
            Vector2 point)
        {
            float normalY = Mathf.Max(0.2f, planeNormal.y);
            return planeCenter.y
                - (planeNormal.x * (point.x - planeCenter.x)
                   + planeNormal.z * (point.y - planeCenter.z))
                / normalY;
        }

        static Vector3 ClampSurfaceNormal(Vector3 normal, float maximumAngle)
        {
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal);
            return Quaternion.RotateTowards(
                Quaternion.identity,
                rotation,
                maximumAngle) * Vector3.up;
        }

        static float GetWaterClearance(ICityTerrainSampler terrain)
        {
            CityNoiseTerrain noiseTerrain = terrain as CityNoiseTerrain;
            return noiseTerrain != null
                ? noiseTerrain.WaterClearance
                : 0.8f;
        }

        static Mesh CreateExtrudedMesh(
            IReadOnlyList<Vector2> footprint,
            float baseHeight,
            float height,
            string meshName)
        {
            int count = footprint.Count;
            var vertices = new List<Vector3>(count * 2);
            for (int i = 0; i < count; i++)
                vertices.Add(new Vector3(footprint[i].x, baseHeight, footprint[i].y));
            for (int i = 0; i < count; i++)
                vertices.Add(new Vector3(footprint[i].x, baseHeight + height, footprint[i].y));

            if (!CityPolygonGeometry.TryTriangulate(
                    footprint,
                    out List<int> capTriangles))
            {
                throw new ArgumentException(
                    "Extruded footprint could not be triangulated.",
                    nameof(footprint));
            }
            var triangles = new List<int>(
                capTriangles.Count * 2 + count * 6);
            for (int i = 0; i < capTriangles.Count; i += 3)
            {
                int a = capTriangles[i];
                int b = capTriangles[i + 1];
                int c = capTriangles[i + 2];
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(count + c);
                triangles.Add(count + b);
                triangles.Add(count + a);
            }

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                triangles.Add(i);
                triangles.Add(count + next);
                triangles.Add(next);
                triangles.Add(i);
                triangles.Add(count + i);
                triangles.Add(count + next);
            }

            var mesh = new Mesh
            {
                name = meshName
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddTriangle(
            List<int> triangles,
            int a,
            int b,
            int c,
            bool reverse)
        {
            triangles.Add(a);
            triangles.Add(reverse ? c : b);
            triangles.Add(reverse ? b : c);
        }

        static Vector2 FindLongestEdgeDirection(
            IReadOnlyList<Vector2> footprint)
        {
            float longest = -1f;
            Vector2 direction = Vector2.right;
            for (int i = 0; i < footprint.Count; i++)
            {
                Vector2 edge =
                    footprint[(i + 1) % footprint.Count] - footprint[i];
                if (edge.sqrMagnitude <= longest)
                    continue;
                longest = edge.sqrMagnitude;
                direction = edge.normalized;
            }
            return direction;
        }

        static void ProjectSize(
            IReadOnlyList<Vector2> footprint,
            Vector2 primaryAxis,
            Vector2 secondaryAxis,
            out float width,
            out float depth)
        {
            float minimumPrimary = float.MaxValue;
            float maximumPrimary = float.MinValue;
            float minimumSecondary = float.MaxValue;
            float maximumSecondary = float.MinValue;
            for (int i = 0; i < footprint.Count; i++)
            {
                float primary = Vector2.Dot(footprint[i], primaryAxis);
                float secondary = Vector2.Dot(footprint[i], secondaryAxis);
                minimumPrimary = Mathf.Min(minimumPrimary, primary);
                maximumPrimary = Mathf.Max(maximumPrimary, primary);
                minimumSecondary = Mathf.Min(minimumSecondary, secondary);
                maximumSecondary = Mathf.Max(maximumSecondary, secondary);
            }

            width = maximumPrimary - minimumPrimary;
            depth = maximumSecondary - minimumSecondary;
        }

        static Vector2 Average(IReadOnlyList<Vector2> points)
        {
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < points.Count; i++)
                sum += points[i];
            return sum / Mathf.Max(1, points.Count);
        }

        static float CalculateRendererMinimumProjection(
            GameObject root,
            Vector3 axis)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return Vector3.Dot(root.transform.position, axis);

            float minimum = float.MaxValue;
            for (int i = 0; i < renderers.Length; i++)
            {
                Bounds bounds = renderers[i].bounds;
                Vector3 center = bounds.center;
                Vector3 extents = bounds.extents;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 corner = center + Vector3.Scale(
                                extents,
                                new Vector3(x, y, z));
                            minimum = Mathf.Min(
                                minimum,
                                Vector3.Dot(corner, axis));
                        }
                    }
                }
            }
            return minimum;
        }

        static Bounds CalculateRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.one);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
