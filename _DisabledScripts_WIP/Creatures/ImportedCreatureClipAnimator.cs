using UnityEngine;

public sealed class ImportedCreatureClipAnimator : MonoBehaviour
{
    static readonly int SpeedId = Animator.StringToHash("Speed");

    Animator animator;
    Rigidbody body;
    SphericalGravitySource gravitySource;
    float maximumSpeed = 1f;

    public void Configure(
        Animator targetAnimator,
        Rigidbody targetBody,
        SphericalGravitySource targetGravitySource,
        float targetMaximumSpeed)
    {
animator = targetAnimator;
        body = targetBody;
        gravitySource = targetGravitySource;
        maximumSpeed = Mathf.Max(.01f, targetMaximumSpeed);
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.SetFloat(SpeedId, 0f);
        }
}

    void Update()
    {
        if (animator == null || body == null || gravitySource == null)
            return;

        Vector3 up = gravitySource.GetUp(body.worldCenterOfMass);
        Vector3 tangentVelocity = Vector3.ProjectOnPlane(body.velocity, up);
        float normalizedSpeed = Mathf.Clamp01(tangentVelocity.magnitude / maximumSpeed);
        animator.SetFloat(SpeedId, normalizedSpeed, .12f, Time.deltaTime);
    }
}
