// Packages/vulcan/Editor/AICallableAttribute.cs
// Mark a static method as callable by AI through the Bridge.
// Only methods with this attribute will be invoked via call_method.

using System;

namespace KDL.Editor
{
    /// <summary>
    /// Mark a static method as callable by AI through the Unity Bridge.
    /// Only public static methods with this attribute can be invoked
    /// via the call_method MCP tool. Methods without this attribute
    /// will be rejected even if they are in whitelisted namespaces.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class AICallableAttribute : Attribute
    {
        /// <summary>
        /// Human-readable description of what this method does.
        /// Used for documentation and AI tool discovery.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Category tag for list_callable lazy-load grouping.
        /// Free-form string; recommended format is two-level dotted, e.g.
        /// "Vulcan.Scene", "Vulcan.Actor.Attr", "AIWorkflow.Diag".
        ///
        /// When null/empty, BridgeServer auto-derives the category from the
        /// declaring type's full name by stripping configured prefixes
        /// (see ProjectSettings/VulcanBridge.json's "categoryStripPrefixes",
        /// falling back to namespaceWhitelist if not configured).
        ///
        /// Used by the list_callable MCP tool to:
        ///   1) list_callable() → list categories with method counts
        ///   2) list_callable(category="...") → list methods in that category
        ///
        /// Setting this explicitly overrides the auto-derived category.
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Tool kind: Read (default) or Write.
        /// "Write" = persists to disk (code edits, config files, .unity / .mat / .prefab / asset modifications).
        /// "Read"  = read-only OR only mutates PlayMode-transient runtime state
        ///           (hp/skills/virtual-keys — cleared when PlayMode exits, never written to disk).
        ///
        /// Purely informational: BridgeServer does NOT enforce — it surfaces [R]/[W] markers in
        /// list_callable output so the LLM can make informed decisions about side-effect risk.
        ///
        /// Note: this is orthogonal to the "⚠ destructive" Description prefix.
        ///   - "⚠ destructive" Description: broader caution flag (covers PlayMode-destructive ops too)
        ///   - Kind = Write:                strict "writes to disk"
        ///   A method can be both Description="⚠ destructive ..." AND Kind = Read (e.g. SetGodMode).
        /// See: Documentation/unityBridge开发文档记录/07-AICallable扩展-设计与实施.md §4.3 / §11.7
        /// </summary>
        public ToolKind Kind { get; set; } = ToolKind.Read;

        public AICallableAttribute(string description = "")
        {
            Description = description;
        }
    }

    /// <summary>
    /// Tool kind for AICallableAttribute.Kind. See that field's documentation.
    /// </summary>
    public enum ToolKind
    {
        /// <summary>Read-only, or mutates only PlayMode-transient runtime state (default).</summary>
        Read = 0,

        /// <summary>Modifies disk state: code, config files, Unity assets/scenes/prefabs.</summary>
        Write = 1,
    }
}
