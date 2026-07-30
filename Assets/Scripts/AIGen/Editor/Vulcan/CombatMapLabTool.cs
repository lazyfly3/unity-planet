#if UNITY_EDITOR

using System;
using System.Collections;
using System.Reflection;
using System.Text;
using KDL.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.CombatMap;
using UnityPlanet.CombatMap.Editor;

namespace KDL.Editor.Vulcan
{
    /// <summary>
    /// Project-scoped AI entry points for the semantic CombatMapLab.
    /// </summary>
    public static class CombatMapLabTool
    {
        [AICallable(
            "重建隔离的 CombatMapLab 场景，确保默认配方存在，"
            + "生成默认候选方案，并保持其不加入构建设置。",
            Category = "CombatMap.Lab",
            Kind = ToolKind.Write)]
        public static string RebuildLabScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "错误：播放模式下无法重建 CombatMapLab。";

            CombatMapLabAssetBuilder.RebuildLaboratoryAssets();
            return AssetDatabase.LoadAssetAtPath<SceneAsset>(
                       CombatMapLabAssetBuilder.ScenePath) != null
                ? "成功：已重建="
                  + CombatMapLabAssetBuilder.ScenePath
                : "错误：未能创建 CombatMapLab 场景。";
        }

        [AICallable(
            "以单场景模式打开隔离的 CombatMapLab 场景。",
            Category = "CombatMap.Lab",
            Kind = ToolKind.Write)]
        public static string OpenLabScene()
        {
            bool opened =
                CombatMapLabAssetBuilder.OpenLaboratoryScene();
            return opened
                ? "成功：已打开="
                  + SceneManager.GetActiveScene().path
                : "错误：无法打开 CombatMapLab 场景。";
        }

        [AICallable(
            "使用指定种子生成非持久化的 CombatMapLab 预览。"
            + "参数：seed=7319",
            Category = "CombatMap.Lab",
            Kind = ToolKind.Write)]
        public static string GeneratePreview(int seed = 7319)
        {
            Component controller;
            string error;
            if (!TryGetController(out controller, out error))
                return error;

            CombatMapRuntimeController runtime =
                controller as CombatMapRuntimeController;
            if (runtime == null)
                return "错误：CombatMapRuntimeController 不兼容。";
            CombatMapGenerationResult value =
                runtime.GenerateBestPreview(seed);
            return FormatGeneration(value, "预览");
        }

        [AICallable(
            "使用指定种子生成、验证并应用 CombatMapLab 候选方案。"
            + "仅当候选方案可提交时才会修改场景。参数：seed=7319",
            Category = "CombatMap.Lab",
            Kind = ToolKind.Write)]
        public static string ApplyCandidate(int seed = 7319)
        {
            if (Application.isPlaying)
                return "错误：持久化应用候选方案仅支持编辑模式。";

            Component controller;
            string error;
            if (!TryGetController(out controller, out error))
                return error;

            CombatMapRuntimeController runtime =
                controller as CombatMapRuntimeController;
            if (runtime == null)
                return "错误：CombatMapRuntimeController 不兼容。";
            AirCombatMapSettings settings = runtime.Recipe != null
                ? runtime.Recipe.CreateValidatedSettings(seed)
                : AirCombatMapSettings.CreateDefault()
                    .ValidatedCopy(seed);
            CombatMapGenerationResult candidate =
                CombatMapGenerator.GenerateBest(
                    settings,
                    runtime.transform.position
                    + settings.mapCenterOffset,
                    seed);
            if (candidate != null && !candidate.CanCommit)
            {
                return "错误：候选方案未通过验证。\n"
                    + FormatGeneration(candidate, "候选方案");
            }

            if (candidate == null)
                return "错误：生成过程未返回候选方案。";
            if (!runtime.ApplyCandidate(seed))
                return "错误：无法应用候选方案。";

            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(
                controller.gameObject.scene);
            EditorSceneManager.SaveScene(
                controller.gameObject.scene);

            return "成功：已应用种子=" + seed + "\n"
                + FormatGeneration(
                    runtime.CurrentResult ?? candidate,
                    "已应用");
        }

        [AICallable(
            "验证当前已应用的 CombatMapLab 候选方案，"
            + "并返回评分和硬错误。",
            Category = "CombatMap.Lab")]
        public static string ValidateCurrentCandidate()
        {
            Component controller;
            string error;
            if (!TryGetController(out controller, out error))
                return error;

            CombatMapRuntimeController runtime =
                controller as CombatMapRuntimeController;
            if (runtime == null)
                return "错误：CombatMapRuntimeController 不兼容。";
            return FormatValidation(
                runtime.ValidateCurrentCandidate());
        }

        [AICallable(
            "检查当前 CombatMapLab 的战斗语义：种子、校验和、"
            + "战术体积、曲率航路、SDF 地形特征、多高度视线"
            + "和验证状态。",
            Category = "CombatMap.Lab")]
        public static string InspectCombatSemantics()
        {
            Component controller;
            string error;
            if (!TryGetController(out controller, out error))
                return error;

            CombatSemanticPlan plan =
                CombatMapLabAssetBuilder.ReadControllerMember(
                    controller,
                    "CurrentPlan",
                    "Plan",
                    "currentPlan") as CombatSemanticPlan;
            CombatMapValidationReport report =
                CombatMapLabAssetBuilder.ReadControllerMember(
                    controller,
                    "CurrentValidation",
                    "Validation",
                    "currentValidation")
                as CombatMapValidationReport;
            if (plan == null)
                return "错误：没有已应用的语义方案。";

            var builder = new StringBuilder();
            builder.Append("种子=").Append(plan.seed)
                .Append(" 拓扑变体=").Append(plan.topologyVariant)
                .Append(" 校验和=").Append(plan.checksum)
                .Append(" 中心=").Append(plan.mapCenter)
                .Append(" 地图尺寸=")
                .Append(plan.mapSize.ToString("0.##"))
                .Append(" 边界警告半径=")
                .Append(plan.warningRadius.ToString("0.##"))
                .Append(" 判负半径=")
                .Append(plan.forfeitRadius.ToString("0.##"))
                .Append('\n');
            builder.Append("战术体积数=")
                .Append(plan.tacticalVolumes != null
                    ? plan.tacticalVolumes.Length
                    : 0)
                .Append(" 锚点数=")
                .Append(plan.anchors != null
                    ? plan.anchors.Length
                    : 0)
                .Append(" 路线数=")
                .Append(plan.routes != null
                    ? plan.routes.Length
                    : 0)
                .Append(" 地形特征数=")
                .Append(plan.terrainStamps != null
                    ? plan.terrainStamps.Length
                    : 0)
                .Append(" 遮挡物数=")
                .Append(plan.occluders != null
                    ? plan.occluders.Length
                    : 0);

            if (plan.tacticalVolumes != null)
            {
                foreach (CombatTacticalVolume volume
                         in plan.tacticalVolumes)
                {
                    if (volume == null)
                        continue;
                    builder.Append('\n')
                        .Append("战术体积 ")
                        .Append(volume.stableId)
                        .Append(" 类型=")
                        .Append(FormatVolumeType(volume.type))
                        .Append(" 位置=")
                        .Append(volume.position)
                        .Append(" 尺寸=")
                        .Append(volume.size)
                        .Append(" 目标离地=")
                        .Append(
                            volume.preferredClearance
                                .ToString("0.##"));
                }
            }

            if (plan.anchors != null)
            {
                foreach (CombatSemanticAnchor anchor
                         in plan.anchors)
                {
                    if (anchor == null)
                        continue;
                    builder.Append('\n')
                        .Append("锚点 ")
                        .Append(anchor.stableId)
                        .Append(" 类型=")
                        .Append(FormatAnchorType(anchor.type))
                        .Append(" 位置=").Append(anchor.position)
                        .Append(" 半径=")
                        .Append(anchor.radius.ToString("0.##"));
                }
            }

            if (plan.routes != null)
            {
                foreach (CombatSemanticRoute route in plan.routes)
                {
                    if (route == null)
                        continue;
                    builder.Append('\n')
                        .Append("路线 ")
                        .Append(route.stableId)
                        .Append(" 类型=")
                        .Append(FormatRouteType(route.type))
                        .Append(" 宽度=")
                        .Append(route.width.ToString("0.##"))
                        .Append(" 长度=")
                        .Append(route.Length.ToString("0.##"))
                        .Append(" 语义节点数=")
                        .Append(route.controlVolumeIds != null
                            ? route.controlVolumeIds.Length
                            : 0)
                        .Append(" 路径点数=")
                        .Append(route.waypoints != null
                            ? route.waypoints.Length
                            : 0);
                }
            }

            builder.Append('\n').Append(
                FormatValidation(report));
            return builder.ToString();
        }

        [AICallable(
            "截取一张 1600x900 的 CombatMapLab 全景预览，"
            + "并返回 PNG 的绝对路径。",
            Category = "CombatMap.Lab",
            Kind = ToolKind.Write)]
        public static string CapturePreview()
        {
            if (SceneManager.GetActiveScene().path
                != CombatMapLabAssetBuilder.ScenePath
                && !CombatMapLabAssetBuilder
                    .OpenLaboratoryScene())
            {
                return "错误：无法打开 CombatMapLab 场景。";
            }
            return "成功：路径=" + CombatMapLabAssetBuilder
                .CapturePreview(1600, 900);
        }

        static bool TryGetController(
            out Component controller,
            out string error)
        {
            controller = null;
            error = string.Empty;
            if (SceneManager.GetActiveScene().path
                != CombatMapLabAssetBuilder.ScenePath)
            {
                if (!CombatMapLabAssetBuilder
                        .OpenLaboratoryScene())
                {
                    error =
                        "错误：无法打开 CombatMapLab 场景。";
                    return false;
                }
            }

            controller =
                CombatMapLabAssetBuilder.FindController();
            if (controller != null)
                return true;

            error =
                "错误：缺少 CombatMapRuntimeController。";
            return false;
        }

        static bool TryInvoke(
            Component controller,
            string[] methodNames,
            object[] arguments,
            out object result,
            out string error)
        {
            result = null;
            error = string.Empty;
            if (controller == null)
            {
                error = "错误：缺少控制器。";
                return false;
            }

            const BindingFlags flags =
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic;
            Type type = controller.GetType();
            foreach (string methodName in methodNames)
            {
                foreach (MethodInfo method
                         in type.GetMethods(flags))
                {
                    if (!string.Equals(
                            method.Name,
                            methodName,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    ParameterInfo[] parameters =
                        method.GetParameters();
                    if (parameters.Length != arguments.Length)
                        continue;
                    bool compatible = true;
                    for (int index = 0;
                         index < parameters.Length;
                         index++)
                    {
                        object argument = arguments[index];
                        if (argument != null
                            && !parameters[index].ParameterType
                                .IsInstanceOfType(argument))
                        {
                            compatible = false;
                            break;
                        }
                    }
                    if (!compatible)
                        continue;

                    try
                    {
                        result = method.Invoke(
                            controller,
                            arguments);
                        return true;
                    }
                    catch (TargetInvocationException exception)
                    {
                        Exception cause =
                            exception.InnerException ?? exception;
                        Debug.LogException(cause);
                        error = "错误：" + cause.Message;
                        return false;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                        error = "错误：" + exception.Message;
                        return false;
                    }
                }
            }

            error = "错误：没有兼容的方法："
                + string.Join("/", methodNames);
            return false;
        }

        static CombatMapGenerationResult ExtractBestCandidate(
            object value)
        {
            CombatMapGenerationResult direct =
                value as CombatMapGenerationResult;
            if (direct != null)
                return direct;
            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null || value is string)
                return null;

            CombatMapGenerationResult result = null;
            foreach (object item in enumerable)
            {
                CombatMapGenerationResult candidate =
                    item as CombatMapGenerationResult;
                if (candidate == null)
                    continue;
                if (result == null
                    || CompareCandidates(candidate, result) > 0)
                {
                    result = candidate;
                }
            }
            return result;
        }

        static string FormatGeneration(
            object value,
            string label)
        {
            CombatMapGenerationResult candidate =
                ExtractBestCandidate(value);
            if (candidate == null)
            {
                return value == null
                    ? label + "=成功"
                    : label + "=" + value;
            }

            var builder = new StringBuilder();
            builder.Append(label)
                .Append(" 候选索引=")
                .Append(candidate.candidateIndex)
                .Append(" 派生种子=")
                .Append(candidate.derivedSeed)
                .Append(" 可提交=")
                .Append(FormatBoolean(candidate.CanCommit));
            if (candidate.plan != null)
            {
                builder.Append(" 校验和=")
                    .Append(candidate.plan.checksum)
                    .Append(" 战术体积数=")
                    .Append(candidate.plan.tacticalVolumes != null
                        ? candidate.plan.tacticalVolumes.Length
                        : 0)
                    .Append(" 锚点数=")
                    .Append(candidate.plan.anchors != null
                        ? candidate.plan.anchors.Length
                        : 0)
                    .Append(" 路线数=")
                    .Append(candidate.plan.routes != null
                        ? candidate.plan.routes.Length
                        : 0);
            }
            if (candidate.validation != null)
            {
                builder.Append('\n').Append(
                    FormatValidation(candidate.validation));
            }
            return builder.ToString();
        }

        static string FormatValidation(
            CombatMapValidationReport report)
        {
            if (report == null)
                return "验证结果=缺失";

            var builder = new StringBuilder();
            builder.Append("得分=")
                .Append(report.score.ToString("0.##"))
                .Append(" 最低提交分=")
                .Append(
                    report.minimumCommitScore.ToString("0.##"))
                .Append(" 可提交=")
                .Append(FormatBoolean(report.CanCommit))
                .Append(" 存在硬错误=")
                .Append(FormatBoolean(report.HasHardErrors))
                .Append(" 校验和=")
                .Append(report.checksum);
            builder.Append('\n')
                .Append("尺度适配=")
                .Append(
                    report.scaleCompatibilityScore
                        .ToString("0.#"))
                .Append(" 拓扑韧性=")
                .Append(report.topologyScore.ToString("0.#"))
                .Append(" 遮挡节奏=")
                .Append(report.coverRhythmScore.ToString("0.#"))
                .Append(" 机动可行性=")
                .Append(report.kinematicScore.ToString("0.#"));
            builder.Append('\n')
                .Append("首次接触=")
                .Append(report.firstContactSeconds.ToString("0.#"))
                .Append("秒 平均遮挡=")
                .Append(report.meanOcclusionSeconds.ToString("0.#"))
                .Append("秒 最大暴露=")
                .Append(report.maximumExposureSeconds.ToString("0.#"))
                .Append("秒 最小转弯半径=")
                .Append(report.minimumTurnRadius.ToString("0.#"))
                .Append("米 最小盆地直径=")
                .Append(
                    report.minimumManeuverDiameter
                        .ToString("0.#"))
                .Append("米");
            builder.Append('\n')
                .Append("最少出口=")
                .Append(report.minimumExitCount)
                .Append(" 割点=")
                .Append(report.articulationPointCount)
                .Append(" 全图眼位=")
                .Append(report.globalEyePointCount)
                .Append(" 路线选择熵=")
                .Append(report.routeUsageEntropy.ToString("0.##"))
                .Append(" 最大支配率=")
                .Append(
                    report.maximumRouteDominance
                        .ToString("P0"));
            if (report.violations != null)
            {
                foreach (CombatMapViolation violation
                         in report.violations)
                {
                    if (violation == null)
                        continue;
                    builder.Append('\n')
                        .Append(FormatSeverity(violation.severity))
                        .Append(' ')
                        .Append(violation.code)
                        .Append(": ")
                        .Append(violation.message);
                }
            }
            return builder.ToString();
        }

        static string FormatAnchorType(CombatAnchorType type)
        {
            switch (type)
            {
                case CombatAnchorType.PlayerSpawn:
                    return "玩家出生点";
                case CombatAnchorType.EnemySpawn:
                    return "敌方出生点";
                case CombatAnchorType.CentralConflict:
                    return "中央冲突点";
                case CombatAnchorType.PlayerRetreat:
                    return "玩家撤退点";
                case CombatAnchorType.EnemyRetreat:
                    return "敌方撤退点";
                case CombatAnchorType.PowerPosition:
                    return "优势位置";
                case CombatAnchorType.Landmark:
                    return "地标";
                default:
                    return type.ToString();
            }
        }

        static string FormatRouteType(CombatRouteType type)
        {
            switch (type)
            {
                case CombatRouteType.Main:
                    return "主路线";
                case CombatRouteType.TerrainMaskedFlank:
                    return "地形遮蔽侧翼路线";
                case CombatRouteType.LongRange:
                    return "远程路线";
                case CombatRouteType.Retreat:
                    return "撤退路线";
                default:
                    return type.ToString();
            }
        }

        static string FormatVolumeType(
            CombatTacticalVolumeType type)
        {
            switch (type)
            {
                case CombatTacticalVolumeType.SpawnBasin:
                    return "出生盆地";
                case CombatTacticalVolumeType.ManeuverBowl:
                    return "回旋交战盆地";
                case CombatTacticalVolumeType.OcclusionGate:
                    return "遮断门";
                case CombatTacticalVolumeType.ExposureLane:
                    return "高风险暴露线";
                case CombatTacticalVolumeType.RecoveryPocket:
                    return "恢复与重新接敌空间";
                default:
                    return type.ToString();
            }
        }

        static int CompareCandidates(
            CombatMapGenerationResult first,
            CombatMapGenerationResult second)
        {
            bool firstValid = first != null && first.CanCommit;
            bool secondValid = second != null && second.CanCommit;
            if (firstValid != secondValid)
                return firstValid ? 1 : -1;
            float firstCritical = MinimumCritical(
                first?.validation);
            float secondCritical = MinimumCritical(
                second?.validation);
            int critical = firstCritical.CompareTo(secondCritical);
            if (critical != 0)
                return critical;
            return (first?.validation?.score ?? float.MinValue)
                .CompareTo(
                    second?.validation?.score
                    ?? float.MinValue);
        }

        static float MinimumCritical(
            CombatMapValidationReport report)
        {
            if (report == null)
                return float.MinValue;
            return Mathf.Min(
                report.scaleCompatibilityScore,
                Mathf.Min(
                    report.topologyScore,
                    Mathf.Min(
                        report.coverRhythmScore,
                        report.kinematicScore)));
        }

        static string FormatSeverity(
            CombatMapViolationSeverity severity)
        {
            switch (severity)
            {
                case CombatMapViolationSeverity.Warning:
                    return "警告";
                case CombatMapViolationSeverity.HardError:
                    return "硬错误";
                default:
                    return severity.ToString();
            }
        }

        static string FormatBoolean(bool value)
        {
            return value ? "是" : "否";
        }
    }
}

#endif
