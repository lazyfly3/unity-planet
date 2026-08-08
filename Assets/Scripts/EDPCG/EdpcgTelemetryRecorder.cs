using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.EDPCG
{
    public static class EdpcgRuntimeRegistry
    {
        public static EdpcgEncounterRuntime Active { get; private set; }

        internal static void Register(EdpcgEncounterRuntime runtime)
        {
            if (runtime != null)
                Active = runtime;
        }

        internal static void Unregister(EdpcgEncounterRuntime runtime)
        {
            if (Active == runtime)
                Active = null;
        }
    }

    public sealed class EdpcgTelemetryRecorder
    {
        const int DefaultSampleCapacity = 1800;
        const int DefaultEventCapacity = 4096;
        const int DefaultChangeCapacity = 512;

        readonly List<EdpcgPressureSample> samples;
        readonly List<EdpcgTelemetryEvent> events;
        readonly List<EdpcgRuntimeChange> changes;
        int eventSequence;

        public IReadOnlyList<EdpcgPressureSample> Samples => samples;
        public IReadOnlyList<EdpcgTelemetryEvent> Events => events;
        public IReadOnlyList<EdpcgRuntimeChange> Changes => changes;
        public int SampleCapacity { get; }
        public int EventCapacity { get; }

        public EdpcgTelemetryRecorder(
            int sampleCapacity = DefaultSampleCapacity,
            int eventCapacity = DefaultEventCapacity)
        {
            SampleCapacity = Mathf.Max(64, sampleCapacity);
            EventCapacity = Mathf.Max(128, eventCapacity);
            samples = new List<EdpcgPressureSample>(SampleCapacity);
            events = new List<EdpcgTelemetryEvent>(EventCapacity);
            changes = new List<EdpcgRuntimeChange>(DefaultChangeCapacity);
        }

        public void Clear()
        {
            samples.Clear();
            events.Clear();
            changes.Clear();
            eventSequence = 0;
        }

        public void RecordSample(EdpcgPressureSample sample)
        {
            if (sample == null)
                return;
            if (samples.Count >= SampleCapacity)
                samples.RemoveAt(0);
            samples.Add(sample.Copy());
        }

        public EdpcgTelemetryEvent RecordEvent(
            float missionTime,
            EdpcgTelemetryEventKind kind,
            string reasonCode = "",
            string rosterMemberId = "",
            Vector3 worldPosition = default(Vector3),
            float pressureDelta = 0f,
            string areaId = "",
            string routeId = "",
            string squadId = "")
        {
            if (events.Count >= EventCapacity)
                events.RemoveAt(0);
            var item = new EdpcgTelemetryEvent
            {
                missionTime = Mathf.Max(0f, missionTime),
                kind = kind,
                stableEventId = "event-" + (++eventSequence).ToString("D6"),
                rosterMemberId = rosterMemberId ?? string.Empty,
                squadId = squadId ?? string.Empty,
                areaId = areaId ?? string.Empty,
                routeId = routeId ?? string.Empty,
                reasonCode = reasonCode ?? string.Empty,
                pressureDelta = pressureDelta,
                worldPosition = worldPosition
            };
            events.Add(item);
            return item;
        }

        public void RecordChange(EdpcgRuntimeChange change)
        {
            if (change == null)
                return;
            if (changes.Count >= DefaultChangeCapacity)
                changes.RemoveAt(0);
            changes.Add(change);
        }

        public EdpcgRuntimeChange FindChange(string changeId)
        {
            if (string.IsNullOrEmpty(changeId))
                return null;
            for (int index = changes.Count - 1; index >= 0; index--)
            {
                if (string.Equals(
                        changes[index].changeId,
                        changeId,
                        StringComparison.Ordinal))
                {
                    return changes[index];
                }
            }
            return null;
        }

        public void CopyRange(
            float startTime,
            float endTime,
            List<EdpcgPressureSample> sampleOutput,
            List<EdpcgTelemetryEvent> eventOutput)
        {
            if (sampleOutput == null || eventOutput == null)
                throw new ArgumentNullException(
                    sampleOutput == null
                        ? nameof(sampleOutput)
                        : nameof(eventOutput));
            sampleOutput.Clear();
            eventOutput.Clear();
            float minimum = Mathf.Min(startTime, endTime);
            float maximum = Mathf.Max(startTime, endTime);
            for (int index = 0; index < samples.Count; index++)
            {
                EdpcgPressureSample sample = samples[index];
                if (sample.missionTime >= minimum &&
                    sample.missionTime <= maximum)
                {
                    sampleOutput.Add(sample.Copy());
                }
            }
            for (int index = 0; index < events.Count; index++)
            {
                EdpcgTelemetryEvent item = events[index];
                if (item.missionTime >= minimum &&
                    item.missionTime <= maximum)
                {
                    eventOutput.Add(item);
                }
            }
        }
    }
}
