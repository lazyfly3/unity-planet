using ModularAssembly;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    public readonly struct VehicleModuleDamageFeedback
    {
        public readonly string RuntimeId;
        public readonly string DisplayName;
        public readonly Vector3 HitPoint;
        public readonly Vector3 Impulse;
        public readonly Bounds WorldBounds;
        public readonly float HealthRatio;
        public readonly bool Destroyed;
        public readonly float DamageAmount;
        public readonly GridModuleCategory Category;
        public readonly bool IsCore;

        public VehicleModuleDamageFeedback(
            string runtimeId,
            string displayName,
            Vector3 hitPoint,
            Vector3 impulse,
            Bounds worldBounds,
            float healthRatio,
            bool destroyed,
            float damageAmount,
            GridModuleCategory category,
            bool isCore)
        {
            RuntimeId = runtimeId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            HitPoint = hitPoint;
            Impulse = impulse;
            WorldBounds = worldBounds;
            HealthRatio = Mathf.Clamp01(healthRatio);
            Destroyed = destroyed;
            DamageAmount = Mathf.Max(0f, damageAmount);
            Category = category;
            IsCore = isCore;
        }
    }

    public sealed class VehicleDamageFeedbackPresenter : MonoBehaviour
    {
        VehicleStructureGraph graph;
        Camera sceneCamera;
        Transform vehicleRoot;
        VehicleModuleDamageFeedback lastHit;
        Vector2 incomingDirection = Vector2.up;
        string primaryMessage = string.Empty;
        string secondaryMessage = string.Empty;
        Color feedbackColor = Color.white;
        float hitMarkerUntil;
        float directionUntil;
        float messageUntil;
        GUIStyle bannerStyle;
        GUIStyle detailStyle;
        GUIStyle arrowStyle;

        public void Initialize(
            VehicleStructureGraph source,
            Camera camera,
            Transform root)
        {
            Unsubscribe();
            graph = source;
            sceneCamera = camera;
            vehicleRoot = root;
            if (graph == null)
                return;
            graph.ModuleDamaged += HandleModuleDamaged;
            graph.StructureChanged += HandleStructureChanged;
            graph.Destroyed += HandleVehicleDestroyed;
        }

        void HandleModuleDamaged(VehicleModuleDamageFeedback feedback)
        {
            lastHit = feedback;
            float now = Time.unscaledTime;
            hitMarkerUntil = now + (feedback.Destroyed ? 1.15f : 0.7f);
            directionUntil = now + 0.9f;
            messageUntil = now + (feedback.Destroyed ? 1.8f : 1.1f);
            feedbackColor = feedback.Destroyed
                ? new Color(1f, 0.22f, 0.08f, 1f)
                : new Color(1f, 0.66f, 0.12f, 1f);
            string moduleName = string.IsNullOrWhiteSpace(feedback.DisplayName)
                ? "模块"
                : feedback.DisplayName;
            primaryMessage = feedback.Destroyed
                ? moduleName + " 已摧毁"
                : moduleName + " 受击";
            secondaryMessage = feedback.Destroyed
                ? "该模块已退出质量、推力、气动和武器计算"
                : "完整度 " +
                  Mathf.RoundToInt(feedback.HealthRatio * 100f) +
                  "%";
            incomingDirection = ResolveIncomingDirection(
                feedback.HitPoint,
                feedback.Impulse);
        }

        void HandleStructureChanged(VehicleStructureDelta delta)
        {
            if (delta == null ||
                string.IsNullOrWhiteSpace(delta.DirectHitRuntimeId))
                return;
            int detachedCount = Mathf.Max(
                0,
                delta.RemovedRuntimeIds.Count - 1);
            if (delta.CoreDestroyed)
            {
                primaryMessage = "核心损毁";
                secondaryMessage = "载具失去战斗能力";
            }
            else if (detachedCount > 0)
            {
                primaryMessage = "结构连接断裂";
                secondaryMessage =
                    detachedCount + " 个关联模块脱落并立即失效";
            }
            else
            {
                return;
            }
            feedbackColor = new Color(1f, 0.18f, 0.07f, 1f);
            messageUntil = Time.unscaledTime + 2.2f;
        }

        void HandleVehicleDestroyed()
        {
            primaryMessage = "载具失去战斗能力";
            secondaryMessage = "核心或核心连通结构已失效";
            feedbackColor = new Color(1f, 0.12f, 0.05f, 1f);
            messageUntil = Time.unscaledTime + 2.5f;
        }

        Vector2 ResolveIncomingDirection(
            Vector3 hitPoint,
            Vector3 impulse)
        {
            if (sceneCamera == null)
                return Vector2.up;
            Vector3 origin = vehicleRoot != null
                ? vehicleRoot.position
                : transform.position;
            Vector3 worldDirection = hitPoint - origin;
            if (worldDirection.sqrMagnitude < 0.001f)
                worldDirection = -impulse;
            if (worldDirection.sqrMagnitude < 0.001f)
                return Vector2.up;
            Vector3 local = sceneCamera.transform.InverseTransformDirection(
                worldDirection.normalized);
            Vector2 result = new Vector2(local.x, -local.y);
            if (local.z < 0f)
                result = -result;
            return result.sqrMagnitude > 0.001f
                ? result.normalized
                : Vector2.up;
        }

        void EnsureStyles()
        {
            if (bannerStyle != null)
                return;
            bannerStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 19,
                fontStyle = FontStyle.Bold,
                wordWrap = false
            };
            detailStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Normal
            };
            arrowStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 28,
                fontStyle = FontStyle.Bold
            };
        }

        void OnGUI()
        {
            if (sceneCamera == null)
                return;
            EnsureStyles();
            float now = Time.unscaledTime;
            if (now < hitMarkerUntil)
                DrawModuleMarker(lastHit.WorldBounds);
            if (now < directionUntil)
                DrawIncomingDirection();
            if (now < messageUntil)
                DrawMessage();
        }

        void DrawMessage()
        {
            float remaining = messageUntil - Time.unscaledTime;
            float alpha = Mathf.Clamp01(remaining / 0.25f);
            Color oldColor = GUI.color;
            Color color = feedbackColor;
            color.a *= alpha;
            GUI.color = new Color(0.01f, 0.035f, 0.055f, 0.88f * alpha);
            Rect panel = new Rect(
                Screen.width * 0.5f - 210f,
                Screen.height * 0.13f,
                420f,
                66f);
            GUI.Box(panel, GUIContent.none);
            GUI.color = color;
            GUI.Label(
                new Rect(panel.x + 8f, panel.y + 6f, panel.width - 16f, 28f),
                primaryMessage,
                bannerStyle);
            GUI.color = new Color(0.86f, 0.95f, 1f, alpha);
            GUI.Label(
                new Rect(panel.x + 8f, panel.y + 34f, panel.width - 16f, 24f),
                secondaryMessage,
                detailStyle);
            GUI.color = oldColor;
        }

        void DrawIncomingDirection()
        {
            float remaining = directionUntil - Time.unscaledTime;
            float alpha = Mathf.Clamp01(remaining / 0.2f);
            Vector2 center = new Vector2(
                Screen.width * 0.5f,
                Screen.height * 0.5f);
            float radius = Mathf.Clamp(
                Mathf.Min(Screen.width, Screen.height) * 0.115f,
                72f,
                126f);
            Vector2 position = center + incomingDirection * radius;
            float angle =
                Mathf.Atan2(incomingDirection.y, incomingDirection.x) *
                Mathf.Rad2Deg +
                90f;
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUIUtility.RotateAroundPivot(angle, position);
            GUI.color = new Color(1f, 0.32f, 0.08f, alpha);
            GUI.Label(
                new Rect(position.x - 18f, position.y - 18f, 36f, 36f),
                "▲",
                arrowStyle);
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        void DrawModuleMarker(Bounds worldBounds)
        {
            if (!TryProjectBounds(worldBounds, out Rect rect))
                return;
            float remaining = hitMarkerUntil - Time.unscaledTime;
            float alpha = Mathf.Clamp01(remaining / 0.2f);
            Color oldColor = GUI.color;
            GUI.color = new Color(
                feedbackColor.r,
                feedbackColor.g,
                feedbackColor.b,
                alpha);
            const float thickness = 2f;
            float corner = Mathf.Clamp(
                Mathf.Min(rect.width, rect.height) * 0.28f,
                10f,
                24f);
            DrawSolid(new Rect(rect.xMin, rect.yMin, corner, thickness));
            DrawSolid(new Rect(rect.xMin, rect.yMin, thickness, corner));
            DrawSolid(new Rect(rect.xMax - corner, rect.yMin, corner, thickness));
            DrawSolid(new Rect(rect.xMax - thickness, rect.yMin, thickness, corner));
            DrawSolid(new Rect(rect.xMin, rect.yMax - thickness, corner, thickness));
            DrawSolid(new Rect(rect.xMin, rect.yMax - corner, thickness, corner));
            DrawSolid(new Rect(rect.xMax - corner, rect.yMax - thickness, corner, thickness));
            DrawSolid(new Rect(rect.xMax - thickness, rect.yMax - corner, thickness, corner));
            GUI.color = oldColor;
        }

        bool TryProjectBounds(Bounds bounds, out Rect result)
        {
            result = default;
            Vector3 center = bounds.center;
            Vector3 extents = Vector3.Max(
                bounds.extents,
                Vector3.one * 0.15f);
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            int visibleCorners = 0;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 screen = sceneCamera.WorldToScreenPoint(
                    center + new Vector3(
                        extents.x * x,
                        extents.y * y,
                        extents.z * z));
                if (screen.z <= 0.01f)
                    continue;
                visibleCorners++;
                float guiY = Screen.height - screen.y;
                minX = Mathf.Min(minX, screen.x);
                minY = Mathf.Min(minY, guiY);
                maxX = Mathf.Max(maxX, screen.x);
                maxY = Mathf.Max(maxY, guiY);
            }
            if (visibleCorners == 0)
                return false;
            minX = Mathf.Clamp(minX - 5f, 2f, Screen.width - 2f);
            maxX = Mathf.Clamp(maxX + 5f, 2f, Screen.width - 2f);
            minY = Mathf.Clamp(minY - 5f, 2f, Screen.height - 2f);
            maxY = Mathf.Clamp(maxY + 5f, 2f, Screen.height - 2f);
            float width = Mathf.Max(18f, maxX - minX);
            float height = Mathf.Max(18f, maxY - minY);
            result = new Rect(
                minX,
                minY,
                Mathf.Min(width, Screen.width - minX - 2f),
                Mathf.Min(height, Screen.height - minY - 2f));
            return result.width > 1f && result.height > 1f;
        }

        static void DrawSolid(Rect rect)
        {
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }

        void Unsubscribe()
        {
            if (graph == null)
                return;
            graph.ModuleDamaged -= HandleModuleDamaged;
            graph.StructureChanged -= HandleStructureChanged;
            graph.Destroyed -= HandleVehicleDestroyed;
        }

        void OnDestroy()
        {
            Unsubscribe();
        }
    }
}
