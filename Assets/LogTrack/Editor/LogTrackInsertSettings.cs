#if UNITY_EDITOR
using System.IO;

namespace LogTrack.Editor
{
    /// <summary>
    /// 从 InstrumentRoot 加载 LogTrackSetting.txt，供手动插桩、编译钩子和 Batch 复用。
    /// </summary>
    public static class LogTrackInsertSettings
    {
        public const string SettingFileName = "LogTrackSetting.txt";

        public static bool TryLoadFromInstrumentRoot(out LogTrackSetting setting)
        {
            var root = LogTrackSettings.InstrumentRoot;
            var full = Path.GetFullPath(root);
            if (!full.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                full += Path.DirectorySeparatorChar;
            }

            return TryLoad(full, out setting);
        }

        public static bool TryLoad(string scriptsFullDir, out LogTrackSetting setting)
        {
            setting = new LogTrackSetting();
            if (string.IsNullOrEmpty(scriptsFullDir))
            {
                return false;
            }

            if (!scriptsFullDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                scriptsFullDir += Path.DirectorySeparatorChar;
            }

            if (!Directory.Exists(scriptsFullDir.TrimEnd(Path.DirectorySeparatorChar)))
            {
                return false;
            }

            var settingPath = Path.Combine(scriptsFullDir, SettingFileName);
            if (!File.Exists(settingPath))
            {
                return false;
            }

            setting.Load(scriptsFullDir, SettingFileName);
            return setting.loaded;
        }

        public static bool GetProjectDefaultAutoInstrumentOnCompile()
        {
            if (TryLoadFromInstrumentRoot(out var setting))
            {
                return setting.autoInstrumentOnCompile;
            }

            return false;
        }
    }
}
#endif
