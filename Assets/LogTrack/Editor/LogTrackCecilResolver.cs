#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using UnityEditor;
using UnityEngine;

namespace LogTrack.Editor
{
    internal static class LogTrackCecilResolver
    {
        public static string ScriptAssembliesDir =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "ScriptAssemblies");

        public static DefaultAssemblyResolver CreateResolver()
        {
            var resolver = new DefaultAssemblyResolver();
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;

            AddSearchDirectory(resolver, ScriptAssembliesDir);
            AddSearchDirectory(resolver, Path.Combine(projectRoot, "Library", "Bee", "artifacts"));
            AddSearchDirectory(resolver, Path.Combine(EditorApplication.applicationContentsPath, "Managed"));
            AddSearchDirectory(resolver, Path.Combine(EditorApplication.applicationContentsPath, "NetStandard", "ref", "2.1.0"));

            return resolver;
        }

        public static ReaderParameters CreateReaderParameters(DefaultAssemblyResolver resolver, bool readWrite = true)
        {
            return new ReaderParameters
            {
                ReadWrite = readWrite,
                InMemory = false,
                AssemblyResolver = resolver
            };
        }

        public static AssemblyDefinition ReadRuntimeAssembly(DefaultAssemblyResolver resolver)
        {
            var runtimeDll = Path.Combine(ScriptAssembliesDir, "LogTrack.Runtime.dll");
            if (!File.Exists(runtimeDll))
            {
                throw new FileNotFoundException("LogTrack.Runtime.dll not found. Compile the project first.", runtimeDll);
            }

            return AssemblyDefinition.ReadAssembly(runtimeDll, CreateReaderParameters(resolver, readWrite: false));
        }

        public static string ResolveCecilAssemblyPath()
        {
            var candidates = new[]
            {
                Path.Combine(EditorApplication.applicationContentsPath, "Tools", "BuildPipeline", "Unity.Cecil.dll"),
                Path.Combine(EditorApplication.applicationContentsPath, "Managed", "Unity.Cecil.dll")
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return candidates[0];
        }

        private static void AddSearchDirectory(DefaultAssemblyResolver resolver, string path)
        {
            if (Directory.Exists(path))
            {
                resolver.AddSearchDirectory(path);
            }
        }
    }
}
#endif
