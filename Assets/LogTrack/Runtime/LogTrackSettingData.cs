using System;
using System.Collections.Generic;
using System.IO;

public class LogTrackSetting
{
    public static readonly string[] DefaultIncludeAssemblies = { "Assembly-CSharp" };
    public static readonly string[] DefaultExcludeAssemblies =
    {
        "Assembly-CSharp-Editor",
        "LogTrack.Runtime",
        "LogTrack.Editor"
    };

    public bool loaded { get; private set; }
    public string baseDir { get; private set; }
    public string logTrackClass = "FSPDebuger";
    public string logTrackMacro = "FSPDebuger";
    public bool autoInstrumentOnCompile;
    public string[] includeAssemblies = DefaultIncludeAssemblies;
    public string[] excludeAssemblies = DefaultExcludeAssemblies;

    public void Load(string baseDir, string filename)
    {
        if (!baseDir.EndsWith("/") && !baseDir.EndsWith("\\"))
        {
            baseDir += "/";
        }

        loaded = true;
        this.baseDir = baseDir;

        var settingPath = baseDir + filename;
        if (!File.Exists(settingPath))
        {
            return;
        }

        var lines = File.ReadAllLines(settingPath);
        if (lines.Length > 0 && !string.IsNullOrWhiteSpace(lines[0]))
        {
            logTrackClass = lines[0].Trim();
        }

        if (lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1]))
        {
            logTrackMacro = lines[1].Trim();
        }

        var listIncludes = new List<string>();
        var listExcludes = new List<string>();
        List<string> listTokens = null;
        for (int i = 2; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            if (line.StartsWith("AutoInstrumentOnCompile:", StringComparison.OrdinalIgnoreCase))
            {
                var value = line.Substring("AutoInstrumentOnCompile:".Length).Trim();
                autoInstrumentOnCompile = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (line.Equals("IncludeAssemblies:", StringComparison.OrdinalIgnoreCase))
            {
                listTokens = listIncludes;
                continue;
            }

            if (line.Equals("ExcludeAssemblies:", StringComparison.OrdinalIgnoreCase))
            {
                listTokens = listExcludes;
                continue;
            }

            // Legacy source-instrumentation sections are ignored.
            if (line.Equals("ExcludeList:", StringComparison.OrdinalIgnoreCase)
                || line.Equals("ExternalList:", StringComparison.OrdinalIgnoreCase))
            {
                listTokens = null;
                continue;
            }

            listTokens?.Add(line);
        }

        if (listIncludes.Count > 0)
        {
            includeAssemblies = listIncludes.ToArray();
        }

        if (listExcludes.Count > 0)
        {
            excludeAssemblies = listExcludes.ToArray();
        }
    }
}
