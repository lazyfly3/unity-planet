using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace UnityPlanet.EDPCG
{
    public static class EdpcgExportService
    {
        const int ImageWidth = 1600;
        const int ImageHeight = 900;
        const int PlotLeft = 100;
        const int PlotRight = 1540;
        const int PlotTop = 80;
        const int PlotBottom = 790;

        [Serializable]
        sealed class TelemetryExport
        {
            public string missionId = string.Empty;
            public int citySeed;
            public int rosterSeed;
            public EdpcgPressureSample[] samples =
                Array.Empty<EdpcgPressureSample>();
            public EdpcgTelemetryEvent[] events =
                Array.Empty<EdpcgTelemetryEvent>();
            public EdpcgRuntimeChange[] tuningChanges =
                Array.Empty<EdpcgRuntimeChange>();
        }

        public static string ExportAll(
            EdpcgEncounterRuntime runtime,
            string parentDirectory = null)
        {
            if (runtime == null || runtime.Recorder == null)
                throw new ArgumentNullException(nameof(runtime));
            string root = string.IsNullOrWhiteSpace(parentDirectory)
                ? Path.Combine(Application.persistentDataPath, "EDPCGExports")
                : parentDirectory;
            string folder = Path.Combine(
                root,
                DateTime.UtcNow.ToString(
                    "yyyyMMdd-HHmmss",
                    CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            var files = new List<string>(12);
            ExportPng(runtime, Path.Combine(folder, "pressure-curve.png"));
            files.Add("pressure-curve.png");
            ExportSvg(runtime, Path.Combine(folder, "pressure-curve.svg"));
            files.Add("pressure-curve.svg");
            ExportCsv(runtime, Path.Combine(folder, "telemetry.csv"));
            files.Add("telemetry.csv");
            ExportJson(runtime, Path.Combine(folder, "session.json"));
            files.Add("session.json");
            ExportHtml(runtime, Path.Combine(folder, "report.html"));
            files.Add("report.html");
            ExportLegendIcons(folder, files);
            WriteManifest(runtime, folder, files);
            return folder;
        }

        public static void ExportPng(
            EdpcgEncounterRuntime runtime,
            string path)
        {
            ValidatePath(path);
            IReadOnlyList<EdpcgPressureSample> samples =
                runtime.Recorder.Samples;
            Color32 background = new Color32(18, 22, 29, 255);
            Color32[] pixels = new Color32[ImageWidth * ImageHeight];
            for (int index = 0; index < pixels.Length; index++)
                pixels[index] = background;
            FillRect(
                pixels,
                PlotLeft,
                PlotTop,
                PlotRight,
                PlotBottom,
                new Color32(26, 32, 42, 255));
            for (int grid = 0; grid <= 10; grid++)
            {
                int y = Mathf.RoundToInt(Mathf.Lerp(
                    PlotBottom,
                    PlotTop,
                    grid / 10f));
                DrawLine(
                    pixels,
                    PlotLeft,
                    y,
                    PlotRight,
                    y,
                    new Color32(55, 63, 76, 255));
            }
            if (samples.Count > 0)
            {
                float start = samples[0].missionTime;
                float end = Mathf.Max(
                    start + 0.1f,
                    samples[samples.Count - 1].missionTime);
                for (int index = 0; index < samples.Count; index++)
                {
                    EdpcgPressureSample sample = samples[index];
                    int x = TimeToX(sample.missionTime, start, end);
                    int x2 = index + 1 < samples.Count
                        ? TimeToX(samples[index + 1].missionTime, start, end)
                        : x + 2;
                    int top = PressureToY(sample.targetPressureMaximum);
                    int bottom = PressureToY(sample.targetPressureMinimum);
                    FillRectAlpha(
                        pixels,
                        x,
                        top,
                        Mathf.Max(x + 1, x2),
                        bottom,
                        new Color32(60, 183, 134, 52));
                    if (index == 0)
                        continue;
                    EdpcgPressureSample previous = samples[index - 1];
                    int px = TimeToX(previous.missionTime, start, end);
                    DrawLine(
                        pixels,
                        px,
                        PressureToY(previous.actualPressure),
                        x,
                        PressureToY(sample.actualPressure),
                        new Color32(255, 180, 65, 255),
                        3);
                    DrawLine(
                        pixels,
                        px,
                        PressureToY(previous.forecastPressure4Seconds),
                        x,
                        PressureToY(sample.forecastPressure4Seconds),
                        new Color32(100, 184, 255, 255),
                        2);
                    DrawLine(
                        pixels,
                        px,
                        PressureToY(previous.forecastPressure8Seconds),
                        x,
                        PressureToY(sample.forecastPressure8Seconds),
                        new Color32(174, 125, 255, 255));
                }
            }
            var texture = new Texture2D(
                ImageWidth,
                ImageHeight,
                TextureFormat.RGBA32,
                false,
                false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(texture);
                else
                    UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        public static void ExportSvg(
            EdpcgEncounterRuntime runtime,
            string path)
        {
            ValidatePath(path);
            IReadOnlyList<EdpcgPressureSample> samples =
                runtime.Recorder.Samples;
            var builder = new StringBuilder(32768);
            builder.AppendLine(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1600\" height=\"900\" viewBox=\"0 0 1600 900\">");
            builder.AppendLine("<rect width=\"1600\" height=\"900\" fill=\"#12161d\"/>");
            builder.AppendLine("<rect x=\"100\" y=\"80\" width=\"1440\" height=\"710\" fill=\"#1a202a\" stroke=\"#566174\"/>");
            builder.AppendLine("<text x=\"100\" y=\"48\" fill=\"#f2f5f8\" font-family=\"sans-serif\" font-size=\"28\">EDPCG pressure curve</text>");
            if (samples.Count > 0)
            {
                float start = samples[0].missionTime;
                float end = Mathf.Max(
                    start + 0.1f,
                    samples[samples.Count - 1].missionTime);
                builder.Append("<path fill=\"#3cb786\" fill-opacity=\"0.18\" d=\"");
                AppendBandPath(builder, samples, start, end);
                builder.AppendLine("\"/>");
                AppendPolyline(builder, samples, start, end,
                    sample => sample.actualPressure, "#ffb441", 4);
                AppendPolyline(builder, samples, start, end,
                    sample => sample.forecastPressure4Seconds, "#64b8ff", 3);
                AppendPolyline(builder, samples, start, end,
                    sample => sample.forecastPressure8Seconds, "#ae7dff", 2);
            }
            builder.AppendLine("<g font-family=\"sans-serif\" font-size=\"18\" fill=\"#f2f5f8\"><text x=\"110\" y=\"830\">target band</text><text x=\"290\" y=\"830\">actual</text><text x=\"430\" y=\"830\">forecast 4s</text><text x=\"620\" y=\"830\">forecast 8s</text></g>");
            builder.AppendLine("</svg>");
            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
        }

        public static void ExportCsv(
            EdpcgEncounterRuntime runtime,
            string path)
        {
            ValidatePath(path);
            var builder = new StringBuilder(65536);
            builder.AppendLine(
                "time,phase,target_min,target_max,actual,forecast_4s,forecast_8s,enemy,navigation,environmental_pursuit_pressure,environment,player_strain,active,engaged,environmental_pursuers,tokens,suicide_commits,ranged_lanes,nav_recovery,resolved,area_id,area_kind");
            IReadOnlyList<EdpcgPressureSample> samples =
                runtime.Recorder.Samples;
            for (int index = 0; index < samples.Count; index++)
            {
                EdpcgPressureSample item = samples[index];
                AppendNumber(builder, item.missionTime);
                builder.Append(',').Append(item.phase).Append(',');
                AppendNumber(builder, item.targetPressureMinimum);
                builder.Append(',');
                AppendNumber(builder, item.targetPressureMaximum);
                builder.Append(',');
                AppendNumber(builder, item.actualPressure);
                builder.Append(',');
                AppendNumber(builder, item.forecastPressure4Seconds);
                builder.Append(',');
                AppendNumber(builder, item.forecastPressure8Seconds);
                builder.Append(',');
                AppendNumber(builder, item.enemyThreatPressure);
                builder.Append(',');
                AppendNumber(builder, item.navigationPressure);
                builder.Append(',');
                AppendNumber(builder, item.environmentalPursuitPressure);
                builder.Append(',');
                AppendNumber(builder, item.measuredEnvironmentPressure);
                builder.Append(',');
                AppendNumber(builder, item.playerStrain);
                builder.Append(',').Append(item.activeCount)
                    .Append(',').Append(item.engagementCount)
                    .Append(',').Append(item.environmentalPursuitCount)
                    .Append(',').Append(item.attackTokensUsed)
                    .Append(',').Append(item.suicideCommitCount)
                    .Append(',').Append(item.rangedFireLaneCount)
                    .Append(',').Append(item.navigationRecoveryCount)
                    .Append(',').Append(item.resolvedCount).Append(',')
                    .Append(Csv(item.activeTacticalAreaId)).Append(',')
                    .Append(item.activeTacticalAreaKind).AppendLine();
            }
            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
        }

        public static void ExportJson(
            EdpcgEncounterRuntime runtime,
            string path)
        {
            ValidatePath(path);
            var export = new TelemetryExport
            {
                missionId = runtime.MissionId,
                citySeed = runtime.CitySeed,
                rosterSeed = runtime.RosterSeed,
                samples = Copy(runtime.Recorder.Samples),
                events = Copy(runtime.Recorder.Events),
                tuningChanges = Copy(runtime.Recorder.Changes)
            };
            File.WriteAllText(
                path,
                JsonUtility.ToJson(export, true),
                Encoding.UTF8);
        }

        public static void ExportHtml(
            EdpcgEncounterRuntime runtime,
            string path)
        {
            ValidatePath(path);
            string temporarySvg = Path.GetTempFileName();
            try
            {
                ExportSvg(runtime, temporarySvg);
                string svg = File.ReadAllText(temporarySvg, Encoding.UTF8);
                var builder = new StringBuilder(svg.Length + 4096);
                builder.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>EDPCG report</title><style>body{background:#12161d;color:#eef2f6;font-family:system-ui;margin:24px}section{max-width:1600px;margin:auto}svg{width:100%;height:auto}table{border-collapse:collapse}td,th{padding:7px 12px;border-bottom:1px solid #465063;text-align:left}</style></head><body><section>");
                builder.Append("<h1>EDPCG session — ")
                    .Append(Html(runtime.MissionId)).AppendLine("</h1>");
                builder.Append(svg);
                EdpcgPressureSample sample = runtime.CurrentSample;
                builder.Append("<table><tr><th>Planet tier</th><td>")
                    .Append(runtime.Settings.planetTier)
                    .Append("</td></tr><tr><th>Roster</th><td>")
                    .Append(runtime.ResolvedCount).Append('/')
                    .Append(runtime.RosterCount)
                    .Append("</td></tr><tr><th>Credited kills</th><td>")
                    .Append(runtime.CreditedKills)
                    .Append("</td></tr><tr><th>Pressure</th><td>")
                    .Append(sample.actualPressure.ToString("P1", CultureInfo.InvariantCulture))
                    .AppendLine("</td></tr></table></section></body></html>");
                File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
            }
            finally
            {
                if (File.Exists(temporarySvg))
                    File.Delete(temporarySvg);
            }
        }

        static void ExportLegendIcons(
            string folder,
            List<string> files)
        {
            string[] names =
            {
                "phase-preview", "phase-engage", "phase-peak",
                "phase-recover", "event-spawn", "event-attack",
                "event-navigation-recovery"
            };
            string[] colors =
            {
                "#64b8ff", "#3cb786", "#ffb441", "#ae7dff",
                "#64b8ff", "#ff6b55", "#ae7dff"
            };
            for (int index = 0; index < names.Length; index++)
            {
                string fileName = "icon-" + names[index] + ".svg";
                string svg =
                    "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\" viewBox=\"0 0 64 64\">" +
                    "<circle cx=\"32\" cy=\"32\" r=\"25\" fill=\"" + colors[index] + "\" fill-opacity=\"0.22\" stroke=\"" + colors[index] + "\" stroke-width=\"4\"/>" +
                    "<path d=\"M20 34l8 8 17-20\" fill=\"none\" stroke=\"" + colors[index] + "\" stroke-width=\"5\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/></svg>";
                File.WriteAllText(
                    Path.Combine(folder, fileName),
                    svg,
                    Encoding.UTF8);
                files.Add(fileName);
            }
        }

        static void WriteManifest(
            EdpcgEncounterRuntime runtime,
            string folder,
            List<string> files)
        {
            var manifest = new EdpcgExportManifest
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                profileVersion = runtime.Profile.profileVersion,
                formulaVersion = runtime.Profile.formulaVersion,
                buildVersion = Application.version,
                missionId = runtime.MissionId,
                planetDifficultyTier = runtime.Settings.planetTier,
                citySeed = runtime.CitySeed,
                rosterSeed = runtime.RosterSeed,
                rangeStart = runtime.Recorder.Samples.Count > 0
                    ? runtime.Recorder.Samples[0].missionTime
                    : 0f,
                rangeEnd = runtime.CurrentSample.missionTime,
                sampleRate = 10f,
                files = files.ToArray(),
                tuningChanges = Copy(runtime.Recorder.Changes)
            };
            File.WriteAllText(
                Path.Combine(folder, "manifest.json"),
                JsonUtility.ToJson(manifest, true),
                Encoding.UTF8);
        }

        static void AppendBandPath(
            StringBuilder builder,
            IReadOnlyList<EdpcgPressureSample> samples,
            float start,
            float end)
        {
            for (int index = 0; index < samples.Count; index++)
            {
                EdpcgPressureSample sample = samples[index];
                builder.Append(index == 0 ? 'M' : 'L')
                    .Append(TimeToX(sample.missionTime, start, end))
                    .Append(' ').Append(PressureToY(sample.targetPressureMaximum))
                    .Append(' ');
            }
            for (int index = samples.Count - 1; index >= 0; index--)
            {
                EdpcgPressureSample sample = samples[index];
                builder.Append('L')
                    .Append(TimeToX(sample.missionTime, start, end))
                    .Append(' ').Append(PressureToY(sample.targetPressureMinimum))
                    .Append(' ');
            }
            builder.Append('Z');
        }

        static void AppendPolyline(
            StringBuilder builder,
            IReadOnlyList<EdpcgPressureSample> samples,
            float start,
            float end,
            Func<EdpcgPressureSample, float> value,
            string color,
            int width)
        {
            builder.Append("<polyline fill=\"none\" stroke=\"")
                .Append(color).Append("\" stroke-width=\"")
                .Append(width).Append("\" points=\"");
            for (int index = 0; index < samples.Count; index++)
            {
                builder.Append(TimeToX(samples[index].missionTime, start, end))
                    .Append(',').Append(PressureToY(value(samples[index])))
                    .Append(' ');
            }
            builder.AppendLine("\"/>");
        }

        static int TimeToX(float time, float start, float end)
        {
            return Mathf.RoundToInt(Mathf.Lerp(
                PlotLeft,
                PlotRight,
                Mathf.InverseLerp(start, end, time)));
        }

        static int PressureToY(float pressure)
        {
            return Mathf.RoundToInt(Mathf.Lerp(
                PlotBottom,
                PlotTop,
                Mathf.Clamp01(pressure)));
        }

        static void FillRect(
            Color32[] pixels,
            int left,
            int top,
            int right,
            int bottom,
            Color32 color)
        {
            left = Mathf.Clamp(left, 0, ImageWidth - 1);
            right = Mathf.Clamp(right, 0, ImageWidth - 1);
            top = Mathf.Clamp(top, 0, ImageHeight - 1);
            bottom = Mathf.Clamp(bottom, 0, ImageHeight - 1);
            for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
                pixels[(ImageHeight - 1 - y) * ImageWidth + x] = color;
        }

        static void FillRectAlpha(
            Color32[] pixels,
            int left,
            int top,
            int right,
            int bottom,
            Color32 color)
        {
            left = Mathf.Clamp(left, 0, ImageWidth - 1);
            right = Mathf.Clamp(right, 0, ImageWidth - 1);
            top = Mathf.Clamp(top, 0, ImageHeight - 1);
            bottom = Mathf.Clamp(bottom, 0, ImageHeight - 1);
            float alpha = color.a / 255f;
            for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                int pixel = (ImageHeight - 1 - y) * ImageWidth + x;
                Color32 existing = pixels[pixel];
                pixels[pixel] = new Color32(
                    (byte)Mathf.RoundToInt(Mathf.Lerp(existing.r, color.r, alpha)),
                    (byte)Mathf.RoundToInt(Mathf.Lerp(existing.g, color.g, alpha)),
                    (byte)Mathf.RoundToInt(Mathf.Lerp(existing.b, color.b, alpha)),
                    255);
            }
        }

        static void DrawLine(
            Color32[] pixels,
            int x0,
            int y0,
            int x1,
            int y1,
            Color32 color,
            int width = 1)
        {
            int dx = Mathf.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;
            while (true)
            {
                for (int oy = -width / 2; oy <= width / 2; oy++)
                for (int ox = -width / 2; ox <= width / 2; ox++)
                    SetPixel(pixels, x0 + ox, y0 + oy, color);
                if (x0 == x1 && y0 == y1)
                    break;
                int doubled = 2 * error;
                if (doubled >= dy)
                {
                    error += dy;
                    x0 += sx;
                }
                if (doubled <= dx)
                {
                    error += dx;
                    y0 += sy;
                }
            }
        }

        static void SetPixel(
            Color32[] pixels,
            int x,
            int y,
            Color32 color)
        {
            if (x < 0 || x >= ImageWidth || y < 0 || y >= ImageHeight)
                return;
            pixels[(ImageHeight - 1 - y) * ImageWidth + x] = color;
        }

        static T[] Copy<T>(IReadOnlyList<T> source)
        {
            var result = new T[source.Count];
            for (int index = 0; index < source.Count; index++)
                result[index] = source[index];
            return result;
        }

        static void AppendNumber(StringBuilder builder, float value)
        {
            builder.Append(value.ToString("0.####", CultureInfo.InvariantCulture));
        }

        static string Csv(string value)
        {
            string safe = value ?? string.Empty;
            return "\"" + safe.Replace("\"", "\"\"") + "\"";
        }

        static string Html(string value)
        {
            return (value ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        static void ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Export path is empty.", nameof(path));
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException(
                    "Export path must include a directory.",
                    nameof(path));
            Directory.CreateDirectory(directory);
        }
    }
}
