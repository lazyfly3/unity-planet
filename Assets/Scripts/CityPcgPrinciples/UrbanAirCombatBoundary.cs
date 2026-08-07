using UnityEngine;

namespace UnityPlanet.CityPcg
{
    /// <summary>
    /// 城市空战的分层禁飞边界。它只向整船根 Rigidbody 施力，绝不修改、
    /// 拆分或重新挂接飞船模块；外围无碰撞楼群和雾负责隐藏地图尽头。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class UrbanAirCombatBoundary : MonoBehaviour
    {
        [Header("矩形城市空域")]
        [SerializeField] Vector2 halfExtents = new Vector2(790f, 790f);

        [Tooltip("进入城市边缘多少米时开始显示返航警告。")]
        [Range(60f, 240f)]
        [SerializeField] float warningBand = 130f;

        [Tooltip("最后多少米开始施加柔性返航力。")]
        [Range(20f, 120f)]
        [SerializeField] float returnBand = 55f;

        [Header("返航手感")]
        [Range(1f, 80f)]
        [SerializeField] float baseReturnAcceleration = 16f;

        [Range(20f, 260f)]
        [SerializeField] float maximumReturnAcceleration = 145f;

        [Range(0f, 1f)]
        [SerializeField] float outwardVelocityRemoval = 0.72f;

        [Header("目标整船根刚体")]
        [SerializeField] Rigidbody targetRoot;
        [SerializeField] bool autoFindPlayerTag = true;
        [SerializeField] bool showRuntimeWarning = true;

        float nextSearchTime;
        float pressure01;
        bool beyondHardEdge;

        public Vector2 HalfExtents => halfExtents;
        public float Pressure01 => pressure01;
        public bool IsWarning => pressure01 > 0.001f;
        public bool IsBeyondHardEdge => beyondHardEdge;

        public void Configure(Vector2 targetHalfExtents, Rigidbody root = null)
        {
            halfExtents = new Vector2(
                Mathf.Max(200f, targetHalfExtents.x),
                Mathf.Max(200f, targetHalfExtents.y));
            targetRoot = root;
        }

        void OnValidate()
        {
            halfExtents.x = Mathf.Max(200f, halfExtents.x);
            halfExtents.y = Mathf.Max(200f, halfExtents.y);
            warningBand = Mathf.Clamp(
                warningBand,
                returnBand + 10f,
                Mathf.Min(halfExtents.x, halfExtents.y) * 0.45f);
            returnBand = Mathf.Clamp(returnBand, 20f, warningBand - 10f);
            maximumReturnAcceleration = Mathf.Max(
                baseReturnAcceleration,
                maximumReturnAcceleration);
        }

        void FixedUpdate()
        {
            if (!Application.isPlaying)
                return;
            EnsureTarget();
            if (targetRoot == null || targetRoot.isKinematic)
            {
                pressure01 = 0f;
                beyondHardEdge = false;
                return;
            }

            Vector3 localPosition = transform.InverseTransformPoint(
                targetRoot.worldCenterOfMass);
            float warningX = Mathf.Max(1f, halfExtents.x - warningBand);
            float warningZ = Mathf.Max(1f, halfExtents.y - warningBand);
            float warningPressureX = Mathf.InverseLerp(
                warningX,
                halfExtents.x,
                Mathf.Abs(localPosition.x));
            float warningPressureZ = Mathf.InverseLerp(
                warningZ,
                halfExtents.y,
                Mathf.Abs(localPosition.z));
            pressure01 = Mathf.Max(warningPressureX, warningPressureZ);
            beyondHardEdge = Mathf.Abs(localPosition.x) > halfExtents.x ||
                             Mathf.Abs(localPosition.z) > halfExtents.y;

            float returnX = Mathf.Max(1f, halfExtents.x - returnBand);
            float returnZ = Mathf.Max(1f, halfExtents.y - returnBand);
            float xPressure = Mathf.InverseLerp(
                returnX,
                halfExtents.x,
                Mathf.Abs(localPosition.x));
            float zPressure = Mathf.InverseLerp(
                returnZ,
                halfExtents.y,
                Mathf.Abs(localPosition.z));
            if (xPressure <= 0f && zPressure <= 0f)
                return;

            Vector3 localReturn = new Vector3(
                -Mathf.Sign(localPosition.x) * xPressure,
                0f,
                -Mathf.Sign(localPosition.z) * zPressure);
            if (localReturn.sqrMagnitude <= 0.0001f)
                return;
            float returnPressure = Mathf.Clamp01(
                Mathf.Max(xPressure, zPressure));
            float acceleration = Mathf.Lerp(
                baseReturnAcceleration,
                maximumReturnAcceleration,
                returnPressure * returnPressure);
            if (beyondHardEdge)
                acceleration = maximumReturnAcceleration * 1.55f;
            Vector3 worldReturn = transform.TransformDirection(
                localReturn.normalized);
            targetRoot.AddForce(
                worldReturn * acceleration,
                ForceMode.Acceleration);

            Vector3 localVelocity = transform.InverseTransformDirection(
                targetRoot.velocity);
            Vector3 outwardVelocity = Vector3.zero;
            if (xPressure > 0f &&
                Mathf.Sign(localVelocity.x) == Mathf.Sign(localPosition.x))
            {
                outwardVelocity.x = localVelocity.x * xPressure;
            }
            if (zPressure > 0f &&
                Mathf.Sign(localVelocity.z) == Mathf.Sign(localPosition.z))
            {
                outwardVelocity.z = localVelocity.z * zPressure;
            }
            if (outwardVelocity.sqrMagnitude > 0.0001f)
            {
                targetRoot.AddForce(
                    -transform.TransformDirection(outwardVelocity) *
                    outwardVelocityRemoval,
                    ForceMode.VelocityChange);
            }
        }

        void EnsureTarget()
        {
            if (targetRoot != null || !autoFindPlayerTag ||
                Time.unscaledTime < nextSearchTime)
            {
                return;
            }
            nextSearchTime = Time.unscaledTime + 1f;
            GameObject player;
            try
            {
                player = GameObject.FindGameObjectWithTag("Player");
            }
            catch (UnityException)
            {
                player = null;
            }
            if (player == null)
                return;
            targetRoot = player.GetComponentInParent<Rigidbody>();
            if (targetRoot == null)
                targetRoot = player.GetComponentInChildren<Rigidbody>();
        }

        void OnGUI()
        {
            if (!Application.isPlaying || !showRuntimeWarning || !IsWarning)
                return;
            float alpha = Mathf.Lerp(0.55f, 1f, pressure01);
            Color previous = GUI.color;
            GUI.color = new Color(1f, 0.43f, 0.12f, alpha);
            Rect area = new Rect(
                Screen.width * 0.5f - 210f,
                34f,
                420f,
                58f);
            GUI.Box(
                area,
                beyondHardEdge
                    ? "已越过城市禁飞边界 · 正在强制返航"
                    : "接近城市禁飞区 · 请返回交战空域");
            GUI.color = previous;
        }

        void OnDrawGizmosSelected()
        {
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            DrawRectangle(
                new Vector2(
                    halfExtents.x - warningBand,
                    halfExtents.y - warningBand),
                new Color(1f, 0.72f, 0.08f, 0.75f));
            DrawRectangle(
                new Vector2(
                    halfExtents.x - returnBand,
                    halfExtents.y - returnBand),
                new Color(1f, 0.28f, 0.05f, 0.9f));
            DrawRectangle(
                halfExtents,
                new Color(1f, 0.04f, 0.08f, 1f));
            Gizmos.matrix = previous;
        }

        static void DrawRectangle(Vector2 half, Color color)
        {
            Gizmos.color = color;
            Vector3 a = new Vector3(-half.x, 18f, -half.y);
            Vector3 b = new Vector3(half.x, 18f, -half.y);
            Vector3 c = new Vector3(half.x, 18f, half.y);
            Vector3 d = new Vector3(-half.x, 18f, half.y);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);
        }
    }
}
