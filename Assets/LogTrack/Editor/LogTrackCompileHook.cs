#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace LogTrack.Editor
{
    [InitializeOnLoad]
    public static class LogTrackCompileHook
    {
        private static bool s_running;

        static LogTrackCompileHook()
        {
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        private static void OnCompilationFinished(object obj)
        {
            if (!LogTrackProjectSettings.AutoInstrumentOnCompile || s_running) return;

            var scriptsRoot = Path.GetFullPath(LogTrackSettings.InstrumentRoot);
            var pdbDir = Path.GetFullPath(LogTrackSettings.PdbOutputDir);
            if (!Directory.Exists(scriptsRoot))
            {
                Debug.LogWarning("LogTrack 自动插桩跳过：目录不存在 " + scriptsRoot);
                return;
            }

            try
            {
                s_running = true;
                Directory.CreateDirectory(pdbDir);
                FSPDebugerTool.InsertLogTrack(
                    scriptsRoot + Path.DirectorySeparatorChar,
                    pdbDir + Path.DirectorySeparatorChar,
                    LogTrackProjectSettings.LogTrackClass,
                    LogTrackProjectSettings.LogTrackMacro,
                    LogTrackProjectSettings.ExcludeFiles);
                AssetDatabase.Refresh();
                Debug.Log("LogTrack 编译后自动插桩完成，Pdb: " + pdbDir);
            }
            finally
            {
                s_running = false;
            }
        }
    }

    public static class LogTrackProjectSettings
    {
        private const string PrefKeyAutoInstrument = "LogTrack.AutoInstrumentOnCompile";

        public static bool AutoInstrumentOnCompile
        {
            get => EditorPrefs.HasKey(PrefKeyAutoInstrument)
                ? EditorPrefs.GetBool(PrefKeyAutoInstrument, false)
                : LogTrackInsertSettings.GetProjectDefaultAutoInstrumentOnCompile();
            set => EditorPrefs.SetBool(PrefKeyAutoInstrument, value);
        }

        public static void ClearAutoInstrumentOverride()
        {
            EditorPrefs.DeleteKey(PrefKeyAutoInstrument);
        }

        public static int RingBufferSize
        {
            get => LogTrackSettings.DefaultRingBufferSize;
            set => LogTrackSettings.DefaultRingBufferSize = value;
        }

        public static bool AutoStartOnPlay
        {
            get => LogTrackSettings.AutoStartOnPlay;
            set => LogTrackSettings.AutoStartOnPlay = value;
        }

        public static bool ExportOnStop
        {
            get => LogTrackSettings.ExportOnStop;
            set => LogTrackSettings.ExportOnStop = value;
        }

        public const string LogTrackClass = "FSPDebuger";
        public const string LogTrackMacro = "FSPDebuger";
        public static readonly string[] ExcludeFiles = { };
    }
}
#endif
