using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CityGeneration
{
    /// <summary>
    /// Pure-data planning pipeline used by the citygenerate prototype.
    /// UnityEngine.Object and mesh creation intentionally stay outside this
    /// class so preview generation can run on a worker thread.
    /// </summary>
    public sealed class CityPlanningPipeline : ICityPlanningPipeline
    {
        const int CandidateCount = 6;
        const int MaximumCandidateBatches = 3;
        const int MinimumCandidatesBeforeEarlyStop = 3;
        const float PreferredAutomaticScore = 85f;
        const float RoadGridSize = 10f;
        const float SidewalkWidth = 2.5f;

        readonly CityGenerationSettings settings;
        readonly int buildingCatalogCount;

        public CityPlanningPipeline(
            CityGenerationSettings valueSettings,
            int valueBuildingCatalogCount)
        {
            settings = valueSettings?.ValidatedCopy()
                ?? new CityGenerationSettings();
            buildingCatalogCount = Mathf.Max(
                1,
                valueBuildingCatalogCount);
        }

        public Task<CityPreviewResult> GenerateAutomaticPreviewAsync(
            CityPlanData boundaryOnlyPlan,
            CancellationToken cancellationToken)
        {
            return Task.Run(
                () => GenerateAutomaticPreview(
                    boundaryOnlyPlan,
                    cancellationToken),
                cancellationToken);
        }

        public Task<CityPreviewResult> GeneratePreviewAsync(
            CityPlanData plan,
            CityDirtyRegionSet dirtyRegions,
            CancellationToken cancellationToken)
        {
            return GenerateAutomaticPreviewAsync(
                plan,
                cancellationToken);
        }

        public Task<CityPreviewResult[]> GenerateCandidatesAsync(
            CityPlanData plan,
            CancellationToken cancellationToken)
        {
            return Task.Run(
                () => GenerateCandidates(
                    plan,
                    cancellationToken),
                cancellationToken);
        }

        public CityPreviewResult[] GenerateCandidates(
            CityPlanData sourcePlan,
            CancellationToken cancellationToken = default)
        {
            CityPlanData plan = PrepareAutomaticPlan(
                sourcePlan,
                out CityValidationReport inputReport);
            if (inputReport.HasErrors)
            {
                return new[]
                {
                    new CityPreviewResult
                    {
                        Plan = plan,
                        Output = new CityGenerationOutput(),
                        Validation = inputReport,
                        CandidateIndex = -1
                    }
                };
            }

            cancellationToken.ThrowIfCancellationRequested();
            var candidates = new List<CityPreviewResult>(
                CandidateCount);
            for (int candidateIndex = 0;
                 candidateIndex < CandidateCount;
                 candidateIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CityPlanData candidatePlan = plan.Clone();
                CityGenerationOutput output = BuildCandidate(
                    candidatePlan,
                    candidateIndex,
                    cancellationToken);
                CityValidationReport report = Validate(
                    candidatePlan,
                    output);
                candidates.Add(new CityPreviewResult
                {
                    Plan = candidatePlan,
                    Output = output,
                    Validation = report,
                    CandidateIndex = candidateIndex
                });
            }

            return candidates
                .OrderByDescending(value => value.Score)
                .ThenBy(value => value.CandidateIndex)
                .Take(3)
                .ToArray();
        }

        public CityPreviewResult GenerateAutomaticPreview(
            CityPlanData sourcePlan,
            CancellationToken cancellationToken = default)
        {
            CityPlanData plan = PrepareAutomaticPlan(
                sourcePlan,
                out CityValidationReport inputReport);
            if (inputReport.HasErrors)
            {
                return new CityPreviewResult
                {
                    Plan = plan,
                    Output = new CityGenerationOutput(),
                    Validation = inputReport,
                    CandidateIndex = -1
                };
            }

            var generated = new List<CityPreviewResult>(
                CandidateCount * MaximumCandidateBatches);
            bool acceptedEarly = false;
            for (int batch = 0;
                 batch < MaximumCandidateBatches;
                 batch++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int pattern = 0;
                     pattern < CandidateCount;
                     pattern++)
                {
                    int candidateIndex =
                        batch * CandidateCount + pattern;
                    CityPlanData candidatePlan = plan.Clone();
                    CityGenerationOutput output = BuildCandidate(
                        candidatePlan,
                        candidateIndex,
                        cancellationToken);
                    var preview = new CityPreviewResult
                    {
                        Plan = candidatePlan,
                        Output = output,
                        CandidateIndex = candidateIndex
                    };
                    AddPackageRenderingData(
                        preview,
                        includePathways: false);
                    CityValidationReport report = Validate(
                        candidatePlan,
                        output);
                    preview.Validation = report;
                    generated.Add(preview);
                    if (generated.Count
                            >= MinimumCandidatesBeforeEarlyStop)
                    {
                        CityPreviewResult currentBest = generated
                            .Where(value =>
                                value.Validation != null
                                && !value.Validation.HasErrors)
                            .OrderByDescending(value => value.Score)
                            .ThenBy(value => value.CandidateIndex)
                            .FirstOrDefault();
                        if (currentBest != null
                            && currentBest.Score
                                >= PreferredAutomaticScore)
                        {
                            acceptedEarly = true;
                            break;
                        }
                    }
                }

                if (acceptedEarly)
                    break;
                CityPreviewResult batchBest = generated
                    .Where(value => value.Validation != null
                        && !value.Validation.HasErrors)
                    .OrderByDescending(value => value.Score)
                    .ThenBy(value => value.CandidateIndex)
                    .FirstOrDefault();
                if (batchBest != null
                    && batchBest.Score >= PreferredAutomaticScore)
                {
                    break;
                }
            }

            CityPreviewResult best = generated
                .Where(value => value.Validation != null
                    && !value.Validation.HasErrors)
                .OrderByDescending(value => value.Score)
                .ThenBy(value => value.CandidateIndex)
                .FirstOrDefault();
            if (best != null)
            {
                AddPackageRenderingData(
                    best,
                    includePathways: true);
                return best;
            }

            return generated
                .OrderByDescending(value => value.Score)
                .ThenBy(value => value.CandidateIndex)
                .FirstOrDefault()
                ?? CreateFailedPreview(
                    plan,
                    "NO_VALID_CANDIDATE",
                    "没有生成出满足硬约束的自动城市方案，请重画边界。");
        }

        CityPlanData PrepareAutomaticPlan(
            CityPlanData sourcePlan,
            out CityValidationReport report)
        {
            CityPlanData plan = sourcePlan?.Clone()
                ?? new CityPlanData();
            var issues = new List<CityPlanningIssue>();
            if (plan.boundary == null || plan.boundary.Length < 3)
            {
                issues.Add(Error(
                    "BOUNDARY_TOO_SMALL",
                    "至少需要3个边界点。",
                    Vector2.zero));
                report = Report(0f, issues);
                return plan;
            }

            Vector2[] source = RemoveRepeatedBoundaryPoints(
                plan.boundary);
            if (!CityPolygonGeometry.ResolveBoundary(
                    source,
                    settings,
                    out CityBoundaryResolution resolution,
                    out string error)
                || resolution.Regions == null
                || resolution.Regions.Count == 0)
            {
                issues.Add(Error(
                    "BOUNDARY_INVALID",
                    string.IsNullOrEmpty(error)
                        ? "边界无法修复，请重新绘制。"
                        : error,
                    Average(source)));
                report = Report(0f, issues);
                return plan;
            }

            List<Vector2> largest = resolution.Regions
                .OrderByDescending(region => Mathf.Abs(
                    CityPolygonGeometry.SignedArea(region)))
                .First();
            float largestArea = Mathf.Abs(
                CityPolygonGeometry.SignedArea(largest));
            float totalArea = resolution.Regions.Sum(region =>
                Mathf.Abs(CityPolygonGeometry.SignedArea(region)));
            float sourceArea = Mathf.Abs(
                CityPolygonGeometry.SignedArea(source));
            float repairDifference = sourceArea > 1f
                ? Mathf.Abs(largestArea - sourceArea) / sourceArea
                : 1f;
            float discardedRatio = totalArea > 1f
                ? Mathf.Max(0f, totalArea - largestArea) / totalArea
                : 1f;
            if ((resolution.WasAutoRepaired
                 && repairDifference > 0.15f)
                || discardedRatio > 0.15f)
            {
                issues.Add(Error(
                    "BOUNDARY_REPAIR_TOO_LARGE",
                    "边界修复会改变超过15%的面积，请重新绘制更清晰的边界。",
                    Average(source)));
                report = Report(0f, issues);
                return plan;
            }

            plan.boundary = largest.ToArray();
            plan.anchors = BuildAutomaticAnchors(plan);
            plan.zonePaintGrid = new CityZonePaintGrid
            {
                cellSize = CityPlanData.DefaultZoneCellSize
            };
            BuildOrCleanZoneGrid(plan);
            report = ValidatePlan(plan);
            return plan;
        }

        CityPlanningAnchorData[] BuildAutomaticAnchors(
            CityPlanData plan)
        {
            Vector2[] boundary = plan.boundary;
            Vector2 interiorCenter = FindInteriorCenter(
                boundary,
                CityPlanData.DefaultZoneCellSize);
            List<Vector2> gateCandidates =
                BuildGateCandidates(boundary, interiorCenter);
            var gates = new List<Vector2>();
            if (gateCandidates.Count > 0)
            {
                int first = 0;
                int second = 0;
                float bestDistance = -1f;
                for (int a = 0; a < gateCandidates.Count; a++)
                {
                    for (int b = a + 1;
                         b < gateCandidates.Count;
                         b++)
                    {
                        float distance = Vector2.SqrMagnitude(
                            gateCandidates[a] - gateCandidates[b]);
                        if (distance > bestDistance)
                        {
                            bestDistance = distance;
                            first = a;
                            second = b;
                        }
                    }
                }
                gates.Add(gateCandidates[first]);
                if (gateCandidates.Count > 1)
                    gates.Add(gateCandidates[second]);
            }
            while (gates.Count < 2)
            {
                Vector2 direction = gates.Count == 0
                    ? FindPrimaryDirection(boundary)
                    : -FindPrimaryDirection(boundary);
                gates.Add(MoveInside(
                    boundary,
                    interiorCenter + direction
                        * PolygonSpan(boundary) * 0.42f,
                    interiorCenter,
                    4f));
            }

            float area = Mathf.Abs(
                CityPolygonGeometry.SignedArea(boundary));
            float perimeter = PolygonPerimeter(boundary);
            float compactness = perimeter <= 0.001f
                ? 0f
                : Mathf.Clamp01(
                    4f * Mathf.PI * area
                    / (perimeter * perimeter));
            if (area >= 160000f
                && compactness >= 0.45f
                && gateCandidates.Count >= 3)
            {
                Vector2 third = gateCandidates
                    .OrderByDescending(value =>
                        Mathf.Min(
                            Vector2.SqrMagnitude(value - gates[0]),
                            Vector2.SqrMagnitude(value - gates[1])))
                    .First();
                if (Vector2.Distance(third, gates[0]) > 60f
                    && Vector2.Distance(third, gates[1]) > 60f)
                {
                    gates.Add(third);
                }
            }

            Vector2 cbd = FindAutomaticCbd(
                boundary,
                gates,
                interiorCenter);
            Vector2 gateDirection = gates.Count >= 2
                ? (gates[1] - gates[0]).normalized
                : FindPrimaryDirection(boundary);
            Vector2 civic = MoveInside(
                boundary,
                cbd + new Vector2(
                    -gateDirection.y,
                    gateDirection.x) * 22f,
                cbd,
                4f);
            Vector2 transit = MoveInside(
                boundary,
                cbd + gateDirection * 18f,
                cbd,
                4f);
            Vector2 freightGate = gates
                .OrderByDescending(value =>
                    Vector2.Distance(value, cbd))
                .First();
            Vector2 industrial = MoveInside(
                boundary,
                Vector2.Lerp(freightGate, cbd, 0.18f),
                cbd,
                6f);
            Vector2 park = FindParkSeed(
                boundary,
                cbd,
                industrial,
                interiorCenter);

            var anchors = new List<CityPlanningAnchorData>();
            for (int i = 0; i < gates.Count; i++)
            {
                anchors.Add(new CityPlanningAnchorData
                {
                    stableId = "auto-gate-" + i.ToString("D2"),
                    type = CityPlanningAnchorType.CityGate,
                    position = gates[i],
                    influenceRadius = 55f,
                    isLocked = true
                });
            }
            anchors.Add(CreateAutomaticAnchor(
                "auto-cbd",
                CityPlanningAnchorType.Cbd,
                cbd,
                90f));
            anchors.Add(CreateAutomaticAnchor(
                "auto-transit",
                CityPlanningAnchorType.TransitHub,
                transit,
                65f));
            anchors.Add(CreateAutomaticAnchor(
                "auto-industrial",
                CityPlanningAnchorType.IndustrialHub,
                industrial,
                75f));
            anchors.Add(CreateAutomaticAnchor(
                "auto-park",
                CityPlanningAnchorType.Park,
                park,
                60f));
            anchors.Add(CreateAutomaticAnchor(
                "auto-civic",
                CityPlanningAnchorType.CivicCenter,
                civic,
                55f));
            return anchors.ToArray();
        }

        static CityPlanningAnchorData CreateAutomaticAnchor(
            string stableId,
            CityPlanningAnchorType type,
            Vector2 position,
            float radius)
        {
            return new CityPlanningAnchorData
            {
                stableId = stableId,
                type = type,
                position = position,
                influenceRadius = radius,
                isLocked = true
            };
        }

        List<Vector2> BuildGateCandidates(
            IReadOnlyList<Vector2> boundary,
            Vector2 interiorCenter)
        {
            var result = new List<Vector2>();
            for (int i = 0; i < boundary.Count; i++)
            {
                Vector2 start = boundary[i];
                Vector2 end = boundary[(i + 1) % boundary.Count];
                if (Vector2.Distance(start, end)
                    < Mathf.Max(12f, settings.minimumBoundaryEdge))
                {
                    continue;
                }
                Vector2 midpoint = (start + end) * 0.5f;
                result.Add(MoveInside(
                    boundary,
                    midpoint,
                    interiorCenter,
                    6f));
            }
            return result;
        }

        static Vector2 FindAutomaticCbd(
            IReadOnlyList<Vector2> boundary,
            IReadOnlyList<Vector2> gates,
            Vector2 interiorCenter)
        {
            GetBounds(
                boundary,
                out Vector2 minimum,
                out Vector2 maximum);
            float span = Mathf.Max(1f, PolygonSpan(boundary));
            Vector2 best = interiorCenter;
            float bestScore = float.MinValue;
            for (float y = minimum.y + 2.5f;
                 y <= maximum.y;
                 y += 5f)
            {
                for (float x = minimum.x + 2.5f;
                     x <= maximum.x;
                     x += 5f)
                {
                    var point = new Vector2(x, y);
                    if (!CityPolygonGeometry.ContainsPoint(
                            boundary,
                            point))
                    {
                        continue;
                    }
                    float centrality = 1f - Mathf.Clamp01(
                        Vector2.Distance(point, interiorCenter)
                        / (span * 0.5f));
                    float gateDistance = 0f;
                    for (int i = 0; i < gates.Count; i++)
                        gateDistance += Vector2.Distance(point, gates[i]);
                    float accessibility = gates.Count == 0
                        ? 1f
                        : 1f - Mathf.Clamp01(
                            gateDistance / gates.Count / span);
                    float clearance = Mathf.Clamp01(
                        DistanceToPolygon(point, boundary)
                        / (span * 0.18f));
                    float continuity = Mathf.Clamp01(
                        DistanceToPolygon(point, boundary) / 45f);
                    float score =
                        centrality * 0.45f
                        + accessibility * 0.30f
                        + clearance * 0.15f
                        + continuity * 0.10f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = point;
                    }
                }
            }
            return best;
        }

        static Vector2 FindParkSeed(
            IReadOnlyList<Vector2> boundary,
            Vector2 cbd,
            Vector2 industrial,
            Vector2 fallback)
        {
            GetBounds(
                boundary,
                out Vector2 minimum,
                out Vector2 maximum);
            float span = Mathf.Max(1f, PolygonSpan(boundary));
            Vector2 best = fallback;
            float bestScore = float.MinValue;
            for (float y = minimum.y + 2.5f;
                 y <= maximum.y;
                 y += 5f)
            {
                for (float x = minimum.x + 2.5f;
                     x <= maximum.x;
                     x += 5f)
                {
                    var point = new Vector2(x, y);
                    if (!CityPolygonGeometry.ContainsPoint(
                            boundary,
                            point))
                    {
                        continue;
                    }
                    float cbdDistance = Vector2.Distance(point, cbd);
                    float centralRing = 1f - Mathf.Clamp01(
                        Mathf.Abs(cbdDistance - span * 0.22f)
                        / (span * 0.22f));
                    float industrialDistance = Mathf.Clamp01(
                        Vector2.Distance(point, industrial)
                        / (span * 0.5f));
                    float clearance = Mathf.Clamp01(
                        DistanceToPolygon(point, boundary) / 35f);
                    float score = centralRing * 0.5f
                        + industrialDistance * 0.3f
                        + clearance * 0.2f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = point;
                    }
                }
            }
            return best;
        }

        static Vector2 FindInteriorCenter(
            IReadOnlyList<Vector2> boundary,
            float cellSize)
        {
            GetBounds(
                boundary,
                out Vector2 minimum,
                out Vector2 maximum);
            Vector2 centroid = CityPolygonGeometry.Centroid(boundary);
            Vector2 best = CityPolygonGeometry.ContainsPoint(
                boundary,
                centroid)
                ? centroid
                : boundary[0];
            float bestClearance = DistanceToPolygon(best, boundary);
            float step = Mathf.Max(2.5f, cellSize);
            for (float y = minimum.y + step * 0.5f;
                 y <= maximum.y;
                 y += step)
            {
                for (float x = minimum.x + step * 0.5f;
                     x <= maximum.x;
                     x += step)
                {
                    var point = new Vector2(x, y);
                    if (!CityPolygonGeometry.ContainsPoint(
                            boundary,
                            point))
                    {
                        continue;
                    }
                    float clearance =
                        DistanceToPolygon(point, boundary);
                    if (clearance > bestClearance)
                    {
                        bestClearance = clearance;
                        best = point;
                    }
                }
            }
            return best;
        }

        static Vector2 MoveInside(
            IReadOnlyList<Vector2> boundary,
            Vector2 source,
            Vector2 target,
            float inset)
        {
            if (CityPolygonGeometry.ContainsPoint(boundary, source)
                && DistanceToPolygon(source, boundary) >= inset)
            {
                return source;
            }
            Vector2 direction = (target - source).normalized;
            for (float distance = Mathf.Max(1f, inset);
                 distance <= Mathf.Max(
                     inset,
                     Vector2.Distance(source, target));
                 distance += Mathf.Max(1f, inset))
            {
                Vector2 candidate = source + direction * distance;
                if (CityPolygonGeometry.ContainsPoint(
                        boundary,
                        candidate)
                    && DistanceToPolygon(candidate, boundary)
                        >= inset * 0.75f)
                {
                    return candidate;
                }
            }
            return target;
        }

        static Vector2[] RemoveRepeatedBoundaryPoints(
            IReadOnlyList<Vector2> source)
        {
            var values = new List<Vector2>();
            for (int i = 0; i < source.Count; i++)
            {
                if (values.Count == 0
                    || Vector2.Distance(
                        values[values.Count - 1],
                        source[i]) > 0.25f)
                {
                    values.Add(source[i]);
                }
            }
            if (values.Count > 1
                && Vector2.Distance(
                    values[0],
                    values[values.Count - 1]) <= 0.25f)
            {
                values.RemoveAt(values.Count - 1);
            }
            return values.ToArray();
        }

        static float PolygonPerimeter(
            IReadOnlyList<Vector2> polygon)
        {
            float value = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                value += Vector2.Distance(
                    polygon[i],
                    polygon[(i + 1) % polygon.Count]);
            }
            return value;
        }

        public CityValidationReport Validate(
            CityPlanData plan,
            CityGenerationOutput output)
        {
            var issues = new List<CityPlanningIssue>();
            if (output == null || output.legacyResult == null)
            {
                issues.Add(Error(
                    "OUTPUT_MISSING",
                    "规划结果为空，不能施工。",
                    Vector2.zero));
                return Report(0f, issues);
            }

            CityGenerationResult legacy = output.legacyResult;
            if (legacy.EnforcePackageOnlyRoads)
            {
                if (legacy.ModularNetworkResult == null
                    || !legacy.ModularNetworkResult.IsValid)
                {
                    issues.Add(Error(
                        "PACKAGE_ROAD_COVERAGE",
                        legacy.ModularNetworkResult?.Error
                            ?? "模型包道路没有覆盖全部规划道路。",
                        Average(plan.boundary)));
                }
                if (legacy.RoadLayoutValidation == null
                    || !legacy.RoadLayoutValidation.IsValid)
                {
                    issues.Add(Error(
                        "PACKAGE_ROAD_INVALID",
                        legacy.RoadLayoutValidation?.Error
                            ?? "模型包道路接口校验失败。",
                        Average(plan.boundary)));
                }
            }
            if (legacy.Roads.Count == 0)
            {
                issues.Add(Error(
                    "ROAD_EMPTY",
                    "道路网络为空。",
                    Average(plan.boundary)));
            }
            if (legacy.Blocks.Count == 0)
            {
                issues.Add(Error(
                    "BLOCK_EMPTY",
                    "道路没有围合出有效街区。",
                    Average(plan.boundary)));
            }
            if (legacy.Buildings.Count == 0)
            {
                issues.Add(Error(
                    "BUILDING_EMPTY",
                    "没有地块匹配到建筑。",
                    Average(plan.boundary)));
            }

            int connectedComponents = CountRoadComponents(
                output.roadNodes,
                output.roadEdges);
            legacy.Diagnostics.RoadConnectedComponentCount =
                connectedComponents;
            if (connectedComponents > 1)
            {
                issues.Add(Error(
                    "ROAD_DISCONNECTED",
                    $"道路图存在 {connectedComponents} 个不连通部分。",
                    Average(plan.boundary),
                    "扩大局部重生成范围",
                    "调整主干路",
                    "取消"));
            }

            int invalidBlocks = 0;
            for (int i = 0; i < legacy.Blocks.Count; i++)
            {
                if (!CityPolygonGeometry.ContainsPolygon(
                        plan.boundary,
                        legacy.Blocks[i].Footprint))
                {
                    invalidBlocks++;
                }
            }
            if (invalidBlocks > 0)
            {
                issues.Add(Error(
                    "BLOCK_OUTSIDE",
                    $"{invalidBlocks} 个街区越过平台边界。",
                    Average(plan.boundary)));
            }

            int invalidLots = 0;
            for (int i = 0; i < output.lots.Length; i++)
            {
                CityPlannedLotData lot = output.lots[i];
                if (Vector2.Distance(
                        lot.frontageStart,
                        lot.frontageEnd)
                    + 0.001f < settings.minimumLotFrontage)
                {
                    invalidLots++;
                }
            }
            if (invalidLots > 0)
            {
                issues.Add(Error(
                    "LOT_NO_FRONTAGE",
                    $"{invalidLots} 个可建筑地块没有足够临街宽度。",
                    Average(plan.boundary)));
            }

            int buildingCount = output.buildings?.Length ?? 0;
            int targetBuildingCount = TargetBuildingCount(
                plan.boundary);
            if (buildingCount
                < Mathf.Max(20, targetBuildingCount * 0.65f))
            {
                issues.Add(Warning(
                    "LOW_BUILDING_COUNT",
                    $"当前只有 {buildingCount} 栋建筑，自动规模目标约为 {targetBuildingCount} 栋。",
                    Average(plan.boundary),
                    "自动尝试更高密度道路候选",
                    "重新绘制更紧凑边界"));
            }
            if (buildingCount > 500)
            {
                issues.Add(Warning(
                    "HIGH_BUILDING_COUNT",
                    $"当前有 {buildingCount} 栋建筑，可能增加运行开销。",
                    Average(plan.boundary),
                    "降低地块密度",
                    "接受当前规模"));
            }

            int incompatibleContacts =
                CountIndustrialResidentialContacts(
                    plan.zonePaintGrid);
            if (incompatibleContacts > 0)
            {
                issues.Add(Error(
                    "ZONE_INCOMPATIBLE",
                    "工业区与住宅区之间缺少20米混合或绿化缓冲。",
                    Average(plan.boundary)));
            }

            float score = 0f;
            score += ScoreConnectivity(output) * 25f;
            score += ScoreAnchorAccess(plan, output) * 20f;
            score += ScoreZoneCompatibility(plan) * 20f;
            score += ScoreBlockQuality(output.blocks) * 15f;
            score += ScoreSkyline(output.buildings) * 10f;
            score += ScorePublicSpace(plan) * 5f;
            score += ScoreVisualQuality(output) * 5f;
            score = Mathf.Clamp(score, 0f, 100f);

            if (score < 80f)
            {
                issues.Add(Warning(
                    "SCORE_LOW",
                    $"规划评分 {score:F1}，低于自动施工要求80。",
                    Average(plan.boundary),
                    "系统继续尝试派生候选",
                    "重新绘制边界"));
            }
            return Report(score, issues);
        }

        CityValidationReport ValidatePlan(CityPlanData plan)
        {
            var issues = new List<CityPlanningIssue>();
            if (plan.boundary == null || plan.boundary.Length < 3)
            {
                issues.Add(Error(
                    "BOUNDARY_TOO_SMALL",
                    "至少需要3个边界点。",
                    Vector2.zero));
                return Report(0f, issues);
            }
            if (CityPolygonGeometry.HasSelfIntersection(plan.boundary))
            {
                issues.Add(Error(
                    "BOUNDARY_SELF_INTERSECTION",
                    "统一平台边界不能自相交。",
                    Average(plan.boundary),
                    "使用凹包修复",
                    "使用凸包修复",
                    "返回修改"));
            }
            float area = Mathf.Abs(
                CityPolygonGeometry.SignedArea(plan.boundary));
            if (area < settings.minimumBoundaryArea)
            {
                issues.Add(Error(
                    "BOUNDARY_AREA",
                    $"边界面积 {area:F0}㎡ 过小。",
                    Average(plan.boundary)));
            }
            if (PolygonSpan(plan.boundary)
                > CityPlanData.MaximumBoundarySpan + 0.001f)
            {
                issues.Add(Error(
                    "BOUNDARY_SPAN",
                    "城市边界跨度不能超过512米。",
                    Average(plan.boundary)));
            }

            int gates = 0;
            int cbd = 0;
            CityPlanningAnchorData[] anchors =
                plan.anchors ?? Array.Empty<CityPlanningAnchorData>();
            for (int i = 0; i < anchors.Length; i++)
            {
                CityPlanningAnchorData anchor = anchors[i];
                if (anchor == null)
                    continue;
                if (!CityPolygonGeometry.ContainsPoint(
                        plan.boundary,
                        anchor.position))
                {
                    issues.Add(Error(
                        "ANCHOR_OUTSIDE",
                        "规划锚点必须位于平台边界内。",
                        anchor.position,
                        "移动锚点",
                        "删除锚点"));
                }
                if (anchor.type == CityPlanningAnchorType.CityGate)
                    gates++;
                if (anchor.type == CityPlanningAnchorType.Cbd)
                    cbd++;
            }
            if (gates < 2)
            {
                issues.Add(Error(
                    "GATE_REQUIRED",
                    "至少需要2个城市入口。",
                    Average(plan.boundary)));
            }
            if (cbd < 1)
            {
                issues.Add(Error(
                    "CBD_REQUIRED",
                    "至少需要1个城市中心。",
                    Average(plan.boundary)));
            }
            return Report(issues.Count == 0 ? 100f : 0f, issues);
        }

        CityGenerationOutput BuildCandidate(
            CityPlanData plan,
            int candidateIndex,
            CancellationToken cancellationToken)
        {
            int patternIndex = candidateIndex % CandidateCount;
            int batchIndex = candidateIndex / CandidateCount;
            Vector2 center = FindAnchor(
                plan,
                CityPlanningAnchorType.Cbd,
                FindInteriorCenter(plan.boundary, 5f));
            Vector2 primary = FindPrimaryDirection(plan.boundary);
            if (patternIndex == 1
                || patternIndex == 2
                || patternIndex == 5)
            {
                primary = FindGateDirection(plan, primary);
            }
            float angleOffset = new[] { 0f, 0f, 0f, 90f, 8f, -8f }[
                patternIndex];
            angleOffset += new[] { 0f, -5f, 5f }[
                Mathf.Clamp(batchIndex, 0, 2)];
            primary = Rotate(primary, angleOffset).normalized;
            Vector2 secondary = new Vector2(-primary.y, primary.x);
            var basis = new PlanningBasis(center, primary, secondary);

            GetLocalBounds(
                plan.boundary,
                basis,
                out Vector2 minimum,
                out Vector2 maximum);
            Vector2 cbd = FindAnchor(
                plan,
                CityPlanningAnchorType.Cbd,
                center);
            Vector2 localCbd = basis.ToLocal(cbd);
            float spacing = Mathf.Clamp(
                settings.blockSpacing
                * new[] { 0.92f, 1f, 1.15f, 1.05f, 1f, 1.2f }[
                    patternIndex]
                * (1f + batchIndex * 0.035f),
                28f,
                60f);
            List<float> uLines = BuildAdaptiveGridLines(
                minimum.x,
                maximum.x,
                localCbd.x,
                spacing,
                plan.seed,
                candidateIndex,
                0);
            List<float> vLines = BuildAdaptiveGridLines(
                minimum.y,
                maximum.y,
                localCbd.y,
                spacing,
                plan.seed,
                candidateIndex,
                1);
            uLines = SnapGridLines(uLines, minimum.x, maximum.x);
            vLines = SnapGridLines(vLines, minimum.y, maximum.y);

            var nodes = new List<CityRoadNodeData>();
            var edges = new List<CityRoadEdgeData>();
            var nodeByGrid = new Dictionary<string, CityRoadNodeData>();
            BuildGridRoadGraph(
                plan,
                candidateIndex,
                basis,
                uLines,
                vLines,
                patternIndex,
                nodeByGrid,
                nodes,
                edges);
            AddAnchorArterials(
                plan,
                candidateIndex,
                basis,
                nodeByGrid,
                nodes,
                edges);
            cancellationToken.ThrowIfCancellationRequested();
            RefineZonesByRoadAccess(plan, edges);
            CityZoneData[] zones = BuildZoneData(plan);

            List<PlanningRoadSegment> planningRoads =
                FlattenRoadEdges(edges);
            List<CityRoadSegment> roadSegments = planningRoads
                .Select(value => value.Segment)
                .ToList();
            var blocks = new List<CityPlannedBlockData>();
            var lots = new List<CityPlannedLotData>();
            var buildings = new List<CityPlannedBuildingData>();
            BuildBlocksLotsAndBuildings(
                plan,
                candidateIndex,
                basis,
                uLines,
                vLines,
                planningRoads,
                blocks,
                lots,
                buildings);

            CityGenerationResult legacy = BuildLegacyResult(
                plan,
                basis,
                roadSegments,
                blocks,
                lots,
                buildings);
            var output = new CityGenerationOutput
            {
                zones = zones,
                roadNodes = nodes.ToArray(),
                roadEdges = edges.ToArray(),
                blocks = blocks.ToArray(),
                lots = lots.ToArray(),
                buildings = buildings.ToArray(),
                semanticPoints = BuildSemanticPoints(
                    plan,
                    edges,
                    blocks),
                legacyResult = legacy,
                roadPatternId = RoadPatternId(patternIndex)
            };
            output.layoutChecksum = ComputeLayoutChecksum(output);
            return output;
        }

        void BuildGridRoadGraph(
            CityPlanData plan,
            int candidateIndex,
            PlanningBasis basis,
            IReadOnlyList<float> uLines,
            IReadOnlyList<float> vLines,
            int patternIndex,
            Dictionary<string, CityRoadNodeData> nodeByGrid,
            List<CityRoadNodeData> nodes,
            List<CityRoadEdgeData> edges)
        {
            for (int u = 0; u < uLines.Count; u++)
            {
                for (int v = 0; v < vLines.Count; v++)
                {
                    Vector2 point = basis.ToWorld(
                        new Vector2(uLines[u], vLines[v]));
                    if (!HasBoundaryClearance(
                            plan.boundary,
                            point,
                            settings.majorRoadWidth * 0.5f))
                    {
                        continue;
                    }
                    string key = GridKey(u, v);
                    var node = new CityRoadNodeData
                    {
                        stableId = StableId(
                            plan.seed,
                            "road-node-" + candidateIndex,
                            key,
                            0),
                        position = SnapWorld(point, basis)
                    };
                    nodeByGrid[key] = node;
                    nodes.Add(node);
                }
            }

            for (int u = 0; u < uLines.Count; u++)
            {
                for (int v = 0; v < vLines.Count; v++)
                {
                    if (!nodeByGrid.TryGetValue(
                            GridKey(u, v),
                            out CityRoadNodeData node))
                    {
                        continue;
                    }
                    TryAddGridEdge(u + 1, v, true);
                    TryAddGridEdge(u, v + 1, false);

                    void TryAddGridEdge(
                        int otherU,
                        int otherV,
                        bool alongPrimary)
                    {
                        if (!nodeByGrid.TryGetValue(
                                GridKey(otherU, otherV),
                                out CityRoadNodeData other))
                        {
                            return;
                        }
                        if (patternIndex == 2
                            && !alongPrimary
                            && u % 2 != 0)
                        {
                            return;
                        }
                        Vector2 midpoint =
                            (node.position + other.position) * 0.5f;
                        if (!CityPolygonGeometry.ContainsPoint(
                                plan.boundary,
                                midpoint))
                        {
                            return;
                        }
                        int centerU = uLines.Count / 2;
                        int centerV = vLines.Count / 2;
                        bool arterial = alongPrimary
                            ? v == centerV
                            : u == centerU;
                        bool perimeterCollector =
                            patternIndex == 3
                            && (u <= 1
                                || v <= 1
                                || u >= uLines.Count - 2
                                || v >= vLines.Count - 2);
                        bool collector = !arterial
                            && (perimeterCollector
                                || (alongPrimary ? v : u) % 3 == 0);
                        CityRoadHierarchy hierarchy = arterial
                            ? CityRoadHierarchy.Arterial
                            : collector
                                ? CityRoadHierarchy.Collector
                                : CityRoadHierarchy.Local;
                        Vector2[] controls =
                            new[] { node.position, other.position };
                        AddEdge(
                            plan,
                            candidateIndex,
                            node,
                            other,
                            hierarchy,
                            controls,
                            edges);
                    }
                }
            }
        }

        void AddAnchorArterials(
            CityPlanData plan,
            int candidateIndex,
            PlanningBasis basis,
            Dictionary<string, CityRoadNodeData> nodeByGrid,
            List<CityRoadNodeData> nodes,
            List<CityRoadEdgeData> edges)
        {
            CityPlanningAnchorData[] anchors =
                plan.anchors ?? Array.Empty<CityPlanningAnchorData>();
            for (int anchorIndex = 0;
                 anchorIndex < anchors.Length;
                 anchorIndex++)
            {
                CityPlanningAnchorData anchor = anchors[anchorIndex];
                if (anchor == null
                    || anchor.type == CityPlanningAnchorType.Park
                    || anchor.type == CityPlanningAnchorType.Landmark)
                {
                    continue;
                }

                CityRoadNodeData nearest = null;
                float nearestDistance = float.MaxValue;
                foreach (CityRoadNodeData value in nodeByGrid.Values)
                {
                    float distance = Vector2.SqrMagnitude(
                        value.position - anchor.position);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = value;
                    }
                }
                if (nearest == null
                    || nearestDistance < RoadGridSize * RoadGridSize
                        * 0.04f)
                {
                    continue;
                }

                Vector2 start = SnapWorld(anchor.position, basis);
                for (int inwardStep = 0;
                     inwardStep < 8
                     && !HasBoundaryClearance(
                         plan.boundary,
                         start,
                         settings.majorRoadWidth * 0.5f);
                     inwardStep++)
                {
                    start = SnapWorld(
                        Vector2.MoveTowards(
                            start,
                            nearest.position,
                            RoadGridSize),
                        basis);
                }
                if (!HasBoundaryClearance(
                        plan.boundary,
                        start,
                        settings.majorRoadWidth * 0.5f))
                {
                    continue;
                }

                var anchorNode = new CityRoadNodeData
                {
                    stableId = StableId(
                        plan.seed,
                        "anchor-road-node",
                        anchor.stableId,
                        candidateIndex),
                    position = start
                };
                nodes.Add(anchorNode);
                Vector2 localStart = basis.ToLocal(start);
                Vector2 localEnd = basis.ToLocal(nearest.position);
                Vector2[] elbows =
                {
                    basis.ToWorld(new Vector2(
                        localStart.x,
                        localEnd.y)),
                    basis.ToWorld(new Vector2(
                        localEnd.x,
                        localStart.y))
                };
                int preferredElbow = StableHash(
                        plan.seed,
                        anchor.stableId,
                        candidateIndex) & 1;
                bool connected = false;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    Vector2 elbow =
                        elbows[(preferredElbow + attempt) % 2];
                    if (!ModularSegmentInsideBoundary(
                            plan.boundary,
                            start,
                            elbow,
                            settings.majorRoadWidth * 0.5f)
                        || !ModularSegmentInsideBoundary(
                            plan.boundary,
                            elbow,
                            nearest.position,
                            settings.majorRoadWidth * 0.5f))
                    {
                        continue;
                    }

                    CityRoadNodeData current = anchorNode;
                    if (Vector2.Distance(start, elbow) > 0.05f
                        && Vector2.Distance(
                            elbow,
                            nearest.position) > 0.05f)
                    {
                        var elbowNode = new CityRoadNodeData
                        {
                            stableId = StableId(
                                plan.seed,
                                "anchor-road-elbow",
                                anchor.stableId,
                                candidateIndex),
                            position = elbow
                        };
                        nodes.Add(elbowNode);
                        AddEdge(
                            plan,
                            candidateIndex,
                            current,
                            elbowNode,
                            CityRoadHierarchy.Arterial,
                            new[] { current.position, elbow },
                            edges);
                        current = elbowNode;
                    }
                    AddEdge(
                        plan,
                        candidateIndex,
                        current,
                        nearest,
                        CityRoadHierarchy.Arterial,
                        new[] { current.position, nearest.position },
                        edges);
                    connected = true;
                    break;
                }
                if (!connected)
                    nodes.Remove(anchorNode);
            }

            RebuildNodeConnections(nodes, edges);
        }

        void BuildBlocksLotsAndBuildings(
            CityPlanData plan,
            int candidateIndex,
            PlanningBasis basis,
            IReadOnlyList<float> uLines,
            IReadOnlyList<float> vLines,
            IReadOnlyList<PlanningRoadSegment> roads,
            List<CityPlannedBlockData> blocks,
            List<CityPlannedLotData> lots,
            List<CityPlannedBuildingData> buildings)
        {
            int lastPrefab = -1;
            for (int u = 0; u + 1 < uLines.Count; u++)
            {
                for (int v = 0; v + 1 < vLines.Count; v++)
                {
                    // Package roads occupy the complete 10m module cell.
                    // Blocks begin at the module edge; their first 2.5m
                    // sub-grid ring is reserved for Pathway C.
                    float inset = RoadGridSize * 0.5f;
                    float u0 = uLines[u] + inset;
                    float u1 = uLines[u + 1] - inset;
                    float v0 = vLines[v] + inset;
                    float v1 = vLines[v + 1] - inset;
                    if (u1 - u0 < settings.minimumLotFrontage
                        || v1 - v0 < settings.minimumLotFrontage)
                    {
                        continue;
                    }
                    Vector2[] footprint =
                    {
                        basis.ToWorld(new Vector2(u0, v0)),
                        basis.ToWorld(new Vector2(u1, v0)),
                        basis.ToWorld(new Vector2(u1, v1)),
                        basis.ToWorld(new Vector2(u0, v1))
                    };
                    if (!CityPolygonGeometry.ContainsPolygon(
                            plan.boundary,
                            footprint)
                        || OverlapsAnyRoad(footprint, roads))
                    {
                        continue;
                    }

                    Vector2 blockCenter = Average(footprint);
                    CityZoneType zoneType = SampleZone(plan, blockCenter);
                    if (zoneType == CityZoneType.Unassigned)
                        zoneType = CityZoneType.Residential;
                    string blockId = StableId(
                        plan.seed,
                        "block-" + candidateIndex,
                        GridKey(u, v),
                        0);
                    var block = new CityPlannedBlockData
                    {
                        stableId = blockId,
                        zoneId = "zone-" + zoneType,
                        zoneType = zoneType,
                        footprint = footprint,
                        frontageRoadIds = FindFrontageRoads(
                            footprint,
                            roads),
                        area = Mathf.Abs(
                            CityPolygonGeometry.SignedArea(footprint))
                    };
                    if (block.frontageRoadIds.Length == 0)
                        continue;
                    blocks.Add(block);

                    if (zoneType == CityZoneType.Park)
                        continue;
                    bool splitAlongU = u1 - u0 >= v1 - v0;
                    float frontage = splitAlongU
                        ? u1 - u0
                        : v1 - v0;
                    int lotCount;
                    if (zoneType == CityZoneType.Residential)
                    {
                        float targetFrontage =
                            ResidentialTargetFrontage(
                                plan.seed,
                                blockId);
                        int maximumByMinimumWidth = Mathf.Max(
                            1,
                            Mathf.FloorToInt(
                                frontage
                                / settings.minimumLotFrontage));
                        lotCount = Mathf.Clamp(
                            Mathf.RoundToInt(
                                frontage / targetFrontage),
                            1,
                            Mathf.Min(9, maximumByMinimumWidth));
                    }
                    else
                    {
                        lotCount = Mathf.Clamp(
                            Mathf.FloorToInt(
                                frontage
                                / Mathf.Max(
                                    settings.minimumLotFrontage,
                                    12f)),
                            2,
                            zoneType == CityZoneType.Commercial
                                ? 5
                                : 4);
                    }
                    float[] lotBreaks = BuildLotBreaks(
                        plan.seed,
                        blockId,
                        lotCount,
                        zoneType);
                    for (int lotIndex = 0;
                         lotIndex < lotCount;
                         lotIndex++)
                    {
                        float t0 = lotBreaks[lotIndex];
                        float t1 = lotBreaks[lotIndex + 1];
                        float zoneLotGap =
                            zoneType == CityZoneType.Residential
                                ? Mathf.Min(settings.lotGap, 0.4f)
                                : settings.lotGap;
                        float gap = zoneLotGap * 0.5f;
                        Vector2[] lotFootprint;
                        Vector2 frontageStart;
                        Vector2 frontageEnd;
                        if (splitAlongU)
                        {
                            float a = Mathf.Lerp(u0, u1, t0) + gap;
                            float b = Mathf.Lerp(u0, u1, t1) - gap;
                            lotFootprint = new[]
                            {
                                basis.ToWorld(new Vector2(a, v0)),
                                basis.ToWorld(new Vector2(b, v0)),
                                basis.ToWorld(new Vector2(b, v1)),
                                basis.ToWorld(new Vector2(a, v1))
                            };
                            frontageStart = lotFootprint[0];
                            frontageEnd = lotFootprint[1];
                        }
                        else
                        {
                            float a = Mathf.Lerp(v0, v1, t0) + gap;
                            float b = Mathf.Lerp(v0, v1, t1) - gap;
                            lotFootprint = new[]
                            {
                                basis.ToWorld(new Vector2(u0, a)),
                                basis.ToWorld(new Vector2(u1, a)),
                                basis.ToWorld(new Vector2(u1, b)),
                                basis.ToWorld(new Vector2(u0, b))
                            };
                            frontageStart = lotFootprint[1];
                            frontageEnd = lotFootprint[2];
                        }
                        if (Vector2.Distance(
                                frontageStart,
                                frontageEnd)
                            < settings.minimumLotFrontage)
                        {
                            continue;
                        }

                        string lotId = StableId(
                            plan.seed,
                            "lot",
                            blockId,
                            lotIndex);
                        float frontageSetback =
                            ZoneFrontageSetback(zoneType);
                        if (zoneType == CityZoneType.Residential)
                        {
                            frontageSetback =
                                ResidentialCovingSetback(
                                    plan.seed,
                                    blockId,
                                    lotId,
                                    lotIndex,
                                    0);
                        }
                        var lot = new CityPlannedLotData
                        {
                            stableId = lotId,
                            blockId = blockId,
                            zoneType = zoneType,
                            footprint = lotFootprint,
                            frontageStart = frontageStart,
                            frontageEnd = frontageEnd,
                            entrance =
                                (frontageStart + frontageEnd) * 0.5f,
                            setback = frontageSetback,
                            coverage = ZoneCoverage(zoneType)
                        };
                        lots.Add(lot);

                        int frontageEdgeIndex = splitAlongU ? 0 : 1;
                        float lotDepth = LotDepth(
                            lotFootprint,
                            frontageEdgeIndex);
                        float doubleFrontageDepth =
                            ZoneDoubleFrontageDepth(zoneType);
                        if (zoneType == CityZoneType.Residential)
                        {
                            doubleFrontageDepth +=
                                (Mathf.Abs(StableHash(
                                     plan.seed,
                                     blockId,
                                     47)) % 11 - 5)
                                * 0.8f;
                        }
                        bool allowRearBuilding =
                            lotDepth >= doubleFrontageDepth
                            && zoneType != CityZoneType.Industrial;
                        Vector2[][] buildingPads =
                            CreateBuildingPads(
                                lotFootprint,
                                frontageEdgeIndex,
                                allowRearBuilding);
                        for (int padIndex = 0;
                             padIndex < buildingPads.Length
                            && buildings.Count < 500;
                             padIndex++)
                        {
                            int padFrontageEdge =
                                padIndex == 0
                                    ? frontageEdgeIndex
                                    : (frontageEdgeIndex + 2) % 4;
                            IReadOnlyList<Vector2> placementPad =
                                buildingPads[padIndex];
                            float padSetback = frontageSetback;
                            float sideBias = 0f;
                            if (zoneType == CityZoneType.Residential)
                            {
                                placementPad =
                                    LimitPadDepthFromFrontage(
                                        placementPad,
                                        padFrontageEdge,
                                        ResidentialBuildingDepth(
                                            plan.seed,
                                            blockId,
                                            lotId,
                                            lotIndex,
                                            padIndex));
                                padSetback =
                                    ResidentialCovingSetback(
                                        plan.seed,
                                        blockId,
                                        lotId,
                                        lotIndex,
                                        padIndex);
                                sideBias =
                                    (Mathf.Abs(StableHash(
                                         plan.seed,
                                         lotId,
                                         53 + padIndex))
                                     % 15 - 7)
                                    * 0.12f;
                            }
                            Vector2[] buildingFootprint =
                                InsetStreetwallRectangle(
                                    placementPad,
                                    padFrontageEdge,
                                    padSetback,
                                    ZoneSideSetback(zoneType),
                                    ZoneRearSetback(zoneType),
                                    sideBias);
                            if (buildingFootprint == null)
                                continue;
                            if (zoneType == CityZoneType.Residential)
                            {
                                buildingFootprint =
                                    ApplyResidentialCovingRotation(
                                        buildingFootprint,
                                        lotFootprint,
                                        plan.seed,
                                        blockId,
                                        lotId,
                                        lotIndex,
                                        padIndex);
                            }
                            int prefab = SelectBuildingCatalogIndex(
                                zoneType,
                                plan,
                                Average(buildingFootprint),
                                lotId,
                                candidateIndex * 3 + padIndex,
                                buildingCatalogCount);
                            if (prefab == lastPrefab
                                && buildingCatalogCount > 1)
                            {
                                prefab = (prefab + 1)
                                    % buildingCatalogCount;
                            }
                            lastPrefab = prefab;
                            string buildingKey =
                                lotId + "-pad-" + padIndex;
                            float height = ZoneHeight(
                                zoneType,
                                plan,
                                Average(buildingFootprint),
                                buildingKey);
                            buildings.Add(new CityPlannedBuildingData
                            {
                                stableId = StableId(
                                    plan.seed,
                                    "building",
                                    lotId,
                                    padIndex),
                                lotId = lotId,
                                catalogId = "modern-building-"
                                    + (prefab + 1).ToString("00"),
                                zoneType = zoneType,
                                footprint = buildingFootprint,
                                frontageDirection =
                                    ((buildingFootprint[
                                          (padFrontageEdge + 0) % 4]
                                      + buildingFootprint[
                                          (padFrontageEdge + 1) % 4])
                                     * 0.5f
                                     - Average(buildingFootprint))
                                    .normalized,
                                height = height,
                                uniformScale = 1f,
                                materialIndex = Mathf.Abs(
                                    StableHash(
                                        plan.seed,
                                        buildingKey,
                                        17)) % 4
                            });
                        }
                    }
                }
            }
            EnsureVisibleParkBlocks(
                plan,
                blocks,
                lots,
                buildings);
        }

        static float ResidentialTargetFrontage(
            int seed,
            string blockId)
        {
            int hash = Mathf.Abs(StableHash(
                seed,
                blockId,
                63));
            return 10.5f + hash % 61 / 10f;
        }

        static float[] BuildLotBreaks(
            int seed,
            string blockId,
            int lotCount,
            CityZoneType zoneType)
        {
            var values = new float[lotCount + 1];
            var weights = new float[lotCount];
            float total = 0f;
            float residentialPhase =
                Mathf.Abs(StableHash(seed, blockId, 67))
                % 6283 / 1000f;
            for (int index = 0; index < lotCount; index++)
            {
                float weight = 1f;
                if (zoneType == CityZoneType.Residential)
                {
                    float neighborhoodWave = Mathf.Sin(
                        residentialPhase + index * 1.37f) * 0.17f;
                    float localVariation =
                        (Mathf.Abs(StableHash(
                             seed,
                             blockId,
                             70 + index))
                         % 17 - 8) / 100f;
                    weight = 1f
                        + neighborhoodWave
                        + localVariation;
                }
                weights[index] = weight;
                total += weight;
            }
            float walked = 0f;
            values[0] = 0f;
            for (int index = 0; index < lotCount; index++)
            {
                walked += weights[index];
                values[index + 1] =
                    index + 1 == lotCount
                        ? 1f
                        : walked / Mathf.Max(0.001f, total);
            }
            return values;
        }

        static float ResidentialCovingSetback(
            int seed,
            string blockId,
            string lotId,
            int lotIndex,
            int padIndex)
        {
            float phase =
                Mathf.Abs(StableHash(seed, blockId, 83))
                % 6283 / 1000f;
            float neighborhoodWave = Mathf.Sin(
                phase
                + lotIndex * 1.05f
                + padIndex * 0.43f) * 0.78f;
            float localVariation =
                (Mathf.Abs(StableHash(
                     seed,
                     lotId,
                     89 + padIndex))
                 % 9 - 4) * 0.08f;
            return Mathf.Clamp(
                1.55f + neighborhoodWave + localVariation,
                0.6f,
                2.8f);
        }

        static float ResidentialBuildingDepth(
            int seed,
            string blockId,
            string lotId,
            int lotIndex,
            int padIndex)
        {
            float phase =
                Mathf.Abs(StableHash(seed, blockId, 97))
                % 6283 / 1000f;
            float neighborhoodWave = Mathf.Sin(
                phase + lotIndex * 0.74f) * 1.4f;
            float localVariation =
                Mathf.Abs(StableHash(
                    seed,
                    lotId,
                    101 + padIndex)) % 31 / 10f;
            return Mathf.Clamp(
                12.5f + neighborhoodWave + localVariation,
                11.5f,
                17.5f);
        }

        static Vector2[] LimitPadDepthFromFrontage(
            IReadOnlyList<Vector2> footprint,
            int frontageEdgeIndex,
            float maximumDepth)
        {
            if (footprint == null || footprint.Count != 4)
                return null;
            Vector2[] result = footprint.ToArray();
            int aIndex = ((frontageEdgeIndex % 4) + 4) % 4;
            int bIndex = (aIndex + 1) % 4;
            int cIndex = (aIndex + 2) % 4;
            int dIndex = (aIndex + 3) % 4;
            Vector2 a = result[aIndex];
            Vector2 b = result[bIndex];
            Vector2 c = result[cIndex];
            Vector2 d = result[dIndex];
            float depth = Vector2.Distance(
                (a + b) * 0.5f,
                (c + d) * 0.5f);
            if (depth <= maximumDepth || depth <= 0.001f)
                return result;
            float ratio = maximumDepth / depth;
            result[cIndex] = Vector2.Lerp(b, c, ratio);
            result[dIndex] = Vector2.Lerp(a, d, ratio);
            return result;
        }

        static Vector2[] ApplyResidentialCovingRotation(
            IReadOnlyList<Vector2> footprint,
            IReadOnlyList<Vector2> lotFootprint,
            int seed,
            string blockId,
            string lotId,
            int lotIndex,
            int padIndex)
        {
            if (footprint == null || footprint.Count != 4)
                return null;
            float phase =
                Mathf.Abs(StableHash(seed, blockId, 107))
                % 6283 / 1000f;
            float fieldAngle = Mathf.Sin(
                phase + lotIndex * 0.82f) * 4.2f;
            float localAngle =
                (Mathf.Abs(StableHash(
                     seed,
                     lotId,
                     109 + padIndex))
                 % 7 - 3) * 0.55f;
            float angle = Mathf.Clamp(
                fieldAngle + localAngle,
                -6f,
                6f);
            Vector2[] original = footprint.ToArray();
            Vector2 center = Average(original);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                float radians = angle * Mathf.Deg2Rad;
                float cosine = Mathf.Cos(radians);
                float sine = Mathf.Sin(radians);
                var rotated = new Vector2[original.Length];
                for (int index = 0; index < original.Length; index++)
                {
                    Vector2 delta = original[index] - center;
                    rotated[index] = center + new Vector2(
                        delta.x * cosine - delta.y * sine,
                        delta.x * sine + delta.y * cosine);
                }
                if (CityPolygonGeometry.ContainsPolygon(
                        lotFootprint,
                        rotated))
                {
                    return rotated;
                }
                angle *= 0.5f;
            }
            return original;
        }

        static void EnsureVisibleParkBlocks(
            CityPlanData plan,
            List<CityPlannedBlockData> blocks,
            List<CityPlannedLotData> lots,
            List<CityPlannedBuildingData> buildings)
        {
            if (blocks == null || blocks.Count == 0)
                return;
            int targetParkCount = Mathf.Max(
                1,
                Mathf.CeilToInt(blocks.Count * 0.06f));
            int existingParkCount = blocks.Count(
                block => block.zoneType == CityZoneType.Park);
            if (existingParkCount >= targetParkCount)
                return;

            Vector2 parkAnchor = FindAnchor(
                plan,
                CityPlanningAnchorType.Park,
                Average(plan.boundary));
            CityPlannedBlockData[] candidates = blocks
                .Where(block =>
                    block.zoneType != CityZoneType.Park
                    && block.zoneType != CityZoneType.Commercial
                    && block.zoneType != CityZoneType.Civic)
                .OrderBy(block =>
                    Vector2.SqrMagnitude(
                        Average(block.footprint) - parkAnchor)
                    + (block.zoneType == CityZoneType.Residential
                        ? 0f
                        : block.zoneType == CityZoneType.MixedUse
                            ? 250f
                            : 500f))
                .ThenBy(block => block.stableId)
                .ToArray();
            int promoteCount = Mathf.Min(
                targetParkCount - existingParkCount,
                candidates.Length);
            for (int index = 0; index < promoteCount; index++)
            {
                CityPlannedBlockData block = candidates[index];
                block.zoneType = CityZoneType.Park;
                block.zoneId = "zone-" + CityZoneType.Park;
                HashSet<string> removedLotIds = new HashSet<string>(
                    lots
                        .Where(lot => lot.blockId == block.stableId)
                        .Select(lot => lot.stableId));
                if (removedLotIds.Count > 0)
                {
                    buildings.RemoveAll(building =>
                        removedLotIds.Contains(building.lotId));
                    lots.RemoveAll(lot =>
                        removedLotIds.Contains(lot.stableId));
                }
            }
        }

        static float LotDepth(
            IReadOnlyList<Vector2> footprint,
            int frontageEdgeIndex)
        {
            if (frontageEdgeIndex == 0)
            {
                return Vector2.Distance(
                    (footprint[0] + footprint[1]) * 0.5f,
                    (footprint[2] + footprint[3]) * 0.5f);
            }
            return Vector2.Distance(
                (footprint[1] + footprint[2]) * 0.5f,
                (footprint[3] + footprint[0]) * 0.5f);
        }

        static Vector2[][] CreateBuildingPads(
            IReadOnlyList<Vector2> footprint,
            int frontageEdgeIndex,
            bool splitDepth)
        {
            if (!splitDepth)
                return new[] { footprint.ToArray() };
            const float firstEnd = 0.48f;
            const float secondStart = 0.52f;
            if (frontageEdgeIndex == 0)
            {
                return new[]
                {
                    new[]
                    {
                        footprint[0],
                        footprint[1],
                        Vector2.Lerp(
                            footprint[1],
                            footprint[2],
                            firstEnd),
                        Vector2.Lerp(
                            footprint[0],
                            footprint[3],
                            firstEnd)
                    },
                    new[]
                    {
                        Vector2.Lerp(
                            footprint[0],
                            footprint[3],
                            secondStart),
                        Vector2.Lerp(
                            footprint[1],
                            footprint[2],
                            secondStart),
                        footprint[2],
                        footprint[3]
                    }
                };
            }
            return new[]
            {
                new[]
                {
                    Vector2.Lerp(
                        footprint[1],
                        footprint[0],
                        firstEnd),
                    footprint[1],
                    footprint[2],
                    Vector2.Lerp(
                        footprint[2],
                        footprint[3],
                        firstEnd)
                },
                new[]
                {
                    footprint[0],
                    Vector2.Lerp(
                        footprint[1],
                        footprint[0],
                        secondStart),
                    Vector2.Lerp(
                        footprint[2],
                        footprint[3],
                        secondStart),
                    footprint[3]
                }
            };
        }

        static Vector2[] InsetStreetwallRectangle(
            IReadOnlyList<Vector2> footprint,
            int frontageEdgeIndex,
            float frontageSetback,
            float sideSetback,
            float rearSetback,
            float sideBias = 0f)
        {
            if (footprint == null || footprint.Count != 4)
                return null;
            int aIndex = ((frontageEdgeIndex % 4) + 4) % 4;
            int bIndex = (aIndex + 1) % 4;
            int cIndex = (aIndex + 2) % 4;
            int dIndex = (aIndex + 3) % 4;
            Vector2 a = footprint[aIndex];
            Vector2 b = footprint[bIndex];
            Vector2 c = footprint[cIndex];
            Vector2 d = footprint[dIndex];
            Vector2 tangent = (b - a).normalized;
            Vector2 inward =
                ((d - a).normalized + (c - b).normalized).normalized;
            float width = Vector2.Distance(a, b);
            float depth = Vector2.Distance(
                (a + b) * 0.5f,
                (c + d) * 0.5f);
            float startSideSetback =
                Mathf.Max(0.15f, sideSetback + sideBias);
            float endSideSetback =
                Mathf.Max(0.15f, sideSetback - sideBias);
            if (width <= startSideSetback
                    + endSideSetback
                    + 2f
                || depth <= frontageSetback + rearSetback + 2f)
            {
                return null;
            }
            return new[]
            {
                a + tangent * startSideSetback
                    + inward * frontageSetback,
                b - tangent * endSideSetback
                    + inward * frontageSetback,
                c - tangent * endSideSetback
                    - inward * rearSetback,
                d + tangent * startSideSetback
                    - inward * rearSetback
            };
        }

        static float ZoneFrontageSetback(CityZoneType type)
        {
            switch (type)
            {
                case CityZoneType.Commercial: return 0.35f;
                case CityZoneType.MixedUse: return 0.55f;
                case CityZoneType.Civic: return 0.8f;
                case CityZoneType.Residential: return 1.25f;
                case CityZoneType.Industrial: return 1.5f;
                default: return 1f;
            }
        }

        static float ZoneSideSetback(CityZoneType type)
        {
            switch (type)
            {
                case CityZoneType.Commercial: return 0.3f;
                case CityZoneType.MixedUse: return 0.45f;
                case CityZoneType.Residential: return 0.45f;
                case CityZoneType.Industrial: return 1.2f;
                default: return 0.7f;
            }
        }

        static float ZoneRearSetback(CityZoneType type)
        {
            switch (type)
            {
                case CityZoneType.Commercial: return 0.5f;
                case CityZoneType.MixedUse: return 0.7f;
                case CityZoneType.Residential: return 1.2f;
                case CityZoneType.Industrial: return 1.5f;
                default: return 1f;
            }
        }

        static float ZoneDoubleFrontageDepth(CityZoneType type)
        {
            switch (type)
            {
                case CityZoneType.Commercial: return 18f;
                case CityZoneType.MixedUse: return 18f;
                case CityZoneType.Civic: return 20f;
                case CityZoneType.Residential: return 24f;
                default: return float.MaxValue;
            }
        }

        static int SelectBuildingCatalogIndex(
            CityZoneType type,
            CityPlanData plan,
            Vector2 position,
            string stableId,
            int salt,
            int catalogCount)
        {
            int[] highRise =
            {
                1, 5, 6, 7, 13, 14, 18, 22, 23, 24
            };
            int[] midRise =
            {
                15, 17, 19, 20, 21, 23, 24
            };
            int[] lowRise =
            {
                0, 2, 3, 4, 8, 9, 10, 11, 12, 16
            };
            Vector2 cbd = FindAnchor(
                plan,
                CityPlanningAnchorType.Cbd,
                CityPolygonGeometry.Centroid(plan.boundary));
            float cbdFactor = 1f - Mathf.Clamp01(
                Vector2.Distance(position, cbd)
                / Mathf.Max(1f, PolygonSpan(plan.boundary) * 0.38f));
            int[] preferred;
            switch (type)
            {
                case CityZoneType.Commercial:
                    preferred = highRise;
                    break;
                case CityZoneType.MixedUse:
                    preferred = cbdFactor >= 0.38f
                        ? highRise
                        : midRise;
                    break;
                case CityZoneType.Civic:
                    preferred = midRise;
                    break;
                case CityZoneType.Residential:
                    preferred = cbdFactor >= 0.62f
                        ? midRise
                        : lowRise;
                    break;
                default:
                    preferred = lowRise;
                    break;
            }
            int[] available = preferred
                .Where(value => value >= 0 && value < catalogCount)
                .ToArray();
            if (available.Length == 0)
            {
                return Mathf.Abs(StableHash(
                    plan.seed,
                    stableId,
                    salt)) % Mathf.Max(1, catalogCount);
            }
            int selected = Mathf.Abs(StableHash(
                plan.seed,
                stableId,
                salt)) % available.Length;
            return available[selected];
        }

        CityGenerationResult BuildLegacyResult(
            CityPlanData plan,
            PlanningBasis planningBasis,
            IReadOnlyList<CityRoadSegment> roads,
            IReadOnlyList<CityPlannedBlockData> blocks,
            IReadOnlyList<CityPlannedLotData> lots,
            IReadOnlyList<CityPlannedBuildingData> buildings)
        {
            var boundary = new List<Vector2>(plan.boundary);
            if (CityPolygonGeometry.SignedArea(boundary) < 0f)
                boundary.Reverse();
            var result = new CityGenerationResult
            {
                IsSuccess = true,
                Boundary = boundary,
                Regions = new List<List<Vector2>> { boundary },
                BoundaryResolution = new CityBoundaryResolution
                {
                    SourcePoints = boundary,
                    Regions = new List<List<Vector2>> { boundary },
                    IgnoredRegions = new List<List<Vector2>>(),
                    TotalArea = Mathf.Abs(
                        CityPolygonGeometry.SignedArea(boundary)),
                    Message = "规划边界有效。"
                }
            };
            result.Roads.AddRange(roads);
            result.ModularRoadAxis = planningBasis.AxisU;
            for (int i = 0; i < blocks.Count; i++)
            {
                result.Blocks.Add(new CityBlockData(
                    blocks[i].footprint,
                    blocks[i].zoneType));
            }
            for (int i = 0; i < lots.Count; i++)
            {
                result.Lots.Add(new CityLotData(
                    lots[i].footprint));
            }
            for (int i = 0; i < buildings.Count; i++)
            {
                CityPlannedBuildingData building = buildings[i];
                int prefabIndex = ParseCatalogIndex(
                    building.catalogId);
                result.Buildings.Add(new CityBuildingData(
                    building.footprint,
                    building.height,
                    building.materialIndex,
                    prefabIndex,
                    building.frontageDirection));
            }
            return result;
        }

        void AddPackageRenderingData(
            CityPreviewResult preview,
            bool includePathways)
        {
            CityGenerationResult result =
                preview?.Output?.legacyResult;
            if (result == null
                || result.Boundary == null
                || result.Boundary.Count < 3)
            {
                return;
            }

            result.RoadModules.Clear();
            if (includePathways)
                result.PathwayModules.Clear();
            result.EnforcePackageOnlyRoads = true;
            result.EnforcePackageOnlySurfaces = true;
            Vector2 origin = FindAnchor(
                preview.Plan,
                CityPlanningAnchorType.Cbd,
                Average(result.Boundary));
            var modularBasis = new CityModularGridBasis(
                origin,
                result.ModularRoadAxis);
            result.ModularNetworkResult =
                CityModularRoadPlanner.GenerateNetworkFromRoadSegments(
                    result.Roads,
                    0,
                    modularBasis,
                    settings.modularRoadCellSize,
                    result);
            if (includePathways)
            {
                CityModularRoadPlanner.GeneratePathways(
                    result.Boundary,
                    0,
                    modularBasis,
                    settings,
                    result,
                    0,
                    0,
                    preview.Plan.seed,
                    false);
            }
            if (result.RoadModules.Count > 0)
            {
                result.RoadLayoutValidation =
                    CityModularRoadPlanner.ValidateLayout(
                        result.RoadModules,
                        includePathways
                            ? result.PathwayModules
                            : null);
            }
        }

        void BuildOrCleanZoneGrid(CityPlanData plan)
        {
            CityZonePaintGrid grid = plan.zonePaintGrid
                ?? new CityZonePaintGrid();
            GetBounds(
                plan.boundary,
                out Vector2 minimum,
                out Vector2 maximum);
            float cellSize = grid.cellSize > 0f
                ? grid.cellSize
                : CityPlanData.DefaultZoneCellSize;
            int width = Mathf.CeilToInt(
                (maximum.x - minimum.x) / cellSize);
            int height = Mathf.CeilToInt(
                (maximum.y - minimum.y) / cellSize);
            if (grid.width != width
                || grid.height != height
                || grid.CellCount == 0)
            {
                grid = new CityZonePaintGrid
                {
                    origin = minimum,
                    cellSize = cellSize,
                    width = width,
                    height = height
                };
            }

            CityZoneType[] values = grid.Decode();
            bool hasAssignedCells = values.Any(value =>
                value != CityZoneType.Unassigned);
            if (!hasAssignedCells)
            {
                AssignAutomaticZones(plan, grid, values);
            }
            for (int index = 0; index < values.Length; index++)
            {
                int x = index % grid.width;
                int y = index / grid.width;
                Vector2 point = grid.origin + new Vector2(
                    (x + 0.5f) * grid.cellSize,
                    (y + 0.5f) * grid.cellSize);
                if (!CityPolygonGeometry.ContainsPoint(
                        plan.boundary,
                        point))
                {
                    values[index] = CityZoneType.Unassigned;
                    continue;
                }
                if (values[index] == CityZoneType.Unassigned
                    && hasAssignedCells)
                    values[index] = ChooseAutomaticZone(plan, point);
            }
            RemoveSmallZoneIslands(grid, values, 4);
            grid.Encode(values);
            plan.zonePaintGrid = grid;
        }

        void AssignAutomaticZones(
            CityPlanData plan,
            CityZonePaintGrid grid,
            CityZoneType[] values)
        {
            var inside = new List<int>();
            var positions = new Dictionary<int, Vector2>();
            for (int index = 0; index < values.Length; index++)
            {
                int x = index % grid.width;
                int y = index / grid.width;
                Vector2 point = grid.origin + new Vector2(
                    (x + 0.5f) * grid.cellSize,
                    (y + 0.5f) * grid.cellSize);
                if (!CityPolygonGeometry.ContainsPoint(
                        plan.boundary,
                        point))
                {
                    values[index] = CityZoneType.Unassigned;
                    continue;
                }
                inside.Add(index);
                positions[index] = point;
            }
            if (inside.Count == 0)
                return;

            Vector2 cbd = FindAnchor(
                plan,
                CityPlanningAnchorType.Cbd,
                Average(plan.boundary));
            Vector2 transit = FindAnchor(
                plan,
                CityPlanningAnchorType.TransitHub,
                cbd);
            Vector2 industrial = FindAnchor(
                plan,
                CityPlanningAnchorType.IndustrialHub,
                cbd);
            Vector2 civic = FindAnchor(
                plan,
                CityPlanningAnchorType.CivicCenter,
                cbd);
            Vector2 park = FindAnchor(
                plan,
                CityPlanningAnchorType.Park,
                cbd);
            float span = Mathf.Max(
                1f,
                PolygonSpan(plan.boundary));

            int commercialTarget =
                Mathf.RoundToInt(inside.Count * 0.12f);
            int mixedTarget =
                Mathf.RoundToInt(inside.Count * 0.23f);
            int industrialTarget =
                Mathf.RoundToInt(inside.Count * 0.10f);
            int civicTarget =
                Mathf.RoundToInt(inside.Count * 0.05f);
            int parkTarget =
                Mathf.RoundToInt(inside.Count * 0.10f);

            AssignTop(
                CityZoneType.Commercial,
                commercialTarget,
                index =>
                    Near(positions[index], cbd, span * 0.18f) * 0.82f
                    + Near(
                        positions[index],
                        transit,
                        span * 0.2f) * 0.18f);
            AssignTop(
                CityZoneType.Industrial,
                industrialTarget,
                index =>
                    Near(
                        positions[index],
                        industrial,
                        span * 0.2f) * 0.72f
                    + (1f - Mathf.Clamp01(
                        DistanceToPolygon(
                            positions[index],
                            plan.boundary) / 45f)) * 0.18f
                    + (1f - Near(
                        positions[index],
                        cbd,
                        span * 0.45f)) * 0.10f);
            AssignTop(
                CityZoneType.Civic,
                civicTarget,
                index =>
                    Near(positions[index], civic, span * 0.16f) * 0.7f
                    + Near(positions[index], cbd, span * 0.22f) * 0.3f);
            AssignTop(
                CityZoneType.Park,
                parkTarget,
                index =>
                    Near(positions[index], park, span * 0.2f) * 0.62f
                    + Near(positions[index], cbd, span * 0.35f) * 0.18f
                    + (1f - Mathf.Clamp01(
                        DistanceToPolygon(
                            positions[index],
                            plan.boundary) / 30f)) * 0.20f);
            AssignTop(
                CityZoneType.MixedUse,
                mixedTarget,
                index =>
                    Near(positions[index], cbd, span * 0.34f) * 0.52f
                    + Near(
                        positions[index],
                        transit,
                        span * 0.28f) * 0.28f
                    + Near(
                        positions[index],
                        industrial,
                        28f) * 0.20f);

            for (int i = 0; i < inside.Count; i++)
            {
                int index = inside[i];
                if (values[index] == CityZoneType.Unassigned)
                    values[index] = CityZoneType.Residential;
            }
            InsertIndustrialBuffer(
                values,
                positions,
                20f);
            RebalanceMixedAndResidential(
                values,
                positions,
                mixedTarget,
                cbd,
                22f);

            void AssignTop(
                CityZoneType type,
                int count,
                Func<int, float> score)
            {
                IEnumerable<int> selected = inside
                    .Where(index =>
                        values[index] == CityZoneType.Unassigned)
                    .OrderByDescending(index =>
                        score(index)
                        + (StableHash(
                            plan.seed,
                            type.ToString(),
                            index) & 1023) * 0.0000001f)
                    .ThenBy(index => index)
                    .Take(Mathf.Max(0, count));
                foreach (int index in selected)
                    values[index] = type;
            }
        }

        static void InsertIndustrialBuffer(
            CityZoneType[] values,
            IReadOnlyDictionary<int, Vector2> positions,
            float bufferDistance)
        {
            var industrial = new List<Vector2>();
            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] == CityZoneType.Industrial
                    && positions.TryGetValue(
                        index,
                        out Vector2 position))
                {
                    industrial.Add(position);
                }
            }
            float bufferSquared = bufferDistance * bufferDistance;
            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] != CityZoneType.Residential
                    || !positions.TryGetValue(
                        index,
                        out Vector2 position))
                {
                    continue;
                }
                for (int i = 0; i < industrial.Count; i++)
                {
                    if (Vector2.SqrMagnitude(
                            position - industrial[i])
                        <= bufferSquared)
                    {
                        values[index] = CityZoneType.MixedUse;
                        break;
                    }
                }
            }
        }

        static void RebalanceMixedAndResidential(
            CityZoneType[] values,
            IReadOnlyDictionary<int, Vector2> positions,
            int mixedTarget,
            Vector2 cbd,
            float protectedIndustrialDistance)
        {
            int currentMixed = values.Count(value =>
                value == CityZoneType.MixedUse);
            int excess = currentMixed - mixedTarget;
            if (excess <= 0)
                return;
            var industrial = new List<Vector2>();
            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] == CityZoneType.Industrial
                    && positions.TryGetValue(
                        index,
                        out Vector2 position))
                {
                    industrial.Add(position);
                }
            }
            float protectedSquared =
                protectedIndustrialDistance
                * protectedIndustrialDistance;
            int[] candidates = Enumerable.Range(0, values.Length)
                .Where(index =>
                    values[index] == CityZoneType.MixedUse
                    && positions.ContainsKey(index)
                    && industrial.All(point =>
                        Vector2.SqrMagnitude(
                            positions[index] - point)
                        > protectedSquared))
                .OrderByDescending(index =>
                    Vector2.SqrMagnitude(
                        positions[index] - cbd))
                .ThenBy(index => index)
                .Take(excess)
                .ToArray();
            for (int i = 0; i < candidates.Length; i++)
                values[candidates[i]] = CityZoneType.Residential;
        }

        static float Near(
            Vector2 point,
            Vector2 target,
            float radius)
        {
            return 1f - Mathf.Clamp01(
                Vector2.Distance(point, target)
                / Mathf.Max(1f, radius));
        }

        CityZoneData[] BuildZoneData(CityPlanData plan)
        {
            CityZonePaintGrid grid = plan.zonePaintGrid;
            CityZoneType[] values = grid.Decode();
            var result = new List<CityZoneData>();
            var visited = new bool[values.Length];
            for (int index = 0; index < values.Length; index++)
            {
                CityZoneType type = values[index];
                if (visited[index]
                    || type == CityZoneType.Unassigned)
                {
                    continue;
                }
                List<int> component = FloodComponent(
                    grid,
                    values,
                    visited,
                    index,
                    type);
                GetComponentBounds(
                    grid,
                    component,
                    out Vector2 minimum,
                    out Vector2 maximum);
                result.Add(new CityZoneData
                {
                    stableId = StableId(
                        plan.seed,
                        "zone",
                        type.ToString(),
                        result.Count),
                    type = type,
                    boundary = new[]
                    {
                        new Vector2(minimum.x, minimum.y),
                        new Vector2(maximum.x, minimum.y),
                        new Vector2(maximum.x, maximum.y),
                        new Vector2(minimum.x, maximum.y)
                    },
                    density = ZoneDensity(type),
                    minimumBuildingHeight = ZoneHeightRange(type).x,
                    maximumBuildingHeight = ZoneHeightRange(type).y,
                    targetBlockArea = ZoneTargetBlockArea(type)
                });
            }
            return result.ToArray();
        }

        static void RefineZonesByRoadAccess(
            CityPlanData plan,
            IReadOnlyList<CityRoadEdgeData> edges)
        {
            CityZonePaintGrid grid = plan.zonePaintGrid;
            if (grid == null
                || grid.CellCount == 0
                || edges == null
                || edges.Count == 0)
            {
                return;
            }
            CityZoneType[] values = grid.Decode();
            var positions = new Dictionary<int, Vector2>();
            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] == CityZoneType.Unassigned)
                    continue;
                int x = index % grid.width;
                int y = index / grid.width;
                positions[index] = grid.origin + new Vector2(
                    (x + 0.5f) * grid.cellSize,
                    (y + 0.5f) * grid.cellSize);
            }

            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] != CityZoneType.Commercial
                    && values[index] != CityZoneType.Civic)
                {
                    continue;
                }
                Vector2 point = positions[index];
                float nearest = float.MaxValue;
                for (int edgeIndex = 0;
                     edgeIndex < edges.Count;
                     edgeIndex++)
                {
                    Vector2[] samples = SampleCurve(
                        edges[edgeIndex].controlPoints,
                        4);
                    for (int sample = 0;
                         sample + 1 < samples.Length;
                         sample++)
                    {
                        nearest = Mathf.Min(
                            nearest,
                            DistancePointToSegment(
                                point,
                                samples[sample],
                                samples[sample + 1]));
                    }
                }
                if (nearest > 45f)
                    values[index] = CityZoneType.MixedUse;
            }

            InsertIndustrialBuffer(
                values,
                positions,
                20f);
            RemoveSmallZoneIslands(grid, values, 4);
            grid.Encode(values);
        }

        static Vector2 FindGateDirection(
            CityPlanData plan,
            Vector2 fallback)
        {
            CityPlanningAnchorData[] gates =
                (plan.anchors ?? Array.Empty<CityPlanningAnchorData>())
                .Where(anchor => anchor != null
                    && anchor.type
                    == CityPlanningAnchorType.CityGate)
                .ToArray();
            if (gates.Length < 2)
                return fallback;
            Vector2 best = fallback;
            float bestDistance = 0f;
            for (int a = 0; a < gates.Length; a++)
            {
                for (int b = a + 1; b < gates.Length; b++)
                {
                    Vector2 delta =
                        gates[b].position - gates[a].position;
                    if (delta.sqrMagnitude > bestDistance)
                    {
                        bestDistance = delta.sqrMagnitude;
                        best = delta.normalized;
                    }
                }
            }
            return best.sqrMagnitude > 0.0001f
                ? best
                : fallback;
        }

        static string RoadPatternId(int patternIndex)
        {
            switch (patternIndex)
            {
                case 0: return "long-axis-grid";
                case 1: return "gate-cbd-grid";
                case 2: return "spine-ladder";
                case 3: return "perimeter-cross";
                case 4: return "warped-grid";
                case 5: return "concave-skeleton";
                default: return "automatic";
            }
        }

        CitySemanticPointData[] BuildSemanticPoints(
            CityPlanData plan,
            IReadOnlyList<CityRoadEdgeData> edges,
            IReadOnlyList<CityPlannedBlockData> blocks)
        {
            var values = new List<CitySemanticPointData>();
            for (int i = 0; i < edges.Count; i++)
            {
                CityRoadEdgeData edge = edges[i];
                Vector2[] points = SampleCurve(
                    edge.controlPoints,
                    4);
                for (int p = 0; p + 1 < points.Length; p++)
                {
                    Vector2 start = points[p];
                    Vector2 end = points[p + 1];
                    Vector2 direction = (end - start).normalized;
                    float length = Vector2.Distance(start, end);
                    int samples = Mathf.Max(
                        1,
                        Mathf.FloorToInt(length / 20f));
                    for (int sample = 0; sample <= samples; sample++)
                    {
                        Vector2 position = Vector2.Lerp(
                            start,
                            end,
                            sample / (float)samples);
                        values.Add(new CitySemanticPointData
                        {
                            stableId = StableId(
                                plan.seed,
                                "lane-point",
                                edge.stableId,
                                values.Count),
                            type = CitySemanticPointType.VehicleLane,
                            position = position,
                            forward = direction,
                            roadId = edge.stableId
                        });
                        if (sample > 0
                            && sample < samples)
                        {
                            float side =
                                (Mathf.Abs(StableHash(
                                    plan.seed,
                                    edge.stableId,
                                    sample + p * 31)) & 1) == 0
                                    ? 1f
                                    : -1f;
                            values.Add(new CitySemanticPointData
                            {
                                stableId = StableId(
                                    plan.seed,
                                    "light-point",
                                    edge.stableId,
                                    values.Count),
                                type = CitySemanticPointType.StreetLight,
                                position = position
                                    + new Vector2(
                                        -direction.y,
                                        direction.x)
                                    * side
                                    * (edge.width * 0.5f
                                       + SidewalkWidth * 0.5f),
                                forward = direction,
                                roadId = edge.stableId
                            });
                        }
                    }
                }
                if (edge.hierarchy == CityRoadHierarchy.Arterial
                    && (i % 3) == 0
                    && points.Length >= 2)
                {
                    Vector2 direction =
                        (points[points.Length - 1] - points[0]).normalized;
                    values.Add(new CitySemanticPointData
                    {
                        stableId = StableId(
                            plan.seed,
                            "bus-stop",
                            edge.stableId,
                            values.Count),
                        type = CitySemanticPointType.BusStop,
                        position = points[points.Length / 2]
                            + new Vector2(-direction.y, direction.x)
                            * (edge.width * 0.5f + SidewalkWidth * 0.7f),
                        forward = direction,
                        roadId = edge.stableId
                    });
                }
            }
            for (int i = 0; i < blocks.Count; i++)
            {
                CityPlannedBlockData block = blocks[i];
                Vector2 center = Average(block.footprint);
                if (block.zoneType == CityZoneType.Park)
                {
                    for (int tree = 0; tree < 3; tree++)
                    {
                        Vector2 position = tree == 0
                            ? center
                            : Vector2.Lerp(
                                center,
                                block.footprint[
                                    (tree * 2) % block.footprint.Length],
                                0.38f);
                        values.Add(new CitySemanticPointData
                        {
                            stableId = StableId(
                                plan.seed,
                                "park-tree",
                                block.stableId,
                                tree),
                            type = CitySemanticPointType.Tree,
                            position = position
                        });
                    }
                    for (int bench = 0; bench < 2; bench++)
                    {
                        Vector2 first =
                            block.footprint[
                                bench % block.footprint.Length];
                        Vector2 second =
                            block.footprint[
                                (bench + 1) % block.footprint.Length];
                        values.Add(new CitySemanticPointData
                        {
                            stableId = StableId(
                                plan.seed,
                                "park-bench",
                                block.stableId,
                                bench),
                            type = CitySemanticPointType.Bench,
                            position = Vector2.Lerp(
                                (first + second) * 0.5f,
                                center,
                                0.2f),
                            forward = (second - first).normalized
                        });
                    }
                    continue;
                }
                if ((block.zoneType == CityZoneType.Commercial
                     || block.zoneType == CityZoneType.MixedUse)
                    && block.footprint.Length >= 2
                    && (i % 2) == 0)
                {
                    Vector2 first = block.footprint[0];
                    Vector2 second = block.footprint[1];
                    values.Add(new CitySemanticPointData
                    {
                        stableId = StableId(
                            plan.seed,
                            "street-bench",
                            block.stableId,
                            i),
                        type = CitySemanticPointType.Bench,
                        position = Vector2.Lerp(
                            (first + second) * 0.5f,
                            center,
                            0.14f),
                        forward = (second - first).normalized
                    });
                }
            }
            return values.ToArray();
        }

        static void AddEdge(
            CityPlanData plan,
            int candidateIndex,
            CityRoadNodeData start,
            CityRoadNodeData end,
            CityRoadHierarchy hierarchy,
            Vector2[] controls,
            List<CityRoadEdgeData> edges)
        {
            float width = RoadGridSize;
            edges.Add(new CityRoadEdgeData
            {
                stableId = StableId(
                    plan.seed,
                    "road-edge-" + candidateIndex,
                    start.stableId + end.stableId,
                    edges.Count),
                startNodeId = start.stableId,
                endNodeId = end.stableId,
                hierarchy = hierarchy,
                width = width,
                controlPoints = controls,
                hasVehicleLanes = true
            });
        }

        static List<PlanningRoadSegment> FlattenRoadEdges(
            IReadOnlyList<CityRoadEdgeData> edges)
        {
            var result = new List<PlanningRoadSegment>();
            for (int i = 0; i < edges.Count; i++)
            {
                CityRoadEdgeData edge = edges[i];
                Vector2[] points = SampleCurve(
                    edge.controlPoints,
                    edge.controlPoints != null
                    && edge.controlPoints.Length == 4
                        ? 8
                        : 1);
                for (int point = 0;
                     point + 1 < points.Length;
                     point++)
                {
                    if (Vector2.Distance(
                            points[point],
                            points[point + 1]) < 0.05f)
                    {
                        continue;
                    }
                    result.Add(new PlanningRoadSegment(
                        edge.stableId,
                        new CityRoadSegment(
                            points[point],
                            points[point + 1],
                            edge.width,
                            edge.hierarchy
                                != CityRoadHierarchy.Local)));
                }
            }
            return result;
        }

        static Vector2[] SampleCurve(
            IReadOnlyList<Vector2> controls,
            int subdivisions)
        {
            if (controls == null || controls.Count < 2)
                return Array.Empty<Vector2>();
            if (controls.Count != 4)
                return controls.ToArray();
            subdivisions = Mathf.Max(1, subdivisions);
            var points = new Vector2[subdivisions + 1];
            for (int i = 0; i <= subdivisions; i++)
            {
                float t = i / (float)subdivisions;
                float inverse = 1f - t;
                points[i] =
                    inverse * inverse * inverse * controls[0]
                    + 3f * inverse * inverse * t * controls[1]
                    + 3f * inverse * t * t * controls[2]
                    + t * t * t * controls[3];
            }
            return points;
        }

        static void RebuildNodeConnections(
            IReadOnlyList<CityRoadNodeData> nodes,
            IReadOnlyList<CityRoadEdgeData> edges)
        {
            var values = new Dictionary<string, List<string>>();
            for (int i = 0; i < nodes.Count; i++)
            {
                values[nodes[i].stableId] = new List<string>();
            }
            for (int i = 0; i < edges.Count; i++)
            {
                CityRoadEdgeData edge = edges[i];
                if (values.TryGetValue(
                        edge.startNodeId,
                        out List<string> start))
                {
                    start.Add(edge.stableId);
                }
                if (values.TryGetValue(
                        edge.endNodeId,
                        out List<string> end))
                {
                    end.Add(edge.stableId);
                }
            }
            for (int i = 0; i < nodes.Count; i++)
                nodes[i].connectedEdgeIds = values[nodes[i].stableId].ToArray();
        }

        static int CountRoadComponents(
            IReadOnlyList<CityRoadNodeData> nodes,
            IReadOnlyList<CityRoadEdgeData> edges)
        {
            if (nodes == null || nodes.Count == 0)
                return 0;
            var adjacency =
                new Dictionary<string, List<string>>();
            for (int i = 0; i < nodes.Count; i++)
                adjacency[nodes[i].stableId] = new List<string>();
            for (int i = 0; i < edges.Count; i++)
            {
                CityRoadEdgeData edge = edges[i];
                if (!adjacency.ContainsKey(edge.startNodeId)
                    || !adjacency.ContainsKey(edge.endNodeId))
                {
                    continue;
                }
                adjacency[edge.startNodeId].Add(edge.endNodeId);
                adjacency[edge.endNodeId].Add(edge.startNodeId);
            }
            var visited = new HashSet<string>();
            int components = 0;
            foreach (string node in adjacency.Keys)
            {
                if (!visited.Add(node))
                    continue;
                components++;
                var queue = new Queue<string>();
                queue.Enqueue(node);
                while (queue.Count > 0)
                {
                    string current = queue.Dequeue();
                    List<string> neighbors = adjacency[current];
                    for (int i = 0; i < neighbors.Count; i++)
                    {
                        if (visited.Add(neighbors[i]))
                            queue.Enqueue(neighbors[i]);
                    }
                }
            }
            return components;
        }

        static bool CurveInsideBoundary(
            IReadOnlyList<Vector2> boundary,
            IReadOnlyList<Vector2> controls)
        {
            Vector2[] samples = SampleCurve(controls, 12);
            for (int i = 0; i < samples.Length; i++)
            {
                if (!CityPolygonGeometry.ContainsPoint(
                        boundary,
                        samples[i]))
                {
                    return false;
                }
            }
            return true;
        }

        static bool ModularSegmentInsideBoundary(
            IReadOnlyList<Vector2> boundary,
            Vector2 start,
            Vector2 end,
            float clearance)
        {
            float distance = Vector2.Distance(start, end);
            int samples = Mathf.Max(
                1,
                Mathf.CeilToInt(distance / (RoadGridSize * 0.5f)));
            for (int sample = 0; sample <= samples; sample++)
            {
                Vector2 point = Vector2.Lerp(
                    start,
                    end,
                    sample / (float)samples);
                if (!HasBoundaryClearance(
                        boundary,
                        point,
                        clearance))
                {
                    return false;
                }
            }
            return true;
        }

        static bool OverlapsAnyRoad(
            IReadOnlyList<Vector2> polygon,
            IReadOnlyList<PlanningRoadSegment> roads)
        {
            for (int i = 0; i < roads.Count; i++)
            {
                CityRoadSegment road = roads[i].Segment;
                if (SegmentIntersectsPolygon(
                        road.Start,
                        road.End,
                        polygon)
                    || CityPolygonGeometry.ContainsPoint(
                        polygon,
                        road.Start)
                    || CityPolygonGeometry.ContainsPoint(
                        polygon,
                        road.End))
                {
                    return true;
                }
            }
            return false;
        }

        static bool SegmentIntersectsPolygon(
            Vector2 start,
            Vector2 end,
            IReadOnlyList<Vector2> polygon)
        {
            for (int i = 0; i < polygon.Count; i++)
            {
                if (SegmentsIntersect(
                        start,
                        end,
                        polygon[i],
                        polygon[(i + 1) % polygon.Count]))
                {
                    return true;
                }
            }
            return false;
        }

        static bool SegmentsIntersect(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d)
        {
            float o1 = Cross(b - a, c - a);
            float o2 = Cross(b - a, d - a);
            float o3 = Cross(d - c, a - c);
            float o4 = Cross(d - c, b - c);
            return o1 * o2 < -0.000001f
                && o3 * o4 < -0.000001f;
        }

        static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        static Vector2[] InsetRectangle(
            IReadOnlyList<Vector2> source,
            float inset)
        {
            if (source == null || source.Count != 4)
                return null;
            Vector2 center = Average(source);
            var result = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                Vector2 delta = source[i] - center;
                float length = delta.magnitude;
                if (length <= inset * 1.5f)
                    return null;
                result[i] = center
                    + delta * Mathf.Clamp01(
                        (length - inset) / length);
            }
            return result;
        }

        static string[] FindFrontageRoads(
            IReadOnlyList<Vector2> footprint,
            IReadOnlyList<PlanningRoadSegment> roads)
        {
            var ids = new List<string>();
            GetBounds(
                footprint,
                out Vector2 minimum,
                out Vector2 maximum);
            for (int roadIndex = 0;
                 roadIndex < roads.Count;
                 roadIndex++)
            {
                PlanningRoadSegment planningRoad = roads[roadIndex];
                CityRoadSegment road = planningRoad.Segment;
                float maximumDistance =
                    road.Width * 0.5f + SidewalkWidth + 0.5f;
                if (Mathf.Max(road.Start.x, road.End.x)
                        < minimum.x - maximumDistance
                    || Mathf.Min(road.Start.x, road.End.x)
                        > maximum.x + maximumDistance
                    || Mathf.Max(road.Start.y, road.End.y)
                        < minimum.y - maximumDistance
                    || Mathf.Min(road.Start.y, road.End.y)
                        > maximum.y + maximumDistance)
                {
                    continue;
                }
                if (DistanceSegmentToPolygon(
                        road.Start,
                        road.End,
                        footprint)
                    <= maximumDistance)
                {
                    if (!ids.Contains(planningRoad.StableId))
                        ids.Add(planningRoad.StableId);
                }
            }
            return ids.ToArray();
        }

        static float DistanceSegmentToPolygon(
            Vector2 start,
            Vector2 end,
            IReadOnlyList<Vector2> polygon)
        {
            if (SegmentIntersectsPolygon(start, end, polygon)
                || CityPolygonGeometry.ContainsPoint(polygon, start)
                || CityPolygonGeometry.ContainsPoint(polygon, end))
            {
                return 0f;
            }

            float best = float.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 first = polygon[i];
                Vector2 second = polygon[(i + 1) % polygon.Count];
                float distance = Mathf.Min(
                    DistancePointToSegment(first, start, end),
                    Mathf.Min(
                        DistancePointToSegment(start, first, second),
                        DistancePointToSegment(end, first, second)));
                best = Mathf.Min(best, distance);
            }
            return best;
        }

        static float DistanceToPolygon(
            Vector2 point,
            IReadOnlyList<Vector2> polygon)
        {
            float best = float.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                best = Mathf.Min(
                    best,
                    DistancePointToSegment(
                        point,
                        polygon[i],
                        polygon[(i + 1) % polygon.Count]));
            }
            return best;
        }

        static float DistancePointToSegment(
            Vector2 point,
            Vector2 start,
            Vector2 end)
        {
            Vector2 delta = end - start;
            float denominator = delta.sqrMagnitude;
            if (denominator <= 0.000001f)
                return Vector2.Distance(point, start);
            float t = Mathf.Clamp01(
                Vector2.Dot(point - start, delta) / denominator);
            return Vector2.Distance(
                point,
                Vector2.Lerp(start, end, t));
        }

        static void RemoveSmallZoneIslands(
            CityZonePaintGrid grid,
            CityZoneType[] values,
            int minimumCells)
        {
            var visited = new bool[values.Length];
            for (int index = 0; index < values.Length; index++)
            {
                CityZoneType type = values[index];
                if (visited[index]
                    || type == CityZoneType.Unassigned)
                {
                    continue;
                }
                List<int> component = FloodComponent(
                    grid,
                    values,
                    visited,
                    index,
                    type);
                if (component.Count >= minimumCells)
                    continue;
                CityZoneType replacement = FindNeighborZone(
                    grid,
                    values,
                    component,
                    type);
                for (int i = 0; i < component.Count; i++)
                    values[component[i]] = replacement;
            }
        }

        static List<int> FloodComponent(
            CityZonePaintGrid grid,
            IReadOnlyList<CityZoneType> values,
            bool[] visited,
            int start,
            CityZoneType type)
        {
            var result = new List<int>();
            var queue = new Queue<int>();
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                result.Add(current);
                int x = current % grid.width;
                int y = current / grid.width;
                Visit(x - 1, y);
                Visit(x + 1, y);
                Visit(x, y - 1);
                Visit(x, y + 1);

                void Visit(int nextX, int nextY)
                {
                    if (nextX < 0
                        || nextY < 0
                        || nextX >= grid.width
                        || nextY >= grid.height)
                    {
                        return;
                    }
                    int next = nextY * grid.width + nextX;
                    if (visited[next] || values[next] != type)
                        return;
                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }
            return result;
        }

        static CityZoneType FindNeighborZone(
            CityZonePaintGrid grid,
            IReadOnlyList<CityZoneType> values,
            IReadOnlyList<int> component,
            CityZoneType fallback)
        {
            var counts = new Dictionary<CityZoneType, int>();
            for (int i = 0; i < component.Count; i++)
            {
                int index = component[i];
                int x = index % grid.width;
                int y = index / grid.width;
                Count(x - 1, y);
                Count(x + 1, y);
                Count(x, y - 1);
                Count(x, y + 1);
            }
            CityZoneType best = fallback;
            int bestCount = 0;
            foreach (KeyValuePair<CityZoneType, int> pair in counts)
            {
                if (pair.Value > bestCount)
                {
                    best = pair.Key;
                    bestCount = pair.Value;
                }
            }
            return best;

            void Count(int nextX, int nextY)
            {
                if (nextX < 0
                    || nextY < 0
                    || nextX >= grid.width
                    || nextY >= grid.height)
                {
                    return;
                }
                CityZoneType type =
                    values[nextY * grid.width + nextX];
                if (type == CityZoneType.Unassigned
                    || type == fallback)
                {
                    return;
                }
                counts[type] = counts.TryGetValue(
                    type,
                    out int count)
                    ? count + 1
                    : 1;
            }
        }

        static void GetComponentBounds(
            CityZonePaintGrid grid,
            IReadOnlyList<int> component,
            out Vector2 minimum,
            out Vector2 maximum)
        {
            minimum = new Vector2(float.MaxValue, float.MaxValue);
            maximum = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < component.Count; i++)
            {
                int index = component[i];
                int x = index % grid.width;
                int y = index / grid.width;
                Vector2 first = grid.origin + new Vector2(
                    x * grid.cellSize,
                    y * grid.cellSize);
                Vector2 last = first + Vector2.one * grid.cellSize;
                minimum = Vector2.Min(minimum, first);
                maximum = Vector2.Max(maximum, last);
            }
        }

        static CityZoneType ChooseAutomaticZone(
            CityPlanData plan,
            Vector2 point)
        {
            CityPlanningAnchorData nearest = null;
            float nearestNormalized = float.MaxValue;
            CityPlanningAnchorData[] anchors =
                plan.anchors ?? Array.Empty<CityPlanningAnchorData>();
            for (int i = 0; i < anchors.Length; i++)
            {
                CityPlanningAnchorData anchor = anchors[i];
                if (anchor == null)
                    continue;
                float normalized = Vector2.Distance(
                    point,
                    anchor.position)
                    / Mathf.Max(1f, anchor.influenceRadius);
                if (normalized < nearestNormalized)
                {
                    nearest = anchor;
                    nearestNormalized = normalized;
                }
            }
            if (nearest != null && nearestNormalized <= 1f)
            {
                switch (nearest.type)
                {
                    case CityPlanningAnchorType.Cbd:
                        return nearestNormalized < 0.42f
                            ? CityZoneType.Commercial
                            : CityZoneType.MixedUse;
                    case CityPlanningAnchorType.IndustrialHub:
                        return CityZoneType.Industrial;
                    case CityPlanningAnchorType.Park:
                        return CityZoneType.Park;
                    case CityPlanningAnchorType.CivicCenter:
                        return CityZoneType.Civic;
                    case CityPlanningAnchorType.TransitHub:
                        return CityZoneType.MixedUse;
                }
            }
            Vector2 center = CityPolygonGeometry.Centroid(
                plan.boundary);
            float span = Mathf.Max(1f, PolygonSpan(plan.boundary));
            float centerDistance =
                Vector2.Distance(point, center) / span;
            return centerDistance < 0.18f
                ? CityZoneType.MixedUse
                : CityZoneType.Residential;
        }

        static CityZoneType SampleZone(
            CityPlanData plan,
            Vector2 point)
        {
            CityZonePaintGrid grid = plan.zonePaintGrid;
            if (grid == null || grid.CellCount == 0)
                return CityZoneType.Unassigned;
            int x = Mathf.FloorToInt(
                (point.x - grid.origin.x) / grid.cellSize);
            int y = Mathf.FloorToInt(
                (point.y - grid.origin.y) / grid.cellSize);
            if (x < 0 || y < 0 || x >= grid.width || y >= grid.height)
                return CityZoneType.Unassigned;
            CityZoneType[] values = grid.Decode();
            return values[y * grid.width + x];
        }

        static List<float> BuildAdaptiveGridLines(
            float minimum,
            float maximum,
            float center,
            float baseSpacing,
            int seed,
            int candidateIndex,
            int axisIndex)
        {
            var values = new List<float> { center };
            AddDirection(-1);
            AddDirection(1);
            AddBoundaryRoad(
                Mathf.Ceil(
                    (minimum + RoadGridSize * 0.5f)
                    / RoadGridSize)
                * RoadGridSize);
            AddBoundaryRoad(
                Mathf.Floor(
                    (maximum - RoadGridSize * 0.5f)
                    / RoadGridSize)
                * RoadGridSize);
            values.Sort();
            return values;

            void AddBoundaryRoad(float value)
            {
                if (value <= minimum
                    || value >= maximum
                    || values.Any(existing =>
                        Mathf.Abs(existing - value)
                        < RoadGridSize * 3f))
                {
                    return;
                }
                values.Add(value);
            }

            void AddDirection(int direction)
            {
                float current = center;
                int ordinal = 0;
                while (ordinal < 64)
                {
                    float distance = Mathf.Abs(current - center);
                    float extent = direction < 0
                        ? center - minimum
                        : maximum - center;
                    float normalizedDistance = Mathf.Clamp01(
                        distance / Mathf.Max(1f, extent));
                    float scale = Mathf.Lerp(
                        0.9f,
                        1.9f,
                        normalizedDistance);
                    int variation = Mathf.Abs(StableHash(
                        seed,
                        "block-spacing-"
                            + candidateIndex
                            + "-"
                            + axisIndex
                            + "-"
                            + direction,
                        ordinal)) % 3 - 1;
                    float step = Mathf.Clamp(
                        Mathf.Round(
                            (baseSpacing * scale
                             + variation * RoadGridSize)
                            / RoadGridSize)
                        * RoadGridSize,
                        40f,
                        90f);
                    float next = current + direction * step;
                    if (next <= minimum + 12f
                        || next >= maximum - 12f)
                    {
                        break;
                    }
                    values.Add(next);
                    current = next;
                    ordinal++;
                }
            }
        }

        static List<float> SnapGridLines(
            IReadOnlyList<float> source,
            float minimum,
            float maximum)
        {
            return source
                .Select(value =>
                    Mathf.Round(value / RoadGridSize) * RoadGridSize)
                .Where(value =>
                    value >= minimum - 0.001f
                    && value <= maximum + 0.001f)
                .Distinct()
                .OrderBy(value => value)
                .ToList();
        }

        static bool HasBoundaryClearance(
            IReadOnlyList<Vector2> boundary,
            Vector2 point,
            float clearance)
        {
            return CityPolygonGeometry.ContainsPoint(boundary, point)
                && DistanceToPolygon(point, boundary) >= clearance;
        }

        static Vector2 SnapWorld(
            Vector2 world,
            PlanningBasis basis)
        {
            Vector2 local = basis.ToLocal(world);
            local.x = Mathf.Round(local.x / RoadGridSize)
                * RoadGridSize;
            local.y = Mathf.Round(local.y / RoadGridSize)
                * RoadGridSize;
            return basis.ToWorld(local);
        }

        static Vector2 FindPrimaryDirection(
            IReadOnlyList<Vector2> polygon)
        {
            Vector2 best = Vector2.right;
            float bestLength = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 edge =
                    polygon[(i + 1) % polygon.Count] - polygon[i];
                if (edge.sqrMagnitude > bestLength)
                {
                    best = edge.normalized;
                    bestLength = edge.sqrMagnitude;
                }
            }
            return best;
        }

        static Vector2 FindAnchor(
            CityPlanData plan,
            CityPlanningAnchorType type,
            Vector2 fallback)
        {
            CityPlanningAnchorData[] anchors =
                plan.anchors ?? Array.Empty<CityPlanningAnchorData>();
            for (int i = 0; i < anchors.Length; i++)
            {
                if (anchors[i] != null && anchors[i].type == type)
                    return anchors[i].position;
            }
            return fallback;
        }

        static float ZoneDensity(CityZoneType type)
        {
            switch (type)
            {
                case CityZoneType.Commercial: return 0.95f;
                case CityZoneType.MixedUse: return 0.86f;
                case CityZoneType.Residential: return 0.72f;
                case CityZoneType.Industrial: return 0.55f;
                case CityZoneType.Civic: return 0.58f;
                case CityZoneType.Park: return 0.08f;
                default: return 0.5f;
            }
        }

        static float ZoneCoverage(CityZoneType type)
        {
            switch (type)
            {
                case CityZoneType.Commercial: return 0.82f;
                case CityZoneType.MixedUse: return 0.74f;
                case CityZoneType.Residential: return 0.62f;
                case CityZoneType.Industrial: return 0.68f;
                case CityZoneType.Civic: return 0.58f;
                default: return 0f;
            }
        }

        static Vector2 ZoneHeightRange(CityZoneType type)
        {
            switch (type)
            {
                case CityZoneType.Commercial:
                    return new Vector2(30f, 72f);
                case CityZoneType.MixedUse:
                    return new Vector2(20f, 48f);
                case CityZoneType.Residential:
                    return new Vector2(10f, 30f);
                case CityZoneType.Industrial:
                    return new Vector2(8f, 22f);
                case CityZoneType.Civic:
                    return new Vector2(12f, 32f);
                default:
                    return new Vector2(0f, 0f);
            }
        }

        static float ZoneTargetBlockArea(CityZoneType type)
        {
            switch (type)
            {
                case CityZoneType.Commercial: return 1500f;
                case CityZoneType.MixedUse: return 1800f;
                case CityZoneType.Residential: return 2100f;
                case CityZoneType.Industrial: return 3600f;
                case CityZoneType.Civic: return 2400f;
                default: return 1800f;
            }
        }

        static float ZoneHeight(
            CityZoneType type,
            CityPlanData plan,
            Vector2 position,
            string stableId)
        {
            Vector2 range = ZoneHeightRange(type);
            Vector2 cbd = FindAnchor(
                plan,
                CityPlanningAnchorType.Cbd,
                CityPolygonGeometry.Centroid(plan.boundary));
            float distanceFactor = 1f - Mathf.Clamp01(
                Vector2.Distance(position, cbd)
                / Mathf.Max(1f, PolygonSpan(plan.boundary) * 0.45f));
            float noise = (StableHash(
                plan.seed,
                stableId,
                31) & 1023) / 1023f;
            float factor = Mathf.Clamp01(
                distanceFactor * 0.65f + noise * 0.35f);
            return Mathf.Lerp(range.x, range.y, factor);
        }

        static int TargetBuildingCount(
            IReadOnlyList<Vector2> boundary)
        {
            float area = Mathf.Abs(
                CityPolygonGeometry.SignedArea(boundary));
            return Mathf.Clamp(
                Mathf.RoundToInt(area * 0.65f / 420f),
                30,
                500);
        }

        static int CountIndustrialResidentialContacts(
            CityZonePaintGrid grid)
        {
            if (grid == null || grid.CellCount == 0)
                return 0;
            CityZoneType[] values = grid.Decode();
            int contacts = 0;
            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] != CityZoneType.Industrial)
                    continue;
                int x = index % grid.width;
                int y = index / grid.width;
                Count(x - 1, y);
                Count(x + 1, y);
                Count(x, y - 1);
                Count(x, y + 1);

                void Count(int nextX, int nextY)
                {
                    if (nextX < 0
                        || nextY < 0
                        || nextX >= grid.width
                        || nextY >= grid.height)
                    {
                        return;
                    }
                    if (values[nextY * grid.width + nextX]
                        == CityZoneType.Residential)
                    {
                        contacts++;
                    }
                }
            }
            return contacts;
        }

        static float ScoreConnectivity(
            CityGenerationOutput output)
        {
            if (output == null
                || output.roadNodes == null
                || output.roadEdges == null
                || output.roadNodes.Length == 0)
            {
                return 0f;
            }
            if (CountRoadComponents(
                    output.roadNodes,
                    output.roadEdges) != 1)
            {
                return 0f;
            }
            int cycleRank = Mathf.Max(
                0,
                output.roadEdges.Length
                - output.roadNodes.Length + 1);
            float redundancy = Mathf.Clamp01(
                cycleRank
                / (float)Mathf.Max(
                    1,
                    output.roadNodes.Length / 20));
            return 0.7f + redundancy * 0.3f;
        }

        static float ScoreZoneCompatibility(CityPlanData plan)
        {
            CityZonePaintGrid grid = plan.zonePaintGrid;
            if (grid == null || grid.CellCount == 0)
                return 0f;
            CityZoneType[] values = grid.Decode();
            int assigned = values.Count(value =>
                value != CityZoneType.Unassigned);
            if (assigned == 0)
                return 0f;
            var targets = new Dictionary<CityZoneType, float>
            {
                { CityZoneType.Commercial, 0.12f },
                { CityZoneType.MixedUse, 0.23f },
                { CityZoneType.Residential, 0.40f },
                { CityZoneType.Industrial, 0.10f },
                { CityZoneType.Civic, 0.05f },
                { CityZoneType.Park, 0.10f }
            };
            float difference = 0f;
            foreach (KeyValuePair<CityZoneType, float> target
                     in targets)
            {
                float actual = values.Count(value =>
                    value == target.Key) / (float)assigned;
                difference += Mathf.Abs(actual - target.Value);
            }
            float ratioScore = 1f - Mathf.Clamp01(
                difference / 0.45f);
            float bufferScore =
                CountIndustrialResidentialContacts(grid) == 0
                    ? 1f
                    : 0f;
            return ratioScore * 0.75f + bufferScore * 0.25f;
        }

        static float ScoreVisualQuality(
            CityGenerationOutput output)
        {
            float roads = ScoreRoadQuality(output.roadEdges);
            CityPlannedBuildingData[] buildings =
                output.buildings
                ?? Array.Empty<CityPlannedBuildingData>();
            if (buildings.Length == 0)
                return roads * 0.6f;
            int repeats = 0;
            for (int i = 0; i < buildings.Length; i++)
            {
                int first = Mathf.Max(0, i - 3);
                for (int previous = first;
                     previous < i;
                     previous++)
                {
                    if (buildings[previous].catalogId
                        == buildings[i].catalogId)
                    {
                        repeats++;
                        break;
                    }
                }
            }
            float variety = 1f - repeats
                / (float)Mathf.Max(1, buildings.Length);
            return roads * 0.6f + variety * 0.4f;
        }

        static float ScoreZoneCoverage(CityPlanData plan)
        {
            CityZonePaintGrid grid = plan.zonePaintGrid;
            if (grid == null || grid.CellCount == 0)
                return 0f;
            CityZoneType[] values = grid.Decode();
            int inside = 0;
            int assigned = 0;
            for (int i = 0; i < values.Length; i++)
            {
                int x = i % grid.width;
                int y = i / grid.width;
                Vector2 point = grid.origin + new Vector2(
                    (x + 0.5f) * grid.cellSize,
                    (y + 0.5f) * grid.cellSize);
                if (!CityPolygonGeometry.ContainsPoint(
                        plan.boundary,
                        point))
                {
                    continue;
                }
                inside++;
                if (values[i] != CityZoneType.Unassigned)
                    assigned++;
            }
            return inside == 0 ? 0f : assigned / (float)inside;
        }

        static float ScoreBlockQuality(
            IReadOnlyList<CityPlannedBlockData> blocks)
        {
            if (blocks == null || blocks.Count == 0)
                return 0f;
            int valid = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                float area = blocks[i].area;
                if (area >= 300f && area <= 5000f)
                    valid++;
            }
            return valid / (float)blocks.Count;
        }

        static float ScoreSkyline(
            IReadOnlyList<CityPlannedBuildingData> buildings)
        {
            if (buildings == null || buildings.Count == 0)
                return 0f;
            float commercial = 0f;
            int commercialCount = 0;
            float other = 0f;
            int otherCount = 0;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i].zoneType == CityZoneType.Commercial)
                {
                    commercial += buildings[i].height;
                    commercialCount++;
                }
                else
                {
                    other += buildings[i].height;
                    otherCount++;
                }
            }
            if (commercialCount == 0)
                return 0.5f;
            float commercialAverage = commercial / commercialCount;
            float otherAverage = otherCount > 0
                ? other / otherCount
                : 0f;
            return Mathf.Clamp01(
                0.5f
                + (commercialAverage - otherAverage) / 50f);
        }

        static float ScoreAnchorAccess(
            CityPlanData plan,
            CityGenerationOutput output)
        {
            if (output?.roadEdges == null)
                return 0f;
            CityPlanningAnchorData[] anchors =
                plan.anchors ?? Array.Empty<CityPlanningAnchorData>();
            float total = 0f;
            int required = 0;
            for (int i = 0; i < anchors.Length; i++)
            {
                CityPlanningAnchorData anchor = anchors[i];
                if (anchor == null
                    || anchor.type == CityPlanningAnchorType.Park
                    || anchor.type == CityPlanningAnchorType.Landmark)
                {
                    continue;
                }
                required++;
                float nearest = float.MaxValue;
                for (int edgeIndex = 0;
                     edgeIndex < output.roadEdges.Length;
                     edgeIndex++)
                {
                    CityRoadEdgeData edge =
                        output.roadEdges[edgeIndex];
                    if (edge.hierarchy
                        != CityRoadHierarchy.Arterial)
                    {
                        continue;
                    }
                    Vector2[] samples = SampleCurve(
                        edge.controlPoints,
                        6);
                    for (int sample = 0;
                         sample + 1 < samples.Length;
                         sample++)
                    {
                        nearest = Mathf.Min(
                            nearest,
                            DistancePointToSegment(
                                anchor.position,
                                samples[sample],
                                samples[sample + 1]));
                    }
                }
                total += 1f - Mathf.Clamp01(nearest / 20f);
            }
            if (required == 0)
                return 0f;
            return total / required;
        }

        static float ScorePublicSpace(CityPlanData plan)
        {
            CityZonePaintGrid grid = plan.zonePaintGrid;
            if (grid == null)
                return 0f;
            CityZoneType[] values = grid.Decode();
            int assigned = 0;
            int parks = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == CityZoneType.Unassigned)
                    continue;
                assigned++;
                if (values[i] == CityZoneType.Park)
                    parks++;
            }
            if (assigned == 0)
                return 0f;
            float ratio = parks / (float)assigned;
            return 1f - Mathf.Clamp01(
                Mathf.Abs(ratio - 0.1f) / 0.1f);
        }

        static float ScoreRoadQuality(
            IReadOnlyList<CityRoadEdgeData> edges)
        {
            if (edges == null || edges.Count == 0)
                return 0f;
            int valid = 0;
            for (int i = 0; i < edges.Count; i++)
            {
                Vector2[] points = SampleCurve(
                    edges[i].controlPoints,
                    4);
                float length = 0f;
                for (int p = 0; p + 1 < points.Length; p++)
                    length += Vector2.Distance(points[p], points[p + 1]);
                if (length >= 8f)
                    valid++;
            }
            return valid / (float)edges.Count;
        }

        static string ComputeLayoutChecksum(
            CityGenerationOutput output)
        {
            unchecked
            {
                uint hash = 2166136261u;
                Mix(output.roadEdges?.Length ?? 0);
                Mix(output.blocks?.Length ?? 0);
                Mix(output.lots?.Length ?? 0);
                Mix(output.buildings?.Length ?? 0);
                if (output.roadNodes != null)
                {
                    for (int i = 0; i < output.roadNodes.Length; i++)
                    {
                        Mix(Mathf.RoundToInt(
                            output.roadNodes[i].position.x * 100f));
                        Mix(Mathf.RoundToInt(
                            output.roadNodes[i].position.y * 100f));
                    }
                }
                return hash.ToString("X8");

                void Mix(int value)
                {
                    hash ^= (uint)value;
                    hash *= 16777619u;
                }
            }
        }

        static int ParseCatalogIndex(string catalogId)
        {
            if (string.IsNullOrEmpty(catalogId))
                return 0;
            int dash = catalogId.LastIndexOf('-');
            if (dash >= 0
                && int.TryParse(
                    catalogId.Substring(dash + 1),
                    out int value))
            {
                return Mathf.Max(0, value - 1);
            }
            return 0;
        }

        static string StableId(
            int seed,
            string stage,
            string parent,
            int localIndex)
        {
            return StableHash(
                    seed,
                    stage + "|" + parent,
                    localIndex)
                .ToString("X8");
        }

        static int StableHash(
            int seed,
            string text,
            int value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)seed) * 16777619u;
                if (text != null)
                {
                    for (int i = 0; i < text.Length; i++)
                        hash = (hash ^ text[i]) * 16777619u;
                }
                hash = (hash ^ (uint)value) * 16777619u;
                return (int)hash;
            }
        }

        static CityPreviewResult CreateFailedPreview(
            CityPlanData plan,
            string code,
            string message)
        {
            return new CityPreviewResult
            {
                Plan = plan?.Clone() ?? new CityPlanData(),
                Output = new CityGenerationOutput(),
                Validation = Report(
                    0f,
                    new List<CityPlanningIssue>
                    {
                        Error(code, message, Vector2.zero)
                    }),
                CandidateIndex = -1
            };
        }

        static CityPlanningIssue Error(
            string code,
            string message,
            Vector2 position,
            params string[] repairOptions)
        {
            return new CityPlanningIssue
            {
                code = code,
                severity = CityPlanningIssueSeverity.Error,
                message = message,
                position = position,
                repairOptions = repairOptions
                    ?? Array.Empty<string>()
            };
        }

        static CityPlanningIssue Warning(
            string code,
            string message,
            Vector2 position,
            params string[] repairOptions)
        {
            return new CityPlanningIssue
            {
                code = code,
                severity = CityPlanningIssueSeverity.Warning,
                message = message,
                position = position,
                repairOptions = repairOptions
                    ?? Array.Empty<string>()
            };
        }

        static CityValidationReport Report(
            float score,
            List<CityPlanningIssue> issues)
        {
            return new CityValidationReport
            {
                score = score,
                issues = issues?.ToArray()
                    ?? Array.Empty<CityPlanningIssue>()
            };
        }

        static string GridKey(int x, int y)
        {
            return x + ":" + y;
        }

        static float PolygonSpan(IReadOnlyList<Vector2> polygon)
        {
            GetBounds(
                polygon,
                out Vector2 minimum,
                out Vector2 maximum);
            return Mathf.Max(
                maximum.x - minimum.x,
                maximum.y - minimum.y);
        }

        static void GetBounds(
            IReadOnlyList<Vector2> values,
            out Vector2 minimum,
            out Vector2 maximum)
        {
            minimum = new Vector2(float.MaxValue, float.MaxValue);
            maximum = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < values.Count; i++)
            {
                minimum = Vector2.Min(minimum, values[i]);
                maximum = Vector2.Max(maximum, values[i]);
            }
        }

        static void GetLocalBounds(
            IReadOnlyList<Vector2> values,
            PlanningBasis basis,
            out Vector2 minimum,
            out Vector2 maximum)
        {
            minimum = new Vector2(float.MaxValue, float.MaxValue);
            maximum = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < values.Count; i++)
            {
                Vector2 point = basis.ToLocal(values[i]);
                minimum = Vector2.Min(minimum, point);
                maximum = Vector2.Max(maximum, point);
            }
        }

        static Vector2 Average(IReadOnlyList<Vector2> values)
        {
            if (values == null || values.Count == 0)
                return Vector2.zero;
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < values.Count; i++)
                sum += values[i];
            return sum / values.Count;
        }

        static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(
                value.x * cosine - value.y * sine,
                value.x * sine + value.y * cosine);
        }

        readonly struct PlanningBasis
        {
            public readonly Vector2 Origin;
            public readonly Vector2 AxisU;
            public readonly Vector2 AxisV;

            public PlanningBasis(
                Vector2 origin,
                Vector2 axisU,
                Vector2 axisV)
            {
                Origin = origin;
                AxisU = axisU.normalized;
                AxisV = axisV.normalized;
            }

            public Vector2 ToLocal(Vector2 world)
            {
                Vector2 delta = world - Origin;
                return new Vector2(
                    Vector2.Dot(delta, AxisU),
                    Vector2.Dot(delta, AxisV));
            }

            public Vector2 ToWorld(Vector2 local)
            {
                return Origin + AxisU * local.x + AxisV * local.y;
            }
        }

        readonly struct PlanningRoadSegment
        {
            public readonly string StableId;
            public readonly CityRoadSegment Segment;

            public PlanningRoadSegment(
                string stableId,
                CityRoadSegment segment)
            {
                StableId = stableId;
                Segment = segment;
            }
        }
    }
}
