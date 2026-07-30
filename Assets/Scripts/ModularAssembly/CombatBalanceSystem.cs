using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    [Serializable]
    public sealed class CombatBalanceProfile
    {
        public float fullDamageDps = 220f;
        public float hardDamageDps = 450f;
        public float diminishingSlope = 90f;
        public float targetMedianTtk = 30f;
        public float minimumAcceptedTtk = 25f;
        public float maximumAcceptedTtk = 35f;
        public float capabilityLossConfirmation = 1f;

        public float EffectiveDps(float rawDps)
        {
            rawDps = Mathf.Max(0f, rawDps);
            if (rawDps <= fullDamageDps)
            {
                return rawDps;
            }

            float softened = fullDamageDps +
                Mathf.Sqrt(Mathf.Max(0f, rawDps - fullDamageDps) * Mathf.Max(0f, diminishingSlope));
            return Mathf.Min(rawDps, hardDamageDps, softened);
        }

        public float DamageScale(float rawDps)
        {
            return rawDps > 0.0001f ? EffectiveDps(rawDps) / rawDps : 1f;
        }
    }

    [Serializable]
    public sealed class CombatTtkTelemetry
    {
        public string targetName;
        public float firstDamageTime = -1f;
        public float terminalTime = -1f;
        public float accumulatedEffectiveDamage;
        public float disconnectedCpu;
        public int destroyedModules;
        public string terminalReason;

        public bool HasStarted
        {
            get { return firstDamageTime >= 0f; }
        }

        public float Ttk
        {
            get
            {
                if (firstDamageTime < 0f)
                {
                    return 0f;
                }

                float end = terminalTime >= 0f ? terminalTime : Time.time;
                return Mathf.Max(0f, end - firstDamageTime);
            }
        }

        public void Reset(string newTargetName)
        {
            targetName = newTargetName;
            firstDamageTime = -1f;
            terminalTime = -1f;
            accumulatedEffectiveDamage = 0f;
            disconnectedCpu = 0f;
            destroyedModules = 0;
            terminalReason = string.Empty;
        }

        public void RecordDamage(float effectiveDamage)
        {
            if (effectiveDamage <= 0f || terminalTime >= 0f)
            {
                return;
            }

            if (firstDamageTime < 0f)
            {
                firstDamageTime = Time.time;
            }

            accumulatedEffectiveDamage += effectiveDamage;
        }

        public void RecordStructureLoss(int modules, float cpu)
        {
            destroyedModules += Mathf.Max(0, modules);
            disconnectedCpu += Mathf.Max(0f, cpu);
        }

        public void Complete(string reason)
        {
            if (terminalTime >= 0f)
            {
                return;
            }

            terminalReason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
            terminalTime = Time.time;
            Debug.Log(
                "[CombatTTK] target=" + targetName +
                " ttk=" + Ttk.ToString("0.00") + "s" +
                " damage=" + accumulatedEffectiveDamage.ToString("0.0") +
                " modules=" + destroyedModules +
                " disconnectedCpu=" + disconnectedCpu.ToString("0.0") +
                " reason=" + terminalReason);
        }
    }

    public static class CombatBalanceRuntime
    {
        public static readonly CombatBalanceProfile Profile = new CombatBalanceProfile();

        public static float EffectiveDps(float rawDps)
        {
            return Profile.EffectiveDps(rawDps);
        }

        public static float DamageScale(float rawDps)
        {
            return Profile.DamageScale(rawDps);
        }
    }

    /// <summary>
    /// Applies the shared group fire budget without coupling the balance layer to a
    /// particular weapon implementation. WeaponRuntime owns an independent profile,
    /// so changing its damage cannot mutate the global profile catalogue.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatWeaponBudgetController : MonoBehaviour
    {
        private sealed class WeaponReflection
        {
            public object runtime;
            public object profile;
            public int group;
            public FieldInfo damageField;
            public FieldInfo rateField;
            public PropertyInfo damageProperty;
            public PropertyInfo rateProperty;
            public float baseDamage;
            public float rate;

            public void SetDamage(float value)
            {
                if (damageField != null)
                {
                    damageField.SetValue(profile, value);
                }
                else if (damageProperty != null && damageProperty.CanWrite)
                {
                    damageProperty.SetValue(profile, value, null);
                }
            }
        }

        private MonoBehaviour coordinator;
        private FieldInfo weaponsField;
        private readonly Dictionary<object, float> baseDamageByProfile = new Dictionary<object, float>();
        private readonly List<WeaponReflection> reflected = new List<WeaponReflection>(64);
        private float nextRefresh;
        private int lastFingerprint;

        public void Bind(MonoBehaviour target)
        {
            coordinator = target;
            weaponsField = FindField(target != null ? target.GetType() : null, "weapons");
            lastFingerprint = int.MinValue;
            RefreshNow();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh)
            {
                return;
            }

            nextRefresh = Time.unscaledTime + 0.25f;
            RefreshNow();
        }

        public void RefreshNow()
        {
            if (coordinator == null)
            {
                coordinator = FindCoordinator(gameObject);
                weaponsField = FindField(coordinator != null ? coordinator.GetType() : null, "weapons");
            }

            if (coordinator == null || weaponsField == null)
            {
                return;
            }

            IEnumerable list = weaponsField.GetValue(coordinator) as IEnumerable;
            if (list == null)
            {
                return;
            }

            reflected.Clear();
            int fingerprint = 17;
            foreach (object runtime in list)
            {
                WeaponReflection weapon = ReflectWeapon(runtime);
                if (weapon == null)
                {
                    continue;
                }

                reflected.Add(weapon);
                fingerprint = unchecked(fingerprint * 31 + RuntimeHelpersHash(runtime));
                fingerprint = unchecked(fingerprint * 31 + weapon.group);
            }

            if (fingerprint == lastFingerprint)
            {
                return;
            }

            lastFingerprint = fingerprint;
            Dictionary<int, float> rawByGroup = new Dictionary<int, float>();
            for (int i = 0; i < reflected.Count; i++)
            {
                WeaponReflection weapon = reflected[i];
                float raw = weapon.baseDamage * weapon.rate;
                float current;
                rawByGroup.TryGetValue(weapon.group, out current);
                rawByGroup[weapon.group] = current + raw;
            }

            for (int i = 0; i < reflected.Count; i++)
            {
                WeaponReflection weapon = reflected[i];
                float raw = rawByGroup[weapon.group];
                weapon.SetDamage(weapon.baseDamage * CombatBalanceRuntime.DamageScale(raw));
            }
        }

        private WeaponReflection ReflectWeapon(object runtime)
        {
            if (runtime == null)
            {
                return null;
            }

            Type runtimeType = runtime.GetType();
            object profile = ReadMember(runtime, runtimeType, "Profile", "profile");
            if (profile == null)
            {
                return null;
            }

            int group = ReadInt(runtime, runtimeType, 1, "Group", "group");
            Type profileType = profile.GetType();
            FieldInfo damageField = FindField(profileType, "damage");
            FieldInfo rateField = FindField(profileType, "shotsPerSecond");
            PropertyInfo damageProperty = FindProperty(profileType, "damage", "Damage");
            PropertyInfo rateProperty = FindProperty(profileType, "shotsPerSecond", "ShotsPerSecond");
            float damage = ReadFloat(profile, damageField, damageProperty);
            float rate = ReadFloat(profile, rateField, rateProperty);
            if (damage <= 0f || rate <= 0f)
            {
                return null;
            }

            float baseDamage;
            if (!baseDamageByProfile.TryGetValue(profile, out baseDamage))
            {
                baseDamage = damage;
                baseDamageByProfile[profile] = baseDamage;
            }

            return new WeaponReflection
            {
                runtime = runtime,
                profile = profile,
                group = Mathf.Clamp(group, 1, 4),
                damageField = damageField,
                rateField = rateField,
                damageProperty = damageProperty,
                rateProperty = rateProperty,
                baseDamage = baseDamage,
                rate = rate
            };
        }

        private static MonoBehaviour FindCoordinator(GameObject host)
        {
            MonoBehaviour[] behaviours = host.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null && behaviours[i].GetType().Name == "WeaponSystemCoordinator")
                {
                    return behaviours[i];
                }
            }

            return null;
        }

        private static object ReadMember(object instance, Type type, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                PropertyInfo property = FindProperty(type, names[i]);
                if (property != null && property.CanRead)
                {
                    return property.GetValue(instance, null);
                }

                FieldInfo field = FindField(type, names[i]);
                if (field != null)
                {
                    return field.GetValue(instance);
                }
            }

            return null;
        }

        private static int ReadInt(object instance, Type type, int fallback, params string[] names)
        {
            object value = ReadMember(instance, type, names);
            return value is int ? (int)value : fallback;
        }

        private static float ReadFloat(object instance, FieldInfo field, PropertyInfo property)
        {
            object value = field != null
                ? field.GetValue(instance)
                : property != null && property.CanRead
                    ? property.GetValue(instance, null)
                    : null;
            return value is float ? (float)value : 0f;
        }

        private static FieldInfo FindField(Type type, string name)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType;
            }

            return null;
        }

        private static PropertyInfo FindProperty(Type type, params string[] names)
        {
            while (type != null)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    PropertyInfo property = type.GetProperty(
                        names[i],
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (property != null)
                    {
                        return property;
                    }
                }

                type = type.BaseType;
            }

            return null;
        }

        private static int RuntimeHelpersHash(object value)
        {
            return value != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value) : 0;
        }
    }
}
