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
    private static readonly Regex ms_regexTypeName =
        new Regex(@"\b(?:class|struct|interface|record(?:\s+(?:class|struct))?)\s+(\w+)");
    private static readonly Regex ms_regexNamespace = new Regex(@"\bnamespace\s+([\w\.]+)");
    private const string DepthGuardName = "__logTrackDepthEntered";

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

            var popCode = GetDepthFinallyCode();
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

    public static void RemoveLogTrackCode(string baseDir, string subPath)
    {
        var fullPath = Path.Combine(baseDir, subPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath)) return;

        var text = File.ReadAllText(fullPath);
        var matches = ms_regexFuncAll.Matches(text);
        var escapedClass = Regex.Escape(ms_logTrackClass);
        var legacyEntry = new Regex(
            @"if\(" + escapedClass + @"\.EnableLogTrackInternal\)\{" +
            escapedClass + @"\.PushDepth\(\);" + escapedClass + @"\.LogTrack\([^;]*\);\}" +
            @"(?:/#.*?#/)?");
        var legacyPop = new Regex(
            @"\s*if\(" + escapedClass + @"\.EnableLogTrackInternal\)" +
            escapedClass + @"\.PopDepth\(\);");
        var guardedEntry = new Regex(
            @"bool\s+" + DepthGuardName + @"\s*=\s*" + escapedClass + @"\.EnableLogTrackInternal;\s*" +
            @"if\(" + DepthGuardName + @"\)\{" + escapedClass + @"\.PushDepth\(\);" +
            escapedClass + @"\.LogTrack\([^;]*\);\}(?:/#.*?#/)?\s*try\s*\{");
        var guardedFinally = new Regex(
            @"\}\s*finally\s*\{\s*if\(" + DepthGuardName + @"\)" +
            escapedClass + @"\.PopDepth\(\);\s*\}");
        var hasChanged = false;

        for (int i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];
            var body = text.Substring(match.Index, match.Length);
            if (!body.Contains(ms_logTrackClass + ".LogTrack")) continue;

            var cleaned = guardedEntry.Replace(body, string.Empty, 1);
            cleaned = guardedFinally.Replace(cleaned, string.Empty, 1);
            cleaned = legacyEntry.Replace(cleaned, string.Empty, 1);
            cleaned = legacyPop.Replace(cleaned, string.Empty, 1);
            if (cleaned == body) continue;

            text = text.Remove(match.Index, match.Length).Insert(match.Index, cleaned);
            hasChanged = true;
        }

        if (hasChanged) File.WriteAllText(fullPath, text);
    }

    /// <summary>为已插桩但缺少 PushDepth/PopDepth 的函数补齐 depth 钩子。</summary>
    private static void EnsureDepthHooks(ref string text, string fullPath)
    {
        var matches = ms_regexFuncAll.Matches(text);
        var hasChanged = false;
        var escapedClass = Regex.Escape(ms_logTrackClass);
        var legacyEntry = new Regex(
            @"if\(" + escapedClass + @"\.EnableLogTrackInternal\)\{" +
            escapedClass + @"\.PushDepth\(\);(?<log>" + escapedClass + @"\.LogTrack\([^;]*\);)\}");
        var legacyPop = new Regex(
            @"\s*if\(" + escapedClass + @"\.EnableLogTrackInternal\)" +
            escapedClass + @"\.PopDepth\(\);");

        for (int i = matches.Count - 1; i >= 0; i--)
        {
            var matchFuncAll = matches[i];
            var funcStart = matchFuncAll.Index;
            var funcLen = matchFuncAll.Length;
            var funcBody = text.Substring(funcStart, funcLen);
            if (!funcBody.Contains(ms_logTrackClass + ".LogTrack")) continue;
            if (funcBody.Contains(DepthGuardName) && funcBody.Contains("finally")) continue;

            var entry = legacyEntry.Match(funcBody);
            if (!entry.Success) continue;

            var guardedEntry =
                "bool " + DepthGuardName + " = " + ms_logTrackClass + ".EnableLogTrackInternal;\n    " +
                "if(" + DepthGuardName + "){" + ms_logTrackClass + ".PushDepth();" +
                entry.Groups["log"].Value + "}\n    try\n    {";
            var newBody = funcBody.Remove(entry.Index, entry.Length).Insert(entry.Index, guardedEntry);
            newBody = legacyPop.Replace(newBody, string.Empty);
            newBody = newBody.Insert(newBody.Length - 1, GetDepthFinallyCode());

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
        string code = "bool " + DepthGuardName + " = " + ms_logTrackClass + ".EnableLogTrackInternal;\n    " +
                      "if(" + DepthGuardName + "){" + ms_logTrackClass + ".PushDepth();" +
                      ms_logTrackClass + ".LogTrack(0";

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

        code += "\n    try\n    {";

        return code;
    }

    private static string GetDepthFinallyCode()
    {
        return "\n    }\n    finally\n    {\n        if(" + DepthGuardName + ")" +
               ms_logTrackClass + ".PopDepth();\n    }";
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
                var className = ExtractClassNameAtLine(fileText, i + 1, subPath);
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

    private static string ExtractClassNameAtLine(string fileText, int lineNumber, string subPath)
    {
        var nsMatch = ms_regexNamespace.Match(fileText);
        var targetIndex = GetLineStartIndex(fileText, lineNumber);
        var containingTypes = new List<KeyValuePair<int, string>>();

        foreach (Match typeMatch in ms_regexTypeName.Matches(fileText))
        {
            if (typeMatch.Index > targetIndex) break;

            var openBrace = fileText.IndexOf('{', typeMatch.Index + typeMatch.Length);
            if (openBrace < 0 || openBrace > targetIndex) continue;

            var semicolon = fileText.IndexOf(';', typeMatch.Index + typeMatch.Length);
            if (semicolon >= 0 && semicolon < openBrace) continue;

            var closeBrace = FindMatchingBrace(fileText, openBrace);
            if (closeBrace >= targetIndex)
            {
                containingTypes.Add(new KeyValuePair<int, string>(openBrace, typeMatch.Groups[1].Value));
            }
        }

        containingTypes.Sort((a, b) => a.Key.CompareTo(b.Key));
        var shortName = containingTypes.Count > 0
            ? string.Join(".", containingTypes.ConvertAll(item => item.Value).ToArray())
            : Path.GetFileNameWithoutExtension(subPath);
        return nsMatch.Success ? nsMatch.Groups[1].Value + "." + shortName : shortName;
    }

    private static int GetLineStartIndex(string text, int lineNumber)
    {
        if (lineNumber <= 1) return 0;

        var currentLine = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            currentLine++;
            if (currentLine == lineNumber) return i + 1;
        }

        return text.Length;
    }

    private static int FindMatchingBrace(string text, int openBrace)
    {
        var depth = 0;
        var inLineComment = false;
        var inBlockComment = false;
        var inString = false;
        var inChar = false;
        var verbatimString = false;

        for (int i = openBrace; i < text.Length; i++)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n') inLineComment = false;
                continue;
            }
            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }
            if (inString)
            {
                if (verbatimString && c == '"' && next == '"')
                {
                    i++;
                    continue;
                }
                if (c == '"' && (verbatimString || !IsEscaped(text, i))) inString = false;
                continue;
            }
            if (inChar)
            {
                if (c == '\'' && !IsEscaped(text, i)) inChar = false;
                continue;
            }

            if (c == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }
            if (c == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }
            if (c == '"')
            {
                verbatimString = i > 0 && text[i - 1] == '@';
                inString = true;
                continue;
            }
            if (c == '\'')
            {
                inChar = true;
                continue;
            }
            if (c == '{') depth++;
            if (c == '}' && --depth == 0) return i;
        }

        return -1;
    }

    private static bool IsEscaped(string text, int index)
    {
        var slashCount = 0;
        for (int i = index - 1; i >= 0 && text[i] == '\\'; i--) slashCount++;
        return (slashCount & 1) != 0;
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
    public bool autoInstrumentOnCompile;
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
            if (line.StartsWith("AutoInstrumentOnCompile:", StringComparison.OrdinalIgnoreCase))
            {
                var value = line.Substring("AutoInstrumentOnCompile:".Length).Trim();
                autoInstrumentOnCompile = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                continue;
            }

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
                var normalizedExclude = exclude.Replace('\\', '/');
                hashExclude.Add(normalizedExclude);
                LogTrackCSharp.RemoveLogTrackCode(baseDir, normalizedExclude);
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
