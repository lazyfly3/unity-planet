#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LogTrack.Editor
{
    internal sealed class AssemblyCatalogEntry
    {
        public string assemblyName;
        public string dllPath;
        public bool selectable;
        public string reason;
    }

    internal static class LogTrackIlAssemblyCatalog
    {
        private const string PrefSelectedAssemblies = "LogTrack.SelectedAssemblies";

        private static readonly string[] BuiltInExcludePrefixes =
        {
            "Unity.",
            "UnityEngine.",
            "UnityEditor.",
            "UnityEditor.",
            "System.",
            "mscorlib",
            "netstandard",
            "LogTrack.",
            "nunit.",
            "Mono.",
            "Microsoft."
        };

        public static string GetDllPath(string assemblyName)
        {
            return Path.Combine(LogTrackCecilResolver.ScriptAssembliesDir, assemblyName + ".dll");
        }

        public static List<AssemblyCatalogEntry> ScanCatalog(IEnumerable<string> configExcludeAssemblies = null)
        {
            var dir = LogTrackCecilResolver.ScriptAssembliesDir;
            var configExcludes = new HashSet<string>(
                configExcludeAssemblies ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            var entries = new List<AssemblyCatalogEntry>();
            if (!Directory.Exists(dir))
            {
                return entries;
            }

            foreach (var dllPath in Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName))
            {
                var assemblyName = Path.GetFileNameWithoutExtension(dllPath);
                var selectable = IsSelectable(assemblyName, configExcludes, out var reason);
                entries.Add(new AssemblyCatalogEntry
                {
                    assemblyName = assemblyName,
                    dllPath = dllPath,
                    selectable = selectable,
                    reason = reason
                });
            }

            return entries;
        }

        public static string[] ResolveTargetAssemblies(LogTrackSetting setting)
        {
            var includes = setting?.includeAssemblies ?? LogTrackSetting.DefaultIncludeAssemblies;
            var excludes = new HashSet<string>(
                setting?.excludeAssemblies ?? LogTrackSetting.DefaultExcludeAssemblies,
                StringComparer.OrdinalIgnoreCase);

            var resolved = new List<string>();
            foreach (var name in includes)
            {
                if (string.IsNullOrWhiteSpace(name) || excludes.Contains(name))
                {
                    continue;
                }

                if (!IsSelectable(name, excludes, out _))
                {
                    continue;
                }

                resolved.Add(name);
            }

            return resolved.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static string[] LoadSelectedAssembliesOrDefault()
        {
            var saved = EditorPrefs.GetString(PrefSelectedAssemblies, string.Empty);
            if (!string.IsNullOrEmpty(saved))
            {
                return saved.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            }

            return LogTrackSetting.DefaultIncludeAssemblies;
        }

        public static void SaveSelectedAssemblies(IEnumerable<string> assemblyNames)
        {
            var value = string.Join(";", assemblyNames ?? Array.Empty<string>());
            EditorPrefs.SetString(PrefSelectedAssemblies, value);
        }

        private static bool IsSelectable(string assemblyName, HashSet<string> configExcludes, out string reason)
        {
            if (configExcludes.Contains(assemblyName))
            {
                reason = "配置排除";
                return false;
            }

            if (assemblyName.EndsWith("-Editor", StringComparison.OrdinalIgnoreCase))
            {
                reason = "Editor 程序集";
                return false;
            }

            foreach (var prefix in BuiltInExcludePrefixes)
            {
                if (assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "内置排除";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }
    }
}
#endif
