using System;
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

    public readonly struct CombatDamageAppliedFeedback
    {
        public readonly VehicleCombatTeam SourceTeam;
        public readonly VehicleCombatTeam TargetTeam;
        public readonly Vector3 HitPoint;
        public readonly float DamageAmount;
        public readonly bool Destroyed;

        public CombatDamageAppliedFeedback(
            VehicleCombatTeam sourceTeam,
            VehicleCombatTeam targetTeam,
            Vector3 hitPoint,
            float damageAmount,
            bool destroyed)
        {
            SourceTeam = sourceTeam;
            TargetTeam = targetTeam;
            HitPoint = hitPoint;
            DamageAmount = Mathf.Max(0f, damageAmount);
            Destroyed = destroyed;
        }
    }

    // Presentation-only notification. Damage remains authoritative in the
    // existing ISpaceDamageable implementations; listeners cannot change it.
    public static class CombatDamageFeedbackBus
    {
        public static event Action<CombatDamageAppliedFeedback> DamageApplied;

        internal static void Report(CombatDamageAppliedFeedback feedback)
        {
            DamageApplied?.Invoke(feedback);
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            DamageApplied = null;
        }
    }

    public sealed class VehicleDamageFeedbackPresenter : MonoBehaviour
    {
        VehicleStructureGraph graph;
        Camera sceneCamera;
        Transform vehicleRoot;
        GridLabCameraController cameraController;
        Vector2 incomingDirection = Vector2.up;
        string primaryMessage = string.Empty;
        string secondaryMessage = string.Empty;
        float incomingStartedAt;
        float incomingUntil;
        float incomingStrength;
        float confirmationStartedAt;
        float confirmationUntil;
        float lastConfirmationSoundAt = float.NegativeInfinity;
        bool confirmedDestruction;
        float criticalMessageUntil;
        GUIStyle criticalStyle;
        GUIStyle detailStyle;
        Texture2D damageVignette;
        AudioSource feedbackAudio;
        AudioClip incomingHitClip;
        AudioClip hitConfirmClip;
        AudioClip destructionConfirmClip;

        public void Initialize(
            VehicleStructureGraph source,
            Camera camera,
            Transform root)
        {
            Initialize(source, camera, root, null);
        }

        public void Initialize(
            VehicleStructureGraph source,
            Camera camera,
            Transform root,
            GridLabCameraController controller)
        {
            Unsubscribe();
            graph = source;
            sceneCamera = camera;
            vehicleRoot = root;
            cameraController = controller;
            CombatDamageFeedbackBus.DamageApplied +=
                HandleDamageApplied;
            EnsureAudio();
            if (graph == null)
                return;
            graph.ModuleDamaged += HandleModuleDamaged;
            graph.StructureChanged += HandleStructureChanged;
            graph.Destroyed += HandleVehicleDestroyed;
        }

        void HandleModuleDamaged(VehicleModuleDamageFeedback feedback)
        {
            float now = Time.unscaledTime;
            float rawDamageWeight = 1f - Mathf.Exp(
                -feedback.DamageAmount / 45f);
            float healthWeight = 1f - feedback.HealthRatio;
            float severity = feedback.Destroyed
                ? 1f
                : Mathf.Clamp01(
                    0.22f + rawDamageWeight * 0.48f +
                    healthWeight * 0.3f);
            incomingStrength = Mathf.Max(
                incomingStrength * 0.62f,
                severity);
            incomingStartedAt = now;
            incomingUntil = now + Mathf.Lerp(0.24f, 0.52f, severity);
            incomingDirection = ResolveIncomingDirection(
                feedback.HitPoint,
                feedback.Impulse);
            Vector3 origin = vehicleRoot != null
                ? vehicleRoot.position
                : transform.position;
            cameraController?.AddDamageImpulse(
                feedback.HitPoint - origin,
                severity);
            if (feedbackAudio != null && incomingHitClip != null)
                feedbackAudio.PlayOneShot(
                    incomingHitClip,
                    Mathf.Lerp(0.28f, 0.62f, severity));

            if (!feedback.Destroyed)
                return;
            string moduleName = string.IsNullOrWhiteSpace(feedback.DisplayName)
                ? "模块"
                : feedback.DisplayName;
            primaryMessage = moduleName + " 已摧毁";
            secondaryMessage = "操控、火力或结构性能已发生实际变化";
            criticalMessageUntil = now + 1.65f;
        }

        void HandleDamageApplied(CombatDamageAppliedFeedback feedback)
        {
            if (feedback.SourceTeam != VehicleCombatTeam.Player ||
                feedback.TargetTeam != VehicleCombatTeam.Enemy)
                return;
            float now = Time.unscaledTime;
            bool continuing = now < confirmationUntil;
            confirmedDestruction = feedback.Destroyed ||
                                   (continuing && confirmedDestruction);
            confirmationStartedAt = now;
            confirmationUntil = now +
                (confirmedDestruction ? 0.24f : 0.14f);
            if (feedbackAudio == null ||
                now - lastConfirmationSoundAt < 0.045f)
                return;
            AudioClip clip = confirmedDestruction
                ? destructionConfirmClip
                : hitConfirmClip;
            if (clip != null)
            {
                feedbackAudio.PlayOneShot(
                    clip,
                    confirmedDestruction ? 0.48f : 0.3f);
                lastConfirmationSoundAt = now;
            }
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
            criticalMessageUntil = Time.unscaledTime + 2.2f;
        }

        void HandleVehicleDestroyed()
        {
            primaryMessage = "载具失去战斗能力";
            secondaryMessage = "核心或核心连通结构已失效";
            criticalMessageUntil = Time.unscaledTime + 2.5f;
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
            if (criticalStyle != null)
                return;
            criticalStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                wordWrap = false
            };
            detailStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = FontStyle.Normal
            };
        }

        void OnGUI()
        {
            if (sceneCamera == null)
                return;
            EnsureStyles();
            float now = Time.unscaledTime;
            if (now < incomingUntil)
            {
                DrawDamageVignette();
                DrawIncomingDirection();
            }
            if (now < confirmationUntil)
                DrawHitConfirmation();
            if (now < criticalMessageUntil)
                DrawCriticalStatus();
        }

        void DrawCriticalStatus()
        {
            float remaining = criticalMessageUntil - Time.unscaledTime;
            float alpha = Mathf.Clamp01(remaining / 0.25f);
            Color oldColor = GUI.color;
            Rect panel = new Rect(
                Screen.width * 0.5f - 230f,
                Screen.height - 176f,
                460f,
                52f);
            GUI.color = new Color(0f, 0f, 0f, 0.72f * alpha);
            GUI.Label(
                new Rect(panel.x + 1f, panel.y + 1f, panel.width, 24f),
                primaryMessage,
                criticalStyle);
            GUI.color = new Color(1f, 0.28f, 0.22f, alpha);
            GUI.Label(
                new Rect(panel.x, panel.y, panel.width, 24f),
                primaryMessage,
                criticalStyle);
            GUI.color = new Color(0.86f, 0.9f, 0.92f, alpha * 0.9f);
            GUI.Label(
                new Rect(panel.x, panel.y + 24f, panel.width, 22f),
                secondaryMessage,
                detailStyle);
            GUI.color = oldColor;
        }

        void DrawDamageVignette()
        {
            EnsureDamageVignette();
            if (damageVignette == null)
                return;
            float duration = Mathf.Max(
                0.01f,
                incomingUntil - incomingStartedAt);
            float progress = Mathf.Clamp01(
                (Time.unscaledTime - incomingStartedAt) / duration);
            float envelope = (1f - progress) * (1f - progress);
            Color previous = GUI.color;
            GUI.color = new Color(
                1f,
                1f,
                1f,
                incomingStrength * envelope * 0.62f);
            GUI.DrawTexture(
                new Rect(0f, 0f, Screen.width, Screen.height),
                damageVignette,
                ScaleMode.StretchToFill,
                true);
            GUI.color = previous;
        }

        void DrawIncomingDirection()
        {
            float duration = Mathf.Max(
                0.01f,
                incomingUntil - incomingStartedAt);
            float progress = Mathf.Clamp01(
                (Time.unscaledTime - incomingStartedAt) / duration);
            float alpha = (1f - progress) * incomingStrength;
            Vector2 center = new Vector2(
                Screen.width * 0.5f,
                Screen.height * 0.5f);
            float radius = Mathf.Clamp(
                Mathf.Min(Screen.width, Screen.height) * 0.165f,
                94f,
                168f);
            Vector2 position = center + incomingDirection * radius;
            float angle =
                Mathf.Atan2(incomingDirection.y, incomingDirection.x) *
                Mathf.Rad2Deg + 90f;
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUIUtility.RotateAroundPivot(angle, position);
            GUI.color = new Color(1f, 0.72f, 0.68f, alpha);
            DrawSolid(new Rect(position.x - 22f, position.y - 1.5f, 44f, 3f));
            GUI.color = new Color(1f, 0.16f, 0.12f, alpha * 0.72f);
            DrawSolid(new Rect(position.x - 15f, position.y + 4f, 30f, 2f));
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        void DrawHitConfirmation()
        {
            float duration = Mathf.Max(
                0.01f,
                confirmationUntil - confirmationStartedAt);
            float progress = Mathf.Clamp01(
                (Time.unscaledTime - confirmationStartedAt) / duration);
            float alpha = 1f - Mathf.SmoothStep(0.55f, 1f, progress);
            Vector2 center = new Vector2(
                Screen.width * 0.5f,
                Screen.height * 0.5f);
            float radius = confirmedDestruction ? 18f : 14f;
            float length = confirmedDestruction ? 10f : 8f;
            Color previous = GUI.color;
            GUI.color = confirmedDestruction
                ? new Color(1f, 0.28f, 0.22f, alpha)
                : new Color(0.82f, 0.98f, 1f, alpha);
            for (int index = 0; index < 4; index++)
            {
                float angle = 45f + index * 90f;
                float radians = angle * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(
                    Mathf.Cos(radians),
                    Mathf.Sin(radians));
                Vector2 position = center +
                    direction * (radius + length * 0.5f);
                Matrix4x4 matrix = GUI.matrix;
                GUIUtility.RotateAroundPivot(angle, position);
                DrawSolid(new Rect(
                    position.x - length * 0.5f,
                    position.y - 1.25f,
                    length,
                    2.5f));
                GUI.matrix = matrix;
            }
            GUI.color = previous;
        }

        void EnsureDamageVignette()
        {
            if (damageVignette != null)
                return;
            const int size = 64;
            damageVignette = new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false)
            {
                name = "RuntimeDamageVignette",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size * 2f - 1f;
                float ny = (y + 0.5f) / size * 2f - 1f;
                float radial = Mathf.Sqrt(
                    nx * nx * 0.7f + ny * ny);
                float edge = Mathf.SmoothStep(0.52f, 1.05f, radial);
                pixels[y * size + x] = new Color(
                    0.46f,
                    0.005f,
                    0.008f,
                    edge * edge * 0.42f);
            }
            damageVignette.SetPixels32(pixels);
            damageVignette.Apply(false, true);
        }

        void EnsureAudio()
        {
            if (!Application.isPlaying || feedbackAudio != null)
                return;
            feedbackAudio = gameObject.AddComponent<AudioSource>();
            feedbackAudio.playOnAwake = false;
            feedbackAudio.loop = false;
            feedbackAudio.spatialBlend = 0f;
            feedbackAudio.dopplerLevel = 0f;
            feedbackAudio.ignoreListenerPause = true;
            incomingHitClip = CreateFeedbackClip(
                "IncomingHit",
                0.11f,
                0);
            hitConfirmClip = CreateFeedbackClip(
                "HitConfirm",
                0.055f,
                1);
            destructionConfirmClip = CreateFeedbackClip(
                "DestructionConfirm",
                0.09f,
                2);
        }

        static AudioClip CreateFeedbackClip(
            string clipName,
            float duration,
            int kind)
        {
            const int sampleRate = 22050;
            int sampleCount = Mathf.CeilToInt(duration * sampleRate);
            var samples = new float[sampleCount];
            uint noiseState = 0x9E3779B9u + (uint)kind * 7919u;
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)sampleRate;
                float normalized = index / (float)Mathf.Max(1, sampleCount - 1);
                float envelope = (1f - normalized) * (1f - normalized);
                noiseState = noiseState * 1664525u + 1013904223u;
                float noise = ((noiseState >> 9) & 0x7FFFu) /
                              16383.5f - 1f;
                float value;
                if (kind == 0)
                {
                    value = Mathf.Sin(2f * Mathf.PI * 92f * time) * 0.62f +
                            Mathf.Sin(2f * Mathf.PI * 184f * time) * 0.18f +
                            noise * 0.2f;
                }
                else if (kind == 1)
                {
                    value = Mathf.Sin(2f * Mathf.PI * 1080f * time) * 0.72f +
                            Mathf.Sin(2f * Mathf.PI * 1620f * time) * 0.2f;
                }
                else
                {
                    value = Mathf.Sin(2f * Mathf.PI * 520f * time) * 0.58f +
                            Mathf.Sin(2f * Mathf.PI * 1040f * time) * 0.34f +
                            noise * 0.08f;
                }
                samples[index] = Mathf.Clamp(value * envelope, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create(
                clipName,
                sampleCount,
                1,
                sampleRate,
                false);
            clip.SetData(samples, 0);
            return clip;
        }

        static void DrawSolid(Rect rect)
        {
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }

        void Unsubscribe()
        {
            CombatDamageFeedbackBus.DamageApplied -=
                HandleDamageApplied;
            if (graph != null)
            {
                graph.ModuleDamaged -= HandleModuleDamaged;
                graph.StructureChanged -= HandleStructureChanged;
                graph.Destroyed -= HandleVehicleDestroyed;
            }
        }

        void OnDestroy()
        {
            Unsubscribe();
            DestroyRuntimeObject(damageVignette);
            DestroyRuntimeObject(incomingHitClip);
            DestroyRuntimeObject(hitConfirmClip);
            DestroyRuntimeObject(destructionConfirmClip);
        }

        static void DestroyRuntimeObject(UnityEngine.Object value)
        {
            if (value == null)
                return;
            if (Application.isPlaying)
                Destroy(value);
            else
                DestroyImmediate(value);
        }
    }
}
