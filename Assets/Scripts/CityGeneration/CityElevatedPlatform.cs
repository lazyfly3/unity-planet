using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGeneration
{
    public sealed class CityPlatformLayout
    {
        public IReadOnlyList<List<Vector2>> Regions { get; internal set; }
        public float MinimumGroundHeight { get; internal set; }
        public float MaximumGroundHeight { get; internal set; }
        public float TopHeight { get; internal set; }
        public float SlabThickness { get; internal set; }
        public int FoundationCount => Regions == null ? 0 : Regions.Count;
        public float MaximumClearance =>
            TopHeight - SlabThickness - MinimumGroundHeight;
    }

    public static class CityElevatedPlatformPlanner
    {
        public static CityPlatformLayout Create(
            IReadOnlyList<List<Vector2>> regions,
            ICityTerrainSampler terrain,
            float topClearance = 0.5f,
            float slabThickness = 0.4f,
            float sampleSpacing = 2f)
        {
            if (regions == null || regions.Count == 0)
                throw new ArgumentException(
                    "At least one closed region is required.",
                    nameof(regions));

            topClearance = Mathf.Max(0.05f, topClearance);
            slabThickness = Mathf.Max(0.1f, slabThickness);
            sampleSpacing = Mathf.Max(0.25f, sampleSpacing);

            Vector2 gridOrigin = Vector2.zero;
            if (terrain is CityNoiseTerrain noiseTerrain)
            {
                sampleSpacing = noiseTerrain.Size / noiseTerrain.Resolution;
                gridOrigin = new Vector2(
                    noiseTerrain.transform.position.x
                        - noiseTerrain.Size * 0.5f,
                    noiseTerrain.transform.position.z
                        - noiseTerrain.Size * 0.5f);
            }

            float minimum = float.MaxValue;
            float maximum = float.MinValue;
            for (int regionIndex = 0;
                 regionIndex < regions.Count;
                 regionIndex++)
            {
                IReadOnlyList<Vector2> region = regions[regionIndex];
                SampleBoundary(region);
                SampleInterior(region);
            }

            if (minimum == float.MaxValue)
            {
                minimum = 0f;
                maximum = 0f;
            }

            return new CityPlatformLayout
            {
                Regions = regions,
                MinimumGroundHeight = minimum,
                MaximumGroundHeight = maximum,
                TopHeight = maximum + topClearance,
                SlabThickness = slabThickness
            };

            void Record(Vector2 point)
            {
                float height = terrain == null
                    ? 0f
                    : terrain.SampleTerrain(point).GroundHeight;
                minimum = Mathf.Min(minimum, height);
                maximum = Mathf.Max(maximum, height);
            }

            void SampleBoundary(IReadOnlyList<Vector2> polygon)
            {
                for (int i = 0; i < polygon.Count; i++)
                {
                    Vector2 start = polygon[i];
                    Vector2 end = polygon[(i + 1) % polygon.Count];
                    float length = Vector2.Distance(start, end);
                    int samples = Mathf.Max(
                        1,
                        Mathf.CeilToInt(length / sampleSpacing));
                    for (int sample = 0; sample <= samples; sample++)
                    {
                        Record(Vector2.Lerp(
                            start,
                            end,
                            sample / (float)samples));
                    }
                }
            }

            void SampleInterior(IReadOnlyList<Vector2> polygon)
            {
                float minimumX = float.MaxValue;
                float maximumX = float.MinValue;
                float minimumY = float.MaxValue;
                float maximumY = float.MinValue;
                for (int i = 0; i < polygon.Count; i++)
                {
                    minimumX = Mathf.Min(minimumX, polygon[i].x);
                    maximumX = Mathf.Max(maximumX, polygon[i].x);
                    minimumY = Mathf.Min(minimumY, polygon[i].y);
                    maximumY = Mathf.Max(maximumY, polygon[i].y);
                }

                float firstX = gridOrigin.x + Mathf.Ceil(
                    (minimumX - gridOrigin.x) / sampleSpacing) * sampleSpacing;
                float firstY = gridOrigin.y + Mathf.Ceil(
                    (minimumY - gridOrigin.y) / sampleSpacing) * sampleSpacing;
                for (float y = firstY;
                     y <= maximumY + 0.0001f;
                     y += sampleSpacing)
                {
                    for (float x = firstX;
                         x <= maximumX + 0.0001f;
                         x += sampleSpacing)
                    {
                        var point = new Vector2(x, y);
                        if (CityPolygonGeometry.ContainsPoint(polygon, point))
                            Record(point);
                    }
                }
            }
        }
    }
}
