using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ModularAssembly;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPlanet.ModularAssembly
{
    public static class VehicleAiDiagnosticExporter
    {
        const string Schema = "unity-planet.vehicle-flight-diagnostic.v1";
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        static readonly Vector3Int[] PositiveNeighbors =
        {
            Vector3Int.right,
            Vector3Int.up,
            new Vector3Int(0, 0, 1)
        };

        public static bool TryExport(
            GridAssemblyModel model,
            GridAssemblyPresenter presenter,
            RobocraftMotionCoordinator motion,
            out string path,
            out string message)
        {
            path = string.Empty;
            message = string.Empty;
            if (model == null)
            {
                message = "导出失败：当前装配模型不存在。";
                return false;
            }

            try
            {
                string directory = Path.Combine(
                    Application.persistentDataPath,
                    "VehicleDiagnostics");
                Directory.CreateDirectory(directory);
                path = Path.Combine(
                    directory,
                    "飞船AI诊断_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss", Invariant) +
                    ".md");
                File.WriteAllText(
                    path,
                    BuildReport(model, presenter, motion),
                    new UTF8Encoding(false));
                message = "AI飞行诊断文件已生成。";
                return true;
            }
            catch (Exception exception)
            {
                path = string.Empty;
                message = "导出失败：" + exception.Message;
                Debug.LogException(exception);
                return false;
            }
        }

        static string BuildReport(
            GridAssemblyModel model,
            GridAssemblyPresenter presenter,
            RobocraftMotionCoordinator motion)
        {
            GridModuleRecord[] records = model.Records
                .Where(record => record != null && record.Definition != null)
                .OrderBy(record => record.RuntimeId, StringComparer.Ordinal)
                .ToArray();
            GridAssemblyValidation validation = model.Validate();
            GridAssemblyMetrics metrics = model.CalculateMetrics();
            Dictionary<string, List<Vector3Int>> cells =
                BuildCells(model, records);
            Dictionary<string, HashSet<string>> graph =
                BuildGraph(records, cells);
            Dictionary<string, int> depth = CoreDepths(graph);
            HashSet<string> articulationPoints =
                FindArticulationPoints(graph);
            VehicleActuatorDiagnostic[] actuators = motion != null
                ? motion.CaptureActuatorDiagnostics()
                : Array.Empty<VehicleActuatorDiagnostic>();
            IReadOnlyList<ModuleAeroSurface> aeroSurfaces = motion != null
                ? motion.AeroSurfaces
                : Array.Empty<ModuleAeroSurface>();
            RobocraftTelemetry telemetry = motion != null
                ? motion.Telemetry
                : default;
            DirectionalAuthority24 authority = motion != null
                ? motion.Authority24
                : default;
            VehiclePhysicsSnapshot physics = motion != null
                ? motion.PhysicsSnapshot
                : default;
            Vector3 geometryCenter = CalculateGeometryCenter(cells);
            Vector3 thrustCenter = CalculateThrustCenter(
                actuators,
                physics.centerOfMassLocal);

            var report = new StringBuilder(65536);
            report.AppendLine("# 模块飞行器 AI 诊断报告");
            report.AppendLine();
            report.AppendLine("> 此文件由游戏直接读取当前内存中的飞船生成。");
            report.AppendLine("> 可把整个文件交给 AI，询问飞行器为什么无法起飞、持续旋转、转向迟钝、失速或制动不足。");
            report.AppendLine();
            AppendMetadata(report, model, records.Length);
            AppendCoordinateConvention(report);
            AppendValidation(report, validation, metrics);
            AppendWholeVehicle(
                report,
                motion,
                telemetry,
                physics,
                authority,
                geometryCenter,
                thrustCenter);
            AppendTopology(
                report,
                graph,
                depth,
                articulationPoints,
                validation);
            AppendModules(report, records, cells, graph, depth, articulationPoints);
            AppendFunctionalAxes(report, records, presenter);
            AppendActuators(report, actuators);
            AppendAeroSurfaces(report, aeroSurfaces);
            AppendAutomaticFindings(
                report,
                validation,
                telemetry,
                authority,
                records,
                graph,
                articulationPoints,
                geometryCenter,
                thrustCenter,
                actuators,
                aeroSurfaces,
                motion != null);
            AppendBlueprint(report, model.CaptureBlueprint());
            AppendSuggestedPrompt(report);
            return report.ToString();
        }

        static void AppendMetadata(
            StringBuilder report,
            GridAssemblyModel model,
            int moduleCount)
        {
            report.AppendLine("## 1. 报告信息");
            report.AppendLine();
            report.AppendLine($"- `schema`: `{Schema}`");
            report.AppendLine(
                $"- 导出时间：`{DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}`");
            report.AppendLine(
                $"- Unity版本：`{Application.unityVersion}`");
            report.AppendLine(
                $"- 游戏版本：`{Escape(Application.version)}`");
            report.AppendLine(
                $"- 场景：`{Escape(SceneManager.GetActiveScene().name)}`");
            report.AppendLine($"- 蓝图格式：`{ModularBlueprintData.CurrentFormatVersion}`");
            report.AppendLine($"- 模块数量：`{moduleCount}`");
            report.AppendLine($"- 核心辅助：`{model.CoreAssistMode}`");
            report.AppendLine();
        }

        static void AppendCoordinateConvention(StringBuilder report)
        {
            report.AppendLine("## 2. 坐标与单位约定");
            report.AppendLine();
            report.AppendLine("- 1个网格单位 = 1米。");
            report.AppendLine("- 飞船局部 `+Z` = 船头，`+Y` = 上甲板，`+X` = 右侧。");
            report.AppendLine("- 质量单位：kg；力：N；力矩：N·m；速度：m/s。");
            report.AppendLine("- 推进器表中的方向是**实际推力方向**，不是尾焰方向。");
            report.AppendLine("- 模块旋转使用24向离散姿态索引。");
            report.AppendLine();
        }

        static void AppendValidation(
            StringBuilder report,
            GridAssemblyValidation validation,
            GridAssemblyMetrics metrics)
        {
            report.AppendLine("## 3. 蓝图合法性与静态统计");
            report.AppendLine();
            report.AppendLine("|项目|数值|");
            report.AppendLine("|---|---:|");
            report.AppendLine($"|设计合法|{YesNo(validation.IsValid)}|");
            report.AppendLine($"|存在核心|{YesNo(validation.HasCore)}|");
            report.AppendLine($"|占格冲突|{YesNo(validation.HasOverlap)}|");
            report.AppendLine($"|断连模块|{validation.DisconnectedIds.Count}|");
            report.AppendLine($"|模块超限|{YesNo(validation.ExceedsModuleLimit)}|");
            report.AppendLine($"|能源超限|{YesNo(validation.ExceedsEnergy)}|");
            report.AppendLine($"|CPU|{validation.CpuCost}/{ModuleCpuBudget.Maximum}|");
            report.AppendLine($"|模块数|{metrics.moduleCount}|");
            report.AppendLine($"|定义质量|{F(metrics.totalMass)} kg|");
            report.AppendLine($"|能源容量|{F(metrics.energyCapacity)}|");
            report.AppendLine($"|能源预算消耗|{F(metrics.energyCost)}|");
            report.AppendLine($"|定义总推力|{F(metrics.totalThrust)} N|");
            report.AppendLine(
                $"|静态质心|{V(metrics.localCenterOfMass)} m|");
            report.AppendLine();
            if (!string.IsNullOrWhiteSpace(validation.Message))
                report.AppendLine($"验证信息：{Escape(validation.Message)}");
            report.AppendLine();
        }

        static void AppendWholeVehicle(
            StringBuilder report,
            RobocraftMotionCoordinator motion,
            RobocraftTelemetry telemetry,
            VehiclePhysicsSnapshot physics,
            DirectionalAuthority24 authority,
            Vector3 geometryCenter,
            Vector3 thrustCenter)
        {
            report.AppendLine("## 4. RC3整船物理快照");
            report.AppendLine();
            if (motion == null)
            {
                report.AppendLine("未找到RC3物理协调器，以下动态物理数据不可用。");
                report.AppendLine();
                return;
            }

            report.AppendLine("|项目|数值|");
            report.AppendLine("|---|---|");
            report.AppendLine($"|物理状态|{Escape(telemetry.status)}|");
            report.AppendLine($"|物理审计|{Escape(telemetry.physicsAudit)}|");
            report.AppendLine($"|RC3拥有物理|{YesNo(motion.OwnsPhysics)}|");
            report.AppendLine($"|物理质量|{F(telemetry.totalMass)} kg|");
            report.AppendLine($"|物理质心|{V(telemetry.centerOfMassLocal)} m|");
            report.AppendLine($"|几何中心|{V(geometryCenter)} m|");
            report.AppendLine($"|有效推力中心|{V(thrustCenter)} m|");
            report.AppendLine($"|主惯量|{V(physics.principalInertia)} kg·m²|");
            report.AppendLine($"|主轴旋转|{Q(physics.principalAxes)}|");
            report.AppendLine(
                $"|惯量三角不等式|{YesNo(telemetry.inertiaTriangleValid)}|");
            report.AppendLine(
                $"|惯量回退|{YesNo(telemetry.inertiaFallbackUsed)}|");
            report.AppendLine($"|暴露面积|{V(telemetry.exposedArea)} m²|");
            report.AppendLine($"|阻力面积|{V(telemetry.dragArea)} m²|");
            report.AppendLine($"|机翼总面积|{F(telemetry.wingArea)} m²|");
            report.AppendLine($"|失速速度|{F(telemetry.stallSpeed)} m/s|");
            report.AppendLine($"|预计空中极速|{F(telemetry.airTopSpeed)} m/s|");
            report.AppendLine($"|0–50m/s时间|{F(telemetry.zeroToFiftyTime)} s|");
            report.AppendLine($"|制动时间|{F(telemetry.brakingTime)} s|");
            report.AppendLine($"|升重比|{F(telemetry.hoverRatio)}|");
            report.AppendLine($"|稳定器占用|{F(telemetry.stabilizerUsage * 100f)}%|");
            report.AppendLine($"|空气密度|{F(telemetry.airDensity)} kg/m³|");
            report.AppendLine($"|平均风|{V(telemetry.meanWindVelocity)} m/s|");
            report.AppendLine($"|阵风|{V(telemetry.gustVelocity)} m/s|");
            report.AppendLine($"|迎角|{F(telemetry.angleOfAttack)}°|");
            report.AppendLine($"|侧滑角|{F(telemetry.sideslipAngle)}°|");
            report.AppendLine();
            report.AppendLine("### 六向实体控制能力（飞船局部坐标）");
            report.AppendLine();
            report.AppendLine("|轴|正向力 N|负向力 N|");
            report.AppendLine("|---|---:|---:|");
            AppendAxisRow(
                report,
                "+X右 / -X左",
                authority.positiveForce.x,
                authority.negativeForce.x);
            AppendAxisRow(
                report,
                "+Y上 / -Y下",
                authority.positiveForce.y,
                authority.negativeForce.y);
            AppendAxisRow(
                report,
                "+Z前 / -Z后",
                authority.positiveForce.z,
                authority.negativeForce.z);
            report.AppendLine();
            report.AppendLine("|旋转轴|正向力矩 N·m|负向力矩 N·m|");
            report.AppendLine("|---|---:|---:|");
            AppendAxisRow(
                report,
                "X俯仰",
                authority.positiveTorque.x,
                authority.negativeTorque.x);
            AppendAxisRow(
                report,
                "Y偏航",
                authority.positiveTorque.y,
                authority.negativeTorque.y);
            AppendAxisRow(
                report,
                "Z滚转",
                authority.positiveTorque.z,
                authority.negativeTorque.z);
            report.AppendLine();
            report.AppendLine("### 当前控制闭环");
            report.AppendLine();
            report.AppendLine(
                $"- 请求力：`{V(telemetry.requestedControlForceWorld)}` N");
            report.AppendLine(
                $"- 实际力：`{V(telemetry.actualControlForceWorld)}` N");
            report.AppendLine(
                $"- 力残差：`{V(telemetry.controlForceResidualWorld)}` N");
            report.AppendLine(
                $"- 请求力矩：`{V(telemetry.requestedControlTorqueWorld)}` N·m");
            report.AppendLine(
                $"- 实际力矩：`{V(telemetry.actualControlTorqueWorld)}` N·m");
            report.AppendLine(
                $"- 力矩残差：`{V(telemetry.controlTorqueResidualWorld)}` N·m");
            report.AppendLine(
                $"- 闭合率：`{F(telemetry.controlClosureRatio * 100f)}%`");
            report.AppendLine();
        }

        static void AppendTopology(
            StringBuilder report,
            Dictionary<string, HashSet<string>> graph,
            Dictionary<string, int> depth,
            HashSet<string> articulationPoints,
            GridAssemblyValidation validation)
        {
            int edgeCount = graph.Values.Sum(neighbors => neighbors.Count) / 2;
            int connected = depth.Count(pair => pair.Value >= 0);
            report.AppendLine("## 5. 结构连接图");
            report.AppendLine();
            report.AppendLine($"- 节点数：`{graph.Count}`");
            report.AppendLine($"- 六面接触边数：`{edgeCount}`");
            report.AppendLine($"- 与核心连通节点：`{connected}`");
            report.AppendLine(
                $"- 关键桥接节点（割点）：`{articulationPoints.Count}`");
            report.AppendLine(
                $"- 已断连节点：`{validation.DisconnectedIds.Count}`");
            report.AppendLine();
            if (articulationPoints.Count > 0)
            {
                report.AppendLine(
                    "关键桥接节点：" +
                    string.Join(
                        "、",
                        articulationPoints
                            .OrderBy(value => value, StringComparer.Ordinal)
                            .Select(value => $"`{Escape(value)}`")));
                report.AppendLine();
            }
            report.AppendLine(
                "割点被摧毁后可能让其后的整组机翼、武器或推进器与核心断开。");
            report.AppendLine();
        }

        static void AppendModules(
            StringBuilder report,
            GridModuleRecord[] records,
            Dictionary<string, List<Vector3Int>> cells,
            Dictionary<string, HashSet<string>> graph,
            Dictionary<string, int> depth,
            HashSet<string> articulationPoints)
        {
            report.AppendLine("## 6. 完整模块清单");
            report.AppendLine();
            report.AppendLine(
                "|#|runtimeId|模块ID|中文名|类别|原点|姿态|占格|质量kg|耐久|能量供给/消耗|镜像组|度数|核心距离|割点|行为设置|");
            report.AppendLine(
                "|---:|---|---|---|---|---|---:|---|---:|---:|---|---|---:|---:|---|---|");
            for (int index = 0; index < records.Length; index++)
            {
                GridModuleRecord record = records[index];
                GridModuleDefinition definition = record.Definition;
                List<Vector3Int> moduleCells = cells[record.RuntimeId];
                int moduleDepth = depth.TryGetValue(
                    record.RuntimeId,
                    out int value) ? value : -1;
                report.Append('|').Append(index + 1)
                    .Append('|').Append(Escape(record.RuntimeId))
                    .Append('|').Append(Escape(definition.ModuleId))
                    .Append('|').Append(Escape(definition.DisplayName))
                    .Append('|').Append(definition.Category)
                    .Append('|').Append(V(record.Pose.Origin))
                    .Append('|').Append(record.Pose.orientation)
                    .Append('|').Append(Cells(moduleCells))
                    .Append('|').Append(F(definition.MassKg))
                    .Append('|').Append(F(definition.MaxIntegrity))
                    .Append('|').Append(F(definition.EnergyCapacity))
                    .Append('/').Append(F(definition.EnergyCost))
                    .Append('|').Append(Escape(record.Pose.mirrorGroupId))
                    .Append('|').Append(graph[record.RuntimeId].Count)
                    .Append('|').Append(moduleDepth)
                    .Append('|').Append(YesNo(
                        articulationPoints.Contains(record.RuntimeId)))
                    .Append('|').Append(Escape(record.BehaviorSettings))
                    .AppendLine("|");
            }
            report.AppendLine();
        }

        static void AppendFunctionalAxes(
            StringBuilder report,
            GridModuleRecord[] records,
            GridAssemblyPresenter presenter)
        {
            report.AppendLine("## 7. 功能模块轴向与挂点");
            report.AppendLine();
            if (presenter == null)
            {
                report.AppendLine("未找到当前飞船Presenter，无法读取模型功能轴。");
                report.AppendLine();
                return;
            }

            var rows = new List<string>();
            Transform reference = presenter.transform;
            foreach (GridModuleRecord record in records)
            {
                if (!presenter.Views.TryGetValue(
                        record.RuntimeId,
                        out GridModuleView view) ||
                    view == null)
                    continue;
                NeoXBehaviorModule behavior =
                    view.GetComponentInChildren<NeoXBehaviorModule>(true);
                if (behavior == null)
                    continue;
                Vector3 mountNormal = reference.InverseTransformDirection(
                    behavior.WorldMountNormal);
                Vector3 exhaustDirection = reference.InverseTransformDirection(
                    behavior.WorldExhaustDirection);
                Vector3 exhaustPosition = reference.InverseTransformPoint(
                    behavior.WorldExhaustPosition);
                Vector3 muzzleDirection = reference.InverseTransformDirection(
                    behavior.WorldMuzzleDirection);
                Vector3 muzzlePosition = reference.InverseTransformPoint(
                    behavior.WorldMuzzlePosition);
                rows.Add(
                    $"|{Escape(record.RuntimeId)}|" +
                    $"{Escape(behavior.SourceId)}|" +
                    $"{behavior.BehaviorKind}|" +
                    $"{V(mountNormal)}|" +
                    $"{V(exhaustDirection)}|" +
                    $"{V(exhaustPosition)}|" +
                    $"{V(muzzleDirection)}|" +
                    $"{V(muzzlePosition)}|" +
                    $"{behavior.WeaponGroup}|" +
                    $"{Escape(behavior.AppearanceId)}|");
            }
            if (rows.Count == 0)
            {
                report.AppendLine("当前没有可读取的功能模块运行时轴向。");
                report.AppendLine();
                return;
            }
            report.AppendLine(
                "|runtimeId|NeoX源ID|行为|安装法线|喷口轴|喷口位置|枪口轴|枪口位置|武器组|外观|");
            report.AppendLine(
                "|---|---|---|---|---|---|---|---|---:|---|");
            foreach (string row in rows)
                report.AppendLine(row);
            report.AppendLine();
        }

        static void AppendActuators(
            StringBuilder report,
            VehicleActuatorDiagnostic[] actuators)
        {
            report.AppendLine("## 8. 实体飞行执行器");
            report.AppendLine();
            if (actuators.Length == 0)
            {
                report.AppendLine("没有注册实体推进器或RCS执行器。");
                report.AppendLine();
                return;
            }
            report.AppendLine(
                "|runtimeId|源ID|局部位置|实际推力方向|配置推力N|当前环境最大推力N|最大配对力矩N·m|响应s|目标油门|实际油门|仅大气|螺旋桨|");
            report.AppendLine(
                "|---|---|---|---|---:|---:|---|---:|---:|---:|---|---|");
            foreach (VehicleActuatorDiagnostic actuator in actuators)
            {
                report.Append('|').Append(Escape(actuator.runtimeId))
                    .Append('|').Append(Escape(actuator.sourceId))
                    .Append('|').Append(V(actuator.localPosition))
                    .Append('|').Append(V(actuator.localForceDirection))
                    .Append('|').Append(F(actuator.configuredMaximumForce))
                    .Append('|').Append(F(actuator.effectiveMaximumForce))
                    .Append('|').Append(V(actuator.maximumTorqueLocal))
                    .Append('|').Append(F(actuator.responseTime))
                    .Append('|').Append(F(actuator.targetThrottle))
                    .Append('|').Append(F(actuator.actualThrottle))
                    .Append('|').Append(YesNo(actuator.atmosphereOnly))
                    .Append('|').Append(YesNo(actuator.propeller))
                    .AppendLine("|");
            }
            report.AppendLine();
        }

        static void AppendAeroSurfaces(
            StringBuilder report,
            IReadOnlyList<ModuleAeroSurface> surfaces)
        {
            report.AppendLine("## 9. 机翼与舵面");
            report.AppendLine();
            if (surfaces == null || surfaces.Count == 0)
            {
                report.AppendLine("没有注册有效机翼或舵面。");
                report.AppendLine();
                return;
            }
            report.AppendLine(
                "|runtimeId|气动中心|弦向|翼展向|法线|面积m²|舵面|");
            report.AppendLine("|---|---|---|---|---|---:|---|");
            foreach (ModuleAeroSurface surface in surfaces)
            {
                report.Append('|').Append(Escape(surface.runtimeId))
                    .Append('|').Append(V(surface.aerodynamicCenterLocal))
                    .Append('|').Append(V(surface.chordLocal))
                    .Append('|').Append(V(surface.spanLocal))
                    .Append('|').Append(V(surface.normalLocal))
                    .Append('|').Append(F(surface.area))
                    .Append('|').Append(YesNo(surface.isControlSurface))
                    .AppendLine("|");
            }
            report.AppendLine();
        }

        static void AppendAutomaticFindings(
            StringBuilder report,
            GridAssemblyValidation validation,
            RobocraftTelemetry telemetry,
            DirectionalAuthority24 authority,
            GridModuleRecord[] records,
            Dictionary<string, HashSet<string>> graph,
            HashSet<string> articulationPoints,
            Vector3 geometryCenter,
            Vector3 thrustCenter,
            VehicleActuatorDiagnostic[] actuators,
            IReadOnlyList<ModuleAeroSurface> aeroSurfaces,
            bool hasMotion)
        {
            var findings = new List<string>();
            if (!validation.IsValid)
                findings.Add("蓝图当前无效：" + validation.Message);
            if (validation.DisconnectedIds.Count > 0)
                findings.Add(
                    $"存在{validation.DisconnectedIds.Count}个未连接核心的模块。");
            if (articulationPoints.Count > 0)
                findings.Add(
                    $"结构图存在{articulationPoints.Count}个割点，局部受损可能切掉整组功能模块。");
            if (records.Length > 0 && graph.Count != records.Length)
                findings.Add("结构图节点数与模块数不一致，应检查重复runtimeId。");
            if (!hasMotion)
            {
                findings.Add("未找到RC3协调器，无法判断真实推力和气动能力。");
            }
            else
            {
                if (actuators.Length == 0)
                    findings.Add("没有实体飞行执行器；标准/无辅助模式无法主动飞行。");
                if (telemetry.hoverRatio < 1f)
                    findings.Add(
                        $"当前升重比为{F(telemetry.hoverRatio)}，无法依靠当前垂直推力持续悬停。");
                if (telemetry.stabilizerUsage > 0.8f)
                    findings.Add(
                        $"稳定器预计占用{F(telemetry.stabilizerUsage * 100f)}%，剩余姿态控制余量较小。");
                if (!telemetry.inertiaTriangleValid ||
                    telemetry.inertiaFallbackUsed)
                    findings.Add("惯量计算异常或触发回退，物理手感不应直接用于平衡判断。");
                if (telemetry.controlClosureRatio < 0.9f &&
                    (telemetry.requestedControlForceWorld.sqrMagnitude > 1f ||
                     telemetry.requestedControlTorqueWorld.sqrMagnitude > 1f))
                    findings.Add(
                        $"当前控制请求闭合率只有{F(telemetry.controlClosureRatio * 100f)}%，存在执行器饱和或缺轴。");
                AddMissingForceFindings(findings, authority);
                float minimumInertia = Mathf.Max(
                    0.001f,
                    Mathf.Min(
                        telemetry.inertia.x,
                        Mathf.Min(telemetry.inertia.y, telemetry.inertia.z)));
                float maximumInertia = Mathf.Max(
                    telemetry.inertia.x,
                    Mathf.Max(telemetry.inertia.y, telemetry.inertia.z));
                if (maximumInertia / minimumInertia > 6f)
                    findings.Add(
                        $"主惯量最大/最小比为{F(maximumInertia / minimumInertia)}，部分旋转轴会明显迟钝。");
                if (aeroSurfaces.Count > 0 && telemetry.stallSpeed > 0f)
                    findings.Add(
                        $"机翼需要约{F(telemetry.stallSpeed)}m/s空速才能承担主要升力；静止时不会自动托举。");
            }
            float centerOffset = Vector3.Distance(geometryCenter, thrustCenter);
            if (actuators.Length > 0 && centerOffset > 1.5f)
                findings.Add(
                    $"有效推力中心与几何中心相距{F(centerOffset)}m，姿态补偿可能消耗较多推力。");
            if (findings.Count == 0)
                findings.Add("未发现明显静态缺陷；应结合实际输入、速度和迎角继续分析动态问题。");

            report.AppendLine("## 10. 工具自动发现");
            report.AppendLine();
            foreach (string finding in findings)
                report.AppendLine("- " + Escape(finding));
            report.AppendLine();
        }

        static void AddMissingForceFindings(
            List<string> findings,
            DirectionalAuthority24 authority)
        {
            AddMissingDirection(
                findings,
                "+X右移",
                authority.positiveForce.x);
            AddMissingDirection(
                findings,
                "-X左移",
                authority.negativeForce.x);
            AddMissingDirection(
                findings,
                "+Y上升",
                authority.positiveForce.y);
            AddMissingDirection(
                findings,
                "-Y下降",
                authority.negativeForce.y);
            AddMissingDirection(
                findings,
                "+Z前进",
                authority.positiveForce.z);
            AddMissingDirection(
                findings,
                "-Z后退",
                authority.negativeForce.z);
        }

        static void AddMissingDirection(
            List<string> findings,
            string label,
            float capacity)
        {
            if (capacity <= 0.01f)
                findings.Add($"缺少{label}方向的实体推力能力。");
        }

        static void AppendBlueprint(
            StringBuilder report,
            ModularBlueprintData blueprint)
        {
            report.AppendLine("## 11. 原始蓝图机器数据");
            report.AppendLine();
            report.AppendLine("```json");
            report.AppendLine(JsonUtility.ToJson(blueprint, true));
            report.AppendLine("```");
            report.AppendLine();
        }

        static void AppendSuggestedPrompt(StringBuilder report)
        {
            report.AppendLine("## 12. 建议直接向AI提出的问题");
            report.AppendLine();
            report.AppendLine("```text");
            report.AppendLine("请根据这份Unity模块飞行器诊断报告分析：");
            report.AppendLine("1. 这艘飞船能否悬停、固定翼起飞和稳定转向？");
            report.AppendLine("2. 为什么它可能持续旋转、串轴、制动不足或无法升空？");
            report.AppendLine("3. 哪些推进器方向缺失，哪些推进器位置造成了较大附带力矩？");
            report.AppendLine("4. 质量、质心、主惯量、机翼面积和失速速度是否匹配？");
            report.AppendLine("5. 在尽量少改造外形的前提下，应移动、增加或删除哪些模块？");
            report.AppendLine("请区分确定结论、合理推断和仍需PlayMode实测的数据。");
            report.AppendLine("```");
        }

        static Dictionary<string, List<Vector3Int>> BuildCells(
            GridAssemblyModel model,
            IEnumerable<GridModuleRecord> records)
        {
            return records.ToDictionary(
                record => record.RuntimeId,
                record => model.GetCells(record),
                StringComparer.Ordinal);
        }

        static Dictionary<string, HashSet<string>> BuildGraph(
            IEnumerable<GridModuleRecord> records,
            Dictionary<string, List<Vector3Int>> cells)
        {
            var graph = records.ToDictionary(
                record => record.RuntimeId,
                _ => new HashSet<string>(StringComparer.Ordinal),
                StringComparer.Ordinal);
            var occupancy = new Dictionary<Vector3Int, string>();
            foreach (KeyValuePair<string, List<Vector3Int>> pair in cells)
            foreach (Vector3Int cell in pair.Value)
                if (!occupancy.ContainsKey(cell))
                    occupancy[cell] = pair.Key;

            foreach (KeyValuePair<Vector3Int, string> pair in occupancy)
            foreach (Vector3Int offset in PositiveNeighbors)
            {
                if (!occupancy.TryGetValue(
                        pair.Key + offset,
                        out string neighbor) ||
                    string.Equals(
                        pair.Value,
                        neighbor,
                        StringComparison.Ordinal))
                    continue;
                graph[pair.Value].Add(neighbor);
                graph[neighbor].Add(pair.Value);
            }
            return graph;
        }

        static Dictionary<string, int> CoreDepths(
            Dictionary<string, HashSet<string>> graph)
        {
            var result = graph.Keys.ToDictionary(
                key => key,
                _ => -1,
                StringComparer.Ordinal);
            if (!result.ContainsKey(GridAssemblyModel.CoreRuntimeId))
                return result;

            var queue = new Queue<string>();
            result[GridAssemblyModel.CoreRuntimeId] = 0;
            queue.Enqueue(GridAssemblyModel.CoreRuntimeId);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                foreach (string neighbor in graph[current])
                {
                    if (result[neighbor] >= 0)
                        continue;
                    result[neighbor] = result[current] + 1;
                    queue.Enqueue(neighbor);
                }
            }
            return result;
        }

        static HashSet<string> FindArticulationPoints(
            Dictionary<string, HashSet<string>> graph)
        {
            var discovery = new Dictionary<string, int>(StringComparer.Ordinal);
            var low = new Dictionary<string, int>(StringComparer.Ordinal);
            var result = new HashSet<string>(StringComparer.Ordinal);
            int time = 0;
            foreach (string node in graph.Keys)
            {
                if (!discovery.ContainsKey(node))
                    VisitArticulation(
                        node,
                        null,
                        graph,
                        discovery,
                        low,
                        result,
                        ref time);
            }
            return result;
        }

        static void VisitArticulation(
            string node,
            string parent,
            Dictionary<string, HashSet<string>> graph,
            Dictionary<string, int> discovery,
            Dictionary<string, int> low,
            HashSet<string> result,
            ref int time)
        {
            discovery[node] = ++time;
            low[node] = discovery[node];
            int childCount = 0;
            foreach (string neighbor in graph[node])
            {
                if (string.Equals(neighbor, parent, StringComparison.Ordinal))
                    continue;
                if (!discovery.ContainsKey(neighbor))
                {
                    childCount++;
                    VisitArticulation(
                        neighbor,
                        node,
                        graph,
                        discovery,
                        low,
                        result,
                        ref time);
                    low[node] = Math.Min(low[node], low[neighbor]);
                    if (parent != null &&
                        low[neighbor] >= discovery[node])
                        result.Add(node);
                }
                else
                {
                    low[node] = Math.Min(low[node], discovery[neighbor]);
                }
            }
            if (parent == null && childCount > 1)
                result.Add(node);
        }

        static Vector3 CalculateGeometryCenter(
            Dictionary<string, List<Vector3Int>> cells)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (List<Vector3Int> moduleCells in cells.Values)
            foreach (Vector3Int cell in moduleCells)
            {
                sum += (Vector3)cell + Vector3.one * 0.5f;
                count++;
            }
            return count > 0 ? sum / count : Vector3.zero;
        }

        static Vector3 CalculateThrustCenter(
            IEnumerable<VehicleActuatorDiagnostic> actuators,
            Vector3 fallback)
        {
            Vector3 sum = Vector3.zero;
            float total = 0f;
            foreach (VehicleActuatorDiagnostic actuator in actuators)
            {
                float weight = Mathf.Max(0f, actuator.effectiveMaximumForce);
                sum += actuator.localPosition * weight;
                total += weight;
            }
            return total > 0.001f ? sum / total : fallback;
        }

        static string Cells(IEnumerable<Vector3Int> cells)
        {
            return string.Join(
                ";",
                cells.Select(cell =>
                    $"{cell.x},{cell.y},{cell.z}"));
        }

        static void AppendAxisRow(
            StringBuilder report,
            string label,
            float positive,
            float negative)
        {
            report.Append('|').Append(label)
                .Append('|').Append(F(positive))
                .Append('|').Append(F(negative))
                .AppendLine("|");
        }

        static string V(Vector3 value)
        {
            return $"{F(value.x)},{F(value.y)},{F(value.z)}";
        }

        static string V(Vector3Int value)
        {
            return $"{value.x},{value.y},{value.z}";
        }

        static string Q(Quaternion value)
        {
            return $"{F(value.x)},{F(value.y)},{F(value.z)},{F(value.w)}";
        }

        static string F(float value)
        {
            if (float.IsNaN(value))
                return "NaN";
            if (float.IsPositiveInfinity(value))
                return "+Infinity";
            if (float.IsNegativeInfinity(value))
                return "-Infinity";
            return value.ToString("0.###", Invariant);
        }

        static string YesNo(bool value)
        {
            return value ? "是" : "否";
        }

        static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value
                .Replace("\\", "\\\\")
                .Replace("|", "\\|")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }
    }
}
