// ============================================================================
// EditorPerformanceOps.cs - Vulcan Editor performance and memory diagnostics
// ----------------------------------------------------------------------------
// Namespace: KDL.Editor.Vulcan
//
// This toolset is intentionally split into read-only diagnostics and
// execute=false-by-default actions. Agents must ask the user before running any
// execute=true call because these actions can close windows or change Editor UI
// state, even when they do not write assets to disk.
// ============================================================================

#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using KDL.Editor;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace KDL.Editor.Vulcan
{
    /// <summary>
    /// Unity Editor 卡顿 / 内存过高诊断工具。通过 Unity MCP call_method 反射调用。
    /// </summary>
    public static class EditorPerformanceOps
    {
        private const string CategoryName = "Vulcan.Editor.Performance";

        private static readonly string[] ProtectedWindowTypeNames =
        {
            "SceneView",
            "GameView",
            "InspectorWindow",
            "ProjectBrowser",
            "SceneHierarchyWindow",
            "ConsoleWindow",
            "PackageManagerWindow",
        };

        private sealed class ProfilerCaptureState
        {
            public string CaptureId;
            public DateTime StartedAt;
            public string RawPath;
            public bool SaveRaw;
            public bool DeepProfile;
            public bool PreviousProfilerEnabled;
            public bool PreviousBinaryLogEnabled;
            public string PreviousLogFile;
            public bool HadDeepProfileValue;
            public bool PreviousDeepProfile;
        }

        private static ProfilerCaptureState _activeProfilerCapture;

        [AICallable("读取 Unity Editor 卡顿/内存诊断快照：进程内存、Profiler 内存、编译/导入/Play 状态、打开窗口和当前场景对象统计。args: includeWindows=true&includeSceneStats=true&top=20",
            Category = CategoryName)]
        public static string GetLagSnapshot(
            string includeWindows = "true",
            string includeSceneStats = "true",
            string top = "20")
        {
            try
            {
                int maxRows = IntArg(top, 20, 1, 200);
                var sb = new StringBuilder(4096);
                sb.AppendLine("kind=unity_editor_performance_snapshot");
                sb.Append("timestamp=").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).AppendLine();
                sb.Append("unityVersion=").Append(Application.unityVersion).AppendLine();
                sb.Append("projectPath=").Append(Clean(Application.dataPath)).AppendLine();
                sb.Append("isPlaying=").Append(EditorApplication.isPlaying).AppendLine();
                sb.Append("isPaused=").Append(EditorApplication.isPaused).AppendLine();
                sb.Append("isCompiling=").Append(EditorApplication.isCompiling).AppendLine();
                sb.Append("isUpdating=").Append(EditorApplication.isUpdating).AppendLine();

                var activeScene = SceneManager.GetActiveScene();
                sb.Append("activeScene=").Append(Clean(activeScene.path)).AppendLine();
                sb.Append("sceneCount=").Append(SceneManager.sceneCount).AppendLine();
                AppendMemorySnapshot(sb, "memory.");

                if (BoolArg(includeSceneStats, true))
                {
                    AppendSceneStats(sb);
                }

                if (BoolArg(includeWindows, true))
                {
                    sb.AppendLine();
                    sb.AppendLine("openWindows:");
                    AppendOpenWindows(sb, maxRows);
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return "ERROR: GetLagSnapshot failed: " + ex;
            }
        }

        [AICallable("启动 Unity Editor Profiler 采样。底层由 Vulcan Agent 负责等待并调用 StopProfilerCapture；不要直接暴露给 LLM 连续调用。args: captureId=&deepProfile=false&saveRaw=true&outputDir=&includeSnapshot=true",
            Category = CategoryName)]
        public static string StartProfilerCapture(
            string captureId = "",
            string deepProfile = "false",
            string saveRaw = "true",
            string outputDir = "",
            string includeSnapshot = "true")
        {
            try
            {
                if (_activeProfilerCapture != null)
                {
                    return "ERROR: profiler capture already active: " + _activeProfilerCapture.CaptureId;
                }

                bool shouldDeepProfile = BoolArg(deepProfile, false);
                bool shouldSaveRaw = BoolArg(saveRaw, true);
                bool shouldIncludeSnapshot = BoolArg(includeSnapshot, true);
                string id = SafeFilePart(string.IsNullOrWhiteSpace(captureId) ? Guid.NewGuid().ToString("N").Substring(0, 8) : captureId);
                string rawPath = "";
                if (shouldSaveRaw)
                {
                    string dir = ResolveProfilerCaptureOutputDir(outputDir);
                    Directory.CreateDirectory(dir);
                    rawPath = Path.Combine(dir, "unity_editor_profiler_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + id + ".data");
                }

                bool hadDeepProfile;
                bool previousDeepProfile;
                TryGetProfilerDeepProfile(out previousDeepProfile, out hadDeepProfile);

                var state = new ProfilerCaptureState
                {
                    CaptureId = id,
                    StartedAt = DateTime.Now,
                    RawPath = rawPath,
                    SaveRaw = shouldSaveRaw,
                    DeepProfile = shouldDeepProfile,
                    PreviousProfilerEnabled = Profiler.enabled,
                    PreviousBinaryLogEnabled = Profiler.enableBinaryLog,
                    PreviousLogFile = Profiler.logFile,
                    HadDeepProfileValue = hadDeepProfile,
                    PreviousDeepProfile = previousDeepProfile,
                };

                if (shouldDeepProfile)
                {
                    TrySetProfilerDeepProfile(true);
                }
                if (shouldSaveRaw)
                {
                    Profiler.logFile = rawPath;
                    Profiler.enableBinaryLog = true;
                }
                Profiler.enabled = true;
                _activeProfilerCapture = state;

                var sb = new StringBuilder(4096);
                sb.AppendLine("kind=unity_editor_profiler_capture_start");
                sb.Append("captureId=").Append(id).AppendLine();
                sb.Append("startedAt=").Append(state.StartedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).AppendLine();
                sb.Append("profilerEnabled=true").AppendLine();
                sb.Append("deepProfileRequested=").Append(shouldDeepProfile).AppendLine();
                sb.Append("deepProfileSupported=").Append(hadDeepProfile).AppendLine();
                sb.Append("saveRaw=").Append(shouldSaveRaw).AppendLine();
                if (!string.IsNullOrEmpty(rawPath))
                    sb.Append("rawPath=").Append(Clean(rawPath)).AppendLine();
                sb.Append("previousProfilerEnabled=").Append(state.PreviousProfilerEnabled).AppendLine();
                sb.Append("previousBinaryLogEnabled=").Append(state.PreviousBinaryLogEnabled).AppendLine();
                if (shouldIncludeSnapshot)
                {
                    sb.AppendLine();
                    sb.AppendLine("snapshotBefore:");
                    AppendMemorySnapshot(sb, "memory.");
                    using (var process = Process.GetCurrentProcess())
                    {
                        sb.Append("processThreadCount=").Append(process.Threads.Count).AppendLine();
                    }
                    sb.Append("isCompiling=").Append(EditorApplication.isCompiling).AppendLine();
                    sb.Append("isUpdating=").Append(EditorApplication.isUpdating).AppendLine();
                }
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                _activeProfilerCapture = null;
                return "ERROR: StartProfilerCapture failed: " + ex;
            }
        }

        [AICallable("停止 Unity Editor Profiler 采样并恢复进入采样前的 Profiler 状态。args: captureId=&includeSnapshot=true",
            Category = CategoryName)]
        public static string StopProfilerCapture(string captureId = "", string includeSnapshot = "true")
        {
            try
            {
                var state = _activeProfilerCapture;
                if (state == null)
                {
                    return "ERROR: no active profiler capture.";
                }

                string requestedId = SafeFilePart(captureId);
                if (!string.IsNullOrEmpty(requestedId) && !string.Equals(requestedId, state.CaptureId, StringComparison.OrdinalIgnoreCase))
                {
                    return "ERROR: active profiler capture id mismatch. active=" + state.CaptureId + " requested=" + requestedId;
                }

                DateTime stoppedAt = DateTime.Now;
                Profiler.enabled = state.PreviousProfilerEnabled;
                Profiler.enableBinaryLog = state.PreviousBinaryLogEnabled;
                Profiler.logFile = state.PreviousLogFile ?? "";
                if (state.HadDeepProfileValue)
                {
                    TrySetProfilerDeepProfile(state.PreviousDeepProfile);
                }
                _activeProfilerCapture = null;

                long rawBytes = 0;
                bool rawExists = false;
                if (!string.IsNullOrEmpty(state.RawPath))
                {
                    try
                    {
                        if (File.Exists(state.RawPath))
                        {
                            rawExists = true;
                            rawBytes = new FileInfo(state.RawPath).Length;
                        }
                    }
                    catch
                    {
                        rawExists = false;
                    }
                }

                var sb = new StringBuilder(4096);
                sb.AppendLine("kind=unity_editor_profiler_capture_stop");
                sb.Append("captureId=").Append(state.CaptureId).AppendLine();
                sb.Append("startedAt=").Append(state.StartedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).AppendLine();
                sb.Append("stoppedAt=").Append(stoppedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).AppendLine();
                sb.Append("elapsedSeconds=").Append((stoppedAt - state.StartedAt).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)).AppendLine();
                sb.Append("deepProfile=").Append(state.DeepProfile).AppendLine();
                sb.Append("saveRaw=").Append(state.SaveRaw).AppendLine();
                if (!string.IsNullOrEmpty(state.RawPath))
                {
                    sb.Append("rawPath=").Append(Clean(state.RawPath)).AppendLine();
                    sb.Append("rawExists=").Append(rawExists).AppendLine();
                    sb.Append("rawBytes=").Append(rawBytes).AppendLine();
                }
                sb.Append("restoredProfilerEnabled=").Append(state.PreviousProfilerEnabled).AppendLine();
                sb.Append("restoredBinaryLogEnabled=").Append(state.PreviousBinaryLogEnabled).AppendLine();
                if (BoolArg(includeSnapshot, true))
                {
                    sb.AppendLine();
                    sb.AppendLine("snapshotAfter:");
                    AppendMemorySnapshot(sb, "memory.");
                    using (var process = Process.GetCurrentProcess())
                    {
                        sb.Append("processThreadCount=").Append(process.Threads.Count).AppendLine();
                    }
                    sb.Append("isCompiling=").Append(EditorApplication.isCompiling).AppendLine();
                    sb.Append("isUpdating=").Append(EditorApplication.isUpdating).AppendLine();
                }
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                _activeProfilerCapture = null;
                return "ERROR: StopProfilerCapture failed: " + ex;
            }
        }

        [AICallable("列出 Unity 当前加载对象中运行时内存占用最高的对象，用于定位 Texture/Mesh/Material/AudioClip/RenderTexture 等内存热点。args: typeFilter=&limit=50",
            Category = CategoryName)]
        public static string ListTopLoadedObjects(string typeFilter = "", string limit = "50")
        {
            try
            {
                int maxRows = IntArg(limit, 50, 1, 500);
                string[] filters = SplitFilters(typeFilter);
                var rows = new List<LoadedObjectRow>(1024);

                foreach (var obj in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
                {
                    if (obj == null) continue;
                    string typeName = obj.GetType().Name;
                    if (filters.Length > 0 && !filters.Any(f => ContainsIgnoreCase(typeName, f)))
                        continue;

                    long bytes = SafeRuntimeMemorySize(obj);
                    if (bytes <= 0) continue;

                    string path = "";
                    bool persistent = false;
                    try
                    {
                        persistent = EditorUtility.IsPersistent(obj);
                        path = AssetDatabase.GetAssetPath(obj);
                    }
                    catch
                    {
                        // Some editor-only objects do not have an AssetDatabase path.
                    }

                    rows.Add(new LoadedObjectRow
                    {
                        Bytes = bytes,
                        TypeName = typeName,
                        Name = Clean(obj.name),
                        Persistent = persistent,
                        Path = Clean(path),
                    });
                }

                var ordered = rows.OrderByDescending(r => r.Bytes).Take(maxRows).ToArray();
                var sb = new StringBuilder(8192);
                sb.AppendLine("rank\tsizeMB\tbytes\ttype\tpersistent\tname\tpath");
                for (int i = 0; i < ordered.Length; i++)
                {
                    var r = ordered[i];
                    sb.Append(i + 1).Append('\t')
                        .Append(MbNumber(r.Bytes)).Append('\t')
                        .Append(r.Bytes).Append('\t')
                        .Append(r.TypeName).Append('\t')
                        .Append(r.Persistent).Append('\t')
                        .Append(r.Name).Append('\t')
                        .Append(r.Path).AppendLine();
                }

                if (ordered.Length == 0)
                    sb.AppendLine("(empty)");
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return "ERROR: ListTopLoadedObjects failed: " + ex;
            }
        }

        [AICallable("扫描贴图导入设置中可能导致 Editor/运行时内存过高的候选项：Readable、超大尺寸、未压缩、mipmap、crunch 等。只读，不修改 meta。args: folder=Assets&limit=50",
            Category = CategoryName)]
        public static string AnalyzeTextureImportCandidates(string folder = "Assets", string limit = "50")
        {
            try
            {
                string searchFolder = string.IsNullOrWhiteSpace(folder) ? "Assets" : folder.Trim();
                if (!AssetDatabase.IsValidFolder(searchFolder))
                    return "ERROR: folder is not a valid Unity folder: " + searchFolder;

                int maxRows = IntArg(limit, 50, 1, 500);
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                var rows = new List<TextureCandidateRow>(1024);

                foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { searchFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;

                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null) continue;

                    int width;
                    int height;
                    TryGetTextureSize(importer, path, out width, out height);

                    long fileBytes = 0;
                    try
                    {
                        string fullPath = Path.Combine(projectRoot, path.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(fullPath)) fileBytes = new FileInfo(fullPath).Length;
                    }
                    catch
                    {
                        // File size is only used as a hint.
                    }

                    int score = 0;
                    var reasons = new List<string>();
                    if (importer.isReadable)
                    {
                        score += 100;
                        reasons.Add("Read/Write Enabled");
                    }
                    if (importer.maxTextureSize >= 4096 || width >= 4096 || height >= 4096)
                    {
                        score += 90;
                        reasons.Add(">=4096");
                    }
                    else if (importer.maxTextureSize >= 2048 || width >= 2048 || height >= 2048)
                    {
                        score += 45;
                        reasons.Add(">=2048");
                    }
                    if (importer.textureCompression == TextureImporterCompression.Uncompressed)
                    {
                        score += 80;
                        reasons.Add("Uncompressed");
                    }
                    if (importer.mipmapEnabled)
                    {
                        score += 20;
                        reasons.Add("Mipmap");
                    }
                    if (fileBytes >= 8L * 1024L * 1024L)
                    {
                        score += 20;
                        reasons.Add("large file");
                    }

                    if (score <= 0) continue;
                    rows.Add(new TextureCandidateRow
                    {
                        Score = score,
                        Path = Clean(path),
                        Width = width,
                        Height = height,
                        FileBytes = fileBytes,
                        MaxTextureSize = importer.maxTextureSize,
                        IsReadable = importer.isReadable,
                        MipmapEnabled = importer.mipmapEnabled,
                        Compression = importer.textureCompression.ToString(),
                        Crunched = importer.crunchedCompression,
                        CompressionQuality = importer.compressionQuality,
                        Reasons = string.Join(",", reasons),
                    });
                }

                var ordered = rows
                    .OrderByDescending(r => r.Score)
                    .ThenByDescending(r => Math.Max(r.Width, r.Height))
                    .ThenByDescending(r => r.FileBytes)
                    .Take(maxRows)
                    .ToArray();

                var sb = new StringBuilder(8192);
                sb.AppendLine("rank\tscore\twidth\theight\tfileMB\tmaxSize\treadable\tmipmap\tcompression\tcrunched\tquality\treasons\tpath");
                for (int i = 0; i < ordered.Length; i++)
                {
                    var r = ordered[i];
                    sb.Append(i + 1).Append('\t')
                        .Append(r.Score).Append('\t')
                        .Append(r.Width).Append('\t')
                        .Append(r.Height).Append('\t')
                        .Append(MbNumber(r.FileBytes)).Append('\t')
                        .Append(r.MaxTextureSize).Append('\t')
                        .Append(r.IsReadable).Append('\t')
                        .Append(r.MipmapEnabled).Append('\t')
                        .Append(r.Compression).Append('\t')
                        .Append(r.Crunched).Append('\t')
                        .Append(r.CompressionQuality).Append('\t')
                        .Append(r.Reasons).Append('\t')
                        .Append(r.Path).AppendLine();
                }

                if (ordered.Length == 0)
                    sb.AppendLine("(empty: no obvious texture import candidates)");
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return "ERROR: AnalyzeTextureImportCandidates failed: " + ex;
            }
        }

        [AICallable("列出当前打开的 Unity EditorWindow：类型、标题、是否 focused。用于判断哪些窗口可能在消耗性能，尤其 Profiler/FrameDebugger/自定义监控窗口。args: limit=80",
            Category = CategoryName)]
        public static string ListOpenEditorWindows(string limit = "80")
        {
            try
            {
                var sb = new StringBuilder(4096);
                AppendOpenWindows(sb, IntArg(limit, 80, 1, 300));
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return "ERROR: ListOpenEditorWindows failed: " + ex;
            }
        }

        [AICallable("读取 Unity Editor CPU/线程诊断快照：进程线程数、ThreadPool、Job Worker Count、Top 线程累计 CPU。只读诊断。args: includeThreads=true&top=20",
            Category = CategoryName)]
        public static string GetCpuThreadSnapshot(string includeThreads = "true", string top = "20")
        {
            try
            {
                int maxRows = IntArg(top, 20, 1, 200);
                var sb = new StringBuilder(8192);
                sb.AppendLine("kind=unity_editor_cpu_thread_snapshot");
                sb.Append("timestamp=").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).AppendLine();
                sb.Append("processorCount=").Append(Environment.ProcessorCount).AppendLine();
                sb.Append("isCompiling=").Append(EditorApplication.isCompiling).AppendLine();
                sb.Append("isUpdating=").Append(EditorApplication.isUpdating).AppendLine();
                sb.Append("isPlaying=").Append(EditorApplication.isPlaying).AppendLine();

                int minWorker;
                int minIocp;
                int maxWorker;
                int maxIocp;
                int availableWorker;
                int availableIocp;
                ThreadPool.GetMinThreads(out minWorker, out minIocp);
                ThreadPool.GetMaxThreads(out maxWorker, out maxIocp);
                ThreadPool.GetAvailableThreads(out availableWorker, out availableIocp);
                sb.Append("threadPool.minWorker=").Append(minWorker).AppendLine();
                sb.Append("threadPool.maxWorker=").Append(maxWorker).AppendLine();
                sb.Append("threadPool.availableWorker=").Append(availableWorker).AppendLine();
                sb.Append("threadPool.minIocp=").Append(minIocp).AppendLine();
                sb.Append("threadPool.maxIocp=").Append(maxIocp).AppendLine();
                sb.Append("threadPool.availableIocp=").Append(availableIocp).AppendLine();

                AppendJobWorkerSnapshot(sb, "jobs.");

                using (var process = Process.GetCurrentProcess())
                {
                    sb.Append("processId=").Append(process.Id).AppendLine();
                    sb.Append("processThreadCount=").Append(process.Threads.Count).AppendLine();
                    sb.Append("processTotalCpuSec=").Append(process.TotalProcessorTime.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)).AppendLine();

                    if (BoolArg(includeThreads, true))
                    {
                        var rows = new List<ProcessThreadRow>();
                        foreach (ProcessThread thread in process.Threads)
                        {
                            try
                            {
                                rows.Add(new ProcessThreadRow
                                {
                                    Id = thread.Id,
                                    State = thread.ThreadState.ToString(),
                                    WaitReason = SafeWaitReason(thread),
                                    Priority = thread.PriorityLevel.ToString(),
                                    TotalCpuMs = thread.TotalProcessorTime.TotalMilliseconds,
                                    UserCpuMs = thread.UserProcessorTime.TotalMilliseconds,
                                    PrivilegedCpuMs = thread.PrivilegedProcessorTime.TotalMilliseconds,
                                });
                            }
                            catch
                            {
                                // Some OS thread records may disappear while enumerating.
                            }
                        }

                        sb.AppendLine();
                        sb.AppendLine("threads:");
                        sb.AppendLine("rank\tthreadId\tstate\twaitReason\tpriority\ttotalCpuMs\tuserCpuMs\tprivilegedCpuMs");
                        var ordered = rows.OrderByDescending(r => r.TotalCpuMs).Take(maxRows).ToArray();
                        for (int i = 0; i < ordered.Length; i++)
                        {
                            var r = ordered[i];
                            sb.Append(i + 1).Append('\t')
                                .Append(r.Id).Append('\t')
                                .Append(r.State).Append('\t')
                                .Append(r.WaitReason).Append('\t')
                                .Append(r.Priority).Append('\t')
                                .Append(r.TotalCpuMs.ToString("0", CultureInfo.InvariantCulture)).Append('\t')
                                .Append(r.UserCpuMs.ToString("0", CultureInfo.InvariantCulture)).Append('\t')
                                .Append(r.PrivilegedCpuMs.ToString("0", CultureInfo.InvariantCulture)).AppendLine();
                        }
                    }
                }

                sb.AppendLine();
                sb.AppendLine("note=Thread CPU values are cumulative since process/thread start, not an instantaneous CPU sample.");
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return "ERROR: GetCpuThreadSnapshot failed: " + ex;
            }
        }

        [AICallable("读取 Unity 编译/导入诊断快照：isCompiling/isUpdating、AssetDatabase worker 状态、编译 assembly 数量和最大 assembly 源文件数。只读诊断。args: includeAssemblies=true&top=30",
            Category = CategoryName)]
        public static string GetCompilationSnapshot(string includeAssemblies = "true", string top = "30")
        {
            try
            {
                int maxRows = IntArg(top, 30, 1, 200);
                var sb = new StringBuilder(8192);
                sb.AppendLine("kind=unity_editor_compilation_snapshot");
                sb.Append("timestamp=").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).AppendLine();
                sb.Append("isCompiling=").Append(EditorApplication.isCompiling).AppendLine();
                sb.Append("isUpdating=").Append(EditorApplication.isUpdating).AppendLine();
                sb.Append("isAssetImportWorkerProcess=").Append(GetAssetDatabaseBool("IsAssetImportWorkerProcess")).AppendLine();
                sb.Append("isAssetImportWorkerProcessV2=").Append(GetAssetDatabaseBool("IsAssetImportWorkerProcessV2")).AppendLine();
                sb.Append("activeBuildTarget=").Append(EditorUserBuildSettings.activeBuildTarget).AppendLine();
                sb.Append("selectedBuildTargetGroup=").Append(EditorUserBuildSettings.selectedBuildTargetGroup).AppendLine();

                if (!BoolArg(includeAssemblies, true))
                    return sb.ToString().TrimEnd();

                var assemblies = CompilationPipeline.GetAssemblies() ?? new UnityEditor.Compilation.Assembly[0];
                sb.Append("assemblyCount=").Append(assemblies.Length).AppendLine();
                sb.Append("asmdefCount=").Append(AssetDatabase.FindAssets("t:AssemblyDefinitionAsset").Length).AppendLine();
                sb.Append("csFileCount=").Append(AssetDatabase.FindAssets("t:MonoScript").Length).AppendLine();

                sb.AppendLine();
                sb.AppendLine("largestAssemblies:");
                sb.AppendLine("rank\tname\tsourceFiles\tdefines\toutputPath");
                var ordered = assemblies
                    .OrderByDescending(a => a.sourceFiles != null ? a.sourceFiles.Length : 0)
                    .ThenBy(a => a.name, StringComparer.OrdinalIgnoreCase)
                    .Take(maxRows)
                    .ToArray();
                for (int i = 0; i < ordered.Length; i++)
                {
                    var assembly = ordered[i];
                    sb.Append(i + 1).Append('\t')
                        .Append(Clean(assembly.name)).Append('\t')
                        .Append(assembly.sourceFiles != null ? assembly.sourceFiles.Length : 0).Append('\t')
                        .Append(assembly.defines != null ? assembly.defines.Length : 0).Append('\t')
                        .Append(Clean(assembly.outputPath)).AppendLine();
                }

                sb.AppendLine();
                sb.AppendLine("adviceHints=Large Assembly-CSharp or very large asmdef sourceFiles counts usually slow incremental compile. Consider splitting stable code into asmdefs, reducing asmdef dependencies, avoiding AssetDatabase.Refresh loops, and checking code generators/importers that trigger repeated refresh.");
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return "ERROR: GetCompilationSnapshot failed: " + ex;
            }
        }

        [AICallable("⚠ destructive (transient) 预览或执行安全清理：UnloadUnusedAssets、GC.Collect、ClearProgressBar。默认 execute=false 只返回影响说明；execute=true 前必须 AskUser。args: execute=false&unloadUnusedAssets=true&collectGC=true&clearProgressBars=true",
            Category = CategoryName)]
        public static string RunSafeCleanup(
            string execute = "false",
            string unloadUnusedAssets = "true",
            string collectGC = "true",
            string clearProgressBars = "true")
        {
            try
            {
                bool shouldExecute = BoolArg(execute, false);
                bool doUnload = BoolArg(unloadUnusedAssets, true);
                bool doGc = BoolArg(collectGC, true);
                bool doProgress = BoolArg(clearProgressBars, true);

                var sb = new StringBuilder(4096);
                sb.AppendLine("kind=unity_editor_safe_cleanup");
                sb.Append("execute=").Append(shouldExecute).AppendLine();
                sb.AppendLine("impact=May briefly freeze the Editor; unused previews/assets can be unloaded; next asset preview/load may be slower. Does not write project assets.");
                AppendMemorySnapshot(sb, "before.");

                if (!shouldExecute)
                {
                    sb.AppendLine("dryRun=true");
                    sb.Append("wouldUnloadUnusedAssets=").Append(doUnload).AppendLine();
                    sb.Append("wouldCollectGC=").Append(doGc).AppendLine();
                    sb.Append("wouldClearProgressBars=").Append(doProgress).AppendLine();
                    return sb.ToString().TrimEnd();
                }

                if (doProgress)
                    EditorUtility.ClearProgressBar();

                if (doUnload)
                    EditorUtility.UnloadUnusedAssetsImmediate(true);

                if (doGc)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }

                AppendMemorySnapshot(sb, "after.");
                sb.AppendLine("status=OK");
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return "ERROR: RunSafeCleanup failed: " + ex;
            }
        }

        [AICallable("⚠ destructive (session) 预览或调整当前 Unity Editor 会话的 C# Job Worker Count，用于降低 CPU 争用/线程过多。默认 execute=false；execute=true 前必须 AskUser。args: workerCount=4&reset=false&execute=false",
            Category = CategoryName)]
        public static string SetJobWorkerCount(
            string workerCount = "",
            string reset = "false",
            string execute = "false")
        {
            try
            {
                bool shouldExecute = BoolArg(execute, false);
                bool shouldReset = BoolArg(reset, false);
                var jobsType = GetJobsUtilityType();
                if (jobsType == null)
                    return "ERROR: Unity JobsUtility type is unavailable in this Unity version.";

                var countProp = jobsType.GetProperty("JobWorkerCount", BindingFlags.Public | BindingFlags.Static);
                var maxProp = jobsType.GetProperty("JobWorkerMaximumCount", BindingFlags.Public | BindingFlags.Static);
                var resetMethod = jobsType.GetMethod("ResetJobWorkerCount", BindingFlags.Public | BindingFlags.Static);

                if (countProp == null || !countProp.CanRead)
                    return "ERROR: JobsUtility.JobWorkerCount is unavailable.";

                int current = Convert.ToInt32(countProp.GetValue(null, null), CultureInfo.InvariantCulture);
                int maximum = maxProp != null && maxProp.CanRead
                    ? Convert.ToInt32(maxProp.GetValue(null, null), CultureInfo.InvariantCulture)
                    : Math.Max(1, Environment.ProcessorCount);

                int requested = current;
                if (!shouldReset)
                {
                    if (!int.TryParse((workerCount ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out requested))
                        return "ERROR: workerCount must be an integer when reset=false.";
                    if (requested < 1)
                        return "ERROR: workerCount must be >= 1.";
                    if (requested > maximum)
                        return "ERROR: workerCount must be <= JobWorkerMaximumCount (" + maximum + ").";
                }

                var sb = new StringBuilder(2048);
                sb.AppendLine("kind=unity_editor_job_worker_count");
                sb.Append("execute=").Append(shouldExecute).AppendLine();
                sb.Append("currentJobWorkerCount=").Append(current).AppendLine();
                sb.Append("jobWorkerMaximumCount=").Append(maximum).AppendLine();
                sb.Append("processorCount=").Append(Environment.ProcessorCount).AppendLine();
                sb.Append("requestedJobWorkerCount=").Append(shouldReset ? "reset" : requested.ToString(CultureInfo.InvariantCulture)).AppendLine();
                sb.AppendLine("impact=Lowering Job Worker Count can reduce CPU contention and background thread pressure, but large imports, Burst/jobs-heavy editor tools, PlayMode, and some baking/build workflows may become slower. This affects the current Editor session and does not edit project assets.");

                if (!shouldExecute)
                {
                    sb.AppendLine("dryRun=true");
                    return sb.ToString().TrimEnd();
                }

                if (shouldReset)
                {
                    if (resetMethod == null)
                        return "ERROR: ResetJobWorkerCount is unavailable in this Unity version.";
                    resetMethod.Invoke(null, null);
                }
                else
                {
                    if (!countProp.CanWrite)
                        return "ERROR: JobsUtility.JobWorkerCount is read-only in this Unity version.";
                    countProp.SetValue(null, requested, null);
                }

                int after = Convert.ToInt32(countProp.GetValue(null, null), CultureInfo.InvariantCulture);
                sb.Append("afterJobWorkerCount=").Append(after).AppendLine();
                sb.AppendLine("status=OK");
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return "ERROR: SetJobWorkerCount failed: " + ex;
            }
        }

        [AICallable("⚠ destructive (UI) 预览或关闭匹配的 EditorWindow。默认 execute=false；execute=true 前必须 AskUser。为防误关，必须提供 typeContains 或 titleContains。args: typeContains=Profiler&titleContains=&execute=false&includeProtected=false",
            Category = CategoryName)]
        public static string CloseEditorWindows(
            string typeContains = "",
            string titleContains = "",
            string execute = "false",
            string includeProtected = "false")
        {
            try
            {
                bool shouldExecute = BoolArg(execute, false);
                bool allowProtected = BoolArg(includeProtected, false);
                string typeFilter = (typeContains ?? "").Trim();
                string titleFilter = (titleContains ?? "").Trim();

                if (string.IsNullOrEmpty(typeFilter) && string.IsNullOrEmpty(titleFilter))
                    return "ERROR: typeContains or titleContains is required; refusing to match every EditorWindow.";

                var windows = GetOpenWindows()
                    .Where(w => WindowMatches(w, typeFilter, titleFilter))
                    .ToArray();

                var sb = new StringBuilder(4096);
                sb.AppendLine("kind=unity_editor_close_windows");
                sb.Append("execute=").Append(shouldExecute).AppendLine();
                sb.AppendLine("impact=Closes matching Editor UI windows. Window layout/focus may change; active profiling/recording windows may stop showing live data. Does not write project assets.");
                sb.Append("matches=").Append(windows.Length).AppendLine();
                sb.AppendLine("rank\tprotected\ttype\ttitle");

                int closed = 0;
                for (int i = 0; i < windows.Length; i++)
                {
                    var w = windows[i];
                    bool isProtected = IsProtectedWindow(w);
                    sb.Append(i + 1).Append('\t')
                        .Append(isProtected).Append('\t')
                        .Append(Clean(w.GetType().FullName)).Append('\t')
                        .Append(GetWindowTitle(w)).AppendLine();

                    if (shouldExecute && (!isProtected || allowProtected))
                    {
                        w.Close();
                        closed++;
                    }
                }

                if (shouldExecute)
                    sb.Append("closed=").Append(closed).AppendLine();
                else
                    sb.AppendLine("dryRun=true");

                if (!allowProtected && windows.Any(IsProtectedWindow))
                    sb.AppendLine("note=Protected core windows were skipped unless includeProtected=true.");
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return "ERROR: CloseEditorWindows failed: " + ex;
            }
        }

        [AICallable("⚠ destructive (UI) 预览或调整 SceneView 性能显示选项：关闭特效/天空盒/雾/ImageEffects/粒子/AlwaysRefresh 等。默认 execute=false；execute=true 前必须 AskUser。args: execute=false&effects=false&skybox=false&fog=false&imageEffects=false&particles=false&alwaysRefresh=false",
            Category = CategoryName)]
        public static string SetSceneViewPerformanceOptions(
            string execute = "false",
            string effects = "false",
            string skybox = "false",
            string fog = "false",
            string imageEffects = "false",
            string particles = "false",
            string alwaysRefresh = "false")
        {
            try
            {
                bool shouldExecute = BoolArg(execute, false);
                var desired = new Dictionary<string, bool>
                {
                    { "effects", BoolArg(effects, false) },
                    { "skybox", BoolArg(skybox, false) },
                    { "fog", BoolArg(fog, false) },
                    { "imageEffects", BoolArg(imageEffects, false) },
                    { "particles", BoolArg(particles, false) },
                    { "alwaysRefresh", BoolArg(alwaysRefresh, false) },
                };

                var views = SceneView.sceneViews
                    .Cast<SceneView>()
                    .Where(v => v != null)
                    .ToArray();

                var sb = new StringBuilder(4096);
                sb.AppendLine("kind=unity_scene_view_performance_options");
                sb.Append("execute=").Append(shouldExecute).AppendLine();
                sb.Append("sceneViewCount=").Append(views.Length).AppendLine();
                sb.AppendLine("impact=Changes SceneView visual/editor refresh options only. Scene view may look simpler, and some effects/particles/skybox previews may be hidden until re-enabled. Does not write project assets.");

                foreach (var view in views)
                {
                    sb.Append("sceneView=").Append(GetWindowTitle(view)).AppendLine();
                    object state = GetSceneViewState(view);
                    bool stateTouched = false;

                    stateTouched |= TrySetBoolMember(state, new[] { "fxEnabled", "showEffects" }, desired["effects"], shouldExecute, "effects", sb);
                    stateTouched |= TrySetBoolMember(state, new[] { "skyboxEnabled", "showSkybox" }, desired["skybox"], shouldExecute, "skybox", sb);
                    stateTouched |= TrySetBoolMember(state, new[] { "fogEnabled", "showFog" }, desired["fog"], shouldExecute, "fog", sb);
                    stateTouched |= TrySetBoolMember(state, new[] { "imageEffectsEnabled", "showImageEffects" }, desired["imageEffects"], shouldExecute, "imageEffects", sb);
                    stateTouched |= TrySetBoolMember(state, new[] { "particleSystemsEnabled", "showParticleSystems" }, desired["particles"], shouldExecute, "particles", sb);
                    TrySetBoolMember(view, new[] { "alwaysRefresh" }, desired["alwaysRefresh"], shouldExecute, "alwaysRefresh", sb);

                    if (shouldExecute && stateTouched)
                        SetSceneViewState(view, state);
                    if (shouldExecute)
                        view.Repaint();
                }

                if (!shouldExecute)
                    sb.AppendLine("dryRun=true");
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return "ERROR: SetSceneViewPerformanceOptions failed: " + ex;
            }
        }

        [AICallable("Read a general Unity Editor configuration snapshot: EditorSettings, build target, AssetDatabase, Jobs, optional paths/performance hints. Read-only. Args: includePaths=true&includePerformance=true",
            Category = CategoryName)]
        public static string GetEditorConfigSnapshot(
            string includePaths = "true",
            string includePerformance = "true")
        {
            try
            {
                var sb = new StringBuilder(8192);
                sb.AppendLine("kind=unity_editor_config_snapshot");
                sb.Append("timestamp=").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).AppendLine();
                sb.Append("unityVersion=").Append(Application.unityVersion).AppendLine();
                sb.Append("companyName=").Append(Clean(Application.companyName)).AppendLine();
                sb.Append("productName=").Append(Clean(Application.productName)).AppendLine();
                sb.Append("isPlaying=").Append(EditorApplication.isPlaying).AppendLine();
                sb.Append("isPaused=").Append(EditorApplication.isPaused).AppendLine();
                sb.Append("isCompiling=").Append(EditorApplication.isCompiling).AppendLine();
                sb.Append("isUpdating=").Append(EditorApplication.isUpdating).AppendLine();

                if (BoolArg(includePaths, true))
                {
                    sb.AppendLine();
                    sb.AppendLine("paths:");
                    AppendConfigValue(sb, "paths.projectRoot");
                    AppendConfigValue(sb, "paths.assets");
                }

                sb.AppendLine();
                sb.AppendLine("editorSettings:");
                AppendConfigValue(sb, "editor.serializationMode");
                AppendConfigValue(sb, "editor.externalVersionControl");
                AppendConfigValue(sb, "editor.enterPlayModeOptionsEnabled");
                AppendConfigValue(sb, "editor.enterPlayModeOptions");
                AppendConfigValue(sb, "editor.cacheServerMode");
                AppendConfigValue(sb, "editor.cacheServerEndpoint");
                AppendConfigValue(sb, "editor.assetPipelineMode");
                AppendConfigValue(sb, "editor.refreshImportMode");

                sb.AppendLine();
                sb.AppendLine("build:");
                AppendConfigValue(sb, "build.activeBuildTarget");
                AppendConfigValue(sb, "build.selectedBuildTargetGroup");

                sb.AppendLine();
                sb.AppendLine("assetDatabase:");
                AppendConfigValue(sb, "assetDatabase.isAssetImportWorkerProcess");
                AppendConfigValue(sb, "assetDatabase.isAssetImportWorkerProcessV2");
                AppendConfigValue(sb, "assetDatabase.canConnectToCacheServer");

                sb.AppendLine();
                sb.AppendLine("editorPrefs:");
                AppendConfigValue(sb, "editorPrefs.kAutoRefresh");

                sb.AppendLine();
                sb.AppendLine("jobs:");
                AppendConfigValue(sb, "jobs.jobWorkerCount");
                AppendConfigValue(sb, "jobs.jobWorkerMaximumCount");

                if (BoolArg(includePerformance, true))
                {
                    sb.AppendLine();
                    sb.AppendLine("performance:");
                    AppendMemorySnapshot(sb, "memory.");
                    using (var process = Process.GetCurrentProcess())
                    {
                        sb.Append("processThreadCount=").Append(process.Threads.Count).AppendLine();
                    }
                }

                sb.AppendLine();
                sb.AppendLine("supportedConfigKeys=");
                foreach (string key in SupportedEditorConfigKeys())
                    sb.Append("  ").Append(key).AppendLine();

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return "ERROR: GetEditorConfigSnapshot failed: " + ex;
            }
        }

        [AICallable("Read one whitelisted Unity Editor configuration value. Pass an empty key to list supported keys. Read-only. Args: key=editor.enterPlayModeOptionsEnabled",
            Category = CategoryName)]
        public static string GetEditorConfigValue(string key = "")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    var list = new StringBuilder(2048);
                    list.AppendLine("kind=unity_editor_config_keys");
                    list.AppendLine("supportedConfigKeys=");
                    foreach (string supportedKey in SupportedEditorConfigKeys())
                        list.Append("  ").Append(supportedKey).AppendLine();
                    return list.ToString().TrimEnd();
                }

                string normalized = NormalizeConfigKey(key);
                var sb = new StringBuilder(1024);
                sb.AppendLine("kind=unity_editor_config_get");
                sb.Append("key=").Append(normalized).AppendLine();
                sb.Append("value=").Append(ReadEditorConfigValue(normalized)).AppendLine();
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return "ERROR: GetEditorConfigValue failed: " + ex;
            }
        }

        [AICallable("Preview or set a whitelisted Unity Editor configuration value. Default execute=false. execute=true can write Editor/Project settings and must AskUser first. Args: key=editor.enterPlayModeOptionsEnabled&value=true&execute=false",
            Category = CategoryName)]
        public static string SetEditorConfigValue(
            string key = "",
            string value = "",
            string execute = "false")
        {
            try
            {
                string normalized = NormalizeConfigKey(key);
                bool shouldExecute = BoolArg(execute, false);
                if (string.IsNullOrEmpty(normalized))
                    return "ERROR: key is required. Call GetEditorConfigValue with an empty key to list supported keys.";

                switch (normalized)
                {
                    case "jobs.jobworkercount":
                        bool resetWorkers = string.Equals((value ?? "").Trim(), "reset", StringComparison.OrdinalIgnoreCase)
                            || string.Equals((value ?? "").Trim(), "default", StringComparison.OrdinalIgnoreCase);
                        return SetJobWorkerCount(resetWorkers ? "" : value, resetWorkers ? "true" : "false", execute);

                    case "editor.enterplaymodeoptionsenabled":
                        return SetStaticEditorConfigProperty(
                            typeof(EditorSettings),
                            "enterPlayModeOptionsEnabled",
                            normalized,
                            value,
                            shouldExecute,
                            "project",
                            "Changing Enter Play Mode settings can make Play faster, but skipping reloads can expose stale static state or scene state bugs.");

                    case "editor.enterplaymodeoptions":
                        return SetStaticEditorConfigProperty(
                            typeof(EditorSettings),
                            "enterPlayModeOptions",
                            normalized,
                            value,
                            shouldExecute,
                            "project",
                            "Changing Enter Play Mode options can disable domain reload and/or scene reload. This can speed Play Mode but may require code to reset static state correctly.");

                    case "editor.serializationmode":
                        return SetStaticEditorConfigProperty(
                            typeof(EditorSettings),
                            "serializationMode",
                            normalized,
                            value,
                            shouldExecute,
                            "project",
                            "Changing serialization mode affects how Unity assets are saved and can create broad ProjectSettings or asset serialization churn.");

                    case "editor.externalversioncontrol":
                        return SetStaticEditorConfigProperty(
                            typeof(EditorSettings),
                            "externalVersionControl",
                            normalized,
                            value,
                            shouldExecute,
                            "project",
                            "Changing version-control mode affects Unity's asset checkout/status integration for the project.");

                    case "editor.cacheservermode":
                        return SetStaticEditorConfigProperty(
                            typeof(EditorSettings),
                            "cacheServerMode",
                            normalized,
                            value,
                            shouldExecute,
                            "project",
                            "Changing cache server mode affects asset import cache behavior and may change import speed or network usage.");

                    case "editor.cacheserverendpoint":
                        return SetStaticEditorConfigProperty(
                            typeof(EditorSettings),
                            "cacheServerEndpoint",
                            normalized,
                            value,
                            shouldExecute,
                            "project",
                            "Changing cache server endpoint affects asset import cache routing and may require a valid reachable cache server.");

                    case "editor.refreshimportmode":
                        return SetStaticEditorConfigProperty(
                            typeof(EditorSettings),
                            "refreshImportMode",
                            normalized,
                            value,
                            shouldExecute,
                            "project",
                            "Changing refresh/import mode affects how Unity reacts to filesystem changes and can alter editor refresh behavior.");

                    default:
                        return "ERROR: unsupported or read-only config key: " + normalized + ". Call unity_editor_config_get with an empty key to list supported keys.";
                }
            }
            catch (Exception ex)
            {
                return "ERROR: SetEditorConfigValue failed: " + ex;
            }
        }

        private static void AppendMemorySnapshot(StringBuilder sb, string prefix)
        {
            long workingSet = 0;
            long privateBytes = 0;
            double totalCpuSeconds = 0;
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    workingSet = process.WorkingSet64;
                    privateBytes = process.PrivateMemorySize64;
                    totalCpuSeconds = process.TotalProcessorTime.TotalSeconds;
                }
            }
            catch
            {
                // Process counters can fail under restricted platforms.
            }

            sb.Append(prefix).Append("processWorkingSetMB=").Append(MbNumber(workingSet)).AppendLine();
            sb.Append(prefix).Append("processPrivateMB=").Append(MbNumber(privateBytes)).AppendLine();
            sb.Append(prefix).Append("processTotalCpuSec=").Append(totalCpuSeconds.ToString("0.0", CultureInfo.InvariantCulture)).AppendLine();
            sb.Append(prefix).Append("gcManagedMB=").Append(MbNumber(GC.GetTotalMemory(false))).AppendLine();
            sb.Append(prefix).Append("profilerTotalAllocatedMB=").Append(MbNumber(Profiler.GetTotalAllocatedMemoryLong())).AppendLine();
            sb.Append(prefix).Append("profilerTotalReservedMB=").Append(MbNumber(Profiler.GetTotalReservedMemoryLong())).AppendLine();
            sb.Append(prefix).Append("profilerUnusedReservedMB=").Append(MbNumber(Profiler.GetTotalUnusedReservedMemoryLong())).AppendLine();
            sb.Append(prefix).Append("monoUsedMB=").Append(MbNumber(Profiler.GetMonoUsedSizeLong())).AppendLine();
            sb.Append(prefix).Append("monoHeapMB=").Append(MbNumber(Profiler.GetMonoHeapSizeLong())).AppendLine();
        }

        private static string ResolveProfilerCaptureOutputDir(string outputDir)
        {
            if (!string.IsNullOrWhiteSpace(outputDir))
                return outputDir.Trim();
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, ".vulcan", "editor_profiler");
        }

        private static bool TryGetProfilerDeepProfile(out bool value, out bool supported)
        {
            value = false;
            supported = false;
            try
            {
                var driverType = Type.GetType("UnityEditorInternal.ProfilerDriver, UnityEditor");
                if (driverType == null) return false;
                var prop = driverType.GetProperty("deepProfiling", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (prop != null && prop.PropertyType == typeof(bool) && prop.CanRead)
                {
                    value = (bool)prop.GetValue(null, null);
                    supported = true;
                    return true;
                }
                var field = driverType.GetField("deepProfiling", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null && field.FieldType == typeof(bool))
                {
                    value = (bool)field.GetValue(null);
                    supported = true;
                    return true;
                }
            }
            catch
            {
                supported = false;
            }
            return false;
        }

        private static bool TrySetProfilerDeepProfile(bool value)
        {
            try
            {
                var driverType = Type.GetType("UnityEditorInternal.ProfilerDriver, UnityEditor");
                if (driverType == null) return false;
                var prop = driverType.GetProperty("deepProfiling", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (prop != null && prop.PropertyType == typeof(bool) && prop.CanWrite)
                {
                    prop.SetValue(null, value, null);
                    return true;
                }
                var field = driverType.GetField("deepProfiling", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null && field.FieldType == typeof(bool))
                {
                    field.SetValue(null, value);
                    return true;
                }
            }
            catch
            {
                return false;
            }
            return false;
        }

        private static void AppendJobWorkerSnapshot(StringBuilder sb, string prefix)
        {
            var jobsType = GetJobsUtilityType();
            if (jobsType == null)
            {
                sb.Append(prefix).Append("available=false").AppendLine();
                return;
            }

            sb.Append(prefix).Append("available=true").AppendLine();
            AppendStaticProperty(sb, prefix, jobsType, "JobWorkerCount");
            AppendStaticProperty(sb, prefix, jobsType, "JobWorkerMaximumCount");
        }

        private static Type GetJobsUtilityType()
        {
            return Type.GetType("Unity.Jobs.LowLevel.Unsafe.JobsUtility, UnityEngine.CoreModule")
                   ?? Type.GetType("Unity.Jobs.LowLevel.Unsafe.JobsUtility, UnityEngine");
        }

        private static void AppendStaticProperty(StringBuilder sb, string prefix, Type type, string propertyName)
        {
            try
            {
                var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
                if (prop == null || !prop.CanRead)
                {
                    sb.Append(prefix).Append(propertyName).Append("=unsupported").AppendLine();
                    return;
                }
                object value = prop.GetValue(null, null);
                sb.Append(prefix).Append(propertyName).Append('=').Append(value != null ? value.ToString() : "").AppendLine();
            }
            catch (Exception ex)
            {
                sb.Append(prefix).Append(propertyName).Append("=ERROR:").Append(Clean(ex.Message)).AppendLine();
            }
        }

        private static string GetAssetDatabaseBool(string methodName)
        {
            try
            {
                var method = typeof(AssetDatabase).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                if (method == null || method.ReturnType != typeof(bool))
                    return "unsupported";
                return ((bool)method.Invoke(null, null)).ToString();
            }
            catch (Exception ex)
            {
                return "ERROR:" + Clean(ex.Message);
            }
        }

        private static string[] SupportedEditorConfigKeys()
        {
            return new[]
            {
                "paths.projectRoot",
                "paths.assets",
                "editor.serializationMode",
                "editor.externalVersionControl",
                "editor.enterPlayModeOptionsEnabled",
                "editor.enterPlayModeOptions",
                "editor.cacheServerMode",
                "editor.cacheServerEndpoint",
                "editor.assetPipelineMode",
                "editor.refreshImportMode",
                "build.activeBuildTarget",
                "build.selectedBuildTargetGroup",
                "assetDatabase.isAssetImportWorkerProcess",
                "assetDatabase.isAssetImportWorkerProcessV2",
                "assetDatabase.canConnectToCacheServer",
                "editorPrefs.kAutoRefresh",
                "jobs.jobWorkerCount",
                "jobs.jobWorkerMaximumCount",
            };
        }

        private static string NormalizeConfigKey(string key)
        {
            return (key ?? "").Trim().ToLowerInvariant();
        }

        private static void AppendConfigValue(StringBuilder sb, string key)
        {
            sb.Append(key).Append('=').Append(ReadEditorConfigValue(key)).AppendLine();
        }

        private static string ReadEditorConfigValue(string key)
        {
            string normalized = NormalizeConfigKey(key);
            switch (normalized)
            {
                case "paths.projectroot":
                    return Clean(Directory.GetParent(Application.dataPath).FullName);
                case "paths.assets":
                    return Clean(Application.dataPath);
                case "editor.serializationmode":
                    return ReadStaticProperty(typeof(EditorSettings), "serializationMode");
                case "editor.externalversioncontrol":
                    return ReadStaticProperty(typeof(EditorSettings), "externalVersionControl");
                case "editor.enterplaymodeoptionsenabled":
                    return ReadStaticProperty(typeof(EditorSettings), "enterPlayModeOptionsEnabled");
                case "editor.enterplaymodeoptions":
                    return ReadStaticProperty(typeof(EditorSettings), "enterPlayModeOptions");
                case "editor.cacheservermode":
                    return ReadStaticProperty(typeof(EditorSettings), "cacheServerMode");
                case "editor.cacheserverendpoint":
                    return ReadStaticProperty(typeof(EditorSettings), "cacheServerEndpoint");
                case "editor.assetpipelinemode":
                    return ReadStaticProperty(typeof(EditorSettings), "assetPipelineMode");
                case "editor.refreshimportmode":
                    return ReadStaticProperty(typeof(EditorSettings), "refreshImportMode");
                case "build.activebuildtarget":
                    return EditorUserBuildSettings.activeBuildTarget.ToString();
                case "build.selectedbuildtargetgroup":
                    return EditorUserBuildSettings.selectedBuildTargetGroup.ToString();
                case "assetdatabase.isassetimportworkerprocess":
                    return GetAssetDatabaseBool("IsAssetImportWorkerProcess");
                case "assetdatabase.isassetimportworkerprocessv2":
                    return GetAssetDatabaseBool("IsAssetImportWorkerProcessV2");
                case "assetdatabase.canconnecttocacheserver":
                    return InvokeStaticMethodToString(typeof(AssetDatabase), "CanConnectToCacheServer");
                case "editorprefs.kautorefresh":
                    return EditorPrefs.GetInt("kAutoRefresh", -1).ToString(CultureInfo.InvariantCulture);
                case "jobs.jobworkercount":
                    return ReadJobsUtilityProperty("JobWorkerCount");
                case "jobs.jobworkermaximumcount":
                    return ReadJobsUtilityProperty("JobWorkerMaximumCount");
                default:
                    return "unsupported";
            }
        }

        private static string ReadStaticProperty(Type type, string propertyName)
        {
            try
            {
                var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
                if (prop == null || !prop.CanRead)
                    return "unsupported";
                object value = prop.GetValue(null, null);
                return Clean(value != null ? value.ToString() : "");
            }
            catch (Exception ex)
            {
                return "ERROR:" + Clean(ex.Message);
            }
        }

        private static string ReadJobsUtilityProperty(string propertyName)
        {
            var jobsType = GetJobsUtilityType();
            if (jobsType == null)
                return "unsupported";
            return ReadStaticProperty(jobsType, propertyName);
        }

        private static string InvokeStaticMethodToString(Type type, string methodName)
        {
            try
            {
                var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                if (method == null || method.GetParameters().Length != 0)
                    return "unsupported";
                object value = method.Invoke(null, null);
                return Clean(value != null ? value.ToString() : "");
            }
            catch (Exception ex)
            {
                return "ERROR:" + Clean(ex.Message);
            }
        }

        private static string SetStaticEditorConfigProperty(
            Type type,
            string propertyName,
            string key,
            string value,
            bool execute,
            string scope,
            string impact)
        {
            var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
            if (prop == null || !prop.CanRead)
                return "ERROR: config property is unavailable in this Unity version: " + key;
            if (!prop.CanWrite)
                return "ERROR: config property is read-only in this Unity version: " + key;

            object current = prop.GetValue(null, null);
            object requested;
            string parseError;
            if (!TryParseConfigValue(value, prop.PropertyType, out requested, out parseError))
                return "ERROR: invalid value for " + key + ": " + parseError;

            var sb = new StringBuilder(2048);
            sb.AppendLine("kind=unity_editor_config_set");
            sb.Append("key=").Append(key).AppendLine();
            sb.Append("property=").Append(type.FullName).Append(".").Append(propertyName).AppendLine();
            sb.Append("scope=").Append(scope).AppendLine();
            sb.Append("execute=").Append(execute).AppendLine();
            sb.Append("current=").Append(Clean(current != null ? current.ToString() : "")).AppendLine();
            sb.Append("requested=").Append(Clean(requested != null ? requested.ToString() : "")).AppendLine();
            sb.Append("impact=").Append(impact).AppendLine();

            if (!execute)
            {
                sb.AppendLine("dryRun=true");
                return sb.ToString().TrimEnd();
            }

            prop.SetValue(null, requested, null);
            if (string.Equals(scope, "project", StringComparison.OrdinalIgnoreCase))
                AssetDatabase.SaveAssets();
            object after = prop.GetValue(null, null);
            sb.Append("after=").Append(Clean(after != null ? after.ToString() : "")).AppendLine();
            sb.AppendLine("status=OK");
            return sb.ToString().TrimEnd();
        }

        private static bool TryParseConfigValue(string raw, Type targetType, out object value, out string error)
        {
            value = null;
            error = "";
            string text = (raw ?? "").Trim();
            try
            {
                if (targetType == typeof(string))
                {
                    value = raw ?? "";
                    return true;
                }
                if (targetType == typeof(bool))
                {
                    bool parsed;
                    if (!TryParseBoolStrict(text, out parsed))
                    {
                        error = "expected true/false";
                        return false;
                    }
                    value = parsed;
                    return true;
                }
                if (targetType == typeof(int))
                {
                    int parsed;
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                    {
                        error = "expected integer";
                        return false;
                    }
                    value = parsed;
                    return true;
                }
                if (targetType.IsEnum)
                {
                    int parsedInt;
                    value = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt)
                        ? Enum.ToObject(targetType, parsedInt)
                        : Enum.Parse(targetType, text, true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = Clean(ex.Message);
                return false;
            }

            error = "unsupported target type " + targetType.FullName;
            return false;
        }

        private static bool TryParseBoolStrict(string text, out bool value)
        {
            value = false;
            string normalized = (text ?? "").Trim().ToLowerInvariant();
            if (normalized == "1" || normalized == "true" || normalized == "yes" || normalized == "y" || normalized == "on")
            {
                value = true;
                return true;
            }
            if (normalized == "0" || normalized == "false" || normalized == "no" || normalized == "n" || normalized == "off")
            {
                value = false;
                return true;
            }
            return false;
        }

        private static void AppendSceneStats(StringBuilder sb)
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                sb.AppendLine("scene.valid=false");
                return;
            }

            int rootCount = 0;
            int gameObjects = 0;
            int inactiveGameObjects = 0;
            int components = 0;
            int missingComponents = 0;
            int renderers = 0;
            int skinnedRenderers = 0;
            int animators = 0;
            int particleSystems = 0;
            int canvases = 0;
            int cameras = 0;
            int lights = 0;
            int colliders = 0;
            int audioSources = 0;

            try
            {
                var roots = scene.GetRootGameObjects();
                rootCount = roots.Length;
                foreach (var root in roots)
                {
                    foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (tr == null) continue;
                        var go = tr.gameObject;
                        if (go == null) continue;
                        gameObjects++;
                        if (!go.activeInHierarchy) inactiveGameObjects++;

                        foreach (var component in go.GetComponents<Component>())
                        {
                            if (component == null) missingComponents++;
                            else components++;
                        }

                        renderers += go.GetComponents<Renderer>().Length;
                        skinnedRenderers += go.GetComponents<SkinnedMeshRenderer>().Length;
                        animators += go.GetComponents<Animator>().Length;
                        particleSystems += go.GetComponents<ParticleSystem>().Length;
                        canvases += go.GetComponents<Canvas>().Length;
                        cameras += go.GetComponents<Camera>().Length;
                        lights += go.GetComponents<Light>().Length;
                        colliders += go.GetComponents<Collider>().Length + go.GetComponents<Collider2D>().Length;
                        audioSources += go.GetComponents<AudioSource>().Length;
                    }
                }
            }
            catch (Exception ex)
            {
                sb.Append("sceneStatsError=").Append(Clean(ex.Message)).AppendLine();
            }

            sb.Append("scene.rootCount=").Append(rootCount).AppendLine();
            sb.Append("scene.gameObjects=").Append(gameObjects).AppendLine();
            sb.Append("scene.inactiveGameObjects=").Append(inactiveGameObjects).AppendLine();
            sb.Append("scene.components=").Append(components).AppendLine();
            sb.Append("scene.missingComponents=").Append(missingComponents).AppendLine();
            sb.Append("scene.renderers=").Append(renderers).AppendLine();
            sb.Append("scene.skinnedRenderers=").Append(skinnedRenderers).AppendLine();
            sb.Append("scene.animators=").Append(animators).AppendLine();
            sb.Append("scene.particleSystems=").Append(particleSystems).AppendLine();
            sb.Append("scene.canvases=").Append(canvases).AppendLine();
            sb.Append("scene.cameras=").Append(cameras).AppendLine();
            sb.Append("scene.lights=").Append(lights).AppendLine();
            sb.Append("scene.colliders=").Append(colliders).AppendLine();
            sb.Append("scene.audioSources=").Append(audioSources).AppendLine();
        }

        private static void AppendOpenWindows(StringBuilder sb, int limit)
        {
            var windows = GetOpenWindows().Take(limit).ToArray();
            sb.AppendLine("rank\tfocused\tprotected\ttype\ttitle\tposition");
            for (int i = 0; i < windows.Length; i++)
            {
                var w = windows[i];
                var rect = w.position;
                sb.Append(i + 1).Append('\t')
                    .Append(EditorWindow.focusedWindow == w).Append('\t')
                    .Append(IsProtectedWindow(w)).Append('\t')
                    .Append(Clean(w.GetType().FullName)).Append('\t')
                    .Append(GetWindowTitle(w)).Append('\t')
                    .AppendFormat(CultureInfo.InvariantCulture, "x={0:0},y={1:0},w={2:0},h={3:0}", rect.x, rect.y, rect.width, rect.height)
                    .AppendLine();
            }

            if (windows.Length == 0)
                sb.AppendLine("(empty)");
        }

        private static IEnumerable<EditorWindow> GetOpenWindows()
        {
            return Resources.FindObjectsOfTypeAll<EditorWindow>()
                .Where(w => w != null)
                .OrderBy(w => w.GetType().FullName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(GetWindowTitle, StringComparer.OrdinalIgnoreCase);
        }

        private static bool WindowMatches(EditorWindow window, string typeFilter, string titleFilter)
        {
            if (window == null) return false;
            string typeName = window.GetType().FullName ?? window.GetType().Name;
            string title = GetWindowTitle(window);
            bool typeOk = string.IsNullOrEmpty(typeFilter) || ContainsIgnoreCase(typeName, typeFilter);
            bool titleOk = string.IsNullOrEmpty(titleFilter) || ContainsIgnoreCase(title, titleFilter);
            return typeOk && titleOk;
        }

        private static bool IsProtectedWindow(EditorWindow window)
        {
            if (window == null) return false;
            string name = window.GetType().Name;
            return ProtectedWindowTypeNames.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        }

        private static object GetSceneViewState(SceneView view)
        {
            var prop = typeof(SceneView).GetProperty("sceneViewState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return prop != null ? prop.GetValue(view, null) : null;
        }

        private static void SetSceneViewState(SceneView view, object state)
        {
            if (view == null || state == null) return;
            var prop = typeof(SceneView).GetProperty("sceneViewState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && prop.CanWrite)
                prop.SetValue(view, state, null);
        }

        private static bool TrySetBoolMember(
            object target,
            string[] names,
            bool desired,
            bool execute,
            string label,
            StringBuilder sb)
        {
            if (target == null)
            {
                sb.Append("  ").Append(label).Append("=unsupported").AppendLine();
                return false;
            }

            var type = target.GetType();
            foreach (string name in names)
            {
                var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && prop.PropertyType == typeof(bool) && prop.CanRead)
                {
                    bool oldValue = (bool)prop.GetValue(target, null);
                    if (execute && prop.CanWrite)
                        prop.SetValue(target, desired, null);
                    sb.Append("  ").Append(label).Append(": ")
                        .Append(oldValue).Append(" -> ").Append(desired)
                        .Append(" via ").Append(type.Name).Append(".").Append(name)
                        .Append(prop.CanWrite ? "" : " (read-only)")
                        .AppendLine();
                    return prop.CanWrite;
                }

                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(bool))
                {
                    bool oldValue = (bool)field.GetValue(target);
                    if (execute)
                        field.SetValue(target, desired);
                    sb.Append("  ").Append(label).Append(": ")
                        .Append(oldValue).Append(" -> ").Append(desired)
                        .Append(" via ").Append(type.Name).Append(".").Append(name)
                        .AppendLine();
                    return true;
                }
            }

            sb.Append("  ").Append(label).Append("=unsupported").AppendLine();
            return false;
        }

        private static void TryGetTextureSize(TextureImporter importer, string path, out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                var method = typeof(TextureImporter).GetMethod(
                    "GetSourceTextureWidthAndHeight",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null)
                {
                    object[] args = { 0, 0 };
                    method.Invoke(importer, args);
                    width = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
                    height = Convert.ToInt32(args[1], CultureInfo.InvariantCulture);
                    return;
                }
            }
            catch
            {
                // Fallback below may load the asset, but only when the importer API is unavailable.
            }

            try
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null)
                {
                    width = texture.width;
                    height = texture.height;
                }
            }
            catch
            {
                width = 0;
                height = 0;
            }
        }

        private static long SafeRuntimeMemorySize(UnityEngine.Object obj)
        {
            try
            {
                return Profiler.GetRuntimeMemorySizeLong(obj);
            }
            catch
            {
                return 0;
            }
        }

        private static int IntArg(string value, int defaultValue, int min, int max)
        {
            int parsed;
            if (!int.TryParse((value ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                parsed = defaultValue;
            if (parsed < min) return min;
            if (parsed > max) return max;
            return parsed;
        }

        private static bool BoolArg(string value, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            string normalized = value.Trim().ToLowerInvariant();
            if (normalized == "1" || normalized == "true" || normalized == "yes" || normalized == "y" || normalized == "on")
                return true;
            if (normalized == "0" || normalized == "false" || normalized == "no" || normalized == "n" || normalized == "off")
                return false;
            return defaultValue;
        }

        private static string[] SplitFilters(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new string[0];
            return raw.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool ContainsIgnoreCase(string text, string needle)
        {
            return (text ?? "").IndexOf(needle ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetWindowTitle(EditorWindow window)
        {
            try
            {
                return Clean(window != null && window.titleContent != null ? window.titleContent.text : "");
            }
            catch
            {
                return "";
            }
        }

        private static string Clean(string text)
        {
            return (text ?? "")
                .Replace('\t', ' ')
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
        }

        private static string SafeFilePart(string text)
        {
            var raw = Clean(text);
            if (string.IsNullOrEmpty(raw)) return "";
            var chars = raw.Select(ch =>
                char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.'
                    ? ch
                    : '_').ToArray();
            var safe = new string(chars).Trim('_');
            return safe.Length <= 80 ? safe : safe.Substring(0, 80);
        }

        private static string MbNumber(long bytes)
        {
            return (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string SafeWaitReason(ProcessThread thread)
        {
            try
            {
                return thread.ThreadState == System.Diagnostics.ThreadState.Wait ? thread.WaitReason.ToString() : "";
            }
            catch
            {
                return "";
            }
        }

        private sealed class LoadedObjectRow
        {
            public long Bytes;
            public string TypeName;
            public string Name;
            public bool Persistent;
            public string Path;
        }

        private sealed class TextureCandidateRow
        {
            public int Score;
            public string Path;
            public int Width;
            public int Height;
            public long FileBytes;
            public int MaxTextureSize;
            public bool IsReadable;
            public bool MipmapEnabled;
            public string Compression;
            public bool Crunched;
            public int CompressionQuality;
            public string Reasons;
        }

        private sealed class ProcessThreadRow
        {
            public int Id;
            public string State;
            public string WaitReason;
            public string Priority;
            public double TotalCpuMs;
            public double UserCpuMs;
            public double PrivilegedCpuMs;
        }
    }
}

#endif // UNITY_EDITOR
