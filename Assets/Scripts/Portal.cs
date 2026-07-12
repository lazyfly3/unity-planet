using System.Collections;
using UnityEngine;

/// <summary>
/// 简单传送门：玩家进入 Trigger 后传送到配对的出口门。
///
/// 挂载方式：
/// 1. 在传送门根物体上挂本脚本，并添加 Collider（勾选 Is Trigger）
/// 2. 将配对的另一扇门拖到 Linked Portal
/// 3. 玩家物体需设置 Tag 为 Player（或修改 Player Tag 字段）
/// 4. 两扇门需互相引用（A → B，B → A）以实现双向传送
/// </summary>
[RequireComponent(typeof(Collider))]
public class Portal : MonoBehaviour
{
    [Header("配对")]
    [SerializeField] Portal linkedPortal;

    [Header("传送设置")]
    [SerializeField] float cooldownDuration = 0.35f;
    [SerializeField] string playerTag = "Player";

    static float lastTeleportTime = -999f;

    public Portal LinkedPortal => linkedPortal;
    public Transform Surface => transform;

    void Reset()
    {
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (linkedPortal == null)
            return;

        if (!other.CompareTag(playerTag))
            return;

        if (Time.time - lastTeleportTime < cooldownDuration)
            return;

        Teleport(other.transform);
    }

    void Teleport(Transform target)
    {
        Transform entry = transform;
        Transform exit = linkedPortal.transform;

        Vector3 localOffset = entry.InverseTransformPoint(target.position);
        localOffset = Vector3.Scale(localOffset, entry.localScale);

        Vector3 newPosition = exit.TransformPoint(localOffset);
        Quaternion rotationDelta = exit.rotation * Quaternion.Inverse(entry.rotation);

        ApplyTeleportRotation(target, rotationDelta);
        ApplyTeleportPosition(target, newPosition);

        lastTeleportTime = Time.time;
        StartCoroutine(DisableTriggersTemporarily());
        linkedPortal.StartCoroutine(linkedPortal.DisableTriggersTemporarily());
    }

    void ApplyTeleportPosition(Transform target, Vector3 newPosition)
    {
        Rigidbody rb = target.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.MovePosition(newPosition);
            return;
        }

        target.position = newPosition;
    }

    void ApplyTeleportRotation(Transform target, Quaternion rotationDelta)
    {
        Rigidbody rb = target.GetComponent<Rigidbody>();
        Transform cameraTransform = FindPlayerCameraTransform(target);

        if (cameraTransform != null && cameraTransform != target)
        {
            Quaternion newCameraRotation = rotationDelta * cameraTransform.rotation;
            Vector3 lookForward = newCameraRotation * Vector3.forward;

            Vector3 flatForward = Vector3.ProjectOnPlane(lookForward, Vector3.up);
            if (flatForward.sqrMagnitude > 0.0001f)
                target.rotation = Quaternion.LookRotation(flatForward.normalized, Vector3.up);

            float newPitch = Vector3.SignedAngle(target.forward, lookForward, target.right);
            newPitch = Mathf.Clamp(newPitch, -80f, 80f);
            cameraTransform.localRotation = Quaternion.Euler(newPitch, 0f, 0f);

            wsad moveController = target.GetComponent<wsad>();
            if (moveController != null)
                moveController.SetPitch(newPitch);

            if (rb != null)
                rb.MoveRotation(target.rotation);

            return;
        }

        Quaternion newRotation = rotationDelta * target.rotation;

        if (rb != null)
            rb.MoveRotation(newRotation);
        else
            target.rotation = newRotation;
    }

    static Transform FindPlayerCameraTransform(Transform target)
    {
        wsad moveController = target.GetComponent<wsad>();
        if (moveController != null && moveController.CameraTransform != null)
            return moveController.CameraTransform;

        Camera camera = target.GetComponentInChildren<Camera>();
        return camera != null ? camera.transform : null;
    }

    IEnumerator DisableTriggersTemporarily()
    {
        Collider col = GetComponent<Collider>();
        if (col == null)
            yield break;

        col.enabled = false;
        yield return new WaitForSeconds(cooldownDuration);
        col.enabled = true;
    }
}
