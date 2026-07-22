using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-520)]
[DisallowMultipleComponent]
public sealed class SpaceCombatVfxPool : MonoBehaviour
{
    public static SpaceCombatVfxPool Instance { get; private set; }

    sealed class Bucket
    {
        public GameObject prefab;
        public readonly List<SpacePooledEffect> instances = new List<SpacePooledEffect>(16);
        public int cursor;
    }

    [SerializeField] SpaceCombatVfxCatalog catalog;
    [SerializeField, Min(1)] int projectilePrewarm = 24;
    [SerializeField, Min(1)] int oneShotPrewarm = 8;
    [SerializeField, Min(1)] int audioSourceCount = 12;

    readonly Dictionary<GameObject, Bucket> buckets = new Dictionary<GameObject, Bucket>();
    AudioSource[] audioSources;
    int audioCursor;

    public SpaceCombatVfxCatalog Catalog => catalog;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        if (catalog == null)
            catalog = Resources.Load<SpaceCombatVfxCatalog>("Spaceflight/SpaceCombatVfxCatalog");
        BuildAudioPool();
        PrewarmCatalog();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public SpacePooledEffect Acquire(GameObject prefab, Transform parent = null)
    {
        if (prefab == null)
            return null;
        Bucket bucket = GetBucket(prefab);
        for (int offset = 0; offset < bucket.instances.Count; offset++)
        {
            int index = (bucket.cursor + offset) % bucket.instances.Count;
            SpacePooledEffect candidate = bucket.instances[index];
            if (candidate != null && !candidate.IsPlaying)
            {
                bucket.cursor = (index + 1) % bucket.instances.Count;
                candidate.Activate(parent);
                return candidate;
            }
        }

        SpacePooledEffect created = CreateInstance(bucket);
        created.Activate(parent);
        return created;
    }

    public void PlayOneShot(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        float scale = 1f,
        float lifetime = 2f)
    {
        SpacePooledEffect effect = Acquire(prefab);
        if (effect == null)
            return;
        effect.transform.SetPositionAndRotation(position, rotation);
        effect.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
        effect.ReleaseAfter(lifetime);
    }

    public void PlayAudio(AudioClip clip, Vector3 position, float volume = 1f)
    {
        if (clip == null || audioSources == null || audioSources.Length == 0)
            return;
        AudioSource source = audioSources[audioCursor];
        audioCursor = (audioCursor + 1) % audioSources.Length;
        source.transform.position = position;
        source.Stop();
        source.clip = clip;
        source.volume = Mathf.Clamp01(volume);
        source.Play();
    }

    public void ShiftActiveEffects(Vector3 shift)
    {
        foreach (Bucket bucket in buckets.Values)
        {
            for (int index = 0; index < bucket.instances.Count; index++)
            {
                SpacePooledEffect effect = bucket.instances[index];
                if (effect != null && effect.IsPlaying && effect.transform.parent == transform)
                    effect.transform.position -= shift;
            }
        }
        if (audioSources == null)
            return;
        for (int index = 0; index < audioSources.Length; index++)
        {
            if (audioSources[index] != null && audioSources[index].isPlaying)
                audioSources[index].transform.position -= shift;
        }
    }

    void PrewarmCatalog()
    {
        if (catalog == null)
            return;
        foreach (GameObject prefab in catalog.EnumeratePrefabs())
        {
            if (prefab == null)
                continue;
            int count = prefab.name.IndexOf("projectile", StringComparison.OrdinalIgnoreCase) >= 0
                ? projectilePrewarm
                : oneShotPrewarm;
            Bucket bucket = GetBucket(prefab);
            while (bucket.instances.Count < count)
                CreateInstance(bucket);
        }
    }

    Bucket GetBucket(GameObject prefab)
    {
        if (buckets.TryGetValue(prefab, out Bucket bucket))
            return bucket;
        bucket = new Bucket { prefab = prefab };
        buckets.Add(prefab, bucket);
        return bucket;
    }

    SpacePooledEffect CreateInstance(Bucket bucket)
    {
        GameObject instance = Instantiate(bucket.prefab, transform);
        instance.name = bucket.prefab.name + "_Pooled_" + bucket.instances.Count.ToString("00");
        SpacePooledEffect effect = instance.GetComponent<SpacePooledEffect>();
        if (effect == null)
            effect = instance.AddComponent<SpacePooledEffect>();
        effect.Initialize(this);
        bucket.instances.Add(effect);
        return effect;
    }

    void BuildAudioPool()
    {
        audioSourceCount = Mathf.Max(1, audioSourceCount);
        audioSources = new AudioSource[audioSourceCount];
        for (int index = 0; index < audioSources.Length; index++)
        {
            var child = new GameObject("CombatAudio_" + index.ToString("00"));
            child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.maxDistance = 220f;
            audioSources[index] = source;
        }
    }
}
