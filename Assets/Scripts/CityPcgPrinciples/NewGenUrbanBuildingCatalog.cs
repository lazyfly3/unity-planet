using System;
using UnityEngine;

namespace UnityPlanet.CityPcg
{
    /// <summary>
    /// 新城市场景模型的标准化目录。目录中的每个预制体都必须遵循：
    /// 底面 Y=0、顶部 +Y、正面 +Z、右侧 +X、背面 -Z。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewGenUrbanBuildingCatalog",
        menuName = "Unity Planet/城市 PCG/标准化楼房目录")]
    public sealed class NewGenUrbanBuildingCatalog : ScriptableObject
    {
        [Header("低层：近地掩体与短视线")]
        public GameObject[] low = Array.Empty<GameObject>();

        [Header("中层：主要缠斗遮挡")]
        public GameObject[] medium = Array.Empty<GameObject>();

        [Header("高层：竖向地标与航线分隔")]
        public GameObject[] high = Array.Empty<GameObject>();

        [Header("设施：突袭目标周边")]
        public GameObject[] facility = Array.Empty<GameObject>();

        public GameObject Resolve(AirCombatBuildingBand band, int variant)
        {
            GameObject[] candidates;
            switch (band)
            {
                case AirCombatBuildingBand.Medium:
                    candidates = medium;
                    break;
                case AirCombatBuildingBand.High:
                    candidates = high;
                    break;
                case AirCombatBuildingBand.Facility:
                    candidates = facility;
                    break;
                default:
                    candidates = low;
                    break;
            }

            if (candidates == null || candidates.Length == 0)
                return null;
            int index = variant == int.MinValue
                ? 0
                : Mathf.Abs(variant) % candidates.Length;
            return candidates[index];
        }
    }

}
