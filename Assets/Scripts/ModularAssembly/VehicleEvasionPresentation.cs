using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace UnityPlanet.ModularAssembly
{
    [DisallowMultipleComponent]
    public sealed class VehicleEvasionPresentation : MonoBehaviour
    {
        const float SlowScale = 0.38f;
        const float SlowHoldTime = 0.1f;
        const float SlowRecoverTime = 0.22f;
        const float GhostInterval = 0.05f;
        const float GhostLifetime = 0.58f;
        const float GhostMinimumDistance = 2.25f;
        const int MaximumGhostFrames = 4;
        const int MaximumGhostDrawsPerFrame = 192;

        sealed class GhostFrame
        {
            public readonly List<GhostDraw> draws =
                new List<GhostDraw>();
            public float age;
        }

        struct GhostDraw
        {
            public Mesh mesh;
            public Matrix4x4 matrix;
            public int layer;
        }

        static VehicleEvasionPresentation timeOwner;

        List<GhostFrame> ghosts;
        MaterialPropertyBlock ghostProperties;
        RobocraftMotionCoordinator motion;
        Transform visualRoot;
        Material ghostMaterial;
        Canvas hudCanvas;
        Image hudPanel;
        Text hudText;
        bool flightActive;
        bool captureActive;
        bool ownsTimeScale;
        bool hasGhostAnchor;
        float nextGhostTime;
        float slowElapsed;
        float baseTimeScale = 1f;
        float baseFixedDeltaTime = 0.02f;
        Vector3 ghostAnchorPosition;
        Quaternion ghostAnchorRotation;

        void Awake()
        {
            EnsureRuntimeState();
        }

        void OnEnable()
        {
            EnsureRuntimeState();
        }

        void EnsureRuntimeState()
        {
            if (ghosts == null)
                ghosts = new List<GhostFrame>();
            if (ghostProperties == null)
                ghostProperties = new MaterialPropertyBlock();
        }

        public void Bind(
            RobocraftMotionCoordinator owner,
            Transform root)
        {
            EnsureRuntimeState();
            motion = owner;
            visualRoot = root;
        }

        public void SetFlightActive(bool value)
        {
            EnsureRuntimeState();
            flightActive = value;
            if (value)
            {
                EnsureHud();
                if (hudCanvas != null)
                    hudCanvas.enabled = true;
            }
            else
            {
                CancelEvasion();
                if (hudCanvas != null)
                    hudCanvas.enabled = false;
            }
        }

        public void BeginEvasion(
            Vector3 worldDirection,
            float rollSign)
        {
            EnsureRuntimeState();
            if (!flightActive)
                SetFlightActive(true);
            captureActive = true;
            hasGhostAnchor = false;
            nextGhostTime = Time.unscaledTime;
            CaptureGhost();
            BeginTimeDilation();
        }

        public void EndAction()
        {
            EnsureRuntimeState();
            captureActive = false;
            RestoreTimeScale();
        }

        public void CancelEvasion()
        {
            EnsureRuntimeState();
            captureActive = false;
            RestoreTimeScale();
            ghosts.Clear();
            hasGhostAnchor = false;
        }

        void Update()
        {
            EnsureRuntimeState();
            if (ownsTimeScale)
                UpdateTimeDilation();

            if (captureActive &&
                Time.unscaledTime >= nextGhostTime &&
                ShouldCaptureGhost())
            {
                CaptureGhost();
                nextGhostTime =
                    Time.unscaledTime + GhostInterval;
            }

            float delta = Mathf.Max(
                0f,
                Time.unscaledDeltaTime);
            for (int index = ghosts.Count - 1;
                 index >= 0;
                 index--)
            {
                GhostFrame frame = ghosts[index];
                frame.age += delta;
                if (frame.age >= GhostLifetime)
                    ghosts.RemoveAt(index);
            }

            UpdateHud();
        }

        bool ShouldCaptureGhost()
        {
            if (visualRoot == null || !hasGhostAnchor)
                return true;

            float distance = Vector3.Distance(
                visualRoot.position,
                ghostAnchorPosition);
            return distance >= GhostMinimumDistance;
        }

        void LateUpdate()
        {
            EnsureRuntimeState();
            if (ghostMaterial == null || ghosts.Count == 0)
                return;

            foreach (GhostFrame frame in ghosts)
            {
                float normalizedAge =
                    Mathf.Clamp01(frame.age / GhostLifetime);
                float fade = Mathf.Pow(
                    1f - normalizedAge,
                    1.15f);
                float alpha = fade * 0.32f;
                float hologramIntensity = fade * 0.92f;
                Color color = new Color(
                    0.02f,
                    0.82f,
                    1.35f,
                    alpha);
                ghostProperties.Clear();
                ghostProperties.SetColor(
                    "_BaseColor",
                    color);
                ghostProperties.SetColor(
                    "_Color",
                    color);
                ghostProperties.SetFloat(
                    "_Fade",
                    hologramIntensity);
                ghostProperties.SetColor(
                    "_HologramColor",
                    new Color(
                        0.02f,
                        0.72f * fade,
                        1.2f * fade,
                        1f));
                ghostProperties.SetColor(
                    "_SolidColor",
                    new Color(
                        0.015f,
                        0.12f * fade,
                        0.2f * fade,
                        1f));
                ghostProperties.SetFloat(
                    "_RevealProgress",
                    Mathf.Lerp(0.015f, 0.68f, fade));
                ghostProperties.SetFloat("_RevealMode", 1f);
                ghostProperties.SetFloat("_GridScale", 1.5f);
                ghostProperties.SetFloat("_SolidLag", 0.48f);
                ghostProperties.SetFloat("_WireWidth", 0.9f);
                foreach (GhostDraw draw in frame.draws)
                {
                    if (draw.mesh == null)
                        continue;
                    Graphics.DrawMesh(
                        draw.mesh,
                        draw.matrix,
                        ghostMaterial,
                        draw.layer,
                        null,
                        0,
                        ghostProperties,
                        ShadowCastingMode.Off,
                        false,
                        null,
                        LightProbeUsage.Off,
                        null);
                }
            }
        }

        void CaptureGhost()
        {
            if (visualRoot == null)
                return;
            EnsureGhostMaterial();
            if (ghostMaterial == null)
                return;

            MeshFilter[] filters =
                visualRoot.GetComponentsInChildren<MeshFilter>(
                    false);
            if (filters.Length == 0)
                return;

            var frame = new GhostFrame();
            int stride = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    filters.Length /
                    (float)MaximumGhostDrawsPerFrame));
            for (int index = 0;
                 index < filters.Length &&
                 frame.draws.Count <
                 MaximumGhostDrawsPerFrame;
                 index += stride)
            {
                MeshFilter filter = filters[index];
                if (filter == null ||
                    filter.sharedMesh == null)
                    continue;
                Renderer renderer =
                    filter.GetComponent<Renderer>();
                if (renderer == null ||
                    !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy ||
                    renderer is ParticleSystemRenderer ||
                    renderer is LineRenderer ||
                    renderer is TrailRenderer)
                    continue;

                frame.draws.Add(new GhostDraw
                {
                    mesh = filter.sharedMesh,
                    matrix = renderer.localToWorldMatrix,
                    layer = renderer.gameObject.layer
                });
            }

            if (frame.draws.Count == 0)
                return;
            ghosts.Add(frame);
            ghostAnchorPosition = visualRoot.position;
            ghostAnchorRotation = visualRoot.rotation;
            hasGhostAnchor = true;
            while (ghosts.Count > MaximumGhostFrames)
                ghosts.RemoveAt(0);
        }

        void EnsureGhostMaterial()
        {
            if (ghostMaterial != null)
                return;
            Shader shader =
                Shader.Find(
                    "CityGeneration/HolographicConstruction");
            if (shader == null)
                shader =
                    Shader.Find(
                        "Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                return;

            ghostMaterial = new Material(shader)
            {
                name = "VehicleEvasion_HologramAfterimage",
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = shader.renderQueue
            };
            if (ghostMaterial.HasProperty("_HologramColor"))
            {
                ghostMaterial.SetColor(
                    "_HologramColor",
                    new Color(0.02f, 0.72f, 1.2f, 1f));
            }
            if (ghostMaterial.HasProperty("_SolidColor"))
            {
                ghostMaterial.SetColor(
                    "_SolidColor",
                    new Color(0.015f, 0.12f, 0.2f, 1f));
            }
            if (ghostMaterial.HasProperty("_GridScale"))
                ghostMaterial.SetFloat("_GridScale", 1.5f);
            if (ghostMaterial.HasProperty("_SolidLag"))
                ghostMaterial.SetFloat("_SolidLag", 0.48f);
            if (ghostMaterial.HasProperty("_WireWidth"))
                ghostMaterial.SetFloat("_WireWidth", 0.9f);
            if (ghostMaterial.HasProperty("_BaseMap"))
            {
                ghostMaterial.SetTexture(
                    "_BaseMap",
                    Texture2D.whiteTexture);
            }
            if (ghostMaterial.HasProperty("_MainTex"))
            {
                ghostMaterial.SetTexture(
                    "_MainTex",
                    Texture2D.whiteTexture);
            }
            if (ghostMaterial.HasProperty("_Surface"))
                ghostMaterial.SetFloat("_Surface", 1f);
            if (ghostMaterial.HasProperty("_ZWrite"))
                ghostMaterial.SetFloat("_ZWrite", 0f);
            if (ghostMaterial.HasProperty("_SrcBlend"))
            {
                ghostMaterial.SetFloat(
                    "_SrcBlend",
                    (float)BlendMode.SrcAlpha);
            }
            if (ghostMaterial.HasProperty("_DstBlend"))
            {
                ghostMaterial.SetFloat(
                    "_DstBlend",
                    (float)BlendMode.One);
            }
            ghostMaterial.EnableKeyword(
                "_SURFACE_TYPE_TRANSPARENT");
        }

        void BeginTimeDilation()
        {
            if (Time.timeScale <= 0.001f)
                return;
            if (timeOwner != null && timeOwner != this)
                timeOwner.RestoreTimeScale();

            timeOwner = this;
            ownsTimeScale = true;
            slowElapsed = 0f;
            baseTimeScale = Mathf.Max(
                0.001f,
                Time.timeScale);
            baseFixedDeltaTime = Mathf.Max(
                0.001f,
                Time.fixedDeltaTime);
            ApplyTimeScale(baseTimeScale * SlowScale);
        }

        void UpdateTimeDilation()
        {
            slowElapsed += Mathf.Max(
                0f,
                Time.unscaledDeltaTime);
            if (slowElapsed <= SlowHoldTime)
            {
                ApplyTimeScale(baseTimeScale * SlowScale);
                return;
            }

            float progress = Mathf.Clamp01(
                (slowElapsed - SlowHoldTime) /
                SlowRecoverTime);
            progress = progress * progress *
                       (3f - 2f * progress);
            ApplyTimeScale(Mathf.Lerp(
                baseTimeScale * SlowScale,
                baseTimeScale,
                progress));
            if (progress >= 1f)
                RestoreTimeScale();
        }

        void ApplyTimeScale(float value)
        {
            if (!ownsTimeScale || timeOwner != this)
                return;
            float scale = Mathf.Max(0.001f, value);
            Time.timeScale = scale;
            Time.fixedDeltaTime = Mathf.Max(
                0.001f,
                baseFixedDeltaTime *
                (scale / baseTimeScale));
        }

        void RestoreTimeScale()
        {
            if (!ownsTimeScale || timeOwner != this)
                return;
            Time.timeScale = baseTimeScale;
            Time.fixedDeltaTime = baseFixedDeltaTime;
            ownsTimeScale = false;
            timeOwner = null;
        }

        void EnsureHud()
        {
            if (hudCanvas != null)
                return;

            GameObject canvasObject = new GameObject(
                "VehicleEvasionHud",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.transform.SetParent(
                transform,
                false);
            hudCanvas = canvasObject.GetComponent<Canvas>();
            hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            hudCanvas.sortingOrder = 940;
            CanvasScaler scaler =
                canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution =
                new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject panelObject = new GameObject(
                "Panel",
                typeof(RectTransform),
                typeof(Image));
            panelObject.transform.SetParent(
                canvasObject.transform,
                false);
            hudPanel = panelObject.GetComponent<Image>();
            hudPanel.color =
                new Color(0.01f, 0.08f, 0.12f, 0.76f);
            RectTransform panel =
                panelObject.GetComponent<RectTransform>();
            panel.anchorMin = new Vector2(1f, 0f);
            panel.anchorMax = new Vector2(1f, 0f);
            panel.pivot = new Vector2(1f, 0f);
            panel.anchoredPosition = new Vector2(-22f, 92f);
            panel.sizeDelta = new Vector2(270f, 42f);

            GameObject textObject = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(
                panelObject.transform,
                false);
            hudText = textObject.GetComponent<Text>();
            hudText.font =
                Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            hudText.fontSize = 18;
            hudText.fontStyle = FontStyle.Bold;
            hudText.alignment = TextAnchor.MiddleCenter;
            hudText.color =
                new Color(0.35f, 0.9f, 1f, 1f);
            RectTransform textRect =
                textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }

        void UpdateHud()
        {
            if (hudCanvas == null || hudText == null)
                return;
            bool visible =
                flightActive &&
                motion != null &&
                motion.OwnsPhysics;
            hudCanvas.enabled = visible;
            if (!visible)
                return;

            VehicleEvasionSnapshot value =
                motion.EvasionSnapshot;
            switch (value.state)
            {
                case VehicleEvasionState.Active:
                    hudText.text =
                        $"闪避 F：动作中  " +
                        $"{value.rollProgress:P0}";
                    hudText.color =
                        new Color(0.22f, 0.9f, 1f, 1f);
                    break;
                case VehicleEvasionState.Recovery:
                    hudText.text = "闪避 F：回正";
                    hudText.color =
                        new Color(0.45f, 0.82f, 1f, 1f);
                    break;
                case VehicleEvasionState.Cooldown:
                    hudText.text =
                        $"闪避 F：冷却 " +
                        $"{value.cooldownTimeRemaining:0.0}s";
                    hudText.color =
                        new Color(1f, 0.72f, 0.28f, 1f);
                    break;
                default:
                    hudText.text = "闪避 F：就绪";
                    hudText.color =
                        new Color(0.35f, 1f, 0.82f, 1f);
                    break;
            }
        }

        void OnDisable()
        {
            RestoreTimeScale();
        }

        void OnDestroy()
        {
            RestoreTimeScale();
            if (ghostMaterial != null)
                Destroy(ghostMaterial);
        }
    }
}
