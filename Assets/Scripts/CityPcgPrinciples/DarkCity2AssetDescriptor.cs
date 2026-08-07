using System;
using UnityEngine;

namespace UnityPlanet.CityPcg
{
    /// <summary>
    /// Canonical metadata for a derived Dark City 2 prefab. Source assets are
    /// never edited: the derived root is always +Y up, +Z front/span and +X
    /// right, with buildings resting on Y=0 and bridge sockets facing outward.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DarkCity2AssetDescriptor : MonoBehaviour
    {
        [SerializeField] DarkCity2AssetCategory category;
        [SerializeField] DarkCity2PlacementRole placementRole;
        [SerializeField] Vector3 authoredSize = Vector3.one;
        [SerializeField] DarkCity2StretchAxis stretchAxis;
        [SerializeField] Vector2 allowedStretch = Vector2.one;
        [SerializeField] string sourcePath = string.Empty;
        [SerializeField] DarkCity2ConnectionSocket[] sockets =
            Array.Empty<DarkCity2ConnectionSocket>();

        public DarkCity2AssetCategory Category => category;
        public DarkCity2PlacementRole PlacementRole => placementRole;
        public Vector3 AuthoredSize => authoredSize;
        public DarkCity2StretchAxis StretchAxis => stretchAxis;
        public Vector2 AllowedStretch => allowedStretch;
        public string SourcePath => sourcePath;
        public DarkCity2ConnectionSocket[] Sockets => sockets;

        public void Configure(
            DarkCity2AssetCategory targetCategory,
            Vector3 size,
            DarkCity2StretchAxis targetStretchAxis,
            Vector2 stretchRange,
            string source,
            DarkCity2ConnectionSocket[] targetSockets = null,
            DarkCity2PlacementRole targetPlacementRole =
                DarkCity2PlacementRole.None)
        {
            category = targetCategory;
            placementRole = targetPlacementRole;
            authoredSize = new Vector3(
                Mathf.Max(0.01f, size.x),
                Mathf.Max(0.01f, size.y),
                Mathf.Max(0.01f, size.z));
            stretchAxis = targetStretchAxis;
            allowedStretch = new Vector2(
                Mathf.Max(0.01f, Mathf.Min(stretchRange.x, stretchRange.y)),
                Mathf.Max(0.01f, Mathf.Max(stretchRange.x, stretchRange.y)));
            sourcePath = source ?? string.Empty;
            sockets = targetSockets ?? Array.Empty<DarkCity2ConnectionSocket>();
        }
    }
}
