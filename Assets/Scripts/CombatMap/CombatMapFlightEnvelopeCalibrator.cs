using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ModularAssembly;
using SpacecraftEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.ModularAssembly;
using Object = UnityEngine.Object;

namespace UnityPlanet.CombatMap
{
    /// <summary>
    /// Runs one closed-loop 360 degree turn in an isolated local PhysicsScene.
    /// The source model and Rigidbody are only read and are never restored by
    /// writing values back to them.
    /// </summary>
    public static class CombatMapFlightEnvelopeCalibrator
    {
        const float Step = 0.02f;
        const int MaximumSteps = 6000;
        const float WarmupDegrees = 180f;
        const float FinishDegrees = 540f;

        sealed class ConstantEnvironment : IPlanetEnvironmentProvider
        {
            public bool ForceNoWind { get; set; }

            public PlanetEnvironmentSample Sample(
                Vector3 worldPosition,
                double simulationTime)
            {
                return PlanetEnvironmentSample.EarthLike(
                    Vector3.down * 9.81f,
                    Mathf.Max(0f, worldPosition.y));
            }
        }

        struct SourceSnapshot
        {
            public string fingerprint;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 velocity;
            public Vector3 angularVelocity;
            public bool isKinematic;
            public float mass;
            public Vector3 centerOfMass;
            public Vector3 inertiaTensor;
            public Quaternion inertiaTensorRotation;
            public RigidbodyConstraints constraints;
            public CollisionDetectionMode collisionMode;
            public int layer;
        }

        public static IEnumerator Measure(
            GridAssemblyModel sourceModel,
            Rigidbody sourceBody,
            float designSpeed,
            float designTurnRadius,
            Action<CombatMapFlightEnvelope> completed)
        {
            if (sourceModel == null || sourceBody == null)
            {
                completed?.Invoke(Fallback(
                    string.Empty,
                    new Bounds(Vector3.zero, new Vector3(2f, 2f, 2f)),
                    designSpeed,
                    designTurnRadius,
                    false,
                    "缺少源飞船模型或刚体，无法执行隔离盘旋测试。"));
                yield break;
            }

            SourceSnapshot sourceBefore = CaptureSource(
                sourceModel,
                sourceBody);
            Scene probeScene = default(Scene);
            GameObject root = null;
            Rigidbody probeBody = null;
            RobocraftMotionCoordinator motion = null;
            Bounds localBounds = new Bounds(
                Vector3.zero,
                new Vector3(2f, 2f, 2f));
            string setupError = string.Empty;

            try
            {
                probeScene = SceneManager.CreateScene(
                    "CombatMapTurnProbe_" + sourceBefore.fingerprint,
                    new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                root = new GameObject("IsolatedTurnProbe");
                SceneManager.MoveGameObjectToScene(root, probeScene);
                root.transform.SetPositionAndRotation(
                    new Vector3(0f, 1000f, 0f),
                    Quaternion.identity);

                probeBody = root.AddComponent<Rigidbody>();
                probeBody.useGravity = false;
                probeBody.drag = 0f;
                probeBody.angularDrag = 0f;
                probeBody.isKinematic = true;
                probeBody.interpolation = RigidbodyInterpolation.None;

                Transform parts = new GameObject("Parts").transform;
                parts.SetParent(root.transform, false);
                Transform core = new GameObject("CoreVisual").transform;
                core.SetParent(root.transform, false);
                ShipAssembly assembly = root.AddComponent<ShipAssembly>();
                assembly.Configure(probeBody, parts, null, 1000f);

                ModularBlueprintData blueprint =
                    sourceModel.CaptureBlueprint();
                var probeModel = new GridAssemblyModel(
                    sourceModel.Definitions.Values);
                if (!probeModel.RestoreBlueprint(
                        blueprint,
                        out string restoreError))
                {
                    throw new InvalidOperationException(restoreError);
                }

                GridAssemblyPresenter presenter =
                    root.AddComponent<GridAssemblyPresenter>();
                presenter.Initialize(probeModel, assembly, core);
                assembly.Recalculate();
                localBounds = CaptureLocalBounds(root.transform, probeModel);

                motion = root.AddComponent<RobocraftMotionCoordinator>();
                motion.ConfigureExplicit(
                    probeBody,
                    assembly,
                    probeModel,
                    presenter);
                motion.SetEnvironmentProvider(new ConstantEnvironment());
                motion.BeginDiagnosticFlight();
                motion.enabled = false;
                DisableProbePresentation(root, motion);

                probeBody.isKinematic = false;
                probeBody.position = new Vector3(0f, 1000f, 0f);
                probeBody.rotation = Quaternion.identity;
                probeBody.velocity = Vector3.forward *
                                     Mathf.Max(10f, designSpeed);
                probeBody.angularVelocity = Vector3.zero;
                probeBody.WakeUp();
            }
            catch (Exception exception)
            {
                setupError = exception.GetType().Name + ": " +
                             exception.Message;
            }

            if (!string.IsNullOrEmpty(setupError) ||
                root == null || probeBody == null || motion == null ||
                !probeScene.IsValid())
            {
                if (root != null)
                    Object.Destroy(root);
                if (probeScene.IsValid() && probeScene.isLoaded)
                {
                    AsyncOperation failedUnload =
                        SceneManager.UnloadSceneAsync(probeScene);
                    if (failedUnload != null)
                        yield return failedUnload;
                }
                bool unchanged = IsSourceUnchanged(
                    sourceModel,
                    sourceBody,
                    sourceBefore);
                completed?.Invoke(Fallback(
                    sourceBefore.fingerprint,
                    localBounds,
                    designSpeed,
                    designTurnRadius,
                    unchanged,
                    "隔离盘旋测试初始化失败：" + setupError));
                yield break;
            }

            PhysicsScene physicsScene = probeScene.GetPhysicsScene();
            var points = new List<Vector2>(2048);
            var speeds = new List<float>(2048);
            var curvatureRadii = new List<float>(2048);
            float accumulated = 0f;
            float recordedDegrees = 0f;
            Vector3 previousPlanar = Vector3.forward;
            bool havePrevious = false;
            float minimumY = probeBody.position.y;
            float maximumY = probeBody.position.y;
            string simulationError = string.Empty;

            for (int stepIndex = 0;
                 stepIndex < MaximumSteps && accumulated < FinishDegrees;
                 stepIndex++)
            {
                Vector3 planar = Vector3.ProjectOnPlane(
                    probeBody.velocity,
                    Vector3.up);
                Vector3 forward = planar.sqrMagnitude > 0.01f
                    ? planar.normalized
                    : root.transform.forward;
                Vector3 tangent = Quaternion.AngleAxis(
                    -35f,
                    Vector3.up) * forward;
                float altitudeError = 1000f - probeBody.position.y;
                var frame = new RobocraftControlFrame
                {
                    move = new Vector2(0f, 1f),
                    vertical = Mathf.Clamp(
                        altitudeError * 0.04f -
                        Vector3.Dot(probeBody.velocity, Vector3.up) * 0.08f,
                        -1f,
                        1f),
                    roll = 0f,
                    braking = false,
                    boost = false,
                    freeLook = false,
                    aimForwardWorld = tangent,
                    hasAimOverride = true
                };

                motion.SimulateDiagnosticStep(frame);
                physicsScene.Simulate(Step);

                if (!Finite(probeBody.position) ||
                    !Finite(probeBody.velocity))
                {
                    simulationError =
                        "探针刚体出现非有限位置或速度。";
                    break;
                }

                Vector3 currentPlanar = Vector3.ProjectOnPlane(
                    probeBody.velocity,
                    Vector3.up);
                if (currentPlanar.sqrMagnitude > 0.01f)
                {
                    if (havePrevious)
                    {
                        float delta = Vector3.SignedAngle(
                            previousPlanar,
                            currentPlanar,
                            Vector3.up);
                        float progress = Mathf.Max(0f, -delta);
                        accumulated += progress;
                        if (accumulated >= WarmupDegrees)
                        {
                            recordedDegrees += progress;
                            if (progress > 0.001f)
                            {
                                float angularRate =
                                    progress * Mathf.Deg2Rad / Step;
                                float localRadius =
                                    currentPlanar.magnitude /
                                    Mathf.Max(0.0001f, angularRate);
                                if (Finite(localRadius) &&
                                    localRadius >= 2f &&
                                    localRadius <= 5000f)
                                {
                                    curvatureRadii.Add(localRadius);
                                }
                            }
                        }
                    }
                    previousPlanar = currentPlanar.normalized;
                    havePrevious = true;
                }

                minimumY = Mathf.Min(minimumY, probeBody.position.y);
                maximumY = Mathf.Max(maximumY, probeBody.position.y);
                if (accumulated >= WarmupDegrees)
                {
                    points.Add(new Vector2(
                        probeBody.position.x,
                        probeBody.position.z));
                    speeds.Add(currentPlanar.magnitude);
                }

                if ((stepIndex + 1) % 100 == 0)
                    yield return null;
            }

            motion.EndDiagnosticFlight();
            bool sourceUnchanged = IsSourceUnchanged(
                sourceModel,
                sourceBody,
                sourceBefore);
            CombatMapFlightEnvelope envelope;
            bool circleFit = TryFitCircle(
                points,
                out float fittedRadius,
                out float rms,
                out float closure);
            float radius = Percentile(curvatureRadii, 0.90f);
            if (circleFit && fittedRadius >= 5f && fittedRadius <= 3500f)
                radius = Mathf.Max(radius, fittedRadius);
            if (string.IsNullOrEmpty(simulationError) &&
                recordedDegrees >= 350f &&
                points.Count >= 120 &&
                curvatureRadii.Count >= 100 &&
                Finite(radius))
            {
                float meanSpeed = Mean(speeds);
                float speedDeviation = StandardDeviation(
                    speeds,
                    meanSpeed);
                float altitudeLoss = maximumY - minimumY;
                bool fitValid = meanSpeed >= 5f &&
                                radius >= 5f &&
                                radius <= 3500f;
                float curvatureMean = Mean(curvatureRadii);
                float curvatureDeviation = StandardDeviation(
                    curvatureRadii,
                    curvatureMean);
                float coverageScore = Mathf.Clamp01(
                    recordedDegrees / 360f);
                float fitScore = circleFit
                    ? Mathf.Clamp01(
                        1f - rms /
                        Mathf.Max(1f, fittedRadius * 0.25f))
                    : 0f;
                float curvatureScore = Mathf.Clamp01(
                    1f - curvatureDeviation /
                    Mathf.Max(1f, curvatureMean * 0.5f));
                float closureScore = circleFit
                    ? Mathf.Clamp01(
                        1f - closure /
                        Mathf.Max(1f, fittedRadius * 1.5f))
                    : 0f;
                float speedScore = Mathf.Clamp01(
                    1f - speedDeviation /
                    Mathf.Max(1f, meanSpeed * 0.25f));
                float altitudeScore = Mathf.Clamp01(
                    1f - altitudeLoss /
                    Mathf.Max(20f, localBounds.size.y * 2f));
                float confidence = Mathf.Clamp01(
                    0.30f * coverageScore +
                    0.30f * curvatureScore +
                    0.10f * fitScore +
                    0.10f * closureScore +
                    0.10f * speedScore +
                    0.10f * altitudeScore);

                if (fitValid)
                {
                    envelope = new CombatMapFlightEnvelope
                    {
                        blueprintFingerprint = sourceBefore.fingerprint,
                        localBounds = localBounds,
                        horizontalSpan = Mathf.Max(
                            localBounds.size.x,
                            localBounds.size.z),
                        verticalSpan = localBounds.size.y,
                        hullRadius = localBounds.extents.magnitude,
                        measurementCompleted = true,
                        usedFallback = false,
                        sourceAircraftUnchanged = sourceUnchanged,
                        measuredSpeed = meanSpeed,
                        turnRadius = radius,
                        turnDuration = points.Count * Step,
                        altitudeLoss = altitudeLoss,
                        closureError = closure,
                        circleFitRms = rms,
                        confidence = confidence,
                        sampleCount = points.Count,
                        diagnostic = string.Format(
                            "隔离探针完成一周稳定段：曲率 P90 半径 {0:0.0}m，均速 {1:0.0}m/s，" +
                            "用时 {2:0.0}s，拟合误差 {3:0.0}m，置信度 {4:P0}；源飞船未被修改={5}。",
                            radius,
                            meanSpeed,
                            points.Count * Step,
                            rms,
                            confidence,
                            sourceUnchanged ? "是" : "否")
                    };
                }
                else
                {
                    envelope = Fallback(
                        sourceBefore.fingerprint,
                        localBounds,
                        designSpeed,
                        designTurnRadius,
                        sourceUnchanged,
                        "盘旋覆盖已完成，但圆拟合或速度稳定性未通过安全阈值。" +
                        string.Format(
                            " R={0:0.0}m，RMS={1:0.0}m，闭合={2:0.0}m。",
                            radius,
                            rms,
                            closure));
                }
            }
            else
            {
                string reason = !string.IsNullOrEmpty(simulationError)
                    ? simulationError
                    : string.Format(
                        "最大仿真步内仅完成 {0:0.0}°，有效样本 {1}。",
                        recordedDegrees,
                        points.Count);
                envelope = Fallback(
                    sourceBefore.fingerprint,
                    localBounds,
                    designSpeed,
                    designTurnRadius,
                    sourceUnchanged,
                    reason);
            }

            Object.Destroy(root);
            AsyncOperation unload = SceneManager.UnloadSceneAsync(probeScene);
            if (unload != null)
                yield return unload;
            completed?.Invoke(envelope);
        }

        public static string Fingerprint(GridAssemblyModel model)
        {
            if (model == null)
                return string.Empty;
            ModularBlueprintData blueprint = model.CaptureBlueprint();
            ulong hash = 1469598103934665603UL;
            Hash(ref hash, ((int)blueprint.coreAssistMode).ToString());
            IEnumerable<ModularBlueprintModule> modules =
                (blueprint.modules ?? Array.Empty<ModularBlueprintModule>())
                .Where(value => value != null)
                .OrderBy(value => value.runtimeId, StringComparer.Ordinal);
            foreach (ModularBlueprintModule module in modules)
            {
                Hash(ref hash, module.runtimeId);
                Hash(ref hash, module.moduleId);
                Hash(ref hash, module.pose.x.ToString());
                Hash(ref hash, module.pose.y.ToString());
                Hash(ref hash, module.pose.z.ToString());
                Hash(ref hash, module.pose.orientation.ToString());
                Hash(ref hash, module.pose.mirrorGroupId);
                Hash(ref hash, module.behaviorSettings);
            }
            return hash.ToString("X16");
        }

        static CombatMapFlightEnvelope Fallback(
            string fingerprint,
            Bounds bounds,
            float speed,
            float radius,
            bool sourceUnchanged,
            string reason)
        {
            float safeSpeed = Mathf.Max(10f, speed);
            float safeRadius = Mathf.Max(20f, radius);
            return new CombatMapFlightEnvelope
            {
                blueprintFingerprint = fingerprint ?? string.Empty,
                localBounds = bounds,
                horizontalSpan = Mathf.Max(bounds.size.x, bounds.size.z),
                verticalSpan = Mathf.Max(1f, bounds.size.y),
                hullRadius = bounds.extents.magnitude,
                measurementCompleted = false,
                usedFallback = true,
                sourceAircraftUnchanged = sourceUnchanged,
                measuredSpeed = safeSpeed,
                turnRadius = safeRadius,
                turnDuration = Mathf.PI * 2f * safeRadius / safeSpeed,
                altitudeLoss = 0f,
                closureError = 0f,
                circleFitRms = 0f,
                confidence = sourceUnchanged ? 0.25f : 0f,
                sampleCount = 0,
                diagnostic = "盘旋实测未完成，采用保守设计值。" + reason +
                             " 源飞船未被修改=" +
                             (sourceUnchanged ? "是。" : "否。")
            };
        }

        static SourceSnapshot CaptureSource(
            GridAssemblyModel model,
            Rigidbody body)
        {
            return new SourceSnapshot
            {
                fingerprint = Fingerprint(model),
                position = body.position,
                rotation = body.rotation,
                velocity = body.velocity,
                angularVelocity = body.angularVelocity,
                isKinematic = body.isKinematic,
                mass = body.mass,
                centerOfMass = body.centerOfMass,
                inertiaTensor = body.inertiaTensor,
                inertiaTensorRotation = body.inertiaTensorRotation,
                constraints = body.constraints,
                collisionMode = body.collisionDetectionMode,
                layer = body.gameObject.layer
            };
        }

        static bool IsSourceUnchanged(
            GridAssemblyModel model,
            Rigidbody body,
            SourceSnapshot before)
        {
            return Fingerprint(model) == before.fingerprint &&
                   Vector3.Distance(body.position, before.position) < 0.0001f &&
                   Quaternion.Angle(body.rotation, before.rotation) < 0.001f &&
                   Vector3.Distance(body.velocity, before.velocity) < 0.0001f &&
                   Vector3.Distance(
                       body.angularVelocity,
                       before.angularVelocity) < 0.0001f &&
                   body.isKinematic == before.isKinematic &&
                   Mathf.Abs(body.mass - before.mass) < 0.0001f &&
                   Vector3.Distance(
                       body.centerOfMass,
                       before.centerOfMass) < 0.0001f &&
                   Vector3.Distance(
                       body.inertiaTensor,
                       before.inertiaTensor) < 0.0001f &&
                   Quaternion.Angle(
                       body.inertiaTensorRotation,
                       before.inertiaTensorRotation) < 0.001f &&
                   body.constraints == before.constraints &&
                   body.collisionDetectionMode == before.collisionMode &&
                   body.gameObject.layer == before.layer;
        }

        static Bounds CaptureLocalBounds(
            Transform root,
            GridAssemblyModel model)
        {
            bool initialized = false;
            Bounds result = new Bounds(Vector3.zero, Vector3.zero);
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
                EncapsulateWorldBounds(
                    root,
                    renderers[index].bounds,
                    ref result,
                    ref initialized);
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
                EncapsulateWorldBounds(
                    root,
                    colliders[index].bounds,
                    ref result,
                    ref initialized);

            if (!initialized && model != null)
            {
                foreach (GridModuleRecord record in model.Records)
                foreach (Vector3Int cell in model.GetCells(record))
                {
                    Vector3 minimum = cell;
                    Vector3 maximum = minimum + Vector3.one;
                    if (!initialized)
                    {
                        result = new Bounds(
                            (minimum + maximum) * 0.5f,
                            maximum - minimum);
                        initialized = true;
                    }
                    else
                    {
                        result.Encapsulate(minimum);
                        result.Encapsulate(maximum);
                    }
                }
            }
            if (!initialized)
                result = new Bounds(Vector3.zero, new Vector3(2f, 2f, 2f));
            result.Expand(0.1f);
            return result;
        }

        static void EncapsulateWorldBounds(
            Transform root,
            Bounds world,
            ref Bounds local,
            ref bool initialized)
        {
            Vector3 minimum = world.min;
            Vector3 maximum = world.max;
            for (int mask = 0; mask < 8; mask++)
            {
                Vector3 point = new Vector3(
                    (mask & 1) == 0 ? minimum.x : maximum.x,
                    (mask & 2) == 0 ? minimum.y : maximum.y,
                    (mask & 4) == 0 ? minimum.z : maximum.z);
                Vector3 value = root.InverseTransformPoint(point);
                if (!initialized)
                {
                    local = new Bounds(value, Vector3.zero);
                    initialized = true;
                }
                else
                    local.Encapsulate(value);
            }
        }

        static void DisableProbePresentation(
            GameObject root,
            RobocraftMotionCoordinator motion)
        {
            foreach (Renderer renderer in
                     root.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            foreach (AudioSource audio in
                     root.GetComponentsInChildren<AudioSource>(true))
                audio.enabled = false;
            foreach (ParticleSystem particles in
                     root.GetComponentsInChildren<ParticleSystem>(true))
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (TrailRenderer trail in
                     root.GetComponentsInChildren<TrailRenderer>(true))
                trail.enabled = false;
            foreach (MonoBehaviour behaviour in
                     root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null && behaviour != motion)
                    behaviour.enabled = false;
            }
        }

        static bool TryFitCircle(
            IList<Vector2> points,
            out float radius,
            out float rms,
            out float closure)
        {
            radius = 0f;
            rms = float.PositiveInfinity;
            closure = float.PositiveInfinity;
            if (points == null || points.Count < 3)
                return false;

            double meanX = 0d;
            double meanY = 0d;
            for (int index = 0; index < points.Count; index++)
            {
                meanX += points[index].x;
                meanY += points[index].y;
            }
            meanX /= points.Count;
            meanY /= points.Count;

            double sxx = 0d;
            double sxy = 0d;
            double syy = 0d;
            double sx = 0d;
            double sy = 0d;
            double sxq = 0d;
            double syq = 0d;
            double sq = 0d;
            for (int index = 0; index < points.Count; index++)
            {
                double x = points[index].x - meanX;
                double y = points[index].y - meanY;
                double q = -(x * x + y * y);
                sxx += x * x;
                sxy += x * y;
                syy += y * y;
                sx += x;
                sy += y;
                sxq += x * q;
                syq += y * q;
                sq += q;
            }

            double[,] matrix =
            {
                { sxx, sxy, sx },
                { sxy, syy, sy },
                { sx, sy, points.Count }
            };
            double determinant = Determinant(matrix);
            if (Math.Abs(determinant) < 1e-9)
                return false;
            double a = Determinant(new[,]
            {
                { sxq, sxy, sx },
                { syq, syy, sy },
                { sq, sy, points.Count }
            }) / determinant;
            double b = Determinant(new[,]
            {
                { sxx, sxq, sx },
                { sxy, syq, sy },
                { sx, sq, points.Count }
            }) / determinant;
            double c = Determinant(new[,]
            {
                { sxx, sxy, sxq },
                { sxy, syy, syq },
                { sx, sy, sq }
            }) / determinant;
            double radiusSquared = (a * a + b * b) * 0.25d - c;
            if (radiusSquared <= 0d || double.IsNaN(radiusSquared))
                return false;
            double fittedRadius = Math.Sqrt(radiusSquared);
            double centerX = -a * 0.5d + meanX;
            double centerY = -b * 0.5d + meanY;
            double residual = 0d;
            for (int index = 0; index < points.Count; index++)
            {
                double dx = points[index].x - centerX;
                double dy = points[index].y - centerY;
                double error = Math.Sqrt(dx * dx + dy * dy) -
                               fittedRadius;
                residual += error * error;
            }
            radius = (float)fittedRadius;
            rms = (float)Math.Sqrt(residual / points.Count);
            closure = Vector2.Distance(points[0], points[points.Count - 1]);
            return Finite(radius) && Finite(rms) && Finite(closure);
        }

        static double Determinant(double[,] value)
        {
            return value[0, 0] *
                   (value[1, 1] * value[2, 2] -
                    value[1, 2] * value[2, 1]) -
                   value[0, 1] *
                   (value[1, 0] * value[2, 2] -
                    value[1, 2] * value[2, 0]) +
                   value[0, 2] *
                   (value[1, 0] * value[2, 1] -
                    value[1, 1] * value[2, 0]);
        }

        static float Mean(IList<float> values)
        {
            if (values == null || values.Count == 0)
                return 0f;
            double sum = 0d;
            for (int index = 0; index < values.Count; index++)
                sum += values[index];
            return (float)(sum / values.Count);
        }

        static float StandardDeviation(
            IList<float> values,
            float mean)
        {
            if (values == null || values.Count == 0)
                return 0f;
            double sum = 0d;
            for (int index = 0; index < values.Count; index++)
            {
                double delta = values[index] - mean;
                sum += delta * delta;
            }
            return (float)Math.Sqrt(sum / values.Count);
        }

        static void Hash(ref ulong hash, string value)
        {
            string text = value ?? string.Empty;
            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];
                hash ^= (byte)(character & 0xff);
                hash *= 1099511628211UL;
                hash ^= (byte)(character >> 8);
                hash *= 1099511628211UL;
            }
            hash ^= 0xff;
            hash *= 1099511628211UL;
        }

        static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static bool Finite(Vector3 value)
        {
            return Finite(value.x) && Finite(value.y) && Finite(value.z);
        }
    

static float Percentile(IList<float> values, float percentile)
        {
            if (values == null || values.Count == 0)
                return float.NaN;
            float[] ordered = values
                .Where(Finite)
                .OrderBy(value => value)
                .ToArray();
            if (ordered.Length == 0)
                return float.NaN;
            float index = Mathf.Clamp01(percentile) *
                          (ordered.Length - 1);
            int lower = Mathf.FloorToInt(index);
            int upper = Mathf.CeilToInt(index);
            return Mathf.Lerp(
                ordered[lower],
                ordered[upper],
                index - lower);
        }
}
}
