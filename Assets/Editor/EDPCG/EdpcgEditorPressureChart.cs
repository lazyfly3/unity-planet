#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityPlanet.EDPCG;

namespace UnityPlanet.EditorTools
{
    /// <summary>
    /// Editor-only pressure renderer shared by the live lab. It deliberately
    /// uses editor drawing APIs so no chart code or texture reaches Game view.
    /// </summary>
    internal static class EdpcgEditorPressureChart
    {
        const int MaximumRenderedSamples = 600;
        static readonly Color Background = new Color(0.055f, 0.07f, 0.095f, 1f);
        static readonly Color Grid = new Color(1f, 1f, 1f, 0.09f);
        static readonly Color TargetBand = new Color(0.20f, 0.76f, 0.47f, 0.14f);
        static readonly Color Actual = new Color(1f, 0.64f, 0.18f, 1f);
        static readonly Color Forecast4 = new Color(0.22f, 0.68f, 1f, 1f);
        static readonly Color Forecast8 = new Color(0.68f, 0.44f, 1f, 1f);
        static readonly Color HardLimit = new Color(1f, 0.30f, 0.26f, 0.85f);

        public static void Draw(
            Rect rect,
            IReadOnlyList<EdpcgPressureSample> samples,
            float visibleSeconds,
            float requestedEnd,
            float hardPressureLimit)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            EditorGUI.DrawRect(rect, Background);
            DrawGrid(rect);
            if (samples == null || samples.Count == 0)
            {
                GUI.Label(rect, "等待压力样本……", CenteredLabel());
                return;
            }

            float lastSampleTime = samples[samples.Count - 1].missionTime;
            float end = Mathf.Clamp(requestedEnd, samples[0].missionTime, lastSampleTime);
            float start = Mathf.Max(samples[0].missionTime, end - visibleSeconds);
            end = Mathf.Max(start + 0.1f, end);
            int first = FindFirstVisibleSample(samples, start);
            int last = FindLastVisibleSample(samples, end);
            if (last <= first)
                last = Mathf.Min(samples.Count - 1, first + 1);
            int visibleCount = Mathf.Max(1, last - first + 1);
            int stride = Mathf.Max(1,
                Mathf.CeilToInt(visibleCount / (float)MaximumRenderedSamples));

            DrawTargetBand(rect, samples, first, last, stride, start, end);
            DrawPhaseBoundaries(rect, samples, first, last, stride, start, end);
            DrawHorizontalRule(rect, hardPressureLimit, HardLimit, 1.5f);
            DrawSeries(rect, samples, first, last, stride, start, end,
                SampleValue.Actual, Actual, 2.5f);
            DrawSeries(rect, samples, first, last, stride, start, end,
                SampleValue.Forecast4, Forecast4, 1.8f);
            DrawSeries(rect, samples, first, last, stride, start, end,
                SampleValue.Forecast8, Forecast8, 1.2f);
            DrawAxisLabels(rect, start, end);
        }

        enum SampleValue
        {
            Actual,
            Forecast4,
            Forecast8
        }

        static void DrawGrid(Rect rect)
        {
            for (int index = 0; index <= 10; index++)
            {
                float ratio = index / 10f;
                float y = Mathf.Lerp(rect.yMax, rect.yMin, ratio);
                EditorGUI.DrawRect(new Rect(rect.x, y, rect.width, 1f), Grid);
            }
            for (int index = 1; index < 6; index++)
            {
                float x = Mathf.Lerp(rect.xMin, rect.xMax, index / 6f);
                EditorGUI.DrawRect(new Rect(x, rect.y, 1f, rect.height), Grid);
            }
        }

        static void DrawTargetBand(
            Rect rect,
            IReadOnlyList<EdpcgPressureSample> samples,
            int first,
            int last,
            int stride,
            float start,
            float end)
        {
            for (int index = first; index <= last; index += stride)
            {
                int next = Mathf.Min(last, index + stride);
                EdpcgPressureSample sample = samples[index];
                float x0 = TimeX(rect, sample.missionTime, start, end);
                float x1 = TimeX(rect, samples[next].missionTime, start, end);
                Rect band = Rect.MinMaxRect(
                    x0,
                    PressureY(rect, sample.targetPressureMaximum),
                    Mathf.Max(x0 + 1f, x1),
                    PressureY(rect, sample.targetPressureMinimum));
                EditorGUI.DrawRect(band, TargetBand);
            }
        }

        static void DrawPhaseBoundaries(
            Rect rect,
            IReadOnlyList<EdpcgPressureSample> samples,
            int first,
            int last,
            int stride,
            float start,
            float end)
        {
            EdpcgEncounterPhase previous = samples[first].phase;
            for (int index = first + stride; index <= last; index += stride)
            {
                EdpcgPressureSample sample = samples[index];
                if (sample.phase == previous)
                    continue;
                previous = sample.phase;
                float x = TimeX(rect, sample.missionTime, start, end);
                EditorGUI.DrawRect(new Rect(x, rect.y, 1f, rect.height),
                    new Color(1f, 1f, 1f, 0.24f));
                GUI.Label(new Rect(x + 3f, rect.y + 3f, 75f, 18f),
                    sample.phase.ToString(), EditorStyles.miniLabel);
            }
        }

        static void DrawHorizontalRule(
            Rect rect,
            float pressure,
            Color color,
            float thickness)
        {
            float y = PressureY(rect, pressure);
            EditorGUI.DrawRect(
                new Rect(rect.x, y - thickness * 0.5f, rect.width, thickness),
                color);
        }

        static void DrawSeries(
            Rect rect,
            IReadOnlyList<EdpcgPressureSample> samples,
            int first,
            int last,
            int stride,
            float start,
            float end,
            SampleValue value,
            Color color,
            float width)
        {
            var points = new List<Vector3>(
                Mathf.Min(MaximumRenderedSamples + 1, last - first + 2));
            for (int index = first; index <= last; index += stride)
            {
                EdpcgPressureSample sample = samples[index];
                points.Add(new Vector3(
                    TimeX(rect, sample.missionTime, start, end),
                    PressureY(rect, ReadValue(sample, value)),
                    0f));
            }
            if ((last - first) % stride != 0)
            {
                EdpcgPressureSample sample = samples[last];
                points.Add(new Vector3(
                    TimeX(rect, sample.missionTime, start, end),
                    PressureY(rect, ReadValue(sample, value)),
                    0f));
            }
            if (points.Count < 2)
                return;
            Handles.BeginGUI();
            Handles.color = color;
            Handles.DrawAAPolyLine(width, points.ToArray());
            Handles.color = Color.white;
            Handles.EndGUI();
        }

        static void DrawAxisLabels(Rect rect, float start, float end)
        {
            GUI.Label(new Rect(rect.x + 4f, rect.y + 3f, 42f, 18f),
                "100%", EditorStyles.miniLabel);
            GUI.Label(new Rect(rect.x + 4f, rect.center.y - 9f, 42f, 18f),
                "50%", EditorStyles.miniLabel);
            GUI.Label(new Rect(rect.x + 4f, rect.yMax - 20f, 42f, 18f),
                "0%", EditorStyles.miniLabel);
            GUI.Label(new Rect(rect.x + 42f, rect.yMax - 20f, 72f, 18f),
                start.ToString("0.0") + "s", EditorStyles.miniLabel);
            GUI.Label(new Rect(rect.xMax - 76f, rect.yMax - 20f, 72f, 18f),
                end.ToString("0.0") + "s", EditorStyles.miniLabel);
        }

        static int FindFirstVisibleSample(
            IReadOnlyList<EdpcgPressureSample> samples,
            float start)
        {
            int index = 0;
            while (index + 1 < samples.Count &&
                   samples[index + 1].missionTime < start)
            {
                index++;
            }
            return index;
        }

        static int FindLastVisibleSample(
            IReadOnlyList<EdpcgPressureSample> samples,
            float end)
        {
            int index = samples.Count - 1;
            while (index > 0 && samples[index].missionTime > end)
                index--;
            return index;
        }

        static float ReadValue(EdpcgPressureSample sample, SampleValue value)
        {
            switch (value)
            {
                case SampleValue.Forecast4:
                    return sample.forecastPressure4Seconds;
                case SampleValue.Forecast8:
                    return sample.forecastPressure8Seconds;
                default:
                    return sample.actualPressure;
            }
        }

        static float TimeX(Rect rect, float time, float start, float end)
        {
            return Mathf.Lerp(rect.xMin, rect.xMax,
                Mathf.InverseLerp(start, end, time));
        }

        static float PressureY(Rect rect, float pressure)
        {
            return Mathf.Lerp(rect.yMax, rect.yMin, Mathf.Clamp01(pressure));
        }

        static GUIStyle CenteredLabel()
        {
            GUIStyle style = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            return style;
        }
    }
}
#endif
