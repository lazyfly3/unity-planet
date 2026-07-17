// Packages/com.kihan.vulcan/Editor/LogTrackTools.cs
// LogTrack Unity agent bridge tools — [AICallable] methods invoked by Vulcan's
// logtrack_unity_agent via vulcan-bridge.exe → BridgeServer → call_method.
//
// Provides:
//   - GetLogTrackLogDir / GetLatestLogTrackLogPath / GetLogTrackBenchmarkJson
//   - GetProjectPdbPaths / ReadPdbSymbol
//   - InsertLogTrack (delegates to FSPDebugerTool via reflection; only when the
//     project has the new LogTrack plugin installed)
//
// All methods return JSON strings (the EXE side wraps them in MCP text content).

#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KDL.Editor.Vulcan
{
    /// <summary>
    /// LogTrack-specific [AICallable] tools exposed to Vulcan's
    /// logtrack_unity_agent through vulcan-bridge.exe.
    ///
    /// All return JSON strings so the Python side (bridge_client._extract_tool_result)
    /// can json.loads them into structured dicts.
    /// </summary>
    public static class LogTrackTools
    {
        // ------------------------------------------------------------------
        // Log directory / file discovery
        // ------------------------------------------------------------------

        [AICallable(
            "Return the LogTrack log directory (Application.persistentDataPath/LogTrack).",
            Category = "Vulcan.LogTrack")]
        public static string GetLogTrackLogDir()
        {
            string dir = Path.Combine(Application.persistentDataPath, "LogTrack");
            Directory.CreateDirectory(dir); // idempotent
            return _Json(new
            {
                ok = true,
                log_dir = dir,
                exists = Directory.Exists(dir),
            });
        }

        [AICallable(
            "Return the absolute path of the newest LogTrack text log under persistentDataPath/LogTrack. " +
                          "Empty path if Unity is not running or no log exists yet.",
            Category = "Vulcan.LogTrack")]
        public static string GetLatestLogTrackLogPath()
        {
            string dir = Path.Combine(Application.persistentDataPath, "LogTrack");
            if (!Directory.Exists(dir))
            {
                return _Json(new { ok = true, path = "", reason = "log_dir_not_found", log_dir = dir });
            }
            var info = new DirectoryInfo(dir)
                .GetFiles("*LogTrack*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (info == null)
            {
                return _Json(new { ok = true, path = "", reason = "no_log_file", log_dir = dir });
            }
            return _Json(new
            {
                ok = true,
                path = info.FullName,
                log_dir = dir,
                size_bytes = info.Length,
                mtime_utc_iso = info.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            });
        }

        [AICallable(
            "Return the N newest LogTrack text logs under persistentDataPath/LogTrack, " +
                          "ordered by LastWriteTimeUtc descending. Args: count=2 (1..20).",
            Category = "Vulcan.LogTrack")]
        public static string ListRecentLogTrackLogPaths(int count = 1)
        {
            count = Math.Max(1, Math.Min(count, 20));
            string dir = Path.Combine(Application.persistentDataPath, "LogTrack");
            if (!Directory.Exists(dir))
            {
                return _Json(new
                {
                    ok = true,
                    log_dir = dir,
                    count = 0,
                    logs = new object[0],
                    reason = "log_dir_not_found",
                });
            }
            var files = new DirectoryInfo(dir)
                .GetFiles("*LogTrack*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(count)
                .ToArray();
            var list = files.Select((f, i) => new
            {
                path = f.FullName,
                rank = i + 1,
                size_bytes = f.Length,
                mtime_utc_iso = f.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                name = f.Name,
            }).ToArray();
            return _Json(new
            {
                ok = true,
                log_dir = dir,
                count = list.Length,
                logs = list,
            });
        }

        [AICallable(
            "Read logtrack_benchmark.json under the LogTrack log directory. " +
                          "Returns insert_ms / peak_memory_bytes / export_file_bytes / parse_ms.",
            Category = "Vulcan.LogTrack")]
        public static string GetLogTrackBenchmarkJson()
        {
            string dir = Path.Combine(Application.persistentDataPath, "LogTrack");
            string path = Path.Combine(dir, "logtrack_benchmark.json");
            if (!File.Exists(path))
            {
                return _Json(new
                {
                    ok = false,
                    error = "benchmark_not_found",
                    path = path,
                    hint = "Run a Play session with LogTrack enabled so the runtime writes logtrack_benchmark.json.",
                });
            }
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                // Validate JSON by round-tripping via Unity's JsonUtility (loose) — but
                // logtrack_benchmark.json is a plain object; just return as-is to Python.
                return _Json(new
                {
                    ok = true,
                    path = path,
                    raw = text,
                });
            }
            catch (Exception e)
            {
                return _Json(new { ok = false, error = $"read_failed: {e.Message}", path = path });
            }
        }

        // ------------------------------------------------------------------
        // PDB enumeration + symbol lookup
        // ------------------------------------------------------------------

        [AICallable(
            "List Library/ScriptAssemblies/*.pdb in the current Unity project, with size and mtime.",
            Category = "Vulcan.LogTrack")]
        public static string GetProjectPdbPaths()
        {
            string projRoot = Path.GetDirectoryName(Application.dataPath); // parent of Assets
            string saDir = Path.Combine(projRoot, "Library", "ScriptAssemblies");
            if (!Directory.Exists(saDir))
            {
                return _Json(new
                {
                    ok = false,
                    error = "script_assemblies_not_found",
                    path = saDir,
                    hint = "Open Unity and let it compile once to populate Library/ScriptAssemblies.",
                });
            }
            var files = new DirectoryInfo(saDir)
                .GetFiles("*.pdb", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f.Name)
                .ToArray();
            var list = files.Select(f => new
            {
                path = f.FullName,
                name = f.Name,
                size_bytes = f.Length,
                mtime_utc_iso = f.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            }).ToArray();
            return _Json(new
            {
                ok = true,
                unity_project_root = projRoot,
                script_assemblies = saDir,
                pdb_count = files.Length,
                pdbs = list,
            });
        }

        [AICallable(
            "Resolve Class::Method to source file:line range via PDB. " +
                          "Uses Mono.Cecil when available (loaded from Library/PackageCache or MONO_CECIL_DLL env). " +
                          "Returns source_file / line_start / line_end / param_types.",
            Category = "Vulcan.LogTrack")]
        public static string ReadPdbSymbol(string pdbPath, string className, string methodName)
        {
            if (string.IsNullOrEmpty(pdbPath) || string.IsNullOrEmpty(className) || string.IsNullOrEmpty(methodName))
            {
                return _Json(new { ok = false, error = "pdbPath / className / methodName must not be empty" });
            }
            if (!File.Exists(pdbPath))
            {
                return _Json(new { ok = false, error = $"pdb not found: {pdbPath}" });
            }

            // Try Mono.Cecil via reflection-loaded assembly.
            try
            {
                var result = _TryReadWithMonoCecil(pdbPath, className, methodName);
                if (result != null)
                {
                    return result;
                }
            }
            catch (Exception e)
            {
                return _Json(new
                {
                    ok = false,
                    pdb_path = pdbPath,
                    class_name = className,
                    method_name = methodName,
                    via = "mono_cecil",
                    error = $"Mono.Cecil read failed: {e.Message}",
                });
            }

            return _Json(new
            {
                ok = false,
                pdb_path = pdbPath,
                class_name = className,
                method_name = methodName,
                via = "not_available",
                error = "Mono.Cecil not found in this Unity project. " +
                        "Set MONO_CECIL_DLL env to a Mono.Cecil.dll path, or install a Unity package that brings Mono.Cecil.",
            });
        }

        // ------------------------------------------------------------------
        // InsertLogTrack — delegate to project's FSPDebugerTool via reflection
        // ------------------------------------------------------------------

        [AICallable(
            "Trigger LogTrack IL instrumentation (LogTrackBatch.InsertLogTrack).",
            Category = "Vulcan.LogTrack",
            Kind = ToolKind.Write)]
        public static string InsertLogTrack()
        {
            Type t = _FindTypeByName("LogTrackBatch");
            if (t == null)
            {
                t = _FindTypeByName("LogTrack.Editor.LogTrackBatch");
            }
            if (t == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType("LogTrack.Editor.LogTrackBatch", false);
                    if (t != null) break;
                }
            }
            if (t == null)
            {
                return _Json(new
                {
                    ok = false,
                    error = "LogTrackBatch type not found in loaded assemblies",
                    hint = "Install the LogTrack Unity plugin and let Unity compile LogTrack.Editor first.",
                });
            }
            MethodInfo mi = t.GetMethod("InsertLogTrack", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (mi == null)
            {
                return _Json(new
                {
                    ok = false,
                    error = "LogTrackBatch.InsertLogTrack method not found",
                    hint = "Check the LogTrack plugin version; LogTrack.Editor exposes static InsertLogTrack.",
                });
            }
            try
            {
                object ret = mi.Invoke(null, null);
                return _Json(new
                {
                    ok = true,
                    return_value = ret == null ? "" : ret.ToString(),
                });
            }
            catch (Exception e)
            {
                return _Json(new { ok = false, error = $"InsertLogTrack invoke failed: {e.Message}" });
            }
        }

        // ------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------

        private static string _Json(object obj)
        {
            // Use Unity's JsonUtility for anonymous-ish payloads via reflection; fallback to manual.
            // JsonUtility cannot serialize Dictionary or anonymous types well, so we do a tiny
            // manual serializer for the few shapes we return here.
            return _SimpleJson(obj);
        }

        private static string _SimpleJson(object obj)
        {
            var sb = new StringBuilder();
            _WriteValue(sb, obj);
            return sb.ToString();
        }

        private static void _WriteValue(StringBuilder sb, object obj)
        {
            if (obj == null)
            {
                sb.Append("null");
                return;
            }
            Type t = obj.GetType();
            if (t == typeof(string) || t == typeof(char))
            {
                _WriteString(sb, Convert.ToString(obj));
                return;
            }
            if (t == typeof(bool))
            {
                sb.Append((bool)obj ? "true" : "false");
                return;
            }
            if (t.IsPrimitive || t == typeof(decimal))
            {
                sb.Append(Convert.ToString(obj, System.Globalization.CultureInfo.InvariantCulture));
                return;
            }
            if (t == typeof(DateTime))
            {
                _WriteString(sb, ((DateTime)obj).ToString("o"));
                return;
            }
            if (typeof(IEnumerable).IsAssignableFrom(t) && !typeof(IDictionary).IsAssignableFrom(t))
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in (IEnumerable)obj)
                {
                    if (!first) sb.Append(',');
                    _WriteValue(sb, item);
                    first = false;
                }
                sb.Append(']');
                return;
            }
            if (typeof(IDictionary).IsAssignableFrom(t))
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry entry in (IDictionary)obj)
                {
                    if (!first) sb.Append(',');
                    _WriteString(sb, Convert.ToString(entry.Key));
                    sb.Append(':');
                    _WriteValue(sb, entry.Value);
                    first = false;
                }
                sb.Append('}');
                return;
            }
            // Fall back: reflect public fields + properties
            sb.Append('{');
            bool firstProp = true;
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!firstProp) sb.Append(',');
                _WriteString(sb, f.Name);
                sb.Append(':');
                _WriteValue(sb, f.GetValue(obj));
                firstProp = false;
            }
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                if (!firstProp) sb.Append(',');
                _WriteString(sb, p.Name);
                sb.Append(':');
                try { _WriteValue(sb, p.GetValue(obj, null)); }
                catch (Exception) { sb.Append("null"); }
                firstProp = false;
            }
            sb.Append('}');
        }

        private static void _WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            if (s != null)
            {
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append('"');
        }

        private static Type _FindTypeByName(string typeName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(typeName, false);
                if (t != null) return t;
            }
            // Loose match by short name
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name == typeName) return t;
                    }
                }
                catch (Exception) { /* ignore load errors */ }
            }
            return null;
        }

        private static string _TryReadWithMonoCecil(string pdbPath, string className, string methodName)
        {
            // Try to locate Mono.Cecil.dll anywhere under Library/PackageCache or from env.
            string envDll = Environment.GetEnvironmentVariable("MONO_CECIL_DLL");
            string dllPath = null;
            if (!string.IsNullOrEmpty(envDll) && File.Exists(envDll))
            {
                dllPath = envDll;
            }
            else
            {
                string projRoot = Path.GetDirectoryName(Application.dataPath);
                string pkgCache = Path.Combine(projRoot, "Library", "PackageCache");
                if (Directory.Exists(pkgCache))
                {
                    var hit = Directory.EnumerateFiles(pkgCache, "Mono.Cecil.dll", SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (!string.IsNullOrEmpty(hit) && File.Exists(hit))
                    {
                        dllPath = hit;
                    }
                }
            }
            if (dllPath == null)
            {
                return null; // signal "not available" to caller
            }

            // Reflection-load Mono.Cecil. We avoid a hard compile-time dependency so this
            // source file compiles in any Unity project regardless of whether Cecil is present.
            var asm = Assembly.LoadFrom(dllPath);
            Type readerParamsType = asm.GetType("Mono.Cecil.ReaderParameters", true);
            Type defaultResolverType = asm.GetType("Mono.Cecil.DefaultAssemblyResolver", true);
            Type asmDefType = asm.GetType("Mono.Cecil.AssemblyDefinition", true);
            Type pdbReaderProviderType = asm.GetType("Mono.Cecil.Pdb.PdbReaderProvider", true)
                ?? asm.GetType("Mono.Cecil.Mdb.MdbReaderProvider", true);

            object resolver = Activator.CreateInstance(defaultResolverType);
            object rp = Activator.CreateInstance(readerParamsType);
            // rp.AssemblyResolver = resolver
            readerParamsType.GetProperty("AssemblyResolver")?.SetValue(rp, resolver);
            if (pdbReaderProviderType != null)
            {
                object provider = Activator.CreateInstance(pdbReaderProviderType);
                // rp.ReadSymbols = provider
                readerParamsType.GetProperty("ReadSymbols")?.SetValue(rp, true);
                readerParamsType.GetProperty("SymbolReaderProvider")?.SetValue(rp, provider);
            }
            // Look up the matching .dll next to the .pdb (Unity writes them together).
            string dllSibling = Path.ChangeExtension(pdbPath, ".dll");
            if (!File.Exists(dllSibling))
            {
                return _Json(new
                {
                    ok = false,
                    pdb_path = pdbPath,
                    class_name = className,
                    method_name = methodName,
                    via = "mono_cecil",
                    error = $"sibling dll not found: {dllSibling}",
                });
            }
            // AssemblyDefinition.ReadAssembly(path, rp)
            MethodInfo read = asmDefType.GetMethod("ReadAssembly", new[] { typeof(string), readerParamsType });
            object asmDef = read.Invoke(null, new object[] { dllSibling, rp });
            try
            {
                // asmDef.Modules → IEnumerable<ModuleDefinition>
                object modules = asmDefType.GetProperty("Modules").GetValue(asmDef);
                string sourceFile = null;
                int lineStart = 0, lineEnd = 0;
                var paramTypes = new List<string>();
                bool found = false;
                foreach (object mod in (System.Collections.IEnumerable)modules)
                {
                    Type modType = mod.GetType();
                    object types = modType.GetProperty("Types").GetValue(mod);
                    foreach (object tt in (System.Collections.IEnumerable)types)
                    {
                        Type typeDefType = tt.GetType();
                        string tName = (string)typeDefType.GetProperty("Name").GetValue(tt);
                        string tFull = (string)typeDefType.GetProperty("FullName").GetValue(tt);
                        bool nameMatch = tName == className || tFull == className
                            || tFull.EndsWith("." + className);
                        if (!nameMatch) continue;
                        object methods = typeDefType.GetProperty("Methods").GetValue(tt);
                        foreach (object m in (System.Collections.IEnumerable)methods)
                        {
                            Type methType = m.GetType();
                            string mName = (string)methType.GetProperty("Name").GetValue(m);
                            if (mName != methodName) continue;
                            // parameters
                            var pts = new List<string>();
                            object ps = methType.GetProperty("Parameters").GetValue(m);
                            foreach (object p in (System.Collections.IEnumerable)ps)
                            {
                                Type pType = p.GetType();
                                object pParamType = pType.GetProperty("ParameterType").GetValue(p);
                                pts.Add(pParamType == null ? "?" : pParamType.ToString());
                            }
                            paramTypes = pts;
                            // method.Body.Instructions → sequence points live in Body.Instructions[i].SequencePoint
                            object body = methType.GetProperty("Body").GetValue(m);
                            if (body != null)
                            {
                                object instrs = body.GetType().GetProperty("Instructions").GetValue(body);
                                int firstLine = 0, lastLine = 0;
                                string firstDoc = null;
                                foreach (object ins in (System.Collections.IEnumerable)instrs)
                                {
                                    object sp = ins.GetType().GetProperty("SequencePoint").GetValue(ins);
                                    if (sp == null) continue;
                                    int line = (int)sp.GetType().GetProperty("StartLine").GetValue(sp);
                                    object docObj = sp.GetType().GetProperty("Document").GetValue(sp);
                                    string doc = null;
                                    if (docObj != null)
                                    {
                                        object url = docObj.GetType().GetProperty("Url").GetValue(docObj);
                                        doc = url as string;
                                        if (doc == null && url != null) doc = url.ToString();
                                    }
                                    if (firstLine == 0) { firstLine = line; firstDoc = doc; }
                                    lastLine = Math.Max(lastLine, line);
                                }
                                if (firstLine > 0)
                                {
                                    lineStart = firstLine;
                                    lineEnd = lastLine;
                                    sourceFile = firstDoc;
                                    found = true;
                                    break;
                                }
                            }
                        }
                        if (found) break;
                    }
                    if (found) break;
                }
                if (!found)
                {
                    return _Json(new
                    {
                        ok = false,
                        pdb_path = pdbPath,
                        class_name = className,
                        method_name = methodName,
                        via = "mono_cecil",
                        error = "class/method not found in this assembly",
                    });
                }
                return _Json(new
                {
                    ok = true,
                    pdb_path = pdbPath,
                    class_name = className,
                    method_name = methodName,
                    source_file = sourceFile,
                    line_start = lineStart,
                    line_end = lineEnd,
                    param_types = paramTypes.ToArray(),
                    via = "mono_cecil",
                });
            }
            finally
            {
                // asmDef.Dispose()
                asmDefType.GetMethod("Dispose")?.Invoke(asmDef, null);
            }
        }
    }
}
#endif
