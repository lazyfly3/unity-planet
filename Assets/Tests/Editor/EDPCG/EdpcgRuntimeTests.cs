#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using ModularAssembly;
using NUnit.Framework;
using UnityEngine;
using UnityPlanet.EDPCG;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.Tests.Editor
{
    public sealed class EdpcgRuntimeTests
    {
        [Test]
        public void DefaultDifficultyUsesAscendingFixedRostersEndingAt64()
        {
            EdpcgDifficultyProfile profile =
                EdpcgDifficultyProfile.LoadOrCreateMemoryDefault();
            int previous = 0;
            int[] expected = { 16, 24, 32, 40, 52, 64 };
            for (int tier = 0; tier < expected.Length; tier++)
            {
                EdpcgTierSettings settings = profile.Resolve(tier);
                Assert.That(settings.rosterCount, Is.EqualTo(expected[tier]));
                Assert.That(settings.rosterCount, Is.GreaterThan(previous));
                Assert.That(
                    settings.interceptorCount + settings.strikerCount +
                    settings.gunshipCount,
                    Is.EqualTo(settings.rosterCount));
                Assert.That(settings.populationCap, Is.LessThanOrEqualTo(28));
                Assert.That(settings.fullSimulationCap, Is.LessThanOrEqualTo(16));
                Assert.That(settings.attackTokenCap, Is.LessThanOrEqualTo(4));
                previous = settings.rosterCount;
            }
        }

        [Test]
        public void HighestTierHasRequired64MemberComposition()
        {
            EdpcgTierSettings settings =
                EdpcgDifficultyProfile.LoadOrCreateMemoryDefault().Resolve(5);
            Assert.That(settings.rosterCount, Is.EqualTo(64));
            Assert.That(settings.interceptorCount, Is.EqualTo(40));
            Assert.That(settings.strikerCount, Is.EqualTo(20));
            Assert.That(settings.gunshipCount, Is.EqualTo(4));
            Assert.That(settings.requiredCreditedKills, Is.EqualTo(20));
        }

        [Test]
        public void PressureBandsStayOrderedAndBelowHardLimit()
        {
            EdpcgDifficultyProfile profile =
                EdpcgDifficultyProfile.LoadOrCreateMemoryDefault();
            for (int tier = 0; tier < 6; tier++)
            {
                EdpcgTierSettings settings = profile.Resolve(tier);
                foreach (EdpcgEncounterPhase phase in
                         System.Enum.GetValues(typeof(EdpcgEncounterPhase)))
                {
                    settings.ResolveTargetBand(phase, out float minimum,
                        out float maximum);
                    Assert.That(minimum, Is.InRange(0f, 1f));
                    Assert.That(maximum, Is.InRange(minimum, 1f));
                    if (phase == EdpcgEncounterPhase.Peak)
                        Assert.That(maximum,
                            Is.LessThanOrEqualTo(settings.hardPressureLimit));
                }
                Assert.That(settings.recoverSeconds, Is.GreaterThanOrEqualTo(8f));
            }
        }

        [Test]
        public void RuntimeBuildsStableUnique64MemberRoster()
        {
            GameObject root = new GameObject("EDPCG_Runtime_Test");
            try
            {
                Rigidbody body = root.AddComponent<Rigidbody>();
                VehicleStructureGraph graph =
                    root.AddComponent<VehicleStructureGraph>();
                HordeCombatDirector director =
                    root.AddComponent<HordeCombatDirector>();
                EdpcgEncounterRuntime runtime =
                    root.AddComponent<EdpcgEncounterRuntime>();
                runtime.Configure(
                    director,
                    body,
                    graph,
                    null,
                    "clearance",
                    5,
                    123456);
                runtime.BeginSession();

                Assert.That(runtime.RosterCount, Is.EqualTo(64));
                var ids = new HashSet<string>();
                int interceptors = 0;
                int strikers = 0;
                int gunships = 0;
                for (int index = 0; index < runtime.Roster.Count; index++)
                {
                    EdpcgRosterMember member = runtime.Roster[index];
                    Assert.That(ids.Add(member.rosterMemberId), Is.True);
                    Assert.That(member.rosterIndex, Is.EqualTo(index));
                    if (member.role == HordeEnemyRole.Interceptor)
                        interceptors++;
                    else if (member.role == HordeEnemyRole.Striker)
                        strikers++;
                    else
                        gunships++;
                }
                Assert.That(interceptors, Is.EqualTo(40));
                Assert.That(strikers, Is.EqualTo(20));
                Assert.That(gunships, Is.EqualTo(4));
                runtime.EndSession();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RosterResolutionIsIdempotentAndSelfDetonationIsNotCredited()
        {
            GameObject root = new GameObject("EDPCG_Resolution_Test");
            try
            {
                EdpcgEncounterRuntime runtime = BuildRuntime(root, 0, 991);
                EdpcgRosterMember member = runtime.Roster[0];
                Assert.That(runtime.ResolveMember(
                    member.rosterMemberId,
                    EdpcgEnemyResolutionReason.SelfDetonated,
                    Vector3.zero,
                    out bool credited), Is.True);
                Assert.That(credited, Is.False);
                Assert.That(runtime.ResolveMember(
                    member.rosterMemberId,
                    EdpcgEnemyResolutionReason.KilledByPlayer,
                    Vector3.zero,
                    out _), Is.False);
                Assert.That(runtime.ResolvedCount, Is.EqualTo(1));
                Assert.That(runtime.CreditedKills, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void LiveTuningRecordsTransactionAndCanRevert()
        {
            GameObject root = new GameObject("EDPCG_Tuning_Test");
            try
            {
                EdpcgEncounterRuntime runtime = BuildRuntime(root, 2, 17);
                float original = runtime.Settings.spawnIntervalSeconds;
                Assert.That(runtime.TryApplyTuning(
                    "spawnIntervalSeconds",
                    "1.75",
                    EdpcgLiveApplyPolicy.ApplyNow,
                    out string changeId,
                    out string error), Is.True, error);
                Assert.That(runtime.Settings.spawnIntervalSeconds,
                    Is.EqualTo(1.75f).Within(0.001f));
                Assert.That(runtime.Recorder.Changes.Count, Is.EqualTo(1));
                Assert.That(runtime.RevertChange(changeId), Is.True);
                Assert.That(runtime.Settings.spawnIntervalSeconds,
                    Is.EqualTo(original).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TelemetryRecorderKeepsBoundedRecentSamples()
        {
            var recorder = new EdpcgTelemetryRecorder(64, 128);
            for (int index = 0; index < 80; index++)
            {
                recorder.RecordSample(new EdpcgPressureSample
                {
                    missionTime = index,
                    actualPressure = index / 100f
                });
            }
            Assert.That(recorder.Samples.Count, Is.EqualTo(64));
            Assert.That(recorder.Samples[0].missionTime, Is.EqualTo(16f));
            Assert.That(recorder.Samples[63].missionTime, Is.EqualTo(79f));
        }

        [Test]
        public void MissionRulesExpose64EnemiesAtHighestNonBossTier()
        {
            FinitePlanetMissionRules clearance =
                FinitePlanetMissionRules.Resolve("clearance", 5);
            FinitePlanetMissionRules assault =
                FinitePlanetMissionRules.Resolve("industrial_outpost", 5);
            Assert.That(clearance.RosterCount, Is.EqualTo(64));
            Assert.That(assault.RosterCount, Is.EqualTo(64));
            Assert.That(clearance.Kind,
                Is.EqualTo(FinitePlanetMissionObjectiveKind.Clearance));
            Assert.That(assault.Kind,
                Is.EqualTo(FinitePlanetMissionObjectiveKind.Assault));
        }

        [Test]
        public void ExporterCreatesChartsDataReportAndLegendIcons()
        {
            GameObject root = new GameObject("EDPCG_Export_Test");
            string parent = Path.Combine(
                Path.GetTempPath(),
                "UnityPlanetEdpcgTests",
                System.Guid.NewGuid().ToString("N"));
            try
            {
                EdpcgEncounterRuntime runtime = BuildRuntime(root, 1, 441);
                runtime.UpdateDirectorSnapshot(3, 2, 1, 1, 0, 0, 3);
                runtime.Tick(0.1f);
                string folder = EdpcgExportService.ExportAll(runtime, parent);
                Assert.That(File.Exists(Path.Combine(folder,
                    "pressure-curve.png")), Is.True);
                Assert.That(File.Exists(Path.Combine(folder,
                    "pressure-curve.svg")), Is.True);
                Assert.That(File.Exists(Path.Combine(folder,
                    "telemetry.csv")), Is.True);
                Assert.That(File.Exists(Path.Combine(folder,
                    "session.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(folder,
                    "report.html")), Is.True);
                Assert.That(File.Exists(Path.Combine(folder,
                    "manifest.json")), Is.True);
                Assert.That(Directory.GetFiles(folder, "icon-*.svg").Length,
                    Is.GreaterThanOrEqualTo(7));
            }
            finally
            {
                Object.DestroyImmediate(root);
                if (Directory.Exists(parent))
                    Directory.Delete(parent, true);
            }
        }

        static EdpcgEncounterRuntime BuildRuntime(
            GameObject root,
            int tier,
            int seed)
        {
            Rigidbody body = root.AddComponent<Rigidbody>();
            VehicleStructureGraph graph =
                root.AddComponent<VehicleStructureGraph>();
            HordeCombatDirector director =
                root.AddComponent<HordeCombatDirector>();
            EdpcgEncounterRuntime runtime =
                root.AddComponent<EdpcgEncounterRuntime>();
            runtime.Configure(
                director,
                body,
                graph,
                null,
                "clearance",
                tier,
                seed);
            runtime.BeginSession();
            return runtime;
        }
    }
}
#endif
