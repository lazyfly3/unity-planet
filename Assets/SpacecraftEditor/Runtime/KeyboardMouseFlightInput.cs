using UnityEngine;

namespace SpacecraftEditor
{
    [DefaultExecutionOrder(-600)]
    [DisallowMultipleComponent]
    public sealed class KeyboardMouseFlightInput : MonoBehaviour, ISpacecraftFlightCommandSource, ISpacecraftPilotControlSource
    {
        [SerializeField, Range(0.005f, 0.2f)] float vjoySensitivity = 0.045f;
        [SerializeField, Range(0f, 0.3f)] float vjoyDeadZone = 0.07f;
        [SerializeField, Range(1f, 3f)] float vjoyExponent = 1.8f;
        [SerializeField, Min(1f)] float speedLimitStep = 10f;

        Vector2 vjoyCursor;
        SpacecraftFlightCommand command;
        bool coupledToggleRequested;
        bool directToggleRequested;
        bool cruisePressed;
        float speedLimitDelta;

        public bool CaptureEnabled { get; set; }
        public SpacecraftFlightCommand Command => command;
        public Vector2 VJoyCursor => vjoyCursor;
        public bool FreeLookHeld { get; private set; }
        public bool CruiseHeld { get; private set; }

        void Update()
        {
            if (!CaptureEnabled)
            {
                command = default;
                FreeLookHeld = false;
                CruiseHeld = false;
                return;
            }

            FreeLookHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool workshopOrbit = Input.GetMouseButton(1);
            if (!FreeLookHeld && !workshopOrbit)
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
            CruiseHeld = Input.GetKey(KeyCode.B);

            float wheel = Input.GetAxisRaw("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) > 0.0001f)
                speedLimitDelta += Mathf.Sign(wheel) * speedLimitStep;
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
            speedLimitDelta = 0f;
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
