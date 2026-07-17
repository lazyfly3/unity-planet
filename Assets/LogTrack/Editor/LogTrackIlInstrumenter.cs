#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEditor.Compilation;
using UnityEngine;

namespace LogTrack.Editor
{
    public sealed class IlInstrumentResult
    {
        public bool success;
        public string error;
        public int patchedMethods;
        public int skippedMethods;
        public readonly List<string> patchedAssemblies = new List<string>();
    }

    public static class LogTrackIlInstrumenter
    {
        public const int MaxArgCount = 7;

        private static readonly HashSet<string> SupportedPrimitiveTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Int32",
            "System.UInt32",
            "System.Int64",
            "System.UInt64",
            "System.Int16",
            "System.UInt16",
            "System.Byte",
            "System.SByte",
            "System.Single",
            "System.Double",
            "System.Boolean"
        };

        public static IlInstrumentResult PatchAssemblies(
            IEnumerable<string> assemblyNames,
            string pdbOutputDir,
            string logTrackClass = "FSPDebuger")
        {
            var result = new IlInstrumentResult();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                pdbOutputDir = NormalizeDir(pdbOutputDir);
                Directory.CreateDirectory(pdbOutputDir.TrimEnd(Path.DirectorySeparatorChar));

                var resolver = LogTrackCecilResolver.CreateResolver();
                using var runtimeAssembly = LogTrackCecilResolver.ReadRuntimeAssembly(resolver);
                var runtimeType = runtimeAssembly.MainModule.GetType(logTrackClass);
                if (runtimeType == null)
                {
                    result.error = logTrackClass + " not found in LogTrack.Runtime.dll";
                    return result;
                }

                if (ImportPropertyGetter(runtimeAssembly.MainModule, runtimeType, "EnableLogTrackInternal") == null)
                {
                    result.error = "Missing get_EnableLogTrackInternal. Recompile LogTrack.Runtime first.";
                    return result;
                }

                var pdbPath = Path.Combine(pdbOutputDir, "LogPdb.pdb.json");
                var pdb = File.Exists(pdbPath) ? LogTrackPdbFile.Open(pdbPath) ?? new LogTrackPdbFile() : new LogTrackPdbFile();

                var assemblyErrors = new List<string>();
                foreach (var assemblyName in assemblyNames ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(assemblyName))
                    {
                        continue;
                    }

                    var dllPath = LogTrackIlAssemblyCatalog.GetDllPath(assemblyName);
                    if (!File.Exists(dllPath))
                    {
                        Debug.LogWarning("[LogTrackIl] DLL not found, skip: " + dllPath);
                        continue;
                    }

                    var assemblyResult = PatchAssembly(dllPath, pdb, runtimeType, assemblyName);
                    result.patchedMethods += assemblyResult.patchedMethods;
                    result.skippedMethods += assemblyResult.skippedMethods;
                    if (assemblyResult.success)
                    {
                        result.patchedAssemblies.Add(assemblyName);
                    }
                    else if (!string.IsNullOrEmpty(assemblyResult.error))
                    {
                        assemblyErrors.Add(assemblyName + ": " + assemblyResult.error);
                        Debug.LogWarning("[LogTrackIl] " + assemblyName + ": " + assemblyResult.error);
                    }
                }

                pdb.Save(pdbPath);
                pdb.SaveAsCSV(Path.Combine(pdbOutputDir, "LogPdb.pdb.csv"));
                LogTrackSettings.PdbRelativePath = Path.Combine(
                    ToProjectRelativePath(pdbOutputDir),
                    "LogPdb.pdb.json").Replace('\\', '/');

                result.success = result.patchedMethods > 0;
                if (!result.success && assemblyErrors.Count > 0)
                {
                    result.error = string.Join("\n", assemblyErrors);
                }
                else if (!result.success && string.IsNullOrEmpty(result.error))
                {
                    result.error = BuildNoPatchDiagnosticMessage(assemblyNames);
                }
            }
            catch (Exception ex)
            {
                result.success = false;
                result.error = ex.Message;
                Debug.LogError("[LogTrackIl] Patch failed: " + ex);
            }
            finally
            {
                sw.Stop();
                LogTrackBenchmark.RecordInsertMs(sw.Elapsed.TotalMilliseconds);
            }

            return result;
        }

        public static IlInstrumentResult PatchAssembly(
            string dllPath,
            LogTrackPdbFile pdb,
            TypeDefinition runtimeType,
            string assemblyNameForPdb)
        {
            var result = new IlInstrumentResult();
            var resolver = LogTrackCecilResolver.CreateResolver();
            var readerParams = LogTrackCecilResolver.CreateReaderParameters(resolver, readWrite: true);

            using var assembly = AssemblyDefinition.ReadAssembly(dllPath, readerParams);
            var module = assembly.MainModule;

            var enableLogTrack = ImportPropertyGetter(module, runtimeType, "EnableLogTrackInternal");
            var pushDepth = ImportMethod(module, runtimeType, "PushDepth", 0);
            var popDepth = ImportMethod(module, runtimeType, "PopDepth", 0);

            if (enableLogTrack == null || pushDepth == null || popDepth == null)
            {
                result.error = "Missing FSPDebuger runtime hooks.";
                return result;
            }

            foreach (var type in module.Types.ToList())
            {
                PatchType(type, module, pdb, assemblyNameForPdb, enableLogTrack, pushDepth, popDepth, runtimeType, ref result);
            }

            if (result.patchedMethods == 0)
            {
                result.error = "未找到可插桩方法（可能已全部插桩、含 try/catch、或为 getter/setter/构造器）。";
                return result;
            }

            assembly.Write(new WriterParameters { WriteSymbols = false });
            result.success = true;
            return result;
        }

        private static void PatchType(
            TypeDefinition type,
            ModuleDefinition module,
            LogTrackPdbFile pdb,
            string assemblyName,
            MethodReference enableLogTrack,
            MethodReference pushDepth,
            MethodReference popDepth,
            TypeDefinition runtimeType,
            ref IlInstrumentResult result)
        {
            foreach (var method in type.Methods)
            {
                if (!ShouldPatchMethod(method, enableLogTrack.DeclaringType.FullName))
                {
                    result.skippedMethods++;
                    continue;
                }

                if (TryPatchMethod(method, module, pdb, assemblyName, enableLogTrack, pushDepth, popDepth, runtimeType))
                {
                    result.patchedMethods++;
                }
                else
                {
                    result.skippedMethods++;
                }
            }

            foreach (var nested in type.NestedTypes)
            {
                PatchType(nested, module, pdb, assemblyName, enableLogTrack, pushDepth, popDepth, runtimeType, ref result);
            }
        }

        private static bool ShouldPatchMethod(MethodDefinition method, string runtimeTypeFullName)
        {
            if (!method.HasBody || method.IsAbstract || method.IsConstructor)
            {
                return false;
            }

            if (method.IsGetter || method.IsSetter)
            {
                return false;
            }

            if (IsUnsafeCompilerGeneratedMethod(method))
            {
                return false;
            }

            if (method.Body.Instructions.Count == 0 || method.Body.ExceptionHandlers.Count > 0)
            {
                return false;
            }

            if (!method.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ret))
            {
                return false;
            }

            if (method.Body.Instructions.Any(instruction => IsFspDebugerHookCall(instruction, runtimeTypeFullName)))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 跳过 async/iterator 状态机等不宜改写 IL 的编译器生成方法；lambda 闭包仍允许插桩。
        /// </summary>
        private static bool IsUnsafeCompilerGeneratedMethod(MethodDefinition method)
        {
            if (method.Name == "SetStateMachine")
            {
                return true;
            }

            var typeName = method.DeclaringType.Name;
            if (method.Name == "MoveNext"
                && (typeName.Contains("d__", StringComparison.Ordinal)
                    || (typeName.StartsWith("<", StringComparison.Ordinal) && typeName.Contains(">d__", StringComparison.Ordinal))))
            {
                return true;
            }

            return false;
        }

        private static bool TryPatchMethod(
            MethodDefinition method,
            ModuleDefinition module,
            LogTrackPdbFile pdb,
            string assemblyName,
            MethodReference enableLogTrack,
            MethodReference pushDepth,
            MethodReference popDepth,
            TypeDefinition runtimeType)
        {
            var argParams = CollectLogParameters(method);
            var logTrackMethod = FindLogTrackOverload(runtimeType, argParams.Count);
            if (logTrackMethod == null)
            {
                return false;
            }

            var className = method.DeclaringType.FullName;
            var funcName = method.Name;
            var hash = pdb.AddItem(0, argParams.Count, assemblyName, 0, string.Empty, className, funcName);
            var importedLogTrack = module.ImportReference(logTrackMethod);

            var il = method.Body.GetILProcessor();
            WidenShortBranches(method.Body);

            var first = method.Body.Instructions[0];
            var prologue = new List<Instruction>
            {
                il.Create(OpCodes.Call, enableLogTrack),
                il.Create(OpCodes.Brfalse, first),
                il.Create(OpCodes.Call, pushDepth),
                il.Create(OpCodes.Ldc_I4, hash),
            };

            for (var i = 0; i < argParams.Count; i++)
            {
                prologue.AddRange(CreateLoadAsIntInstructions(il, argParams[i]));
            }

            prologue.Add(il.Create(OpCodes.Call, importedLogTrack));

            foreach (var instruction in prologue)
            {
                il.InsertBefore(first, instruction);
            }

            InsertPopBeforeReturns(method, il, enableLogTrack, popDepth);
            return true;
        }

        private static List<ParameterDefinition> CollectLogParameters(MethodDefinition method)
        {
            var result = new List<ParameterDefinition>();
            foreach (var parameter in method.Parameters)
            {
                if (!SupportedPrimitiveTypes.Contains(parameter.ParameterType.FullName))
                {
                    continue;
                }

                result.Add(parameter);
                if (result.Count >= MaxArgCount)
                {
                    break;
                }
            }

            return result;
        }

        private static IEnumerable<Instruction> CreateLoadAsIntInstructions(ILProcessor il, ParameterDefinition parameter)
        {
            var instructions = new List<Instruction> { il.Create(OpCodes.Ldarg, parameter) };
            switch (parameter.ParameterType.FullName)
            {
                case "System.Boolean":
                    var falseLabel = il.Create(OpCodes.Ldc_I4_0);
                    var endLabel = il.Create(OpCodes.Nop);
                    instructions.Add(il.Create(OpCodes.Brfalse_S, falseLabel));
                    instructions.Add(il.Create(OpCodes.Ldc_I4_1));
                    instructions.Add(il.Create(OpCodes.Br_S, endLabel));
                    instructions.Add(falseLabel);
                    instructions.Add(endLabel);
                    break;
                case "System.Single":
                case "System.Double":
                case "System.Int64":
                case "System.UInt64":
                    instructions.Add(il.Create(OpCodes.Conv_I4));
                    break;
                case "System.UInt32":
                case "System.UInt16":
                case "System.Byte":
                    instructions.Add(il.Create(OpCodes.Conv_U4));
                    break;
                default:
                    instructions.Add(il.Create(OpCodes.Conv_I4));
                    break;
            }

            return instructions;
        }

        private static void InsertPopBeforeReturns(
            MethodDefinition method,
            ILProcessor il,
            MethodReference enableLogTrack,
            MethodReference popDepth)
        {
            var originalInstructions = method.Body.Instructions.ToList();
            var returns = originalInstructions.Where(instruction => instruction.OpCode == OpCodes.Ret).ToList();
            if (returns.Count == 0)
            {
                return;
            }

            var replacementTargets = new Dictionary<Instruction, Instruction>();
            VariableDefinition returnValue = null;
            if (method.ReturnType.FullName != "System.Void")
            {
                returnValue = new VariableDefinition(method.ReturnType);
                method.Body.Variables.Add(returnValue);
                method.Body.InitLocals = true;
            }

            foreach (var ret in returns)
            {
                Instruction branchTarget;
                if (returnValue != null)
                {
                    var store = il.Create(OpCodes.Stloc, returnValue);
                    branchTarget = store;
                    replacementTargets[ret] = branchTarget;
                    il.InsertBefore(ret, store);
                    il.InsertBefore(ret, il.Create(OpCodes.Call, enableLogTrack));
                    var load = il.Create(OpCodes.Ldloc, returnValue);
                    il.InsertBefore(ret, il.Create(OpCodes.Brfalse, load));
                    il.InsertBefore(ret, il.Create(OpCodes.Call, popDepth));
                    il.InsertBefore(ret, load);
                }
                else
                {
                    var callEnable = il.Create(OpCodes.Call, enableLogTrack);
                    branchTarget = callEnable;
                    replacementTargets[ret] = branchTarget;
                    il.InsertBefore(ret, callEnable);
                    il.InsertBefore(ret, il.Create(OpCodes.Brfalse, ret));
                    il.InsertBefore(ret, il.Create(OpCodes.Call, popDepth));
                }
            }

            foreach (var instruction in originalInstructions)
            {
                if (instruction.Operand is Instruction target && replacementTargets.TryGetValue(target, out var replacement))
                {
                    instruction.Operand = replacement;
                }
                else if (instruction.Operand is Instruction[] targets)
                {
                    for (var i = 0; i < targets.Length; i++)
                    {
                        if (replacementTargets.TryGetValue(targets[i], out var arrayReplacement))
                        {
                            targets[i] = arrayReplacement;
                        }
                    }
                }
            }
        }

        private static MethodDefinition FindLogTrackOverload(TypeDefinition runtimeType, int valueArgCount)
        {
            return runtimeType.Methods.FirstOrDefault(method =>
                method.Name == "LogTrack"
                && method.IsStatic
                && method.Parameters.Count == valueArgCount + 1
                && method.Parameters[0].ParameterType.FullName == "System.Int32"
                && method.Parameters.Skip(1).All(parameter => parameter.ParameterType.FullName == "System.Int32"));
        }

        private static MethodReference ImportMethod(ModuleDefinition module, TypeDefinition runtimeType, string name, int parameterCount)
        {
            var method = runtimeType.Methods.FirstOrDefault(m =>
                m.Name == name && m.Parameters.Count == parameterCount && m.IsStatic);
            return method == null ? null : module.ImportReference(method);
        }

        private static MethodReference ImportPropertyGetter(ModuleDefinition module, TypeDefinition runtimeType, string propertyName)
        {
            var property = runtimeType.Properties.FirstOrDefault(p => p.Name == propertyName);
            if (property?.GetMethod != null)
            {
                return module.ImportReference(property.GetMethod);
            }

            var getter = runtimeType.Methods.FirstOrDefault(method =>
                method.Name == "get_" + propertyName
                && method.IsStatic
                && !method.HasParameters
                && method.ReturnType.FullName == "System.Boolean");
            return getter == null ? null : module.ImportReference(getter);
        }

        private static bool IsFspDebugerHookCall(Instruction instruction, string runtimeTypeFullName)
        {
            if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
            {
                return false;
            }

            if (instruction.Operand is not MethodReference method)
            {
                return false;
            }

            if (method.DeclaringType.FullName != runtimeTypeFullName)
            {
                return false;
            }

            return method.Name == "LogTrack"
                || method.Name == "get_EnableLogTrackInternal"
                || method.Name == "PushDepth"
                || method.Name == "PopDepth";
        }

        private static bool IsLogTrackCall(Instruction instruction, string runtimeTypeFullName)
        {
            return IsFspDebugerHookCall(instruction, runtimeTypeFullName) && instruction.Operand is MethodReference method
                && method.Name == "LogTrack";
        }

        private static void WidenShortBranches(MethodBody body)
        {
            foreach (var instruction in body.Instructions)
            {
                instruction.OpCode = instruction.OpCode.Code switch
                {
                    Code.Beq_S => OpCodes.Beq,
                    Code.Bge_S => OpCodes.Bge,
                    Code.Bge_Un_S => OpCodes.Bge_Un,
                    Code.Bgt_S => OpCodes.Bgt,
                    Code.Bgt_Un_S => OpCodes.Bgt_Un,
                    Code.Ble_S => OpCodes.Ble,
                    Code.Ble_Un_S => OpCodes.Ble_Un,
                    Code.Blt_S => OpCodes.Blt,
                    Code.Blt_Un_S => OpCodes.Blt_Un,
                    Code.Bne_Un_S => OpCodes.Bne_Un,
                    Code.Br_S => OpCodes.Br,
                    Code.Brfalse_S => OpCodes.Brfalse,
                    Code.Brtrue_S => OpCodes.Brtrue,
                    Code.Leave_S => OpCodes.Leave,
                    _ => instruction.OpCode
                };
            }
        }

        private static string BuildNoPatchDiagnosticMessage(IEnumerable<string> assemblyNames)
        {
            var names = (assemblyNames ?? Array.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (names.Length == 0)
            {
                return "未选择任何程序集。";
            }

            var missing = new List<string>();
            var failed = new List<string>();
            foreach (var name in names)
            {
                var dllPath = LogTrackIlAssemblyCatalog.GetDllPath(name);
                if (!File.Exists(dllPath))
                {
                    missing.Add(name + " -> " + dllPath);
                    continue;
                }

                failed.Add(name);
            }

            if (missing.Count == names.Length)
            {
                return "目标 DLL 均不存在，说明脚本尚未编译成功。请先在 Console 解决编译错误，确认生成 Assembly-CSharp.dll。\n"
                    + string.Join("\n", missing);
            }

            if (missing.Count > 0)
            {
                return "部分 DLL 不存在：\n" + string.Join("\n", missing);
            }

            return "DLL 存在但没有任何方法被改写。可能程序集已被插桩、方法均不可插（含 try/catch 等），或 DLL 已被损坏。"
                + "请使用「还原 IL 插桩并重编译」后重试。已选："
                + string.Join(", ", failed);
        }

        /// <summary>
        /// 删除 ScriptAssemblies / Bee 缓存中已插桩或损坏的 DLL，触发从源码重编译。
        /// </summary>
        public static void RestoreAssembliesFromSource(IEnumerable<string> assemblyNames)
        {
            var deleted = new List<string>();
            foreach (var assemblyName in assemblyNames ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(assemblyName))
                {
                    continue;
                }

                DeleteAssemblyArtifacts(assemblyName, deleted);
            }

            if (deleted.Count > 0)
            {
                CompilationPipeline.RequestScriptCompilation();
                Debug.Log("[LogTrackIl] 已删除损坏/已插桩 DLL，正在触发重编译：\n" + string.Join("\n", deleted));
            }
            else
            {
                Debug.LogWarning("[LogTrackIl] 未找到可删除的 DLL。");
            }
        }

        public static bool TryValidateRuntimeHooks(string logTrackClass, out string error)
        {
            error = string.Empty;
            try
            {
                var resolver = LogTrackCecilResolver.CreateResolver();
                using var runtimeAssembly = LogTrackCecilResolver.ReadRuntimeAssembly(resolver);
                var runtimeType = runtimeAssembly.MainModule.GetType(logTrackClass);
                if (runtimeType == null)
                {
                    error = logTrackClass + " not found in LogTrack.Runtime.dll";
                    return false;
                }

                if (ImportPropertyGetter(runtimeAssembly.MainModule, runtimeType, "EnableLogTrackInternal") == null)
                {
                    error = "Missing get_EnableLogTrackInternal on " + logTrackClass + ". Recompile LogTrack.Runtime first.";
                    return false;
                }

                if (ImportMethod(runtimeAssembly.MainModule, runtimeType, "PushDepth", 0) == null
                    || ImportMethod(runtimeAssembly.MainModule, runtimeType, "PopDepth", 0) == null)
                {
                    error = "Missing PushDepth/PopDepth on " + logTrackClass;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static IlInstrumentResult PatchAssemblyAtPath(
            string dllPath,
            string pdbOutputDir,
            string assemblyNameForPdb,
            string logTrackClass = "FSPDebuger")
        {
            var result = new IlInstrumentResult();
            if (!File.Exists(dllPath))
            {
                result.error = "DLL not found: " + dllPath;
                return result;
            }

            if (!TryValidateRuntimeHooks(logTrackClass, out var hookError))
            {
                result.error = hookError;
                return result;
            }

            try
            {
                pdbOutputDir = NormalizeDir(pdbOutputDir);
                Directory.CreateDirectory(pdbOutputDir.TrimEnd(Path.DirectorySeparatorChar));

                var resolver = LogTrackCecilResolver.CreateResolver();
                using var runtimeAssembly = LogTrackCecilResolver.ReadRuntimeAssembly(resolver);
                var runtimeType = runtimeAssembly.MainModule.GetType(logTrackClass);
                var pdbPath = Path.Combine(pdbOutputDir, "LogPdb.pdb.json");
                var pdb = File.Exists(pdbPath) ? LogTrackPdbFile.Open(pdbPath) ?? new LogTrackPdbFile() : new LogTrackPdbFile();

                var assemblyResult = PatchAssembly(dllPath, pdb, runtimeType, assemblyNameForPdb);
                pdb.Save(pdbPath);
                pdb.SaveAsCSV(Path.Combine(pdbOutputDir, "LogPdb.pdb.csv"));

                return assemblyResult;
            }
            catch (Exception ex)
            {
                result.error = ex.Message;
                return result;
            }
        }

        private static void DeleteAssemblyArtifacts(string assemblyName, List<string> deleted)
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                LogTrackIlAssemblyCatalog.GetDllPath(assemblyName),
                Path.ChangeExtension(LogTrackIlAssemblyCatalog.GetDllPath(assemblyName), ".pdb")
            };

            var beeRoot = Path.Combine(projectRoot, "Library", "Bee", "artifacts");
            if (Directory.Exists(beeRoot))
            {
                foreach (var pattern in new[] { assemblyName + ".dll", assemblyName + ".pdb", assemblyName + ".ref.dll" })
                {
                    foreach (var file in Directory.GetFiles(beeRoot, pattern, SearchOption.AllDirectories))
                    {
                        paths.Add(file);
                    }
                }
            }

            foreach (var path in paths)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                File.Delete(path);
                deleted.Add(path);
            }
        }

        public static bool TryGetAssemblyDllStatus(string assemblyName, out string dllPath, out bool exists, out long sizeBytes)
        {
            dllPath = LogTrackIlAssemblyCatalog.GetDllPath(assemblyName);
            if (!File.Exists(dllPath))
            {
                exists = false;
                sizeBytes = 0;
                return false;
            }

            exists = true;
            sizeBytes = new FileInfo(dllPath).Length;
            return true;
        }

        private static string NormalizeDir(string path)
        {
            path = Path.GetFullPath(path);
            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                path += Path.DirectorySeparatorChar;
            }

            return path;
        }

        private static string ToProjectRelativePath(string absoluteDir)
        {
            absoluteDir = absoluteDir.Replace('\\', '/').TrimEnd('/');
            var dataPath = Application.dataPath.Replace('\\', '/');
            if (absoluteDir.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
            {
                return "Assets" + absoluteDir.Substring(dataPath.Length);
            }

            return absoluteDir;
        }
    }
}
#endif
