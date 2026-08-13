using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityPlanet.CityPcg
{
    /// <summary>
    /// Builds only the non-interactive city seen beyond the combat boundary.
    /// It never adds collision, rigidbodies, navigation, mission anchors or
    /// combat semantics; AirCombatCityPlan remains the owner of playable space.
    /// </summary>
    static class UrbanCityVisualContinuityBuilder
    {
        const string RootName = "BackgroundCityContinuity_VisualOnly";
        const int GroundSegmentCount = 40;
        const float FarSkylineMaximumOutsideDistance =
            AirCombatCityPcgLab.VisualBackgroundMaximumOutsideDistance;
        const float FarSkylineMaximumBuildingHeight =
            AirCombatCityPcgLab.VisualBackgroundMaximumBuildingHeight;

        struct EdgeFrame
        {
            public Vector3 outward;
            public Vector3 tangent;
            public float inwardFacingYaw;
        }

        struct VisualCombineKey : System.IEquatable<VisualCombineKey>
        {
            public Material material;
            public int cellX;
            public int cellZ;

            public bool Equals(VisualCombineKey other)
            {
                return material == other.material &&
                       cellX == other.cellX &&
                       cellZ == other.cellZ;
            }

            public override bool Equals(object value)
            {
                return value is VisualCombineKey &&
                       Equals((VisualCombineKey)value);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = material != null
                        ? material.GetInstanceID()
                        : 0;
                    hash = hash * 397 ^ cellX;
                    hash = hash * 397 ^ cellZ;
                    return hash;
                }
            }
        }

        public static void Build(
            Transform parent,
            AirCombatCityPlan plan,
            AirCombatCitySettings settings,
            AirCombatCityPalette palette,
            NewGenUrbanBuildingCatalog buildingCatalog,
            DarkCity2UrbanCatalog darkCityCatalog)
        {
            if (parent == null || plan == null || settings == null)
                return;

            var rootObject = new GameObject(RootName);
            rootObject.transform.SetParent(parent, false);
            rootObject.isStatic = true;
            Transform root = rootObject.transform;

            Material paving = darkCityCatalog != null &&
                              darkCityCatalog.cityPaving != null
                ? darkCityCatalog.cityPaving
                : palette != null && palette.cityBlockPaving != null
                    ? palette.cityBlockPaving
                    : palette != null ? palette.road : null;
            Material asphalt = darkCityCatalog != null &&
                               darkCityCatalog.asphalt != null
                ? darkCityCatalog.asphalt
                : palette != null && palette.asphalt != null
                    ? palette.asphalt
                    : palette != null ? palette.road : null;
            float textureMetres = darkCityCatalog != null
                ? Mathf.Max(2f, darkCityCatalog.roadTextureMeters)
                : 8f;

            BuildVisualGround(
                root,
                settings,
                asphalt != null ? asphalt : paving,
                textureMetres);
            BuildPeripheralStreetFabric(
                root,
                settings,
                plan.resolvedSeed,
                asphalt,
                textureMetres);
            BuildCombatBoundaryHologram(
                root,
                plan,
                settings);
            BuildNearAndMidCity(
                root,
                settings,
                plan.resolvedSeed,
                buildingCatalog,
                darkCityCatalog,
                paving,
                textureMetres);
            BuildFarSkyline(
                root,
                settings,
                plan.resolvedSeed,
                buildingCatalog,
                darkCityCatalog,
                paving,
                textureMetres);

            // Collapse the many tiny visual-only renderers into spatial
            // material chunks. Chunking retains camera culling while avoiding
            // a background transform and draw-submission tax during combat.
            CombineVisualPresentation(root);

            // Future visual-prefab changes cannot silently introduce gameplay
            // components: neutralise them before activation/static batching.
            MakeHierarchyVisualOnly(rootObject);
        }

        static void BuildVisualGround(
            Transform parent,
            AirCombatCitySettings settings,
            Material material,
            float textureMetres)
        {
            if (material == null)
                return;

            float radius = Mathf.Max(
                settings.mapSize * 3.4f,
                settings.mapSize * 0.5f +
                settings.maximumAltitude * 8f + 1200f);
            var vertices = new Vector3[GroundSegmentCount + 1];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[GroundSegmentCount * 3];
            vertices[0] = new Vector3(0f, -0.055f, 0f);
            uv[0] = Vector2.zero;
            for (int index = 0; index < GroundSegmentCount; index++)
            {
                float angle = index / (float)GroundSegmentCount *
                              Mathf.PI * 2f;
                float modulation = 0.965f +
                                   Hash01(1949, index, 17, 53) * 0.07f;
                float x = Mathf.Cos(angle) * radius * modulation;
                float z = Mathf.Sin(angle) * radius * modulation;
                vertices[index + 1] = new Vector3(x, -0.055f, z);
                uv[index + 1] = new Vector2(
                    x / textureMetres,
                    z / textureMetres);

                int next = (index + 1) % GroundSegmentCount;
                int triangle = index * 3;
                triangles[triangle] = 0;
                triangles[triangle + 1] = next + 1;
                triangles[triangle + 2] = index + 1;
            }

            var mesh = new Mesh
            {
                name = "ContinuityGround_VisualOnly_Mesh",
                vertices = vertices,
                uv = uv,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var ground = new GameObject(
                "ContinuityGround_VisualOnly_NoCollision");
            ground.transform.SetParent(parent, false);
            ground.isStatic = true;
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = ground.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            ConfigureLowCostRenderer(renderer);
            ground.AddComponent<CityPcgGeneratedMeshOwner>().Configure(mesh);
        }

        static void BuildPeripheralStreetFabric(
            Transform parent,
            AirCombatCitySettings settings,
            int seed,
            Material asphalt,
            float textureMetres)
        {
            if (asphalt == null)
                return;

            Transform streetRoot = CreateRoot(
                parent,
                "PeripheralStreetFabric_VisualOnly_NoCollision");
            const float GridStep = 148f;
            float half = settings.mapSize * 0.5f;
            // Roads continue farther than the normal combat camera can see so
            // the city never reveals a second, artificial outer square.
            float outer = half + 3400f;
            int lineRadius = Mathf.CeilToInt(outer / GridStep);
            for (int line = -lineRadius; line <= lineRadius; line++)
            {
                float coordinate = (line + 0.5f) * GridStep + Mathf.Lerp(
                    -9f,
                    9f,
                    Hash01(seed, line, 0, 457));
                bool arterial = PositiveModulo(line, 4) == 0;
                float width = arterial ? 19f : 8f;

                if (Mathf.Abs(coordinate) > half - 16f)
                {
                    CreateSurfaceStrip(
                        streetRoot,
                        "PeripheralStreet_NS_" + line,
                        new Vector3(coordinate, 0f, -outer),
                        new Vector3(coordinate, 0f, outer),
                        width,
                        0.006f,
                        asphalt,
                        textureMetres);
                }
                else
                {
                    CreateSurfaceStrip(
                        streetRoot,
                        "PeripheralStreet_NS_South_" + line,
                        new Vector3(coordinate, 0f, -outer),
                        new Vector3(coordinate, 0f, -half + 12f),
                        width,
                        0.006f,
                        asphalt,
                        textureMetres);
                    CreateSurfaceStrip(
                        streetRoot,
                        "PeripheralStreet_NS_North_" + line,
                        new Vector3(coordinate, 0f, half - 12f),
                        new Vector3(coordinate, 0f, outer),
                        width,
                        0.006f,
                        asphalt,
                        textureMetres);
                }

                float horizontalCoordinate =
                    (line + 0.5f) * GridStep + Mathf.Lerp(
                        -9f,
                        9f,
                        Hash01(seed, line, 1, 461));
                if (Mathf.Abs(horizontalCoordinate) > half - 16f)
                {
                    CreateSurfaceStrip(
                        streetRoot,
                        "PeripheralStreet_EW_" + line,
                        new Vector3(-outer, 0f, horizontalCoordinate),
                        new Vector3(outer, 0f, horizontalCoordinate),
                        width,
                        0.006f,
                        asphalt,
                        textureMetres);
                }
                else
                {
                    CreateSurfaceStrip(
                        streetRoot,
                        "PeripheralStreet_EW_West_" + line,
                        new Vector3(-outer, 0f, horizontalCoordinate),
                        new Vector3(-half + 12f, 0f, horizontalCoordinate),
                        width,
                        0.006f,
                        asphalt,
                        textureMetres);
                    CreateSurfaceStrip(
                        streetRoot,
                        "PeripheralStreet_EW_East_" + line,
                        new Vector3(half - 12f, 0f, horizontalCoordinate),
                        new Vector3(outer, 0f, horizontalCoordinate),
                        width,
                        0.006f,
                        asphalt,
                        textureMetres);
                }
            }
        }

        static void BuildCombatBoundaryHologram(
            Transform parent,
            AirCombatCityPlan plan,
            AirCombatCitySettings settings)
        {
            if (parent == null || plan == null || settings == null ||
                plan.boundaryWalls == null || plan.boundaryWalls.Count == 0)
            {
                return;
            }

            Material hologram = Resources.Load<Material>(
                "CombatCityPCG/PCG_UrbanBoundaryHologram");
            if (hologram == null)
                return;

            Transform boundaryRoot = CreateRoot(
                parent,
                "ProximityNoFlyHologram_VisualOnly_NoCollision");
            float bottom = -8f;
            float top = settings.maximumAltitude + 24f;
            const float TileSize = 180f;
            for (int wallIndex = 0;
                 wallIndex < plan.boundaryWalls.Count;
                 wallIndex++)
            {
                AirCombatBoundaryWallPlan wall = plan.boundaryWalls[wallIndex];
                bool horizontal = wall.size.x >= wall.size.z;
                float span = horizontal ? wall.size.x : wall.size.z;
                Vector3 innerFace = wall.center;
                if (horizontal)
                {
                    float sign = Mathf.Sign(wall.center.z);
                    innerFace.z -= sign * (wall.size.z * 0.5f + 0.35f);
                }
                else
                {
                    float sign = Mathf.Sign(wall.center.x);
                    innerFace.x -= sign * (wall.size.x * 0.5f + 0.35f);
                }

                CreateHologramWall(
                    boundaryRoot,
                    "ProximityNoFlyWall_" + wall.stableId,
                    innerFace,
                    span,
                    bottom,
                    top,
                    TileSize,
                    horizontal,
                    wall.center.x,
                    wall.center.z,
                    hologram);
            }
        }

        static void CreateHologramWall(
            Transform parent,
            string objectName,
            Vector3 center,
            float span,
            float bottom,
            float top,
            float tileSize,
            bool horizontal,
            float sideX,
            float sideZ,
            Material material)
        {
            if (parent == null || material == null || span <= 0.1f ||
                top <= bottom)
            {
                return;
            }

            float half = span * 0.5f;
            float uMax = span / Mathf.Max(1f, tileSize);
            float vMax = (top - bottom) / Mathf.Max(1f, tileSize);
            Vector3[] vertices;
            Vector2[] uv;
            int[] triangles = { 0, 1, 2, 0, 2, 3 };
            if (horizontal && sideZ >= 0f)
            {
                vertices = new[]
                {
                    new Vector3(center.x - half, bottom, center.z),
                    new Vector3(center.x - half, top, center.z),
                    new Vector3(center.x + half, top, center.z),
                    new Vector3(center.x + half, bottom, center.z)
                };
                uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(0f, vMax),
                    new Vector2(uMax, vMax),
                    new Vector2(uMax, 0f)
                };
            }
            else if (horizontal)
            {
                vertices = new[]
                {
                    new Vector3(center.x - half, bottom, center.z),
                    new Vector3(center.x + half, bottom, center.z),
                    new Vector3(center.x + half, top, center.z),
                    new Vector3(center.x - half, top, center.z)
                };
                uv = new[]
                {
                    new Vector2(uMax, 0f),
                    new Vector2(0f, 0f),
                    new Vector2(0f, vMax),
                    new Vector2(uMax, vMax)
                };
            }
            else if (sideX >= 0f)
            {
                vertices = new[]
                {
                    new Vector3(center.x, bottom, center.z - half),
                    new Vector3(center.x, bottom, center.z + half),
                    new Vector3(center.x, top, center.z + half),
                    new Vector3(center.x, top, center.z - half)
                };
                uv = new[]
                {
                    new Vector2(uMax, 0f),
                    new Vector2(0f, 0f),
                    new Vector2(0f, vMax),
                    new Vector2(uMax, vMax)
                };
            }
            else
            {
                vertices = new[]
                {
                    new Vector3(center.x, bottom, center.z + half),
                    new Vector3(center.x, bottom, center.z - half),
                    new Vector3(center.x, top, center.z - half),
                    new Vector3(center.x, top, center.z + half)
                };
                uv = new[]
                {
                    new Vector2(uMax, 0f),
                    new Vector2(0f, 0f),
                    new Vector2(0f, vMax),
                    new Vector2(uMax, vMax)
                };
            }

            var mesh = new Mesh
            {
                name = objectName + "_Mesh",
                vertices = vertices,
                uv = uv,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var wall = new GameObject(objectName);
            wall.transform.SetParent(parent, false);
            wall.isStatic = true;
            wall.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = wall.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            ConfigureLowCostRenderer(renderer);
            wall.AddComponent<CityPcgGeneratedMeshOwner>().Configure(mesh);
        }

        static void CreateSurfaceStrip(
            Transform parent,
            string objectName,
            Vector3 start,
            Vector3 end,
            float width,
            float y,
            Material material,
            float textureMetres)
        {
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length <= 0.01f || material == null)
                return;
            Vector3 direction = delta / length;
            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
            float halfWidth = Mathf.Max(0.05f, width * 0.5f);
            Vector3 first = start + Vector3.up * y;
            Vector3 last = end + Vector3.up * y;
            var mesh = new Mesh
            {
                name = objectName + "_Mesh",
                vertices = new[]
                {
                    first - side * halfWidth,
                    first + side * halfWidth,
                    last + side * halfWidth,
                    last - side * halfWidth
                },
                uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(width / textureMetres, 0f),
                    new Vector2(width / textureMetres, length / textureMetres),
                    new Vector2(0f, length / textureMetres)
                },
                triangles = new[] { 0, 2, 1, 0, 3, 2 }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var surface = new GameObject(objectName);
            surface.transform.SetParent(parent, false);
            surface.isStatic = true;
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = surface.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            ConfigureLowCostRenderer(renderer);
            surface.AddComponent<CityPcgGeneratedMeshOwner>().Configure(mesh);
        }

        static void BuildNearAndMidCity(
            Transform parent,
            AirCombatCitySettings settings,
            int seed,
            NewGenUrbanBuildingCatalog buildingCatalog,
            DarkCity2UrbanCatalog darkCityCatalog,
            Material blockPaving,
            float textureMetres)
        {
            Transform massingRoot = CreateRoot(
                parent,
                "NearMidCityDistricts_LowPoly_VisualOnly");
            float half = settings.mapSize * 0.5f;
            const float GridStep = 148f;
            const float MinimumOutsideDistance = 14f;
            const float MaximumOutsideDistance = 1600f;
            float outerHalf = half + MaximumOutsideDistance;
            int gridRadius = Mathf.CeilToInt(outerHalf / GridStep);
            for (int gridX = -gridRadius; gridX <= gridRadius; gridX++)
            for (int gridZ = -gridRadius; gridZ <= gridRadius; gridZ++)
            {
                float x = gridX * GridStep + Mathf.Lerp(
                    -34f,
                    34f,
                    Hash01(seed, gridX, gridZ, 503));
                float z = gridZ * GridStep + Mathf.Lerp(
                    -34f,
                    34f,
                    Hash01(seed, gridZ, gridX, 509));
                Vector3 position = new Vector3(x, 0f, z);
                float outsideDistance = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) -
                                        half;
                if (outsideDistance < MinimumOutsideDistance ||
                    outsideDistance > MaximumOutsideDistance)
                {
                    continue;
                }

                float depth01 = Mathf.InverseLerp(
                    MinimumOutsideDistance,
                    MaximumOutsideDistance,
                    outsideDistance);
                int districtX = FloorDivide(gridX, 3);
                int districtZ = FloorDivide(gridZ, 3);
                float districtDensity = Hash01(
                    seed,
                    districtX,
                    districtZ,
                    517);
                float occupancy = Mathf.Lerp(0.96f, 0.68f, depth01) +
                                  Mathf.Lerp(-0.08f, 0.08f, districtDensity);
                if (Hash01(seed, gridX * 7, gridZ * 11, 521) > occupancy)
                    continue;

                int side = ResolveDominantSide(position);

                float width = Mathf.Lerp(
                    66f,
                    116f,
                    Hash01(seed, gridX, gridZ, 523));
                float buildingDepth = Mathf.Lerp(
                    62f,
                    112f,
                    Hash01(seed, gridZ, gridX, 541));
                // The real combat boundary contains tall cover towers.  The
                // visual city must taper away from those towers instead of
                // creating a second ring of increasingly tall needles.
                float minimumHeight = Mathf.Lerp(88f, 34f, depth01);
                float maximumHeight = Mathf.Lerp(332f, 138f, depth01);
                float height = Mathf.Lerp(
                    minimumHeight,
                    maximumHeight,
                    Hash01(seed, gridX * 13, gridZ * 17, 547));
                float towerChance = Mathf.Lerp(0.24f, 0.06f, depth01);
                if (Hash01(seed, gridX, gridZ, 551) < towerChance)
                {
                    height *= Mathf.Lerp(
                        1.18f,
                        1.42f,
                        Hash01(seed, gridX, gridZ, 553));
                }
                height = Mathf.Clamp(height, 38f, 430f);
                EdgeFrame frame = ResolveEdgeFrame(side);
                float yaw = frame.inwardFacingYaw + Mathf.Lerp(
                    -24f,
                    24f,
                    Hash01(seed, gridX * 19, gridZ * 23, 557));
                if (blockPaving != null)
                {
                    Vector3 padDirection =
                        Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    float padWidth = Mathf.Min(GridStep - 22f, width + 26f);
                    float padDepth = Mathf.Min(
                        GridStep - 22f,
                        buildingDepth + 26f);
                    CreateSurfaceStrip(
                        massingRoot,
                        "DistrictPad_X" + gridX + "_Z" + gridZ,
                        position - padDirection * (padDepth * 0.5f),
                        position + padDirection * (padDepth * 0.5f),
                        padWidth,
                        -0.012f,
                        blockPaving,
                        textureMetres);
                }
                PlaceVisualBuilding(
                    massingRoot,
                    buildingCatalog,
                    darkCityCatalog,
                    "ContinuityBlock_X" + gridX + "_Z" + gridZ,
                    position,
                    new Vector3(width, height, buildingDepth),
                    yaw,
                    seed + gridX * 1009 + gridZ * 137,
                    false);
            }
        }

        static void BuildFarSkyline(
            Transform parent,
            AirCombatCitySettings settings,
            int seed,
            NewGenUrbanBuildingCatalog buildingCatalog,
            DarkCity2UrbanCatalog darkCityCatalog,
            Material blockPaving,
            float textureMetres)
        {
            Transform skylineRoot = CreateRoot(
                parent,
                "FarSkyline_LowPoly_VisualOnly");
            float half = settings.mapSize * 0.5f;
            const float GridStep = 250f;
            const float MinimumOutsideDistance = 1420f;
            const float MaximumOutsideDistance =
                FarSkylineMaximumOutsideDistance;
            float outerHalf = half + MaximumOutsideDistance;
            int gridRadius = Mathf.CeilToInt(outerHalf / GridStep);
            for (int gridX = -gridRadius; gridX <= gridRadius; gridX++)
            for (int gridZ = -gridRadius; gridZ <= gridRadius; gridZ++)
            {
                float x = gridX * GridStep + Mathf.Lerp(
                    -86f,
                    86f,
                    Hash01(seed, gridX, gridZ, 563));
                float z = gridZ * GridStep + Mathf.Lerp(
                    -86f,
                    86f,
                    Hash01(seed, gridZ, gridX, 569));
                float outsideDistance = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) -
                                        half;
                if (outsideDistance < MinimumOutsideDistance ||
                    outsideDistance > MaximumOutsideDistance)
                {
                    continue;
                }
                float depth01 = Mathf.InverseLerp(
                    MinimumOutsideDistance,
                    MaximumOutsideDistance,
                    outsideDistance);
                int districtX = FloorDivide(gridX, 2);
                int districtZ = FloorDivide(gridZ, 2);
                float cluster = Hash01(
                    seed,
                    districtX,
                    districtZ,
                    567);
                float occupancy = Mathf.Lerp(0.44f, 0.17f, depth01) +
                                  Mathf.Lerp(-0.07f, 0.11f, cluster);
                if (Hash01(seed, gridX * 29, gridZ * 31, 571) > occupancy)
                    continue;

                float width = Mathf.Lerp(
                    84f,
                    172f,
                    Hash01(seed, gridX, gridZ, 577));
                float buildingDepth = Mathf.Lerp(
                    80f,
                    168f,
                    Hash01(seed, gridZ, gridX, 587));
                float height = Mathf.Lerp(
                    68f,
                    292f,
                    Hash01(seed, gridX * 37, gridZ * 41, 593));
                if (cluster > 0.78f &&
                    Hash01(seed, gridX, gridZ, 597) > 0.72f)
                {
                    height *= Mathf.Lerp(
                        1.20f,
                        1.48f,
                        Hash01(seed, gridZ, gridX, 598));
                }
                height = Mathf.Clamp(
                    height,
                    72f,
                    FarSkylineMaximumBuildingHeight);
                float yaw = Mathf.Round(
                    Hash01(seed, gridX, gridZ, 599) * 3f) * 90f +
                    Mathf.Lerp(
                        -14f,
                        14f,
                        Hash01(seed, gridZ, gridX, 601));
                if (blockPaving != null)
                {
                    Vector3 position = new Vector3(x, 0f, z);
                    Vector3 padDirection =
                        Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    float padWidth = Mathf.Min(GridStep - 28f, width + 34f);
                    float padDepth = Mathf.Min(
                        GridStep - 28f,
                        buildingDepth + 34f);
                    CreateSurfaceStrip(
                        skylineRoot,
                        "FarDistrictPad_X" + gridX + "_Z" + gridZ,
                        position - padDirection * (padDepth * 0.5f),
                        position + padDirection * (padDepth * 0.5f),
                        padWidth,
                        -0.012f,
                        blockPaving,
                        textureMetres);
                }
                PlaceVisualBuilding(
                    skylineRoot,
                    buildingCatalog,
                    darkCityCatalog,
                    "FarSkyline_X" + gridX + "_Z" + gridZ,
                    new Vector3(x, 0f, z),
                    new Vector3(width, height, buildingDepth),
                    yaw,
                    seed + gridX * 2003 + gridZ * 373,
                    false);
            }
        }

        static int ResolveDominantSide(Vector3 position)
        {
            if (Mathf.Abs(position.z) >= Mathf.Abs(position.x))
                return position.z >= 0f ? 0 : 1;
            return position.x >= 0f ? 2 : 3;
        }

        static void PlaceVisualBuilding(
            Transform parent,
            NewGenUrbanBuildingCatalog buildingCatalog,
            DarkCity2UrbanCatalog darkCityCatalog,
            string objectName,
            Vector3 position,
            Vector3 targetSize,
            float yaw,
            int stableVariant,
            bool preferDetailed)
        {
            AirCombatBuildingBand band = targetSize.y >= 250f
                ? AirCombatBuildingBand.High
                : targetSize.y >= 135f
                    ? AirCombatBuildingBand.Medium
                    : AirCombatBuildingBand.Low;
            GameObject prefab = preferDetailed && buildingCatalog != null
                ? buildingCatalog.Resolve(band, stableVariant)
                : null;
            if (prefab == null && darkCityCatalog != null)
                prefab = darkCityCatalog.ResolveBackground(stableVariant);
            if (prefab == null && buildingCatalog != null)
            {
                prefab = buildingCatalog.Resolve(band, stableVariant);
                if (prefab == null && band != AirCombatBuildingBand.Medium)
                {
                    prefab = buildingCatalog.Resolve(
                        AirCombatBuildingBand.Medium,
                        stableVariant);
                }
                if (prefab == null)
                {
                    prefab = buildingCatalog.Resolve(
                        AirCombatBuildingBand.Low,
                        stableVariant);
                }
            }
            if (prefab == null)
                return;

            Vector3 authoredSize = ResolveAuthoredSize(prefab);
            var instance = new GameObject(objectName);
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = new Vector3(
                position.x,
                0f,
                position.z);
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Material facade = ResolveFacadeMaterial(prefab);
            float podiumHeight = Mathf.Clamp(
                targetSize.y * 0.18f,
                18f,
                48f);

            if (targetSize.y <= 112f)
            {
                CreateVisualBox(
                    instance.transform,
                    "LowRiseMassing",
                    new Vector3(0f, targetSize.y * 0.5f, 0f),
                    targetSize,
                    facade);
                if (targetSize.y >= 62f)
                {
                    float roofHeight = Mathf.Clamp(
                        targetSize.y * 0.22f,
                        10f,
                        22f);
                    CreateVisualBox(
                        instance.transform,
                        "LowRiseRoofStep",
                        new Vector3(
                            targetSize.x * 0.12f,
                            targetSize.y + roofHeight * 0.5f,
                            -targetSize.z * 0.08f),
                        new Vector3(
                            targetSize.x * 0.54f,
                            roofHeight,
                            targetSize.z * 0.48f),
                        facade);
                }
                SetStaticRecursively(instance.transform);
                return;
            }

            if (targetSize.y < 215f)
            {
                float split = Hash01(stableVariant, 37, 41, 601);
                float firstWidth = targetSize.x * Mathf.Lerp(
                    0.50f,
                    0.62f,
                    split);
                float firstDepth = targetSize.z * Mathf.Lerp(
                    0.58f,
                    0.74f,
                    Hash01(stableVariant, 43, 47, 603));
                float firstHeight = targetSize.y * Mathf.Lerp(
                    0.82f,
                    1f,
                    Hash01(stableVariant, 53, 59, 604));
                CreateVisualBox(
                    instance.transform,
                    "MidRisePrimary",
                    new Vector3(
                        -targetSize.x * 0.18f,
                        firstHeight * 0.5f,
                        -targetSize.z * 0.08f),
                    new Vector3(firstWidth, firstHeight, firstDepth),
                    facade);

                float secondWidth = targetSize.x * Mathf.Lerp(
                    0.30f,
                    0.42f,
                    Hash01(stableVariant, 61, 67, 605));
                float secondDepth = targetSize.z * Mathf.Lerp(
                    0.42f,
                    0.62f,
                    Hash01(stableVariant, 71, 73, 606));
                float secondHeight = targetSize.y * Mathf.Lerp(
                    0.48f,
                    0.76f,
                    Hash01(stableVariant, 79, 83, 607));
                CreateVisualBox(
                    instance.transform,
                    "MidRiseSecondary",
                    new Vector3(
                        targetSize.x * 0.30f,
                        secondHeight * 0.5f,
                        targetSize.z * 0.14f),
                    new Vector3(secondWidth, secondHeight, secondDepth),
                    facade);

                if (Hash01(stableVariant, 89, 97, 608) > 0.46f)
                {
                    float thirdHeight = targetSize.y * Mathf.Lerp(
                        0.34f,
                        0.58f,
                        Hash01(stableVariant, 101, 103, 609));
                    CreateVisualBox(
                        instance.transform,
                        "MidRiseCourtyardWing",
                        new Vector3(
                            targetSize.x * 0.14f,
                            thirdHeight * 0.5f,
                            -targetSize.z * 0.30f),
                        new Vector3(
                            targetSize.x * 0.58f,
                            thirdHeight,
                            targetSize.z * 0.28f),
                        facade);
                }
                SetStaticRecursively(instance.transform);
                return;
            }

            CreateVisualBox(
                instance.transform,
                "Podium",
                new Vector3(0f, podiumHeight * 0.5f, 0f),
                new Vector3(targetSize.x, podiumHeight, targetSize.z),
                facade);

            float towerHeight = Mathf.Max(24f, targetSize.y - podiumHeight);
            float towerWidth = targetSize.x * Mathf.Lerp(
                0.54f,
                0.74f,
                Hash01(stableVariant, 3, 5, 607));
            float towerDepth = targetSize.z * Mathf.Lerp(
                0.52f,
                0.73f,
                Hash01(stableVariant, 7, 11, 613));
            float verticalScale = towerHeight /
                                  Mathf.Max(0.1f, authoredSize.y);
            float horizontalScaleX = Mathf.Clamp(
                towerWidth / Mathf.Max(0.1f, authoredSize.x),
                verticalScale * 0.72f,
                verticalScale * 1.58f);
            float horizontalScaleZ = Mathf.Clamp(
                towerDepth / Mathf.Max(0.1f, authoredSize.z),
                verticalScale * 0.72f,
                verticalScale * 1.58f);
            var tower = new GameObject("PreservedAspectTower");
            tower.transform.SetParent(instance.transform, false);
            tower.transform.localPosition = new Vector3(
                Mathf.Lerp(
                    -targetSize.x * 0.10f,
                    targetSize.x * 0.10f,
                    Hash01(stableVariant, 13, 17, 617)),
                podiumHeight,
                Mathf.Lerp(
                    -targetSize.z * 0.10f,
                    targetSize.z * 0.10f,
                    Hash01(stableVariant, 19, 23, 619)));
            tower.transform.localScale = new Vector3(
                horizontalScaleX,
                verticalScale,
                horizontalScaleZ);
            CopyMeshPresentationOnly(prefab.transform, tower.transform);

            if (targetSize.y >= 178f)
            {
                float annexHeight = Mathf.Clamp(
                    targetSize.y * 0.24f,
                    34f,
                    76f);
                float annexSign = Hash01(
                    stableVariant,
                    29,
                    31,
                    631) < 0.5f ? -1f : 1f;
                CreateVisualBox(
                    instance.transform,
                    "Annex",
                    new Vector3(
                        annexSign * targetSize.x * 0.31f,
                        annexHeight * 0.5f,
                        targetSize.z * 0.08f),
                    new Vector3(
                        targetSize.x * 0.38f,
                        annexHeight,
                        targetSize.z * 0.72f),
                    facade);
            }
            SetStaticRecursively(instance.transform);
        }

        static Material ResolveFacadeMaterial(GameObject prefab)
        {
            if (prefab == null)
                return null;
            MeshRenderer renderer =
                prefab.GetComponentInChildren<MeshRenderer>(true);
            if (renderer == null || renderer.sharedMaterials == null)
                return null;
            for (int index = 0;
                 index < renderer.sharedMaterials.Length;
                 index++)
            {
                if (renderer.sharedMaterials[index] != null)
                    return renderer.sharedMaterials[index];
            }
            return null;
        }

        static void CreateVisualBox(
            Transform parent,
            string objectName,
            Vector3 localPosition,
            Vector3 localSize,
            Material material)
        {
            if (parent == null || material == null)
                return;
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (cube == null)
                return;

            var box = new GameObject(objectName);
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = new Vector3(
                Mathf.Max(0.1f, localSize.x),
                Mathf.Max(0.1f, localSize.y),
                Mathf.Max(0.1f, localSize.z));
            box.isStatic = true;
            box.AddComponent<MeshFilter>().sharedMesh = cube;
            MeshRenderer renderer = box.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            ConfigureLowCostRenderer(renderer);
        }

        static void CopyMeshPresentationOnly(
            Transform sourceRoot,
            Transform destinationRoot)
        {
            MeshFilter[] sourceFilters =
                sourceRoot.GetComponentsInChildren<MeshFilter>(true);
            for (int index = 0; index < sourceFilters.Length; index++)
            {
                MeshFilter sourceFilter = sourceFilters[index];
                MeshRenderer sourceRenderer =
                    sourceFilter.GetComponent<MeshRenderer>();
                if (sourceFilter.sharedMesh == null || sourceRenderer == null)
                    continue;

                var visual = new GameObject(
                    "VisualMesh_" + index.ToString("D2") + "_" +
                    sourceFilter.name);
                visual.transform.SetParent(destinationRoot, false);
                Matrix4x4 relative = sourceRoot.worldToLocalMatrix *
                                     sourceFilter.transform.localToWorldMatrix;
                Vector4 position = relative.GetColumn(3);
                visual.transform.localPosition = new Vector3(
                    position.x,
                    position.y,
                    position.z);
                visual.transform.localRotation = relative.rotation;
                visual.transform.localScale = relative.lossyScale;
                visual.layer = destinationRoot.gameObject.layer;
                visual.isStatic = true;

                visual.AddComponent<MeshFilter>().sharedMesh =
                    sourceFilter.sharedMesh;
                MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = sourceRenderer.sharedMaterials;
                renderer.enabled = sourceRenderer.enabled;
                ConfigureLowCostRenderer(renderer);
            }
        }

        static Vector3 ResolveAuthoredSize(GameObject instance)
        {
            NormalizedBuildingModelInfo normalized =
                instance.GetComponent<NormalizedBuildingModelInfo>();
            if (normalized != null)
                return normalized.AuthoredSize;
            DarkCity2AssetDescriptor descriptor =
                instance.GetComponent<DarkCity2AssetDescriptor>();
            if (descriptor != null)
                return descriptor.AuthoredSize;

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return Vector3.one;
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            Vector3 localSize = instance.transform.InverseTransformVector(
                bounds.size);
            return new Vector3(
                Mathf.Abs(localSize.x),
                Mathf.Abs(localSize.y),
                Mathf.Abs(localSize.z));
        }

        static EdgeFrame ResolveEdgeFrame(int side)
        {
            switch (side)
            {
                case 0:
                    return new EdgeFrame
                    {
                        outward = Vector3.forward,
                        tangent = Vector3.right,
                        inwardFacingYaw = 180f
                    };
                case 1:
                    return new EdgeFrame
                    {
                        outward = Vector3.back,
                        tangent = Vector3.right,
                        inwardFacingYaw = 0f
                    };
                case 2:
                    return new EdgeFrame
                    {
                        outward = Vector3.right,
                        tangent = Vector3.forward,
                        inwardFacingYaw = -90f
                    };
                default:
                    return new EdgeFrame
                    {
                        outward = Vector3.left,
                        tangent = Vector3.forward,
                        inwardFacingYaw = 90f
                    };
            }
        }

        static Transform CreateRoot(Transform parent, string objectName)
        {
            var gameObject = new GameObject(objectName);
            gameObject.transform.SetParent(parent, false);
            gameObject.isStatic = true;
            return gameObject.transform;
        }

        static void CombineVisualPresentation(Transform root)
        {
            if (root == null)
                return;

            MeshRenderer[] sourceRenderers =
                root.GetComponentsInChildren<MeshRenderer>(true);
            for (int index = 0; index < sourceRenderers.Length; index++)
            {
                MeshFilter filter =
                    sourceRenderers[index].GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null &&
                    !filter.sharedMesh.isReadable)
                {
                    // Future imported assets may disable CPU read access. In
                    // that case retain the safe uncombined presentation rather
                    // than making part of the skyline disappear.
                    return;
                }
            }

            var sourceRoots = new List<GameObject>(root.childCount);
            for (int index = 0; index < root.childCount; index++)
                sourceRoots.Add(root.GetChild(index).gameObject);

            const float ChunkSize = 720f;
            var groups = new Dictionary<
                VisualCombineKey,
                List<CombineInstance>>(128);
            Matrix4x4 worldToRoot = root.worldToLocalMatrix;
            for (int index = 0; index < sourceRenderers.Length; index++)
            {
                MeshRenderer sourceRenderer = sourceRenderers[index];
                if (sourceRenderer == null || !sourceRenderer.enabled)
                    continue;
                MeshFilter filter = sourceRenderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                    continue;
                Material[] materials = sourceRenderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                    continue;

                Vector3 center = root.InverseTransformPoint(
                    sourceRenderer.bounds.center);
                int cellX = Mathf.FloorToInt(center.x / ChunkSize);
                int cellZ = Mathf.FloorToInt(center.z / ChunkSize);
                int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
                for (int subMesh = 0;
                     subMesh < subMeshCount;
                     subMesh++)
                {
                    Material material = materials[
                        Mathf.Min(subMesh, materials.Length - 1)];
                    if (material == null)
                        continue;
                    var key = new VisualCombineKey
                    {
                        material = material,
                        cellX = cellX,
                        cellZ = cellZ
                    };
                    if (!groups.TryGetValue(
                            key,
                            out List<CombineInstance> instances))
                    {
                        instances = new List<CombineInstance>(32);
                        groups.Add(key, instances);
                    }
                    instances.Add(new CombineInstance
                    {
                        mesh = mesh,
                        subMeshIndex = Mathf.Min(
                            subMesh,
                            mesh.subMeshCount - 1),
                        transform = worldToRoot *
                                    sourceRenderer.transform.localToWorldMatrix
                    });
                }
            }

            Transform combinedRoot = CreateRoot(
                root,
                "CombinedBackgroundChunks_VisualOnly");
            combinedRoot.gameObject.SetActive(false);
            int groupIndex = 0;
            try
            {
                foreach (KeyValuePair<
                             VisualCombineKey,
                             List<CombineInstance>> pair in groups)
                {
                    if (pair.Value.Count == 0)
                        continue;
                    var mesh = new Mesh
                    {
                        name = "BackgroundChunk_" +
                               groupIndex.ToString("D3") + "_Mesh",
                        indexFormat = IndexFormat.UInt32
                    };
                    mesh.CombineMeshes(
                        pair.Value.ToArray(),
                        true,
                        true,
                        false);
                    mesh.RecalculateBounds();

                    var chunk = new GameObject(
                        "BackgroundChunk_" +
                        groupIndex.ToString("D3"));
                    chunk.transform.SetParent(combinedRoot, false);
                    chunk.layer = root.gameObject.layer;
                    chunk.isStatic = true;
                    chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
                    MeshRenderer renderer =
                        chunk.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = pair.Key.material;
                    ConfigureLowCostRenderer(renderer);
                    chunk.AddComponent<CityPcgGeneratedMeshOwner>()
                        .Configure(mesh);
                    groupIndex++;
                }
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning(
                    "Background city mesh combination was skipped: " +
                    exception.Message);
                DestroyGameObject(combinedRoot.gameObject);
                return;
            }

            combinedRoot.gameObject.SetActive(true);
            for (int index = 0; index < sourceRoots.Count; index++)
            {
                if (sourceRoots[index] == null)
                    continue;
                sourceRoots[index].SetActive(false);
                DestroyGameObject(sourceRoots[index]);
            }
        }

        static void MakeHierarchyVisualOnly(GameObject root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
                DestroyComponent(colliders[index]);
            }

            Joint[] joints = root.GetComponentsInChildren<Joint>(true);
            for (int index = 0; index < joints.Length; index++)
                DestroyComponent(joints[index]);

            Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>(true);
            for (int index = 0; index < bodies.Length; index++)
            {
                bodies[index].detectCollisions = false;
                bodies[index].isKinematic = true;
                DestroyComponent(bodies[index]);
            }

            Behaviour[] behaviours = root.GetComponentsInChildren<Behaviour>(true);
            for (int index = 0; index < behaviours.Length; index++)
                behaviours[index].enabled = false;

            ParticleSystem[] particles =
                root.GetComponentsInChildren<ParticleSystem>(true);
            for (int index = 0; index < particles.Length; index++)
            {
                particles[index].Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
                ConfigureLowCostRenderer(renderers[index]);
            SetStaticRecursively(root.transform);
        }

        static void ConfigureLowCostRenderer(Renderer renderer)
        {
            if (renderer == null)
                return;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
        }

        static void SetStaticRecursively(Transform root)
        {
            if (root == null)
                return;
            root.gameObject.isStatic = true;
            for (int index = 0; index < root.childCount; index++)
                SetStaticRecursively(root.GetChild(index));
        }

        static void DestroyComponent(Component component)
        {
            if (component == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(component);
            else
                Object.DestroyImmediate(component);
        }

        static void DestroyGameObject(GameObject gameObject)
        {
            if (gameObject == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(gameObject);
            else
                Object.DestroyImmediate(gameObject);
        }

        static int StableStringHash(string value)
        {
            unchecked
            {
                int hash = 23;
                if (value == null)
                    return hash;
                for (int index = 0; index < value.Length; index++)
                    hash = hash * 31 + value[index];
                return hash;
            }
        }

        static int PositiveModulo(int value, int modulo)
        {
            if (modulo <= 0)
                return 0;
            int result = value % modulo;
            return result < 0 ? result + modulo : result;
        }

        static int FloorDivide(int value, int divisor)
        {
            if (divisor <= 0)
                return 0;
            if (value >= 0)
                return value / divisor;
            return -((-value + divisor - 1) / divisor);
        }

        static float Hash01(int seed, int first, int second, int salt)
        {
            unchecked
            {
                uint value = (uint)seed;
                value ^= (uint)first * 0x9E3779B9u;
                value ^= (uint)second * 0x85EBCA6Bu;
                value ^= (uint)salt * 0xC2B2AE35u;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return (value & 0x00FFFFFFu) / 16777215f;
            }
        }
    }

}
