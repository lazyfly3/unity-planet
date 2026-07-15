using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

public enum LogHashType
{
    NewHash = 0,
    OldHash = 1,
}

public static class LogTrackCSharp
{
    private static string ms_logTrackClass = "FSPDebuger";
    private static string ms_logTrackMacro = "FSPDebuger";

    public const int TabSpaceSize = 4;
    public const int MaxArgCount = 7;

    private static Regex ms_regexFuncHead =
        new Regex(@"(public|private|protected)((\s+(static|override|virtual)*\s+)|\s+)\w+(<\w+>)*(\[\])*\s+\w+(<\w+>)*\s*\(([^\)]+\s*)?\)");

    private static Regex ms_regexFuncName = new Regex(@"\w+(<\w+>)*\s*\(");
    private static Regex ms_regexFuncParam = new Regex(@"\(([^\)]+\s*)?\)");
    private static Regex ms_regexFuncAll =
        new Regex(@"(public|private|protected)((\s+(static|override|virtual)*\s+)|\s+)\w+(<\w+>)*(\[\])*\s+\w+(<\w+>)*\s*\(([^\)]+\s*)?\)\s*\{[^{}]*(((?'Open'\{)[^{}]*)+((?'-Open'\})[^{}]*)+)*(?(Open)(?!))\}");
    private static Regex ms_regexFirstCode = new Regex(@"[^;]*;");
    private static Regex ms_regexLogTrackCodeIgnore;
    private static Regex ms_regexLogTrackCode;
    private static Regex ms_regexLogTrackMacro;
    private static readonly Regex ms_regexLeftBrace = new Regex(@"\{");
    private static readonly Regex ms_regexNumber = new Regex(@"\d+");
    private static readonly Regex ms_regexValidType = new Regex(@"\b(long|ulong|int|uint|short|ushort|byte|float|double|bool)\b");
    private static readonly Regex ms_regexClassName = new Regex(@"\bclass\s+(\w+)");
    private static readonly Regex ms_regexNamespace = new Regex(@"\bnamespace\s+([\w\.]+)");

    static LogTrackCSharp()
    {
        ResetLogCodeRegex();
    }

    private static void ResetLogCodeRegex()
    {
        ms_regexLogTrackCodeIgnore = new Regex(@"\b(" + ms_logTrackMacro + @")\s*\.\s*IgnoreTrack\s*\(([^;]+\s*)?\);");
        ms_regexLogTrackCode = new Regex(@"\b(" + ms_logTrackClass + @")\s*\.\s*LogTrack\s*\(([^;]+\s*)?\);");
        ms_regexLogTrackMacro = new Regex(@"\b(" + ms_logTrackMacro + @")\s*\.\s*(LogTrack|BeginTrack|EndTrack|EnterTrackFrame)\s*\(([^;]+\s*)?\);");
    }

    public static void SetLogTrackMacro(string logTrackMacro)
    {
        ms_logTrackMacro = string.IsNullOrEmpty(logTrackMacro) ? ms_logTrackClass : logTrackMacro;
        ResetLogCodeRegex();
    }

    public static void SetLogTrackClass(string logTrackClass)
    {
        if (string.IsNullOrEmpty(logTrackClass))
        {
            Debuger.LogError("不能为空！");
            return;
        }
        ms_logTrackClass = logTrackClass;
        ResetLogCodeRegex();
    }

    public static void InsertLogTrackCode(string baseDir, string subPath)
    {
        var fullPath = Path.Combine(baseDir, subPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath)) return;

        var text = File.ReadAllText(fullPath);
        var matches = ms_regexFuncAll.Matches(text);
        var hasChanged = false;

        for (int i = matches.Count - 1; i >= 0; i--)
        {
            var matchFuncAll = matches[i];
            var matchFuncHead = ms_regexFuncHead.Match(text, matchFuncAll.Index, matchFuncAll.Length);
            var matchLeftBrace = ms_regexLeftBrace.Match(text, matchFuncAll.Index, matchFuncAll.Length);
            if (!matchLeftBrace.Success) continue;

            int len = matchFuncAll.Index + matchFuncAll.Length - (matchLeftBrace.Index + matchLeftBrace.Length);
            var matchFirstCode = ms_regexFirstCode.Match(text, matchLeftBrace.Index + matchLeftBrace.Length, len);
            if (!matchFirstCode.Success) continue;
            var funcSlice = text.Substring(matchFuncAll.Index, matchFuncAll.Length);
            if (funcSlice.Contains(ms_logTrackClass + ".LogTrack"))
            {
                continue;
            }
            if (ms_regexLogTrackCodeIgnore.IsMatch(matchFirstCode.Value)) continue;

            var popCode = "\n    if(" + ms_logTrackClass + ".EnableLogTrackInternal)" + ms_logTrackClass + ".PopDepth();";
            if (!text.Substring(matchFuncAll.Index, matchFuncAll.Length).Contains(".PopDepth()"))
            {
                var insertPopAt = matchFuncAll.Index + matchFuncAll.Length - 1;
                text = text.Insert(insertPopAt, popCode);
            }

            var textLogCode = GetLogTrackCode(matchFuncHead.Value);
            text = text.Insert(matchLeftBrace.Index + matchLeftBrace.Length, textLogCode);

            hasChanged = true;
        }

        if (hasChanged)
        {
            File.WriteAllText(fullPath, text);
        }

        EnsureDepthHooks(ref text, fullPath);
    }

    /// <summary>为已插桩但缺少 PushDepth/PopDepth 的函数补齐 depth 钩子。</summary>
    private static void EnsureDepthHooks(ref string text, string fullPath)
    {
        var matches = ms_regexFuncAll.Matches(text);
        var hasChanged = false;

        for (int i = matches.Count - 1; i >= 0; i--)
        {
            var matchFuncAll = matches[i];
            var funcStart = matchFuncAll.Index;
            var funcLen = matchFuncAll.Length;
            var funcBody = text.Substring(funcStart, funcLen);
            if (!funcBody.Contains(ms_logTrackClass + ".LogTrack")) continue;

            var newBody = funcBody;
            if (!newBody.Contains(".PopDepth()"))
            {
                var popCode = "\n    if(" + ms_logTrackClass + ".EnableLogTrackInternal)" + ms_logTrackClass + ".PopDepth();";
                newBody = newBody.Insert(newBody.Length - 1, popCode);
            }

            if (!newBody.Contains(".PushDepth()"))
            {
                var legacy = "if(" + ms_logTrackClass + ".EnableLogTrackInternal)" + ms_logTrackClass + ".LogTrack";
                var wrapped = "if(" + ms_logTrackClass + ".EnableLogTrackInternal){" + ms_logTrackClass + ".PushDepth();" + ms_logTrackClass + ".LogTrack";
                if (newBody.Contains(legacy))
                {
                    var idx = newBody.IndexOf(legacy, StringComparison.Ordinal);
                    newBody = newBody.Substring(0, idx) + wrapped + newBody.Substring(idx + legacy.Length);
                    var logPos = newBody.IndexOf(ms_logTrackClass + ".LogTrack", StringComparison.Ordinal);
                    if (logPos >= 0)
                    {
                        var semi = newBody.IndexOf(");", logPos, StringComparison.Ordinal);
                        if (semi >= 0 && semi + 2 < newBody.Length && newBody[semi + 2] != '}')
                        {
                            newBody = newBody.Insert(semi + 2, "}");
                        }
                    }
                }
            }

            if (newBody != funcBody)
            {
                text = text.Remove(funcStart, funcLen).Insert(funcStart, newBody);
                hasChanged = true;
            }
        }

        if (hasChanged)
        {
            File.WriteAllText(fullPath, text);
        }
    }

    private static string GetLogTrackCode(string textFuncHead)
    {
        string code = "if(" + ms_logTrackClass + ".EnableLogTrackInternal){" + ms_logTrackClass + ".PushDepth();" + ms_logTrackClass + ".LogTrack(0";

        var match = ms_regexFuncParam.Match(textFuncHead);
        var tmp = match.Value.Substring(1, match.Value.Length - 2);
        var args = tmp.Split(',');
        int argCount = 0;
        var regex = new Regex(@"\w+(\[\])*");

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Contains("<") || arg.Contains(">") || arg.Contains("[") || arg.Contains("params"))
            {
                continue;
            }

            var matches = regex.Matches(arg);
            if (matches.Count <= 1) continue;
            string match0 = matches[0].Value;
            if (!ms_regexValidType.IsMatch(match0)) continue;

            argCount++;
            if (argCount > MaxArgCount) continue;

            string name = matches[1].Value;
            code += match0 == "bool" ? ", (" + name + "?1:0)" : ", (int)" + name;
        }

        code += ");}";
        var matchFuncName = ms_regexFuncName.Match(textFuncHead);
        if (matchFuncName.Success)
        {
            var funcName = matchFuncName.Value.Substring(0, matchFuncName.Value.Length - 1);
            code += "/#" + funcName + "#/";
        }

        return code;
    }

    public static void HandleLogTrackMacro(string baseDir, string subPath)
    {
        if (ms_logTrackMacro == ms_logTrackClass) return;

        var fullPath = Path.Combine(baseDir, subPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath)) return;

        var lines = File.ReadAllLines(fullPath);
        var hasChanged = false;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!ms_regexLogTrackMacro.IsMatch(lines[i])) continue;
            lines[i] = lines[i].Replace(ms_logTrackMacro, ms_logTrackClass);
            hasChanged = true;
        }

        if (hasChanged)
        {
            File.WriteAllLines(fullPath, lines);
        }
    }

    public static void HashLogTrackCode(string baseDir, string subPath, LogTrackPdbFile pdb, LogHashType hashType)
    {
        var fullPath = Path.Combine(baseDir, subPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath)) return;

        var fileText = File.ReadAllText(fullPath);
        var className = ExtractClassName(fileText, subPath);
        var lines = File.ReadAllLines(fullPath);
        var hasChanged = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var matchLogCode = ms_regexLogTrackCode.Match(line);
            if (!matchLogCode.Success) continue;

            var matchLogHash = ms_regexNumber.Match(line, matchLogCode.Index, matchLogCode.Length);
            if (!matchLogHash.Success) continue;

            int.TryParse(matchLogHash.Value, out var hash);
            int argCnt = GetLogTrackArgCnt(matchLogCode.Value);
            var dbgStr = GetLogTrackDebugString(ref line, matchLogCode.Index + matchLogCode.Length);
            var funcName = !string.IsNullOrEmpty(dbgStr) ? dbgStr : ExtractFuncNameFromLine(line);
            if (string.IsNullOrEmpty(funcName))
            {
                funcName = ExtractFuncNameFromFileAtLine(fileText, i + 1);
            }

            if ((hashType == LogHashType.NewHash && hash == 0) ||
                (hashType == LogHashType.OldHash && hash != 0))
            {
                int validHash = pdb.AddItem(hash, argCnt, subPath, i + 1, dbgStr, className, funcName);
                if (validHash != hash)
                {
                    line = line.Remove(matchLogHash.Index, matchLogHash.Length);
                    line = line.Insert(matchLogHash.Index, validHash.ToString());
                    lines[i] = line;
                    hasChanged = true;
                }
            }
        }

        if (hasChanged)
        {
            File.WriteAllLines(fullPath, lines);
        }
    }

    private static string ExtractClassName(string fileText, string subPath)
    {
        var nsMatch = ms_regexNamespace.Match(fileText);
        var classMatch = ms_regexClassName.Match(fileText);
        var shortName = classMatch.Success ? classMatch.Groups[1].Value : Path.GetFileNameWithoutExtension(subPath);
        if (nsMatch.Success)
        {
            return nsMatch.Groups[1].Value + "." + shortName;
        }
        return shortName;
    }

    private static string ExtractFuncNameFromLine(string line)
    {
        var a = line.IndexOf("/#", StringComparison.Ordinal);
        var b = line.IndexOf("#/", StringComparison.Ordinal);
        if (a >= 0 && b > a)
        {
            return line.Substring(a + 2, b - a - 2);
        }
        return string.Empty;
    }

    private static string ExtractFuncNameFromFileAtLine(string fileText, int lineNumber)
    {
        var lines = fileText.Split('\n');
        var start = Math.Max(0, lineNumber - 40);
        for (int i = lineNumber - 1; i >= start; i--)
        {
            var matchFuncHead = ms_regexFuncHead.Match(lines[i]);
            if (!matchFuncHead.Success) continue;
            var matchFuncName = ms_regexFuncName.Match(matchFuncHead.Value);
            if (!matchFuncName.Success) continue;
            return matchFuncName.Value.Substring(0, matchFuncName.Value.Length - 1);
        }

        return string.Empty;
    }

    private static int GetLogTrackArgCnt(string code)
    {
        while (true)
        {
            int a = code.IndexOf("/*", StringComparison.Ordinal);
            int b = code.IndexOf("*/", StringComparison.Ordinal);
            if (a >= 0 && b >= 0)
            {
                code = code.Replace(code.Substring(a, b - a + 2), string.Empty);
            }
            else break;
        }
        return code.Split(',').Length - 1;
    }

    private static string GetLogTrackDebugString(ref string logCode, int beginIndex)
    {
        var a = logCode.IndexOf("/#", beginIndex, StringComparison.Ordinal);
        var b = logCode.IndexOf("#/", beginIndex, StringComparison.Ordinal);
        if (a >= 0 && b >= 0)
        {
            var tmp = logCode.Substring(a, b - a + 2);
            logCode = logCode.Replace(tmp, string.Empty);
            return tmp.Substring(2, tmp.Length - 4);
        }
        return string.Empty;
    }
}

public class LogTrackSetting
{
    public bool loaded { get; private set; }
    public string baseDir { get; private set; }
    public string logTrackClass = "FSPDebuger";
    public string logTrackMacro = "KHDebug";
    public string[] excludeFiles = { @"Runtime/Utils/KHDebug.cs" };
    public string[] externalFiles = Array.Empty<string>();

    public void Load(string baseDir, string filename)
    {
        if (!baseDir.EndsWith("/") && !baseDir.EndsWith("\\"))
        {
            baseDir += "/";
        }

        loaded = true;
        this.baseDir = baseDir;

        var settingPath = baseDir + filename;
        if (!File.Exists(settingPath)) return;

        var lines = File.ReadAllLines(settingPath);
        logTrackClass = lines[0];
        logTrackMacro = lines.Length > 1 ? lines[1] : logTrackClass;

        var listExcludes = new List<string>();
        var listExternals = new List<string>();
        List<string> listTokens = null;
        for (int i = 2; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line == "ExcludeList:")
            {
                listTokens = listExcludes;
                continue;
            }
            if (line == "ExternalList:")
            {
                listTokens = listExternals;
                continue;
            }
            listTokens?.Add(line);
        }

        excludeFiles = listExcludes.ToArray();
        externalFiles = listExternals.ToArray();
    }
}

public static class FSPDebugerTool
{
    public static void InsertLogTrack(string baseDir, string pdbDir, string logTrackClass, string logTrackMacro, string[] excludeFiles = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        baseDir = baseDir.Replace('\\', '/');
        if (!baseDir.EndsWith("/")) baseDir += "/";
        pdbDir = pdbDir.Replace('\\', '/');
        if (!pdbDir.EndsWith("/")) pdbDir += "/";

        var listFilePaths = Directory.GetFiles(baseDir, "*.cs", SearchOption.AllDirectories);
        var pdb = new LogTrackPdbFile();

        LogTrackCSharp.SetLogTrackClass(logTrackClass);
        LogTrackCSharp.SetLogTrackMacro(logTrackMacro);

        var hashExclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (excludeFiles != null)
        {
            foreach (var exclude in excludeFiles)
            {
                hashExclude.Add(exclude.Replace('\\', '/'));
            }
        }

        foreach (var filepath in listFilePaths)
        {
            var normalized = filepath.Replace('\\', '/');
            var subPath = normalized.Substring(baseDir.Length);
            if (hashExclude.Contains(subPath)) continue;

            LogTrackCSharp.InsertLogTrackCode(baseDir, subPath);
            LogTrackCSharp.HandleLogTrackMacro(baseDir, subPath);
            LogTrackCSharp.HashLogTrackCode(baseDir, subPath, pdb, LogHashType.OldHash);
            LogTrackCSharp.HashLogTrackCode(baseDir, subPath, pdb, LogHashType.NewHash);
        }

        foreach (var external in LoadExternalFiles(baseDir, excludeFiles))
        {
            LogTrackCSharp.InsertLogTrackCode(baseDir, external);
            LogTrackCSharp.HandleLogTrackMacro(baseDir, external);
            LogTrackCSharp.HashLogTrackCode(baseDir, external, pdb, LogHashType.OldHash);
            LogTrackCSharp.HashLogTrackCode(baseDir, external, pdb, LogHashType.NewHash);
        }

        Directory.CreateDirectory(pdbDir);
        pdb.Save(pdbDir + "LogPdb.pdb.json");
        pdb.SaveAsCSV(pdbDir + "LogPdb.pdb.csv");
        sw.Stop();
        LogTrackBenchmark.RecordInsertMs(sw.Elapsed.TotalMilliseconds);
    }

    private static IEnumerable<string> LoadExternalFiles(string baseDir, string[] excludeFiles)
    {
        // 复现版保留接口，默认无 external。
        yield break;
    }
}
