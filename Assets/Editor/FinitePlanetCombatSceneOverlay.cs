using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityPlanet.CombatMap;

[InitializeOnLoad]
public static class FinitePlanetCombatSceneOverlay
{
    const string MenuPath = "Tools/Planet Combat/Scene PCG Overlay";
    const string PreferenceKey =
        "UnityPlanet.FinitePlanetCombat.ScenePcgOverlay";
    const int CircleSegments = 48;

    static readonly Color WarningColor =
        new Color(1f, 0.68f, 0.1f, 0.9f);
    static readonly Color LimitColor =
        new Color(1f, 0.2f, 0.12f, 0.88f);
    static readonly Color RidgeColor =
        new Color(1f, 0.32f, 0.08f, 0.9f);
    static readonly Color MesaColor =
        new Color(0.95f, 0.2f, 0.82f, 0.9f);
    static readonly Color ProtectedColor =
        new Color(0.08f, 0.95f, 0.78f, 0.88f);

    static GUIStyle labelStyle;
    static GUIStyle titleStyle;

    static FinitePlanetCombatSceneOverlay()
    {
        SceneView.duringSceneGui += DuringSceneGui;
    }

    [MenuItem(MenuPath)]
    static void Toggle()
    {
        Enabled = !Enabled;
        SceneView.RepaintAll();
    }

    [MenuItem(MenuPath, true)]
    static bool ToggleValidation()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }

    static bool Enabled
    {
        get => EditorPrefs.GetBool(PreferenceKey, true);
        set
        {
            EditorPrefs.SetBool(PreferenceKey, value);
            Menu.SetChecked(MenuPath, value);
        }
    }

    static void DuringSceneGui(SceneView sceneView)
    {
        if (!Enabled || !EditorApplication.isPlaying)
            return;

        InfinitePlanarSurfaceWorld world =
            Object.FindObjectOfType<InfinitePlanarSurfaceWorld>();
        FinitePlanetCombatTerrainPlan terrain =
            world != null ? world.FiniteCombatTerrainPlan : null;
        CombatSemanticPlan plan = terrain?.SemanticPlan;
        if (plan == null)
            return;

        EnsureStyles();
        CompareFunction priorZTest = Handles.zTest;
        Color priorColor = Handles.color;
        Handles.zTest = CompareFunction.LessEqual;

        DrawBoundaries(terrain, plan);
        DrawTerrainStamps(terrain, plan);
        DrawRoutes(terrain, plan);
        DrawTacticalVolumes(terrain, plan);
        DrawAnchors(terrain, plan);
        DrawViolations(terrain);
        DrawLegend(sceneView, terrain, plan);

        Handles.color = priorColor;
        Handles.zTest = priorZTest;
    }

    static void DrawBoundaries(
        FinitePlanetCombatTerrainPlan terrain,
        CombatSemanticPlan plan)
    {
        float centerY = GroundHeight(
            terrain,
            plan.mapCenter.x,
            plan.mapCenter.z) + 5f;
        Vector3 center = new Vector3(
            plan.mapCenter.x,
            centerY,
            plan.mapCenter.z);
        Handles.color = WarningColor;
        Handles.DrawWireDisc(center, Vector3.up, plan.warningRadius);
        Handles.color = LimitColor;
        Handles.DrawWireDisc(center, Vector3.up, plan.forfeitRadius);
        Handles.Label(
            center + Vector3.up * 14f,
            "战斗区域中心\n黄线：预警范围  红线：高山边界",
            titleStyle);
    }

    static void DrawTerrainStamps(
        FinitePlanetCombatTerrainPlan terrain,
        CombatSemanticPlan plan)
    {
        if (plan.terrainStamps == null)
            return;

        foreach (CombatTerrainStamp stamp in plan.terrainStamps)
        {
            if (stamp == null)
                continue;

            Color color = StampColor(stamp.type);
            Vector3 start = OnGround(terrain, stamp.start, 2.5f);
            Vector3 end = OnGround(terrain, stamp.end, 2.5f);
            DrawPlanarCapsule(start, end, stamp.radius, color);

            Vector3 center = OnGround(
                terrain,
                Vector3.Lerp(stamp.start, stamp.end, 0.5f),
                3.5f);
            if (stamp.type == CombatTerrainStampType.RidgeCapsule
                || stamp.type == CombatTerrainStampType.MesaCapsule)
            {
                Vector3 basePoint = new Vector3(
                    center.x,
                    terrain.BaseGroundHeight,
                    center.z);
                Handles.color = new Color(
                    color.r,
                    color.g,
                    color.b,
                    0.55f);
                Handles.DrawDottedLine(basePoint, center, 5f);
            }

            Handles.Label(
                center + Vector3.up * 7f,
                StampDisplayName(stamp.type)
                + "  实际地高 "
                + (center.y - terrain.BaseGroundHeight).ToString("F0")
                + "m\n"
                + stamp.stableId,
                labelStyle);
        }
    }

    static void DrawRoutes(
        FinitePlanetCombatTerrainPlan terrain,
        CombatSemanticPlan plan)
    {
        if (plan.routes == null)
            return;

        foreach (CombatSemanticRoute route in plan.routes)
        {
            if (route?.waypoints == null || route.waypoints.Length < 2)
                continue;

            var points = new Vector3[route.waypoints.Length];
            for (int index = 0; index < points.Length; index++)
            {
                Vector3 source = route.waypoints[index];
                float ground = GroundHeight(terrain, source.x, source.z);
                points[index] = new Vector3(
                    source.x,
                    Mathf.Max(source.y, ground + 12f),
                    source.z);
            }

            Handles.color = RouteColor(route.type);
            Handles.DrawAAPolyLine(
                Mathf.Clamp(route.width * 0.035f, 2f, 8f),
                points);
            for (int index = 0; index < points.Length; index++)
            {
                Handles.DrawWireDisc(
                    points[index],
                    Vector3.up,
                    Mathf.Max(4f, route.width * 0.075f));
            }
            Handles.Label(
                points[points.Length / 2] + Vector3.up * 9f,
                RouteDisplayName(route.type) + "\n" + route.stableId,
                labelStyle);
        }
    }

    static void DrawTacticalVolumes(
        FinitePlanetCombatTerrainPlan terrain,
        CombatSemanticPlan plan)
    {
        if (plan.tacticalVolumes == null)
            return;

        foreach (CombatTacticalVolume volume in plan.tacticalVolumes)
        {
            if (volume == null)
                continue;

            float ground = GroundHeight(
                terrain,
                volume.position.x,
                volume.position.z);
            Vector3 center = new Vector3(
                volume.position.x,
                ground + volume.size.y * 0.5f,
                volume.position.z);
            Handles.color = VolumeColor(volume.type);
            Handles.DrawWireDisc(
                new Vector3(center.x, ground + 2f, center.z),
                Vector3.up,
                volume.HorizontalDiameter * 0.5f);
            Handles.DrawWireCube(center, volume.size);
            Handles.Label(
                center + Vector3.up * (volume.size.y * 0.5f + 6f),
                VolumeDisplayName(volume.type) + "\n" + volume.stableId,
                labelStyle);
        }
    }

    static void DrawAnchors(
        FinitePlanetCombatTerrainPlan terrain,
        CombatSemanticPlan plan)
    {
        if (plan.anchors == null)
            return;

        foreach (CombatSemanticAnchor anchor in plan.anchors)
        {
            if (anchor == null)
                continue;

            float ground = GroundHeight(
                terrain,
                anchor.position.x,
                anchor.position.z);
            Vector3 position = new Vector3(
                anchor.position.x,
                Mathf.Max(anchor.position.y, ground + 6f),
                anchor.position.z);
            Handles.color = AnchorColor(anchor.type);
            Handles.DrawWireDisc(
                position,
                Vector3.up,
                Mathf.Max(4f, anchor.radius));
            Handles.DrawDottedLine(
                new Vector3(position.x, ground, position.z),
                position,
                4f);
            Vector3 forward = anchor.forward.sqrMagnitude > 0.001f
                ? anchor.forward.normalized
                : Vector3.forward;
            Handles.ArrowHandleCap(
                0,
                position,
                Quaternion.LookRotation(forward),
                Mathf.Max(12f, anchor.radius),
                EventType.Repaint);
            Handles.Label(
                position + Vector3.up * 7f,
                AnchorDisplayName(anchor.type) + "\n" + anchor.stableId,
                labelStyle);
        }
    }

    static void DrawViolations(FinitePlanetCombatTerrainPlan terrain)
    {
        CombatMapValidationReport report = terrain.Validation;
        if (report?.violations == null)
            return;

        foreach (CombatMapViolation violation in report.violations)
        {
            if (violation == null)
                continue;
            Vector3 position = OnGround(terrain, violation.position, 12f);
            Handles.color = violation.severity
                == CombatMapViolationSeverity.HardError
                    ? Color.red
                    : Color.yellow;
            float size = HandleUtility.GetHandleSize(position) * 0.18f;
            Handles.SphereHandleCap(
                0,
                position,
                Quaternion.identity,
                size,
                EventType.Repaint);
            Handles.Label(
                position + Vector3.up * size,
                violation.code,
                titleStyle);
        }
    }

    static void DrawLegend(
        SceneView sceneView,
        FinitePlanetCombatTerrainPlan terrain,
        CombatSemanticPlan plan)
    {
        Handles.BeginGUI();
        Rect area = new Rect(14f, 42f, 365f, 178f);
        GUI.Box(area, GUIContent.none, EditorStyles.helpBox);
        GUILayout.BeginArea(new Rect(
            area.x + 10f,
            area.y + 8f,
            area.width - 20f,
            area.height - 16f));
        GUILayout.Label("实际战斗地形 · PCG 约束叠加", EditorStyles.boldLabel);
        GUILayout.Label(
            "任务 " + terrain.MissionKind
            + "    气候 " + terrain.Climate
            + "    Seed " + terrain.Seed);
        GUILayout.Label(
            "山体基准 " + terrain.Settings.mountainHeight.ToString("F0")
            + "m    最高地形约 "
            + terrain.MaximumTerrainHeightAboveBase.ToString("F0") + "m");
        GUILayout.Label(
            "橙/紫：抬高山脊与高台    青：必须保持低矮的盆地/航道");
        GUILayout.Label(
            "蓝/绿/红路线：敌机进场与战术航线    方框：可用空域");
        GUILayout.Label(
            "得分 " + (terrain.Validation != null
                ? terrain.Validation.score.ToString("F1")
                : "-")
            + "    Checksum " + ShortChecksum(plan.checksum));
        GUILayout.Label("关闭：Tools > Planet Combat > Scene PCG Overlay");
        GUILayout.EndArea();
        Handles.EndGUI();
    }

    static Vector3 OnGround(
        FinitePlanetCombatTerrainPlan terrain,
        Vector3 source,
        float lift)
    {
        return new Vector3(
            source.x,
            GroundHeight(terrain, source.x, source.z) + lift,
            source.z);
    }

    static float GroundHeight(
        FinitePlanetCombatTerrainPlan terrain,
        float x,
        float z)
    {
        return terrain.SampleHeight(x, z);
    }

    static void DrawPlanarCapsule(
        Vector3 start,
        Vector3 end,
        float radius,
        Color color)
    {
        Handles.color = color;
        float safeRadius = Mathf.Max(1f, radius);
        Vector3 planar = end - start;
        planar.y = 0f;
        if (planar.sqrMagnitude < 0.01f)
        {
            Handles.DrawWireDisc(start, Vector3.up, safeRadius);
            return;
        }

        Vector3 normal = Vector3.Cross(Vector3.up, planar.normalized);
        Handles.DrawWireDisc(start, Vector3.up, safeRadius);
        Handles.DrawWireDisc(end, Vector3.up, safeRadius);
        Handles.DrawAAPolyLine(2.5f,
            start + normal * safeRadius,
            end + normal * safeRadius);
        Handles.DrawAAPolyLine(2.5f,
            start - normal * safeRadius,
            end - normal * safeRadius);
    }

    static Color StampColor(CombatTerrainStampType type)
    {
        switch (type)
        {
            case CombatTerrainStampType.RidgeCapsule:
                return RidgeColor;
            case CombatTerrainStampType.MesaCapsule:
                return MesaColor;
            case CombatTerrainStampType.Basin:
            case CombatTerrainStampType.Corridor:
                return ProtectedColor;
            case CombatTerrainStampType.RoadBed:
                return new Color(0.2f, 0.65f, 1f, 0.88f);
            default:
                return new Color(1f, 0.82f, 0.12f, 0.88f);
        }
    }

    static Color RouteColor(CombatRouteType type)
    {
        switch (type)
        {
            case CombatRouteType.TerrainMaskedFlank:
                return new Color(0.1f, 1f, 0.7f, 0.9f);
            case CombatRouteType.LongRange:
                return new Color(1f, 0.35f, 0.12f, 0.9f);
            case CombatRouteType.Retreat:
                return new Color(0.65f, 0.5f, 1f, 0.88f);
            case CombatRouteType.EnemyIngress:
                return new Color(1f, 0.16f, 0.12f, 0.92f);
            default:
                return new Color(0.15f, 0.75f, 1f, 0.9f);
        }
    }

    static Color AnchorColor(CombatAnchorType type)
    {
        switch (type)
        {
            case CombatAnchorType.PlayerSpawn:
                return new Color(0.1f, 0.75f, 1f, 0.92f);
            case CombatAnchorType.EnemySpawn:
            case CombatAnchorType.EnemyIngress:
                return new Color(1f, 0.2f, 0.15f, 0.92f);
            case CombatAnchorType.CentralConflict:
                return new Color(1f, 0.8f, 0.1f, 0.92f);
            case CombatAnchorType.PowerPosition:
                return new Color(1f, 0.25f, 0.85f, 0.92f);
            default:
                return new Color(0.65f, 0.75f, 1f, 0.85f);
        }
    }

    static Color VolumeColor(CombatTacticalVolumeType type)
    {
        switch (type)
        {
            case CombatTacticalVolumeType.SpawnBasin:
                return new Color(0.15f, 0.78f, 1f, 0.78f);
            case CombatTacticalVolumeType.ManeuverBowl:
                return new Color(1f, 0.72f, 0.08f, 0.82f);
            case CombatTacticalVolumeType.OcclusionGate:
                return new Color(0.15f, 1f, 0.55f, 0.82f);
            case CombatTacticalVolumeType.ExposureLane:
                return new Color(0.28f, 0.58f, 1f, 0.82f);
            default:
                return new Color(0.82f, 0.3f, 1f, 0.78f);
        }
    }

    static string StampDisplayName(CombatTerrainStampType type)
    {
        switch (type)
        {
            case CombatTerrainStampType.RidgeCapsule: return "山脊塑形";
            case CombatTerrainStampType.MesaCapsule: return "高台塑形";
            case CombatTerrainStampType.Basin: return "低地保护";
            case CombatTerrainStampType.Corridor: return "航道保护";
            case CombatTerrainStampType.RoadBed: return "道路低床";
            default: return "建筑平台";
        }
    }

    static string RouteDisplayName(CombatRouteType type)
    {
        switch (type)
        {
            case CombatRouteType.Main: return "主交战航线";
            case CombatRouteType.TerrainMaskedFlank: return "地形遮挡侧翼";
            case CombatRouteType.LongRange: return "远程火力航线";
            case CombatRouteType.Retreat: return "恢复/撤退航线";
            default: return "敌机进场航线";
        }
    }

    static string VolumeDisplayName(CombatTacticalVolumeType type)
    {
        switch (type)
        {
            case CombatTacticalVolumeType.SpawnBasin: return "出生盆地";
            case CombatTacticalVolumeType.ManeuverBowl: return "回旋交战盆地";
            case CombatTacticalVolumeType.OcclusionGate: return "遮断门";
            case CombatTacticalVolumeType.ExposureLane: return "高风险暴露线";
            default: return "恢复与重新接敌空间";
        }
    }

    static string AnchorDisplayName(CombatAnchorType type)
    {
        switch (type)
        {
            case CombatAnchorType.PlayerSpawn: return "玩家出生区";
            case CombatAnchorType.EnemySpawn: return "主要敌方出生区";
            case CombatAnchorType.EnemyIngress: return "敌军入口";
            case CombatAnchorType.CentralConflict: return "中央交战区";
            case CombatAnchorType.PlayerRetreat: return "玩家撤退点";
            case CombatAnchorType.EnemyRetreat: return "敌方撤退点";
            case CombatAnchorType.PowerPosition: return "地形强势位置";
            case CombatAnchorType.Landmark: return "中央地标";
            default: return type.ToString();
        }
    }

    static string ShortChecksum(string checksum)
    {
        if (string.IsNullOrEmpty(checksum))
            return "-";
        return checksum.Length <= 10 ? checksum : checksum.Substring(0, 10);
    }

    static void EnsureStyles()
    {
        if (labelStyle != null)
            return;

        labelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(0.82f, 0.96f, 1f, 1f) },
            alignment = TextAnchor.MiddleCenter,
            fontSize = 10
        };
        titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            normal = { textColor = Color.white },
            alignment = TextAnchor.MiddleCenter,
            fontSize = 11
        };
    }
}
