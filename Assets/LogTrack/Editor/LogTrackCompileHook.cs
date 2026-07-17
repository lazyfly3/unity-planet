#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
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
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (!LogTrackProjectSettings.AutoInstrumentOnCompile || s_running)
            {
                return;
            }

            if (!LogTrackInsertSettings.TryLoad(out var setting))
            {
                Debug.LogWarning("LogTrack 自动插桩跳过：未找到 LogTrackSetting.txt");
                return;
            }

            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
            var targets = LogTrackIlAssemblyCatalog.ResolveTargetAssemblies(setting);
            if (Array.IndexOf(targets, assemblyName) < 0)
            {
                return;
            }

            if (messages != null && messages.Any(message => message.type == CompilerMessageType.Error))
            {
                return;
            }

            if (!File.Exists(assemblyPath))
            {
                return;
            }

            if (!LogTrackIlInstrumenter.TryValidateRuntimeHooks(setting.logTrackClass, out var hookError))
            {
                Debug.LogWarning("LogTrack 自动插桩跳过：" + hookError);
                return;
            }

            try
            {
                s_running = true;
                var pdbDir = Path.GetFullPath(LogTrackSettings.PdbOutputDir);
                Directory.CreateDirectory(pdbDir);
                var result = LogTrackIlInstrumenter.PatchAssemblyAtPath(
                    assemblyPath,
                    pdbDir,
                    assemblyName,
                    setting.logTrackClass);

                if (result.success)
                {
                    Debug.Log($"LogTrack IL 自动插桩完成: {assemblyName}, methods={result.patchedMethods}, pdb={pdbDir}");
                }
                else if (!string.IsNullOrEmpty(result.error))
                {
                    Debug.LogWarning("LogTrack IL 自动插桩: " + result.error);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("LogTrack IL 自动插桩失败: " + ex.Message);
            }
            finally
            {
                s_running = false;
            }
        }
    }

    public static class LogTrackProjectSettings
    {
        private const string PrefKeyAutoInstrumentOverride = "LogTrack.AutoInstrumentOnCompile.Override";
        private const string LegacyPrefKeyAutoInstrument = "LogTrack.AutoInstrumentOnCompile";

        public static bool AutoInstrumentOnCompile
        {
            get
            {
                if (EditorPrefs.HasKey(PrefKeyAutoInstrumentOverride))
                {
                    return EditorPrefs.GetBool(PrefKeyAutoInstrumentOverride);
                }

                return LogTrackInsertSettings.GetProjectDefaultAutoInstrumentOnCompile();
            }
        }

        public static bool HasAutoInstrumentOverride => EditorPrefs.HasKey(PrefKeyAutoInstrumentOverride);

        public static bool ProjectDefaultAutoInstrumentOnCompile =>
            LogTrackInsertSettings.GetProjectDefaultAutoInstrumentOnCompile();

        public static void SetAutoInstrumentOnCompile(bool value)
        {
            EditorPrefs.SetBool(PrefKeyAutoInstrumentOverride, value);
        }

        public static void ClearAutoInstrumentOverride()
        {
            EditorPrefs.DeleteKey(PrefKeyAutoInstrumentOverride);
            EditorPrefs.DeleteKey(LegacyPrefKeyAutoInstrument);
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
    }
}
#endif
