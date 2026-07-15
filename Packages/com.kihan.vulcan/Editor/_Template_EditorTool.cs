// ============================================================================
// _Template_EditorTool.cs — Vulcan 正式 Editor 工具模板
// ----------------------------------------------------------------------------
// ⚠ 这个文件仅供复制参考，**不会被编译**（VULCAN_TOOL_TEMPLATES 默认未定义）。
//
// 怎么用：
//   1. 复制本文件到 Assets/Scripts/AIGen/Editor/Vulcan/EditorXxx.cs（替换 Xxx）
//   2. 删掉文件顶部的 `#if VULCAN_TOOL_TEMPLATES` 和文件底部的 `#endif`，
//      但保留 `#if UNITY_EDITOR` / `#endif`（Editor-only 编译保护）
//   3. 把类名 `_TemplateEditorTool` 改成你的类名（PascalCase，跟文件名一致）
//   4. 替换示例方法为你的真实业务方法
//   5. 按照 vulcan/Vulcan/VulcanToUnityBridge/Documentation/UnityMCP开发文档记录/MCP工具开发规范.md 调整方法签名 / 描述 / 返回值
//   6. 按 vulcan/Vulcan/VulcanToUnityBridge/Documentation/UnityMCP开发文档记录/新增MCP工具CheckList.md 把工具登记到所有该登记的地方
//
// 命名空间：KDL.Editor.Vulcan （已在 ProjectSettings/VulcanBridge.json
// 的 namespaceWhitelist `KDL.Editor.` 前缀范围内，无需改白名单）
// 所有 Vulcan 正式工具（无论 Editor 工具还是 Gameplay 工具）统一用这一个命名空间，
// 通过文件名前缀（EditorXxx.cs / GameplayXxx.cs）区分用途。
// ============================================================================

#if VULCAN_TOOL_TEMPLATES   // ← 默认未定义，本文件不编译；复制时删除这行 + 末尾 #endif
#if UNITY_EDITOR

using System;
using System.Linq;
using System.Text;
using KDL.Editor;                    // [AICallable] 特性所在
using UnityEditor;
using UnityEngine;

namespace KDL.Editor.Vulcan
{
    /// <summary>
    /// 【模板】Editor 工具示例：操作 Editor 自身能力（场景 / 资产 / Inspector 等）。
    ///
    /// 命名规则：类名 = 文件名（去掉 _Template_ 前缀）；类名 PascalCase。
    /// 一个文件一个类，按"领域"拆分（例如 EditorSceneOps / EditorAssetOps / EditorBuildOps）。
    /// </summary>
    public static class _TemplateEditorTool
    {
        // ─────────────────────────────────────────────────────────────────
        // 示例 1：无参方法，返回字符串列表
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 模板示例：列出当前选中的 Asset 路径。
        /// </summary>
        [AICallable("【模板】列出当前 Project 窗口选中的资产路径，按字典序返回，每行一条")]
        public static string ListSelectedAssets()
        {
            var paths = Selection.assetGUIDs
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .OrderBy(p => p)
                .ToArray();

            if (paths.Length == 0)
                return "(empty: 当前没有选中任何资产)";

            return string.Join("\n", paths);
        }

        // ─────────────────────────────────────────────────────────────────
        // 示例 2：有参方法，参数 case-insensitive 匹配
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 模板示例：返回某资产的 GUID。
        ///
        /// 客户端（AI）调用方式：
        ///   call_method type=KDL.Editor.Vulcan._TemplateEditorTool
        ///               method=GetAssetGuid args=path=Assets/Foo.prefab
        /// </summary>
        [AICallable("【模板】根据资产路径返回 GUID。args: path=Assets/.../foo.asset")]
        public static string GetAssetGuid(string path)
        {
            // ── 必备：参数校验，错误统一用 "ERROR: ..." 开头返回 ──
            if (string.IsNullOrEmpty(path))
                return "ERROR: 'path' 参数必填";

            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
                return "ERROR: 资产不存在或未导入：" + path;

            return guid;
        }

        // ─────────────────────────────────────────────────────────────────
        // 示例 3：写操作（破坏性）— 必须在描述里加 ⚠ 警告
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 模板示例：把某 Asset 重命名（破坏性写操作）。
        ///
        /// 写操作必须在 [AICallable] 描述里以 "⚠ destructive" 标注，
        /// 让模型知道这是不可逆操作，触发更严格的"先告知再执行"行为。
        /// </summary>
        [AICallable("⚠ destructive 【模板】重命名资产。args: path=Assets/Foo.asset&newName=Bar")]
        public static string RenameAsset(string path, string newName)
        {
            if (string.IsNullOrEmpty(path)) return "ERROR: 'path' 必填";
            if (string.IsNullOrEmpty(newName)) return "ERROR: 'newName' 必填";

            string err = AssetDatabase.RenameAsset(path, newName);
            if (!string.IsNullOrEmpty(err))
                return "ERROR: 重命名失败：" + err;

            AssetDatabase.SaveAssets();
            return "OK: " + path + " → " + newName;
        }

        // ─────────────────────────────────────────────────────────────────
        // 示例 4：返回结构化信息（多字段时用纯文本表格 / 简单 JSON）
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 模板示例：返回工程当前状态摘要。
        /// 多字段输出推荐"key=value 多行" 或紧凑 JSON，不要返回花哨的 Markdown 表格
        /// （会浪费 token，模型解析也不见得更准）。
        /// </summary>
        [AICallable("【模板】返回 Editor 简要状态：是否在编译/PlayMode/打开的场景路径")]
        public static string GetEditorBriefStatus()
        {
            var sb = new StringBuilder();
            sb.Append("isCompiling=").Append(EditorApplication.isCompiling).Append('\n');
            sb.Append("isPlaying=").Append(EditorApplication.isPlaying).Append('\n');
            sb.Append("isPaused=").Append(EditorApplication.isPaused).Append('\n');
            sb.Append("activeScene=").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
            return sb.ToString();
        }
    }
}

#endif // UNITY_EDITOR
#endif // VULCAN_TOOL_TEMPLATES
