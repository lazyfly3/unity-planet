using System;
using UnityEngine;

namespace SpacecraftEditor
{
    [DisallowMultipleComponent]
    public sealed class ShipFlightController : MonoBehaviour
    {
        [SerializeField] ShipAssembly assembly;
        [SerializeField] Rigidbody shipBody;
        [SerializeField] ShipHullController hullController;
        [SerializeField] KeyboardMouseFlightInput flightInput;
        [SerializeField] SpacecraftIfcsMotor ifcsMotor;

        SpacecraftApp app;
        bool flying;
        Vector3 resetPosition;
        Quaternion resetRotation;
        float throttle;
        SpacecraftAssistMode lastReportedMode;

        public event Action StateChanged;
        public bool IsFlying => flying;
        public bool StabilizationEnabled => ifcsMotor == null
            || ifcsMotor.AssistMode != SpacecraftAssistMode.Direct;
        public float Throttle => flying && ifcsMotor != null
            && ifcsMotor.AssistMode != SpacecraftAssistMode.Direct
                ? ifcsMotor.CurrentThrottle
                : throttle;
        public float Speed => shipBody == null ? 0f : shipBody.velocity.magnitude;
        public float AngularSpeed => shipBody == null ? 0f : shipBody.angularVelocity.magnitude * Mathf.Rad2Deg;
        public SpacecraftAssistMode AssistMode => ifcsMotor == null
            ? SpacecraftAssistMode.Direct
            : ifcsMotor.AssistMode;
        public SpacecraftControlTelemetry Telemetry => ifcsMotor == null ? default : ifcsMotor.Telemetry;
        public KeyboardMouseFlightInput FlightInput => flightInput;

        public void Configure(SpacecraftApp owner, ShipAssembly targetAssembly, Rigidbody body)
        {
            app = owner;
            assembly = targetAssembly;
            shipBody = body;
            ResolveReferences();
            ifcsMotor?.Configure(shipBody, assembly, hullController, flightInput, true);
        }

        void Awake()
        {
            ResolveReferences();
        }

        void Update()
        {
            if (!flying)
                return;

            if (ifcsMotor != null && ifcsMotor.AssistMode != lastReportedMode)
            {
                lastReportedMode = ifcsMotor.AssistMode;
                StateChanged?.Invoke();
            }
            if (Input.GetKeyDown(KeyCode.R))
                ResetFlight();
            if (Input.GetKeyDown(KeyCode.Escape))
                app?.ExitFlight();
        }

        void FixedUpdate()
        {
            if (!flying || shipBody == null || assembly == null)
                return;
            if (ifcsMotor != null && ifcsMotor.AssistMode != SpacecraftAssistMode.Direct)
                return;
            ApplyThrusterInputs(Input.GetKey);
        }

        public void ApplyThrusterInputs(Func<KeyCode, bool> isPressed)
        {
            throttle = 0f;
            if (shipBody == null || assembly == null)
                return;

            foreach (ThrusterPart thruster in assembly.Thrusters)
            {
                if (thruster == null)
                    continue;
                float partThrottle = ResolveThrottle(thruster.ActivationKey, isPressed);
                thruster.ApplyThrust(shipBody, partThrottle);
                throttle = Mathf.Max(throttle, partThrottle);
            }
        }

        public static float ResolveThrottle(KeyCode activationKey, Func<KeyCode, bool> isPressed)
        {
            return activationKey != KeyCode.None && isPressed != null && isPressed(activationKey) ? 1f : 0f;
        }

        public void EnterFlight()
        {
            ResolveReferences();
            if (shipBody == null)
                return;
            resetPosition = transform.position;
            resetRotation = transform.rotation;
            assembly?.Recalculate();
            shipBody.isKinematic = false;
            shipBody.useGravity = false;
            shipBody.drag = 0f;
            shipBody.angularDrag = 0f;
            ifcsMotor?.Configure(shipBody, assembly, hullController, flightInput, true);
            if (ifcsMotor != null)
            {
                ifcsMotor.SetAssistMode(SpacecraftAssistMode.Coupled);
                ifcsMotor.ControlsEnabled = true;
                lastReportedMode = ifcsMotor.AssistMode;
            }
            else if (flightInput != null)
            {
                flightInput.CaptureEnabled = true;
            }
            flying = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            StateChanged?.Invoke();
        }

        public void ExitFlight()
        {
            if (!flying)
                return;
            if (ifcsMotor != null)
            {
                ifcsMotor.ControlsEnabled = false;
                ifcsMotor.ResetControllerState();
            }
            StopAllThrusters();
            flying = false;
            throttle = 0f;
            shipBody.velocity = Vector3.zero;
            shipBody.angularVelocity = Vector3.zero;
            shipBody.isKinematic = true;
            transform.SetPositionAndRotation(resetPosition, resetRotation);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            StateChanged?.Invoke();
        }

        public void ResetFlight()
        {
            if (shipBody == null)
                return;
            ifcsMotor?.ResetControllerState();
            shipBody.velocity = Vector3.zero;
            shipBody.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(resetPosition, resetRotation);
        }

        void ResolveReferences()
        {
            if (shipBody == null)
                shipBody = GetComponent<Rigidbody>();
            if (assembly == null)
                assembly = GetComponent<ShipAssembly>();
            if (hullController == null)
                hullController = GetComponentInChildren<ShipHullController>(true);
            if (flightInput == null)
                flightInput = GetComponent<KeyboardMouseFlightInput>();
            if (ifcsMotor == null)
                ifcsMotor = GetComponent<SpacecraftIfcsMotor>();
        }

        void StopAllThrusters()
        {
            if (assembly == null)
                return;
            foreach (ThrusterPart thruster in assembly.Thrusters)
                thruster?.SetExhaust(0f);
        }

        void OnDisable()
        {
            if (ifcsMotor != null)
                ifcsMotor.ControlsEnabled = false;
            StopAllThrusters();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
