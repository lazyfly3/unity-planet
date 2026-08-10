using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityPlanet.Development;
using UnityPlanet.SpaceStation.Skills;

public sealed class DevelopmentCheatConsoleTests
{
    string previousSlotId;
    string testSlotId;
    GameObject consoleObject;

    [SetUp]
    public void SetUp()
    {
        previousSlotId = GalaxyLaunchContext.SelectedSlotId;
        GalaxySaveSlotMetadata slot = GalaxySaveSlotService.CreateSlot(
            "KG 关卡解锁测试",
            7319);
        testSlotId = slot.slotId;
        GalaxyLaunchContext.SelectSlot(testSlotId);
        consoleObject = new GameObject("DevelopmentCheatConsoleTest");
        consoleObject.AddComponent<DevelopmentCheatConsole>();
    }

    [TearDown]
    public void TearDown()
    {
        PlayerSkillCombatEffects.SetNoCooldownForTesting(false);
        if (consoleObject != null)
            Object.DestroyImmediate(consoleObject);

        if (string.IsNullOrWhiteSpace(previousSlotId))
            GalaxyLaunchContext.Clear();
        else
            GalaxyLaunchContext.SelectSlot(previousSlotId);

        if (!string.IsNullOrWhiteSpace(testSlotId))
            GalaxySaveSlotService.DeleteSlot(testSlotId);
    }

    [Test]
    public void KgCheat_UnlocksAllMissionsWithoutFakingCompletions()
    {
        Assert.That(
            PlanetMissionProgressService.AreAllMissionsUnlocked(),
            Is.False);

        DevelopmentCheatConsole console =
            consoleObject.GetComponent<DevelopmentCheatConsole>();
        MethodInfo execute = typeof(DevelopmentCheatConsole).GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(execute, Is.Not.Null);
        execute.Invoke(console, new object[] { " KG " });

        PlanetMissionProgressData progress =
            PlanetMissionProgressService.LoadOrCreate();
        Assert.That(progress.allMissionsUnlocked, Is.True);
        Assert.That(progress.completedMissions, Is.Empty);
    }
}
