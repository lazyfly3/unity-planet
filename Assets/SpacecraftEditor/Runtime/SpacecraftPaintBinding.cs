using System;
using UnityEngine;

namespace SpacecraftEditor
{
    /// <summary>
    /// Keeps the authored material array intact and limits player paint to explicitly
    /// selected slots. Glass, emission, mechanisms and weapons remain untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpacecraftPaintBinding : MonoBehaviour
    {
        public const string NativePaintId = "paint.native";

        [Serializable]
        public sealed class RendererBinding
        {
            [SerializeField] private Renderer renderer;
            [SerializeField] private Material[] nativeMaterials;
            [SerializeField] private int[] paintableSlots;

            public Renderer Renderer => renderer;
            public Material[] NativeMaterials => nativeMaterials;
            public int[] PaintableSlots => paintableSlots;

#if UNITY_EDITOR
            public void Configure(Renderer target, int[] slots)
            {
                renderer = target;
                nativeMaterials = target == null
                    ? Array.Empty<Material>()
                    : target.sharedMaterials;
                paintableSlots = slots ?? Array.Empty<int>();
            }
#endif

            public void RestoreNative()
            {
                if (renderer == null || nativeMaterials == null || nativeMaterials.Length == 0)
                    return;
                renderer.sharedMaterials = (Material[])nativeMaterials.Clone();
            }

            public void Apply(Material paint)
            {
                if (renderer == null || paint == null)
                    return;
                Material[] materials = nativeMaterials != null && nativeMaterials.Length > 0
                    ? (Material[])nativeMaterials.Clone()
                    : renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                    return;
                if (paintableSlots != null)
                {
                    foreach (int slot in paintableSlots)
                    {
                        if (slot >= 0 && slot < materials.Length)
                            materials[slot] = paint;
                    }
                }
                renderer.sharedMaterials = materials;
            }
        }

        [SerializeField] private RendererBinding[] bindings = Array.Empty<RendererBinding>();

        public RendererBinding[] Bindings => bindings;

#if UNITY_EDITOR
        public void Configure(RendererBinding[] values)
        {
            bindings = values ?? Array.Empty<RendererBinding>();
        }
#endif

        public void RestoreNative()
        {
            if (bindings == null)
                return;
            foreach (RendererBinding binding in bindings)
                binding?.RestoreNative();
        }

        public void ApplyPaint(Material paint)
        {
            if (bindings == null)
                return;
            foreach (RendererBinding binding in bindings)
                binding?.Apply(paint);
        }
    }
}
