using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class SpaceKilometerScaleValidator : MonoBehaviour
{
    [SerializeField] float metersPerKilometerUnit = 1000f;
    [SerializeField] float exactPresentationRangeKilometers = 100000f;
    [SerializeField] float highSpeedEnterMetersPerSecond = 1000f;
    [SerializeField] float highSpeedExitMetersPerSecond = 600f;

    public bool Validate(out string error)
    {
        if (!Mathf.Approximately(
                metersPerKilometerUnit,
                (float)SpaceKilometerScale.MetersPerUnit))
        {
            error = "Astronomical scale must remain 1000 meters per Unity unit.";
            return false;
        }
        if (exactPresentationRangeKilometers < 1000f)
        {
            error = "Exact astronomical presentation range is too small.";
            return false;
        }
        if (highSpeedExitMetersPerSecond >= highSpeedEnterMetersPerSecond)
        {
            error = "High-speed exit threshold must be lower than the enter threshold.";
            return false;
        }
        if (LayerMask.NameToLayer("SpaceKilometerView") < 0
            || LayerMask.NameToLayer("SpacePhysicsBubble") < 0)
        {
            error = "Required dual-scale rendering layers are missing.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    void Reset()
    {
        metersPerKilometerUnit = (float)SpaceKilometerScale.MetersPerUnit;
        exactPresentationRangeKilometers = 100000f;
        highSpeedEnterMetersPerSecond = 1000f;
        highSpeedExitMetersPerSecond = 600f;
    }
}
