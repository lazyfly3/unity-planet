using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    [Serializable]
    public struct AeroPanelRuntimeState
    {
        public string runtimeId;
        public int panelIndex;
        public Vector3 centerLocal;
        public Vector3 chordLocal;
        public Vector3 spanLocal;
        public Vector3 normalLocal;
        public Vector3 localAirVelocity;
        public float normalizedSpan;
        public float area;
        public float spanLength;
        public float meanChord;
        public float aspectRatio;
        public float oswaldEfficiency;
        public float incidenceRadians;
        public float controlAreaRatio;
        public float maximumControlDeflectionRadians;
        public float stallStartRadians;
        public float stallEndRadians;
        public float angleOfAttackDegrees;
        public float sideSlipDegrees;
        public float dynamicPressure;
        public float separation;
        public float shadowEfficiency;
        public float wakeEfficiency;
        public float downwashRadians;
        public float groundEffect;
        public bool isControlSurface;
        public bool leftSide;
    }

    [Serializable]
    public struct ControlSurfaceActuatorState
    {
        public string runtimeId;
        public float targetDeflectionDegrees;
        public float actualDeflectionDegrees;
        public float hingeLoad;
        public float hingeLoadLimit;
        public Vector3 torquePerUnitLocal;
        public bool saturated;
    }

    [Serializable]
    public struct VehicleFlightEnvelopeState
    {
        public bool active;
        public float separation;
        public float leftSeparation;
        public float rightSeparation;
        public float inputScale;
        public Vector3 recoveryTorqueLocal;
        public string status;
    }

    [Serializable]
    public struct VehicleStabilitySnapshot
    {
        public Vector3 neutralPointLocal;
        public float staticMargin;
        public float trimUsage;
        public Vector3 trimTorqueLocal;
        public Vector3 remainingControlTorqueLocal;
        public bool staticallyStable;
    }

    [Serializable]
    public struct AeroInteractionLink
    {
        public int sourcePanel;
        public int targetPanel;
        public float downstreamDistance;
        public float influence;
    }

    public sealed class AeroInteractionGraph
    {
        readonly List<AeroInteractionLink> links = new List<AeroInteractionLink>();
        public IReadOnlyList<AeroInteractionLink> Links => links;
        internal void Clear() => links.Clear();
        internal void Add(AeroInteractionLink link) => links.Add(link);
    }

    public sealed partial class VehiclePhysicsRc3State
    {
        const int MaximumAeroPanels = 256;
        const float SeparationTime = 0.18f;
        const float ReattachmentTime = 0.55f;
        const float MaximumDownwash = 12f * Mathf.Deg2Rad;
        const float HingeMomentCoefficient = 0.55f;

        sealed class ControlRuntime
        {
            public string runtimeId;
            public float normalizedTarget;
            public float normalizedActual;
            public float normalizedLimit = 1f;
            public float maximumDeflection = 20f * Mathf.Deg2Rad;
            public float deflectionRate = 90f * Mathf.Deg2Rad;
            public float hingeLimit = 2500f;
            public float hingeLoad;
            public Vector3 torquePerUnit;
        }

        readonly List<AeroPanelRuntimeState> aeroPanels = new List<AeroPanelRuntimeState>();
        readonly List<ControlSurfaceActuatorState> controlTelemetry = new List<ControlSurfaceActuatorState>();
        readonly Dictionary<string, ControlRuntime> controls = new Dictionary<string, ControlRuntime>(StringComparer.Ordinal);
        readonly List<ControlRuntime> controlOrder = new List<ControlRuntime>();
        readonly Dictionary<int, List<AeroInteractionLink>> interactionsByTarget = new Dictionary<int, List<AeroInteractionLink>>();
        readonly AeroInteractionGraph interactionGraph = new AeroInteractionGraph();
        Vector3 controlTorqueAuthority;
        VehicleFlightEnvelopeState flightEnvelope;
        VehicleStabilitySnapshot stability;

        public IReadOnlyList<AeroPanelRuntimeState> AeroPanels => aeroPanels;
        public IReadOnlyList<ControlSurfaceActuatorState> ControlSurfaces => controlTelemetry;
        public AeroInteractionGraph InteractionGraph => interactionGraph;
        public Vector3 ControlTorqueAuthority => controlTorqueAuthority;
        public VehicleFlightEnvelopeState FlightEnvelope => flightEnvelope;
        public VehicleStabilitySnapshot Stability => stability;

        public void ResetAerodynamicsRuntime()
        {
            for (int i = 0; i < aeroPanels.Count; i++)
            {
                AeroPanelRuntimeState panel = aeroPanels[i];
                panel.separation = 0f;
                aeroPanels[i] = panel;
            }
            foreach (ControlRuntime control in controlOrder)
            {
                control.normalizedTarget = 0f;
                control.normalizedActual = 0f;
                control.hingeLoad = 0f;
            }
            flightEnvelope = default;
            controlTorqueAuthority = Vector3.zero;
            stability.trimTorqueLocal = Vector3.zero;
            stability.trimUsage = 0f;
            UpdateRc33Snapshot();
        }

        void RebuildRc33AeroPanels()
        {
            var oldSeparation = new Dictionary<string, float>(StringComparer.Ordinal);
            for (int i = 0; i < aeroPanels.Count; i++)
            {
                AeroPanelRuntimeState old = aeroPanels[i];
                oldSeparation[PanelKey(old.runtimeId, old.panelIndex)] = old.separation;
            }
            var oldDeflection = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (ControlRuntime old in controlOrder)
                oldDeflection[old.runtimeId] = old.normalizedActual;
            aeroPanels.Clear();
            controls.Clear();
            controlOrder.Clear();
            interactionGraph.Clear();
            interactionsByTarget.Clear();

            for (int wingIndex = 0; wingIndex < wings.Count && aeroPanels.Count < MaximumAeroPanels; wingIndex++)
            {
                ModuleAeroSurface wing = wings[wingIndex];
                int count = Mathf.Min(Mathf.Clamp(Mathf.CeilToInt(wing.spanLength), 1, 4), MaximumAeroPanels - aeroPanels.Count);
                for (int panelIndex = 0; panelIndex < count; panelIndex++)
                {
                    float fraction = (panelIndex + 0.5f) / count - 0.5f;
                    float washout = Mathf.Abs(fraction) * (wing.spanLength > 4f ? -1.5f : -1f) * Mathf.Deg2Rad;
                    var panel = new AeroPanelRuntimeState
                    {
                        runtimeId = wing.runtimeId,
                        panelIndex = panelIndex,
                        centerLocal = wing.aerodynamicCenterLocal + wing.spanLocal * fraction * wing.spanLength,
                        chordLocal = wing.chordLocal,
                        spanLocal = wing.spanLocal,
                        normalLocal = wing.normalLocal,
                        normalizedSpan = fraction * 2f,
                        area = wing.area / count,
                        spanLength = wing.spanLength,
                        meanChord = wing.meanChord,
                        aspectRatio = wing.aspectRatio,
                        oswaldEfficiency = wing.oswaldEfficiency,
                        incidenceRadians = wing.incidenceRadians + washout,
                        controlAreaRatio = wing.controlAreaRatio,
                        maximumControlDeflectionRadians = wing.maximumControlDeflectionRadians,
                        stallStartRadians = wing.stallStartRadians,
                        stallEndRadians = wing.stallEndRadians,
                        shadowEfficiency = 1f,
                        wakeEfficiency = 1f,
                        isControlSurface = wing.isControlSurface,
                        leftSide = wing.aerodynamicCenterLocal.x < MassProperties.centerOfMassLocal.x
                    };
                    oldSeparation.TryGetValue(PanelKey(panel.runtimeId, panel.panelIndex), out panel.separation);
                    aeroPanels.Add(panel);
                }
                if (wing.isControlSurface && !controls.ContainsKey(wing.runtimeId))
                {
                    profiles.TryGetValue(wing.runtimeId, out ModulePhysicsProfile profile);
                    string source = profile.sourceId ?? string.Empty;
                    var control = new ControlRuntime
                    {
                        runtimeId = wing.runtimeId,
                        maximumDeflection = Mathf.Max(Mathf.Deg2Rad, wing.maximumControlDeflectionRadians),
                        deflectionRate = source.Contains("waste_rudder") ? 120f * Mathf.Deg2Rad : 90f * Mathf.Deg2Rad,
                        hingeLimit = source.Contains("waste_rudder") ? 1800f : 3500f
                    };
                    oldDeflection.TryGetValue(control.runtimeId, out control.normalizedActual);
                    controls.Add(control.runtimeId, control);
                    controlOrder.Add(control);
                }
            }
            controlOrder.Sort((a, b) => string.CompareOrdinal(a.runtimeId, b.runtimeId));
            BuildInteractionGraph();
            CalculateStaticStability();
            UpdateRc33Snapshot();
        }

        public void PrepareAerodynamics(
            Rigidbody body,
            Transform root,
            float fallbackDensity,
            VehicleAirflowField airflow,
            PlanetEnvironmentSample vehicleEnvironment)
        {
            controlTorqueAuthority = Vector3.zero;
            if (body == null || root == null)
                return;
            for (int i = 0; i < aeroPanels.Count; i++)
            {
                AeroPanelRuntimeState panel = aeroPanels[i];
                Vector3 worldPoint = root.TransformPoint(panel.centerLocal);
                // A ship is small compared with a planetary wind cell. Reuse
                // the center sample so modular geometry still affects forces
                // without querying procedural terrain for every block face.
                PlanetEnvironmentSample localEnvironment =
                    vehicleEnvironment;
                Vector3 localVelocity = root.InverseTransformDirection(
                    airflow != null
                        ? airflow.RelativeAirVelocity(
                            body,
                            worldPoint,
                            panel.runtimeId,
                            localEnvironment)
                        : body.GetPointVelocity(worldPoint));
                localVelocity -= panel.spanLocal * Vector3.Dot(localVelocity, panel.spanLocal);
                float speed = localVelocity.magnitude;
                float localDensity = airflow != null
                    ? Mathf.Max(0f, localEnvironment.airDensity)
                    : Mathf.Max(0f, fallbackDensity);
                float chordSpeed = Vector3.Dot(localVelocity, panel.chordLocal);
                float normalSpeed = Vector3.Dot(localVelocity, panel.normalLocal);
                panel.localAirVelocity = localVelocity;
                panel.dynamicPressure = 0.5f * localDensity * speed * speed;
                panel.angleOfAttackDegrees = speed > 0.1f
                    ? (Mathf.Atan2(-normalSpeed, Mathf.Max(0.1f, chordSpeed)) + panel.incidenceRadians) * Mathf.Rad2Deg
                    : 0f;
                panel.sideSlipDegrees = speed > 0.1f
                    ? Mathf.Asin(Mathf.Clamp(Vector3.Dot(localVelocity / speed, panel.spanLocal), -1f, 1f)) * Mathf.Rad2Deg
                    : 0f;
                panel.shadowEfficiency = PanelVisibility(panel, localVelocity);
                panel.groundEffect = GroundEffect(
                    panel,
                    localEnvironment,
                    airflow != null);
                panel.wakeEfficiency = 1f;
                panel.downwashRadians = 0f;
                aeroPanels[i] = panel;
            }
            for (int target = 0; target < aeroPanels.Count; target++)
            {
                if (!interactionsByTarget.TryGetValue(target, out List<AeroInteractionLink> links))
                    continue;
                AeroPanelRuntimeState panel = aeroPanels[target];
                float wakeLoss = 0f;
                float downwash = 0f;
                for (int j = 0; j < links.Count; j++)
                {
                    AeroInteractionLink link = links[j];
                    AeroPanelRuntimeState source = aeroPanels[link.sourcePanel];
                    float influence = link.influence * Mathf.Clamp01(source.dynamicPressure / 600f) * (1f - source.separation * 0.35f);
                    wakeLoss += influence * 0.25f;
                    downwash += Mathf.Sign(source.angleOfAttackDegrees) * MaximumDownwash * influence;
                }
                panel.wakeEfficiency = Mathf.Clamp(1f - wakeLoss, 0.55f, 1f);
                panel.downwashRadians = Mathf.Clamp(downwash, -MaximumDownwash, MaximumDownwash);
                aeroPanels[target] = panel;
            }
            PrepareControlBasis();
            RefreshEnvelope();
            UpdateRc33Snapshot();
        }

        public Vector3 ShapeManeuverTorque(Vector3 requestedLocalTorque, VehicleCoreAssistMode mode)
        {
            if (mode == VehicleCoreAssistMode.Disabled || !flightEnvelope.active)
                return requestedLocalTorque;
            float scale = mode == VehicleCoreAssistMode.Training
                ? Mathf.Max(0.35f, flightEnvelope.inputScale)
                : flightEnvelope.inputScale;
            Vector3 recovery = flightEnvelope.recoveryTorqueLocal;
            Vector3 result = requestedLocalTorque;
            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(result[axis]) < 0.0001f)
                    continue;
                bool recovers = Mathf.Abs(recovery[axis]) > 0.01f && Mathf.Sign(result[axis]) == Mathf.Sign(recovery[axis]);
                if (!recovers)
                    result[axis] *= scale;
            }
            return result;
        }

        public Vector3 AllocateControlSurfaceTorque(Vector3 requestedLocalTorque, float deltaTime)
        {
            foreach (ControlRuntime control in controlOrder)
                control.normalizedTarget = 0f;
            Vector3 residual = requestedLocalTorque;
            for (int iteration = 0; iteration < 8; iteration++)
            {
                foreach (ControlRuntime control in controlOrder)
                {
                    float denominator = control.torquePerUnit.sqrMagnitude;
                    if (denominator < 0.0001f || control.normalizedLimit <= 0f)
                        continue;
                    float oldTarget = control.normalizedTarget;
                    float correction = Vector3.Dot(residual, control.torquePerUnit) / denominator;
                    control.normalizedTarget = Mathf.Clamp(oldTarget + correction, -control.normalizedLimit, control.normalizedLimit);
                    residual -= control.torquePerUnit * (control.normalizedTarget - oldTarget);
                }
            }
            Vector3 actualTorque = Vector3.zero;
            controlTelemetry.Clear();
            foreach (ControlRuntime control in controlOrder)
            {
                float normalizedRate = control.deflectionRate / Mathf.Max(0.001f, control.maximumDeflection);
                control.normalizedActual = Mathf.MoveTowards(control.normalizedActual, control.normalizedTarget, normalizedRate * Mathf.Max(0f, deltaTime));
                actualTorque += control.torquePerUnit * control.normalizedActual;
                controlTelemetry.Add(new ControlSurfaceActuatorState
                {
                    runtimeId = control.runtimeId,
                    targetDeflectionDegrees = control.normalizedTarget * control.maximumDeflection * Mathf.Rad2Deg,
                    actualDeflectionDegrees = control.normalizedActual * control.maximumDeflection * Mathf.Rad2Deg,
                    hingeLoad = control.hingeLoad,
                    hingeLoadLimit = control.hingeLimit,
                    torquePerUnitLocal = control.torquePerUnit,
                    saturated = Mathf.Abs(control.normalizedTarget) >= control.normalizedLimit - 0.001f
                });
            }
            return actualTorque;
        }

        public void SetAssistTelemetry(Vector3 trimTorqueLocal, Vector3 totalAuthorityLocal)
        {
            stability.trimTorqueLocal = trimTorqueLocal;
            stability.trimUsage = Mathf.Max(SafeUsage(trimTorqueLocal.x, totalAuthorityLocal.x),
                Mathf.Max(SafeUsage(trimTorqueLocal.y, totalAuthorityLocal.y), SafeUsage(trimTorqueLocal.z, totalAuthorityLocal.z)));
            stability.remainingControlTorqueLocal = new Vector3(
                Mathf.Max(0f, totalAuthorityLocal.x - Mathf.Abs(trimTorqueLocal.x)),
                Mathf.Max(0f, totalAuthorityLocal.y - Mathf.Abs(trimTorqueLocal.y)),
                Mathf.Max(0f, totalAuthorityLocal.z - Mathf.Abs(trimTorqueLocal.z)));
            UpdateRc33Snapshot();
        }

        public void AccumulateAerodynamics(
            Rigidbody body,
            Transform root,
            float density,
            VehicleAirflowField airflow,
            PlanetEnvironmentSample vehicleEnvironment,
            VehicleForceLedger ledger)
        {
            VehiclePhysicsSnapshot snapshot = Snapshot;
            snapshot.currentLift = 0f;
            snapshot.currentDrag = 0f;
            snapshot.liftCenterLocal = Vector3.zero;
            snapshot.dragCenterLocal = Vector3.zero;
            snapshot.aerodynamicTorqueLocal = Vector3.zero;
            snapshot.maximumDynamicPressure = 0f;
            if (body == null || root == null || ledger == null || density <= 0.000001f)
            {
                Snapshot = snapshot;
                return;
            }
            Vector3 weightedLift = Vector3.zero;
            Vector3 weightedDrag = Vector3.zero;
            Vector3 totalTorqueWorld = Vector3.zero;
            foreach (ExposedAeroFace face in faces)
            {
                Vector3 position = root.TransformPoint(face.centerLocal);
                Vector3 normal = root.TransformDirection(face.normalLocal).normalized;
                PlanetEnvironmentSample localEnvironment =
                    vehicleEnvironment;
                Vector3 relativeVelocity = airflow != null
                    ? airflow.RelativeAirVelocity(
                        body,
                        position,
                        face.runtimeId,
                        localEnvironment)
                    : body.GetPointVelocity(position);
                float normalSpeed = Vector3.Dot(relativeVelocity, normal);
                if (normalSpeed <= 0.05f)
                    continue;
                float localDensity = airflow != null
                    ? localEnvironment.airDensity
                    : density;
                Vector3 force = -normal * (0.5f * localDensity * face.dragCoefficient * face.area * normalSpeed * normalSpeed);
                ledger.AddForceAtPoint(force, position);
                float magnitude = force.magnitude;
                snapshot.currentDrag += magnitude;
                weightedDrag += face.centerLocal * magnitude;
                totalTorqueWorld += Vector3.Cross(position - body.worldCenterOfMass, force);
            }
            float leftArea = 0f, rightArea = 0f, leftSeparation = 0f, rightSeparation = 0f;
            float totalPanelArea = 0f, groundEffectArea = 0f;
            int stalled = 0;
            for (int i = 0; i < aeroPanels.Count; i++)
            {
                AeroPanelRuntimeState panel = aeroPanels[i];
                float deflection = controls.TryGetValue(panel.runtimeId, out ControlRuntime control) ? control.normalizedActual : 0f;
                float effectiveAngle = PanelEffectiveAngle(panel, deflection);
                float targetSeparation = SeparationTarget(Mathf.Abs(effectiveAngle), panel.stallStartRadians, panel.stallEndRadians);
                float timeConstant = targetSeparation > panel.separation ? SeparationTime : ReattachmentTime;
                float response = 1f - Mathf.Exp(-Time.fixedDeltaTime / Mathf.Max(0.001f, timeConstant));
                panel.separation = Mathf.Lerp(panel.separation, targetSeparation, response);
                Vector3 localForce = EvaluatePanelForceLocal(panel, deflection, out Vector3 localLift, out Vector3 localDrag);
                Vector3 worldForce = root.TransformDirection(localForce);
                Vector3 worldPoint = root.TransformPoint(panel.centerLocal);
                ledger.AddForceAtPoint(worldForce, worldPoint);
                float liftMagnitude = localLift.magnitude;
                float dragMagnitude = localDrag.magnitude;
                snapshot.currentLift += liftMagnitude;
                snapshot.currentDrag += dragMagnitude;
                weightedLift += panel.centerLocal * liftMagnitude;
                weightedDrag += panel.centerLocal * dragMagnitude;
                totalTorqueWorld += Vector3.Cross(worldPoint - body.worldCenterOfMass, worldForce);
                snapshot.maximumDynamicPressure = Mathf.Max(snapshot.maximumDynamicPressure, panel.dynamicPressure);
                totalPanelArea += panel.area;
                groundEffectArea += panel.groundEffect * panel.area;
                if (panel.separation >= 0.5f) stalled++;
                if (panel.leftSide) { leftArea += panel.area; leftSeparation += panel.separation * panel.area; }
                else { rightArea += panel.area; rightSeparation += panel.separation * panel.area; }
                aeroPanels[i] = panel;
            }
            if (snapshot.currentLift > 0.001f) snapshot.liftCenterLocal = weightedLift / snapshot.currentLift;
            if (snapshot.currentDrag > 0.001f) snapshot.dragCenterLocal = weightedDrag / snapshot.currentDrag;
            snapshot.aerodynamicTorqueLocal = root.InverseTransformDirection(totalTorqueWorld);
            snapshot.aeroPanelCount = aeroPanels.Count;
            snapshot.stalledPanelCount = stalled;
            snapshot.leftWingSeparation = leftArea > 0f ? leftSeparation / leftArea : 0f;
            snapshot.rightWingSeparation = rightArea > 0f ? rightSeparation / rightArea : 0f;
            snapshot.averageGroundEffect = totalPanelArea > 0f ? groundEffectArea / totalPanelArea : 0f;
            Snapshot = snapshot;
            RefreshEnvelope();
            UpdateRc33Snapshot();
        }

        void PrepareControlBasis()
        {
            foreach (ControlRuntime control in controlOrder)
            {
                control.torquePerUnit = Vector3.zero;
                control.hingeLoad = 0f;
                control.normalizedLimit = 1f;
            }
            for (int i = 0; i < aeroPanels.Count; i++)
            {
                AeroPanelRuntimeState panel = aeroPanels[i];
                if (!panel.isControlSurface || !controls.TryGetValue(panel.runtimeId, out ControlRuntime control))
                    continue;
                Vector3 plus = EvaluatePanelForceLocal(panel, 1f, out _, out _);
                Vector3 minus = EvaluatePanelForceLocal(panel, -1f, out _, out _);
                Vector3 forcePerUnit = (plus - minus) * 0.5f;
                control.torquePerUnit += Vector3.Cross(panel.centerLocal - MassProperties.centerOfMassLocal, forcePerUnit);
                control.hingeLoad += panel.dynamicPressure * panel.area * Mathf.Max(0.1f, panel.meanChord) *
                                     HingeMomentCoefficient * panel.maximumControlDeflectionRadians;
            }
            controlTorqueAuthority = Vector3.zero;
            foreach (ControlRuntime control in controlOrder)
            {
                control.normalizedLimit = control.hingeLoad > control.hingeLimit
                    ? Mathf.Clamp01(control.hingeLimit / control.hingeLoad)
                    : 1f;
                Vector3 limited = control.torquePerUnit * control.normalizedLimit;
                controlTorqueAuthority += new Vector3(Mathf.Abs(limited.x), Mathf.Abs(limited.y), Mathf.Abs(limited.z));
            }
        }

        Vector3 EvaluatePanelForceLocal(AeroPanelRuntimeState panel, float normalizedDeflection,
            out Vector3 lift, out Vector3 drag)
        {
            lift = Vector3.zero;
            drag = Vector3.zero;
            float speed = panel.localAirVelocity.magnitude;
            float chordSpeed = Vector3.Dot(panel.localAirVelocity, panel.chordLocal);
            if (speed < 1f || chordSpeed <= 0.05f || panel.dynamicPressure <= 0.01f)
                return Vector3.zero;
            Vector3 direction = panel.localAirVelocity / speed;
            float angle = PanelEffectiveAngle(panel, normalizedDeflection);
            profiles.TryGetValue(panel.runtimeId, out ModulePhysicsProfile profile);
            float cosineSweep = Mathf.Max(0.15f, Mathf.Cos(profile.sweepRadians));
            float aspect = Mathf.Max(0.25f, panel.aspectRatio);
            float oswald = Mathf.Clamp(panel.oswaldEfficiency, 0.3f, 1f);
            float finiteSlope = LiftSlope * cosineSweep /
                (1f + LiftSlope * cosineSweep / (Mathf.PI * oswald * aspect));
            float attachedCl = Mathf.Clamp(finiteSlope * angle, -1.8f, 1.8f);
            float attachedCd = WingCd0 + attachedCl * attachedCl / (Mathf.PI * oswald * aspect);
            float flatCl = Mathf.Sin(2f * angle);
            float flatCd = 0.15f + 1.75f * Mathf.Sin(angle) * Mathf.Sin(angle);
            float cl = Mathf.Lerp(attachedCl, flatCl, panel.separation);
            float cd = Mathf.Lerp(attachedCd, flatCd, panel.separation);
            float pressure = panel.dynamicPressure * panel.shadowEfficiency * panel.wakeEfficiency;
            float ground = Mathf.Clamp01(panel.groundEffect);
            cl *= 1f + 0.08f * ground;
            float induced = Mathf.Max(0f, cd - WingCd0);
            cd = WingCd0 + induced * (1f - 0.55f * ground);
            Vector3 liftDirection = Vector3.Cross(direction, panel.spanLocal).normalized;
            if (Vector3.Dot(liftDirection, panel.normalLocal) < 0f)
                liftDirection = -liftDirection;
            lift = liftDirection * pressure * panel.area * cl;
            drag = -direction * pressure * panel.area * cd;
            return lift + drag;
        }

        float PanelEffectiveAngle(AeroPanelRuntimeState panel, float normalizedDeflection)
        {
            float speed = panel.localAirVelocity.magnitude;
            if (speed < 0.1f)
                return panel.incidenceRadians;
            float chordSpeed = Vector3.Dot(panel.localAirVelocity, panel.chordLocal);
            float normalSpeed = Vector3.Dot(panel.localAirVelocity, panel.normalLocal);
            return Mathf.Atan2(-normalSpeed, Mathf.Max(0.1f, chordSpeed)) + panel.incidenceRadians -
                   panel.downwashRadians + Mathf.Clamp(normalizedDeflection, -1f, 1f) *
                   panel.maximumControlDeflectionRadians * panel.controlAreaRatio;
        }

        float PanelVisibility(AeroPanelRuntimeState panel, Vector3 localVelocity)
        {
            if (localVelocity.sqrMagnitude < 0.01f)
                return 1f;
            Vector3 upstream = localVelocity.normalized;
            int visible = 0;
            for (int ray = 0; ray < 5; ray++)
            {
                float fraction = (ray - 2) * 0.18f;
                Vector3 start = panel.centerLocal + panel.spanLocal * fraction * panel.spanLength;
                if (!PathBlockedLocal(start, start + upstream * 6f, panel.runtimeId, string.Empty))
                    visible++;
            }
            return Mathf.Lerp(0.25f, 1f, visible / 5f);
        }

        float GroundEffect(
            AeroPanelRuntimeState panel,
            PlanetEnvironmentSample sample,
            bool hasAirflow)
        {
            if (!hasAirflow)
                return 0f;
            if (!sample.hasSurface || sample.surfaceDistance < 0f)
                return 0f;
            return Mathf.Clamp01(1f - sample.surfaceDistance / Mathf.Max(0.5f, panel.spanLength));
        }

        void BuildInteractionGraph()
        {
            for (int source = 0; source < aeroPanels.Count; source++)
            {
                AeroPanelRuntimeState upstream = aeroPanels[source];
                for (int target = 0; target < aeroPanels.Count; target++)
                {
                    if (source == target) continue;
                    AeroPanelRuntimeState downstream = aeroPanels[target];
                    float distance = upstream.centerLocal.z - downstream.centerLocal.z;
                    if (distance <= 0.05f) continue;
                    Vector3 offset = downstream.centerLocal - upstream.centerLocal;
                    Vector3 lateral = new Vector3(offset.x, offset.y, 0f);
                    float radius = 0.5f * upstream.spanLength + 0.12f * distance;
                    if (lateral.sqrMagnitude > radius * radius) continue;
                    float influence = Mathf.Exp(-distance / Mathf.Max(1f, upstream.spanLength * 4f)) *
                                      Mathf.Clamp01(1f - lateral.magnitude / Mathf.Max(0.1f, radius));
                    var link = new AeroInteractionLink
                    {
                        sourcePanel = source,
                        targetPanel = target,
                        downstreamDistance = distance,
                        influence = influence
                    };
                    interactionGraph.Add(link);
                    if (!interactionsByTarget.TryGetValue(target, out List<AeroInteractionLink> list))
                    {
                        list = new List<AeroInteractionLink>();
                        interactionsByTarget.Add(target, list);
                    }
                    list.Add(link);
                }
            }
        }

        void CalculateStaticStability()
        {
            float area = 0f, weightedZ = 0f, weightedChord = 0f;
            foreach (ModuleAeroSurface wing in wings)
            {
                if (Mathf.Abs(wing.normalLocal.y) < 0.45f) continue;
                area += wing.area;
                weightedZ += wing.aerodynamicCenterLocal.z * wing.area;
                weightedChord += wing.meanChord * wing.area;
            }
            float neutralZ = area > 0f ? weightedZ / area : MassProperties.centerOfMassLocal.z;
            float meanChord = area > 0f ? weightedChord / area : 1f;
            stability.neutralPointLocal = new Vector3(MassProperties.centerOfMassLocal.x,
                MassProperties.centerOfMassLocal.y, neutralZ);
            stability.staticMargin = (MassProperties.centerOfMassLocal.z - neutralZ) / Mathf.Max(0.1f, meanChord);
            stability.staticallyStable = stability.staticMargin >= 0.03f;
        }

        void RefreshEnvelope()
        {
            float area = 0f, separation = 0f, leftArea = 0f, rightArea = 0f, left = 0f, right = 0f;
            float maxPressure = 0f;
            foreach (AeroPanelRuntimeState panel in aeroPanels)
            {
                area += panel.area;
                separation += panel.separation * panel.area;
                maxPressure = Mathf.Max(maxPressure, panel.dynamicPressure);
                if (panel.leftSide) { leftArea += panel.area; left += panel.separation * panel.area; }
                else { rightArea += panel.area; right += panel.separation * panel.area; }
            }
            float average = area > 0f ? separation / area : 0f;
            float leftAverage = leftArea > 0f ? left / leftArea : 0f;
            float rightAverage = rightArea > 0f ? right / rightArea : 0f;
            float scale = average <= 0.1f ? 1f : average <= 0.5f
                ? Mathf.Lerp(1f, 0.35f, Mathf.InverseLerp(0.1f, 0.5f, average))
                : Mathf.Lerp(0.35f, 0.15f, Mathf.InverseLerp(0.5f, 1f, average));
            Vector3 recovery = Vector3.zero;
            if (Mathf.Abs(Snapshot.angleOfAttackDegrees) > 1f) recovery.x = Mathf.Sign(Snapshot.angleOfAttackDegrees);
            if (Mathf.Abs(Snapshot.sideSlipDegrees) > 1f) recovery.y = Mathf.Sign(Snapshot.sideSlipDegrees);
            if (Mathf.Abs(rightAverage - leftAverage) > 0.02f) recovery.z = Mathf.Sign(rightAverage - leftAverage);
            flightEnvelope = new VehicleFlightEnvelopeState
            {
                active = aeroPanels.Count > 0 && maxPressure >= 25f,
                separation = average,
                leftSeparation = leftAverage,
                rightSeparation = rightAverage,
                inputScale = scale,
                recoveryTorqueLocal = recovery,
                status = average < 0.1f ? "Attached" : average < 0.5f ? "Stall warning" : "Deep stall"
            };
        }

        void UpdateRc33Snapshot()
        {
            VehiclePhysicsSnapshot snapshot = Snapshot;
            snapshot.aeroPanelCount = aeroPanels.Count;
            snapshot.flightEnvelope = flightEnvelope;
            snapshot.stability = stability;
            Snapshot = snapshot;
        }

        static float SeparationTarget(float angle, float start, float end)
        {
            return Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(Mathf.Max(0.01f, start), Mathf.Max(start + 0.01f, end), angle));
        }

        static float SafeUsage(float value, float authority)
        {
            return authority > 0.001f ? Mathf.Clamp01(Mathf.Abs(value) / authority) : Mathf.Abs(value) > 0.001f ? 1f : 0f;
        }

        static string PanelKey(string runtimeId, int panelIndex)
        {
            return (runtimeId ?? string.Empty) + ":" + panelIndex;
        }
    }
}
