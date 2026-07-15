// Assets/Scripts/Editor/AIWorkflow/BridgeServer.cs
// Unity-side Named Pipe server for unity-bridge.exe communication.
// Handles PING, RUN_TESTS, GET_TEST_RESULTS, CALL_METHOD commands.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace KDL.Editor
{
    /// <summary>
    /// Unity-side Named Pipe server that listens for commands from unity-bridge.exe.
    /// Auto-starts via [InitializeOnLoad] when the Unity Editor loads.
    /// </summary>
    [InitializeOnLoad]
    public static class BridgeServer
    {
        // Pipe name is derived from project path hash so multiple Unity editors
        // (opening different projects) never collide on the same pipe.
        // The EXE side computes the same hash from its own ProjectRoot.
        private static readonly string PipeName = ComputePipeName();

        private static string ComputePipeName()
        {
            // Use the full project path (lowercased, forward-slash normalized) as the key.
            // This is stable across sessions but unique per project directory.
            string projectPath = Application.dataPath; // e.g. "K:/KDL/Assets"
            string rootPath = System.IO.Path.GetDirectoryName(projectPath); // "K:/KDL"
            string normalized = rootPath.Replace('\\', '/').ToLowerInvariant();
            // Simple deterministic hash (FNV-1a 32-bit)
            uint hash = 2166136261u;
            foreach (char c in normalized)
            {
                hash ^= (uint)c;
                hash *= 16777619u;
            }
            //string pipeName = "UnityBridge_" + hash.ToString("X8");
            string pipeName = "VulcanBridge_" + hash.ToString("X8");
            Debug.Log("[BridgeServer] PipeName=" + pipeName + " (from path: " + rootPath + ")");
            return pipeName;
        }
        private static Thread _listenThread;
        private static volatile bool _running;
        private static NamedPipeServerStream _currentPipe; // Track for cleanup
        private static readonly object _pipeLock = new object();
        // volatile ensures background thread (GET_TEST_RESULTS) always sees
        // the latest value written by main thread (RunFinished callback).
        private static volatile string _lastTestResults = "{}";

        // ──── Compile error cache ──────────────────────────────────────────────────
        // Collects CompilerMessage[] from each assembly compilation.
        // Thread-safe: writes from CompilationPipeline callbacks, reads from pipe thread.
        private static readonly List<CompilerMessage> _lastCompileErrors = new List<CompilerMessage>();
        private static readonly List<CompilerMessage> _lastCompileWarnings = new List<CompilerMessage>();
        private static readonly object _compileMessagesLock = new object();
        private static DateTime _lastCompilationStartTime = DateTime.MinValue;
        private static string _lastCompilationState = "success"; // "success" or "error"

        // Main thread dispatch: SynchronizationContext + command queue + EditorApplication.update
        // This replaces the unreliable EditorApplication.delayCall approach that fails when Unity is backgrounded.
        // Inspired by CoplayDev/unity-mcp-beta's TransportCommandDispatcher pattern.
        private static SynchronizationContext _mainThreadContext;
        private static int _mainThreadId;
        private static readonly Queue<Action> _mainThreadQueue = new Queue<Action>();
        private static readonly object _queueLock = new object();

        // REFRESH_ASSETS uses a fire-and-forget flag instead of blocking main thread dispatch.
        // This avoids pipe timeout when Unity is backgrounded and main thread dispatch is slow.
        // The flag is checked every EditorApplication.update tick.
        private static volatile bool _refreshRequested;
        private static volatile bool _refreshCompleted;
        private static volatile bool _isCompiling;
        private static volatile bool _isUpdating;

        // 鈹€鈹€ Compile state file mechanism 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
        // The EXE polls this file to detect compilation state changes.
        // This is the MOST RELIABLE external detection method because:
        // 1) CompilationPipeline callbacks fire regardless of foreground/background
        // 2) [DidReloadScripts] fires after domain reload completes
        // 3) No dependency on EditorApplication.update or SynchronizationContext
        // File contents: "idle", "compiling", "success", or "error:CS1234:message"
        // IMPORTANT: Application.dataPath can ONLY be called from the main thread.
        // We cache the path in static ctor (which runs on main thread) so that
        // WriteCompileState can be called safely from any thread (including pipe threads).
        private static string _compileStateFilePath;
        private static string CompileStateFilePath => _compileStateFilePath;

        // PlayMode state file — same mechanism as compile_state.txt.
        // EXE polls this file to detect PlayMode transitions without pipe (works during Domain Reload).
        private static string _playmodeStateFilePath;

        // Cached project root path (from Application.dataPath on main thread).
        // Used by PING and other background-thread-safe responses.
        private static string _cachedProjectRoot;

        private static void WritePlaymodeState(string state)
        {
            try
            {
                string path = _playmodeStateFilePath;
                if (string.IsNullOrEmpty(path)) return;
                File.WriteAllText(path, state + "\n" + DateTime.UtcNow.ToString("o"), Encoding.UTF8);
            }
            catch { }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange stateChange)
        {
            switch (stateChange)
            {
                case PlayModeStateChange.ExitingEditMode:
                    WritePlaymodeState("entering");
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    WritePlaymodeState("playing");
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    WritePlaymodeState("exiting");
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    WritePlaymodeState("stopped");
                    break;
            }
        }

        private static void WriteCompileState(string state)
        {
            try
            {
                string path = CompileStateFilePath;
                File.WriteAllText(path, state + "\n" + DateTime.UtcNow.ToString("o"), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                try { Debug.LogWarning("[BridgeServer] WriteCompileState failed: " + ex.Message); } catch { }
            }
        }

        // Whitelist for call_method (3-layer: namespace prefix + assembly + [AICallable]).
        // Loaded from <projectRoot>/ProjectSettings/VulcanBridge.json at static-ctor time.
        // If the file is missing or malformed, falls back to the defaults below (KDL.Editor.* + Assembly-CSharp-Editor)
        // so demoNoChange / KDL projects keep working without any extra config.
        static string[] AllowedNamespacePrefixes = new[]
        {
            "KDL.Editor.",
            "KDL.Editor.Tests.",
            "KDL.Editor.Testbed.",
        };

        static HashSet<string> AllowedAssemblyNames = new HashSet<string>
        {
            "Assembly-CSharp-Editor",
            // All KDL Editor/Tests code now compiles into the default Editor assembly
        };

        // 旧 PC/Agent 工具通过 TCP invoke 调这些 public static 方法，历史代码未统一标 [AICallable]。
        // 迁移到 MCP 后仅对这批已存在工具做精确 allowlist，避免放开任意反射调用面。
        static readonly HashSet<string> LegacyCallableMethods = new HashSet<string>
        {
            "KHTimeLineWindow2.CurrentResId",
            "KHTimeLineWindow2.GetResDescription",
            "KHTimeLineWindow2.ReplaceRes",
            "KHTimeLineWindow2.PatchClipData",
            "KHTimeLineWindow2.PatchObjectFrame",
            "KHTimeLineWindow2.UndoLastEdit",
            "KHTimeLineWindow2.GetEditBackupHistory",
            "KHTimeLineWindow2.GetCurrentBytesFilePath",
            "KHTimeLineWindow2.RevealInExplorer",
            "KHTimeLineWindow2.SvnShowLog",
            "KHTimeLineWindow2.GetAnimationStateFile",
            "KHTimeLineWindow2.SetAnimationStateFile",
            "KHTimeLineWindow2.GetTimelineState",
            "KHTimeLineWindow2.SetClipByIndex",
            "KHTimeLineWindow2.SetCurrentFrame",
            "KHTimeLineWindow2.GetResDataJson",
            "KHTimeLineWindow2.GetScriptTypeRegistry",
            "KHTimeLineWindow2.GetMotionDefTable",
            "KHTimeLineWindow2.GetScriptTypeGroupDef",
            "KHTimeLineWindow2.Save",
            "KHTimeLineWindow2.Reset",
            "KHTimeLineWindow2.TogglePlay",
            "KHTimeLineWindow2.TleSave",
            "KHTimeLineWindow2.TleTogglePlay",
            "KHTimeLineWindow2.TleReset",
            "KHTimeLineWindow2.ConfirmAllAIModified",
            "KHTimeLineWindow2.ConfirmAIModified",
            "VulcanEditorServer.GetItemLogicData",
            "VulcanEditorServer.GetActorLogicData",
            "VulcanEditorServer.GetConsoleLogs",
            "VulcanEditorServer.ClearConsoleLogs",
            "VulcanEditorServer.GetBuffInfo",
            "VulcanEditorServer.GetAllSkillGroups",
            "VulcanEditorServer.GetRecordOperations",
            "VulcanEditorServer.CreateTestRecord",
            "VulcanEditorServer.ListRecordFiles",
            "VulcanEditorServer.DeleteTestRecord",
            "VulcanEditorServer.CleanupTestRecords",
            "FrameRecorderWindow.ToolGetStatus",
            "FrameRecorderWindow.ToolOpenAndRecord2D",
            "FrameRecorderWindow.ToolGetOverview",
            "FrameRecorderWindow.ToolGetFrameLog",
            "FrameRecorderWindow.ToolGetFrameLogBatch",
            "FrameRecorderWindow.ToolSetLogSetting",
            "FrameRecorderWindow.ShowWindow",
            "KO.ILRuntimeFixAutoGenerateTool.AutoGenerateJson",
            "VulcanRecordPlayback.ConfigureFrameRecorder",
            "VulcanRecordPlayback.StartFrameRecording",
            "VulcanRecordPlayback.StopFrameRecording",
            "VulcanRecordPlayback.ScheduleStopFrameRecording",
            "VulcanRecordPlayback.SwitchFrameRecorderSlot",
            "VulcanRecordPlayback.GetFrameRecorderSlotStatus",
            "VulcanSpriteAnimImporter.ImportSpriteAnimations",
        };

        // Prefixes stripped from Type.FullName when auto-deriving Category for [AICallable]
        // methods that don't explicitly set Category. Loaded from VulcanBridge.json's
        // "categoryStripPrefixes" field; falls back to AllowedNamespacePrefixes when absent.
        // Example: "KDL.Editor.Vulcan.GameplayActorOps" with prefix "KDL.Editor."
        //          → derived Category = "Vulcan.GameplayActorOps"
        // See: Documentation/unityBridge开发文档记录/07-AICallable扩展-设计与实施.md §5
        static string[] CategoryStripPrefixes = new[]
        {
            "KDL.Editor.",
        };

        /// <summary>
        /// Try to override the whitelist defaults with the project-level config file:
        ///   &lt;projectRoot&gt;/ProjectSettings/VulcanBridge.json
        ///   {
        ///     "namespaceWhitelist":     ["KH.Editor.", "MyProj.Editor."],
        ///     "assemblyWhitelist":      ["Assembly-CSharp-Editor"],
        ///     "categoryStripPrefixes":  ["KDL.Editor.", "KDL."]
        ///   }
        /// MUST be called from the main thread (uses Application.dataPath).
        /// Silent on missing file (defaults stay). Logs a warning on parse failure.
        /// </summary>
        static void LoadWhitelistConfig()
        {
            try
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                string configPath = Path.Combine(projectRoot, "ProjectSettings", "VulcanBridge.json");
                if (!File.Exists(configPath))
                {
                    Debug.Log("[BridgeServer] VulcanBridge.json not found at " + configPath +
                              " — using built-in defaults (KDL.Editor.* / Assembly-CSharp-Editor).");
                    // No config: stripPrefixes defaults to namespaceWhitelist.
                    CategoryStripPrefixes = AllowedNamespacePrefixes;
                    return;
                }

                string json = File.ReadAllText(configPath, Encoding.UTF8);
                var nsList = ExtractStringArray(json, "namespaceWhitelist");
                var asmList = ExtractStringArray(json, "assemblyWhitelist");
                var stripList = ExtractStringArray(json, "categoryStripPrefixes");

                if (nsList != null && nsList.Count > 0)
                    AllowedNamespacePrefixes = nsList.ToArray();
                if (asmList != null && asmList.Count > 0)
                    AllowedAssemblyNames = new HashSet<string>(asmList);
                // categoryStripPrefixes: if not configured, reuse namespaceWhitelist as
                // the strip prefix list (whitelist roots are usually the right thing to strip).
                if (stripList != null && stripList.Count > 0)
                    CategoryStripPrefixes = stripList.ToArray();
                else
                    CategoryStripPrefixes = AllowedNamespacePrefixes;

                Debug.Log("[BridgeServer] Whitelist loaded from " + configPath +
                          " — namespaces=[" + string.Join(", ", AllowedNamespacePrefixes) + "]" +
                          " assemblies=[" + string.Join(", ", AllowedAssemblyNames) + "]" +
                          " categoryStripPrefixes=[" + string.Join(", ", CategoryStripPrefixes) + "]");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BridgeServer] Failed to parse VulcanBridge.json: " + ex.Message +
                                 " — falling back to built-in defaults.");
                CategoryStripPrefixes = AllowedNamespacePrefixes;
            }
        }

        /// <summary>Minimal JSON string-array extractor (zero-dep). Returns null if key not found.</summary>
        static List<string> ExtractStringArray(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            string needle = "\"" + key + "\"";
            int keyIdx = json.IndexOf(needle, StringComparison.Ordinal);
            if (keyIdx < 0) return null;
            int colon = json.IndexOf(':', keyIdx + needle.Length);
            if (colon < 0) return null;
            int lb = json.IndexOf('[', colon + 1);
            if (lb < 0) return null;
            int rb = json.IndexOf(']', lb + 1);
            if (rb < 0) return null;

            var result = new List<string>();
            string body = json.Substring(lb + 1, rb - lb - 1);
            int i = 0;
            while (i < body.Length)
            {
                int q1 = body.IndexOf('"', i);
                if (q1 < 0) break;
                int q2 = body.IndexOf('"', q1 + 1);
                if (q2 < 0) break;
                result.Add(body.Substring(q1 + 1, q2 - q1 - 1));
                i = q2 + 1;
            }
            return result;
        }

        static BridgeServer()
        {
            // Skip initialization in AssetImportWorker and other batch-mode sub-processes.
            // Unity spawns AssetImportWorker as a separate process with -batchmode.
            // [InitializeOnLoad] fires in those sub-processes too, causing them to
            // create competing pipe servers and return wrong PIDs.
            if (Application.isBatchMode)
            {
                Debug.Log("[BridgeServer] Skipping: running in batch mode (AssetImportWorker or CI).");
                return;
            }

            // Load call_method whitelist from ProjectSettings/VulcanBridge.json (silent fallback to defaults).
            // Done early in the static ctor, before any pipe activity, on the main thread.
            LoadWhitelistConfig();

            // Capture main thread context at editor load (static ctor runs on main thread)
            _mainThreadContext = SynchronizationContext.Current;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

            // Install permanent EditorApplication.update hooks.
            // DrainMainThreadQueue: processes command queue for main-thread-only commands.
            // CheckRefreshRequest: polls the _refreshRequested flag to trigger AssetDatabase.Refresh.
            // These must NEVER be unregistered.
            EditorApplication.update += DrainMainThreadQueue;
            EditorApplication.update += CheckRefreshRequest;

            // Cache compile state file path on main thread (Application.dataPath requires main thread)
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            _cachedProjectRoot = projectRoot;
            _compileStateFilePath = Path.Combine(projectRoot, "Library", "compile_state.txt");
            _playmodeStateFilePath = Path.Combine(projectRoot, "Library", "playmode_state.txt");

            // PlayMode state file — written by callback, polled by EXE.
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            WritePlaymodeState(EditorApplication.isPlaying ? "playing" : "stopped");

            // Register compilation pipeline callbacks for compile state file + error collection.
            // These fire reliably even when Unity is backgrounded.
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;

            // Write initial compile state after domain reload.
            // IMPORTANT: Do NOT unconditionally write "success" here!
            // Domain reload only fires for assemblies that compiled successfully.
            // Other assemblies (e.g. Assembly-CSharp) may still have compile errors.
            // EditorUtility.scriptCompilationFailed reflects the OVERALL compilation status.
            if (EditorUtility.scriptCompilationFailed)
            {
                WriteCompileState("error");
                Debug.Log("[BridgeServer] Domain reload complete, but scriptCompilationFailed=true. compile state: error");
            }
            else
            {
                WriteCompileState("success");
                Debug.Log("[BridgeServer] Domain reload complete, compile state: success");
            }

            StartServer();
            EditorApplication.quitting += StopServer;
            // Re-register domain reload safety
            AssemblyReloadEvents.beforeAssemblyReload += StopServer;
            AssemblyReloadEvents.afterAssemblyReload += StartServer;
        }

        /// <summary>
        /// Enqueue an action for execution on the Unity main thread.
        /// Uses SynchronizationContext.Post + QueuePlayerLoopUpdate to ensure
        /// the action executes even when Unity is in background.
        /// </summary>
        private static void RunOnMainThread(Action action)
        {
            // If we're already on the main thread, execute immediately
            if (Thread.CurrentThread.ManagedThreadId == _mainThreadId)
            {
                try { action?.Invoke(); }
                catch (Exception ex) { Debug.LogError("[BridgeServer] Main thread action failed: " + ex.Message); }
                return;
            }

            lock (_queueLock)
            {
                _mainThreadQueue.Enqueue(action);
            }

            // Multi-strategy wake-up to ensure Unity's main thread processes the queue,
            // even when Unity is backgrounded/unfocused.

            // Strategy 1: QueuePlayerLoopUpdate 鈥?asks Unity to run one update loop iteration.
            // This is the most direct mechanism but may be delayed when Unity is unfocused.
            try { EditorApplication.QueuePlayerLoopUpdate(); } catch { }

            // Strategy 2: SynchronizationContext.Post 鈥?posts to the main thread's message pump.
            // More reliable than QueuePlayerLoopUpdate in some Unity versions.
            if (_mainThreadContext != null)
            {
                _mainThreadContext.Post(_ =>
                {
                    DrainMainThreadQueue();
                    try { EditorApplication.QueuePlayerLoopUpdate(); } catch { }
                }, null);
            }

            // Strategy 3: EditorApplication.delayCall 鈥?fallback for when the above two fail.
            // delayCall is throttled when Unity is unfocused, but it's still a valid backup.
            EditorApplication.delayCall += DrainMainThreadQueue;
        }

        /// <summary>
        /// Drain and execute all queued main-thread actions.
        /// Called from EditorApplication.update every frame.
        /// Self-driving: while the queue is non-empty, keeps calling QueuePlayerLoopUpdate
        /// to ensure Unity continues pumping update ticks even when backgrounded.
        /// </summary>
        private static void DrainMainThreadQueue()
        {
            // Process all pending actions
            while (true)
            {
                Action action = null;
                lock (_queueLock)
                {
                    if (_mainThreadQueue.Count == 0) break;
                    action = _mainThreadQueue.Dequeue();
                }

                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogError("[BridgeServer] Main thread action failed: " + ex.Message);
                }
            }

            // Self-driving loop: if there are still items in the queue (added during
            // execution of previous actions), request another update tick immediately.
            // This also handles the edge case where items are enqueued just after
            // the while-loop exits but before this method returns.
            lock (_queueLock)
            {
                if (_mainThreadQueue.Count > 0)
                {
                    try { EditorApplication.QueuePlayerLoopUpdate(); } catch { }
                }
            }
        }

        // 鈹€鈹€ CompilationPipeline callbacks 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
        // These fire reliably regardless of Unity foreground/background state.
        private static void OnCompilationStarted(object context)
        {
            Debug.Log("[BridgeServer] CompilationPipeline: compilation STARTED");
            lock (_compileMessagesLock)
            {
                _lastCompileErrors.Clear();
                _lastCompileWarnings.Clear();
                _lastCompilationStartTime = DateTime.UtcNow;
            }
            WriteCompileState("compiling");
        }

        private static void OnCompilationFinished(object context)
        {
            // At this point compilation output is known.
            // Check for errors by examining EditorUtility.scriptCompilationFailed.
            bool hasErrors = EditorUtility.scriptCompilationFailed;
            
            if (hasErrors)
            {
                // Compilation failed: assemblyCompilationFinished won't fire for failed assemblies.
                // Parse Editor.log to collect error messages (works for standard Unity).
                ParseEditorLogForErrors();
                
                // Fallback: If no errors collected (Tuanjie doesn't write to Editor.log),
                // use LogEntries reflection to read compile errors from Unity Console.
                lock (_compileMessagesLock)
                {
                    if (_lastCompileErrors.Count == 0)
                    {
                        Debug.Log("[BridgeServer] No errors from Editor.log or assemblyCompilationFinished, trying LogEntries fallback...");
                        CollectErrorsFromLogEntries();
                    }
                    _lastCompilationState = "error";
                }
                
                Debug.Log("[BridgeServer] CompilationPipeline: compilation FAILED (errors=" + _lastCompileErrors.Count + ")");
                
                // Write compile_errors.json so EXE can read errors even when:
                // 1) Tuanjie engine doesn't write compile errors to Editor.log
                // 2) Pipe communication fails during domain reload
                WriteCompileErrorsJson();
                
                WriteCompileState("error");
            }
            else
            {
                lock (_compileMessagesLock)
                {
                    _lastCompilationState = "success";
                }
                
                // Clear compile_errors.json on success
                ClearCompileErrorsJson();
                
                Debug.Log("[BridgeServer] CompilationPipeline: compilation finished (success pending domain reload)");
                // Don't write "success" here – domain reload will re-run static ctor
                // which writes "success". If we write it here, EXE might read "success"
                // before domain reload completes, causing premature return.
                WriteCompileState("reloading");
            }
        }
        
        /// <summary>
        /// Fallback: Use reflection to read compile errors from Unity Console via LogEntries API.
        /// This works on Tuanjie engine where compile errors are NOT written to Editor.log
        /// but ARE visible in the Console window.
        /// Must be called from main thread (CompilationPipeline callbacks run on main thread).
        /// Caller must hold _compileMessagesLock.
        /// </summary>
        private static void CollectErrorsFromLogEntries()
        {
            try
            {
                var logEntriesType = Type.GetType("UnityEditor.LogEntries, UnityEditor");
                if (logEntriesType == null)
                {
                    Debug.LogWarning("[BridgeServer] LogEntries type not found via reflection");
                    return;
                }
                
                var startMethod = logEntriesType.GetMethod("StartGettingEntries", 
                    BindingFlags.Static | BindingFlags.Public);
                var getEntryMethod = logEntriesType.GetMethod("GetEntryInternal", 
                    BindingFlags.Static | BindingFlags.Public);
                var endMethod = logEntriesType.GetMethod("EndGettingEntries", 
                    BindingFlags.Static | BindingFlags.Public);
                var getCountMethod = logEntriesType.GetMethod("GetCount", 
                    BindingFlags.Static | BindingFlags.Public);
                
                if (startMethod == null || getCountMethod == null || endMethod == null)
                {
                    Debug.LogWarning("[BridgeServer] LogEntries reflection methods not found");
                    return;
                }
                
                int count = (int)getCountMethod.Invoke(null, null);
                if (count == 0) return;
                
                startMethod.Invoke(null, null);
                try
                {
                    // LogEntry struct fields we need
                    var logEntryType = Type.GetType("UnityEditor.LogEntry, UnityEditor");
                    if (logEntryType == null)
                    {
                        Debug.LogWarning("[BridgeServer] LogEntry type not found");
                        return;
                    }
                    
                    var entry = Activator.CreateInstance(logEntryType);
                    var modeField = logEntryType.GetField("mode", 
                        BindingFlags.Instance | BindingFlags.Public);
                    var messageField = logEntryType.GetField("message", 
                        BindingFlags.Instance | BindingFlags.Public) 
                        ?? logEntryType.GetField("condition", 
                        BindingFlags.Instance | BindingFlags.Public);
                    var fileField = logEntryType.GetField("file", 
                        BindingFlags.Instance | BindingFlags.Public);
                    var lineField = logEntryType.GetField("line", 
                        BindingFlags.Instance | BindingFlags.Public);
                    
                    // Scan last entries for compile errors
                    // mode & 1 = Error, mode & 2 = Warning
                    // Compile errors typically have mode flags that include ScriptCompileError (1 << 11 = 2048)
                    int startIdx = Math.Max(0, count - 200); // Check last 200 entries
                    for (int i = startIdx; i < count; i++)
                    {
                        if (getEntryMethod != null)
                        {
                            bool ok = (bool)getEntryMethod.Invoke(null, new object[] { i, entry });
                            if (!ok) continue;
                        }
                        
                        int mode = modeField != null ? (int)modeField.GetValue(entry) : 0;
                        string message = messageField != null ? (string)messageField.GetValue(entry) : "";
                        string file = fileField != null ? (string)fileField.GetValue(entry) : "";
                        int line = lineField != null ? (int)lineField.GetValue(entry) : 0;
                        
                        // Check if this is a compile error:
                        // ScriptCompileError mode flag = 1 << 11 = 2048
                        // Also check message pattern: "Assets/...cs(line,col): error CS..."
                        bool isCompileError = (mode & 2048) != 0;
                        bool looksLikeCompileError = message != null && 
                            message.Contains("): error CS");
                        
                        if (isCompileError || looksLikeCompileError)
                        {
                            var parsed = ParseCompilerMessageLine(message);
                            if (parsed.HasValue)
                            {
                                _lastCompileErrors.Add(parsed.Value);
                            }
                            else
                            {
                                // Can't parse structured format, store raw message
                                _lastCompileErrors.Add(new CompilerMessage
                                {
                                    file = file ?? "",
                                    line = line,
                                    column = 0,
                                    message = message ?? "",
                                    type = CompilerMessageType.Error
                                });
                            }
                            
                            if (_lastCompileErrors.Count >= 50) break;
                        }
                    }
                    
                    Debug.Log("[BridgeServer] LogEntries fallback collected " + _lastCompileErrors.Count + " compile errors");
                }
                finally
                {
                    endMethod.Invoke(null, null);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BridgeServer] CollectErrorsFromLogEntries failed: " + ex.Message);
            }
        }
        
        /// <summary>
        /// Write compile errors/warnings to Library/compile_errors.json.
        /// This file is the RELIABLE channel for EXE to read compile errors,
        /// because Tuanjie engine does NOT write compile errors to Editor.log.
        /// Format matches HandleGetCompileErrors() output for consistency.
        /// </summary>
        private static void WriteCompileErrorsJson()
        {
            try
            {
                List<CompilerMessage> errors;
                List<CompilerMessage> warnings;
                lock (_compileMessagesLock)
                {
                    errors = new List<CompilerMessage>(_lastCompileErrors);
                    warnings = new List<CompilerMessage>(_lastCompileWarnings);
                }
                
                var errorsJson = new List<string>();
                foreach (var err in errors)
                    errorsJson.Add(FormatCompilerMessage(err));
                
                var warningsJson = new List<string>();
                foreach (var warn in warnings)
                    warningsJson.Add(FormatCompilerMessage(warn));
                
                string json = "{" +
                    "\"errorCount\":" + errors.Count + "," +
                    "\"warningCount\":" + warnings.Count + "," +
                    "\"errors\":[" + string.Join(",", errorsJson.ToArray()) + "]," +
                    "\"warnings\":[" + string.Join(",", warningsJson.ToArray()) + "]" +
                    "}";
                
                string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "compile_errors.json");
                File.WriteAllText(path, json, Encoding.UTF8);
                Debug.Log("[BridgeServer] Wrote compile_errors.json (" + errors.Count + " errors, " + warnings.Count + " warnings)");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BridgeServer] WriteCompileErrorsJson failed: " + ex.Message);
            }
        }
        
        /// <summary>
        /// Clear compile_errors.json on successful compilation.
        /// </summary>
        private static void ClearCompileErrorsJson()
        {
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "compile_errors.json");
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        /// <summary>
        /// Callback fired for each assembly after compilation.
        /// Collects CompilerMessage[] for errors/warnings from SUCCESSFUL compilations.
        /// Note: This does NOT fire for failed assemblies, so we also parse Editor.log in OnCompilationFinished.
        /// </summary>
        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (messages == null || messages.Length == 0) return;

            lock (_compileMessagesLock)
            {
                foreach (var msg in messages)
                {
                    if (msg.type == CompilerMessageType.Error)
                    {
                        _lastCompileErrors.Add(msg);
                    }
                    else if (msg.type == CompilerMessageType.Warning)
                    {
                        _lastCompileWarnings.Add(msg);
                    }
                }
            }
        }

        /// <summary>
        /// Parse Editor.log to extract compilation errors when compilation fails.
        /// This is necessary because assemblyCompilationFinished doesn't fire for failed assemblies.
        /// Uses FileShare.ReadWrite to read the log file while Unity is writing to it.
        /// Uses HashSet to deduplicate errors (Editor.log may contain multiple compilation attempts).
        /// </summary>
        private static void ParseEditorLogForErrors()
        {
            try
            {
                string logPath = Application.consoleLogPath;
                if (!System.IO.File.Exists(logPath))
                {
                    Debug.LogWarning("[BridgeServer] Editor.log not found at: " + logPath);
                    return;
                }

                // Read with FileShare.ReadWrite to avoid sharing violation
                string[] allLines;
                using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    var linesList = new List<string>();
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        linesList.Add(line);
                    }
                    allLines = linesList.ToArray();
                }
                
                // Process last 10000 lines to capture recent compilation errors
                var lines = allLines.Skip(Math.Max(0, allLines.Length - 10000)).ToArray();
                
                // Use HashSet to deduplicate errors (same file:line:message)
                var errorSet = new HashSet<string>();
                var warningSet = new HashSet<string>();
                
                lock (_compileMessagesLock)
                {
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        
                        // Unity error format: "Assets/Path/File.cs(123,45): error CS0029: message"
                        if (line.Contains("): error ") || line.Contains("): warning "))
                        {
                            var parsed = ParseCompilerMessageLine(line);
                            if (parsed.HasValue)
                            {
                                // Create unique key for deduplication: file:line:column:message
                                string key = parsed.Value.file + ":" + parsed.Value.line + ":" + parsed.Value.column + ":" + parsed.Value.message;
                                
                                if (line.Contains("): error "))
                                {
                                    if (errorSet.Add(key))
                                    {
                                        _lastCompileErrors.Add(parsed.Value);
                                    }
                                }
                                else
                                {
                                    if (warningSet.Add(key))
                                    {
                                        _lastCompileWarnings.Add(parsed.Value);
                                    }
                                }
                            }
                        }
                    }
                }
                
                Debug.Log("[BridgeServer] ParseEditorLog: found " + _lastCompileErrors.Count + " unique errors, " + _lastCompileWarnings.Count + " unique warnings");
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[BridgeServer] Failed to parse Editor.log: " + ex.Message);
            }
        }

        /// <summary>
        /// Parse a single compiler message line from Editor.log.
        /// Format: "Assets/Path/File.cs(123,45): error CS0029: Cannot implicitly convert..."
        /// </summary>
        private static CompilerMessage? ParseCompilerMessageLine(string line)
        {
            try
            {
                // Find the file path (before the opening parenthesis)
                int parenIndex = line.IndexOf('(');
                if (parenIndex < 0) return null;
                
                string file = line.Substring(0, parenIndex).Trim();
                
                // Find line and column numbers inside parentheses
                int closeParenIndex = line.IndexOf(')', parenIndex);
                if (closeParenIndex < 0) return null;
                
                string lineColStr = line.Substring(parenIndex + 1, closeParenIndex - parenIndex - 1);
                string[] parts = lineColStr.Split(',');
                if (parts.Length != 2) return null;
                
                if (!int.TryParse(parts[0], out int lineNum)) return null;
                if (!int.TryParse(parts[1], out int colNum)) return null;
                
                // Find the message (after ": error " or ": warning ")
                int msgStartIndex = line.IndexOf(": ", closeParenIndex);
                if (msgStartIndex < 0) return null;
                msgStartIndex += 2; // skip ": "
                
                // Skip "error " or "warning " prefix
                msgStartIndex = line.IndexOf(": ", msgStartIndex);
                if (msgStartIndex < 0) return null;
                msgStartIndex += 2;
                
                string message = line.Substring(msgStartIndex).Trim();
                
                bool isError = line.Contains("): error ");
                
                return new CompilerMessage
                {
                    file = file,
                    line = lineNum,
                    column = colNum,
                    message = message,
                    type = isError ? CompilerMessageType.Error : CompilerMessageType.Warning
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// EditorApplication.update hook that checks the _refreshRequested flag
        /// and executes AssetDatabase.Refresh + RequestScriptCompilation on the main thread.
        /// This is more reliable than RunOnMainThread for REFRESH_ASSETS because:
        /// 1) EditorApplication.update fires every frame (even if throttled when backgrounded)
        /// 2) No blocking/waiting on the pipe thread 鈥?the pipe thread just polls the result
        /// 3) QueuePlayerLoopUpdate nudges Unity to fire this hook even when backgrounded
        /// </summary>
        private static void CheckRefreshRequest()
        {
            if (!_refreshRequested) return;
            // If RunOnMainThread already completed the refresh, skip
            if (_refreshCompleted) { _refreshRequested = false; return; }
            _refreshRequested = false;

            DoRefreshAndWriteState("CheckRefreshRequest");
        }

        /// <summary>
        /// Shared implementation: execute AssetDatabase.Refresh + RequestScriptCompilation,
        /// then write compile_state.txt with the result.
        /// If no compilation is triggered, writes "success" so the EXE doesn't wait forever.
        /// </summary>
        private static void DoRefreshAndWriteState(string caller)
        {
            try
            {
                Debug.Log("[BridgeServer] " + caller + ": Executing AssetDatabase.Refresh on main thread...");
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

                try
                {
                    CompilationPipeline.RequestScriptCompilation();
                    Debug.Log("[BridgeServer] " + caller + ": RequestScriptCompilation() called");
                }
                catch (Exception compEx)
                {
                    Debug.LogWarning("[BridgeServer] " + caller + ": RequestScriptCompilation failed (non-fatal): " + compEx.Message);
                }

                _isCompiling = EditorApplication.isCompiling;
                _isUpdating = EditorApplication.isUpdating;
                Debug.Log("[BridgeServer] " + caller + ": Done (isCompiling=" + _isCompiling + ", isUpdating=" + _isUpdating + ")");

                // If Unity did NOT start compiling, write "success" immediately.
                // Otherwise CompilationPipeline callbacks will update the state file.
                // This prevents the EXE from waiting forever when there's nothing to compile.
                if (!_isCompiling && !_isUpdating)
                {
                    WriteCompileState("success");
                    Debug.Log("[BridgeServer] " + caller + ": No compilation needed, wrote 'success' to compile_state.txt");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[BridgeServer] " + caller + " failed: " + ex.Message);
            }
            finally
            {
                _refreshCompleted = true;
            }
        }

        /// <summary>
        /// Start the Named Pipe server on a background thread.
        /// </summary>
        public static void StartServer()
        {
            if (_running) return;
            _running = true;
            _listenThread = new Thread(() =>
            {
                // Wait for old pipe handles to be fully released by OS after domain reload
                Thread.Sleep(2000);
                if (!_running) return;
                ListenLoop();
            })
            {
                IsBackground = true,
                Name = "BridgeServer"
            };
            _listenThread.Start();
            Debug.Log("[BridgeServer] Started listening on pipe: " + PipeName);
        }

        /// <summary>
        /// Stop the Named Pipe server and force-release the pipe.
        /// </summary>
        public static void StopServer()
        {
            _running = false;

            // Force close the current pipe to unblock WaitForConnection
            lock (_pipeLock)
            {
                if (_currentPipe != null)
                {
                    try
                    {
                        if (_currentPipe.IsConnected)
                            _currentPipe.Disconnect();
                        _currentPipe.Dispose();
                    }
                    catch { }
                    _currentPipe = null;
                }
            }

            // Connect to the pipe as a client to unblock WaitForConnection
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut))
                {
                    client.Connect(500);
                }
            }
            catch { }

            // Wait for thread to finish
            if (_listenThread != null && _listenThread.IsAlive)
            {
                if (!_listenThread.Join(3000))
                {
                    try { _listenThread.Abort(); } catch { }
                }
                _listenThread = null;
            }

            // Give OS time to fully release the pipe handle
            Thread.Sleep(200);

            Debug.Log("[BridgeServer] Stopped.");
        }

        private static void ListenLoop()
        {
            int retryDelay = 500; // Exponential backoff start

            while (_running)
            {
                NamedPipeServerStream pipeServer = null;
                try
                {
                    // Outer safety: if anything in this iteration crashes,
                    // we log and continue the loop instead of killing the thread.
                    pipeServer = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    retryDelay = 500; // Reset backoff on success
                    lock (_pipeLock) { _currentPipe = pipeServer; }

                    // Wait for connection with timeout so we can check _running flag
                    var ar = pipeServer.BeginWaitForConnection(null, null);
                    while (!ar.AsyncWaitHandle.WaitOne(500))
                    {
                        if (!_running)
                        {
                            pipeServer.Dispose();
                            return;
                        }
                    }
                    pipeServer.EndWaitForConnection(ar);

                    // Read request
                    string request = ReadMessage(pipeServer);
                    if (string.IsNullOrEmpty(request))
                    {
                        pipeServer.Dispose();
                        continue;
                    }

                    // Determine if command needs Unity main thread
                    string cmdPeek = ExtractJsonString(request, "cmd");
                    bool needsMainThread = RequiresMainThread(cmdPeek);

                    string response = null;

                    if (needsMainThread)
                    {
                        // Process on main thread for Unity API access.
                        // Uses RunOnMainThread (SynchronizationContext + QueuePlayerLoopUpdate)
                        // instead of EditorApplication.delayCall, which fails when Unity is backgrounded.
                        var waitHandle = new ManualResetEvent(false);

                        RunOnMainThread(() =>
                        {
                            try
                            {
                                response = ProcessCommand(request);
                            }
                            catch (Exception ex)
                            {
                                response = MakeError("Exception: " + ex.Message);
                            }
                            finally
                            {
                                waitHandle.Set();
                            }
                        });

                    // Determine dynamic timeout from the command's timeout field.
                    // RUN_TESTS with synchronous completion may need 120s+.
                    string cmdTimeout = ExtractJsonString(request, "timeout");
                    int dynamicTimeoutSec = 30; // Default 30s for most commands
                    if (!string.IsNullOrEmpty(cmdTimeout))
                    {
                        int parsed;
                        if (int.TryParse(cmdTimeout, out parsed) && parsed > 0)
                            dynamicTimeoutSec = parsed + 10; // Add 10s buffer
                    }

                    // Wait for main thread, but also check if pipe is still connected.
                    // Client may disconnect early (timeout), in which case we should
                    // stop waiting and move on to accept a new connection.
                    int elapsed = 0;
                    const int checkInterval = 500; // Check every 500ms
                    int maxWait = dynamicTimeoutSec * 1000;
                    bool signaled = false;

                        while (elapsed < maxWait)
                        {
                            signaled = waitHandle.WaitOne(checkInterval);
                            if (signaled) break;

                            elapsed += checkInterval;

                            // Check if client is still connected
                            if (!pipeServer.IsConnected)
                            {
                                try { Debug.LogWarning("[BridgeServer] Client disconnected while waiting for main thread. Aborting wait."); }
                                catch { }
                                break;
                            }
                        }

                        if (!signaled && pipeServer.IsConnected)
                        {
                            response = MakeError("Timeout waiting for Unity main thread (" + dynamicTimeoutSec + "s). Command: " + (cmdPeek ?? "unknown"));
                        }
                    }
                    else
                    {
                        // Process directly on background thread (no Unity API needed)
                        try
                        {
                            response = ProcessCommand(request);
                        }
                        catch (Exception ex)
                        {
                            response = MakeError("Exception: " + ex.Message);
                        }
                    }

                    // Write response (pipe may already be disconnected if client timed out)
                    try
                    {
                        WriteMessage(pipeServer, response ?? MakeError("null response"));
                    }
                    catch (IOException)
                    {
                        // "Pipe is broken" 鈥?client disconnected before we could respond.
                        // This is normal when client-side timeout fires before delayCall executes.
                        try { Debug.LogWarning("[BridgeServer] Client disconnected before response could be sent (Pipe broken). This is usually harmless."); }
                        catch { }
                    }
                    lock (_pipeLock) { _currentPipe = null; }
                    try { pipeServer.Dispose(); } catch { }
                }
                catch (Exception ex)
                {
                    // Suppress errors during shutdown
                    if (_running)
                    {
                        // Use try-catch for logging: Debug.LogWarning may throw
                        // on background threads in some Unity versions
                        try { Debug.LogWarning("[BridgeServer] Error: " + ex.Message); }
                        catch { /* Swallow logging errors to keep thread alive */ }
                    }
                    lock (_pipeLock) { _currentPipe = null; }
                    if (pipeServer != null)
                    {
                        try { pipeServer.Dispose(); } catch { }
                    }
                    // Exponential backoff (500ms 鈫?1s 鈫?2s 鈫?4s, max 5s)
                    Thread.Sleep(retryDelay);
                    retryDelay = Math.Min(retryDelay * 2, 5000);
                }
            }
        }

        private static string ReadMessage(NamedPipeServerStream pipe)
        {
            // Protocol: 4-byte little-endian length prefix, then UTF-8 payload
            byte[] lenBuf = new byte[4];
            int read = 0;
            while (read < 4)
            {
                int n = pipe.Read(lenBuf, read, 4 - read);
                if (n == 0) return null;
                read += n;
            }
            int length = BitConverter.ToInt32(lenBuf, 0);
            if (length <= 0 || length > 10 * 1024 * 1024) return null;

            byte[] payload = new byte[length];
            read = 0;
            while (read < length)
            {
                int n = pipe.Read(payload, read, length - read);
                if (n == 0) return null;
                read += n;
            }
            return Encoding.UTF8.GetString(payload);
        }

        private static void WriteMessage(NamedPipeServerStream pipe, string message)
        {
            byte[] payload = Encoding.UTF8.GetBytes(message);
            byte[] lenBuf = BitConverter.GetBytes(payload.Length);
            pipe.Write(lenBuf, 0, 4);
            pipe.Write(payload, 0, payload.Length);
            pipe.Flush();
        }

        /// <summary>
        /// Determine if a command requires Unity main thread execution.
        /// Commands that only return static data can run on background thread,
        /// which is critical when Unity is in background (delayCall won't fire).
        /// </summary>
        private static bool RequiresMainThread(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return true; // unknown → safe default
            switch (cmd.ToUpper())
            {
                case "PING":            // Only reads Application.unityVersion (thread-safe)
                case "GET_TEST_RESULTS": // Only reads _lastTestResults (volatile field)
                case "GET_LOG_PATH":     // Only reads Application.consoleLogPath (thread-safe)
                case "GET_COMPILE_ERRORS": // Only reads _lastCompileErrors/Warnings (lock-protected)
                case "GET_COMPILE_STATUS": // Only reads _lastCompilationState/Time (lock-protected)
                case "LIST_CALLABLE":   // Reflection scan of loaded assemblies (thread-safe)
                    return false;
                case "REFRESH_ASSETS":  // Now uses fire-and-forget flag, does NOT need main thread blocking
                    return false;
                case "OPEN_SCENE":     // Must run on main thread to open scene
                case "ENTER_PLAYMODE": // Must run on main thread to set isPlaying
                case "EXIT_PLAYMODE":  // Must run on main thread to set isPlaying
                case "GET_PLAYMODE_STATE": // Must read isPlaying on main thread
                case "INSPECT_HIERARCHY":  // Must access GameObject hierarchy on main thread
                case "INSPECT_GAMEOBJECT": // Must access GameObject components on main thread
                case "CAPTURE_SCREENSHOT": // Must access Camera/SceneView on main thread
                case "SIMULATE_CLICK":     // Must access EventSystem on main thread
                case "SIMULATE_CLICK_AT":  // Must access EventSystem/GraphicRaycaster on main thread
                case "SIMULATE_DRAG":      // Must access EventSystem on main thread
                case "GET_CONSOLE_LOG":    // Must access LogEntries via reflection on main thread
                    return true;
                case "RUN_TESTS":       // TestRunnerApi MUST run on main thread
                case "CALL_METHOD":     // Reflection invocation MUST run on main thread
                default:
                    return true;
            }
        }

        /// <summary>
        /// Route incoming JSON command to the appropriate handler.
        /// </summary>
        private static string ProcessCommand(string json)
        {
            // Minimal JSON parsing without external dependencies
            string cmd = ExtractJsonString(json, "cmd");
            if (string.IsNullOrEmpty(cmd))
            {
                SafeLog("[BridgeServer] MCP call received with missing 'cmd' field", true);
                return MakeError("Missing 'cmd' field");
            }

            SafeLog("[BridgeServer] 鈫?MCP call: " + cmd.ToUpper());

            string response;
            switch (cmd.ToUpper())
            {
                case "PING":
                    // Include PID so EXE can precisely identify this Unity instance's window
                    // (critical when multiple Unity editors are running)
                    int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                    // Use cached project root (Application.dataPath requires main thread; _cachedProjectRoot is set in static ctor).
                    string projectPath = _cachedProjectRoot ?? "(unknown)";
                    response = "{\"status\":\"ok\",\"message\":\"" + EscapeJson(Application.unityVersion) + "\",\"pid\":" + pid + ",\"projectPath\":\"" + EscapeJson(projectPath) + "\"}";
                    SafeLog("[BridgeServer] ← PING response: ok (" + Application.unityVersion + ", PID=" + pid + ", project=" + projectPath + ")");
                    return response;
                case "RUN_TESTS":
                    response = HandleRunTests(json);
                    Debug.Log("[BridgeServer] 鈫?RUN_TESTS response: " + (response.Contains("\"ok\"") ? "started" : "error"));
                    return response;

                case "GET_TEST_RESULTS":
                    if (_testsRunning)
                    {
                        double elapsed = (DateTime.UtcNow - _testsStartTime).TotalSeconds;
                        if (elapsed > TEST_TIMEOUT_SECONDS)
                        {
                            _testsRunning = false;
                            _lastTestResults = "{\"passed\":0,\"failed\":0,\"skipped\":0,\"error\":\"Test run timed out or RunFailed after " + (int)elapsed + "s\"}";
                            Debug.LogWarning("[BridgeServer] Test run auto-reset after " + (int)elapsed + "s (likely RunFailed)");
                        }
                        else
                        {
                            response = MakeOkData("{\"status\":\"running\"}");
                            SafeLog("[BridgeServer] ← GET_TEST_RESULTS response sent (running=True)");
                            return response;
                        }
                    }
                    response = MakeOkData(_lastTestResults ?? "{\"status\":\"no_results\"}");
                    SafeLog("[BridgeServer] ← GET_TEST_RESULTS response sent (running=" + _testsRunning + ")");
                    return response;

                case "GET_COMPILE_ERRORS":
                    response = HandleGetCompileErrors();
                    SafeLog("[BridgeServer] ← GET_COMPILE_ERRORS response sent");
                    return response;

                case "GET_COMPILE_STATUS":
                    response = HandleGetCompileStatus();
                    SafeLog("[BridgeServer] ← GET_COMPILE_STATUS response sent");
                    return response;

                case "CALL_METHOD":
                    string typeName = ExtractJsonString(json, "typeName");
                    string methodName = ExtractJsonString(json, "methodName");
                    Debug.Log("[BridgeServer]   target: " + typeName + "." + methodName + "()");
                    response = HandleCallMethod(json);
                    Debug.Log("[BridgeServer] 鈫?CALL_METHOD response: " + (response.Contains("\"ok\"") ? "ok" : "error"));
                    return response;

                case "REFRESH_ASSETS":
                    // Fire-and-forget: trigger refresh via multiple strategies, return immediately.
                    // The EXE side polls Library/compile_state.txt written by CompilationPipeline
                    // callbacks to detect compilation state 鈥?no need to wait here.
                    _refreshCompleted = false;
                    _isCompiling = false;
                    _isUpdating = false;
                    _refreshRequested = true;

                    // Write "refresh_requested" state file so EXE knows we received the request
                    WriteCompileState("refresh_requested");
                    SafeLog("[BridgeServer] 鈫?REFRESH_ASSETS: flag set + state file written");

                    // Strategy 1: RunOnMainThread (SynchronizationContext.Post + delayCall)
                    RunOnMainThread(() =>
                    {
                        // If CheckRefreshRequest already handled it, skip
                        if (!_refreshRequested && _refreshCompleted) return;
                        _refreshRequested = false;
                        DoRefreshAndWriteState("REFRESH_ASSETS:RunOnMainThread");
                    });

                    // Strategy 2: QueuePlayerLoopUpdate nudge
                    try { EditorApplication.QueuePlayerLoopUpdate(); } catch { }

                    // Strategy 3: Background nudge thread.
                    // Sends a LIMITED number of nudges to wake up the main thread,
                    // then stops. Previous version flooded SynchronizationContext.Post
                    // every 500ms for 30s, which caused Unity to freeze on
                    // "UnitySynchronization.ExecuteTasks" for the entire duration.
                    //
                    // New approach: only nudge via QueuePlayerLoopUpdate (lightweight),
                    // and only Post to SynchronizationContext ONCE if refresh hasn't
                    // started after 5 seconds. Max 10 nudges over 30s.
                    new Thread(() =>
                    {
                        bool hasPostedRescue = false;
                        int[] nudgeDelays = { 1000, 2000, 3000, 3000, 3000, 3000, 3000, 3000, 3000, 3000 }; // 10 nudges, ~28s total
                        for (int i = 0; i < nudgeDelays.Length; i++)
                        {
                            Thread.Sleep(nudgeDelays[i]);
                            if (_refreshCompleted) break;

                            // Lightweight nudge: only QueuePlayerLoopUpdate (no main thread blocking)
                            try { EditorApplication.QueuePlayerLoopUpdate(); } catch { }

                            // One-shot rescue: if refresh still hasn't been picked up after 5s,
                            // post ONE SynchronizationContext callback as a last resort.
                            if (!hasPostedRescue && i >= 2 && _refreshRequested && _mainThreadContext != null)
                            {
                                hasPostedRescue = true;
                                try
                                {
                                    _mainThreadContext.Post(_ =>
                                    {
                                        if (_refreshCompleted) return;
                                        if (_refreshRequested)
                                        {
                                            _refreshRequested = false;
                                            DoRefreshAndWriteState("REFRESH_ASSETS:NudgeRescue");
                                        }
                                    }, null);
                                }
                                catch { }
                            }
                        }
                    }) { IsBackground = true, Name = "RefreshNudge" }.Start();

                    // Return immediately 鈥?EXE will poll compile_state.txt
                    response = "{\"status\":\"ok\",\"message\":\"REFRESH_ASSETS requested\"" +
                        ",\"compileStateFile\":\"" + EscapeJson(CompileStateFilePath) + "\"" +
                        ",\"fireAndForget\":true}";
                    SafeLog("[BridgeServer] 鈫?REFRESH_ASSETS: returned immediately, EXE polls compile_state.txt");
                    return response;

                case "GET_LOG_PATH":
                    string logPath = Application.consoleLogPath;
                    response = "{\"status\":\"ok\",\"logPath\":\"" + EscapeJson(logPath) + "\"}";
                    SafeLog("[BridgeServer] 鈫?GET_LOG_PATH: " + logPath);
                    return response;

                case "OPEN_SCENE":
                {
                    string scenePath = ExtractJsonString(json, "scenePath");
                    if (string.IsNullOrEmpty(scenePath))
                    {
                        response = MakeError("Missing 'scenePath'");
                        return response;
                    }
                    try
                    {
                        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
                        response = MakeOkData("{\"opened\":\"" + EscapeJson(scenePath) + "\"}");
                    }
                    catch (System.Exception ex)
                    {
                        response = MakeError("OpenScene failed: " + ex.Message);
                    }
                    return response;
                }

                case "ENTER_PLAYMODE":
                    return HandleEnterPlayMode();

                case "EXIT_PLAYMODE":
                    return HandleExitPlayMode();

                case "GET_PLAYMODE_STATE":
                    return HandleGetPlayModeState();

                case "INSPECT_HIERARCHY":
                    response = HandleInspectHierarchy(json);
                    SafeLog("[BridgeServer] <- INSPECT_HIERARCHY response sent");
                    return response;

                case "INSPECT_GAMEOBJECT":
                    string goPath = ExtractJsonString(json, "path");
                    SafeLog("[BridgeServer]   target: " + (goPath ?? "(root)"));
                    response = HandleInspectGameObject(json);
                    SafeLog("[BridgeServer] <- INSPECT_GAMEOBJECT response sent");
                    return response;

                case "CAPTURE_SCREENSHOT":
                    response = HandleCaptureScreenshot(json);
                    SafeLog("[BridgeServer] <- CAPTURE_SCREENSHOT response sent");
                    return response;

                case "SIMULATE_CLICK":
                    response = HandleSimulateClick(json);
                    SafeLog("[BridgeServer] <- SIMULATE_CLICK response sent");
                    return response;

                case "SIMULATE_CLICK_AT":
                    response = HandleSimulateClickAt(json);
                    SafeLog("[BridgeServer] <- SIMULATE_CLICK_AT response sent");
                    return response;

                case "SIMULATE_DRAG":
                    response = HandleSimulateDrag(json);
                    SafeLog("[BridgeServer] <- SIMULATE_DRAG response sent");
                    return response;

                case "GET_CONSOLE_LOG":
                    response = HandleGetConsoleLog(json);
                    SafeLog("[BridgeServer] <- GET_CONSOLE_LOG response sent");
                    return response;

                case "LIST_CALLABLE":
                    response = HandleListCallable(json);
                    SafeLog("[BridgeServer] <- LIST_CALLABLE response sent (" + response.Length + " chars)");
                    return response;

                default:
                    SafeLog("[BridgeServer] 鈫?Unknown command: " + cmd, true);
                    return MakeError("Unknown command: " + cmd);
            }
        }

        /// <summary>
        /// Thread-safe logging. When called from background thread, uses
        /// EditorApplication.delayCall to marshal to main thread.
        /// Falls back to System.IO file logging if Unity API unavailable.
        /// </summary>
        private static void SafeLog(string message, bool isWarning = false)
        {
            // Check if we're on main thread by trying Thread.CurrentThread.ManagedThreadId
            // Unity's main thread is typically id=1, but this isn't guaranteed.
            // Instead, we use a try/catch approach: if Debug.Log works, we're on main thread.
            try
            {
                if (isWarning)
                    Debug.LogWarning(message);
                else
                    Debug.Log(message);
            }
            catch
            {
                // Background thread 鈥?schedule on main thread
                string msg = message;
                bool warn = isWarning;
                EditorApplication.delayCall += () =>
                {
                    if (warn)
                        Debug.LogWarning(msg);
                    else
                        Debug.Log(msg);
                };
            }
        }

        #region Compile Errors

        /// <summary>
        /// Handle GET_COMPILE_ERRORS command.
        /// Returns the cached compile errors/warnings collected from last compilation.
        /// Thread-safe: reads from _lastCompileErrors/Warnings with lock.
        /// </summary>
        /// <summary>
        /// Handle GET_COMPILE_ERRORS command.
        /// Returns the cached compile errors/warnings collected from last compilation.
        /// Thread-safe: reads from _lastCompileErrors/Warnings with lock.
        /// </summary>
        private static string HandleGetCompileErrors()
        {
            List<CompilerMessage> errors;
            List<CompilerMessage> warnings;

            lock (_compileMessagesLock)
            {
                errors = new List<CompilerMessage>(_lastCompileErrors);
                warnings = new List<CompilerMessage>(_lastCompileWarnings);
            }

            var errorsJson = new List<string>();
            foreach (var err in errors)
            {
                errorsJson.Add(FormatCompilerMessage(err));
            }

            var warningsJson = new List<string>();
            foreach (var warn in warnings)
            {
                warningsJson.Add(FormatCompilerMessage(warn));
            }

            string json = "{" +
                "\"errorCount\":" + errors.Count + "," +
                "\"warningCount\":" + warnings.Count + "," +
                "\"errors\":[" + string.Join(",", errorsJson.ToArray()) + "]," +
                "\"warnings\":[" + string.Join(",", warningsJson.ToArray()) + "]" +
                "}";

            return MakeOkData(json);
        }

        /// <summary>
        /// Handle GET_COMPILE_STATUS command.
        /// Returns metadata about the last compilation:
        /// - lastState: "success" or "error"
        /// - lastStartTime: ISO 8601 timestamp when compilation started
        /// - errorCount/warningCount: counts from last compilation
        /// Thread-safe: reads from _lastCompilationState/Time with lock.
        /// 
        /// This allows external tools (like unity-bridge.exe) to make smart decisions:
        /// - If lastState is "success", expect fast recompilation (incremental)
        /// - If lastState is "error", Unity may not trigger full compilation pipeline
        /// - Use lastStartTime to calculate how long ago last compilation was
        /// </summary>
        private static string HandleGetCompileStatus()
        {
            string state;
            string startTime;
            int errorCount;
            int warningCount;

            lock (_compileMessagesLock)
            {
                state = _lastCompilationState;
                startTime = _lastCompilationStartTime.ToString("o"); // ISO 8601
                errorCount = _lastCompileErrors.Count;
                warningCount = _lastCompileWarnings.Count;
            }

            string json = "{" +
                "\"lastState\":\"" + state + "\"," +
                "\"lastStartTime\":\"" + startTime + "\"," +
                "\"errorCount\":" + errorCount + "," +
                "\"warningCount\":" + warningCount +
                "}";

            return MakeOkData(json);
        }

        /// <summary>
        /// Format a CompilerMessage as JSON.
        /// Returns: "file":"path","line":123,"column":45,"message":"text"
        /// </summary>
        private static string FormatCompilerMessage(CompilerMessage msg)
        {
            return "{" +
                "\"file\":\"" + EscapeJson(msg.file ?? "") + "\"," +
                "\"line\":" + msg.line + "," +
                "\"column\":" + msg.column + "," +
                "\"message\":\"" + EscapeJson(msg.message ?? "") + "\"" +
                "}";
        }

        #endregion

        #region Test Runner

        // Track whether tests are currently running (for GET_TEST_RESULTS status)
        private static volatile bool _testsRunning;
        private static DateTime _testsStartTime = DateTime.MinValue;
        private const int TEST_TIMEOUT_SECONDS = 120;

        private static string HandleRunTests(string json)
        {
            string filter = ExtractJsonString(json, "filter") ?? "";

            try
            {
                // ── 场景未保存时的静默处理 ─────────────────────────────────
                // Unity EditMode 测试在执行前，某些测试会隐式切换/重载场景。
                // 如果当前场景是 dirty 的（未保存），会弹出 "Save Modifications?" modal
                // 对话框，阻塞主线程 → bridge pipe 超时 → AI 侧看到 "tests_started" 但
                // 永远拿不到结果。
                //
                // 策略：
                //   1) 若场景已保存过（有资源路径）→ 静默保存；
                //   2) 若场景从未保存过（untitled，没路径）→ 用 SaveCurrentModifiedScenesIfUserWantsTo(false)
                //      放弃修改；这样 TestRunner 后续切场景时不会再弹窗。
                // 任何异常都降级为 warning，继续跑测试（保留原始行为）。
                try
                {
                    PrepareScenesForRunTests();
                }
                catch (Exception prepEx)
                {
                    Debug.LogWarning("[BridgeServer] PrepareScenesForRunTests failed (continuing): " + prepEx.Message);
                }

                var testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
                var testFilter = new Filter
                {
                    testMode = TestMode.EditMode
                };

                if (!string.IsNullOrEmpty(filter))
                {
                    testFilter.groupNames = new[] { filter };
                }

                // IMPORTANT: Do NOT block the main thread waiting for test results!
                // RunFinished callback fires on the main thread. If we spin-wait here
                // (on the main thread), RunFinished can never execute → deadlock → Unity freezes.
                //
                // Instead: fire-and-forget. Return immediately with "tests_started".
                // The MCP client polls GET_TEST_RESULTS to retrieve results when done.
                _testsRunning = true;
                _testsStartTime = DateTime.UtcNow;
                _lastTestResults = "{\"status\":\"running\"}";

                var callbacks = new BridgeTestCallbacks(null);
                testRunnerApi.RegisterCallbacks(callbacks);
                testRunnerApi.Execute(new ExecutionSettings(testFilter));

                Debug.Log("[BridgeServer] Tests started (non-blocking). Poll GET_TEST_RESULTS for results.");

                return MakeOk("tests_started. Use get_test_results to poll for completion.");
            }
            catch (Exception ex)
            {
                _testsRunning = false;
                return MakeError("Failed to start tests: " + ex.Message);
            }
        }

        /// <summary>
        /// 在 run_tests 执行前静默处理场景未保存状态，防止 TestRunner 弹出
        /// "Save Modifications?" 对话框阻塞主线程。
        ///
        /// 行为（按顺序执行）：
        ///   1. **第一道防线**：无条件把共享测试场景 PluginTestScene.unity 从 Editor 里
        ///      Close 掉（不管它是否 dirty）。因为 4 个 Unity 工程通过软链共享同一份
        ///      磁盘文件，任何一个工程的 SaveOpenScenes 都会写盘，互相覆盖并触发
        ///      "modified externally" 弹窗。PluginTestScene 的生命周期由 Testbed 流程
        ///      （PlayModeTestRunner）全权负责，对 run_tests 而言是"无副作用可丢"状态。
        ///   2. 对剩余的每个已加载且 dirty 的场景：
        ///      · 若该场景有磁盘路径 → 调 EditorSceneManager.SaveScene 静默保存；
        ///      · 否则（untitled scene） → 通过保存到临时路径再删除的方式 clear dirty flag，
        ///        因 Unity 公开 API 无法"无弹窗丢弃 untitled 修改"。
        ///   3. 最终调 SaveOpenScenes 再保一次底（会跳过已不 dirty 的场景）。
        ///      PluginTestScene 已在步骤 1 被 Close，不会进入这里的 save 路径。
        ///
        /// 任何异常由调用方捕获，降级为 warning。
        /// </summary>
        private static void PrepareScenesForRunTests()
        {
            // PluginTestScene 在多项目软链共享场景下禁止 SaveScene / SaveOpenScenes。
            // 用它的规范路径做识别。
            const string PluginTestScenePath = "Assets/ResDep/AIGen/_GameUI/Scenes/PluginTestScene.unity";

            // ================================================================
            // 步骤 1：无条件 Close PluginTestScene（dirty or not）
            // ================================================================
            // 即便场景不 dirty，SaveOpenScenes 理论上不会写它；但 Unity 内部在某些
            // AssetDatabase.Refresh / import 事件后可能自发把场景标 dirty（尤其是
            // 场景里引用的 prefab 被软链对端工程重写的时候）。在 run_tests 这种
            // 无 UI 确认窗口的流程里，任何一次意外写盘都会污染其他工程的工作区。
            // 所以我们采取最保守策略：只要它被打开了，就先关掉。
            TryCloseSharedScene(PluginTestScenePath);

            // ================================================================
            // 步骤 2：扫描剩余 dirty 场景并静默处理
            // ================================================================
            // 重新获取 sceneCount（步骤 1 可能已经改动了场景列表）
            int sceneCount = UnityEditor.SceneManagement.EditorSceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = UnityEditor.SceneManagement.EditorSceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                if (!scene.isDirty) continue;

                // 规范化路径（scene.path 在 Unity 里已是正斜杠，但防御性 Replace 一下）
                var normalizedPath = string.IsNullOrEmpty(scene.path) ? "" : scene.path.Replace('\\', '/');

                // 双保险：万一 TryCloseSharedScene 失败了（被 Unity 拒绝关闭等），这里再拦一次
                if (string.Equals(normalizedPath, PluginTestScenePath, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning("[BridgeServer] run_tests preflight: PluginTestScene still loaded & dirty after TryCloseSharedScene. Skipping save to avoid multi-project symlink corruption.");
                    continue;
                }

                if (!string.IsNullOrEmpty(scene.path))
                {
                    // 场景有路径：直接静默 Save
                    bool ok = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                    Debug.Log("[BridgeServer] run_tests preflight: saved dirty scene '" + scene.path + "' => " + ok);
                }
                else
                {
                    // untitled 场景：存到临时路径再删，既 clear dirty flag 又不留垃圾
                    string tmp = "Assets/_BridgeServerTempScene_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".unity";
                    try
                    {
                        bool savedTmp = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, tmp);
                        Debug.Log("[BridgeServer] run_tests preflight: saved untitled scene to temp '" + tmp + "' => " + savedTmp);
                        if (savedTmp)
                        {
                            // 立刻删除临时场景文件，保持 Assets 干净
                            AssetDatabase.DeleteAsset(tmp);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[BridgeServer] run_tests preflight: failed to save untitled scene: " + ex.Message);
                    }
                }
            }

            // ================================================================
            // 步骤 3：兜底 SaveOpenScenes
            // ================================================================
            // 统一保存一次所有已打开场景（无弹窗）。
            // 注意：PluginTestScene 已在步骤 1 被 CloseScene 移除，不会进入这里的 save 路径。
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        }

        /// <summary>
        /// 把指定路径的场景从 Editor 中 Close 掉（无论是否 dirty，直接丢弃修改）。
        /// 用于多项目软链场景保护：PluginTestScene.unity 被 4 个 Unity 工程通过符号
        /// 链接共享磁盘文件，任何一次 SaveOpenScenes / SaveScene 都会覆盖对端工程
        /// 的工作结果。run_tests 预处理、PlayMode 切换等关键节点必须先 Close 再继续。
        ///
        /// 策略：
        /// - 找不到匹配场景 → 静默 no-op（这是常态，不应产生日志噪音）。
        /// - 匹配到且还有其他 loaded scene → 直接 CloseScene(scene, removeScene:true)。
        /// - 匹配到且是唯一 loaded scene → 先 NewScene(Empty, Additive) 占位，再 Close。
        ///   Unity 禁止把 Editor 置于"零场景"状态，必须留一个活场景。
        /// </summary>
        /// <param name="scenePath">Assets/ 开头的正斜杠路径</param>
        /// <returns>true = 真的执行了 Close；false = 场景没被打开，或操作失败</returns>
        private static bool TryCloseSharedScene(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return false;

            int sceneCount = UnityEditor.SceneManagement.EditorSceneManager.sceneCount;
            UnityEngine.SceneManagement.Scene? target = null;
            int loadedCount = 0;

            for (int i = 0; i < sceneCount; i++)
            {
                var s = UnityEditor.SceneManagement.EditorSceneManager.GetSceneAt(i);
                if (!s.isLoaded) continue;
                loadedCount++;
                var normalizedPath = string.IsNullOrEmpty(s.path) ? "" : s.path.Replace('\\', '/');
                if (string.Equals(normalizedPath, scenePath, StringComparison.OrdinalIgnoreCase))
                {
                    target = s;
                }
            }

            if (!target.HasValue)
            {
                // 场景根本没打开 —— 常态，不产生日志
                return false;
            }

            try
            {
                if (loadedCount > 1)
                {
                    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(target.Value, true);
                }
                else
                {
                    // 唯一 loaded scene：先 additive 新建空场景占位，再 Close 目标
                    UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                        UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                        UnityEditor.SceneManagement.NewSceneMode.Additive);
                    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(target.Value, true);
                }
                Debug.Log("[BridgeServer] TryCloseSharedScene: closed '" + scenePath +
                          "' to protect multi-project symlinked file from accidental save.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BridgeServer] TryCloseSharedScene: failed to close '" + scenePath + "': " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Test runner callbacks that capture results for GET_TEST_RESULTS.
        /// </summary>
        private class BridgeTestCallbacks : ICallbacks
        {
            private int _passed;
            private int _failed;
            private int _skipped;
            private readonly List<string> _failures = new List<string>();
            private readonly ManualResetEvent _completionSignal;

            public BridgeTestCallbacks() : this(null) { }

            public BridgeTestCallbacks(ManualResetEvent completionSignal)
            {
                _completionSignal = completionSignal;
            }

            public void RunStarted(ITestAdaptor testsToRun) { }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.HasChildren) return;

                switch (result.TestStatus)
                {
                    case TestStatus.Passed:
                        _passed++;
                        break;
                    case TestStatus.Failed:
                        _failed++;
                        _failures.Add(result.FullName + ": " + result.Message);
                        break;
                    case TestStatus.Skipped:
                    case TestStatus.Inconclusive:
                        _skipped++;
                        break;
                }
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                // Build JSON result
                var sb = new StringBuilder();
                sb.Append("{\"passed\":").Append(_passed);
                sb.Append(",\"failed\":").Append(_failed);
                sb.Append(",\"skipped\":").Append(_skipped);
                if (_failures.Count > 0)
                {
                    sb.Append(",\"failures\":[");
                    for (int i = 0; i < _failures.Count && i < 20; i++)
                    {
                        if (i > 0) sb.Append(",");
                        sb.Append("\"").Append(EscapeJson(_failures[i])).Append("\"");
                    }
                    sb.Append("]");
                }
                sb.Append("}");
                _lastTestResults = sb.ToString();
                _testsRunning = false;
                Debug.Log("[BridgeServer] Tests complete: " + _lastTestResults);

                // Signal completion (kept for potential future use)
                if (_completionSignal != null)
                    _completionSignal.Set();
            }
        }

        #endregion

        #region Call Method (Whitelist Enforcement)

        private static string HandleCallMethod(string json)
        {
            string typeName = ExtractJsonString(json, "typeName");
            string methodName = ExtractJsonString(json, "methodName");
            string rawArgs = ExtractJsonString(json, "args") ?? "";

            if (string.IsNullOrEmpty(typeName))
                return MakeError("Missing 'typeName'");
            if (string.IsNullOrEmpty(methodName))
                return MakeError("Missing 'methodName'");

            // Find the type across all loaded assemblies
            Type targetType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                targetType = asm.GetType(typeName, false);
                if (targetType != null) break;
            }

            if (targetType == null)
                return MakeError("Type not found: " + typeName);

            // Three-layer whitelist check
            if (!IsCallAllowed(targetType, methodName, out string rejectReason))
                return MakeError(rejectReason);

            // Parse args into Dictionary<string, string>
            var argDict = ParseArgsString(rawArgs);

            // Invoke the method with matched arguments
            try
            {
                var method = targetType.GetMethod(methodName,
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

                if (method == null)
                    return MakeError("Method not found: " + methodName);

                object[] invokeArgs = BuildInvokeArguments(method, argDict);
                object result = method.Invoke(null, invokeArgs);
                string resultStr = result?.ToString() ?? "null";
                return MakeOkData("\"" + EscapeJson(resultStr) + "\"");
            }
            catch (TargetInvocationException ex)
            {
                return MakeError("Method threw exception: " +
                    (ex.InnerException?.Message ?? ex.Message));
            }
            catch (Exception ex)
            {
                return MakeError("Invocation failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Parse key=value&amp;key2=value2 format args string.
        /// Values are URL-decoded. Empty string returns empty dict.
        /// </summary>
        static Dictionary<string, string> ParseArgsString(string raw)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(raw)) return dict;

            foreach (var pair in raw.Split('&'))
            {
                int eqIdx = pair.IndexOf('=');
                if (eqIdx <= 0) continue;
                string key = pair.Substring(0, eqIdx);
                string val = pair.Substring(eqIdx + 1);
                dict[key] = Uri.UnescapeDataString(val);
            }
            return dict;
        }

        /// <summary>
        /// Match parsed arg values to method parameters by name (case-insensitive).
        /// Supports: string, int, float, bool, double, long. Missing optional params get default/null.
        /// </summary>
        static object[] BuildInvokeArguments(MethodInfo method, Dictionary<string, string> argDict)
        {
            var parameters = method.GetParameters();
            var args = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                string valStr;
                bool hasValue = argDict.TryGetValue(param.Name, out valStr);

                // 兼容旧 TCP invoke：PC 侧位置参数会被 MCP 透传层转成 arg0/arg1/arg2。
                // 新 callMethod 仍优先按形参名匹配；只有形参名缺失时才回退到位置参数。
                if (!hasValue)
                {
                    hasValue = argDict.TryGetValue("arg" + i, out valStr);
                }

                if (!hasValue || string.IsNullOrEmpty(valStr))
                {
                    // No value provided — use Type.Default or null for reference types
                    args[i] = (param.ParameterType.IsValueType)
                        ? Activator.CreateInstance(param.ParameterType)
                        : null;
                    continue;
                }

                args[i] = ConvertValue(valStr, param.ParameterType);
            }

            return args;
        }

        /// <summary>
        /// Convert a string value to the target parameter type.
        /// </summary>
        static object ConvertValue(string value, Type targetType)
        {
            if (targetType == typeof(string)) return value;
            if (targetType == typeof(int)) return int.Parse(value);
            if (targetType == typeof(float)) return float.Parse(value);
            if (targetType == typeof(bool)) return bool.Parse(value);
            if (targetType == typeof(double)) return double.Parse(value);
            if (targetType == typeof(long)) return long.Parse(value);
            if (targetType == typeof(int[])) return ParseIntArray(value);
            if (targetType == typeof(string[])) return ParseStringArray(value);
            // Fallback: attempt ChangeType
            return System.Convert.ChangeType(value, targetType);
        }

        static int[] ParseIntArray(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new int[0];
            string trimmed = value.Trim();
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                trimmed = trimmed.Substring(1, trimmed.Length - 2);
            if (string.IsNullOrWhiteSpace(trimmed)) return new int[0];

            string[] parts = trimmed.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            int[] result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim().Trim('"');
                result[i] = int.Parse(part);
            }
            return result;
        }

        static string[] ParseStringArray(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new string[0];
            string trimmed = value.Trim();
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                trimmed = trimmed.Substring(1, trimmed.Length - 2);
            if (string.IsNullOrWhiteSpace(trimmed)) return new string[0];

            string[] parts = trimmed.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = parts[i].Trim().Trim('"');
            }
            return parts;
        }

        /// <summary>
        /// Security check for call_method.
        /// Only requires [AICallable] attribute — if a developer explicitly marks a method,
        /// it's allowed regardless of namespace or assembly.
        /// </summary>
        static bool IsCallAllowed(Type type, string methodName, out string rejectReason)
        {
            rejectReason = null;

            // Find the method
            var method = type.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                rejectReason = "Public static method not found: " +
                    type.FullName + "." + methodName;
                return false;
            }

            if (method.IsDefined(typeof(AICallableAttribute), false))
                return true;

            string shortKey = type.Name + "." + methodName;
            string fullKey = type.FullName + "." + methodName;
            if (LegacyCallableMethods.Contains(shortKey) || LegacyCallableMethods.Contains(fullKey))
            {
                Debug.Log("[BridgeServer] Legacy callable allowed: " + fullKey);
                return true;
            }

            rejectReason = "Method " + type.FullName + "." + methodName +
                " is not marked with [AICallable] and is not in the legacy MCP allowlist. " +
                "Only [AICallable] methods or explicitly allowlisted legacy tools can be called via Bridge.";
            return false;
        }

        /// <summary>
        /// Scan all loaded assemblies for public static methods marked with [AICallable].
        /// Three modes (selected by JSON 'mode' field):
        ///   - "categories" : list distinct Categories with method counts (~500 token, default for AI discovery)
        ///   - "category"   : list methods in a specific Category (~600 token per category)
        ///   - "all"        : flat dump of every method (legacy behavior, ~10000 token, debug only)
        ///
        /// When 'mode' is missing, defaults to "all" for backwards compatibility with earlier
        /// HandleListCallable callers (vulcan-bridge.exe versions before list_callable lazy-load shipped).
        /// </summary>
        private static string HandleListCallable(string json)
        {
            try
            {
                string mode = ExtractJsonString(json, "mode");
                string filterCategory = ExtractJsonString(json, "category");
                if (string.IsNullOrEmpty(mode)) mode = "all"; // legacy default

                var entries = ScanAllCallables();

                switch (mode)
                {
                    case "categories":
                        return BuildCategoriesList(entries);
                    case "category":
                        if (string.IsNullOrEmpty(filterCategory))
                            return MakeError("LIST_CALLABLE mode=category requires 'category' field");
                        return BuildCategoryDetail(entries, filterCategory);
                    case "all":
                    default:
                        return BuildFlatList(entries);
                }
            }
            catch (Exception ex)
            {
                return MakeError("LIST_CALLABLE failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>One scanned [AICallable] method entry (with derived Category).</summary>
        private struct CallableEntry
        {
            public string TypeFullName;
            public string MethodName;
            public string Description;
            public string ParamSignature;   // "name:Type, name2:Type2"
            public string Category;          // explicit attr.Category or derived from TypeFullName
            public ToolKind Kind;            // explicit attr.Kind (default Read)
        }

        /// <summary>
        /// Reflect across all non-system assemblies and collect every [AICallable] method,
        /// resolving each entry's Category (explicit overrides auto-derived).
        /// Pure read — safe to call from background pipe thread.
        /// </summary>
        private static List<CallableEntry> ScanAllCallables()
        {
            var result = new List<CallableEntry>();
            string[] stripPrefixes = CategoryStripPrefixes ?? AllowedNamespacePrefixes;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Skip system assemblies for performance (same filter as legacy code)
                string asmName = assembly.GetName().Name;
                if (asmName.StartsWith("System") || asmName.StartsWith("Microsoft") ||
                    asmName.StartsWith("Mono") || asmName.StartsWith("mscorlib") ||
                    asmName.StartsWith("Unity.") || asmName.StartsWith("UnityEngine") ||
                    asmName.StartsWith("UnityEditor") || asmName.StartsWith("netstandard") ||
                    asmName.StartsWith("nunit") || asmName.StartsWith("Newtonsoft"))
                    continue;

                Type[] types;
                try { types = assembly.GetTypes(); }
                catch { continue; }

                foreach (var type in types)
                {
                    MethodInfo[] methods;
                    try { methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static); }
                    catch { continue; }

                    foreach (var method in methods)
                    {
                        var attr = Attribute.GetCustomAttribute(method, typeof(AICallableAttribute), false)
                                   as AICallableAttribute;
                        if (attr == null) continue;

                        // Build parameter signature
                        var parameters = method.GetParameters();
                        string paramStr = "";
                        if (parameters.Length > 0)
                        {
                            var paramParts = new string[parameters.Length];
                            for (int i = 0; i < parameters.Length; i++)
                            {
                                var p = parameters[i];
                                paramParts[i] = p.Name + ":" + p.ParameterType.Name;
                            }
                            paramStr = string.Join(", ", paramParts);
                        }

                        // Resolve Category: explicit attr.Category > auto-derived
                        string category = !string.IsNullOrEmpty(attr.Category)
                            ? attr.Category
                            : DeriveCategoryFromTypeName(type.FullName, stripPrefixes);

                        result.Add(new CallableEntry
                        {
                            TypeFullName   = type.FullName,
                            MethodName     = method.Name,
                            Description    = attr.Description ?? "",
                            ParamSignature = paramStr,
                            Category       = category,
                            Kind           = attr.Kind,
                        });
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Auto-derive Category from a Type's FullName by stripping the longest matching prefix.
        /// Falls back to the full FullName when no prefix matches (so methods are never "lost").
        /// Method-level granularity is intentionally dropped — Category is a class-level grouping.
        /// </summary>
        private static string DeriveCategoryFromTypeName(string typeFullName, string[] stripPrefixes)
        {
            if (string.IsNullOrEmpty(typeFullName)) return "Uncategorized";
            if (stripPrefixes == null || stripPrefixes.Length == 0) return typeFullName;

            // Longest-prefix match (ordinal, case-sensitive)
            string longest = "";
            foreach (var p in stripPrefixes)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (typeFullName.StartsWith(p, StringComparison.Ordinal) && p.Length > longest.Length)
                    longest = p;
            }

            return longest.Length > 0
                ? typeFullName.Substring(longest.Length)
                : typeFullName;
        }

        /// <summary>
        /// mode=categories : "Category(N methods)\n..." plus header line.
        /// Wraps the markdown text into pipe protocol envelope.
        /// </summary>
        private static string BuildCategoriesList(List<CallableEntry> entries)
        {
            var sb = new StringBuilder();
            sb.Append("Available [AICallable] categories ");
            sb.Append("(call list_callable(category=<name>) for method details):\n\n");

            // Group by Category, alphabetical. Track total + R/W counts per category.
            var totalCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var readCounts  = new Dictionary<string, int>(StringComparer.Ordinal);
            var writeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            int totalR = 0, totalW = 0;
            foreach (var e in entries)
            {
                if (!totalCounts.ContainsKey(e.Category))
                {
                    totalCounts[e.Category] = 0;
                    readCounts[e.Category]  = 0;
                    writeCounts[e.Category] = 0;
                }
                totalCounts[e.Category]++;
                if (e.Kind == ToolKind.Write) { writeCounts[e.Category]++; totalW++; }
                else                          { readCounts[e.Category]++;  totalR++; }
            }
            var keys = new List<string>(totalCounts.Keys);
            keys.Sort(StringComparer.Ordinal);

            foreach (var k in keys)
            {
                sb.Append("- ").Append(k).Append("  (").Append(totalCounts[k]).Append(" methods, ")
                  .Append(readCounts[k]).Append("R+").Append(writeCounts[k]).Append("W)\n");
            }

            sb.Append("\n").Append(keys.Count).Append(" categories, ")
              .Append(entries.Count).Append(" methods total (")
              .Append(totalR).Append("R+").Append(totalW).Append("W).\n");
            sb.Append("\nLegend: [R]=read-only or PlayMode-transient; [W]=writes to disk (code/config/assets/scenes).\n");

            return WrapAsPipeData(sb.ToString());
        }

        /// <summary>
        /// mode=category : detailed listing of all methods in the requested Category.
        /// Returns error envelope when category doesn't exist (case-sensitive match).
        /// </summary>
        private static string BuildCategoryDetail(List<CallableEntry> entries, string category)
        {
            var matched = entries.FindAll(e =>
                string.Equals(e.Category, category, StringComparison.Ordinal));
            if (matched.Count == 0)
                return MakeError("Category not found: " + category +
                                 ". Use list_callable() to see available categories.");

            // Compute R/W breakdown for header
            int rCount = 0, wCount = 0;
            foreach (var e in matched)
            {
                if (e.Kind == ToolKind.Write) wCount++;
                else rCount++;
            }

            var sb = new StringBuilder();
            sb.Append("[").Append(category).Append("] — ").Append(matched.Count)
              .Append(" methods (").Append(rCount).Append("R+").Append(wCount).Append("W)\n\n");

            // Sub-group by TypeFullName so multiple types under same Category are tidy
            var byType = new Dictionary<string, List<CallableEntry>>(StringComparer.Ordinal);
            foreach (var e in matched)
            {
                if (!byType.ContainsKey(e.TypeFullName))
                    byType[e.TypeFullName] = new List<CallableEntry>();
                byType[e.TypeFullName].Add(e);
            }
            var typeKeys = new List<string>(byType.Keys);
            typeKeys.Sort(StringComparer.Ordinal);

            foreach (var typeName in typeKeys)
            {
                sb.Append("type: ").Append(typeName).Append("\n\n");
                foreach (var e in byType[typeName])
                {
                    // [R]/[W] prefix so AI immediately sees side-effect risk
                    sb.Append("  - [").Append(e.Kind == ToolKind.Write ? "W" : "R").Append("] ")
                      .Append(e.MethodName);
                    if (!string.IsNullOrEmpty(e.ParamSignature))
                        sb.Append("(").Append(e.ParamSignature).Append(")");
                    if (!string.IsNullOrEmpty(e.Description))
                        sb.Append("\n    : ").Append(e.Description);
                    sb.Append("\n\n");
                }
            }

            sb.Append("Legend: [R]=read-only or PlayMode-transient; [W]=writes to disk.\n");
            sb.Append("Usage:\n");
            sb.Append("  call_method(type=\"<full type>\", method=\"<name>\", args=\"key=value&...\")\n");

            return WrapAsPipeData(sb.ToString());
        }

        /// <summary>
        /// mode=all : legacy flat JSON dump of every entry. Kept for debugging and
        /// for backwards compat with vulcan-bridge.exe versions that still read
        /// {"methods":[...]} directly. ~10000 token output — not recommended for AI.
        /// </summary>
        private static string BuildFlatList(List<CallableEntry> entries)
        {
            var sb = new StringBuilder();
            sb.Append("{\"status\":\"ok\",\"methods\":[");
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var e = entries[i];
                sb.Append("{\"type\":\"").Append(EscapeJson(e.TypeFullName));
                sb.Append("\",\"method\":\"").Append(EscapeJson(e.MethodName));
                sb.Append("\",\"description\":\"").Append(EscapeJson(e.Description));
                sb.Append("\",\"params\":\"").Append(EscapeJson(e.ParamSignature));
                sb.Append("\",\"category\":\"").Append(EscapeJson(e.Category));
                sb.Append("\",\"kind\":\"").Append(e.Kind == ToolKind.Write ? "Write" : "Read");
                sb.Append("\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>
        /// Wrap a plain markdown string into the pipe protocol envelope:
        ///   {"status":"ok","data":"<escaped markdown>"}
        /// Used by mode=categories and mode=category outputs.
        /// </summary>
        private static string WrapAsPipeData(string text)
        {
            return "{\"status\":\"ok\",\"data\":\"" + EscapeJson(text) + "\"}";
        }

        #endregion

        #region PlayMode Control

        private static string HandleEnterPlayMode()
        {
            // Runs on MAIN THREAD (RequiresMainThread = true).
            // EXE side calls EnsureUnityForeground() before sending this command,
            // so RunOnMainThread/DrainMainThreadQueue will execute reliably.
            if (EditorApplication.isPlaying)
            {
                Debug.Log("[BridgeServer] ENTER_PLAYMODE: Already in PlayMode.");
                return "{\"status\":\"ok\",\"message\":\"Already in PlayMode\",\"alreadyInState\":true}";
            }
            if (EditorApplication.isCompiling)
            {
                Debug.LogWarning("[BridgeServer] ENTER_PLAYMODE: Compiling, cannot enter PlayMode.");
                return MakeError("Cannot enter PlayMode while compiling");
            }
            Debug.Log("[BridgeServer] ENTER_PLAYMODE: Setting isPlaying = true on main thread.");
            EditorApplication.isPlaying = true;
            return "{\"status\":\"ok\",\"message\":\"PlayMode entry initiated\"}";
        }

        private static string HandleExitPlayMode()
        {
            // Runs on MAIN THREAD (RequiresMainThread = true).
            // EXE side calls EnsureUnityForeground() before sending this command.
            if (!EditorApplication.isPlaying)
            {
                Debug.Log("[BridgeServer] EXIT_PLAYMODE: Already in EditMode.");
                return "{\"status\":\"ok\",\"message\":\"Already in EditMode\",\"alreadyInState\":true}";
            }
            Debug.Log("[BridgeServer] EXIT_PLAYMODE: Setting isPlaying = false on main thread.");
            EditorApplication.isPlaying = false;
            return "{\"status\":\"ok\",\"message\":\"PlayMode exit initiated\"}";
        }

        private static string HandleGetPlayModeState()
        {
            // Runs on MAIN THREAD (RequiresMainThread = true).
            // EXE side calls EnsureUnityForeground() before sending this command.
            bool isPlaying = EditorApplication.isPlaying;
            bool isPaused = EditorApplication.isPaused;
            bool isCompiling = EditorApplication.isCompiling;
            Debug.Log("[BridgeServer] GET_PLAYMODE_STATE: isPlaying=" + isPlaying + ", isPaused=" + isPaused + ", isCompiling=" + isCompiling);
            return "{\"status\":\"ok\"" +
                ",\"isPlaying\":" + (isPlaying ? "true" : "false") +
                ",\"isPaused\":" + (isPaused ? "true" : "false") +
                ",\"isCompiling\":" + (isCompiling ? "true" : "false") + "}";
        }

        #endregion

        #region Hierarchy Inspection

        /// <summary>
        /// Inspect the Hierarchy tree. Returns a JSON tree of all root GameObjects
        /// (or children of a specified path) with name, active state, component list, and children count.
        /// Parameters:
        ///   path (optional): parent GO path to inspect children of (e.g. "Canvas/Panel")
        ///   depth (optional): max recursion depth (default 3, max 10)
        ///   scene (optional): scene name filter
        /// </summary>
        private static string HandleInspectHierarchy(string json)
        {
            string path = ExtractJsonString(json, "path");
            string depthStr = ExtractJsonString(json, "depth");
            string sceneName = ExtractJsonString(json, "scene");
            int maxDepth = 3;
            if (!string.IsNullOrEmpty(depthStr))
            {
                int parsed;
                if (int.TryParse(depthStr, out parsed) && parsed >= 1 && parsed <= 10)
                    maxDepth = parsed;
            }

            try
            {
                var sb = new StringBuilder();

                if (!string.IsNullOrEmpty(path))
                {
                    // Find specific GO and list its children
                    var go = GameObject.Find(path);
                    // GameObject.Find only finds active objects. Try hierarchy traversal for inactive ones.
                    if (go == null)
                        go = FindGameObjectByPath(path);

                    // UITK fallback: if no GameObject matches, try resolving as a VisualElement path.
                    if (go == null)
                    {
                        UITKHit hit;
                        if (TryFindUITK(path, out hit))
                        {
                            sb.Append("{\"status\":\"ok\",\"path\":\"").Append(EscapeJson(path)).Append("\"");
                            sb.Append(",\"framework\":\"uitk\"");
                            sb.Append(",\"docPath\":\"").Append(EscapeJson(hit.docPath ?? "")).Append("\"");
                            sb.Append(",\"vePath\":\"").Append(EscapeJson(hit.vePath ?? "")).Append("\"");
                            sb.Append(",\"tree\":");
                            SerializeVETree(hit.element, sb, 0, maxDepth);
                            sb.Append("}");
                            return sb.ToString();
                        }
                        return MakeError("GameObject or VisualElement not found: " + path);
                    }

                    sb.Append("{\"status\":\"ok\",\"path\":\"").Append(EscapeJson(path)).Append("\"");
                    sb.Append(",\"children\":[");
                    bool first = true;
                    for (int i = 0; i < go.transform.childCount; i++)
                    {
                        if (!first) sb.Append(",");
                        SerializeGameObjectTree(go.transform.GetChild(i).gameObject, sb, 0, maxDepth);
                        first = false;
                    }
                    sb.Append("]}");
                }
                else
                {
                    // List all root GameObjects across all loaded scenes
                    sb.Append("{\"status\":\"ok\",\"scenes\":[");
                    bool firstScene = true;
                    for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
                    {
                        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                        if (!scene.isLoaded) continue;
                        if (!string.IsNullOrEmpty(sceneName) && scene.name != sceneName) continue;

                        if (!firstScene) sb.Append(",");
                        sb.Append("{\"name\":\"").Append(EscapeJson(scene.name)).Append("\"");
                        sb.Append(",\"rootObjects\":[");
                        var roots = scene.GetRootGameObjects();
                        for (int r = 0; r < roots.Length; r++)
                        {
                            if (r > 0) sb.Append(",");
                            SerializeGameObjectTree(roots[r], sb, 0, maxDepth);
                        }
                        sb.Append("]}");
                        firstScene = false;
                    }

                    // Also include DontDestroyOnLoad objects
                    // They live in a special scene that's not enumerated by SceneManager
                    if (string.IsNullOrEmpty(sceneName) || sceneName == "DontDestroyOnLoad")
                    {
                        var ddolObjects = GetDontDestroyOnLoadObjects();
                        if (ddolObjects.Length > 0)
                        {
                            if (!firstScene) sb.Append(",");
                            sb.Append("{\"name\":\"DontDestroyOnLoad\"");
                            sb.Append(",\"rootObjects\":[");
                            for (int d = 0; d < ddolObjects.Length; d++)
                            {
                                if (d > 0) sb.Append(",");
                                SerializeGameObjectTree(ddolObjects[d], sb, 0, maxDepth);
                            }
                            sb.Append("]}");
                        }
                    }

                    sb.Append("]"); // close scenes array

                    // Append UITK documents (UI Toolkit — VisualElement trees)
                    var uiDocs = EnumerateActiveUIDocuments();
                    sb.Append(",\"uitkDocuments\":[");
                    for (int i = 0; i < uiDocs.Count; i++)
                    {
                        if (i > 0) sb.Append(",");
                        var doc = uiDocs[i];
                        sb.Append("{\"docPath\":\"").Append(EscapeJson(GetGameObjectPath(doc.gameObject))).Append("\"");
                        var ps = doc.panelSettings;
                        sb.Append(",\"sortingOrder\":").Append(ps != null ? ps.sortingOrder.ToString("F1") : "0");
                        sb.Append(",\"panelSettings\":\"").Append(EscapeJson(ps != null ? ps.name : "")).Append("\"");
                        sb.Append(",\"root\":");
                        SerializeVETree(doc.rootVisualElement, sb, 0, maxDepth);
                        sb.Append("}");
                    }
                    sb.Append("]");

                    sb.Append("}"); // close root object
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return MakeError("InspectHierarchy failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Get root GameObjects in the DontDestroyOnLoad scene.
        /// Unity doesn't expose this directly, so we use a workaround.
        /// </summary>
        private static GameObject[] GetDontDestroyOnLoadObjects()
        {
            try
            {
                // Find objects that are in a scene named "DontDestroyOnLoad"
                var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
                var ddolRoots = new List<GameObject>();
                foreach (var go in allObjects)
                {
                    if (go.transform.parent == null &&
                        go.scene.name == "DontDestroyOnLoad" &&
                        go.hideFlags == HideFlags.None)
                    {
                        ddolRoots.Add(go);
                    }
                }
                return ddolRoots.ToArray();
            }
            catch
            {
                return new GameObject[0];
            }
        }

        /// <summary>
        /// Serialize a GameObject tree node to JSON.
        /// Includes: name, activeSelf, activeInHierarchy, components (type names),
        /// childCount, and recursive children up to maxDepth.
        /// </summary>
        private static void SerializeGameObjectTree(GameObject go, StringBuilder sb, int currentDepth, int maxDepth)
        {
            sb.Append("{\"name\":\"").Append(EscapeJson(go.name)).Append("\"");
            sb.Append(",\"active\":").Append(go.activeSelf ? "true" : "false");
            sb.Append(",\"activeInHierarchy\":").Append(go.activeInHierarchy ? "true" : "false");

            // Component type names (skip Transform as it's always present)
            var components = go.GetComponents<Component>();
            sb.Append(",\"components\":[");
            bool firstComp = true;
            foreach (var comp in components)
            {
                if (comp == null) continue; // Missing script
                string typeName = comp.GetType().Name;
                if (typeName == "Transform" || typeName == "RectTransform") continue;
                if (!firstComp) sb.Append(",");
                sb.Append("\"").Append(EscapeJson(typeName)).Append("\"");
                firstComp = false;
            }
            sb.Append("]");

            sb.Append(",\"childCount\":").Append(go.transform.childCount);

            // Recurse into children if within depth limit
            if (currentDepth < maxDepth && go.transform.childCount > 0)
            {
                sb.Append(",\"children\":[");
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    if (i > 0) sb.Append(",");
                    SerializeGameObjectTree(go.transform.GetChild(i).gameObject, sb, currentDepth + 1, maxDepth);
                }
                sb.Append("]");
            }

            sb.Append("}");
        }

        /// <summary>
        /// Inspect a specific GameObject in detail. Returns comprehensive component data.
        /// Parameters:
        ///   path: full hierarchy path (e.g. "Canvas/Panel/Button")
        ///   components: if "true", include detailed property values for each component
        /// </summary>
        private static string HandleInspectGameObject(string json)
        {
            string path = ExtractJsonString(json, "path");
            string includeComponents = ExtractJsonString(json, "components");
            bool detailed = (includeComponents == "true");

            if (string.IsNullOrEmpty(path))
                return MakeError("Missing 'path' parameter");

            try
            {
                var go = GameObject.Find(path);
                // GameObject.Find only finds active objects. Try hierarchy traversal for inactive ones.
                if (go == null)
                    go = FindGameObjectByPath(path);
                if (go == null)
                    return MakeError("GameObject not found (active or inactive): " + path);

                var sb = new StringBuilder();
                sb.Append("{\"status\":\"ok\"");
                sb.Append(",\"name\":\"").Append(EscapeJson(go.name)).Append("\"");
                sb.Append(",\"path\":\"").Append(EscapeJson(path)).Append("\"");
                sb.Append(",\"active\":").Append(go.activeSelf ? "true" : "false");
                sb.Append(",\"activeInHierarchy\":").Append(go.activeInHierarchy ? "true" : "false");
                sb.Append(",\"layer\":").Append(go.layer);
                sb.Append(",\"tag\":\"").Append(EscapeJson(go.tag)).Append("\"");
                sb.Append(",\"scene\":\"").Append(EscapeJson(go.scene.name ?? "null")).Append("\"");

                // Transform info
                var rt = go.GetComponent<RectTransform>();
                if (rt != null)
                {
                    sb.Append(",\"rectTransform\":{");
                    sb.Append("\"anchoredPosition\":[").Append(rt.anchoredPosition.x.ToString("F1")).Append(",").Append(rt.anchoredPosition.y.ToString("F1")).Append("]");
                    sb.Append(",\"sizeDelta\":[").Append(rt.sizeDelta.x.ToString("F1")).Append(",").Append(rt.sizeDelta.y.ToString("F1")).Append("]");
                    sb.Append(",\"anchorMin\":[").Append(rt.anchorMin.x.ToString("F2")).Append(",").Append(rt.anchorMin.y.ToString("F2")).Append("]");
                    sb.Append(",\"anchorMax\":[").Append(rt.anchorMax.x.ToString("F2")).Append(",").Append(rt.anchorMax.y.ToString("F2")).Append("]");
                    sb.Append(",\"pivot\":[").Append(rt.pivot.x.ToString("F2")).Append(",").Append(rt.pivot.y.ToString("F2")).Append("]");
                    sb.Append(",\"rect\":{\"x\":").Append(rt.rect.x.ToString("F1")).Append(",\"y\":").Append(rt.rect.y.ToString("F1"));
                    sb.Append(",\"width\":").Append(rt.rect.width.ToString("F1")).Append(",\"height\":").Append(rt.rect.height.ToString("F1")).Append("}");
                    sb.Append("}");
                }
                else
                {
                    var t = go.transform;
                    sb.Append(",\"transform\":{");
                    sb.Append("\"localPosition\":[").Append(t.localPosition.x.ToString("F2")).Append(",").Append(t.localPosition.y.ToString("F2")).Append(",").Append(t.localPosition.z.ToString("F2")).Append("]");
                    sb.Append(",\"localScale\":[").Append(t.localScale.x.ToString("F2")).Append(",").Append(t.localScale.y.ToString("F2")).Append(",").Append(t.localScale.z.ToString("F2")).Append("]");
                    sb.Append("}");
                }

                // Children names
                sb.Append(",\"children\":[");
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    if (i > 0) sb.Append(",");
                    var child = go.transform.GetChild(i);
                    sb.Append("{\"name\":\"").Append(EscapeJson(child.name)).Append("\"");
                    sb.Append(",\"active\":").Append(child.gameObject.activeSelf ? "true" : "false");
                    sb.Append("}");
                }
                sb.Append("]");

                // Component details
                sb.Append(",\"components\":[");
                var components = go.GetComponents<Component>();
                bool firstComp = true;
                foreach (var comp in components)
                {
                    if (comp == null)
                    {
                        if (!firstComp) sb.Append(",");
                        sb.Append("{\"type\":\"MissingScript\",\"enabled\":false}");
                        firstComp = false;
                        continue;
                    }

                    if (!firstComp) sb.Append(",");
                    SerializeComponent(comp, sb, detailed);
                    firstComp = false;
                }
                sb.Append("]");

                sb.Append("}");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return MakeError("InspectGameObject failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Serialize a Component to JSON with type name, enabled state, and key properties.
        /// For common UGUI components, includes domain-specific fields.
        /// </summary>
        private static void SerializeComponent(Component comp, StringBuilder sb, bool detailed)
        {
            string typeName = comp.GetType().Name;
            sb.Append("{\"type\":\"").Append(EscapeJson(typeName)).Append("\"");

            // Enabled state (Behaviour and Renderer have 'enabled')
            if (comp is Behaviour beh)
                sb.Append(",\"enabled\":").Append(beh.enabled ? "true" : "false");
            else if (comp is Renderer ren)
                sb.Append(",\"enabled\":").Append(ren.enabled ? "true" : "false");

            if (detailed)
            {
                // Serialize key properties for common component types
                SerializeComponentProperties(comp, sb);
            }

            sb.Append("}");
        }

        /// <summary>
        /// Serialize key properties for well-known component types.
        /// Focuses on UGUI components since that's our primary use case.
        /// </summary>
        private static void SerializeComponentProperties(Component comp, StringBuilder sb)
        {
            // UnityEngine.UI.Text
            if (comp is UnityEngine.UI.Text text)
            {
                sb.Append(",\"text\":\"").Append(EscapeJson(text.text ?? "")).Append("\"");
                sb.Append(",\"fontSize\":").Append(text.fontSize);
                sb.Append(",\"color\":\"").Append(ColorToHex(text.color)).Append("\"");
                sb.Append(",\"alignment\":\"").Append(text.alignment.ToString()).Append("\"");
                sb.Append(",\"raycastTarget\":").Append(text.raycastTarget ? "true" : "false");
                return;
            }

            // UnityEngine.UI.Image
            if (comp is UnityEngine.UI.Image img)
            {
                sb.Append(",\"sprite\":\"").Append(EscapeJson(img.sprite != null ? img.sprite.name : "null")).Append("\"");
                sb.Append(",\"color\":\"").Append(ColorToHex(img.color)).Append("\"");
                sb.Append(",\"imageType\":\"").Append(img.type.ToString()).Append("\"");
                sb.Append(",\"raycastTarget\":").Append(img.raycastTarget ? "true" : "false");
                return;
            }

            // UnityEngine.UI.Button
            if (comp is UnityEngine.UI.Button btn)
            {
                sb.Append(",\"interactable\":").Append(btn.interactable ? "true" : "false");
                sb.Append(",\"onClick_count\":").Append(btn.onClick.GetPersistentEventCount());
                return;
            }

            // UnityEngine.UI.ScrollRect
            if (comp is UnityEngine.UI.ScrollRect scroll)
            {
                sb.Append(",\"horizontal\":").Append(scroll.horizontal ? "true" : "false");
                sb.Append(",\"vertical\":").Append(scroll.vertical ? "true" : "false");
                sb.Append(",\"content\":\"").Append(EscapeJson(scroll.content != null ? scroll.content.name : "null")).Append("\"");
                sb.Append(",\"viewport\":\"").Append(EscapeJson(scroll.viewport != null ? scroll.viewport.name : "null")).Append("\"");
                sb.Append(",\"normalizedPosition\":[").Append(scroll.normalizedPosition.x.ToString("F2")).Append(",").Append(scroll.normalizedPosition.y.ToString("F2")).Append("]");
                return;
            }

            // UnityEngine.UI.LayoutGroup (Vertical/Horizontal)
            if (comp is UnityEngine.UI.VerticalLayoutGroup vlg)
            {
                sb.Append(",\"spacing\":").Append(vlg.spacing.ToString("F1"));
                sb.Append(",\"childAlignment\":\"").Append(vlg.childAlignment.ToString()).Append("\"");
                sb.Append(",\"childForceExpandWidth\":").Append(vlg.childForceExpandWidth ? "true" : "false");
                sb.Append(",\"childForceExpandHeight\":").Append(vlg.childForceExpandHeight ? "true" : "false");
                sb.Append(",\"childControlWidth\":").Append(vlg.childControlWidth ? "true" : "false");
                sb.Append(",\"childControlHeight\":").Append(vlg.childControlHeight ? "true" : "false");
                return;
            }
            if (comp is UnityEngine.UI.HorizontalLayoutGroup hlg)
            {
                sb.Append(",\"spacing\":").Append(hlg.spacing.ToString("F1"));
                sb.Append(",\"childAlignment\":\"").Append(hlg.childAlignment.ToString()).Append("\"");
                sb.Append(",\"childForceExpandWidth\":").Append(hlg.childForceExpandWidth ? "true" : "false");
                sb.Append(",\"childForceExpandHeight\":").Append(hlg.childForceExpandHeight ? "true" : "false");
                return;
            }

            // UnityEngine.UI.ContentSizeFitter
            if (comp is UnityEngine.UI.ContentSizeFitter csf)
            {
                sb.Append(",\"horizontalFit\":\"").Append(csf.horizontalFit.ToString()).Append("\"");
                sb.Append(",\"verticalFit\":\"").Append(csf.verticalFit.ToString()).Append("\"");
                return;
            }

            // UnityEngine.UI.LayoutElement
            if (comp is UnityEngine.UI.LayoutElement le)
            {
                sb.Append(",\"ignoreLayout\":").Append(le.ignoreLayout ? "true" : "false");
                sb.Append(",\"minWidth\":").Append(le.minWidth.ToString("F1"));
                sb.Append(",\"minHeight\":").Append(le.minHeight.ToString("F1"));
                sb.Append(",\"preferredWidth\":").Append(le.preferredWidth.ToString("F1"));
                sb.Append(",\"preferredHeight\":").Append(le.preferredHeight.ToString("F1"));
                sb.Append(",\"flexibleWidth\":").Append(le.flexibleWidth.ToString("F1"));
                sb.Append(",\"flexibleHeight\":").Append(le.flexibleHeight.ToString("F1"));
                return;
            }

            // UnityEngine.UI.Mask / RectMask2D
            if (comp is UnityEngine.UI.Mask mask)
            {
                sb.Append(",\"showMaskGraphic\":").Append(mask.showMaskGraphic ? "true" : "false");
                return;
            }
            if (comp is UnityEngine.UI.RectMask2D)
            {
                // RectMask2D has no user-configurable properties beyond enabled
                return;
            }

            // CanvasGroup
            if (comp is CanvasGroup cg)
            {
                sb.Append(",\"alpha\":").Append(cg.alpha.ToString("F2"));
                sb.Append(",\"interactable\":").Append(cg.interactable ? "true" : "false");
                sb.Append(",\"blocksRaycasts\":").Append(cg.blocksRaycasts ? "true" : "false");
                return;
            }

            // Canvas
            if (comp is Canvas canvas)
            {
                sb.Append(",\"renderMode\":\"").Append(canvas.renderMode.ToString()).Append("\"");
                sb.Append(",\"sortingOrder\":").Append(canvas.sortingOrder);
                sb.Append(",\"overrideSorting\":").Append(canvas.overrideSorting ? "true" : "false");
                return;
            }
        }

        /// <summary>
        /// Find a GameObject by hierarchy path, including inactive objects.
        /// Supports paths like "ServerListPanel(Clone)/server_main_container/server_list".
        /// Searches all loaded scenes and DontDestroyOnLoad.
        /// </summary>
        private static GameObject FindGameObjectByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string[] parts = path.Split('/');
            string rootName = parts[0];

            // Search all root objects across all scenes (including inactive)
            GameObject root = null;
            for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var go in scene.GetRootGameObjects())
                {
                    if (go.name == rootName) { root = go; break; }
                }
                if (root != null) break;
            }

            // Also check DontDestroyOnLoad
            if (root == null)
            {
                foreach (var go in GetDontDestroyOnLoadObjects())
                {
                    if (go.name == rootName) { root = go; break; }
                }
            }

            // Deep search: if not found as root, search all transforms (breadth-first)
            if (root == null)
            {
                var allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
                foreach (var t in allTransforms)
                {
                    if (t.name == rootName && t.gameObject.scene.IsValid() &&
                        t.gameObject.hideFlags == HideFlags.None)
                    {
                        root = t.gameObject;
                        break;
                    }
                }
            }

            if (root == null) return null;

            // Navigate through the rest of the path
            var current = root.transform;
            for (int i = 1; i < parts.Length; i++)
            {
                Transform child = current.Find(parts[i]);
                if (child == null) return null;
                current = child;
            }
            return current.gameObject;
        }

        private static string ColorToHex(Color c)
        {
            return "#" + ColorUtility.ToHtmlStringRGBA(c);
        }

        #endregion

        #region Screenshot Capture

        /// <summary>
        /// Capture a screenshot from Game View camera or Scene View camera.
        /// For "game" source: tries ScreenCapture first (fully composited including
        /// ScreenSpaceOverlay), then GameView internal RT, then single-camera fallback.
        /// </summary>
        private static string HandleCaptureScreenshot(string json)
        {
            string source = ExtractJsonString(json, "source") ?? "game";
            string maxResStr = ExtractJsonString(json, "maxResolution");
            int maxResolution = 1280;
            if (!string.IsNullOrEmpty(maxResStr))
            {
                int parsed;
                if (int.TryParse(maxResStr, out parsed) && parsed >= 64 && parsed <= 4096)
                    maxResolution = parsed;
            }

            try
            {
                string actualSource = source.ToLower();

                if (actualSource != "scene")
                {
                    // --- Strategy 1: ScreenCapture (best — full composite) ---
                    var r1 = TryCaptureViaScreenCapture(maxResolution);
                    if (r1 != null) return r1;

                    // --- Strategy 2: GameView internal RenderTexture ---
                    var r2 = TryCaptureGameViewRT(maxResolution);
                    if (r2 != null) return r2;
                }

                // --- Strategy 3 (fallback): single-camera render ---
                return CaptureViaCameraRender(actualSource, maxResolution);
            }
            catch (System.Exception ex)
            {
                return MakeError("Screenshot capture failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Strategy 1: ScreenCapture.CaptureScreenshotAsTexture — captures the full
        /// composited Game View (all cameras + ScreenSpaceOverlay + IMGUI).
        /// </summary>
        private static string TryCaptureViaScreenCapture(int maxResolution)
        {
            // ScreenCapture.CaptureScreenshotAsTexture() requires an active render loop
            // ("end of frame" state). In EditMode there is no such loop, so the call
            // always fails. Skip entirely outside PlayMode.
            if (!Application.isPlaying)
            {
                SafeLog("[BridgeServer] ScreenCapture skipped — not in PlayMode");
                return null;
            }

            // Pre-flight check: CaptureScreenshotAsTexture internally requests a region
            // matching the Game View's target resolution. If the actual Render Target
            // (i.e. the GameView window pixel size) is smaller than that resolution,
            // Unity prints an unrecoverable internal error:
            //   "CaptureScreenshot(...) requested a region that exceeds the active
            //    Render Target sized [W:xxx, H:xxx]."
            // We must detect this mismatch BEFORE calling the API to avoid log spam.
            try
            {
                var assembly = typeof(EditorWindow).Assembly;
                var gameViewType = assembly.GetType("UnityEditor.GameView");
                if (gameViewType != null)
                {
                    var windows = Resources.FindObjectsOfTypeAll(gameViewType);
                    if (windows != null && windows.Length > 0)
                    {
                        var gameView = windows[0] as EditorWindow;
                        if (gameView != null)
                        {
                            // Get the GameView's actual pixel rect (window size)
                            var pos = gameView.position;
                            int winW = (int)pos.width;
                            int winH = (int)pos.height;

                            // Get the GameView's target resolution via reflection
                            // (m_TargetSize or the selected GameViewSize)
                            int targetW = 0, targetH = 0;
                            var targetSizeField = gameViewType.GetField("m_TargetSize",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (targetSizeField != null)
                            {
                                var targetSize = (Vector2)targetSizeField.GetValue(gameView);
                                targetW = (int)targetSize.x;
                                targetH = (int)targetSize.y;
                            }

                            // If target resolution exceeds window pixel size, skip
                            if (targetW > 0 && targetH > 0 && (targetW > winW || targetH > winH))
                            {
                                SafeLog($"[BridgeServer] ScreenCapture skipped — target resolution " +
                                    $"{targetW}x{targetH} exceeds GameView window {winW}x{winH}");
                                return null;
                            }

                            // Also check the actual RT if available
                            RenderTexture viewRT = null;
                            var type = gameViewType;
                            while (type != null && viewRT == null)
                            {
                                var field = type.GetField("m_TargetTexture",
                                    BindingFlags.NonPublic | BindingFlags.Instance);
                                if (field != null)
                                    viewRT = field.GetValue(gameView) as RenderTexture;
                                type = type.BaseType;
                            }

                            if (viewRT != null && viewRT.width > 0 && viewRT.height > 0)
                            {
                                // If the RT is smaller than the target resolution, skip
                                if (targetW > viewRT.width || targetH > viewRT.height)
                                {
                                    SafeLog($"[BridgeServer] ScreenCapture skipped — target resolution " +
                                        $"{targetW}x{targetH} exceeds RT {viewRT.width}x{viewRT.height}");
                                    return null;
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                SafeLog("[BridgeServer] ScreenCapture pre-flight check failed: " + ex.Message);
                // If pre-flight fails, skip to be safe — avoid Unity internal error spam
                return null;
            }

            Texture2D captured = null;
            try
            {
                captured = ScreenCapture.CaptureScreenshotAsTexture();
                if (captured == null || captured.width <= 1 || captured.height <= 1)
                {
                    SafeLog("[BridgeServer] ScreenCapture returned null/empty");
                    return null;
                }

                SafeLog($"[BridgeServer] ScreenCapture OK: {captured.width}x{captured.height}");
                return EncodeTextureToResponse(captured, maxResolution, "screen_capture");
            }
            catch (System.Exception ex)
            {
                SafeLog("[BridgeServer] ScreenCapture failed: " + ex.Message);
                return null;
            }
            finally
            {
                if (captured != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(captured);
                    else UnityEngine.Object.DestroyImmediate(captured);
                }
            }
        }

        /// <summary>
        /// Strategy 2: Read the GameView's internal RenderTexture via reflection.
        /// GameView → (PlayModeView base) → m_TargetTexture holds the composited frame.
        /// </summary>
        private static string TryCaptureGameViewRT(int maxResolution)
        {
            Texture2D tex = null;
            try
            {
                var assembly = typeof(EditorWindow).Assembly;
                var gameViewType = assembly.GetType("UnityEditor.GameView");
                if (gameViewType == null) return null;

                var windows = Resources.FindObjectsOfTypeAll(gameViewType);
                if (windows == null || windows.Length == 0) return null;

                var gameView = windows[0] as EditorWindow;
                if (gameView == null) return null;

                RenderTexture viewRT = null;

                // Walk inheritance chain looking for m_TargetTexture
                var type = gameViewType;
                while (type != null && viewRT == null)
                {
                    var field = type.GetField("m_TargetTexture",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                        viewRT = field.GetValue(gameView) as RenderTexture;
                    type = type.BaseType;
                }

                // In EditMode the GameView may not have rendered yet, so
                // m_TargetTexture can be null. Force a Repaint and retry once.
                if ((viewRT == null || viewRT.width <= 0 || viewRT.height <= 0) && !Application.isPlaying)
                {
                    SafeLog("[BridgeServer] m_TargetTexture null in EditMode, forcing Repaint");
                    gameView.Repaint();

                    // After Repaint, re-read m_TargetTexture
                    viewRT = null;
                    type = gameViewType;
                    while (type != null && viewRT == null)
                    {
                        var field = type.GetField("m_TargetTexture",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        if (field != null)
                            viewRT = field.GetValue(gameView) as RenderTexture;
                        type = type.BaseType;
                    }
                }

                if (viewRT == null || viewRT.width <= 0 || viewRT.height <= 0)
                {
                    SafeLog("[BridgeServer] GameView m_TargetTexture not available, trying window pixel grab");
                    // Fallback: use internal GrabPixels via reflection (works in EditMode)
                    return TryCaptureGameViewPixels(gameView, maxResolution);
                }

                int w = viewRT.width, h = viewRT.height;
                var prevActive = RenderTexture.active;
                RenderTexture flippedRT = null;
                try
                {
                    // The GameView RT is typically Y-flipped (OpenGL convention).
                    // Blit with vertical flip: scale(1,-1) + offset(0,1).
                    flippedRT = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(viewRT, flippedRT, new Vector2(1, -1), new Vector2(0, 1));

                    RenderTexture.active = flippedRT;
                    tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();
                }
                finally
                {
                    RenderTexture.active = prevActive;
                    if (flippedRT != null) RenderTexture.ReleaseTemporary(flippedRT);
                }

                SafeLog($"[BridgeServer] GameView RT capture OK: {w}x{h}");
                return EncodeTextureToResponse(tex, maxResolution, "game_view_rt");
            }
            catch (System.Exception ex)
            {
                SafeLog("[BridgeServer] GameView RT capture failed: " + ex.Message);
                return null;
            }
            finally
            {
                if (tex != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(tex);
                    else UnityEngine.Object.DestroyImmediate(tex);
                }
            }
        }

        /// <summary>
        /// Fallback for EditMode when m_TargetTexture is unavailable.
        /// Reads the GameView window pixels via EditorWindow position rect.
        /// </summary>
        private static string TryCaptureGameViewPixels(EditorWindow gameView, int maxResolution)
        {
            Texture2D tex = null;
            try
            {
                // Use the window's position rect to determine pixel dimensions
                var pos = gameView.position;
                int w = Mathf.Max(1, (int)pos.width);
                int h = Mathf.Max(1, (int)pos.height);

                if (w <= 1 || h <= 1)
                {
                    SafeLog("[BridgeServer] GameView window too small for pixel grab");
                    return null;
                }

                // Try using internal method: UnityEditorInternal.InternalEditorUtility.ReadScreenPixel
                var internalUtilType = System.Type.GetType(
                    "UnityEditorInternal.InternalEditorUtility, UnityEditor");
                if (internalUtilType != null)
                {
                    var readMethod = internalUtilType.GetMethod("ReadScreenPixel",
                        BindingFlags.Public | BindingFlags.Static);
                    if (readMethod != null)
                    {
                        // ReadScreenPixel(Vector2 pixelPos, int sizex, int sizey) -> Color[]
                        var screenPos = gameView.position.position;
                        var pixels = readMethod.Invoke(null, new object[] {
                            screenPos, w, h
                        }) as Color[];

                        if (pixels != null && pixels.Length == w * h)
                        {
                            tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                            tex.SetPixels(pixels);
                            tex.Apply();
                            SafeLog($"[BridgeServer] GameView pixel grab OK: {w}x{h}");
                            return EncodeTextureToResponse(tex, maxResolution, "game_view_pixels");
                        }
                    }
                }

                SafeLog("[BridgeServer] GameView pixel grab not available");
                return null;
            }
            catch (System.Exception ex)
            {
                SafeLog("[BridgeServer] GameView pixel grab failed: " + ex.Message);
                return null;
            }
            finally
            {
                if (tex != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(tex);
                    else UnityEngine.Object.DestroyImmediate(tex);
                }
            }
        }

        /// <summary>
        /// Strategy 3 (fallback): render a single camera to a temporary RT.
        /// Does NOT capture ScreenSpaceOverlay canvases.
        /// </summary>
        private static string CaptureViaCameraRender(string requestedSource, int maxResolution)
        {
            Camera cam = null;
            string actualSource = requestedSource;

            if (actualSource == "scene")
            {
                var sceneView = SceneView.lastActiveSceneView;
                if (sceneView != null) cam = sceneView.camera;
                if (cam == null) return MakeError("No active SceneView found");
            }
            else
            {
                cam = Camera.main;
                if (cam == null)
                {
#if UNITY_2022_2_OR_NEWER
                    var cams = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
#else
                    var cams = UnityEngine.Object.FindObjectsOfType<Camera>();
#endif
                    if (cams != null && cams.Length > 0) cam = cams[0];
                }
                if (cam == null)
                {
                    var sceneView = SceneView.lastActiveSceneView;
                    if (sceneView != null) { cam = sceneView.camera; actualSource = "scene_fallback"; }
                }
                if (cam == null) return MakeError("No Camera found");
            }

            int width = Mathf.Max(1, cam.pixelWidth > 0 ? cam.pixelWidth : Screen.width);
            int height = Mathf.Max(1, cam.pixelHeight > 0 ? cam.pixelHeight : Screen.height);

            RenderTexture prevRT = cam.targetTexture;
            RenderTexture prevActive = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            Texture2D tex = null;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                SafeLog($"[BridgeServer] Camera fallback capture: {width}x{height} ({actualSource})");
                return EncodeTextureToResponse(tex, maxResolution, actualSource);
            }
            finally
            {
                cam.targetTexture = prevRT;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                if (tex != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(tex);
                    else UnityEngine.Object.DestroyImmediate(tex);
                }
            }
        }

        /// <summary>
        /// Encode a Texture2D to PNG, optionally downscale, and return the JSON response.
        /// </summary>
        private static string EncodeTextureToResponse(Texture2D tex, int maxResolution, string sourceLabel)
        {
            int width = tex.width, height = tex.height;
            int imgW = width, imgH = height;
            byte[] png;
            Texture2D downscaled = null;

            try
            {
                if (width > maxResolution || height > maxResolution)
                {
                    float scale = Mathf.Min((float)maxResolution / width, (float)maxResolution / height);
                    scale = Mathf.Min(scale, 1f);
                    int dstW = Mathf.Max(1, Mathf.RoundToInt(width * scale));
                    int dstH = Mathf.Max(1, Mathf.RoundToInt(height * scale));

                    var drt = RenderTexture.GetTemporary(dstW, dstH, 0, RenderTextureFormat.ARGB32);
                    drt.filterMode = FilterMode.Bilinear;
                    var prevActive = RenderTexture.active;
                    try
                    {
                        Graphics.Blit(tex, drt);
                        RenderTexture.active = drt;
                        downscaled = new Texture2D(dstW, dstH, TextureFormat.RGBA32, false);
                        downscaled.ReadPixels(new Rect(0, 0, dstW, dstH), 0, 0);
                        downscaled.Apply();
                    }
                    finally
                    {
                        RenderTexture.active = prevActive;
                        RenderTexture.ReleaseTemporary(drt);
                    }
                    png = downscaled.EncodeToPNG();
                    imgW = dstW;
                    imgH = dstH;
                }
                else
                {
                    png = tex.EncodeToPNG();
                }

                string base64 = System.Convert.ToBase64String(png);
                return "{\"status\":\"ok\"" +
                    ",\"imageBase64\":\"" + base64 + "\"" +
                    ",\"mimeType\":\"image/png\"" +
                    ",\"width\":" + imgW +
                    ",\"height\":" + imgH +
                    ",\"source\":\"" + EscapeJson(sourceLabel) + "\"}";
            }
            finally
            {
                if (downscaled != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(downscaled);
                    else UnityEngine.Object.DestroyImmediate(downscaled);
                }
            }
        }

        #endregion

        #region UI Interaction Simulation

        // NGUI type cache (resolved via reflection since BridgeServer's asmdef doesn't reference Assembly-CSharp)
        private static System.Type _nguiCameraType;
        private static System.Reflection.MethodInfo _nguiNotifyMethod;
        private static bool _nguiChecked;

        private static void EnsureNguiReflection()
        {
            if (_nguiChecked) return;
            _nguiChecked = true;
            _nguiCameraType = System.Type.GetType("UICamera, Assembly-CSharp")
                ?? System.Type.GetType("UICamera, Assembly-CSharp-firstpass");
            if (_nguiCameraType != null)
                _nguiNotifyMethod = _nguiCameraType.GetMethod("Notify",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null, new System.Type[] { typeof(GameObject), typeof(string), typeof(object) }, null);
        }

        private static bool IsNguiWidget(GameObject go)
        {
            return go.GetComponent("UIWidget") != null
                || go.GetComponent("UIButton") != null
                || go.GetComponent("UIEventListener") != null
                || go.GetComponent("UIToggle") != null
                || go.GetComponent("UILabel") != null
                || go.GetComponent("UISprite") != null;
        }

        private static string DetectUIFramework(GameObject go)
        {
            if (go.GetComponent<RectTransform>() != null &&
                (go.GetComponent<UnityEngine.UI.Button>() != null || go.GetComponent<UnityEngine.UI.Toggle>() != null
                 || go.GetComponent<Graphic>() != null))
                return "ugui";
            if (IsNguiWidget(go))
                return "ngui";
            if (go.GetComponent<RectTransform>() != null)
                return "ugui";
            if (go.GetComponent<Collider>() != null || go.GetComponent("UIPanel") != null)
                return "ngui";
            return "unknown";
        }

        private static string MapActionToNguiMessage(string action)
        {
            switch (action.ToLower())
            {
                case "click": return "OnClick";
                case "down": return "OnPress";
                case "up": return "OnPress";
                case "submit": return "OnClick";
                default: return null;
            }
        }

        // ===========================================================================
        //  UI Toolkit (UITK) helpers
        //  Support simulate_click / simulate_click_at / inspect_hierarchy for
        //  VisualElement-based UIs driven by UIDocument.
        // ===========================================================================

        /// <summary>
        /// Result bundle for a UITK lookup: the matching VisualElement plus the
        /// owning UIDocument GameObject (for diagnostics / path reporting).
        /// </summary>
        private struct UITKHit
        {
            public VisualElement element;
            public UIDocument document;
            public string docPath;   // GameObject path of the UIDocument
            public string vePath;    // '/' joined name chain from root to element
        }

        /// <summary>
        /// Enumerate all active UIDocuments in loaded scenes + DontDestroyOnLoad.
        /// Uses Resources.FindObjectsOfTypeAll to also pick up inactive ones,
        /// but we filter to those whose GameObject is active in hierarchy and
        /// whose rootVisualElement is non-null.
        /// </summary>
        private static List<UIDocument> EnumerateActiveUIDocuments()
        {
            var list = new List<UIDocument>();
            UIDocument[] all;
            try { all = Resources.FindObjectsOfTypeAll<UIDocument>(); }
            catch { return list; }

            for (int i = 0; i < all.Length; i++)
            {
                var doc = all[i];
                if (doc == null) continue;
                if (doc.gameObject == null) continue;
                if (doc.hideFlags == HideFlags.NotEditable || doc.hideFlags == HideFlags.HideAndDontSave) continue;
                if (!doc.gameObject.activeInHierarchy) continue;
                if (doc.rootVisualElement == null) continue;
                // Skip asset-side UIDocuments (those not tied to a real scene object).
                if (!doc.gameObject.scene.IsValid()) continue;
                list.Add(doc);
            }
            return list;
        }

        /// <summary>
        /// Build a '/' separated name chain for a VisualElement, walking parents
        /// up to (but not including) the rootVisualElement. Unnamed elements
        /// are rendered as their type name.
        /// </summary>
        private static string BuildVEPath(VisualElement ve, VisualElement root)
        {
            if (ve == null) return "";
            var names = new List<string>();
            var cur = ve;
            while (cur != null && cur != root)
            {
                string n = !string.IsNullOrEmpty(cur.name) ? cur.name : cur.GetType().Name;
                names.Add(n);
                cur = cur.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }

        /// <summary>
        /// Try to find a VisualElement by path.
        /// Path forms supported:
        ///   "UIDocGoPath#veName"                  – explicit document + VE name
        ///   "UIDocGoPath#parentName/childName"    – explicit document + VE chain
        ///   "veName"                              – scan all UIDocuments, Q by name
        ///   "parentName/childName"                – scan all UIDocuments, walk chain
        /// Returns true on first match.
        /// </summary>
        private static bool TryFindUITK(string path, out UITKHit hit)
        {
            hit = default(UITKHit);
            if (string.IsNullOrEmpty(path)) return false;

            string docPart = null;
            string vePart = path;
            int hashIdx = path.IndexOf('#');
            if (hashIdx >= 0)
            {
                docPart = path.Substring(0, hashIdx);
                vePart = path.Substring(hashIdx + 1);
            }

            var docs = EnumerateActiveUIDocuments();
            foreach (var doc in docs)
            {
                if (!string.IsNullOrEmpty(docPart))
                {
                    string goPath = GetGameObjectPath(doc.gameObject);
                    if (goPath != docPart && doc.gameObject.name != docPart) continue;
                }

                var root = doc.rootVisualElement;
                if (root == null) continue;

                VisualElement found = ResolveVEByChain(root, vePart);
                if (found != null)
                {
                    hit.element = found;
                    hit.document = doc;
                    hit.docPath = GetGameObjectPath(doc.gameObject);
                    hit.vePath = BuildVEPath(found, root);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Resolve a VisualElement under <paramref name="root"/> by a '/' separated
        /// name chain. A single-segment chain falls back to a tree-wide Q() query.
        /// </summary>
        private static VisualElement ResolveVEByChain(VisualElement root, string chain)
        {
            if (root == null || string.IsNullOrEmpty(chain)) return null;
            var segs = chain.Split('/');
            if (segs.Length == 1)
            {
                // Fast path: subtree query by name
                return root.Q<VisualElement>(segs[0]);
            }

            // Walk segment by segment. Each segment may match a direct child or any descendant.
            VisualElement cur = root;
            for (int i = 0; i < segs.Length; i++)
            {
                if (string.IsNullOrEmpty(segs[i])) continue;
                VisualElement next = null;
                // Prefer direct child match first (stricter)
                for (int c = 0; c < cur.childCount; c++)
                {
                    var ch = cur[c];
                    if (ch.name == segs[i]) { next = ch; break; }
                }
                // Fallback: descendant query by name
                if (next == null)
                    next = cur.Q<VisualElement>(segs[i]);
                if (next == null) return null;
                cur = next;
            }
            return cur;
        }

        /// <summary>
        /// Dispatch a simulated pointer action onto a VisualElement.
        /// Returns true if a best-effort event chain was sent.
        /// Uses UITK runtime events: PointerDownEvent / PointerUpEvent / ClickEvent (2021.2+),
        /// with NavigationSubmitEvent fallback for "submit".
        /// For Button, also invokes clicked directly – this matches how user code
        /// typically hooks up behavior (button.clicked += ...).
        /// </summary>
        private static bool SendUITKClick(VisualElement ve, string action, out string method)
        {
            method = "VisualElement.SendEvent";
            if (ve == null) return false;
            string a = (action ?? "click").ToLower();

            var panel = ve.panel;
            // Compute a representative world/local position for event payloads.
            Vector2 localCenter = new Vector2(ve.layout.width * 0.5f, ve.layout.height * 0.5f);
            Vector2 worldCenter = ve.LocalToWorld(localCenter);

            try
            {
                switch (a)
                {
                    case "down":
                        SendPointerDown(ve, worldCenter, localCenter);
                        return true;
                    case "up":
                        SendPointerUp(ve, worldCenter, localCenter);
                        return true;
                    case "submit":
                        using (var submit = NavigationSubmitEvent.GetPooled())
                        {
                            submit.target = ve;
                            ve.SendEvent(submit);
                        }
                        method = "NavigationSubmitEvent";
                        return true;
                    case "click":
                    default:
                        SendPointerDown(ve, worldCenter, localCenter);
                        SendPointerUp(ve, worldCenter, localCenter);
                        // Button: invoke clicked directly (mirrors real user path through Clickable).
                        var btn = ve as UnityEngine.UIElements.Button;
                        if (btn != null)
                        {
                            // clicked is an Action invoked by Clickable on pointer up.
                            // Calling it here guarantees handlers fire even if the
                            // synthetic pointer chain above didn't produce a ClickEvent.
                            try
                            {
                                var clickedField = typeof(UnityEngine.UIElements.Button)
                                    .GetField("clicked", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                                // Button.clicked is a public event-backed Action (field name "clicked").
                                var del = clickedField != null ? clickedField.GetValue(btn) as System.Action : null;
                                if (del != null) del.Invoke();
                                else
                                {
                                    // Fallback: reflection-invoke Clickable.SimulateSingleClick if present.
                                    var clickable = btn.clickable;
                                    if (clickable != null)
                                    {
                                        var sim = clickable.GetType().GetMethod("SimulateSingleClick",
                                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                        if (sim != null) sim.Invoke(clickable, new object[] { null, 1 });
                                    }
                                }
                                method = "Button.clicked";
                            }
                            catch { /* best-effort */ }
                        }
                        return true;
                }
            }
            catch (Exception ex)
            {
                SafeLog("[BridgeServer] SendUITKClick error: " + ex.Message, true);
                return false;
            }
        }

        private static void SendPointerDown(VisualElement ve, Vector2 worldPos, Vector2 localPos)
        {
            using (var evt = PointerDownEvent.GetPooled())
            {
                evt.target = ve;
                ve.SendEvent(evt);
            }
        }

        private static void SendPointerUp(VisualElement ve, Vector2 worldPos, Vector2 localPos)
        {
            using (var evt = PointerUpEvent.GetPooled())
            {
                evt.target = ve;
                ve.SendEvent(evt);
            }
        }

        /// <summary>
        /// Pick a VisualElement at screen coordinates across all active UIDocuments.
        /// Screen coordinates use Unity convention (y=0 at bottom). UITK panels
        /// operate in a top-left origin space, so we flip y via Screen.height.
        /// </summary>
        private static bool TryPickUITKAtScreen(Vector2 screenPos, out UITKHit hit)
        {
            hit = default(UITKHit);
            var docs = EnumerateActiveUIDocuments();
            // Higher sortingOrder should win; iterate in descending order.
            docs.Sort((a, b) =>
            {
                float sa = a.panelSettings != null ? a.panelSettings.sortingOrder : 0f;
                float sb = b.panelSettings != null ? b.panelSettings.sortingOrder : 0f;
                return sb.CompareTo(sa);
            });

            foreach (var doc in docs)
            {
                var root = doc.rootVisualElement;
                if (root == null || root.panel == null) continue;

                // Convert screen → panel coords. RuntimePanelUtils handles DPI/match
                // mode defined on PanelSettings.
                Vector2 panelPos;
                try
                {
                    panelPos = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
                }
                catch
                {
                    continue;
                }

                VisualElement picked = null;
                try { picked = root.panel.Pick(panelPos); }
                catch { picked = null; }

                if (picked != null && picked != root)
                {
                    hit.element = picked;
                    hit.document = doc;
                    hit.docPath = GetGameObjectPath(doc.gameObject);
                    hit.vePath = BuildVEPath(picked, root);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Serialize a VisualElement subtree to compact JSON for inspect_hierarchy.
        /// Includes: name, type, classes, visible/enabled flags, rect, childCount.
        /// </summary>
        private static void SerializeVETree(VisualElement ve, StringBuilder sb, int currentDepth, int maxDepth)
        {
            if (ve == null) { sb.Append("null"); return; }
            string n = !string.IsNullOrEmpty(ve.name) ? ve.name : "";
            sb.Append("{\"name\":\"").Append(EscapeJson(n)).Append("\"");
            sb.Append(",\"type\":\"").Append(EscapeJson(ve.GetType().Name)).Append("\"");

            // classList
            sb.Append(",\"classes\":[");
            var classes = ve.GetClasses();
            bool firstCls = true;
            foreach (var cls in classes)
            {
                if (!firstCls) sb.Append(",");
                sb.Append("\"").Append(EscapeJson(cls)).Append("\"");
                firstCls = false;
            }
            sb.Append("]");

            sb.Append(",\"visible\":").Append(ve.visible ? "true" : "false");
            sb.Append(",\"enabled\":").Append(ve.enabledInHierarchy ? "true" : "false");
            try { sb.Append(",\"display\":\"").Append(ve.resolvedStyle.display.ToString()).Append("\""); }
            catch { /* style not resolved yet */ }

            var r = ve.layout;
            sb.Append(",\"rect\":{\"x\":").Append(r.x.ToString("F1"))
              .Append(",\"y\":").Append(r.y.ToString("F1"))
              .Append(",\"w\":").Append(r.width.ToString("F1"))
              .Append(",\"h\":").Append(r.height.ToString("F1")).Append("}");

            sb.Append(",\"childCount\":").Append(ve.childCount);

            if (currentDepth < maxDepth && ve.childCount > 0)
            {
                sb.Append(",\"children\":[");
                for (int i = 0; i < ve.childCount; i++)
                {
                    if (i > 0) sb.Append(",");
                    SerializeVETree(ve[i], sb, currentDepth + 1, maxDepth);
                }
                sb.Append("]");
            }

            sb.Append("}");
        }

        /// <summary>
        /// Simulate a click on a UI element by hierarchy path.
        /// Auto-detects UGUI vs NGUI vs UITK and uses the appropriate event system.
        /// UGUI: ExecuteEvents.Execute (PointerClick chain)
        /// NGUI: UICamera.Notify / SendMessage("OnClick")
        /// UITK: VisualElement.SendEvent (PointerDown/Up, Button.clicked)
        /// Path form "UIDocGoPath#veName[/child]" forces UITK resolution.
        /// </summary>
        private static string HandleSimulateClick(string json)
        {
            string path = ExtractJsonString(json, "path");
            string action = ExtractJsonString(json, "action") ?? "click";

            if (string.IsNullOrEmpty(path))
                return MakeError("Missing 'path' parameter");

            try
            {
                // ── UITK explicit path: "UIDocGoPath#veChain" ─────────────────
                bool explicitUITK = path.IndexOf('#') >= 0;
                if (explicitUITK)
                {
                    UITKHit hit;
                    if (!TryFindUITK(path, out hit))
                        return MakeError("VisualElement not found (UITK): " + path);
                    return BuildUITKClickResponse(path, action, hit);
                }

                var go = GameObject.Find(path);
                if (go == null)
                    go = FindGameObjectByPath(path);

                // ── UITK fallback: GameObject not found → try VisualElement ──
                if (go == null)
                {
                    UITKHit hit;
                    if (TryFindUITK(path, out hit))
                        return BuildUITKClickResponse(path, action, hit);
                    return MakeError("GameObject not found: " + path);
                }

                if (!go.activeInHierarchy)
                    return MakeError("GameObject is inactive: " + path);

                string framework = DetectUIFramework(go);
                var sb = new StringBuilder();
                sb.Append("{\"status\":\"ok\"");
                sb.Append(",\"path\":\"").Append(EscapeJson(path)).Append("\"");
                sb.Append(",\"action\":\"").Append(EscapeJson(action)).Append("\"");
                sb.Append(",\"framework\":\"").Append(framework).Append("\"");

                if (framework == "ngui")
                {
                    EnsureNguiReflection();
                    string nguiMsg = MapActionToNguiMessage(action);
                    if (nguiMsg == null)
                        return MakeError("Unknown action: " + action);

                    if (_nguiNotifyMethod != null)
                    {
                        if (action == "click")
                        {
                            _nguiNotifyMethod.Invoke(null, new object[] { go, "OnPress", true });
                            _nguiNotifyMethod.Invoke(null, new object[] { go, "OnPress", false });
                            _nguiNotifyMethod.Invoke(null, new object[] { go, "OnClick", null });
                        }
                        else if (action == "down")
                            _nguiNotifyMethod.Invoke(null, new object[] { go, "OnPress", true });
                        else if (action == "up")
                            _nguiNotifyMethod.Invoke(null, new object[] { go, "OnPress", false });
                        else
                            _nguiNotifyMethod.Invoke(null, new object[] { go, nguiMsg, null });
                        sb.Append(",\"method\":\"UICamera.Notify\"");
                    }
                    else
                    {
                        if (action == "click")
                        {
                            go.SendMessage("OnPress", true, SendMessageOptions.DontRequireReceiver);
                            go.SendMessage("OnPress", false, SendMessageOptions.DontRequireReceiver);
                            go.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
                        }
                        else if (action == "down")
                            go.SendMessage("OnPress", true, SendMessageOptions.DontRequireReceiver);
                        else if (action == "up")
                            go.SendMessage("OnPress", false, SendMessageOptions.DontRequireReceiver);
                        else
                            go.SendMessage(nguiMsg, SendMessageOptions.DontRequireReceiver);
                        sb.Append(",\"method\":\"SendMessage\"");
                    }

                    var uiButton = go.GetComponent("UIButton");
                    if (uiButton != null)
                    {
                        var isEnabled = uiButton.GetType().GetProperty("isEnabled");
                        if (isEnabled != null)
                            sb.Append(",\"buttonEnabled\":").Append((bool)isEnabled.GetValue(uiButton) ? "true" : "false");
                    }
                    var uiToggle = go.GetComponent("UIToggle");
                    if (uiToggle != null)
                    {
                        var valField = uiToggle.GetType().GetField("value");
                        if (valField != null)
                            sb.Append(",\"toggleValue\":").Append((bool)valField.GetValue(uiToggle) ? "true" : "false");
                    }

                    sb.Append(",\"handled\":true}");
                    return sb.ToString();
                }
                else
                {
                    // UGUI path
                    var eventSystem = EventSystem.current;
                    PointerEventData pointerData = null;
                    if (eventSystem != null)
                    {
                        pointerData = new PointerEventData(eventSystem)
                        {
                            position = GetWorldScreenPosition(go),
                            button = PointerEventData.InputButton.Left,
                            clickCount = 1
                        };
                    }

                    bool handled = false;
                    if (pointerData != null)
                    {
                        switch (action.ToLower())
                        {
                            case "click":
                                ExecuteEvents.Execute(go, pointerData, ExecuteEvents.pointerEnterHandler);
                                ExecuteEvents.Execute(go, pointerData, ExecuteEvents.pointerDownHandler);
                                ExecuteEvents.Execute(go, pointerData, ExecuteEvents.pointerUpHandler);
                                ExecuteEvents.Execute(go, pointerData, ExecuteEvents.pointerClickHandler);
                                handled = true;
                                break;
                            case "down":
                                ExecuteEvents.Execute(go, pointerData, ExecuteEvents.pointerDownHandler);
                                handled = true;
                                break;
                            case "up":
                                ExecuteEvents.Execute(go, pointerData, ExecuteEvents.pointerUpHandler);
                                handled = true;
                                break;
                            case "submit":
                                ExecuteEvents.Execute(go, pointerData, ExecuteEvents.submitHandler);
                                handled = true;
                                break;
                            default:
                                return MakeError("Unknown action: " + action);
                        }
                    }

                    var clickTarget = eventSystem != null
                        ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(go) : null;
                    bool hasHandler = clickTarget != null;

                    // If UGUI found no handler and object might be NGUI, try SendMessage fallback
                    if (!hasHandler && framework == "unknown")
                    {
                        go.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
                        sb.Append(",\"fallback\":\"SendMessage\"");
                        handled = true;
                    }

                    sb.Append(",\"hasHandler\":").Append(hasHandler ? "true" : "false");
                    if (clickTarget != null)
                        sb.Append(",\"clickHandler\":\"").Append(EscapeJson(clickTarget.name)).Append("\"");

                    var button = go.GetComponent<UnityEngine.UI.Button>();
                    if (button != null)
                        sb.Append(",\"buttonInteractable\":").Append(button.interactable ? "true" : "false");
                    var toggle = go.GetComponent<UnityEngine.UI.Toggle>();
                    if (toggle != null)
                    {
                        sb.Append(",\"toggleInteractable\":").Append(toggle.interactable ? "true" : "false");
                        sb.Append(",\"toggleIsOn\":").Append(toggle.isOn ? "true" : "false");
                    }

                    sb.Append(",\"handled\":").Append(handled ? "true" : "false");
                    sb.Append("}");
                    return sb.ToString();
                }
            }
            catch (System.Exception ex)
            {
                return MakeError("SimulateClick failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Build the JSON response for a UITK click and dispatch the event chain.
        /// Shared by explicit-UITK path and GO-not-found fallback branches.
        /// </summary>
        private static string BuildUITKClickResponse(string path, string action, UITKHit hit)
        {
            string method;
            bool handled = SendUITKClick(hit.element, action, out method);

            var sb = new StringBuilder();
            sb.Append("{\"status\":\"ok\"");
            sb.Append(",\"path\":\"").Append(EscapeJson(path)).Append("\"");
            sb.Append(",\"action\":\"").Append(EscapeJson(action)).Append("\"");
            sb.Append(",\"framework\":\"uitk\"");
            sb.Append(",\"docPath\":\"").Append(EscapeJson(hit.docPath ?? "")).Append("\"");
            sb.Append(",\"vePath\":\"").Append(EscapeJson(hit.vePath ?? "")).Append("\"");
            sb.Append(",\"veName\":\"").Append(EscapeJson(hit.element.name ?? "")).Append("\"");
            sb.Append(",\"veType\":\"").Append(EscapeJson(hit.element.GetType().Name)).Append("\"");
            sb.Append(",\"enabled\":").Append(hit.element.enabledInHierarchy ? "true" : "false");
            sb.Append(",\"visible\":").Append(hit.element.visible ? "true" : "false");
            sb.Append(",\"method\":\"").Append(EscapeJson(method)).Append("\"");
            sb.Append(",\"handled\":").Append(handled ? "true" : "false");
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// Simulate a click at screen coordinates.
        /// Tries UGUI GraphicRaycaster first, then UITK panel.Pick, then NGUI physics raycast.
        /// </summary>
        private static string HandleSimulateClickAt(string json)
        {
            string xStr = ExtractJsonString(json, "x");
            string yStr = ExtractJsonString(json, "y");
            string action = ExtractJsonString(json, "action") ?? "click";

            if (string.IsNullOrEmpty(xStr) || string.IsNullOrEmpty(yStr))
                return MakeError("Missing 'x' and/or 'y' parameters");

            float x, y;
            if (!float.TryParse(xStr, out x) || !float.TryParse(yStr, out y))
                return MakeError("Invalid x/y values. Must be numbers.");

            try
            {
                GameObject hitGo = null;
                string hitPath = null;
                string framework = "unknown";
                int totalHits = 0;

                // Strategy 1: UGUI GraphicRaycaster
                var eventSystem = EventSystem.current;
                if (eventSystem != null)
                {
                    var pointerData = new PointerEventData(eventSystem)
                    {
                        position = new Vector2(x, y),
                        button = PointerEventData.InputButton.Left,
                        clickCount = 1
                    };

                    var allResults = new List<RaycastResult>();
#if UNITY_2022_2_OR_NEWER
                    var raycasters = UnityEngine.Object.FindObjectsByType<GraphicRaycaster>(FindObjectsSortMode.None);
#else
                    var raycasters = UnityEngine.Object.FindObjectsOfType<GraphicRaycaster>();
#endif
                    foreach (var raycaster in raycasters)
                    {
                        var results = new List<RaycastResult>();
                        raycaster.Raycast(pointerData, results);
                        allResults.AddRange(results);
                    }

                    if (allResults.Count > 0)
                    {
                        allResults.Sort((a, b) => b.depth.CompareTo(a.depth));
                        hitGo = allResults[0].gameObject;
                        hitPath = GetGameObjectPath(hitGo);
                        framework = "ugui";
                        totalHits = allResults.Count;

                        switch (action.ToLower())
                        {
                            case "click":
                                ExecuteEvents.Execute(hitGo, pointerData, ExecuteEvents.pointerEnterHandler);
                                ExecuteEvents.Execute(hitGo, pointerData, ExecuteEvents.pointerDownHandler);
                                ExecuteEvents.Execute(hitGo, pointerData, ExecuteEvents.pointerUpHandler);
                                ExecuteEvents.Execute(hitGo, pointerData, ExecuteEvents.pointerClickHandler);
                                break;
                            case "down":
                                ExecuteEvents.Execute(hitGo, pointerData, ExecuteEvents.pointerDownHandler);
                                break;
                            case "up":
                                ExecuteEvents.Execute(hitGo, pointerData, ExecuteEvents.pointerUpHandler);
                                break;
                            case "submit":
                                ExecuteEvents.Execute(hitGo, pointerData, ExecuteEvents.submitHandler);
                                break;
                        }
                    }
                }

                // Strategy 2: UITK panel.Pick (runtime UIDocument overlays)
                if (hitGo == null)
                {
                    UITKHit uitkHit;
                    if (TryPickUITKAtScreen(new Vector2(x, y), out uitkHit))
                    {
                        string method;
                        bool handled = SendUITKClick(uitkHit.element, action, out method);
                        var sbU = new StringBuilder();
                        sbU.Append("{\"status\":\"ok\"");
                        sbU.Append(",\"x\":").Append(x);
                        sbU.Append(",\"y\":").Append(y);
                        sbU.Append(",\"action\":\"").Append(EscapeJson(action)).Append("\"");
                        sbU.Append(",\"framework\":\"uitk\"");
                        sbU.Append(",\"docPath\":\"").Append(EscapeJson(uitkHit.docPath ?? "")).Append("\"");
                        sbU.Append(",\"vePath\":\"").Append(EscapeJson(uitkHit.vePath ?? "")).Append("\"");
                        sbU.Append(",\"veName\":\"").Append(EscapeJson(uitkHit.element.name ?? "")).Append("\"");
                        sbU.Append(",\"veType\":\"").Append(EscapeJson(uitkHit.element.GetType().Name)).Append("\"");
                        sbU.Append(",\"method\":\"").Append(EscapeJson(method)).Append("\"");
                        sbU.Append(",\"handled\":").Append(handled ? "true" : "false");
                        sbU.Append("}");
                        return sbU.ToString();
                    }
                }

                // Strategy 3: NGUI physics raycast fallback
                if (hitGo == null)
                {
                    Camera uiCam = Camera.main;
                    if (uiCam == null)
                    {
#if UNITY_2022_2_OR_NEWER
                        var cams = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
#else
                        var cams = UnityEngine.Object.FindObjectsOfType<Camera>();
#endif
                        foreach (var c in cams)
                        {
                            if (c.GetComponent("UICamera") != null) { uiCam = c; break; }
                        }
                        if (uiCam == null && cams.Length > 0) uiCam = cams[0];
                    }

                    if (uiCam != null)
                    {
                        Ray ray = uiCam.ScreenPointToRay(new Vector3(x, y, 0));
                        RaycastHit[] hits = Physics.RaycastAll(ray, uiCam.farClipPlane, uiCam.cullingMask);
                        if (hits.Length > 0)
                        {
                            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                            hitGo = hits[0].collider.gameObject;
                            hitPath = GetGameObjectPath(hitGo);
                            framework = "ngui";
                            totalHits = hits.Length;

                            EnsureNguiReflection();
                            if (_nguiNotifyMethod != null)
                            {
                                if (action == "click" || action == "submit")
                                {
                                    _nguiNotifyMethod.Invoke(null, new object[] { hitGo, "OnPress", true });
                                    _nguiNotifyMethod.Invoke(null, new object[] { hitGo, "OnPress", false });
                                    _nguiNotifyMethod.Invoke(null, new object[] { hitGo, "OnClick", null });
                                }
                                else if (action == "down")
                                    _nguiNotifyMethod.Invoke(null, new object[] { hitGo, "OnPress", true });
                                else if (action == "up")
                                    _nguiNotifyMethod.Invoke(null, new object[] { hitGo, "OnPress", false });
                            }
                            else
                            {
                                hitGo.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
                            }
                        }
                    }
                }

                if (hitGo == null)
                    return MakeError("No UI element found at (" + x + ", " + y + "). Tried UGUI GraphicRaycaster, UITK panel.Pick, and NGUI physics raycast.");

                var sb = new StringBuilder();
                sb.Append("{\"status\":\"ok\"");
                sb.Append(",\"x\":").Append(x);
                sb.Append(",\"y\":").Append(y);
                sb.Append(",\"action\":\"").Append(EscapeJson(action)).Append("\"");
                sb.Append(",\"framework\":\"").Append(framework).Append("\"");
                sb.Append(",\"hitObject\":\"").Append(EscapeJson(hitGo.name)).Append("\"");
                sb.Append(",\"hitPath\":\"").Append(EscapeJson(hitPath)).Append("\"");
                sb.Append(",\"totalHits\":").Append(totalHits);
                sb.Append("}");
                return sb.ToString();
            }
            catch (System.Exception ex)
            {
                return MakeError("SimulateClickAt failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Simulate a drag from one UI element to another.
        /// Fires BeginDrag → Drag → EndDrag on the source, with pointer position set
        /// to the target so that OnEndDrag raycasts hit the target.
        /// </summary>
        private static string HandleSimulateDrag(string json)
        {
            string sourcePath = ExtractJsonString(json, "source_path");
            string targetPath = ExtractJsonString(json, "target_path");

            if (string.IsNullOrEmpty(sourcePath))
                return MakeError("Missing 'source_path' parameter");
            if (string.IsNullOrEmpty(targetPath))
                return MakeError("Missing 'target_path' parameter");

            try
            {
                var sourceGo = GameObject.Find(sourcePath) ?? FindGameObjectByPath(sourcePath);
                if (sourceGo == null)
                    return MakeError("Source GameObject not found: " + sourcePath);

                var targetGo = GameObject.Find(targetPath) ?? FindGameObjectByPath(targetPath);
                if (targetGo == null)
                    return MakeError("Target GameObject not found: " + targetPath);

                if (!sourceGo.activeInHierarchy)
                    return MakeError("Source GameObject is inactive: " + sourcePath);
                if (!targetGo.activeInHierarchy)
                    return MakeError("Target GameObject is inactive: " + targetPath);

                var eventSystem = EventSystem.current;
                if (eventSystem == null)
                    return MakeError("No EventSystem found");

                Vector2 sourcePos = GetWorldScreenPosition(sourceGo);
                Vector2 targetPos = GetWorldScreenPosition(targetGo);

                var pointerData = new PointerEventData(eventSystem)
                {
                    position = sourcePos,
                    button = PointerEventData.InputButton.Left,
                    pressPosition = sourcePos,
                    pointerDrag = sourceGo,
                    dragging = false
                };

                ExecuteEvents.Execute(sourceGo, pointerData, ExecuteEvents.initializePotentialDrag);
                ExecuteEvents.Execute(sourceGo, pointerData, ExecuteEvents.beginDragHandler);
                pointerData.dragging = true;

                int steps = 5;
                for (int i = 1; i <= steps; i++)
                {
                    pointerData.position = Vector2.Lerp(sourcePos, targetPos, (float)i / steps);
                    ExecuteEvents.Execute(sourceGo, pointerData, ExecuteEvents.dragHandler);
                }

                pointerData.position = targetPos;
                ExecuteEvents.Execute(sourceGo, pointerData, ExecuteEvents.endDragHandler);

                var sb = new StringBuilder();
                sb.Append("{\"status\":\"ok\"");
                sb.Append(",\"source_path\":\"").Append(EscapeJson(sourcePath)).Append("\"");
                sb.Append(",\"target_path\":\"").Append(EscapeJson(targetPath)).Append("\"");
                sb.Append(",\"source_pos\":[").Append(sourcePos.x).Append(",").Append(sourcePos.y).Append("]");
                sb.Append(",\"target_pos\":[").Append(targetPos.x).Append(",").Append(targetPos.y).Append("]");
                sb.Append(",\"drag_steps\":").Append(steps);
                sb.Append("}");
                return sb.ToString();
            }
            catch (System.Exception ex)
            {
                return MakeError("SimulateDrag failed: " + ex.Message);
            }
        }

        private static Vector2 GetWorldScreenPosition(GameObject go)
        {
            var rectTransform = go.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                var canvas = go.GetComponentInParent<Canvas>();
                if (canvas != null)
                {
                    Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                    Vector3[] corners = new Vector3[4];
                    rectTransform.GetWorldCorners(corners);
                    Vector3 center = (corners[0] + corners[2]) / 2f;
                    if (cam != null)
                        return cam.WorldToScreenPoint(center);
                    return new Vector2(center.x, center.y);
                }
            }
            return Vector2.zero;
        }

        private static string GetGameObjectPath(GameObject go)
        {
            string path = go.name;
            Transform t = go.transform.parent;
            while (t != null)
            {
                path = t.name + "/" + path;
                t = t.parent;
            }
            return path;
        }

        #endregion

        #region JSON Helpers

        /// <summary>
        /// Extract a string value from a simple JSON object by key.
        /// Handles: {"key":"value"} patterns without external JSON library.
        /// </summary>
        static string ExtractJsonString(string json, string key)
        {
            string pattern = "\"" + key + "\"";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return null;

            idx = json.IndexOf(':', idx + pattern.Length);
            if (idx < 0) return null;

            // Skip whitespace
            idx++;
            while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;

            if (idx >= json.Length) return null;

            // Handle number values
            if (json[idx] != '"')
            {
                int start = idx;
                while (idx < json.Length && json[idx] != ',' && json[idx] != '}')
                    idx++;
                return json.Substring(start, idx - start).Trim();
            }

            // Handle string values
            idx++; // skip opening quote
            var sb = new StringBuilder();
            while (idx < json.Length && json[idx] != '"')
            {
                if (json[idx] == '\\' && idx + 1 < json.Length)
                {
                    idx++;
                    switch (json[idx])
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(json[idx]); break;
                    }
                }
                else
                {
                    sb.Append(json[idx]);
                }
                idx++;
            }
            return sb.ToString();
        }

        static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }

        static string MakeOk(string message)
        {
            return "{\"status\":\"ok\",\"message\":\"" + EscapeJson(message) + "\"}";
        }

        #region Console Log

        /// <summary>
        /// Handle GET_CONSOLE_LOG command.
        /// Uses reflection to access UnityEditor.LogEntries and read Console window entries.
        /// Supports optional parameters:
        ///   - lines: max number of entries to return (default 50, max 200)
        ///   - level: filter by level — "all" (default), "error", "warning", "log"
        /// Must run on main thread.
        /// </summary>
        private static string HandleGetConsoleLog(string json)
        {
            try
            {
                int maxLines = ExtractJsonInt(json, "lines", 50);
                if (maxLines <= 0) maxLines = 50;
                if (maxLines > 200) maxLines = 200;
                string levelFilter = ExtractJsonString(json, "level") ?? "all";

                var logEntriesType = Type.GetType("UnityEditor.LogEntries, UnityEditor");
                if (logEntriesType == null)
                    return MakeError("LogEntries type not found via reflection");

                var startMethod = logEntriesType.GetMethod("StartGettingEntries",
                    BindingFlags.Static | BindingFlags.Public);
                var getEntryMethod = logEntriesType.GetMethod("GetEntryInternal",
                    BindingFlags.Static | BindingFlags.Public);
                var endMethod = logEntriesType.GetMethod("EndGettingEntries",
                    BindingFlags.Static | BindingFlags.Public);
                var getCountMethod = logEntriesType.GetMethod("GetCount",
                    BindingFlags.Static | BindingFlags.Public);

                if (startMethod == null || getCountMethod == null || endMethod == null)
                    return MakeError("LogEntries reflection methods not found");

                int totalCount = (int)getCountMethod.Invoke(null, null);
                if (totalCount == 0)
                    return MakeOkData("{\"total\":0,\"returned\":0,\"entries\":[]}");

                // Resolve LogEntry type and fields
                var logEntryType = Type.GetType("UnityEditor.LogEntry, UnityEditor");
                if (logEntryType == null)
                    return MakeError("LogEntry type not found via reflection");

                var entry = Activator.CreateInstance(logEntryType);
                var modeField = logEntryType.GetField("mode",
                    BindingFlags.Instance | BindingFlags.Public);
                var messageField = logEntryType.GetField("message",
                    BindingFlags.Instance | BindingFlags.Public)
                    ?? logEntryType.GetField("condition",
                    BindingFlags.Instance | BindingFlags.Public);
                var fileField = logEntryType.GetField("file",
                    BindingFlags.Instance | BindingFlags.Public);
                var lineField = logEntryType.GetField("line",
                    BindingFlags.Instance | BindingFlags.Public);

                startMethod.Invoke(null, null);
                try
                {
                    var collected = new List<string>();
                    // Read from the end (newest first)
                    int startIdx = Math.Max(0, totalCount - maxLines * 3); // over-read to account for filtering
                    for (int i = totalCount - 1; i >= startIdx && collected.Count < maxLines; i--)
                    {
                        if (getEntryMethod == null) break;
                        bool ok = (bool)getEntryMethod.Invoke(null, new object[] { i, entry });
                        if (!ok) continue;

                        int mode = modeField != null ? (int)modeField.GetValue(entry) : 0;
                        string message = messageField != null ? (string)messageField.GetValue(entry) : "";
                        string file = fileField != null ? (string)fileField.GetValue(entry) : "";
                        int line = lineField != null ? (int)lineField.GetValue(entry) : 0;

                        // Determine log level from mode flags
                        // mode & 1 = Error/Exception, mode & 2 = Warning, mode & 4 = Log
                        string level;
                        if ((mode & 1) != 0) level = "error";
                        else if ((mode & 2) != 0) level = "warning";
                        else level = "log";

                        // Apply level filter
                        if (levelFilter != "all" && level != levelFilter.ToLower())
                            continue;

                        // Truncate very long messages to avoid pipe buffer overflow
                        if (message != null && message.Length > 1000)
                            message = message.Substring(0, 1000) + "... (truncated)";

                        // Build JSON entry manually (no external JSON lib dependency)
                        collected.Add("{\"level\":\"" + level
                            + "\",\"message\":\"" + EscapeJson(message ?? "")
                            + "\",\"file\":\"" + EscapeJson(file ?? "")
                            + "\",\"line\":" + line + "}");
                    }

                    // Reverse so oldest is first (chronological order)
                    collected.Reverse();

                    string entriesJson = "[" + string.Join(",", collected.ToArray()) + "]";
                    return MakeOkData("{\"total\":" + totalCount
                        + ",\"returned\":" + collected.Count
                        + ",\"level_filter\":\"" + EscapeJson(levelFilter)
                        + "\",\"entries\":" + entriesJson + "}");
                }
                finally
                {
                    endMethod.Invoke(null, null);
                }
            }
            catch (Exception ex)
            {
                return MakeError("GET_CONSOLE_LOG failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Extract an integer value from JSON string. Returns defaultValue if not found.
        /// </summary>
        private static int ExtractJsonInt(string json, string key, int defaultValue)
        {
            // Try to find "key":123 pattern
            string strVal = ExtractJsonString(json, key);
            if (strVal != null && int.TryParse(strVal, out int result))
                return result;

            // Also try raw number pattern: "key":123 (without quotes)
            int keyIdx = json.IndexOf("\"" + key + "\"");
            if (keyIdx < 0) return defaultValue;
            int colonIdx = json.IndexOf(':', keyIdx + key.Length + 2);
            if (colonIdx < 0) return defaultValue;
            int numStart = colonIdx + 1;
            while (numStart < json.Length && (json[numStart] == ' ' || json[numStart] == '\t'))
                numStart++;
            int numEnd = numStart;
            while (numEnd < json.Length && (char.IsDigit(json[numEnd]) || json[numEnd] == '-'))
                numEnd++;
            if (numEnd > numStart && int.TryParse(json.Substring(numStart, numEnd - numStart), out int val))
                return val;
            return defaultValue;
        }

        #endregion

        static string MakeOkData(string dataJson)
        {
            return "{\"status\":\"ok\",\"data\":" + dataJson + "}";
        }

        static string MakeError(string message)
        {
            return "{\"status\":\"error\",\"message\":\"" + EscapeJson(message) + "\"}";
        }

        #endregion
    }
}
#endif
