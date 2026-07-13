#if UNITY_EDITOR
using System.IO;
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
            LogTrackProjectSettings.AutoInstrumentOnCompile = !LogTrackProjectSettings.AutoInstrumentOnCompile;
            Debug.Log("LogTrack 编译后自动插桩: " + (LogTrackProjectSettings.AutoInstrumentOnCompile ? "开启" : "关闭"));
        }

        [MenuItem("Tools/LogTrack/编译后自动插桩", true)]
        public static bool ToggleAutoInstrumentationValidate()
        {
            Menu.SetChecked("Tools/LogTrack/编译后自动插桩", LogTrackProjectSettings.AutoInstrumentOnCompile);
            return true;
        }

        [MenuItem("Tools/LogTrack/创建演示对象", false, 1005)]
        public static void CreateDemoObject()
        {
            var runnerType = System.Type.GetType("LogTrackDemoRunner, Assembly-CSharp");
            if (runnerType == null)
            {
                Debug.LogError("找不到 LogTrackDemoRunner。请确认 Assets/Scripts/LogTrackDemoRunner.cs 存在且编译通过。");
                return;
            }

            var go = new GameObject("LogTrackDemo");
            go.AddComponent(runnerType);
            Selection.activeGameObject = go;
            Debug.Log("已创建 LogTrackDemo。请先执行「插入日志代码」，再点击 Play。");
        }
    }

    public class LogTrackEditorWindow : EditorWindow
    {
        public static void Open(int tabIndex = 0)
        {
            var win = GetWindow<LogTrackEditorWindow>("LogTrack工具");
            win.m_tabIndex = tabIndex;
            win.Show();
        }

        private int m_tabIndex;
        private bool m_showHelp = true;

        private void OnGUI()
        {
            DrawUsageHelp();

            m_tabIndex = GUILayout.SelectionGrid(m_tabIndex, new[] { "导出日志文本", "插入日志代码" }, 2, EditorStyles.toolbarButton);
            if (m_tabIndex == 0)
            {
                LogTrackExportPanel.OnGUI();
            }
            else
            {
                LogTrackInsertPanel.OnGUI();
            }
        }

        private void DrawUsageHelp()
        {
            m_showHelp = EditorGUILayout.Foldout(m_showHelp, "使用说明", true);
            if (!m_showHelp)
            {
                return;
            }

            EditorGUILayout.HelpBox(
                "① 插入日志代码：选择脚本目录，点击「插入日志代码」\n" +
                "② 运行游戏：点击 Play，让游戏跑一会儿\n" +
                "③ 导出日志：切到「导出日志文本」，选择日志和 Pdb 文件，点击「导出文本格式」",
                MessageType.Info);
        }
    }

    internal static class LogTrackExportPanel
    {
        private static string s_logPath = string.Empty;
        private static string s_pdbPath = "Assets/LogTrackGenerated/LogPdb.pdb.json";
        private static string s_logExpPath = string.Empty;

        public static void OnGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("把二进制日志转成可读的 .log 文本文件", EditorStyles.miniLabel);

            s_logPath = EditorGUILayout.TextField("日志路径：", s_logPath);
            if (GUILayout.Button("选择日志文件", GUILayout.Width(120)))
            {
                var picked = EditorUtility.OpenFilePanel("选择日志文件", GetStartDirectory(s_logPath), "bin,json");
                if (!string.IsNullOrEmpty(picked))
                {
                    s_logPath = ToProjectRelativePath(picked);
                }
            }

            s_pdbPath = EditorGUILayout.TextField("Pdb路径：", s_pdbPath);
            if (GUILayout.Button("选择 Pdb 文件", GUILayout.Width(120)))
            {
                var picked = EditorUtility.OpenFilePanel("选择 Pdb 文件", GetStartDirectory(s_pdbPath), "json");
                if (!string.IsNullOrEmpty(picked))
                {
                    s_pdbPath = ToProjectRelativePath(picked);
                }
            }

            if (!string.IsNullOrEmpty(s_logPath) && !File.Exists(s_logPath))
            {
                EditorGUILayout.HelpBox("日志文件不存在，请检查路径或先运行游戏生成日志。", MessageType.Error);
                return;
            }

            if (!string.IsNullOrEmpty(s_pdbPath) && !File.Exists(s_pdbPath))
            {
                EditorGUILayout.HelpBox("Pdb 文件不存在，请先在「插入日志代码」页执行插桩。", MessageType.Error);
                return;
            }

            EditorGUI.BeginDisabledGroup(
                string.IsNullOrEmpty(s_logPath) || !File.Exists(s_logPath)
                || string.IsNullOrEmpty(s_pdbPath) || !File.Exists(s_pdbPath));

            if (GUILayout.Button("导出文本格式"))
            {
                var file = LogTrackFile.Open(s_logPath);
                var pdb = LogTrackPdbFile.Open(s_pdbPath);
                if (file == null || pdb == null)
                {
                    Debug.LogError("日志或 Pdb 解析失败，请确认文件格式正确。");
                    return;
                }

                s_logExpPath = s_logPath.Replace(".bin", ".log").Replace(".json", ".log");
                if (s_logExpPath == s_logPath)
                {
                    s_logExpPath += ".exported.log";
                }

                file.SaveAsText(s_logExpPath, pdb);
                Debug.Log("LogTrack 文本日志已导出: " + s_logExpPath);
            }

            EditorGUI.EndDisabledGroup();

            if (!string.IsNullOrEmpty(s_logExpPath))
            {
                EditorGUILayout.TextField("导出成功：", s_logExpPath);
                if (GUILayout.Button("在文件夹中显示", GUILayout.Width(120)))
                {
                    EditorUtility.RevealInFinder(Path.GetFullPath(s_logExpPath));
                }
            }
        }

        private static string GetStartDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return Application.dataPath;
            }

            var full = Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
            if (Directory.Exists(full))
            {
                return full;
            }

            var dir = Path.GetDirectoryName(full);
            return string.IsNullOrEmpty(dir) ? Application.dataPath : dir;
        }

        private static string ToProjectRelativePath(string absolutePath)
        {
            absolutePath = absolutePath.Replace('\\', '/');
            var dataPath = Application.dataPath.Replace('\\', '/');
            if (absolutePath.StartsWith(dataPath, System.StringComparison.OrdinalIgnoreCase))
            {
                return "Assets" + absolutePath.Substring(dataPath.Length);
            }

            return absolutePath;
        }
    }

    internal static class LogTrackInsertPanel
    {
        private static string s_baseDir = "Assets/Scripts";
        private static string s_pdbDir = "Assets/LogTrackGenerated";
        private static string s_logTrackClass = "FSPDebuger";
        private static string s_logTrackMacro = "FSPDebuger";
        private static string[] s_excludeFiles = { "Editor/PortalPrefabCreator.cs" };

        public static void OnGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("自动在脚本函数里插入日志记录代码", EditorStyles.miniLabel);

            s_baseDir = EditorGUILayout.TextField("目标目录：", s_baseDir);
            if (GUILayout.Button("选择脚本目录", GUILayout.Width(120)))
            {
                var picked = EditorUtility.OpenFolderPanel("选择脚本目录", GetStartDirectory(s_baseDir), string.Empty);
                if (!string.IsNullOrEmpty(picked))
                {
                    s_baseDir = ToProjectRelativePath(picked);
                }
            }

            s_pdbDir = EditorGUILayout.TextField("Pdb目录：", s_pdbDir);
            s_logTrackClass = EditorGUILayout.TextField("LogTrack类：", s_logTrackClass);
            s_logTrackMacro = EditorGUILayout.TextField("替代宏：", s_logTrackMacro);

            if (s_excludeFiles != null && s_excludeFiles.Length > 0)
            {
                EditorGUILayout.LabelField("排除文件：", string.Join(", ", s_excludeFiles));
            }

            LogTrackProjectSettings.AutoInstrumentOnCompile = EditorGUILayout.Toggle(
                "编译后自动插桩",
                LogTrackProjectSettings.AutoInstrumentOnCompile);

            EditorGUILayout.Space(4);
            if (GUILayout.Button("从 LogTrackSetting.txt 加载配置"))
            {
                var settingPath = Path.Combine(s_baseDir, "LogTrackSetting.txt");
                if (File.Exists(settingPath))
                {
                    var setting = new LogTrackSetting();
                    setting.Load(Path.GetFullPath(s_baseDir) + Path.DirectorySeparatorChar, "LogTrackSetting.txt");
                    s_logTrackClass = setting.logTrackClass;
                    s_logTrackMacro = setting.logTrackMacro;
                    s_excludeFiles = setting.excludeFiles ?? System.Array.Empty<string>();
                    Debug.Log("已从 LogTrackSetting.txt 加载配置。");
                }
                else
                {
                    Debug.LogWarning("未找到 " + settingPath);
                }
            }

            EditorGUILayout.Space(4);
            if (GUILayout.Button("插入日志代码"))
            {
                var fullBase = Path.GetFullPath(s_baseDir) + Path.DirectorySeparatorChar;
                var fullPdb = Path.GetFullPath(s_pdbDir) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(fullPdb);

                FSPDebugerTool.InsertLogTrack(fullBase, fullPdb, s_logTrackClass, s_logTrackMacro, s_excludeFiles);
                AssetDatabase.Refresh();
                Debug.Log("LogTrack 插桩完成，Pdb 输出到: " + fullPdb);
            }

            if (GUILayout.Button("创建演示对象（用于快速测试）"))
            {
                LogTrackEditorMenu.CreateDemoObject();
            }
        }

        private static string GetStartDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return Application.dataPath;
            }

            var full = Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
            if (Directory.Exists(full))
            {
                return full;
            }

            var dir = Path.GetDirectoryName(full);
            return string.IsNullOrEmpty(dir) ? Application.dataPath : dir;
        }

        private static string ToProjectRelativePath(string absolutePath)
        {
            absolutePath = absolutePath.Replace('\\', '/');
            var dataPath = Application.dataPath.Replace('\\', '/');
            if (absolutePath.StartsWith(dataPath, System.StringComparison.OrdinalIgnoreCase))
            {
                return "Assets" + absolutePath.Substring(dataPath.Length);
            }

            return absolutePath;
        }
    }
}
#endif
