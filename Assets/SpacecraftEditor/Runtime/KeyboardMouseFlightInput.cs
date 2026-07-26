using UnityEngine;

namespace SpacecraftEditor
{
    [DefaultExecutionOrder(-600)]
    [DisallowMultipleComponent]
    public sealed class KeyboardMouseFlightInput : MonoBehaviour, ISpacecraftFlightCommandSource, ISpacecraftPilotControlSource
    {
        const int CaptureMouseWarmupFrames = 2;

        [SerializeField, Range(0.005f, 0.2f)] float vjoySensitivity = 0.045f;
        [SerializeField, Range(0f, 0.3f)] float vjoyDeadZone = 0.07f;
        [SerializeField, Range(1f, 3f)] float vjoyExponent = 1.8f;
        [SerializeField, Min(1f)] float speedLimitStep = 1f;

        Vector2 vjoyCursor;
        SpacecraftFlightCommand command;
        bool captureEnabled;
        bool pointerInputWasReady;
        int captureMouseWarmupFrames;
        bool coupledToggleRequested;
        bool directToggleRequested;
        bool cruisePressed;
        bool secondaryActionPressed;
        float speedLimitDelta;

        public bool CaptureEnabled
        {
            get => captureEnabled;
            set
            {
                if (captureEnabled == value)
                    return;

                captureEnabled = value;
                command = default;
                FreeLookHeld = false;
                CruiseHeld = false;
                ResetVJoy();
                ClearTransientRequests();
                pointerInputWasReady = false;
                captureMouseWarmupFrames = value
                    ? CaptureMouseWarmupFrames
                    : 0;
            }
        }
        public SpacecraftFlightCommand Command => command;
        public Vector2 VJoyCursor => vjoyCursor;
        public bool FreeLookHeld { get; private set; }
        public bool CruiseHeld { get; private set; }

        void Update()
        {
            bool pointerInputReady = captureEnabled
                && Application.isFocused
                && Cursor.lockState == CursorLockMode.Locked;
            if (!pointerInputReady)
            {
                NeutralizePointerInput();
                pointerInputWasReady = false;
                return;
            }

            // Unity may preserve a large Mouse X/Y delta while the Game view is
            // unfocused or while a cinematic releases/reacquires the pointer.
            // Never feed that delta into the sticky virtual joystick.
            if (!pointerInputWasReady)
            {
                pointerInputWasReady = true;
                NeutralizePointerInput();
                captureMouseWarmupFrames = CaptureMouseWarmupFrames;
                return;
            }

            FreeLookHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool workshopOrbit = Input.GetMouseButton(1);
            bool suppressMouseMotion = captureMouseWarmupFrames > 0;
            if (suppressMouseMotion)
            {
                captureMouseWarmupFrames--;
                vjoyCursor = Vector2.zero;
            }
            else if (!FreeLookHeld && !workshopOrbit)
            {
                vjoyCursor += new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"))
                    * vjoySensitivity;
                vjoyCursor = Vector2.ClampMagnitude(vjoyCursor, 1f);
            }
            if (Input.GetMouseButtonDown(2))
                vjoyCursor = Vector2.zero;

            Vector2 processed = ApplyResponseCurve(vjoyCursor);
            command.translation = Vector3.ClampMagnitude(new Vector3(
                DigitalAxis(KeyCode.A, KeyCode.D),
                DigitalAxis(KeyCode.LeftControl, KeyCode.Space),
                DigitalAxis(KeyCode.S, KeyCode.W)), 1f);
            command.vjoy = new Vector2(-processed.y, processed.x);
            command.roll = DigitalAxis(KeyCode.E, KeyCode.Q);
            command.brake = Input.GetKey(KeyCode.X);
            command.boost = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (Input.GetKeyDown(KeyCode.C))
                coupledToggleRequested = true;
            if (Input.GetKeyDown(KeyCode.T))
                directToggleRequested = true;
            if (Input.GetKeyDown(KeyCode.B))
                cruisePressed = true;
            if (Input.GetMouseButtonDown(1))
                secondaryActionPressed = true;
            CruiseHeld = Input.GetKey(KeyCode.B);

            float wheel = Input.GetAxisRaw("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) > 0.0001f)
                speedLimitDelta += Mathf.Sign(wheel);
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
                return;

            pointerInputWasReady = false;
            NeutralizePointerInput();
        }

        public bool ConsumeCoupledToggle()
        {
            bool value = coupledToggleRequested;
            coupledToggleRequested = false;
            return value;
        }

        public bool ConsumeDirectToggle()
        {
            bool value = directToggleRequested;
            directToggleRequested = false;
            return value;
        }

        public bool ConsumeCruisePressed()
        {
            bool value = cruisePressed;
            cruisePressed = false;
            return value;
        }

        public bool ConsumeSecondaryActionPressed()
        {
            bool value = secondaryActionPressed;
            secondaryActionPressed = false;
            return value;
        }

        public float ConsumeSpeedLimitDelta()
        {
            float value = speedLimitDelta;
            speedLimitDelta = 0f;
            return value;
        }

        public void ResetVJoy()
        {
            vjoyCursor = Vector2.zero;
            command.vjoy = Vector2.zero;
        }

        public void ClearTransientRequests()
        {
            coupledToggleRequested = false;
            directToggleRequested = false;
            cruisePressed = false;
            secondaryActionPressed = false;
            speedLimitDelta = 0f;
        }

        void NeutralizePointerInput()
        {
            command = default;
            FreeLookHeld = false;
            CruiseHeld = false;
            ResetVJoy();
            ClearTransientRequests();
        }

        Vector2 ApplyResponseCurve(Vector2 value)
        {
            float magnitude = value.magnitude;
            if (magnitude <= vjoyDeadZone)
                return Vector2.zero;
            float normalized = Mathf.InverseLerp(vjoyDeadZone, 1f, magnitude);
            float curved = Mathf.Pow(normalized, vjoyExponent);
            return value.normalized * curved;
        }

        static float DigitalAxis(KeyCode negative, KeyCode positive)
        {
            return (Input.GetKey(positive) ? 1f : 0f) - (Input.GetKey(negative) ? 1f : 0f);
        }
    }
}
