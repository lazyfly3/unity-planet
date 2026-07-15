// ============================================================================
// EditorSceneOps.cs — Vulcan 正式 Editor 工具：场景与节点操作
// ----------------------------------------------------------------------------
// 命名空间：KDL.Editor.Vulcan
// 文件名前缀 Editor* 表示"操作 Editor 自身能力"（场景/节点查找与控制）
//
// 提供的 [AICallable] 方法：
//   1. FindScene(name)            — 查找 .unity 资产；精确优先，找不到降级模糊
//   2. OpenScene(path)            — 打开指定场景（正式版，独立于 Demo/SceneTools）
//   3. FindNodes(name, mode)      — 在当前激活场景里按名字查节点；mode=exact|contains
//   4. SetNodeActive(...)         — 设置节点激活状态；path 优先，name 兜底
// ============================================================================

#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KDL.Editor;                      // [AICallable]
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KDL.Editor.Vulcan
{
    /// <summary>
    /// 场景与节点操作工具集。给 AI Unity Chat 使用，通过 call_method 反射调用。
    /// </summary>
    public static class EditorSceneOps
    {
        // ─────────────────────────────────────────────────────────────────
        // 1. 查找指定名称的 Scene
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 查找项目内的 .unity 场景资产。
        /// 匹配策略：
        ///   ① 精确匹配：先按"文件名（不含扩展名） == name" 精确查；找到就只返回精确匹配
        ///   ② 降级模糊：精确匹配 0 命中时，按"文件名包含 name 子串"模糊匹配
        ///
        /// 返回格式（多行文本）：
        ///   match=exact  count=N
        ///   Assets/Foo/Bar.unity
        ///   Assets/Baz/Bar.unity
        /// 或：
        ///   match=none  count=0
        /// </summary>
        [AICallable("查找指定名称的场景。先精确匹配文件名（不含 .unity），找不到降级为包含子串的模糊匹配。" +
                    "args: name=EditorScene",
                    Category = "Vulcan.Editor.Scene")]
        public static string FindScene(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "ERROR: 'name' 参数必填";

            // 拿到所有 .unity 路径
            string[] guids = AssetDatabase.FindAssets("t:Scene");
            string[] allPaths = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToArray();

            if (allPaths.Length == 0)
                return "match=none  count=0";

            // ① 精确匹配（按文件名 == name，case-insensitive）
            string[] exact = allPaths
                .Where(p => string.Equals(
                    Path.GetFileNameWithoutExtension(p),
                    name,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p)
                .ToArray();

            if (exact.Length > 0)
            {
                var sb = new StringBuilder();
                sb.Append("match=exact  count=").Append(exact.Length).Append('\n');
                sb.Append(string.Join("\n", exact));
                return sb.ToString();
            }

            // ② 降级模糊匹配（文件名包含子串，case-insensitive）
            string[] fuzzy = allPaths
                .Where(p => Path.GetFileNameWithoutExtension(p)
                    .IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(p => p)
                .ToArray();

            if (fuzzy.Length == 0)
                return "match=none  count=0";

            var sbF = new StringBuilder();
            sbF.Append("match=fuzzy  count=").Append(fuzzy.Length).Append('\n');
            sbF.Append(string.Join("\n", fuzzy));
            return sbF.ToString();
        }

        // ─────────────────────────────────────────────────────────────────
        // 2. 打开指定 Scene（Vulcan 正式版）
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 打开指定路径的场景。Single 模式（替换当前打开的场景）。
        ///
        /// （Vulcan 工具集的正式入口；2026-05-15 起取代历史上 Demo/SceneTools.cs 的同名方法）
        ///
        /// 返回：
        ///   OK: opened=Assets/Foo/Bar.unity  isLoaded=true
        ///   或 ERROR: ...
        /// </summary>
        [AICallable("打开指定路径的场景（Single 模式，会替换当前场景）。" +
                    "args: path=Assets/Scenes/Foo.unity",
                    Category = "Vulcan.Editor.Scene")]
        public static string OpenScene(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "ERROR: 'path' 参数必填";
            if (!File.Exists(path))
                return "ERROR: 场景文件不存在：" + path;
            if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                return "ERROR: 路径不是 .unity 场景文件：" + path;

            try
            {
                // EditorSceneManager.OpenScene 必须主线程；BridgeServer 已保证 [AICallable] 在主线程
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                return "OK: opened=" + scene.path + "  isLoaded=" + scene.isLoaded;
            }
            catch (Exception ex)
            {
                return "ERROR: 打开场景失败：" + ex.Message;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // 3. 查找当前 Scene 的节点
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 在当前激活的场景里按 GameObject 名字查找节点。
        ///
        /// 参数：
        ///   name : 要找的节点名
        ///   mode : "exact" 精确匹配 / "contains" 包含子串（默认 contains）
        ///
        /// 返回（多行）：
        ///   scene=Assets/Foo.unity  count=N  mode=contains
        ///   - path=Canvas/Panel/BtnA  active=true  components=Image,Button,RectTransform
        ///   - path=Canvas/Panel/BtnB  active=false  components=Image,RectTransform
        /// 或 count=0 时只返回头一行。
        ///
        /// **同名节点会返回多个**——SetNodeActive 用 name 兜底时要靠这个先确认唯一性。
        /// </summary>
        [AICallable("在当前激活场景里按名字查节点。mode=exact 精确 / mode=contains 包含（默认 contains）。" +
                    "返回所有匹配节点的 hierarchy 路径 + 激活状态 + 组件名。args: name=PlayerHUD&mode=contains",
                    Category = "Vulcan.Editor.Scene")]
        public static string FindNodes(string name, string mode = "contains")
        {
            if (string.IsNullOrEmpty(name))
                return "ERROR: 'name' 参数必填";

            bool isExact = string.Equals(mode, "exact", StringComparison.OrdinalIgnoreCase);
            // 任何非 "exact" 都按 contains（含 null/空/拼写错），保持宽容

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return "ERROR: 当前没有激活的场景";

            // 收集所有节点。GetRootGameObjects + 递归
            var matches = new List<GameObject>();
            foreach (var root in scene.GetRootGameObjects())
            {
                CollectByName(root, name, isExact, matches);
            }

            var sb = new StringBuilder();
            sb.Append("scene=").Append(scene.path)
              .Append("  count=").Append(matches.Count)
              .Append("  mode=").Append(isExact ? "exact" : "contains");

            foreach (var go in matches)
            {
                sb.Append('\n');
                sb.Append("- path=").Append(GetHierarchyPath(go))
                  .Append("  active=").Append(go.activeSelf ? "true" : "false");

                var comps = go.GetComponents<Component>()
                    .Where(c => c != null)
                    .Select(c => c.GetType().Name);
                sb.Append("  components=").Append(string.Join(",", comps));
            }

            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────────
        // 4. 设置节点激活状态（写操作）
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 设置某节点的 activeSelf。**path 优先，name 是兜底**。
        ///
        /// 参数（path 与 name 至少传一个；都传时 path 生效，name 忽略）：
        ///   path   : hierarchy 路径，例 "Canvas/Panel/Btn"
        ///   name   : GameObject 名字，仅在 path 未提供时生效；当前场景内同名节点必须**唯一**，否则报错
        ///   active : "true" / "false" 字符串（call_method 是字符串协议，所以参数用 string）
        ///   dryRun : "true"（默认）/ "false"。dryRun 模式下只打印计划不真改
        ///
        /// 返回：
        ///   [DRY RUN] would set <path> active=<value>  (currentActive=<old>)
        ///   或 OK: set <path> active=<value>  (was=<old>)
        ///   或 ERROR: ...
        /// </summary>
        [AICallable("⚠ destructive 设置节点激活状态。path 优先；只传 name 时同名节点必须唯一。" +
                    "默认 dryRun=true 只预演，dryRun=false 才真改。" +
                    "args: path=Canvas/Panel/Btn&active=true&dryRun=false",
                    Category = "Vulcan.Editor.Scene",
                    Kind = ToolKind.Write)]
        public static string SetNodeActive(string path = "", string name = "",
                                            string active = "", string dryRun = "true")
        {
            // ── 参数校验 ──
            if (string.IsNullOrEmpty(active))
                return "ERROR: 'active' 参数必填（true/false）";

            bool? targetActive = ParseBool(active);
            if (!targetActive.HasValue)
                return "ERROR: 'active' 必须是 true 或 false，收到：" + active;

            if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(name))
                return "ERROR: 必须提供 'path' 或 'name' 之一";

            bool isDryRun = !string.Equals(dryRun, "false", StringComparison.OrdinalIgnoreCase);

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return "ERROR: 当前没有激活的场景";

            // ── 解析目标节点 ──
            GameObject target = null;
            string resolvedPath;

            if (!string.IsNullOrEmpty(path))
            {
                // path 优先
                target = FindByHierarchyPath(scene, path);
                if (target == null)
                    return "ERROR: 路径未找到节点：" + path;
                resolvedPath = path;
            }
            else
            {
                // name 兜底——必须唯一
                var matches = new List<GameObject>();
                foreach (var root in scene.GetRootGameObjects())
                    CollectByName(root, name, exact: true, matches: matches);

                if (matches.Count == 0)
                    return "ERROR: 当前场景没有名字为 '" + name + "' 的节点";
                if (matches.Count > 1)
                {
                    var paths = string.Join(", ",
                        matches.Select(GetHierarchyPath));
                    return "ERROR: 名字 '" + name + "' 不唯一，匹配 " + matches.Count
                         + " 个节点：" + paths
                         + "。请改用 path 参数精确指定";
                }
                target = matches[0];
                resolvedPath = GetHierarchyPath(target);
            }

            bool oldActive = target.activeSelf;
            bool newActive = targetActive.Value;

            // 幂等：状态已经是目标值
            if (oldActive == newActive)
            {
                return (isDryRun ? "[DRY RUN] " : "OK: ")
                     + "no-op (already active=" + (oldActive ? "true" : "false")
                     + ")  path=" + resolvedPath;
            }

            if (isDryRun)
            {
                return "[DRY RUN] would set " + resolvedPath
                     + " active=" + (newActive ? "true" : "false")
                     + "  (currentActive=" + (oldActive ? "true" : "false") + ")";
            }

            // ── 真改：用 Undo.RecordObject 让 Editor 能 Ctrl+Z ──
            try
            {
                Undo.RecordObject(target, "Vulcan SetNodeActive");
                target.SetActive(newActive);
                EditorUtility.SetDirty(target);

                // 如果在 Edit Mode，标场景脏让用户能保存
                if (!EditorApplication.isPlaying)
                    EditorSceneManager.MarkSceneDirty(scene);

                return "OK: set " + resolvedPath
                     + " active=" + (newActive ? "true" : "false")
                     + "  (was=" + (oldActive ? "true" : "false") + ")";
            }
            catch (Exception ex)
            {
                return "ERROR: SetActive 失败：" + ex.Message;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // 内部 helper
        // ─────────────────────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────────────
        // 5. 设置 MeshRenderer 的材质贴图（写操作）
        // ─────────────────────────────────────────────────────────────────

        [AICallable("⚠ destructive 设置当前场景中某 Renderer 节点 sharedMaterial 的贴图属性。" +
                    "args: path=Cube&texturePath=Assets/Foo/Bar.png&propertyName=_MainTex",
                    Category = "Vulcan.Editor.Renderer",
                    Kind = ToolKind.Write)]
        public static string SetRendererTexture(string path = "", string name = "",
                                                 string texturePath = "",
                                                 string propertyName = "_MainTex")
        {
            if (string.IsNullOrEmpty(texturePath))
                return "ERROR: 'texturePath' 参数必填";
            if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(name))
                return "ERROR: 必须提供 'path' 或 'name' 之一";

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return "ERROR: 当前没有激活的场景";

            GameObject target = null;
            string resolvedPath;
            if (!string.IsNullOrEmpty(path))
            {
                target = FindByHierarchyPath(scene, path);
                if (target == null)
                    return "ERROR: 路径未找到节点：" + path;
                resolvedPath = path;
            }
            else
            {
                var matches = new List<GameObject>();
                foreach (var root in scene.GetRootGameObjects())
                    CollectByName(root, name, exact: true, matches: matches);
                if (matches.Count == 0)
                    return "ERROR: 当前场景没有名字为 '" + name + "' 的节点";
                if (matches.Count > 1)
                    return "ERROR: 名字 '" + name + "' 不唯一，匹配 " + matches.Count + " 个节点";
                target = matches[0];
                resolvedPath = GetHierarchyPath(target);
            }

            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
                return "ERROR: 节点 " + resolvedPath + " 上没有 Renderer 组件";

            var tex = AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
            if (tex == null)
                return "ERROR: 加载贴图失败（路径不存在或不是 Texture 资产）：" + texturePath;

            string propName = string.IsNullOrEmpty(propertyName) ? "_MainTex" : propertyName;

            try
            {
                var mat = renderer.sharedMaterial;
                if (mat == null)
                    return "ERROR: Renderer 的 sharedMaterial 为空：" + resolvedPath;

                Undo.RecordObject(mat, "Vulcan SetRendererTexture");
                mat.SetTexture(propName, tex);
                EditorUtility.SetDirty(mat);

                string matPath = AssetDatabase.GetAssetPath(mat);
                if (!string.IsNullOrEmpty(matPath))
                    AssetDatabase.SaveAssets();

                if (!EditorApplication.isPlaying)
                    EditorSceneManager.MarkSceneDirty(scene);

                return "OK: set " + resolvedPath + " material '" + mat.name + "' " + propName
                     + " = " + texturePath
                     + (string.IsNullOrEmpty(matPath) ? "  (instance material)" : "  (asset=" + matPath + ")");
            }
            catch (Exception ex)
            {
                return "ERROR: SetTexture 失败：" + ex.Message;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // 5b. 设置 Renderer 的 sharedMaterial（整个材质替换，写操作）
        // ─────────────────────────────────────────────────────────────────

        [AICallable("⚠ destructive 设置当前场景中某 Renderer 节点的 sharedMaterial 为指定 .mat 资产。" +
                    "args: path=MyPlane&materialPath=Assets/Foo/Bar.mat",
                    Category = "Vulcan.Editor.Renderer",
                    Kind = ToolKind.Write)]
        public static string SetRendererMaterial(string path = "", string name = "",
                                                  string materialPath = "")
        {
            if (string.IsNullOrEmpty(materialPath))
                return "ERROR: 'materialPath' 参数必填";
            if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(name))
                return "ERROR: 必须提供 'path' 或 'name' 之一";

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return "ERROR: 当前没有激活的场景";

            GameObject target = null;
            string resolvedPath;
            if (!string.IsNullOrEmpty(path))
            {
                target = FindByHierarchyPath(scene, path);
                if (target == null)
                    return "ERROR: 路径未找到节点：" + path;
                resolvedPath = path;
            }
            else
            {
                var matches = new List<GameObject>();
                foreach (var root in scene.GetRootGameObjects())
                    CollectByName(root, name, exact: true, matches: matches);
                if (matches.Count == 0)
                    return "ERROR: 当前场景没有名字为 '" + name + "' 的节点";
                if (matches.Count > 1)
                    return "ERROR: 名字 '" + name + "' 不唯一，匹配 " + matches.Count + " 个节点";
                target = matches[0];
                resolvedPath = GetHierarchyPath(target);
            }

            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
                return "ERROR: 节点 " + resolvedPath + " 上没有 Renderer 组件";

            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null)
                return "ERROR: 加载材质失败（路径不存在或不是 Material 资产）：" + materialPath;

            try
            {
                Undo.RecordObject(renderer, "Vulcan SetRendererMaterial");
                renderer.sharedMaterial = mat;
                EditorUtility.SetDirty(renderer);

                if (!EditorApplication.isPlaying)
                    EditorSceneManager.MarkSceneDirty(scene);

                return "OK: set " + resolvedPath + " sharedMaterial = " + materialPath
                     + "  (shader=" + (mat.shader != null ? mat.shader.name : "<null>") + ")";
            }
            catch (Exception ex)
            {
                return "ERROR: SetRendererMaterial 失败：" + ex.Message;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // 6. 创建一个内建 Primitive 节点（Cube/Sphere/Plane/...）
        // ─────────────────────────────────────────────────────────────────

        [AICallable("在当前激活场景中创建一个 Unity 内建 primitive 节点（Cube/Sphere/Plane/Capsule/Cylinder/Quad）。" +
                    "args: type=Sphere&name=MySphere&x=0&y=0&z=0",
                    Category = "Vulcan.Editor.CreatePrimitive",
                    Kind = ToolKind.Write)]
        public static string CreatePrimitive(string type = "Cube", string name = "",
                                              string x = "0", string y = "0", string z = "0")
        {
            if (string.IsNullOrEmpty(type))
                return "ERROR: 'type' 参数必填（Cube/Sphere/Plane/Capsule/Cylinder/Quad）";

            PrimitiveType primType;
            try
            {
                primType = (PrimitiveType)Enum.Parse(typeof(PrimitiveType), type, ignoreCase: true);
            }
            catch
            {
                return "ERROR: 不支持的 primitive 类型：" + type
                     + "（可选：Cube/Sphere/Plane/Capsule/Cylinder/Quad）";
            }

            float fx = 0f, fy = 0f, fz = 0f;
            float.TryParse(x, out fx);
            float.TryParse(y, out fy);
            float.TryParse(z, out fz);

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return "ERROR: 当前没有激活的场景";

            try
            {
                var go = GameObject.CreatePrimitive(primType);
                if (!string.IsNullOrEmpty(name)) go.name = name;
                go.transform.position = new Vector3(fx, fy, fz);

                Undo.RegisterCreatedObjectUndo(go, "Vulcan CreatePrimitive");

                if (!EditorApplication.isPlaying)
                    EditorSceneManager.MarkSceneDirty(scene);

                return "OK: created " + GetHierarchyPath(go)
                     + "  type=" + primType
                     + "  pos=(" + fx + "," + fy + "," + fz + ")";
            }
            catch (Exception ex)
            {
                return "ERROR: CreatePrimitive 失败：" + ex.Message;
            }
        }

        /// <summary>解析 "true" / "false" / "1" / "0"，case-insensitive。</summary>
        private static bool? ParseBool(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var lower = s.Trim().ToLowerInvariant();
            if (lower == "true" || lower == "1" || lower == "yes") return true;
            if (lower == "false" || lower == "0" || lower == "no") return false;
            return null;
        }

        /// <summary>沿 transform parent 链拼出 hierarchy 路径，例如 "Canvas/Panel/Btn"。</summary>
        private static string GetHierarchyPath(GameObject go)
        {
            if (go == null) return "";
            var parts = new List<string>();
            var t = go.transform;
            while (t != null)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>
        /// 在指定根节点的子树里递归收集名字匹配的 GameObject。
        /// exact=true 时全等比较；否则用 contains（IndexOf）。
        /// </summary>
        private static void CollectByName(GameObject root, string targetName,
                                            bool exact, List<GameObject> matches)
        {
            if (root == null) return;

            bool hit = exact
                ? string.Equals(root.name, targetName, StringComparison.Ordinal)
                : root.name.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0;

            if (hit) matches.Add(root);

            for (int i = 0; i < root.transform.childCount; i++)
            {
                CollectByName(root.transform.GetChild(i).gameObject,
                              targetName, exact, matches);
            }
        }

        /// <summary>
        /// 按 hierarchy 路径（"A/B/C"）在指定 scene 里找 GameObject。
        /// 第一段是根节点名；同名根节点取第一个；中间任一段缺失返回 null。
        /// </summary>
        private static GameObject FindByHierarchyPath(Scene scene, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var segs = path.Split('/');
            if (segs.Length == 0) return null;

            GameObject cur = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == segs[0]) { cur = root; break; }
            }
            if (cur == null) return null;

            for (int i = 1; i < segs.Length; i++)
            {
                var t = cur.transform.Find(segs[i]);
                if (t == null) return null;
                cur = t.gameObject;
            }
            return cur;
        }
    }
}

#endif // UNITY_EDITOR
