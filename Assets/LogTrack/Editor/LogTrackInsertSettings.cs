#if UNITY_EDITOR
using System;
using System.IO;

namespace LogTrack.Editor
{
    /// <summary>
    /// 加载 LogTrackSetting.txt，供手动插桩、编译钩子和 Batch 复用。
    /// </summary>
    public static class LogTrackInsertSettings
    {
        public const string SettingFileName = "LogTrackSetting.txt";
        private const string PluginSettingRelative = "Assets/LogTrack/Editor/LogTrackSetting.txt";
        private const string LegacySettingRelative = "Assets/Scripts/LogTrackSetting.txt";

        public static bool TryLoad(out LogTrackSetting setting)
        {
            setting = new LogTrackSetting();

            if (TryLoadFromRelativePath(LegacySettingRelative, out setting))
            {
                return true;
            }

            return TryLoadFromRelativePath(PluginSettingRelative, out setting);
        }

        public static bool TryLoadFromRelativePath(string relativePath, out LogTrackSetting setting)
        {
            setting = new LogTrackSetting();
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            var fullDir = Path.GetDirectoryName(Path.GetFullPath(relativePath));
            if (string.IsNullOrEmpty(fullDir) || !Directory.Exists(fullDir))
            {
                return false;
            }

            var settingPath = Path.Combine(fullDir, SettingFileName);
            if (!File.Exists(settingPath))
            {
                return false;
            }

            setting.Load(fullDir + Path.DirectorySeparatorChar, SettingFileName);
            return setting.loaded;
        }

        public static bool GetProjectDefaultAutoInstrumentOnCompile()
        {
            if (TryLoad(out var setting))
            {
                return setting.autoInstrumentOnCompile;
            }

            return false;
        }
    }
}
#endif
