#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Compilation;

namespace LogTrack.Editor
{
    internal static class LogTrackCompileWait
    {
        public static void EnsureCompiled(Action onComplete, Action<string> onError = null)
        {
            if (!EditorApplication.isCompiling)
            {
                onComplete?.Invoke();
                return;
            }

            EditorApplication.update += WaitUpdate;

            void WaitUpdate()
            {
                if (EditorApplication.isCompiling)
                {
                    return;
                }

                EditorApplication.update -= WaitUpdate;
                onComplete?.Invoke();
            }
        }

        public static void RequestCompileAndWait(Action onComplete, Action<string> onError = null)
        {
            if (!EditorApplication.isCompiling)
            {
                CompilationPipeline.RequestScriptCompilation();
            }

            var timeoutAt = EditorApplication.timeSinceStartup + 120d;
            EditorApplication.update += WaitUpdate;

            void WaitUpdate()
            {
                if (EditorApplication.isCompiling)
                {
                    if (EditorApplication.timeSinceStartup > timeoutAt)
                    {
                        EditorApplication.update -= WaitUpdate;
                        onError?.Invoke("Compilation timeout after 120s");
                    }

                    return;
                }

                EditorApplication.update -= WaitUpdate;
                onComplete?.Invoke();
            }
        }
    }
}
#endif
