#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LogTrack.Editor
{
    public static class LogTrackEditorMenu
    {
        [MenuItem("Tools/LogTrack/打开工具窗口", false, 1003)]
        public static void OpenWindow() => LogTrackEditorWindow.Open();

        [MenuItem("Tools/LogTrack/编译后自动插桩", false, 1004)]
        public static void ToggleAutoInstrumentation()
        {
            LogTrackProjectSettings.SetAutoInstrumentOnCompile(!LogTrackProjectSettings.AutoInstrumentOnCompile);
            Debug.Log("LogTrack 编译后自动插桩（本机）: " + (LogTrackProjectSettings.AutoInstrumentOnCompile ? "开启" : "关闭"));
        }

        [MenuItem("Tools/LogTrack/编译后自动插桩", true)]
        public static bool ToggleAutoInstrumentationValidate()
        {
            Menu.SetChecked("Tools/LogTrack/编译后自动插桩", LogTrackProjectSettings.AutoInstrumentOnCompile);
            return true;
        }

        [MenuItem("Tools/LogTrack/重置编译后自动插桩为项目默认", false, 1005)]
        public static void ResetAutoInstrumentationToProjectDefault()
        {
            LogTrackProjectSettings.ClearAutoInstrumentOverride();
            var effective = LogTrackProjectSettings.AutoInstrumentOnCompile;
            Debug.Log("LogTrack 编译后自动插桩已重置为项目默认: " + (effective ? "开启" : "关闭"));
        }

        [MenuItem("Tools/LogTrack/创建 LogTrackSession", false, 1006)]
        public static void CreateLogTrackSession()
        {
            var go = new GameObject("LogTrackSession");
            var session = go.AddComponent<LogTrackSession>();
            Selection.activeGameObject = go;
            Debug.Log($"已创建 LogTrackSession，RingBuffer={session.RingBufferSize}。请先执行「执行 IL 插桩」，再点击 Play。");
        }

        [MenuItem("Tools/LogTrack/执行 IL 插桩", false, 1007)]
        public static void RunIlInstrumentFromMenu()
        {
            LogTrackInsertPanel.RunInstrumentFromMenu();
        }

        [MenuItem("Tools/LogTrack/还原 IL 插桩并重编译", false, 1008)]
        public static void RestoreIlAssembliesFromMenu()
        {
            LogTrackInsertPanel.RestoreSelectedAssembliesFromMenu();
        }

        [MenuItem("Tools/LogTrack/开始录制", false, 1009)]
        public static void StartRecordingFromMenu()
        {
            LogTrackRuntimePanel.StartRecording();
        }

        [MenuItem("Tools/LogTrack/开始录制", true)]
        public static bool StartRecordingFromMenuValidate()
        {
            return EditorApplication.isPlaying && !FSPDebuger.HasTrackSession;
        }

        [MenuItem("Tools/LogTrack/结束录制", false, 1010)]
        public static void StopRecordingFromMenu()
        {
            LogTrackRuntimePanel.StopRecording();
        }

        [MenuItem("Tools/LogTrack/结束录制", true)]
        public static bool StopRecordingFromMenuValidate()
        {
            return EditorApplication.isPlaying && FSPDebuger.HasTrackSession;
        }
    }

    public class LogTrackEditorWindow : EditorWindow
    {
        private const string PrefShowHelp = "LogTrack.UI.ShowHelp";
        private const string PrefShowDeveloper = "LogTrack.UI.ShowDeveloper";

        private bool m_showHelp;
        private bool m_showDeveloper;
        private static LogTrackEditorWindow s_openInstance;

        public static void RequestRepaint()
        {
            if (s_openInstance != null)
            {
                s_openInstance.Repaint();
            }
        }

        public static void Open()
        {
            var win = GetWindow<LogTrackEditorWindow>("LogTrack工具");
            win.minSize = new Vector2(420f, 480f);
            win.Show();
        }

        private void OnEnable()
        {
            s_openInstance = this;
            m_showHelp = EditorPrefs.GetBool(PrefShowHelp, false);
            m_showDeveloper = EditorPrefs.GetBool(PrefShowDeveloper, false);
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            if (s_openInstance == this)
            {
                s_openInstance = null;
            }
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            Repaint();
        }

        private void OnGUI()
        {
            DrawStatusBar();
            EditorGUILayout.Space(4);

            m_showHelp = EditorGUILayout.Foldout(m_showHelp, "使用说明", true);
            if (m_showHelp)
            {
                EditorGUILayout.HelpBox(
                    "① 勾选程序集 → 执行 IL 插桩\n" +
                    "② 点击 Unity Play 进入运行\n" +
                    "③ Play 中点击「开始录制」→「结束录制」导出 .log（弹窗路径），可继续下一段\n" +
                    "④ 退出 Play 时若仍在录制也会自动导出",
                    MessageType.Info);
            }

            EditorGUILayout.Space(4);
            LogTrackRuntimePanel.DrawMain();
            EditorGUILayout.Space(8);
            LogTrackInsertPanel.DrawMain();

            EditorGUILayout.Space(8);
            m_showDeveloper = EditorGUILayout.Foldout(m_showDeveloper, "开发者选项", true);
            if (m_showDeveloper != EditorPrefs.GetBool(PrefShowDeveloper, false))
            {
                EditorPrefs.SetBool(PrefShowDeveloper, m_showDeveloper);
            }

            if (m_showDeveloper)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    LogTrackDeveloperPanel.Draw();
                }
            }

            if (m_showHelp != EditorPrefs.GetBool(PrefShowHelp, false))
            {
                EditorPrefs.SetBool(PrefShowHelp, m_showHelp);
            }
        }

        private static void DrawStatusBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                string recordState;
                if (!EditorApplication.isPlaying)
                {
                    recordState = "未在 Play";
                }
                else if (!FSPDebuger.HasTrackSession)
                {
                    recordState = "Play 中（未启动录制）";
                }
                else
                {
                    recordState = "● 录制中";
                }

                EditorGUILayout.LabelField("状态", recordState, EditorStyles.boldLabel);

                var insertMs = ReadInsertMsHint();
                if (insertMs > 0)
                {
                    EditorGUILayout.LabelField("插桩", insertMs.ToString("F1") + " ms", GUILayout.Width(120));
                }
            }
        }

        private static double ReadInsertMsHint()
        {
            if (LogTrackBenchmark.InsertMs > 0)
            {
                return LogTrackBenchmark.InsertMs;
            }

            var benchPath = Path.Combine(
                Path.GetFullPath(LogTrackSettings.PdbOutputDir),
                "logtrack_benchmark.json");
            if (!File.Exists(benchPath))
            {
                return 0;
            }

            try
            {
                var json = File.ReadAllText(benchPath);
                var match = System.Text.RegularExpressions.Regex.Match(json, "\"insert_ms\"\\s*:\\s*([0-9.]+)");
                if (match.Success && double.TryParse(match.Groups[1].Value, out var ms))
                {
                    return ms;
                }
            }
            catch (Exception)
            {
                // ignore
            }

            return 0;
        }
    }

    internal static class LogTrackRuntimePanel
    {
        internal const string SessionPendingStartKey = "LogTrack.PendingStartOnPlay";

        public static void DrawMain()
        {
            EditorGUILayout.LabelField("录制", EditorStyles.boldLabel);

            LogTrackProjectSettings.AutoStartOnPlay = EditorGUILayout.Toggle(
                "Play 时自动启动",
                LogTrackProjectSettings.AutoStartOnPlay);

            var ringBuffer = LogTrackProjectSettings.RingBufferSize;
            ringBuffer = EditorGUILayout.IntSlider(
                "Ring Buffer Size",
                ringBuffer,
                LogTrackSettings.MinRingBufferSize,
                LogTrackSettings.MaxRingBufferSize);

            if (ringBuffer != LogTrackProjectSettings.RingBufferSize)
            {
                LogTrackProjectSettings.RingBufferSize = ringBuffer;
            }

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (EditorApplication.isPlaying && FSPDebuger.HasTrackSession)
                {
                    if (GUILayout.Button("结束录制", GUILayout.Height(28)))
                    {
                        StopRecording();
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                    {
                        if (GUILayout.Button("开始录制", GUILayout.Height(28)))
                        {
                            StartRecording();
                        }
                    }
                }

                if (GUILayout.Button("打开日志目录", GUILayout.Height(28), GUILayout.Width(110)))
                {
                    var dir = Path.Combine(Application.persistentDataPath, "LogTrack");
                    Directory.CreateDirectory(dir);
                    EditorUtility.RevealInFinder(dir);
                }
            }

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.LabelField("请先点击 Unity Play，再开始录制", EditorStyles.miniLabel);
            }
            else if (!FSPDebuger.HasTrackSession)
            {
                EditorGUILayout.LabelField("可再次点击「开始录制」录下一段（无需退出 Play）", EditorStyles.miniLabel);
            }
        }

        public static void StartRecording()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[LogTrack] 请先进入 Play 模式，再点击开始录制。");
                return;
            }

            if (FSPDebuger.HasTrackSession)
            {
                return;
            }

            if (LogTrackRuntimeBootstrap.TryStartRecording())
            {
                Debug.Log("[LogTrack] 录制已开始");
            }
            else
            {
                Debug.LogWarning("[LogTrack] 无法启动录制（场景已有 LogTrackSession）");
            }
        }

        internal static void TryStartPendingRecording()
        {
            if (!SessionState.GetBool(SessionPendingStartKey, false))
            {
                return;
            }

            SessionState.EraseBool(SessionPendingStartKey);

            if (!EditorApplication.isPlaying || FSPDebuger.HasTrackSession)
            {
                return;
            }

            if (LogTrackSettings.AutoStartOnPlay)
            {
                return;
            }

            if (LogTrackRuntimeBootstrap.TryStartRecording())
            {
                Debug.Log("[LogTrack] 录制已开始");
            }
            else
            {
                Debug.LogWarning("[LogTrack] 无法启动录制（场景已有 LogTrackSession）");
            }
        }

        public static void StopRecording()
        {
            if (!EditorApplication.isPlaying || !FSPDebuger.HasTrackSession)
            {
                EditorUtility.DisplayDialog("LogTrack", "当前未在录制。", "确定");
                return;
            }

            LogTrackRecordingExport.ExportIfNeeded();
            LogTrackExportNotifier.ShowPendingIfAny();

            LogTrackRuntimeBootstrap.DestroyPhaseDriver();
            var driverGo = GameObject.Find(LogTrackRuntimeBootstrap.PhaseDriverObjectName);
            if (driverGo != null)
            {
                UnityEngine.Object.DestroyImmediate(driverGo);
            }

            LogTrackRecordingExport.Reset();
            LogTrackEditorWindow.RequestRepaint();
        }
    }

    internal static class LogTrackInsertPanel
    {
        private static string s_logTrackClass = "FSPDebuger";
        private static string s_pdbDir = LogTrackSettings.PdbOutputDir;
        private static List<AssemblyCatalogEntry> s_catalog = new List<AssemblyCatalogEntry>();
        private static HashSet<string> s_selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool s_initialized;
        private static Vector2 s_assemblyScroll;
        private static bool s_onlySelectable = true;
        private static string s_assemblyFilter = string.Empty;

        public static void DrawMain()
        {
            EnsureInitialized();

            EditorGUILayout.LabelField("IL 插桩", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("编译后对 Library/ScriptAssemblies 内 DLL 插桩（不改 .cs 源码）", EditorStyles.miniLabel);

            if (s_catalog.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "未找到 Library/ScriptAssemblies。请先让 Unity 完成一次脚本编译。",
                    MessageType.Warning);
            }

            s_onlySelectable = EditorGUILayout.Toggle("仅显示可插桩程序集", s_onlySelectable);
            s_assemblyFilter = EditorGUILayout.TextField("筛选（名称包含）", s_assemblyFilter);

            var visibleEntries = GetVisibleEntries().ToList();
            EditorGUILayout.LabelField($"显示 {visibleEntries.Count} / {s_catalog.Count}", EditorStyles.miniLabel);

            s_assemblyScroll = EditorGUILayout.BeginScrollView(s_assemblyScroll, GUILayout.MinHeight(120f), GUILayout.MaxHeight(200f));
            foreach (var entry in visibleEntries)
            {
                EditorGUI.BeginDisabledGroup(!entry.selectable);
                var selected = s_selected.Contains(entry.assemblyName);
                var toggled = EditorGUILayout.ToggleLeft(
                    entry.selectable
                        ? entry.assemblyName
                        : entry.assemblyName + " (" + entry.reason + ")",
                    selected);
                if (entry.selectable && toggled != selected)
                {
                    if (toggled)
                    {
                        s_selected.Add(entry.assemblyName);
                    }
                    else
                    {
                        s_selected.Remove(entry.assemblyName);
                    }
                }

                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("执行 IL 插桩", GUILayout.Height(30)))
            {
                RunInstrument(s_pdbDir);
            }
        }

        public static void DrawDeveloperInsertOptions()
        {
            EnsureInitialized();

            s_pdbDir = EditorGUILayout.TextField("Pdb 目录", s_pdbDir);
            s_logTrackClass = EditorGUILayout.TextField("LogTrack 类", s_logTrackClass);

            EditorGUILayout.LabelField(
                "项目默认（LogTrackSetting.txt）",
                LogTrackProjectSettings.ProjectDefaultAutoInstrumentOnCompile ? "开启" : "关闭");

            if (LogTrackProjectSettings.HasAutoInstrumentOverride)
            {
                EditorGUILayout.LabelField("本机覆盖", "已设置");
            }

            var autoInstrument = LogTrackProjectSettings.AutoInstrumentOnCompile;
            var toggledAutoInstrument = EditorGUILayout.Toggle("编译后自动插桩（本机）", autoInstrument);
            if (toggledAutoInstrument != autoInstrument)
            {
                LogTrackProjectSettings.SetAutoInstrumentOnCompile(toggledAutoInstrument);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("刷新程序集列表"))
            {
                RefreshCatalog();
            }

            if (GUILayout.Button("从 LogTrackSetting.txt 加载"))
            {
                LoadFromSettings();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("还原 IL 插桩并重编译"))
            {
                LogTrackIlInstrumenter.RestoreAssembliesFromSource(s_selected);
            }

            if (GUILayout.Button("重置编译后自动插桩为项目默认"))
            {
                LogTrackProjectSettings.ClearAutoInstrumentOverride();
            }

            if (GUILayout.Button("创建 LogTrackSession（手动模式）"))
            {
                LogTrackEditorMenu.CreateLogTrackSession();
            }
        }

        public static void RunInstrumentFromMenu()
        {
            EnsureInitialized();
            RunInstrument(s_pdbDir);
        }

        public static void RestoreSelectedAssembliesFromMenu()
        {
            EnsureInitialized();
            if (s_selected.Count == 0)
            {
                Debug.LogError("请至少选择一个程序集。");
                return;
            }

            LogTrackIlInstrumenter.RestoreAssembliesFromSource(s_selected);
        }

        private static IEnumerable<AssemblyCatalogEntry> GetVisibleEntries()
        {
            IEnumerable<AssemblyCatalogEntry> query = s_catalog;
            if (s_onlySelectable)
            {
                query = query.Where(entry => entry.selectable);
            }

            if (!string.IsNullOrWhiteSpace(s_assemblyFilter))
            {
                query = query.Where(entry =>
                    entry.assemblyName.IndexOf(s_assemblyFilter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return query
                .OrderByDescending(entry => entry.selectable)
                .ThenByDescending(entry => entry.assemblyName.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase))
                .ThenBy(entry => entry.assemblyName, StringComparer.OrdinalIgnoreCase);
        }

        private static void EnsureInitialized()
        {
            if (s_initialized)
            {
                return;
            }

            s_initialized = true;
            LoadFromSettings();
            RefreshCatalog();
        }

        private static void RefreshCatalog()
        {
            LogTrackInsertSettings.TryLoad(out var setting);
            s_catalog = LogTrackIlAssemblyCatalog.ScanCatalog(setting?.excludeAssemblies);
            if (s_selected.Count == 0)
            {
                foreach (var name in LogTrackIlAssemblyCatalog.LoadSelectedAssembliesOrDefault())
                {
                    s_selected.Add(name);
                }
            }
        }

        private static void LoadFromSettings()
        {
            if (!LogTrackInsertSettings.TryLoad(out var setting))
            {
                Debug.LogWarning("未找到 LogTrackSetting.txt（Assets/Scripts 或 Assets/LogTrack/Editor）");
                return;
            }

            s_logTrackClass = setting.logTrackClass;
            s_selected = new HashSet<string>(
                LogTrackIlAssemblyCatalog.ResolveTargetAssemblies(setting),
                StringComparer.OrdinalIgnoreCase);
            LogTrackIlAssemblyCatalog.SaveSelectedAssemblies(s_selected);
            Debug.Log("已从 LogTrackSetting.txt 加载配置。");
        }

        private static void RunInstrument(string pdbDir)
        {
            if (s_selected.Count == 0)
            {
                Debug.LogError("请至少选择一个程序集。");
                return;
            }

            if (!ValidateSelectedAssemblies(out var diagnostic))
            {
                Debug.LogError(diagnostic);
                return;
            }

            LogTrackSettings.PdbOutputDir = pdbDir;
            LogTrackSettings.PdbRelativePath = Path.Combine(pdbDir, "LogPdb.pdb.json").Replace('\\', '/');
            LogTrackIlAssemblyCatalog.SaveSelectedAssemblies(s_selected);

            var result = LogTrackIlInstrumenter.PatchAssemblies(
                s_selected.ToArray(),
                Path.GetFullPath(pdbDir),
                s_logTrackClass);

            if (result.success)
            {
                var benchDir = Path.GetFullPath(pdbDir);
                Directory.CreateDirectory(benchDir);
                var benchPath = Path.Combine(benchDir, "logtrack_benchmark.json");
                LogTrackBenchmark.Save(benchPath);

                Debug.Log(
                    "LogTrack IL 插桩完成: assemblies="
                    + string.Join(",", result.patchedAssemblies)
                    + ", methods="
                    + result.patchedMethods
                    + ", insert_ms="
                    + LogTrackBenchmark.InsertMs.ToString("F2")
                    + ", pdb="
                    + Path.GetFullPath(pdbDir));
            }
            else
            {
                Debug.LogError("LogTrack IL 插桩失败: " + (result.error ?? "unknown error"));
            }
        }

        private static bool ValidateSelectedAssemblies(out string diagnostic)
        {
            var missing = new List<string>();
            foreach (var name in s_selected.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                if (!LogTrackIlInstrumenter.TryGetAssemblyDllStatus(name, out var dllPath, out var exists, out _))
                {
                    missing.Add(name + " (未生成: " + dllPath + ")");
                }
            }

            if (missing.Count == 0)
            {
                diagnostic = string.Empty;
                return true;
            }

            diagnostic = "LogTrack IL 插桩前检查失败：下列程序集 DLL 不存在，请先让 Unity 编译通过。\n"
                + string.Join("\n", missing);
            return false;
        }
    }

    internal static class LogTrackDeveloperPanel
    {
        public static void Draw()
        {
            EditorGUILayout.LabelField("高级插桩", EditorStyles.boldLabel);
            LogTrackInsertPanel.DrawDeveloperInsertOptions();

            EditorGUILayout.Space(8);
            DrawLogMaintenance();

            EditorGUILayout.Space(8);
            DrawBenchmarkReadOnly();
        }

        private static void DrawLogMaintenance()
        {
            EditorGUILayout.LabelField("日志维护", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "删除 LogTrack 目录下超过 1 天的 .log / .bin 导出文件（不含 benchmark.json）。",
                EditorStyles.wordWrappedMiniLabel);

            if (GUILayout.Button("清理过期日志（1 天前）", GUILayout.Height(24)))
            {
                var result = LogTrackLogCleanup.CleanupExpiredLogs(LogTrackLogCleanup.DefaultRetentionDays);
                var message = LogTrackLogCleanup.FormatCompletionMessage(
                    result,
                    LogTrackLogCleanup.DefaultRetentionDays);
                EditorUtility.DisplayDialog("LogTrack 清理完成", message, "确定");
                Debug.Log("[LogTrack] " + message.Replace("\n", " "));
            }
        }

        private static void DrawBenchmarkReadOnly()
        {
            EditorGUILayout.LabelField("性能基准（只读）", EditorStyles.boldLabel);

            var benchPath = Path.Combine(Application.persistentDataPath, "LogTrack", "logtrack_benchmark.json");
            if (!File.Exists(benchPath))
            {
                var pdbBench = Path.Combine(Path.GetFullPath(LogTrackSettings.PdbOutputDir), "logtrack_benchmark.json");
                if (File.Exists(pdbBench))
                {
                    benchPath = pdbBench;
                }
            }

            if (!File.Exists(benchPath))
            {
                EditorGUILayout.LabelField("暂无数据", EditorStyles.miniLabel);
                return;
            }

            try
            {
                var json = File.ReadAllText(benchPath);
                EditorGUILayout.LabelField("路径", benchPath, EditorStyles.miniLabel);
                EditorGUILayout.LabelField("插桩耗时（ms）", ReadJsonNumber(json, "insert_ms"));
                EditorGUILayout.LabelField("峰值内存（字节）", ReadJsonNumber(json, "peak_memory_bytes"));
                EditorGUILayout.LabelField("导出大小（MB）", FormatBytesAsMb(json, "export_file_bytes"));
                var parseMs = ReadJsonNumber(json, "parse_ms");
                EditorGUILayout.LabelField("解析耗时（ms）", parseMs == "0" || parseMs == "0.00" ? "未运行" : parseMs);
            }
            catch (Exception ex)
            {
                EditorGUILayout.HelpBox("读取 benchmark 失败: " + ex.Message, MessageType.Warning);
            }
        }

        private static string ReadJsonNumber(string json, string key)
        {
            var match = System.Text.RegularExpressions.Regex.Match(json, "\"" + key + "\"\\s*:\\s*([0-9.]+)");
            return match.Success ? match.Groups[1].Value : "-";
        }

        private static string FormatBytesAsMb(string json, string key)
        {
            var raw = ReadJsonNumber(json, key);
            if (raw == "-" || !double.TryParse(raw, out var bytes))
            {
                return "-";
            }

            var mb = bytes / (1024.0 * 1024.0);
            return mb.ToString("F2") + " MB";
        }
    }
}
#endif
