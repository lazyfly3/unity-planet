using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityPlanet.CityPcg;

[InitializeOnLoad]
public static class CombatCitySceneOpportunityOverlay
{
    const string MenuPath = "Tools/城市 PCG/Scene 显示战术区域圆圈";
    const string PreferenceKey =
        "UnityPlanet.CombatCity.SceneOpportunityCircles";
    const int CircleSegments = 64;

    static GUIStyle labelStyle;

    static CombatCitySceneOpportunityOverlay()
    {
        SceneView.duringSceneGui += Draw;
    }

    [MenuItem(MenuPath)]
    static void Toggle()
    {
        Enabled = !Enabled;
        SceneView.RepaintAll();
    }

    [MenuItem(MenuPath, true)]
    static bool ValidateToggle()
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

    static void Draw(SceneView sceneView)
    {
        if (!Enabled)
            return;

        AirCombatCityPcgLab[] labs = Object.FindObjectsOfType<
            AirCombatCityPcgLab>(true);
        if (labs == null || labs.Length == 0)
            return;

        EnsureStyle();
        CompareFunction oldZTest = Handles.zTest;
        Color oldColor = Handles.color;
        Handles.zTest = CompareFunction.Always;
        for (int labIndex = 0; labIndex < labs.Length; labIndex++)
        {
            AirCombatCityPcgLab lab = labs[labIndex];
            AirCombatCityPlan plan = lab != null ? lab.Plan : null;
            if (plan == null || !lab.gameObject.scene.IsValid())
                continue;
            DrawTacticalBlockCoverage(lab.transform, plan);
            DrawBoundaryAirWalls(lab.transform, plan);
            for (int i = 0; i < plan.opportunities.Count; i++)
            {
                TacticalOpportunity opportunity = plan.opportunities[i];
                if (opportunity != null)
                    DrawOpportunity(lab.transform, opportunity);
            }
        }
        Handles.color = oldColor;
        Handles.zTest = oldZTest;
    }

    static void DrawBoundaryAirWalls(
        Transform cityRoot,
        AirCombatCityPlan plan)
    {
        if (plan.boundaryWalls == null || plan.boundaryWalls.Count == 0)
            return;

        Matrix4x4 oldMatrix = Handles.matrix;
        Color oldColor = Handles.color;
        Handles.matrix = cityRoot.localToWorldMatrix;
        Handles.color = new Color(1f, 0.08f, 0.12f, 0.95f);
        for (int i = 0; i < plan.boundaryWalls.Count; i++)
        {
            AirCombatBoundaryWallPlan wall = plan.boundaryWalls[i];
            if (wall != null)
                Handles.DrawWireCube(wall.center, wall.size);
        }
        Handles.matrix = oldMatrix;
        Handles.color = oldColor;

        for (int i = 0; i < plan.boundaryWalls.Count; i++)
        {
            AirCombatBoundaryWallPlan labelWall = plan.boundaryWalls[i];
            if (labelWall == null)
                continue;
            Vector3 label = cityRoot.TransformPoint(new Vector3(
                labelWall.center.x,
                42f,
                labelWall.center.z));
            Handles.Label(label, "【空气墙·高楼后·真实碰撞】", labelStyle);
        }
    }

    static void DrawTacticalBlockCoverage(
        Transform cityRoot,
        AirCombatCityPlan plan)
    {
        var regions = new Dictionary<string, TacticalRegionDisplay>();
        var labelledMergeGroups = new HashSet<string>();
        float scale = Mathf.Max(
            cityRoot.lossyScale.x,
            cityRoot.lossyScale.z);
        scale = Mathf.Max(0.01f, scale);

        for (int i = 0; i < plan.tacticalBlocks.Count; i++)
        {
            CombatCityBlockPlan block = plan.tacticalBlocks[i];
            if (block == null)
                continue;

            Color color = ColorFor(block.role);
            Vector3 center = cityRoot.TransformPoint(new Vector3(
                block.bounds.center.x,
                6f,
                block.bounds.center.z));
            float radius = Mathf.Max(
                22f,
                Mathf.Min(block.bounds.size.x, block.bounds.size.z) *
                0.38f * scale);
            DrawThickCircle(
                center,
                radius,
                new Color(color.r, color.g, color.b, 0.42f),
                2.2f);
            Handles.color = new Color(color.r, color.g, color.b, 0.60f);
            Handles.DrawSolidDisc(center, Vector3.up, 2.2f * scale);

            if (!regions.TryGetValue(
                    block.tacticalRegionId,
                    out TacticalRegionDisplay region))
            {
                region = new TacticalRegionDisplay
                {
                    role = block.role,
                    bounds = block.bounds,
                    labelAnchor = block.bounds.center,
                    blockCount = 1
                };
            }
            else
            {
                region.bounds.Encapsulate(block.bounds.min);
                region.bounds.Encapsulate(block.bounds.max);
                region.blockCount++;
            }
            regions[block.tacticalRegionId] = region;

            DrawRemovedRoadSeam(
                cityRoot,
                block,
                labelledMergeGroups);
        }

        foreach (KeyValuePair<string, TacticalRegionDisplay> pair in regions)
        {
            TacticalRegionDisplay region = pair.Value;
            Color color = ColorFor(region.role);
            Vector3 center = cityRoot.TransformPoint(new Vector3(
                region.bounds.center.x,
                8f,
                region.bounds.center.z));
            float radius = Mathf.Max(
                30f,
                Mathf.Max(region.bounds.extents.x, region.bounds.extents.z) *
                0.92f * scale);
            DrawThickCircle(
                center,
                radius,
                new Color(color.r, color.g, color.b, 0.82f),
                3.6f);
            Vector3 labelPosition = cityRoot.TransformPoint(new Vector3(
                region.labelAnchor.x,
                18f,
                region.labelAnchor.z));
            Handles.Label(
                labelPosition,
                "【" + DisplayName(region.role) + " · " +
                region.blockCount + "格】",
                labelStyle);
        }
    }

    static void DrawRemovedRoadSeam(
        Transform cityRoot,
        CombatCityBlockPlan block,
        HashSet<string> labelledMergeGroups)
    {
        if (!block.mergeEast && !block.mergeNorth)
            return;
        Color color = new Color(1f, 0.18f, 0.72f, 0.95f);
        if (block.mergeEast)
        {
            DrawRemovedRoadSeam(
                cityRoot,
                new Vector3(block.bounds.max.x, 7f,
                    block.bounds.min.z + 8f),
                new Vector3(block.bounds.max.x, 7f,
                    block.bounds.max.z - 8f),
                color,
                labelledMergeGroups.Add(block.mergedGroupId));
        }
        if (block.mergeNorth)
        {
            DrawRemovedRoadSeam(
                cityRoot,
                new Vector3(block.bounds.min.x + 8f, 7f,
                    block.bounds.max.z),
                new Vector3(block.bounds.max.x - 8f, 7f,
                    block.bounds.max.z),
                color,
                labelledMergeGroups.Add(block.mergedGroupId));
        }
    }

    static void DrawRemovedRoadSeam(
        Transform cityRoot,
        Vector3 localStart,
        Vector3 localEnd,
        Color color,
        bool showLabel)
    {
        Vector3 start = cityRoot.TransformPoint(localStart);
        Vector3 end = cityRoot.TransformPoint(localEnd);
        Vector3 center = (start + end) * 0.5f;
        Vector3 direction = (end - start).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, direction) * 11f;
        Handles.color = color;
        Handles.DrawAAPolyLine(5f, start, end);
        Handles.DrawAAPolyLine(5f, center - side, center + side);
        if (showLabel)
            Handles.Label(center + Vector3.up * 8f, "合并街区：内部道路已移除", labelStyle);
    }

    sealed class TacticalRegionDisplay
    {
        public CombatCityBlockRole role;
        public Bounds bounds;
        public Vector3 labelAnchor;
        public int blockCount;
    }

    static void DrawOpportunity(
        Transform cityRoot,
        TacticalOpportunity opportunity)
    {
        Color color = ColorFor(opportunity.kind);
        float altitude = DisplayAltitude(opportunity);
        Vector3 localCenter = new Vector3(
            opportunity.bounds.center.x,
            altitude,
            opportunity.bounds.center.z);
        Vector3 center = cityRoot.TransformPoint(localCenter);
        float scale = Mathf.Max(
            cityRoot.lossyScale.x,
            cityRoot.lossyScale.z);
        float radius = DisplayRadius(opportunity) * Mathf.Max(0.01f, scale);

        Handles.color = color;
        DrawThickCircle(center, radius, color, 7f);
        DrawThickCircle(center, Mathf.Max(8f, radius - 7f),
            new Color(color.r, color.g, color.b, 0.72f), 3f);
        Handles.DrawSolidDisc(center, Vector3.up, Mathf.Max(3f, radius * 0.055f));

        DrawDirectionNodes(cityRoot, opportunity.entrances, altitude,
            center, color, false);
        DrawDirectionNodes(cityRoot, opportunity.exits, altitude,
            center, color, true);

        Handles.Label(
            center + Vector3.up * 12f,
            "【" + DisplayName(opportunity.kind) + "】",
            labelStyle);
    }

    static void DrawDirectionNodes(
        Transform cityRoot,
        Vector3[] points,
        float altitude,
        Vector3 center,
        Color color,
        bool exit)
    {
        if (points == null)
            return;
        Handles.color = color;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 point = cityRoot.TransformPoint(new Vector3(
                points[i].x,
                altitude,
                points[i].z));
            Handles.DrawAAPolyLine(exit ? 4f : 2.5f, center, point);
            if (exit)
                Handles.DrawWireDisc(point, Vector3.up, 9f);
            else
                Handles.DrawSolidDisc(point, Vector3.up, 6f);
        }
    }

    static void DrawThickCircle(
        Vector3 center,
        float radius,
        Color color,
        float width)
    {
        var points = new Vector3[CircleSegments + 1];
        for (int i = 0; i <= CircleSegments; i++)
        {
            float angle = i / (float)CircleSegments * Mathf.PI * 2f;
            points[i] = center + new Vector3(
                Mathf.Cos(angle) * radius,
                0f,
                Mathf.Sin(angle) * radius);
        }
        Handles.color = color;
        Handles.DrawAAPolyLine(width, points);
    }

    static float DisplayRadius(TacticalOpportunity opportunity)
    {
        if (opportunity.kind == TacticalOpportunityKind.KiteLoop)
            return Mathf.Clamp(opportunity.loopRadius, 55f, 170f);
        float horizontal = Mathf.Min(
            opportunity.bounds.size.x,
            opportunity.bounds.size.z);
        return Mathf.Clamp(horizontal * 0.42f, 42f, 150f);
    }

    static float DisplayAltitude(TacticalOpportunity opportunity)
    {
        switch (opportunity.kind)
        {
            case TacticalOpportunityKind.RecoveryPocket:
            case TacticalOpportunityKind.DestructionAmbush:
            case TacticalOpportunityKind.TacticalChoke:
                return Mathf.Max(8f, opportunity.bounds.min.y + 8f);
            case TacticalOpportunityKind.AttackPerch:
                return opportunity.bounds.max.y + 4f;
            default:
                return opportunity.bounds.center.y;
        }
    }

    static Color ColorFor(TacticalOpportunityKind kind)
    {
        switch (kind)
        {
            case TacticalOpportunityKind.ManeuverBowl:
                return new Color(0.2f, 1f, 0.72f, 1f);
            case TacticalOpportunityKind.OcclusionChain:
                return new Color(0.08f, 1f, 0.30f, 1f);
            case TacticalOpportunityKind.ExposureShortcut:
                return new Color(1f, 0.48f, 0.04f, 1f);
            case TacticalOpportunityKind.RecoveryPocket:
                return new Color(0.02f, 0.92f, 1f, 1f);
            case TacticalOpportunityKind.KiteLoop:
                return new Color(0.78f, 0.25f, 1f, 1f);
            case TacticalOpportunityKind.TacticalChoke:
                return new Color(1f, 0.72f, 0.04f, 1f);
            case TacticalOpportunityKind.VerticalEscape:
                return new Color(0.18f, 0.65f, 1f, 1f);
            case TacticalOpportunityKind.AttackPerch:
                return new Color(1f, 0.92f, 0.08f, 1f);
            case TacticalOpportunityKind.DestructionAmbush:
                return new Color(1f, 0.10f, 0.05f, 1f);
            default:
                return Color.white;
        }
    }

    static Color ColorFor(CombatCityBlockRole role)
    {
        switch (role)
        {
            case CombatCityBlockRole.Occlusion:
                return new Color(0.08f, 1f, 0.30f, 1f);
            case CombatCityBlockRole.Exposure:
                return new Color(1f, 0.48f, 0.04f, 1f);
            case CombatCityBlockRole.Recovery:
                return new Color(0.02f, 0.92f, 1f, 1f);
            case CombatCityBlockRole.Kite:
                return new Color(0.78f, 0.25f, 1f, 1f);
            case CombatCityBlockRole.TacticalChoke:
                return new Color(1f, 0.72f, 0.04f, 1f);
            case CombatCityBlockRole.Vertical:
                return new Color(0.18f, 0.65f, 1f, 1f);
            case CombatCityBlockRole.Attack:
                return new Color(1f, 0.92f, 0.08f, 1f);
            case CombatCityBlockRole.Destruction:
                return new Color(1f, 0.10f, 0.05f, 1f);
            case CombatCityBlockRole.CombatBoundary:
                return new Color(0.45f, 0.72f, 1f, 1f);
            default:
                return new Color(0.2f, 1f, 0.72f, 1f);
        }
    }

    static string DisplayName(TacticalOpportunityKind kind)
    {
        switch (kind)
        {
            case TacticalOpportunityKind.ManeuverBowl: return "中央机动街区";
            case TacticalOpportunityKind.OcclusionChain: return "少道路高楼掩体区";
            case TacticalOpportunityKind.ExposureShortcut: return "开放火力捷径";
            case TacticalOpportunityKind.RecoveryPocket: return "维修庭院";
            case TacticalOpportunityKind.KiteLoop: return "环绕街区";
            case TacticalOpportunityKind.TacticalChoke: return "战术连廊窄口";
            case TacticalOpportunityKind.VerticalEscape: return "低中空换层通道";
            case TacticalOpportunityKind.AttackPerch: return "有掩体攻击平台";
            case TacticalOpportunityKind.DestructionAmbush: return "连廊倒塌伏击区";
            default: return "战斗街区";
        }
    }

    static string DisplayName(CombatCityBlockRole role)
    {
        switch (role)
        {
            case CombatCityBlockRole.Occlusion: return "少道路高楼掩体区";
            case CombatCityBlockRole.Exposure: return "开放火力区";
            case CombatCityBlockRole.Recovery: return "恢复接近区";
            case CombatCityBlockRole.Kite: return "环绕机动区";
            case CombatCityBlockRole.TacticalChoke: return "连廊窄口区";
            case CombatCityBlockRole.Vertical: return "垂直换层区";
            case CombatCityBlockRole.Attack: return "掩体攻击区";
            case CombatCityBlockRole.Destruction: return "连廊倒塌伏击区";
            case CombatCityBlockRole.CombatBoundary: return "高楼封锁边界";
            default: return "中央机动支援区";
        }
    }

    static void EnsureStyle()
    {
        if (labelStyle != null)
            return;
        labelStyle = new GUIStyle(EditorStyles.helpBox)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(7, 7, 4, 4)
        };
        labelStyle.normal.textColor = Color.white;
    }
}
