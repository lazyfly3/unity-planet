// ============================================================================
// _Template_GameplayTool.cs — Vulcan 正式 Gameplay 语义工具模板
// ----------------------------------------------------------------------------
// ⚠ 这个文件仅供复制参考，**不会被编译**（VULCAN_TOOL_TEMPLATES 默认未定义）。
//
// "Gameplay 语义"含义：
//   方法操作的是 **运行时 / 业务侧** 的数据（ScriptableObject 配置、Prefab 字段、
//   PlayMode 中的 GameObject / Component 状态等），但 **执行环境仍是 Unity Editor**
//   （因为 BridgeServer 只在 Editor 跑）。
//
//   未来如果需要 PlayMode 下的真·运行时能力（比如运行时改某 Singleton 字段），
//   要扩 BridgeServer 到 Runtime assembly，那时新建 RuntimeXxx.cs。当前 Runtime*
//   命名空间是占位状态，没启用。
//
// 怎么用：
//   1. 复制本文件到 Assets/Scripts/AIGen/Editor/Vulcan/GameplayXxx.cs（替换 Xxx）
//   2. 删掉 `#if VULCAN_TOOL_TEMPLATES` / `#endif`，保留 `#if UNITY_EDITOR`
//   3. 改类名为 GameplayXxx，跟文件名一致
//   4. 写真实业务方法
//   5. 按 vulcan/Vulcan/VulcanToUnityBridge/Documentation/UnityMCP开发文档记录/MCP工具开发规范.md + 新增MCP工具CheckList.md 流程登记
//
// 命名空间：KDL.Editor.Vulcan （所有 Vulcan 正式工具统一用这一个命名空间，
// 不再区分 EditorTools / GameplayTools 子空间；用途靠文件名前缀区分）
// ============================================================================

#if VULCAN_TOOL_TEMPLATES   // ← 默认未定义，本文件不编译；复制时删除这行 + 末尾 #endif
#if UNITY_EDITOR

using System;
using System.IO;
using System.Linq;
using System.Text;
using KDL.Editor;                    // [AICallable] 特性所在
using UnityEditor;
using UnityEngine;

namespace KDL.Editor.Vulcan
{
    /// <summary>
    /// 【模板】Gameplay 语义工具示例：操作业务数据，执行在 Editor。
    ///
    /// 适用场景：
    ///   - 加载 ScriptableObject 看字段值
    ///   - 批量改 Prefab 上某组件的字段
    ///   - 查看 PlayMode 下某 Singleton 的状态
    ///   - 触发某编辑器内的 Gameplay 仿真流程
    /// </summary>
    public static class _TemplateGameplayTool
    {
        // ─────────────────────────────────────────────────────────────────
        // 示例 1：读取业务 ScriptableObject 配置
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 模板示例：读某 ScriptableObject 配置并返回其字段值（JSON 形式）。
        /// </summary>
        [AICallable("【模板】读取某 ScriptableObject 配置。args: path=Assets/Configs/Foo.asset")]
        public static string ReadScriptableObject(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "ERROR: 'path' 参数必填";

            var so = AssetDatabase.LoadMainAssetAtPath(path);
            if (so == null)
                return "ERROR: 资产不存在：" + path;
            if (!(so is ScriptableObject))
                return "ERROR: 不是 ScriptableObject：" + path + "（type=" + so.GetType().Name + "）";

            // EditorJsonUtility 能序列化 SerializedField，比 JsonUtility 更全
            return EditorJsonUtility.ToJson(so, prettyPrint: true);
        }

        // ─────────────────────────────────────────────────────────────────
        // 示例 2：查看 PlayMode 下的 Singleton 状态（只读，安全）
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 模板示例：检查某全局服务的当前状态。
        /// 这种"运行时只读"操作放在 Gameplay 工具里很合适。
        /// </summary>
        [AICallable("【模板】检查 PlayMode 状态下游戏管理器是否就绪。" +
                    "返回 'ready' / 'not_in_playmode' / 'not_initialized'")]
        public static string CheckGameManagerReady()
        {
            if (!EditorApplication.isPlaying)
                return "not_in_playmode";

            // 真实使用时把 SomeGameManager 替换成项目里的具体类
            // var gm = UnityEngine.Object.FindObjectOfType<SomeGameManager>();
            // return gm != null && gm.IsInitialized ? "ready" : "not_initialized";

            return "ready";  // ← 模板占位
        }

        // ─────────────────────────────────────────────────────────────────
        // 示例 3：批量写 Prefab 字段（破坏性，必须 ⚠ destructive 标注）
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 模板示例：批量修改某目录下所有 Prefab 上某组件的某字段值（写操作）。
        /// 写操作必须严谨：
        ///   1. 描述加 ⚠ destructive 标记
        ///   2. 必须有 dryRun 参数让 AI / 用户先确认
        ///   3. 必须用 PrefabUtility 正确保存
        ///   4. 失败要有清晰错误返回，不要静默
        /// </summary>
        [AICallable("⚠ destructive 【模板】批量修改 Prefab 上某字段。" +
                    "args: dir=Assets/Prefabs&componentName=MyComp&fieldName=speed&newValue=10&dryRun=true")]
        public static string BatchSetPrefabField(string dir, string componentName,
                                                  string fieldName, string newValue,
                                                  string dryRun = "true")
        {
            if (string.IsNullOrEmpty(dir)) return "ERROR: 'dir' 必填";
            if (string.IsNullOrEmpty(componentName)) return "ERROR: 'componentName' 必填";
            if (string.IsNullOrEmpty(fieldName)) return "ERROR: 'fieldName' 必填";

            bool isDryRun = string.Equals(dryRun, "true", StringComparison.OrdinalIgnoreCase);

            // ── 模板里只展示骨架，真实实现根据业务自己写 ──
            var sb = new StringBuilder();
            sb.Append(isDryRun ? "[DRY RUN] " : "[APPLY] ");
            sb.Append("会扫描 ").Append(dir)
              .Append(" 下所有 Prefab，找 ").Append(componentName)
              .Append(".").Append(fieldName)
              .Append(" 改成 ").Append(newValue).Append('\n');
            sb.Append("scanned=0 modified=0 errors=0  (template stub)");

            return sb.ToString();
        }
    }
}

#endif // UNITY_EDITOR
#endif // VULCAN_TOOL_TEMPLATES
