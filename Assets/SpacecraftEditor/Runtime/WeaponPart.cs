using UnityEngine;

namespace SpacecraftEditor
{
    [DisallowMultipleComponent]
    public sealed class WeaponPart : SpacecraftPart
    {
        [SerializeField, Range(1, 2)] private int fireGroup = 1;
        [SerializeField] private Transform[] muzzles;
        [SerializeField] private Transform gimbalPivot;

        int currentAmmunition;
        int muzzleCursor;
        float heat;
        float nextFireTime;
        Quaternion gimbalRestRotation;
        bool hasGimbalRestRotation;

        public SpacecraftWeaponDefinition Weapon => Definition == null ? null : Definition.Weapon;
        public int FireGroup => fireGroup;
        public int CurrentAmmunition => currentAmmunition;
        public float Heat => heat;
        public bool IsOverheated => heat >= 0.999f;
        public Transform PrimaryMuzzle
        {
            get
            {
                ResolveMuzzles();
                return muzzles == null || muzzles.Length == 0 || muzzles[0] == null ? transform : muzzles[0];
            }
        }
        public Vector3 RestFireDirection
        {
            get
            {
                ResolveMuzzles();
                if (gimbalPivot != null && hasGimbalRestRotation)
                {
                    Transform parent = gimbalPivot.parent;
                    Vector3 localForward = gimbalRestRotation * Vector3.forward;
                    return parent == null ? localForward.normalized : parent.TransformDirection(localForward).normalized;
                }
                return PrimaryMuzzle.forward.normalized;
            }
        }

        public void ApplyAimDirection(Vector3 worldDirection)
        {
            ResolveMuzzles();
            if (Weapon == null || Weapon.MountMode != SpaceWeaponMountMode.Gimbaled ||
                gimbalPivot == null || worldDirection.sqrMagnitude < 0.0001f)
                return;

            Vector3 localDirection = gimbalPivot.parent == null
                ? worldDirection.normalized
                : gimbalPivot.parent.InverseTransformDirection(worldDirection.normalized);
            gimbalPivot.localRotation = Quaternion.LookRotation(localDirection, Vector3.forward);
        }

        public void ResetAimVisual()
        {
            ResolveMuzzles();
            if (gimbalPivot != null && hasGimbalRestRotation)
                gimbalPivot.localRotation = gimbalRestRotation;
        }

        protected override void Awake()
        {
            base.Awake();
            ResolveMuzzles();
        }

        public override void Configure(
            ShipPartDefinition partDefinition,
            float scale,
            string id = null,
            string groupId = null,
            KeyCode activationKey = KeyCode.None,
            string paintId = null,
            int weaponGroup = 0)
        {
            base.Configure(partDefinition, scale, id, groupId, activationKey, paintId, weaponGroup);
            var profile = Weapon;
            fireGroup = weaponGroup > 0 ? Mathf.Clamp(weaponGroup, 1, 2) : profile == null ? 1 : profile.DefaultFireGroup;
            currentAmmunition = profile == null ? 0 : profile.AmmunitionCapacity;
            heat = 0f;
            nextFireTime = 0f;
            muzzleCursor = 0;
            ResolveMuzzles();
        }

        public void SetFireGroup(int value)
        {
            fireGroup = Mathf.Clamp(value, 1, 2);
        }

        public void TickCooling(float deltaTime)
        {
            if (Weapon != null)
                heat = Mathf.Max(0f, heat - Weapon.HeatDissipation * Mathf.Max(0f, deltaTime));
        }

        public bool TryBeginShot(float time, float availableCapacitor, out float capacitorCost, out Transform muzzle)
        {
            capacitorCost = 0f;
            muzzle = null;
            var profile = Weapon;
            if (profile == null || time < nextFireTime || IsOverheated)
                return false;
            if (profile.UsesAmmunition && currentAmmunition <= 0)
                return false;
            if (profile.UsesCapacitor && availableCapacitor + 0.0001f < profile.CapacitorCost)
                return false;

            nextFireTime = time + 1f / Mathf.Max(0.1f, profile.RoundsPerSecond);
            if (profile.UsesAmmunition)
                currentAmmunition--;
            capacitorCost = profile.CapacitorCost;
            heat = Mathf.Clamp01(heat + profile.HeatPerShot);
            muzzle = NextMuzzle();
            return muzzle != null;
        }

        public override PlacedPartState CaptureState()
        {
            var state = base.CaptureState();
            state.weaponGroup = fireGroup;
            return state;
        }

        Transform NextMuzzle()
        {
            ResolveMuzzles();
            if (muzzles == null || muzzles.Length == 0)
                return transform;
            var result = muzzles[muzzleCursor % muzzles.Length];
            muzzleCursor = (muzzleCursor + 1) % muzzles.Length;
            return result == null ? transform : result;
        }

        void ResolveMuzzles()
        {
            if (gimbalPivot == null)
            {
                Transform[] transforms = GetComponentsInChildren<Transform>(true);
                for (int index = 0; index < transforms.Length; index++)
                {
                    if (transforms[index] != transform &&
                        transforms[index].name.Equals("GimbalPivot", System.StringComparison.OrdinalIgnoreCase))
                    {
                        gimbalPivot = transforms[index];
                        gimbalRestRotation = gimbalPivot.localRotation;
                        hasGimbalRestRotation = true;
                        break;
                    }
                }
            }
            if (muzzles != null && muzzles.Length > 0)
                return;
            var all = GetComponentsInChildren<Transform>(true);
            int count = 0;
            foreach (var candidate in all)
            {
                if (candidate != transform && candidate.name.StartsWith("Muzzle", System.StringComparison.OrdinalIgnoreCase))
                    count++;
            }
            if (count == 0)
            {
                muzzles = new[] { transform };
                return;
            }
            muzzles = new Transform[count];
            int cursor = 0;
            foreach (var candidate in all)
            {
                if (candidate != transform && candidate.name.StartsWith("Muzzle", System.StringComparison.OrdinalIgnoreCase))
                    muzzles[cursor++] = candidate;
            }
        }
    }
}
