#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityPlanet.CityPcg;
using UnityPlanet.EDPCG;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.EditorTools
{
    internal struct EdpcgSceneOverlayOptions
    {
        public bool enabled;
        public bool areas;
        public bool routes;
        public bool ingresses;
        public bool reservations;
        public bool pressureDirections;
        public bool tacticalArrows;
        public bool gridFire;
        public bool effectiveFiringEnvelope;
        public bool blockedFire;
        public bool evasions;
        public bool gapWindows;
        public bool finalBallistics;
        public EdpcgAirThreatViewMode threatViewMode;
        public EdpcgAirThreatRoleFilter threatRoleFilter;
        public int maximumThreatChannels;
        public bool buildings;
        public bool skybridges;
        public bool cables;
        public bool labels;
    }

    /// <summary>
    /// Editor-only, read-only visualization of the live ordinary-enemy city
    /// contract. It never creates scene objects, colliders or gameplay state.
    /// </summary>
    internal static class EdpcgSceneTacticalOverlay
    {
        static EditorWindow owner;
        static EdpcgEncounterRuntime runtime;
        static EdpcgCityTacticalRuntimeMap tacticalMap;
        static FinitePlanetUrbanCombatRuntime urbanRuntime;
        static AirCombatCityPcgLab cityLab;
        static EdpcgGridFireAnalysis gridFireAnalysis;
        static int selectedGridX;
        static int selectedGridZ;
        static Action<int, int> gridSelectionChanged;
        static EdpcgSceneOverlayOptions options;
        static bool subscribed;
        static GUIStyle areaLabelStyle;
        static GUIStyle smallLabelStyle;
        static GUIStyle gridLabelStyle;

        public static void Attach(EditorWindow target)
        {
            owner = target;
            if (subscribed)
                return;
            SceneView.duringSceneGui += Draw;
            subscribed = true;
        }

        public static void Detach(EditorWindow target)
        {
            if (owner != target)
                return;
            owner = null;
            runtime = null;
            tacticalMap = null;
            urbanRuntime = null;
            cityLab = null;
            gridFireAnalysis = null;
            gridSelectionChanged = null;
            if (subscribed)
                SceneView.duringSceneGui -= Draw;
            subscribed = false;
            SceneView.RepaintAll();
        }

        public static void Configure(
            EdpcgEncounterRuntime activeRuntime,
            EdpcgCityTacticalRuntimeMap fallbackMap,
            FinitePlanetUrbanCombatRuntime fallbackUrban,
            AirCombatCityPcgLab fallbackLab,
            EdpcgGridFireAnalysis fireAnalysis,
            int currentGridX,
            int currentGridZ,
            Action<int, int> selectionChanged,
            EdpcgSceneOverlayOptions currentOptions)
        {
            runtime = activeRuntime;
            tacticalMap = activeRuntime != null &&
                          activeRuntime.TacticalMap != null
                ? activeRuntime.TacticalMap
                : fallbackMap;
            urbanRuntime = activeRuntime != null &&
                           activeRuntime.UrbanRuntime != null
                ? activeRuntime.UrbanRuntime
                : fallbackUrban;
            cityLab = urbanRuntime == null ? fallbackLab : null;
            gridFireAnalysis = fireAnalysis;
            selectedGridX = Mathf.Clamp(currentGridX, 0, 7);
            selectedGridZ = Mathf.Clamp(currentGridZ, 0, 7);
            gridSelectionChanged = selectionChanged;
            options = currentOptions;
            if (currentOptions.enabled)
                SceneView.RepaintAll();
        }

        static void Draw(SceneView sceneView)
        {
            if (!options.enabled ||
                (tacticalMap == null && gridFireAnalysis == null))
                return;

            CompareFunction previousZ = Handles.zTest;
            Handles.zTest = CompareFunction.LessEqual;
            try
            {
                if (options.buildings || options.skybridges || options.cables)
                    DrawFinalGeometry();
                if (options.gridFire && gridFireAnalysis != null &&
                    gridFireAnalysis.IsUsable)
                {
                    HandleGridSelection(sceneView, gridFireAnalysis);
                    DrawGridFire(sceneView, gridFireAnalysis);
                }
                EdpcgCityTacticalRuntimeMap map = tacticalMap;
                if (map != null)
                {
                    if (options.areas)
                        DrawAreas(sceneView, map);
                    if (options.routes)
                        DrawRoutes(map);
                    if (options.tacticalArrows)
                        DrawTacticalArrows(map);
                    if (options.ingresses)
                        DrawIngresses(map);
                    if (options.reservations && runtime != null &&
                        runtime.IsRunning)
                        DrawReservations(runtime, map);
                }
                if (options.pressureDirections && runtime != null &&
                    runtime.IsRunning)
                    DrawPressureDirections(runtime);
            }
            finally
            {
                Handles.zTest = previousZ;
            }
        }

        static void DrawGridFire(
            SceneView sceneView,
            EdpcgGridFireAnalysis analysis)
        {
            EdpcgGridFireCell selected = null;
            for (int index = 0; index < analysis.cells.Count; index++)
            {
                EdpcgGridFireCell cell = analysis.cells[index];
                if (cell == null || cell.worldCorners == null ||
                    cell.worldCorners.Length != 4)
                {
                    continue;
                }
                EdpcgGridDifficultyBreakdown difficulty =
                    EvaluateDifficulty(cell);
                Color fill = FireCellColor(cell, difficulty);
                Color outline = cell.gridX == selectedGridX &&
                                cell.gridZ == selectedGridZ
                    ? Color.white
                    : new Color(fill.r, fill.g, fill.b, 0.92f);
                Handles.DrawSolidRectangleWithOutline(
                    cell.worldCorners,
                    fill,
                    outline);
                if (cell.gridX == selectedGridX &&
                    cell.gridZ == selectedGridZ)
                {
                    selected = cell;
                    Handles.color = Color.white;
                    Handles.DrawAAPolyLine(6f,
                        cell.worldCorners[0], cell.worldCorners[1],
                        cell.worldCorners[2], cell.worldCorners[3],
                        cell.worldCorners[0]);
                }
                if (options.labels &&
                    (cell.gridX == selectedGridX &&
                     cell.gridZ == selectedGridZ ||
                     !options.effectiveFiringEnvelope &&
                     ShouldDrawGridLabel(cell)))
                {
                    string label = cell.gridX + "," + cell.gridZ;
                    if (cell.flyable)
                    {
                        label += "  难：" + difficulty.ChineseLevel + " " +
                                 difficulty.score.ToString("P0") +
                                 "\n来：" + CompactThreatDirections(cell) +
                                 "\n避：" + CompactEvasion(cell, difficulty);
                    }
                    else
                    {
                        label += "  不可飞\n不参与难度评价";
                    }
                    Handles.Label(
                        cell.worldSamplePosition + Vector3.up * 5f,
                        label,
                        GridLabelStyle(cell));
                }
            }

            if (selected != null)
                DrawSelectedCellFire(selected, analysis);
            DrawGridInstruction(sceneView, analysis, selected);
        }

        static void DrawSelectedCellFire(
            EdpcgGridFireCell cell,
            EdpcgGridFireAnalysis analysis)
        {
            if (options.effectiveFiringEnvelope)
                DrawEffectiveFiringEnvelope(cell, analysis);
            DrawThreatCompass(cell);
            int displayedWindowCount = 0;
            for (int index = 0;
                 index < cell.fireWindows.Count &&
                 displayedWindowCount < Mathf.Clamp(
                     options.maximumThreatChannels, 1, 3);
                 index++)
            {
                EdpcgAirFireWindow window = cell.fireWindows[index];
                if (!MatchesWindow(window))
                    continue;
                displayedWindowCount++;
                Color roleColor = window.sourceKind ==
                                  EdpcgGridFireSourceKind.Gunship
                    ? new Color(1f, 0.18f, 0.12f, 0.96f)
                    : new Color(1f, 0.58f, 0.12f, 0.96f);
                Color approachColor = window.throughBuildingGap
                    ? new Color(0.15f, 1f, 0.92f, 0.96f)
                    : new Color(1f, 0.62f, 0.16f, 0.90f);
                DrawWorldPath(window.approachWorldPoints,
                    approachColor, window.throughBuildingGap ? 5f : 3f);
                if (window.throughBuildingGap && options.gapWindows)
                {
                    Handles.color = approachColor;
                    float size = HandleUtility.GetHandleSize(
                        window.firingWorldPosition) * 0.12f;
                    Handles.DrawWireCube(window.firingWorldPosition,
                        new Vector3(size * 1.4f, size, size * 0.35f));
                }
                if (options.finalBallistics)
                {
                    Handles.color = roleColor;
                    Handles.DrawAAPolyLine(4f,
                        window.firingWorldPosition,
                        window.playerWorldPosition);
                    DrawArrowHead(window.firingWorldPosition,
                        window.playerWorldPosition, roleColor);
                }
                if (options.labels)
                {
                    Handles.Label(
                        window.firingWorldPosition + Vector3.up *
                        (5f + displayedWindowCount * 3f),
                        ChineseFireSource(window.sourceKind) + "｜" +
                        ChineseSector(window.azimuthSector) +
                        ChineseElevation(window.elevationBand) + "｜" +
                        (window.throughBuildingGap ? "楼缝" : "路线") +
                        "｜" + window.firstHitSeconds.ToString("0.0") +
                        "秒命中" +
                        (window.robustHullClear
                            ? string.Empty
                            : "｜仅导航口径通过"),
                        SmallLabelStyle(roleColor));
                }
            }

            int blockedLabelCount = 0;
            int blockedDirectionMask = 0;
            if (options.blockedFire && options.effectiveFiringEnvelope)
            {
                blockedDirectionMask = DrawFiringVolumeOcclusions(
                    cell, ref blockedLabelCount);
            }
            for (int index = 0; index < cell.fireLines.Count; index++)
            {
                EdpcgGridFireLine line = cell.fireLines[index];
                if (line == null || !MatchesRole(line.sourceKind) ||
                    line.incoming)
                    continue;
                int directionBit = 1 << Mathf.Clamp(
                    line.directionIndex, 0, 23);
                if (!options.blockedFire ||
                    line.blockerWorldPosition == Vector3.zero ||
                    (blockedDirectionMask & directionBit) != 0 ||
                    blockedLabelCount >= 12)
                {
                    continue;
                }
                blockedDirectionMask |= directionBit;
                blockedLabelCount++;
                {
                    Color color = new Color(0.18f, 0.72f, 1f, 0.95f);
                    Vector3 fromPlayer = line.sourceWorldPosition -
                                         line.playerWorldPosition;
                    if (options.effectiveFiringEnvelope &&
                        fromPlayer.sqrMagnitude > 1f)
                    {
                        Handles.color = new Color(
                            color.r, color.g, color.b, 0.18f);
                        Handles.DrawDottedLine(
                            line.playerWorldPosition,
                            line.playerWorldPosition +
                            fromPlayer.normalized *
                            analysis.firingVolumeRadius,
                            14f);
                    }
                    Handles.color = color;
                    Handles.DrawAAPolyLine(4f,
                        line.sourceWorldPosition,
                        line.blockerWorldPosition);
                    Handles.color = new Color(color.r, color.g, color.b, 0.28f);
                    Handles.DrawDottedLine(
                        line.blockerWorldPosition,
                        line.playerWorldPosition,
                        8f);
                    DrawBlockMarker(line.blockerWorldPosition, color);
                    if (options.labels)
                    {
                        Handles.Label(
                            line.blockerWorldPosition + Vector3.up *
                            (4f + blockedLabelCount * 3f),
                            ChineseSector(line.threatSector) +
                            ChineseElevation(line.elevationBand) +
                            "被楼群挡住",
                            SmallLabelStyle(color));
                    }
                }
            }

            if (!options.evasions)
                return;
            int displayedEvasions = 0;
            for (int index = 0;
                 index < cell.evasions.Count && displayedEvasions < 2;
                 index++)
            {
                EdpcgGridEvasionLink evasion = cell.evasions[index];
                if (evasion == null || !evasion.recommended)
                    continue;
                displayedEvasions++;
                Color color = new Color(0.22f, 1f, 0.42f, 0.95f);
                DrawWorldPath(evasion.worldPathPoints, color, 5f);
                if (options.labels)
                {
                    Handles.Label(
                        Vector3.Lerp(evasion.startWorldPosition,
                            evasion.endWorldPosition, 0.58f) +
                        Vector3.up * 6f,
                        (evasion.verticalTransfer ? "换层" : "转移") +
                        "｜" + evasion.travelSeconds.ToString("0.0") +
                        "秒｜风险-" +
                        Mathf.Max(0f, evasion.pressureReduction).ToString("P0") +
                        "｜余量" +
                        evasion.escapeMarginSeconds.ToString("+0.0;-0.0;0.0") +
                        "秒",
                        SmallLabelStyle(color));
                }
            }
        }

        static int DrawFiringVolumeOcclusions(
            EdpcgGridFireCell cell,
            ref int labelCount)
        {
            int directionMask = 0;
            Color strong = new Color(0.16f, 0.70f, 1f, 0.92f);
            Color hidden = new Color(0.16f, 0.70f, 1f, 0.24f);
            for (int index = 0;
                 index < cell.firingVolumeOcclusions.Count;
                 index++)
            {
                EdpcgFiringVolumeOcclusionRay ray =
                    cell.firingVolumeOcclusions[index];
                if (ray == null)
                    continue;
                int directionIndex = Mathf.Clamp(
                    ray.directionIndex, 0, 23);
                int bit = 1 << directionIndex;
                if ((directionMask & bit) != 0)
                    continue;
                directionMask |= bit;
                Handles.color = strong;
                Handles.DrawAAPolyLine(4f,
                    ray.sampleWorldPosition,
                    ray.blockerWorldPosition);
                Handles.color = hidden;
                Handles.DrawDottedLine(
                    ray.blockerWorldPosition,
                    ray.playerWorldPosition,
                    7f);
                DrawBlockMarker(ray.blockerWorldPosition, strong);
                if (!options.labels || labelCount >= 2)
                    continue;
                labelCount++;
                int elevation = directionIndex / 8 - 1;
                int azimuth = directionIndex % 8;
                Handles.Label(
                    ray.blockerWorldPosition + Vector3.up *
                    (3f + labelCount * 2f),
                    ChineseSector(azimuth) + ChineseElevation(elevation) +
                    "｜有效枪位包络被城市实体截断",
                    SmallLabelStyle(strong));
            }
            return directionMask;
        }

        static void DrawEffectiveFiringEnvelope(
            EdpcgGridFireCell cell,
            EdpcgGridFireAnalysis analysis)
        {
            float radius = Mathf.Max(1f, analysis.firingVolumeRadius);
            Vector3 center = cell.worldSamplePosition;
            float worldAltitudeOffset = center.y - analysis.altitude;
            float minimumY = analysis.minimumFiringAltitude +
                             worldAltitudeOffset;
            float maximumY = analysis.maximumFiringAltitude +
                             worldAltitudeOffset;
            Color gunshipColor = new Color(0.40f, 0.78f, 1f, 0.46f);
            Color strikerColor = new Color(1f, 0.62f, 0.18f, 0.48f);
            DrawClippedFiringEnvelope(
                center, radius,
                analysis.gunshipMinimumFiringAltitude +
                worldAltitudeOffset,
                maximumY, gunshipColor, true);
            DrawClippedFiringEnvelope(
                center, 170f, minimumY, maximumY, strikerColor, false);
            if (options.labels)
            {
                Handles.Label(
                    new Vector3(center.x, maximumY + 8f, center.z),
                    "有效枪位包络｜突击机约170米｜炮艇约" +
                    radius.ToString("0") + "米\n" +
                    "只含常用航层、重接敌高度与可达占位",
                    SmallLabelStyle(new Color(
                        0.62f, 0.88f, 1f, 1f)));
            }
        }

        static void DrawClippedFiringEnvelope(
            Vector3 center,
            float radius,
            float minimumY,
            float maximumY,
            Color color,
            bool drawHeightBounds)
        {
            float low = Mathf.Max(minimumY, center.y - radius);
            float high = Mathf.Min(maximumY, center.y + radius);
            if (high <= low + 0.01f)
                return;
            const int HeightSteps = 10;
            var points = new Vector3[HeightSteps + 1];
            for (int sector = 0; sector < 8; sector++)
            {
                Vector3 direction = Quaternion.Euler(
                    0f, sector * 45f, 0f) * Vector3.forward;
                for (int step = 0; step <= HeightSteps; step++)
                {
                    float y = Mathf.Lerp(low, high,
                        step / (float)HeightSteps);
                    float vertical = y - center.y;
                    float horizontal = Mathf.Sqrt(Mathf.Max(
                        0f, radius * radius - vertical * vertical));
                    points[step] = new Vector3(
                        center.x, y, center.z) +
                        direction * horizontal;
                }
                Handles.color = color;
                Handles.DrawAAPolyLine(2f, points);
            }
            float middle = Mathf.Clamp(center.y, low, high);
            DrawEnvelopeRing(center, radius, middle, color);
            if (!drawHeightBounds)
                return;
            DrawEnvelopeRing(center, radius, low,
                new Color(color.r, color.g, color.b, 0.70f));
            DrawEnvelopeRing(center, radius, high,
                new Color(color.r, color.g, color.b, 0.70f));
        }

        static void DrawEnvelopeRing(
            Vector3 center,
            float radius,
            float worldY,
            Color color)
        {
            float vertical = worldY - center.y;
            if (Mathf.Abs(vertical) > radius)
                return;
            float horizontal = Mathf.Sqrt(Mathf.Max(
                0f, radius * radius - vertical * vertical));
            Handles.color = color;
            Handles.DrawWireDisc(
                new Vector3(center.x, worldY, center.z),
                Vector3.up, horizontal);
        }

        static void HandleGridSelection(
            SceneView sceneView,
            EdpcgGridFireAnalysis analysis)
        {
            Event current = Event.current;
            if (current == null || current.type != EventType.MouseDown ||
                current.button != 0 || !current.control || current.alt)
            {
                return;
            }
            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            Vector3 planePoint = analysis.cells.Count > 0
                ? analysis.cells[0].worldSamplePosition
                : Vector3.zero;
            var plane = new Plane(Vector3.up, planePoint);
            if (!plane.Raycast(ray, out float distance))
                return;
            EdpcgGridFireCell cell = analysis.FindNearestCell(
                ray.GetPoint(distance));
            if (cell == null)
                return;
            selectedGridX = cell.gridX;
            selectedGridZ = cell.gridZ;
            gridSelectionChanged?.Invoke(selectedGridX, selectedGridZ);
            current.Use();
            sceneView.Repaint();
        }

        static void DrawGridInstruction(
            SceneView sceneView,
            EdpcgGridFireAnalysis analysis,
            EdpcgGridFireCell selected)
        {
            if (!options.labels || sceneView == null)
                return;
            Handles.BeginGUI();
            try
            {
                if (selected != null)
                {
                    float panelWidth = Mathf.Clamp(
                        sceneView.position.width - 24f, 300f, 650f);
                    if (!selected.flyable)
                    {
                        GUI.Label(
                            new Rect(48f, 8f, panelWidth, 54f),
                            "当前方格 " + selected.gridX + "," +
                            selected.gridZ + "（" +
                            ChineseAltitude(analysis.altitudeLayer) + "）\n" +
                            "参考飞船无法安全进入，本格不参与战斗难度评价。",
                            SmallLabelStyle(Color.white));
                    }
                    else
                    {
                        GetFilteredSummary(selected,
                            out int shownDirections,
                            out float shownFastestHit,
                            out float shownThreatenedVolume,
                            out int shownGapWindows);
                        EdpcgGridDifficultyBreakdown difficulty =
                            EdpcgGridDifficultyEvaluator.Evaluate(
                                selected, shownDirections,
                                shownThreatenedVolume, shownFastestHit,
                                HasFilteredCrossfire(selected));
                        GUI.Label(
                            new Rect(48f, 8f, panelWidth, 92f),
                            "当前方格 " + selected.gridX + "," +
                            selected.gridZ + "（" +
                            ChineseAltitude(analysis.altitudeLayer) + "）\n" +
                            "综合难度 " + difficulty.score.ToString("P0") +
                            "（" + difficulty.ChineseLevel + "）｜火力 " +
                            difficulty.fireThreat.ToString("P0") +
                            "｜脱离 " + difficulty.escapeDifficulty.ToString("P0") +
                            "｜机动 " + difficulty.maneuverDifficulty.ToString("P0") +
                            "\n" +
                            ChineseViewMode(options.threatViewMode) + " " +
                            shownDirections + "向｜受威胁体积 " +
                            shownThreatenedVolume.ToString("P0") +
                            "｜楼缝窗 " + shownGapWindows + "\n" +
                            (float.IsPositiveInfinity(shownFastestHit)
                                ? "没有能完成预警的射击窗"
                                : "最早命中 " +
                                  shownFastestHit.ToString("0.0") +
                                  "秒") +
                            "｜有效脱离 " +
                            difficulty.recommendedExitCount + "｜" +
                            CompactEvasion(selected, difficulty),
                            SmallLabelStyle(Color.white));
                    }
                }
                float instructionWidth = Mathf.Clamp(
                    sceneView.position.width - 24f, 300f, 520f);
                GUI.Label(
                    new Rect(
                        Mathf.Max(12f,
                            sceneView.position.width - instructionWidth - 12f),
                        sceneView.position.height - 82f,
                        instructionWidth,
                        64f),
                    "底色：绿色容易｜黄色中等｜橙色困难｜红色高危；综合火力、脱离与机动\n" +
                    "按住控制键单击选格｜蓝橙网：有效枪位包络｜蓝线与叉：实体裁切/挡枪｜红橙短线：弹道｜绿线：转移\n" +
                    "参考机宽 " + analysis.referenceShipWidth.ToString("0.#") +
                    "米｜结果为只读规划诊断，不是开火授权",
                    SmallLabelStyle(Color.white));
            }
            finally
            {
                Handles.EndGUI();
            }
        }

        static Color FireCellColor(EdpcgGridFireCell cell,
            EdpcgGridDifficultyBreakdown difficulty)
        {
            if (!cell.flyable)
                return new Color(0.16f, 0.16f, 0.18f, 0.35f);
            if (difficulty.score >= 0.68f)
                return new Color(1f, 0.12f, 0.08f, 0.27f);
            if (difficulty.score >= 0.40f)
                return new Color(1f, 0.44f, 0.08f, 0.24f);
            if (difficulty.score >= 0.18f)
                return new Color(1f, 0.84f, 0.12f, 0.20f);
            return new Color(0.18f, 0.92f, 0.36f, 0.17f);
        }

        static EdpcgGridDifficultyBreakdown EvaluateDifficulty(
            EdpcgGridFireCell cell)
        {
            GetFilteredSummary(cell, out int directions,
                out float fastestHit, out float threatenedVolume, out _);
            return EdpcgGridDifficultyEvaluator.Evaluate(
                cell, directions, threatenedVolume, fastestHit,
                HasFilteredCrossfire(cell));
        }

        static bool ShouldDrawGridLabel(EdpcgGridFireCell cell)
        {
            if (SceneView.currentDrawingSceneView == null)
                return false;
            Camera camera = SceneView.currentDrawingSceneView.camera;
            if (camera == null || cell.worldCorners == null ||
                cell.worldCorners.Length < 2)
                return false;
            Vector3 first = camera.WorldToScreenPoint(cell.worldCorners[0]);
            Vector3 second = camera.WorldToScreenPoint(cell.worldCorners[1]);
            return first.z > 0f && second.z > 0f &&
                   Vector2.Distance(first, second) >= 70f;
        }

        static bool MatchesWindow(EdpcgAirFireWindow window)
        {
            if (window == null || !MatchesRole(window.sourceKind) ||
                (!options.gapWindows && window.throughBuildingGap))
            {
                return false;
            }
            switch (options.threatViewMode)
            {
                case EdpcgAirThreatViewMode.WithinFourSeconds:
                    return window.firstHitSeconds <= 4f;
                case EdpcgAirThreatViewMode.WithinEightSeconds:
                    return window.firstHitSeconds <= 8f;
                case EdpcgAirThreatViewMode.AuthorizedByEdpcg:
                    return window.authorizedByTier;
                default:
                    return true;
            }
        }

        static bool MatchesRole(EdpcgGridFireSourceKind kind)
        {
            if (options.threatRoleFilter ==
                EdpcgAirThreatRoleFilter.Striker)
                return kind == EdpcgGridFireSourceKind.Striker;
            if (options.threatRoleFilter ==
                EdpcgAirThreatRoleFilter.Gunship)
                return kind == EdpcgGridFireSourceKind.Gunship;
            return true;
        }

        static bool HasFilteredCrossfire(EdpcgGridFireCell cell)
        {
            for (int first = 0; first < cell.fireWindows.Count; first++)
            {
                EdpcgAirFireWindow left = cell.fireWindows[first];
                if (!MatchesWindow(left))
                    continue;
                for (int second = first + 1;
                     second < cell.fireWindows.Count;
                     second++)
                {
                    EdpcgAirFireWindow right = cell.fireWindows[second];
                    if (!MatchesWindow(right))
                        continue;
                    int separation = Mathf.Abs(
                        left.azimuthSector - right.azimuthSector);
                    separation = Mathf.Min(separation, 8 - separation);
                    bool separated = separation >= 2 ||
                        separation >= 1 &&
                        left.elevationBand != right.elevationBand;
                    float overlap = Mathf.Min(
                        left.approachSeconds + left.visibleWindowSeconds,
                        right.approachSeconds + right.visibleWindowSeconds) -
                        Mathf.Max(left.setupSeconds, right.setupSeconds);
                    bool independent = !string.Equals(
                        left.stableId, right.stableId,
                        StringComparison.Ordinal) &&
                        (!string.Equals(left.routeId, right.routeId,
                            StringComparison.Ordinal) ||
                         Vector3.Distance(left.firingWorldPosition,
                             right.firingWorldPosition) >= 24f);
                    if (separated && independent && overlap >= 0.25f)
                        return true;
                }
            }
            return false;
        }

        static void DrawWorldPath(Vector3[] points, Color color,
            float width)
        {
            if (points == null || points.Length < 2)
                return;
            Handles.color = color;
            Handles.DrawAAPolyLine(width, points);
            DrawArrowHead(points[points.Length - 2],
                points[points.Length - 1], color);
        }

        static void DrawThreatCompass(EdpcgGridFireCell cell)
        {
            GetViewMasks(cell, out int low, out int mid, out int high);
            Color color;
            switch (options.threatViewMode)
            {
                case EdpcgAirThreatViewMode.WithinFourSeconds:
                    color = new Color(1f, 0.34f, 0.10f, 0.96f);
                    break;
                case EdpcgAirThreatViewMode.WithinEightSeconds:
                    color = new Color(1f, 0.72f, 0.12f, 0.94f);
                    break;
                case EdpcgAirThreatViewMode.AuthorizedByEdpcg:
                    color = new Color(1f, 0.12f, 0.08f, 0.98f);
                    break;
                default:
                    color = new Color(1f, 0.86f, 0.20f, 0.92f);
                    break;
            }
            Vector3 center = cell.worldSamplePosition + Vector3.up * 12f;
            float radius = Mathf.Max(18f,
                HandleUtility.GetHandleSize(center) * 0.72f);
            for (int sector = 0; sector < 8; sector++)
            {
                int bit = 1 << sector;
                if (((low | mid | high) & bit) == 0)
                    continue;
                float angle = sector * 45f;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) *
                                    Vector3.forward;
                DrawCompassBand(center, direction, radius * 0.72f,
                    -4f, (low & bit) != 0, color, "下");
                DrawCompassBand(center, direction, radius * 0.88f,
                    0f, (mid & bit) != 0, color, "平");
                DrawCompassBand(center, direction, radius * 1.04f,
                    4f, (high & bit) != 0, color, "上");
            }
        }

        static void DrawCompassBand(
            Vector3 center,
            Vector3 direction,
            float radius,
            float heightOffset,
            bool active,
            Color color,
            string heightLabel)
        {
            if (!active)
                return;
            Vector3 start = center + Vector3.up * heightOffset +
                            direction * (radius - 8f);
            Vector3 end = center + Vector3.up * heightOffset +
                          direction * radius;
            Handles.color = color;
            Handles.DrawAAPolyLine(4f, start, end);
            DrawArrowHead(start, end, color);
            if (options.labels)
                Handles.Label(end + Vector3.up * 1.5f,
                    heightLabel, SmallLabelStyle(color));
        }

        static void GetViewMasks(EdpcgGridFireCell cell,
            out int low, out int mid, out int high)
        {
            // The cached masks intentionally describe the complete analysis.
            // When the author hides gap windows, rebuild the visible masks so
            // heat, labels and the compass use the same filtered population as
            // the selected-cell paths.
            if (!options.gapWindows)
            {
                BuildVisibleMasksWithoutGaps(cell, out low, out mid, out high);
                return;
            }
            if (options.threatRoleFilter != EdpcgAirThreatRoleFilter.All)
            {
                bool striker = options.threatRoleFilter ==
                                EdpcgAirThreatRoleFilter.Striker;
                if (options.threatViewMode ==
                    EdpcgAirThreatViewMode.WithinFourSeconds)
                {
                    low = striker
                        ? cell.strikerForecastFourDirectionMaskLow
                        : cell.gunshipForecastFourDirectionMaskLow;
                    mid = striker
                        ? cell.strikerForecastFourDirectionMaskMid
                        : cell.gunshipForecastFourDirectionMaskMid;
                    high = striker
                        ? cell.strikerForecastFourDirectionMaskHigh
                        : cell.gunshipForecastFourDirectionMaskHigh;
                    return;
                }
                if (options.threatViewMode ==
                    EdpcgAirThreatViewMode.WithinEightSeconds)
                {
                    low = striker
                        ? cell.strikerForecastEightDirectionMaskLow
                        : cell.gunshipForecastEightDirectionMaskLow;
                    mid = striker
                        ? cell.strikerForecastEightDirectionMaskMid
                        : cell.gunshipForecastEightDirectionMaskMid;
                    high = striker
                        ? cell.strikerForecastEightDirectionMaskHigh
                        : cell.gunshipForecastEightDirectionMaskHigh;
                    return;
                }
                if (options.threatViewMode ==
                    EdpcgAirThreatViewMode.AuthorizedByEdpcg)
                {
                    low = striker
                        ? cell.strikerAuthorizedDirectionMaskLow
                        : cell.gunshipAuthorizedDirectionMaskLow;
                    mid = striker
                        ? cell.strikerAuthorizedDirectionMaskMid
                        : cell.gunshipAuthorizedDirectionMaskMid;
                    high = striker
                        ? cell.strikerAuthorizedDirectionMaskHigh
                        : cell.gunshipAuthorizedDirectionMaskHigh;
                    return;
                }
                low = striker
                    ? cell.strikerPotentialDirectionMaskLow
                    : cell.gunshipPotentialDirectionMaskLow;
                mid = striker
                    ? cell.strikerPotentialDirectionMaskMid
                    : cell.gunshipPotentialDirectionMaskMid;
                high = striker
                    ? cell.strikerPotentialDirectionMaskHigh
                    : cell.gunshipPotentialDirectionMaskHigh;
                return;
            }
            switch (options.threatViewMode)
            {
                case EdpcgAirThreatViewMode.WithinFourSeconds:
                    low = cell.forecastFourDirectionMaskLow;
                    mid = cell.forecastFourDirectionMaskMid;
                    high = cell.forecastFourDirectionMaskHigh;
                    break;
                case EdpcgAirThreatViewMode.WithinEightSeconds:
                    low = cell.forecastEightDirectionMaskLow;
                    mid = cell.forecastEightDirectionMaskMid;
                    high = cell.forecastEightDirectionMaskHigh;
                    break;
                case EdpcgAirThreatViewMode.AuthorizedByEdpcg:
                    low = cell.authorizedDirectionMaskLow;
                    mid = cell.authorizedDirectionMaskMid;
                    high = cell.authorizedDirectionMaskHigh;
                    break;
                default:
                    low = cell.potentialDirectionMaskLow;
                    mid = cell.potentialDirectionMaskMid;
                    high = cell.potentialDirectionMaskHigh;
                    break;
            }
        }

        static void BuildVisibleMasksWithoutGaps(
            EdpcgGridFireCell cell,
            out int low,
            out int mid,
            out int high)
        {
            low = 0;
            mid = 0;
            high = 0;
            if (options.threatViewMode == EdpcgAirThreatViewMode.Potential)
            {
                for (int index = 0; index < cell.fireLines.Count; index++)
                {
                    EdpcgGridFireLine line = cell.fireLines[index];
                    if (line == null || !line.incoming ||
                        line.throughBuildingGap ||
                        !MatchesRole(line.sourceKind))
                    {
                        continue;
                    }
                    AddVisibleDirection(ref low, ref mid, ref high,
                        line.threatSector, line.elevationBand);
                }
                return;
            }
            for (int index = 0; index < cell.fireWindows.Count; index++)
            {
                EdpcgAirFireWindow window = cell.fireWindows[index];
                if (!MatchesWindow(window))
                    continue;
                AddVisibleDirection(ref low, ref mid, ref high,
                    window.azimuthSector, window.elevationBand);
            }
        }

        static void AddVisibleDirection(
            ref int low,
            ref int mid,
            ref int high,
            int sector,
            int elevation)
        {
            int bit = 1 << Mathf.Clamp(sector, 0, 7);
            if (elevation < 0)
                low |= bit;
            else if (elevation > 0)
                high |= bit;
            else
                mid |= bit;
        }

        static void GetFilteredSummary(
            EdpcgGridFireCell cell,
            out int directionCount,
            out float fastestHit,
            out float threatenedVolume,
            out int gapWindows)
        {
            GetViewMasks(cell, out int low, out int mid, out int high);
            directionCount = options.threatViewMode ==
                             EdpcgAirThreatViewMode.AuthorizedByEdpcg
                ? CountBits(low | mid | high)
                : CountBits(low) + CountBits(mid) + CountBits(high);
            fastestHit = float.PositiveInfinity;
            threatenedVolume = 0f;
            gapWindows = 0;
            int threatenedSampleMask = 0;
            if (options.threatViewMode == EdpcgAirThreatViewMode.Potential)
            {
                for (int index = 0; index < cell.fireLines.Count; index++)
                {
                    EdpcgGridFireLine line = cell.fireLines[index];
                    if (line != null && line.incoming &&
                        (options.gapWindows || !line.throughBuildingGap) &&
                        MatchesRole(line.sourceKind))
                    {
                        threatenedSampleMask |= line.threatenedSampleMask;
                    }
                }
            }
            for (int index = 0; index < cell.fireWindows.Count; index++)
            {
                EdpcgAirFireWindow window = cell.fireWindows[index];
                if (!MatchesWindow(window))
                    continue;
                fastestHit = Mathf.Min(fastestHit, window.firstHitSeconds);
                threatenedSampleMask |= window.threatenedSampleMask;
                if (window.throughBuildingGap)
                    gapWindows++;
            }
            threatenedVolume = cell.flyableSubSampleCount <= 0
                ? 0f
                : CountBits(threatenedSampleMask) /
                  (float)cell.flyableSubSampleCount;
        }

        static string ChineseViewMode(EdpcgAirThreatViewMode mode)
        {
            switch (mode)
            {
                case EdpcgAirThreatViewMode.WithinFourSeconds:
                    return "锚点4秒";
                case EdpcgAirThreatViewMode.WithinEightSeconds:
                    return "锚点8秒";
                case EdpcgAirThreatViewMode.AuthorizedByEdpcg:
                    return "静态预算";
                default:
                    return "几何可见";
            }
        }

        static string ChineseElevation(int elevation)
        {
            return elevation > 0 ? "上方" : elevation < 0 ? "下方" : "同层";
        }

        static string CompactThreatDirections(EdpcgGridFireCell cell)
        {
            GetViewMasks(cell, out int low, out int mid, out int high);
            int horizontal = low | mid | high;
            if (horizontal == 0)
                return "无成立方向";
            string result = string.Empty;
            int shown = 0;
            int total = CountBits(horizontal);
            for (int sector = 0; sector < 8 && shown < 3; sector++)
            {
                int bit = 1 << sector;
                if ((horizontal & bit) == 0)
                    continue;
                if (shown > 0)
                    result += "/";
                result += ChineseSectorShort(sector);
                bool hasLow = (low & bit) != 0;
                bool hasMid = (mid & bit) != 0;
                bool hasHigh = (high & bit) != 0;
                if (hasHigh && !hasMid && !hasLow)
                    result += "上";
                else if (hasLow && !hasMid && !hasHigh)
                    result += "下";
                else if (hasHigh && hasLow && !hasMid)
                    result += "上下";
                shown++;
            }
            if (total > shown)
                result += "/另" + (total - shown) + "向";
            return result;
        }

        static string CompactEvasion(EdpcgGridFireCell cell,
            EdpcgGridDifficultyBreakdown difficulty)
        {
            if (!difficulty.ResponseRequired)
                return "当前无需规避";
            EdpcgGridEvasionLink best = null;
            for (int index = 0; index < cell.evasions.Count; index++)
            {
                EdpcgGridEvasionLink candidate = cell.evasions[index];
                if (candidate == null || !candidate.recommended)
                    continue;
                if (best == null ||
                    candidate.escapeMarginSeconds >
                    best.escapeMarginSeconds)
                {
                    best = candidate;
                }
            }
            if (best == null)
                return "无有效脱离路线";
            string direction;
            float height = best.endWorldPosition.y -
                           best.startWorldPosition.y;
            if (best.verticalTransfer && Mathf.Abs(height) > 2f)
                direction = height > 0f ? "爬升" : "下降";
            else
                direction = ChineseSectorShort(
                    EdpcgThreatDirectionUtility.HorizontalSector(
                        best.startWorldPosition,
                        best.endWorldPosition));
            return direction + " " + best.travelSeconds.ToString("0.0") +
                   "秒";
        }

        static string ChineseSectorShort(int sector)
        {
            switch (sector & 7)
            {
                case 0: return "北";
                case 1: return "东北";
                case 2: return "东";
                case 3: return "东南";
                case 4: return "南";
                case 5: return "西南";
                case 6: return "西";
                default: return "西北";
            }
        }

        static void DrawAreas(
            SceneView sceneView,
            EdpcgCityTacticalRuntimeMap map)
        {
            for (int index = 0; index < map.Areas.Count; index++)
            {
                EdpcgRuntimeArea area = map.Areas[index];
                Handles.color = AreaColor(area.Kind);
                Handles.DrawWireCube(area.Bounds.center, area.Bounds.size);
                DrawPointArray(area.Entrances, 3.5f);
                DrawPointArray(area.Exits, 2.2f);
            }
            if (options.labels)
                DrawAreaCallouts(sceneView, map);
        }

        static void DrawAreaCallouts(
            SceneView sceneView,
            EdpcgCityTacticalRuntimeMap map)
        {
            if (sceneView == null || map == null)
                return;
            const float Width = 310f;
            const float Height = 62f;
            const float Top = 92f;
            const float Gap = 10f;
            const float Left = 54f;
            float right = Mathf.Max(Left, sceneView.position.width - Width - 14f);

            Handles.BeginGUI();
            try
            {
                for (int index = 0; index < map.Areas.Count; index++)
                {
                    EdpcgRuntimeArea area = map.Areas[index];
                    bool placeLeft = index % 2 == 0;
                    int row = index / 2;
                    Rect rect = new Rect(
                        placeLeft ? Left : right,
                        Top + row * (Height + Gap),
                        Width,
                        Height);
                    Vector2 anchor = HandleUtility.WorldToGUIPoint(
                        area.Bounds.center + Vector3.up * area.Bounds.extents.y);
                    Vector2 edge = new Vector2(
                        placeLeft ? rect.xMax : rect.xMin,
                        rect.center.y);
                    float elbowX = Mathf.Lerp(anchor.x, edge.x, 0.55f);
                    Handles.color = AreaColor(area.Kind);
                    Handles.DrawAAPolyLine(3f,
                        new Vector3(anchor.x, anchor.y),
                        new Vector3(elbowX, anchor.y),
                        new Vector3(edge.x, edge.y));
                    GUI.Label(rect,
                        "区块 " + (index + 1) + "｜" +
                        ChineseAreaDescription(map, area),
                        AreaLabelStyle());
                }
            }
            finally
            {
                Handles.EndGUI();
            }
        }

        static void DrawTacticalArrows(EdpcgCityTacticalRuntimeMap map)
        {
            for (int index = 0; index < map.TacticalArrows.Count; index++)
            {
                EdpcgRuntimeTacticalArrow arrow = map.TacticalArrows[index];
                Color color = TacticalArrowColor(arrow.Kind);
                bool dotted = arrow.Kind ==
                              EdpcgTacticalArrowKind.VerifiedBlockedFireLine;
                DrawArrow(arrow.Start, arrow.End, color,
                    dotted ? 3f : 5f, arrow.Bidirectional, dotted);

                if (arrow.Kind ==
                    EdpcgTacticalArrowKind.VerifiedBlockedFireLine)
                {
                    DrawBlockMarker(arrow.Marker, color);
                }
            }
        }

        static void DrawArrow(
            Vector3 start,
            Vector3 end,
            Color color,
            float width,
            bool bidirectional,
            bool dotted)
        {
            Vector3 delta = end - start;
            if (delta.sqrMagnitude < 1f)
                return;
            Vector3 direction = delta.normalized;
            Handles.color = color;
            if (dotted)
                Handles.DrawDottedLine(start, end, 5f);
            else
                Handles.DrawAAPolyLine(width, start, end);
            float size = Mathf.Clamp(
                HandleUtility.GetHandleSize(end) * 0.13f,
                5f,
                18f);
            Handles.ConeHandleCap(0, end, Quaternion.LookRotation(direction),
                size, EventType.Repaint);
            if (bidirectional)
            {
                Handles.ConeHandleCap(0, start,
                    Quaternion.LookRotation(-direction), size,
                    EventType.Repaint);
            }
        }

        static void DrawBlockMarker(Vector3 position, Color color)
        {
            if (position == Vector3.zero)
                return;
            float size = Mathf.Clamp(
                HandleUtility.GetHandleSize(position) * 0.11f,
                4f,
                14f);
            Handles.color = color;
            Handles.DrawAAPolyLine(5f,
                position + new Vector3(-size, size, 0f),
                position + new Vector3(size, -size, 0f));
            Handles.DrawAAPolyLine(5f,
                position + new Vector3(-size, -size, 0f),
                position + new Vector3(size, size, 0f));
        }

        static void DrawRoutes(EdpcgCityTacticalRuntimeMap map)
        {
            for (int index = 0; index < map.Routes.Count; index++)
            {
                EdpcgRuntimeRoute route = map.Routes[index];
                if (route == null || route.Points == null || route.Points.Length < 2)
                    continue;
                Handles.color = route.IsEnvironmentalTrap
                    ? new Color(1f, 0.28f, 0.75f, 0.95f)
                    : RouteColor(route.SourceKind);
                Handles.DrawAAPolyLine(route.IsEnvironmentalTrap ? 5f : 3f,
                    route.Points);
                if (options.labels)
                {
                    Vector3 midpoint = route.Points[route.Points.Length / 2];
                    Handles.Label(midpoint,
                        route.IsEnvironmentalTrap
                            ? "环境陷阱专用路线"
                            : ChineseRoute(route.SourceKind),
                        SmallLabelStyle(Handles.color));
                }
            }
        }

        static void DrawIngresses(EdpcgCityTacticalRuntimeMap map)
        {
            Handles.color = new Color(1f, 0.32f, 0.18f, 1f);
            for (int index = 0; index < map.Ingresses.Count; index++)
            {
                EdpcgRuntimeIngress ingress = map.Ingresses[index];
                float size = HandleUtility.GetHandleSize(ingress.Position) * 0.12f;
                Handles.SphereHandleCap(0, ingress.Position, Quaternion.identity,
                    size, EventType.Repaint);
                Handles.DrawDottedLine(ingress.Position, ingress.Target, 5f);
                if (options.labels)
                    Handles.Label(ingress.Position,
                        "敌机入口 " + (index + 1),
                        SmallLabelStyle(Handles.color));
            }
        }

        static void DrawReservations(
            EdpcgEncounterRuntime active,
            EdpcgCityTacticalRuntimeMap map)
        {
            if (active.Reservations == null)
                return;
            Handles.color = new Color(1f, 0.86f, 0.12f, 1f);
            for (int index = 0;
                 index < active.Reservations.Reservations.Count;
                 index++)
            {
                EdpcgRouteReservation reservation =
                    active.Reservations.Reservations[index];
                if (!map.TryGetRoute(reservation.routeId,
                        out EdpcgRuntimeRoute route) ||
                    route.Points == null || route.Points.Length < 2)
                {
                    continue;
                }
                Handles.DrawAAPolyLine(7f, route.Points);
                if (options.labels)
                {
                    Handles.Label(route.Points[route.Points.Length / 2],
                        "实时路线预留 " + (index + 1),
                        SmallLabelStyle(Handles.color));
                }
            }
        }

        static void DrawPressureDirections(EdpcgEncounterRuntime active)
        {
            EdpcgPressureSample sample = active.CurrentSample;
            if (sample == null || sample.pressureDirectionMask == 0)
                return;
            Vector3 center = active.PlayerWorldCenter;
            Handles.color = new Color(1f, 0.18f, 0.12f, 0.95f);
            for (int sector = 0; sector < 8; sector++)
            {
                if ((sample.pressureDirectionMask & (1 << sector)) == 0)
                    continue;
                float angle = (sector + 0.5f) * 45f * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Sin(angle), 0f,
                    Mathf.Cos(angle));
                Vector3 end = center + direction * 95f;
                Handles.DrawAAPolyLine(6f, center, end);
                Handles.ConeHandleCap(0, end,
                    Quaternion.LookRotation(-direction), 12f, EventType.Repaint);
            }
            if (options.labels)
                Handles.Label(center + Vector3.up * 18f,
                    "玩家位置｜来压方向 " + sample.pressureDirectionCount,
                    SmallLabelStyle(Handles.color));
        }

        static void DrawFinalGeometry()
        {
            AirCombatCityRuntimeGeometrySnapshot snapshot =
                urbanRuntime != null
                    ? urbanRuntime.RuntimeGeometrySnapshot
                    : cityLab != null
                        ? cityLab.RuntimeGeometrySnapshot
                        : null;
            if (snapshot == null || !snapshot.IsUsable)
                return;

            Func<Vector3, Vector3> projector = urbanRuntime != null
                ? new Func<Vector3, Vector3>(
                    local => Project(urbanRuntime, local))
                : local => cityLab.transform.TransformPoint(local);

            if (options.buildings)
            {
                for (int index = 0; index < snapshot.buildings.Length; index++)
                {
                    AirCombatRuntimeBuildingGeometry building =
                        snapshot.buildings[index];
                    Handles.color = BuildingColor(building.band);
                    Bounds bounds = building.localBounds;
                    bounds.center = projector(bounds.center);
                    Handles.DrawWireCube(bounds.center, bounds.size);
                }
            }
            if (options.skybridges)
            {
                Handles.color = new Color(0.18f, 0.84f, 1f, 0.9f);
                DrawConnections(projector, snapshot.skybridges, 4f);
            }
            if (options.cables)
            {
                Handles.color = new Color(1f, 0.24f, 0.14f, 0.95f);
                DrawConnections(projector, snapshot.aerialCables, 3f);
            }
        }

        static void DrawConnections(
            Func<Vector3, Vector3> projector,
            AirCombatRuntimeConnectionGeometry[] connections,
            float width)
        {
            if (connections == null)
                return;
            for (int index = 0; index < connections.Length; index++)
            {
                Handles.DrawAAPolyLine(width,
                    projector(connections[index].localStart),
                    projector(connections[index].localEnd));
            }
        }

        static Vector3 Project(
            FinitePlanetUrbanCombatRuntime urban,
            Vector3 local)
        {
            return urban.ProjectPlanPosition(local) + Vector3.up * local.y;
        }

        static void DrawPointArray(Vector3[] points, float radius)
        {
            if (points == null)
                return;
            for (int index = 0; index < points.Length; index++)
                Handles.DrawWireDisc(points[index], Vector3.up, radius);
        }

        static Color AreaColor(EdpcgTacticalAreaKind kind)
        {
            switch (kind)
            {
                case EdpcgTacticalAreaKind.SpawnSafeAirspace:
                    return new Color(0.2f, 0.9f, 0.4f, 0.9f);
                case EdpcgTacticalAreaKind.HighRiseOcclusionChain:
                    return new Color(0.2f, 0.65f, 1f, 0.9f);
                case EdpcgTacticalAreaKind.ExposedFireShortcut:
                    return new Color(1f, 0.3f, 0.18f, 0.9f);
                case EdpcgTacticalAreaKind.MagneticCourtyard:
                    return new Color(1f, 0.22f, 0.78f, 0.9f);
                default:
                    return new Color(1f, 0.82f, 0.2f, 0.85f);
            }
        }

        static Color RouteColor(AirCombatRouteKind kind)
        {
            switch (kind)
            {
                case AirCombatRouteKind.MaskedFlank:
                    return new Color(0.2f, 0.72f, 1f, 0.95f);
                case AirCombatRouteKind.LongRange:
                    return new Color(1f, 0.42f, 0.18f, 0.95f);
                case AirCombatRouteKind.VerticalEscape:
                    return new Color(0.5f, 1f, 0.4f, 0.95f);
                default:
                    return new Color(0.95f, 0.92f, 0.28f, 0.95f);
            }
        }

        static Color BuildingColor(AirCombatBuildingBand band)
        {
            switch (band)
            {
                case AirCombatBuildingBand.High:
                    return new Color(0.34f, 0.56f, 1f, 0.25f);
                case AirCombatBuildingBand.Medium:
                    return new Color(0.3f, 0.9f, 1f, 0.22f);
                default:
                    return new Color(0.5f, 0.75f, 0.85f, 0.18f);
            }
        }

        static Color TacticalArrowColor(EdpcgTacticalArrowKind kind)
        {
            switch (kind)
            {
                case EdpcgTacticalArrowKind.VerifiedBlockedFireLine:
                    return new Color(0.18f, 0.72f, 1f, 1f);
                case EdpcgTacticalArrowKind.ExpectedFireExchange:
                    return new Color(1f, 0.48f, 0.12f, 1f);
                case EdpcgTacticalArrowKind.TrapApproach:
                    return new Color(1f, 0.2f, 0.72f, 1f);
                case EdpcgTacticalArrowKind.VerticalTransition:
                    return new Color(0.34f, 1f, 0.38f, 1f);
                case EdpcgTacticalArrowKind.SpawnDeparture:
                    return new Color(0.25f, 1f, 0.55f, 1f);
                default:
                    return new Color(1f, 0.86f, 0.18f, 1f);
            }
        }

        static string ChineseAreaDescription(
            EdpcgCityTacticalRuntimeMap map,
            EdpcgRuntimeArea area)
        {
            switch (area.Kind)
            {
                case EdpcgTacticalAreaKind.SpawnSafeAirspace:
                    return "出生缓冲区\n用途：进入战场，不设计为固定对枪位";
                case EdpcgTacticalAreaKind.CentralManeuverDistrict:
                    return "中央机动区\n期望：近中距离多向交火";
                case EdpcgTacticalAreaKind.HighRiseOcclusionChain:
                    return HasVerifiedBlockedLine(map, area.StableId)
                        ? "高楼遮挡区\n已验证：阻断掩蔽路线到战区中心的枪线"
                        : "高楼遮挡区\n当前未测得有效挡枪点，需要检查该种子";
                case EdpcgTacticalAreaKind.ExposedFireShortcut:
                    return "暴露火力捷径\n期望：远程对枪，长射界高风险";
                case EdpcgTacticalAreaKind.MagneticCourtyard:
                    return "磁场诱敌区\n期望：诱敌进入陷阱，不作为固定对枪位";
                case EdpcgTacticalAreaKind.LowMidVerticalTransition:
                    return "高度换层区\n用途：追击或脱离时换层，不建议停留对枪";
                default:
                    return "未分类战术区\n用途尚未定义";
            }
        }

        static bool HasVerifiedBlockedLine(
            EdpcgCityTacticalRuntimeMap map,
            string areaId)
        {
            if (map == null)
                return false;
            for (int index = 0; index < map.TacticalArrows.Count; index++)
            {
                EdpcgRuntimeTacticalArrow arrow = map.TacticalArrows[index];
                if (arrow.Verified &&
                    arrow.Kind == EdpcgTacticalArrowKind.VerifiedBlockedFireLine &&
                    string.Equals(arrow.AreaId, areaId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        static GUIStyle AreaLabelStyle()
        {
            if (areaLabelStyle != null)
                return areaLabelStyle;
            areaLabelStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                padding = new RectOffset(8, 8, 5, 5)
            };
            areaLabelStyle.normal.textColor = Color.white;
            return areaLabelStyle;
        }

        static GUIStyle SmallLabelStyle(Color color)
        {
            if (smallLabelStyle == null)
            {
                smallLabelStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                    padding = new RectOffset(5, 5, 3, 3)
                };
            }
            smallLabelStyle.normal.textColor = color;
            return smallLabelStyle;
        }

        static GUIStyle GridLabelStyle(EdpcgGridFireCell cell)
        {
            if (gridLabelStyle == null)
            {
                gridLabelStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    fontSize = 9,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(3, 3, 2, 2)
                };
            }
            gridLabelStyle.normal.textColor = cell != null && cell.flyable
                ? Color.white
                : new Color(0.72f, 0.72f, 0.72f);
            return gridLabelStyle;
        }

        static void DrawArrowHead(
            Vector3 start,
            Vector3 end,
            Color color)
        {
            Vector3 direction = end - start;
            if (direction.sqrMagnitude < 1f)
                return;
            float size = Mathf.Clamp(
                HandleUtility.GetHandleSize(end) * 0.09f,
                3f,
                12f);
            Handles.color = color;
            Handles.ConeHandleCap(
                0,
                end,
                Quaternion.LookRotation(direction.normalized),
                size,
                EventType.Repaint);
        }

        static string ChineseFireSource(EdpcgGridFireSourceKind kind)
        {
            return kind == EdpcgGridFireSourceKind.Gunship
                ? "炮艇"
                : "突击机";
        }

        static string ChineseSector(int sector)
        {
            switch (sector & 7)
            {
                case 0: return "北侧";
                case 1: return "东北";
                case 2: return "东侧";
                case 3: return "东南";
                case 4: return "南侧";
                case 5: return "西南";
                case 6: return "西侧";
                default: return "西北";
            }
        }

        static string ChineseAltitude(EdpcgFireAnalysisAltitudeLayer layer)
        {
            switch (layer)
            {
                case EdpcgFireAnalysisAltitudeLayer.Low:
                    return "低空层";
                case EdpcgFireAnalysisAltitudeLayer.High:
                    return "高空层";
                default:
                    return "中空层";
            }
        }

        static int CountBits(int value)
        {
            int count = 0;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }
            return count;
        }

        static string ChineseRoute(AirCombatRouteKind kind)
        {
            switch (kind)
            {
                case AirCombatRouteKind.Main: return "主机动路线";
                case AirCombatRouteKind.MaskedFlank: return "掩蔽侧翼路线";
                case AirCombatRouteKind.LongRange: return "远程火力路线";
                case AirCombatRouteKind.EnemyIngress: return "敌机进入路线";
                case AirCombatRouteKind.VerticalEscape: return "垂直脱离路线";
                case AirCombatRouteKind.KiteLoop: return "风筝环线";
                default: return kind.ToString();
            }
        }
    }
}
#endif
