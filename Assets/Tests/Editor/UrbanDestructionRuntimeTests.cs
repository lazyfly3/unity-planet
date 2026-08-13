using NUnit.Framework;
using System;
using System.Linq;
using UnityEngine;
using UnityPlanet.CityPcg;
using UnityPlanet.ModularAssembly;
using UnityPlanet.SpaceStation.Skills;
using Object = UnityEngine.Object;

public sealed class UrbanDestructionRuntimeTests
{
    sealed class TestModuleDamageAuthority : IVehicleModuleDamageAuthority
    {
        public float DamageReceived { get; private set; }
        public float Integrity(string runtimeId) => 400f - DamageReceived;
        public float MaximumIntegrity(string runtimeId) => 400f;
        public bool IsDestroyed(string runtimeId) => DamageReceived >= 400f;
        public void ApplyDamage(string runtimeId, SpaceDamageInfo damage)
        {
            DamageReceived += damage.amount;
        }
    }

    GameObject root;
    UrbanDestructionCoordinator coordinator;
    UrbanDestructibleBuilding building;
    BoxCollider buildingCollider;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("UrbanDestruction_TargetedTest");
        coordinator = root.AddComponent<UrbanDestructionCoordinator>();
        coordinator.Configure(new UrbanDestructionSettings
        {
            maximumPhysicalDebris = 8,
            maximumVisualDebris = 24,
            debrisLifetime = 4f,
            physicalCollisionSeconds = 0.6f
        });

        GameObject buildingObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        buildingObject.name = "Building_TargetedTest";
        buildingObject.transform.SetParent(root.transform, false);
        buildingObject.transform.localPosition = new Vector3(0f, 60f, 0f);
        buildingObject.transform.localScale = new Vector3(42f, 120f, 38f);
        buildingCollider = buildingObject.GetComponent<BoxCollider>();
        building = buildingObject.AddComponent<UrbanDestructibleBuilding>();
        building.Configure(new AirCombatBuildingLot
        {
            stableId = "test-building-001",
            center = new Vector3(0f, 60f, 0f),
            size = new Vector3(42f, 120f, 38f),
            yaw = 0f,
            band = AirCombatBuildingBand.High
        }, coordinator);
    }

    [TearDown]
    public void TearDown()
    {
        if (root != null)
            Object.DestroyImmediate(root);
    }

    [Test]
    public void OrdinaryProjectileCannotDamageUrbanStructures()
    {
        float before = building.Integrity01;
        bool applied = UrbanDestructionWorld.TryApplyDirect(
            buildingCollider,
            new Vector3(0f, 65f, 19f),
            Vector3.forward,
            12f,
            null);

        Assert.That(applied, Is.False);
        Assert.That(building.IsCollapsed, Is.False);
        Assert.That(building.Integrity01, Is.EqualTo(before),
            "普通攻击只能被建筑阻挡并播放命中特效，不能改变建筑结构。\n");
    }

    [Test]
    public void OrdinaryExplosiveProjectileCannotDamageUrbanStructures()
    {
        float before = building.Integrity01;

        WeaponDamageUtility.ApplyExplosion(
            building.DestructionBounds.center,
            32f,
            500f,
            null,
            null);

        Assert.That(building.IsCollapsed, Is.False);
        Assert.That(building.Integrity01, Is.EqualTo(before),
            "普通武器的爆炸半径不能绕过城市结构破坏限制。\n");
    }

    [Test]
    public void CrescentBladeArmsUntilTheNextPrimaryFireConsumption()
    {
        var owner = new GameObject("CrescentSkill_InputContract");
        try
        {
            PlayerSkillCombatEffects.ArmCrescentBlade(
                owner.transform,
                30f,
                205f,
                193f,
                565f);
            Assert.That(PlayerSkillCombatEffects.IsDestructionRoundArmed(
                owner.transform), Is.True);
            Assert.That(PlayerSkillCombatEffects.TryConsumeCrescentBlade(
                owner,
                out float width,
                out float damage,
                out float speed,
                out float range), Is.True);
            Assert.That(width, Is.EqualTo(30f));
            Assert.That(damage, Is.EqualTo(205f));
            Assert.That(speed, Is.EqualTo(193f));
            Assert.That(range, Is.EqualTo(565f));
            Assert.That(PlayerSkillCombatEffects.IsDestructionRoundArmed(
                owner.transform), Is.False);
        }
        finally
        {
            PlayerSkillCombatEffects.CancelDestructionRound(owner.transform);
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void MiddleShellingCreatesLocalBreachesBeforeStructuralCollapse()
    {
        Vector3 point = new Vector3(0f, 60f, 19f);
        ApplyDemolitionHits(
            buildingCollider,
            point,
            Vector3.forward,
            -Vector3.forward,
            4);

        Assert.That(building.IsCollapsed, Is.False,
            "普通火炮只能逐步打出局部缺口，不能把整栋高楼当作一个刚体推倒。");
        Assert.That(building.BreachCount, Is.EqualTo(4));
        Assert.That(building.BrokenStructuralCells, Is.GreaterThan(0));
        Assert.That(building.ActiveStructuralCells,
            Is.LessThan(building.TotalStructuralCells));
        Assert.That(building.HasLocalizedDamageVisual, Is.True);
        Assert.That(building.HasDamageCavityInterior, Is.True,
            "空心美术楼体被裁开后必须显示来自该建筑自身的内缩结构层。");
        Assert.That(building.DamageCavityPieceCount, Is.GreaterThan(0));
        Renderer[] cavityRenderers = root.GetComponentsInChildren<Renderer>(true)
            .Where(renderer =>
                renderer.name.StartsWith("DamageCavityOriginalShell_"))
            .ToArray();
        Assert.That(cavityRenderers, Is.Not.Empty);
        Assert.That(root.GetComponentsInChildren<Renderer>(true).Any(renderer =>
                renderer.name.StartsWith("CavityLiner") ||
                renderer.name.StartsWith("CavityOcclusionBacking")),
            Is.False,
            "不得再用统一黄棕色柱体、楼板盒或后壁盒填补不同建筑。\n");
        Bounds innerBounds = cavityRenderers[0].bounds;
        for (int index = 1; index < cavityRenderers.Length; index++)
            innerBounds.Encapsulate(cavityRenderers[index].bounds);
        Bounds originalBounds = building.DestructionBounds;
        Assert.That(innerBounds.min.x, Is.GreaterThan(originalBounds.min.x));
        Assert.That(innerBounds.max.x, Is.LessThan(originalBounds.max.x));
        Assert.That(innerBounds.min.z, Is.GreaterThan(originalBounds.min.z));
        Assert.That(innerBounds.max.z, Is.LessThan(originalBounds.max.z));
        Assert.That(innerBounds.size.x, Is.LessThan(originalBounds.size.x));
        Assert.That(innerBounds.size.z, Is.LessThan(originalBounds.size.z),
            "连续破坏后，任何可见内层都不得让建筑包围盒变粗。\n");
        Assert.That(root.GetComponentInChildren<UrbanTopplingSection>(true),
            Is.Null,
            "局部炮击不得创建整楼倾倒控制器。");
    }

    [Test]
    public void SupportLossDetachesOnlyUnsupportedConnectedIslands()
    {
        Vector3 lowerFacade = new Vector3(0f, 18f, 19f);
        for (int x = -1; x <= 1; x++)
        {
            ApplyDemolitionHits(
                buildingCollider,
                lowerFacade + Vector3.right * x * 13f,
                Vector3.forward,
                Vector3.back,
                3);
        }

        Assert.That(building.IsCollapsed, Is.False);
        Assert.That(building.BrokenStructuralCells, Is.GreaterThan(0));
        Assert.That(building.ActiveStructuralCells,
            Is.GreaterThan(0),
            "失去局部支撑只能剥落失稳区域，不能删除整栋楼。");
        Assert.That(building.DetachedClusterCount, Is.GreaterThan(0));
        Assert.That(
            coordinator.ActivePhysicalDebris,
            Is.LessThanOrEqualTo(coordinator.MaximumPhysicalDebris));
        Assert.That(
            root.GetComponentsInChildren<MeshCollider>(true),
            Is.Empty,
            "运行时破坏不允许生成或重烹饪 MeshCollider。");
        Assert.That(root.GetComponentInChildren<UrbanTopplingSection>(true),
            Is.Null);
        Assert.That(building.ActiveCollisionProxyCount, Is.GreaterThan(0));
        UrbanDestructibleRuinSection detached = root
            .GetComponentsInChildren<UrbanDestructibleRuinSection>(true)
            .First(section => section.name.Contains("UnsupportedStructuralCluster"));
        BoxCollider[] detachedColliders = detached.GetComponents<BoxCollider>();
        Assert.That(detachedColliders.Length, Is.GreaterThanOrEqualTo(1));
        Assert.That(detachedColliders.Max(item => item.bounds.size.x),
            Is.LessThan(building.DesignSize.x * 0.55f),
            "单列失稳区允许使用一个紧贴轮廓的盒；不得退化成包住整栋楼空气的大 AABB。");
        Assert.That(detachedColliders.Max(item => item.bounds.size.z),
            Is.LessThan(building.DesignSize.z * 0.55f));
        Assert.That(detached.GetComponentsInChildren<Renderer>(true)
                .Any(renderer => renderer.name.StartsWith("DetachedOriginalFacade_")),
            Is.True,
            "掉落结构必须继承原建筑外壳，而不是显示纯色占位方块。");
        Assert.That(detached.GetComponentsInChildren<Renderer>(true)
                .Any(renderer => renderer.name.StartsWith("ConnectedCell_")),
            Is.False);
        float beforeFall = detached.DestructionBounds.center.y;
        coordinator.DebugAdvanceForAudit(0.7f);
        Physics.SyncTransforms();
        Assert.That(detached.DestructionBounds.center.y,
            Is.LessThan(beforeFall - 2f),
            "失稳区域必须有明确下坠，不得悬停在原楼位置。");
    }

    [Test]
    public void LowAndMiddleHitsRemainLocalAndDoNotShareOneCollapseTemplate()
    {
        UrbanDestructibleBuilding middleBuilding = CreateAdditionalBuilding(
            "Building_MiddleHit",
            "test-building-middle-hit",
            new Vector3(200f, 60f, 0f));
        Collider middleCollider = middleBuilding.GetComponent<Collider>();
        Vector3 lowPoint = new Vector3(0f, 24f, 19f);
        Vector3 middlePoint = new Vector3(200f, 68f, 19f);

        ApplyDemolitionHits(
            buildingCollider,
            lowPoint,
            Vector3.forward,
            Vector3.back,
            3);
        ApplyDemolitionHits(
            middleCollider,
            middlePoint,
            Vector3.forward,
            Vector3.left,
            4);

        Assert.That(building.IsCollapsed, Is.False);
        Assert.That(middleBuilding.IsCollapsed, Is.False);
        Assert.That(building.BrokenStructuralCells, Is.GreaterThan(0));
        Assert.That(middleBuilding.BrokenStructuralCells, Is.GreaterThan(0));
        Assert.That(root.GetComponentsInChildren<UrbanTopplingSection>(true),
            Is.Empty,
            "命中高度不同应改变局部损伤位置，而不是套用同一个整楼倾倒动画。");
    }

    [Test]
    public void CrescentEnergyBladeCutsAtHitHeightWhileOrdinaryShellingDoesNot()
    {
        Renderer sourceRenderer = building.GetComponent<Renderer>();
        Material originalMaterial = sourceRenderer.sharedMaterial;
        int globalProperty = Shader.PropertyToID(
            "_UrbanSliceGlobalPropertyRegression");
        int slotProperty = Shader.PropertyToID(
            "_UrbanSliceSlotPropertyRegression");
        var sourceProperties = new MaterialPropertyBlock();
        sourceProperties.SetFloat(globalProperty, 0.375f);
        sourceRenderer.SetPropertyBlock(sourceProperties);
        sourceProperties.Clear();
        sourceProperties.SetFloat(slotProperty, 0.625f);
        sourceRenderer.SetPropertyBlock(sourceProperties, 0);
        sourceRenderer.lightmapIndex = 2;
        sourceRenderer.realtimeLightmapIndex = 1;
        Vector3 cutPoint = new Vector3(0f, 72f, 19f);
        bool applied = UrbanDestructionWorld.TryApplyEnergyBlade(
            buildingCollider,
            cutPoint,
            Vector3.forward,
            Vector3.back,
            180f,
            30f,
            null);

        Assert.That(applied, Is.True);
        Assert.That(building.IsCollapsed, Is.True);
        Assert.That(building.LastCollapseWasEnergyBlade, Is.True);
        Assert.That(building.LastCollapseCutHeight,
            Is.EqualTo(cutPoint.y).Within(1f));
        UrbanTopplingSection toppling =
            root.GetComponentInChildren<UrbanTopplingSection>(true);
        Assert.That(toppling, Is.Not.Null);
        Assert.That(toppling.CutHeight, Is.EqualTo(cutPoint.y).Within(1f));
        Assert.That(root.GetComponentsInChildren<Renderer>(true).Count(renderer =>
                renderer.name.StartsWith("LowerCutVisual_")),
            Is.GreaterThan(0));
        Assert.That(root.GetComponentsInChildren<Renderer>(true).Count(renderer =>
                renderer.name.StartsWith("UpperCutVisual_")),
            Is.GreaterThan(0));
        Renderer[] cutRenderers = root.GetComponentsInChildren<Renderer>(true)
            .Where(renderer =>
                renderer.name.StartsWith("LowerCutVisual_") ||
                renderer.name.StartsWith("UpperCutVisual_"))
            .ToArray();
        Assert.That(cutRenderers.Any(renderer =>
                renderer.sharedMaterials.Contains(originalMaterial)),
            Is.True,
            "切割后的外立面必须继续引用原建筑材质，不能整栋换成替代 Shader。");
        Assert.That(cutRenderers.All(renderer =>
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh != null &&
                   renderer.sharedMaterials.Length ==
                   filter.sharedMesh.subMeshCount &&
                   renderer.sharedMaterials.All(material => material != null);
        }), Is.True,
            "切割后的每个可见子网格都必须有有效材质，不能因材质槽和子网格数量错位而变成空白面。");
        foreach (Renderer cutRenderer in cutRenderers)
        {
            Assert.That(cutRenderer.lightmapIndex, Is.EqualTo(-1),
                "会移动的运行时切片不能继续采样原静态楼房的烘焙 Lightmap。");
            Assert.That(cutRenderer.realtimeLightmapIndex, Is.EqualTo(-1));
            var copiedProperties = new MaterialPropertyBlock();
            cutRenderer.GetPropertyBlock(copiedProperties);
            Assert.That(copiedProperties.GetFloat(globalProperty),
                Is.EqualTo(0.375f).Within(0.0001f),
                "切割替换 Renderer 时必须保留全局 MaterialPropertyBlock。");
            copiedProperties.Clear();
            cutRenderer.GetPropertyBlock(copiedProperties, 0);
            Assert.That(copiedProperties.GetFloat(slotProperty),
                Is.EqualTo(0.625f).Within(0.0001f),
                "Dark City 外立面的逐材质属性块必须传递到切割网格。");
        }
        Assert.That(cutRenderers.SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null)
                .Any(material => material.name.StartsWith("UrbanCutFacade_")),
            Is.False,
            "真正的 Mesh 切割不应再生成 UrbanCutFacade 替代材质。");
        Assert.That(cutRenderers.Select(renderer =>
                renderer.GetComponent<MeshFilter>())
                .Where(filter => filter != null && filter.sharedMesh != null)
                .Any(filter => filter.sharedMesh.name.Contains("_Slice")),
            Is.True,
            "至少一个跨越切面的原始 Mesh 必须产生真实裁切后的新 Mesh。");
    }

    [Test]
    public void CrescentBladeRecursivelyReplacesTheHitSectionWithTwoHalves()
    {
        int originalFamily =
            UrbanDestructionWorld.ResolveEnergyBladeFamilyId(buildingCollider);
        var firstProjectilePass = new UrbanEnergyBladePass();
        Assert.That(firstProjectilePass.TryClaim(buildingCollider), Is.True);
        Vector3 firstCut = new Vector3(0f, 76f, 19f);
        Assert.That(UrbanDestructionWorld.TryApplyEnergyBlade(
            buildingCollider,
            firstCut,
            Vector3.forward,
            Vector3.back,
            180f,
            30f,
            null), Is.True, "first building cut was not accepted");

        UrbanDestructibleRuinSection lower = root
            .GetComponentsInChildren<UrbanDestructibleRuinSection>(true)
            .First(section => section.name.Contains("LowerRuin"));
        Collider firstCollider = lower.PersistentCollider;
        Assert.That(firstCollider, Is.Not.Null);
        Assert.That(
            UrbanDestructionWorld.ResolveEnergyBladeFamilyId(firstCollider),
            Is.EqualTo(originalFamily),
            "原建筑与它生成的残骸必须属于同一个光刃命中族。");
        Assert.That(firstProjectilePass.TryClaim(firstCollider), Is.False,
            "同一发月牙不得立刻把第一刀刚生成的半段再次切开。");
        var secondProjectilePass = new UrbanEnergyBladePass();
        Assert.That(secondProjectilePass.TryClaim(firstCollider), Is.True,
            "下一发月牙必须能够切中上一刀生成的半段。");
        Assert.That(UrbanDestructionWorld.TryApplyEnergyBlade(
            firstCollider,
            firstCollider.bounds.center,
            Vector3.forward,
            Vector3.back,
            180f,
            30f,
            null), Is.True, "first recursive ruin cut was not accepted");

        Assert.That(lower.IsUrbanDestroyed, Is.True,
            "被二次切中的旧段必须退出结构树，不能仍和新碎块重叠。");
        Assert.That(firstCollider.enabled, Is.False);
        UrbanDestructibleRuinSection[] generationOne = lower
            .GetComponentsInChildren<UrbanDestructibleRuinSection>(true)
            .Where(section => section != lower &&
                              !section.IsUrbanDestroyed &&
                              section.FractureGeneration == 1 &&
                              section.name.Contains("Recursive"))
            .ToArray();
        Assert.That(generationOne.Length, Is.EqualTo(2),
            "一次光刃切割必须把命中段替换为且仅替换为两个新段。");
        Assert.That(generationOne.All(section =>
                section.PersistentCollider != null &&
                section.PersistentCollider.enabled &&
                section.GetComponentsInChildren<UrbanSliceVisual>(true)
                    .Any(visual => visual.GetComponent<Renderer>() != null &&
                                   visual.GetComponent<Renderer>().enabled)),
            Is.True, "a first-generation half is missing its collider or sliced visual");
        Assert.That(generationOne.All(section =>
                UrbanDestructionWorld.ResolveEnergyBladeFamilyId(
                    section.PersistentCollider) == originalFamily),
            Is.True,
            "递归生成的新半段必须继承原建筑族，防止同一发月牙连续吞掉多代切割。");
        Assert.That(secondProjectilePass.TryClaim(
                generationOne[0].PersistentCollider),
            Is.False,
            "第二发月牙完成一次二分后不能在自身飞行期间继续切下一代。");
        Assert.That(generationOne.Count(section =>
                section.GetComponent<Rigidbody>() != null),
            Is.EqualTo(1),
            "二分后的上半段必须拥有独立刚体，不能继续和旧父段表现为一整块。");
        Assert.That(lower.GetComponentsInChildren<Transform>(true)
                .Any(item => item.name.StartsWith("RecursiveFracture_")),
            Is.False,
            "光刃递归切割不能再用通用立方体碎块冒充二分。");

        UrbanDestructibleRuinSection target = generationOne
            .OrderByDescending(section => section.DestructionBounds.size.y)
            .First();
        Collider secondCollider = target.PersistentCollider;
        Assert.That(new UrbanEnergyBladePass().TryClaim(secondCollider), Is.True,
            "第三发月牙必须能够继续切第二发生成的目标半段。");
        Assert.That(UrbanDestructionWorld.TryApplyEnergyBlade(
            secondCollider,
            secondCollider.bounds.center,
            Vector3.forward,
            Vector3.back,
            180f,
            30f,
            null), Is.True, "second recursive ruin cut was not accepted");
        Assert.That(target.IsUrbanDestroyed, Is.True,
            "second-generation replacement was not created for the selected half");
        Assert.That(target.GetComponentsInChildren<UrbanDestructibleRuinSection>(true)
                .Count(section => section != target &&
                                  !section.IsUrbanDestroyed &&
                                  section.FractureGeneration == 2 &&
                                  section.name.Contains("Recursive")),
            Is.EqualTo(2),
            "第三刀仍应继续二分当前段，而不是只增加装饰碎块。");
    }

    [Test]
    public void FallenTowerKeepsColliderAndCanBeDestroyedAgain()
    {
        Vector3 point = new Vector3(0f, 60f, 19f);
        Assert.That(UrbanDestructionWorld.TryApplyEnergyBlade(
            buildingCollider,
            point,
            Vector3.forward,
            Vector3.back,
            180f,
            30f,
            null), Is.True);
        coordinator.DebugAdvanceForAudit(8f);
        Physics.SyncTransforms();

        UrbanDestructibleRuinSection fallen = root
            .GetComponentsInChildren<UrbanDestructibleRuinSection>(true)
            .First(section => section.name.Contains("FallenTower"));
        Collider fallenCollider = fallen.PersistentCollider;
        Assert.That(fallenCollider, Is.Not.Null);
        Assert.That(fallenCollider.enabled, Is.True,
            "倒塌完成后必须继续保留低成本碰撞体。");

        Vector3 secondaryPoint = fallenCollider.bounds.center +
                                 Vector3.right * fallenCollider.bounds.extents.x;
        for (int index = 0; index < 2; index++)
        {
            Assert.That(UrbanDestructionWorld.TryApplyDemolition(
                fallenCollider,
                secondaryPoint + Vector3.up * index * 3f,
                Vector3.right,
                Vector3.left,
                150f,
                20f,
                null), Is.True);
        }

        Assert.That(fallen.FractureStage, Is.EqualTo(1));
        Assert.That(fallenCollider.enabled, Is.True,
            "二次破坏产生碎裂状态，但不能删除倒地楼体碰撞。");
        Assert.That(root.GetComponentsInChildren<UrbanDestructibleRuinSection>(true)
                .Count(section => section.FractureGeneration == 1 &&
                                  section.PersistentCollider != null &&
                                  section.PersistentCollider.enabled),
            Is.GreaterThanOrEqualTo(3));
        UrbanDestructibleRuinSection child = root
            .GetComponentsInChildren<UrbanDestructibleRuinSection>(true)
            .First(section => section.FractureGeneration == 1);
        Assert.That(child.PersistentCollider, Is.Not.Null);
        Assert.That(child.PersistentCollider.enabled, Is.True);
        for (int index = 0; index < 2; index++)
        {
            Assert.That(UrbanDestructionWorld.TryApplyDemolition(
                child.PersistentCollider,
                child.DestructionBounds.center,
                Vector3.forward,
                Vector3.back,
                150f,
                20f,
                null), Is.True);
        }
        Assert.That(root.GetComponentsInChildren<UrbanDestructibleRuinSection>(true)
                .Count(section => section.FractureGeneration == 2),
            Is.GreaterThanOrEqualTo(2),
            "大碎块受击后必须继续产生可命中的中型碎块。");
        UrbanDestructibleRuinSection generationTwo = root
            .GetComponentsInChildren<UrbanDestructibleRuinSection>(true)
            .First(section => section.FractureGeneration == 2);
        for (int index = 0; index < 2; index++)
        {
            Assert.That(UrbanDestructionWorld.TryApplyDemolition(
                generationTwo.PersistentCollider,
                generationTwo.DestructionBounds.center,
                Vector3.up,
                Vector3.down,
                150f,
                20f,
                null), Is.True);
        }
        Assert.That(root.GetComponentsInChildren<UrbanDestructibleRuinSection>(true)
                .Count(section => section.FractureGeneration == 3),
            Is.GreaterThanOrEqualTo(2),
            "断裂不能在第二代硬停止；尺寸足够的碎块必须继续生成后代。");
        for (int index = 0; index < 6; index++)
        {
            Assert.That(UrbanDestructionWorld.TryApplyDemolition(
                fallenCollider,
                fallen.DestructionBounds.center,
                Vector3.right,
                Vector3.left,
                150f,
                20f,
                null), Is.True,
                "已经倒塌并切过的主体仍必须继续接受后续切割。");
        }
        int stageBeforeBlade = fallen.FractureStage;
        Assert.That(UrbanDestructionWorld.TryApplyEnergyBlade(
            fallenCollider,
            fallen.DestructionBounds.center,
            Vector3.up,
            Vector3.forward,
            1f,
            30f,
            null), Is.True);
        Assert.That(fallen.FractureStage, Is.EqualTo(stageBeforeBlade + 1),
            "月牙光刃每次穿过倒塌结构都必须形成一次新的切割。 ");
        Assert.That(fallen.IsUrbanDestroyed, Is.True,
            "光刃切割后旧倒塌段应由两个真实 Mesh 子段替换。");
        Assert.That(fallenCollider.enabled, Is.False);
        Assert.That(fallen.GetComponentsInChildren<UrbanDestructibleRuinSection>(true)
                .Count(section => section != fallen &&
                                  !section.IsUrbanDestroyed &&
                                  section.name.Contains("Recursive")),
            Is.GreaterThanOrEqualTo(2));
        Assert.That(root.GetComponentsInChildren<MeshCollider>(true), Is.Empty);
    }

    [Test]
    public void SlicingTopplingSectionRefreshesItsCompoundCollisionOwnership()
    {
        Assert.That(UrbanDestructionWorld.TryApplyEnergyBlade(
            buildingCollider,
            new Vector3(0f, 42f, 19f),
            Vector3.forward,
            Vector3.back,
            1000f,
            30f,
            null), Is.True);
        UrbanTopplingSection toppling =
            root.GetComponentInChildren<UrbanTopplingSection>(true);
        UrbanDestructibleRuinSection fallen = toppling
            .GetComponentsInChildren<UrbanDestructibleRuinSection>(true)
            .First(section => section.name.Contains("FallenTower"));
        Collider hit = fallen.PersistentCollider;
        int before = toppling.CompoundColliderCount;

        Assert.That(UrbanDestructionWorld.TryApplyEnergyBlade(
            hit,
            fallen.DestructionBounds.center,
            Vector3.up,
            Vector3.forward,
            1f,
            30f,
            null), Is.True);

        Assert.That(toppling.CompoundColliderCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(toppling.GetComponentsInChildren<BoxCollider>(true)
                .Count(item => item.enabled &&
                               item.attachedRigidbody == toppling.GetComponent<Rigidbody>()),
            Is.EqualTo(toppling.CompoundColliderCount));
        Assert.That(toppling.CompoundColliderCount, Is.Not.EqualTo(before),
            "The state machine must replace retired colliders with the sliced halves.");
    }

    [Test]
    public void DecorationBreaksFromExplosionWithoutAddingFlightCollider()
    {
        var decorationObject = new GameObject("RoofBillboard_Test");
        decorationObject.transform.SetParent(root.transform, false);
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.transform.SetParent(decorationObject.transform, false);
        visual.transform.localScale = new Vector3(8f, 4f, 0.4f);
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        UrbanDestructibleDecoration decoration =
            decorationObject.AddComponent<UrbanDestructibleDecoration>();
        decoration.Configure(coordinator);

        Assert.That(
            decorationObject.GetComponentsInChildren<Collider>(true),
            Is.Empty);
        int affected = UrbanDestructionWorld.ApplyExplosion(
            decoration.DestructionBounds.center,
            12f,
            80f,
            null);

        Assert.That(affected, Is.GreaterThanOrEqualTo(1));
        Assert.That(decoration.IsUrbanDestroyed, Is.True);
    }

    [Test]
    public void HighSpeedImpactHasThresholdAndCappedStructuralDamage()
    {
        float slow = coordinator.EvaluateImpactDamage(24f, 800f, 400f);
        float fast = coordinator.EvaluateImpactDamage(72f, 2600f, 400f);

        Assert.That(slow, Is.EqualTo(0f));
        Assert.That(fast, Is.GreaterThan(0f));
        Assert.That(fast, Is.LessThanOrEqualTo(400f * 0.42f));
    }

    [Test]
    public void FallingBuildingImpactDamagesOnlyAtMeaningfulSpeed()
    {
        const float maximumModuleIntegrity = 400f;
        float scrape = UrbanVehicleImpactPolicy.ResolveModuleDamage(
            maximumModuleIntegrity,
            6f,
            250f);
        float fallingHit = UrbanVehicleImpactPolicy.ResolveModuleDamage(
            maximumModuleIntegrity,
            28f,
            4200f);
        float severeHit = UrbanVehicleImpactPolicy.ResolveModuleDamage(
            maximumModuleIntegrity,
            54f,
            10000f);

        Assert.That(scrape, Is.Zero);
        Assert.That(fallingHit, Is.GreaterThan(0f));
        Assert.That(severeHit, Is.GreaterThan(fallingHit));
        Assert.That(severeHit, Is.LessThan(maximumModuleIntegrity),
            "A single falling section damages the contacted module without " +
            "silently deleting a fresh core.");
    }

    [Test]
    public void FallingBuildingImpactRoutesToTheContactedModuleReceiver()
    {
        GameObject module = new GameObject("FallingImpact_Module");
        try
        {
            VehicleModuleDamageReceiver receiver =
                module.AddComponent<VehicleModuleDamageReceiver>();
            var authority = new TestModuleDamageAuthority();
            receiver.Initialize(authority, "hit-module");

            bool slowAccepted = receiver.ApplyUrbanFallingImpact(
                new UrbanVehicleImpactData(
                    Vector3.zero,
                    Vector3.down,
                    5f,
                    100f,
                    root));
            Assert.That(slowAccepted, Is.False);
            Assert.That(authority.DamageReceived, Is.Zero);

            bool fallingAccepted = receiver.ApplyUrbanFallingImpact(
                new UrbanVehicleImpactData(
                    Vector3.one,
                    Vector3.down,
                    30f,
                    4500f,
                    root));
            Assert.That(fallingAccepted, Is.True);
            Assert.That(authority.DamageReceived, Is.GreaterThan(0f));
            Assert.That(authority.DamageReceived, Is.LessThan(400f));
        }
        finally
        {
            Object.DestroyImmediate(module);
        }
    }

    [Test]
    public void BossBuildingRamReusesTheEnergyBladeCollapsePipeline()
    {
        GameObject boss = new GameObject("BossRam_Source");
        try
        {
            Vector3 point = new Vector3(0f, 42f, 19f);
            bool applied = UrbanDestructionWorld.TryApplyEnergyBlade(
                buildingCollider,
                point,
                Vector3.forward,
                Vector3.back,
                1000f,
                30f,
                boss);

            Assert.That(applied, Is.True);
            Assert.That(building.IsCollapsed, Is.True);
            Assert.That(building.LastCollapseWasEnergyBlade, Is.True);
            Assert.That(root.GetComponentInChildren<UrbanTopplingSection>(true),
                Is.Not.Null);
        }
        finally
        {
            Object.DestroyImmediate(boss);
        }
    }

    [Test]
    public void BossTopplingSectionUsesTightCompoundColliders()
    {
        Vector3 point = new Vector3(0f, 42f, 19f);
        Assert.That(UrbanDestructionWorld.TryApplyEnergyBlade(
            buildingCollider,
            point,
            Vector3.forward,
            Vector3.back,
            1000f,
            30f,
            null), Is.True);

        UrbanTopplingSection toppling =
            root.GetComponentInChildren<UrbanTopplingSection>(true);
        Assert.That(toppling, Is.Not.Null);
        BoxCollider[] colliders = toppling.GetComponents<BoxCollider>();
        Assert.That(toppling.CompoundColliderCount, Is.GreaterThanOrEqualTo(3));
        Assert.That(colliders, Has.Length.EqualTo(toppling.CompoundColliderCount));

        Renderer[] upperRenderers = toppling
            .GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.name.StartsWith("UpperCutVisual_"))
            .ToArray();
        Assert.That(upperRenderers, Is.Not.Empty);
        Bounds visible = upperRenderers[0].bounds;
        for (int index = 1; index < upperRenderers.Length; index++)
            visible.Encapsulate(upperRenderers[index].bounds);
        Bounds collision = colliders[0].bounds;
        for (int index = 1; index < colliders.Length; index++)
            collision.Encapsulate(colliders[index].bounds);

        Assert.That(collision.min.x, Is.LessThanOrEqualTo(visible.min.x + 0.2f));
        Assert.That(collision.max.x, Is.GreaterThanOrEqualTo(visible.max.x - 0.2f));
        Assert.That(collision.min.z, Is.LessThanOrEqualTo(visible.min.z + 0.2f));
        Assert.That(collision.max.z, Is.GreaterThanOrEqualTo(visible.max.z - 0.2f));
        Assert.That(collision.size.x, Is.LessThanOrEqualTo(visible.size.x + 0.5f));
        Assert.That(collision.size.z, Is.LessThanOrEqualTo(visible.size.z + 0.5f),
            "倾倒楼体的碰撞体必须贴合可见外壳，不能继续使用缩小或包住大片空气的单盒代理。");
    }

    [Test]
    public void UnsupportedTopplingSectionHandsOffToGravityAfterControlledArc()
    {
        Assert.That(UrbanDestructionWorld.TryApplyEnergyBlade(
            buildingCollider,
            new Vector3(0f, 42f, 19f),
            Vector3.forward,
            Vector3.back,
            1000f,
            30f,
            null), Is.True);
        UrbanTopplingSection toppling =
            root.GetComponentInChildren<UrbanTopplingSection>(true);
        Rigidbody body = toppling.GetComponent<Rigidbody>();

        toppling.DebugAdvance(8f);
        Assert.That(toppling.ReleasePending, Is.True,
            "The final safe hinge pose must request a physics handoff.");
        toppling.DebugReleaseForAudit();

        Assert.That(toppling.IsDynamicFalling, Is.True);
        Assert.That(body.isKinematic, Is.False,
            "An unsupported fallen section must not remain kinematic in mid-air.");
        Assert.That(body.useGravity, Is.True);
        Assert.That(body.detectCollisions, Is.True);
        Assert.That(body.collisionDetectionMode,
            Is.EqualTo(CollisionDetectionMode.ContinuousDynamic));
    }

    [Test]
    public void FallenSectionSettlesOnlyWithSupportAndRemainsWakeable()
    {
        Assert.That(UrbanDestructionWorld.TryApplyEnergyBlade(
            buildingCollider,
            new Vector3(0f, 42f, 19f),
            Vector3.forward,
            Vector3.back,
            1000f,
            30f,
            null), Is.True);
        UrbanTopplingSection toppling =
            root.GetComponentInChildren<UrbanTopplingSection>(true);
        Rigidbody body = toppling.GetComponent<Rigidbody>();
        toppling.DebugAdvance(8f);
        toppling.DebugReleaseForAudit();

        toppling.DebugAdvanceSettlementForAudit(3f, false);
        Assert.That(toppling.IsSettledCover, Is.False,
            "Low velocity without a supporting contact is still floating.");

        toppling.DebugAdvanceSettlementForAudit(1.4f, true);
        Assert.That(toppling.IsSettledCover, Is.True);
        Assert.That(body.isKinematic, Is.False,
            "Settled cover stays a sleeping dynamic body so support removal can wake it.");
        Assert.That(body.IsSleeping(), Is.True);
        Assert.That(body.detectCollisions, Is.True);
    }

    [Test]
    public void BossTopplingStopsAtNeighbourAndTransfersStructuralImpact()
    {
        GameObject neighbourObject = GameObject.CreatePrimitive(
            PrimitiveType.Cube);
        neighbourObject.name = "NeighbourTower_TopplingBlocker";
        neighbourObject.transform.SetParent(root.transform, false);
        neighbourObject.transform.localPosition = new Vector3(0f, 110f, -72f);
        neighbourObject.transform.localScale = new Vector3(52f, 220f, 52f);
        UrbanDestructibleBuilding neighbour =
            neighbourObject.AddComponent<UrbanDestructibleBuilding>();
        neighbour.Configure(new AirCombatBuildingLot
        {
            stableId = "test-neighbour-toppling-blocker",
            center = neighbourObject.transform.localPosition,
            size = neighbourObject.transform.localScale,
            yaw = 0f,
            band = AirCombatBuildingBand.High
        }, coordinator);
        Collider neighbourCollider = neighbourObject.GetComponent<Collider>();
        float integrityBefore = neighbour.Integrity01;

        Assert.That(UrbanDestructionWorld.TryApplyEnergyBlade(
            buildingCollider,
            new Vector3(0f, 42f, 19f),
            Vector3.forward,
            Vector3.back,
            1000f,
            30f,
            null), Is.True);
        UrbanTopplingSection toppling =
            root.GetComponentInChildren<UrbanTopplingSection>(true);
        Assert.That(toppling, Is.Not.Null);

        Physics.SyncTransforms();
        toppling.DebugAdvance(8f);
        Physics.SyncTransforms();

        Assert.That(toppling.WasBlocked, Is.True,
            "上段楼体扫掠到邻楼后必须记录阻挡，不能继续强制穿过。 ");
        Assert.That(toppling.CurrentAngle, Is.LessThan(80f));
        Assert.That(toppling.ReleasePending, Is.True,
            "Obstacle contact ends the controlled hinge phase but must not freeze the tower forever.");
        Assert.That(neighbour.Integrity01, Is.LessThan(integrityBefore),
            "倾倒楼体的动量必须传递给被撞建筑。 ");
        Assert.That(neighbour.BreachCount, Is.Zero,
            "Toppling fatigue must not reuse the visible facade-breach effect.");
        Assert.That(neighbour.HasLocalizedDamageVisual, Is.False,
            "Tower contact should deduct hidden durability without a damage visual.");
        Assert.That(neighbour.IsCollapsed, Is.False,
            "该测试使用高完整度邻楼；中等撞击应先造成结构损伤而不是无条件连锁倒塌。 ");

        BoxCollider[] fallingColliders = toppling.GetComponents<BoxCollider>();
        for (int index = 0; index < fallingColliders.Length; index++)
        {
            Assert.That(Physics.ComputePenetration(
                fallingColliders[index],
                fallingColliders[index].transform.position,
                fallingColliders[index].transform.rotation,
                neighbourCollider,
                neighbourCollider.transform.position,
                neighbourCollider.transform.rotation,
                out _,
                out float penetration), Is.False,
                "倾倒停止后不应与仍直立的邻楼发生穿模，penetration=" +
                penetration.ToString("0.000"));
        }
    }

    [Test]
    public void HeavyTopplingImpactCanStartARealNeighbourCollapse()
    {
        GameObject neighbourObject = GameObject.CreatePrimitive(
            PrimitiveType.Cube);
        neighbourObject.name = "NeighbourTower_ChainCollapseTarget";
        neighbourObject.transform.SetParent(root.transform, false);
        neighbourObject.transform.localPosition = new Vector3(0f, 36f, -52f);
        neighbourObject.transform.localScale = new Vector3(28f, 72f, 28f);
        UrbanDestructibleBuilding neighbour =
            neighbourObject.AddComponent<UrbanDestructibleBuilding>();
        neighbour.Configure(new AirCombatBuildingLot
        {
            stableId = "test-neighbour-chain-collapse",
            center = neighbourObject.transform.localPosition,
            size = neighbourObject.transform.localScale,
            yaw = 0f,
            band = AirCombatBuildingBand.Low
        }, coordinator);

        Assert.That(UrbanDestructionWorld.TryApplyEnergyBlade(
            buildingCollider,
            new Vector3(0f, 42f, 19f),
            Vector3.forward,
            Vector3.back,
            1000f,
            30f,
            null), Is.True);
        UrbanTopplingSection sourceToppling = root
            .GetComponentsInChildren<UrbanTopplingSection>(true)
            .Single();

        Physics.SyncTransforms();
        sourceToppling.DebugAdvance(1.35f);
        Physics.SyncTransforms();

        Assert.That(sourceToppling.WasBlocked, Is.True);
        Assert.That(neighbour.IsCollapsed, Is.True,
            "高速且质量足够大的楼体撞击应允许邻楼发生真实结构失效，" +
            "remainingIntegrity=" + neighbour.Integrity01.ToString("0.000") +
            ", sweepSpeed=" +
            sourceToppling.LastAttemptedSweepSpeed.ToString("0.00") +
            ", transfer=" + sourceToppling.LastStructuralTransferApplied +
            ", target=" + sourceToppling.LastBlockingTargetId +
            ", neighbour=" + neighbour.GetInstanceID() +
            ", damage=" +
            sourceToppling.LastRequestedStructuralDamage.ToString("0.0") +
            ", integrity=" + neighbour.CurrentIntegrity.ToString("0.0") +
            "/" + neighbour.MaximumIntegrity.ToString("0.0") + "。 ");
        Assert.That(root.GetComponentsInChildren<UrbanTopplingSection>(true),
            Has.Length.GreaterThanOrEqualTo(2),
            "邻楼失效后必须进入同一套倾倒流程，才能继续形成有上限的连锁倒塌。 ");
    }

    [Test]
    public void PlainGroundIsNotRegisteredAsUrbanDestructible()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        try
        {
            bool changed = UrbanDestructionWorld.TryApplyDemolition(
                ground.GetComponent<Collider>(),
                Vector3.zero,
                Vector3.up,
                Vector3.down,
                999f,
                20f,
                null);
            Assert.That(changed, Is.False);
            Assert.That(
                ground.GetComponent<IUrbanDestructible>(),
                Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(ground);
        }
    }

    [Test]
    public void ReplacementArtWithMultipleChildMeshesUsesSameDestructionContract()
    {
        var replacement = new GameObject("ReplacementArt_MultiMeshRoot");
        replacement.transform.SetParent(root.transform, false);
        replacement.transform.localPosition = new Vector3(200f, 0f, 0f);

        GameObject lower = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lower.name = "ImportedFacade_Lower";
        lower.transform.SetParent(replacement.transform, false);
        lower.transform.localPosition = new Vector3(0f, 30f, 0f);
        lower.transform.localScale = new Vector3(38f, 60f, 30f);

        GameObject upper = GameObject.CreatePrimitive(PrimitiveType.Cube);
        upper.name = "ImportedFacade_Upper_SecondMaterialSlot";
        upper.transform.SetParent(replacement.transform, false);
        upper.transform.localPosition = new Vector3(0f, 90f, 0f);
        upper.transform.localScale = new Vector3(28f, 60f, 24f);

        UrbanDestructibleBuilding replacementDestructible =
            replacement.AddComponent<UrbanDestructibleBuilding>();
        replacementDestructible.Configure(new AirCombatBuildingLot
        {
            stableId = "replacement-art-multimesh-001",
            center = new Vector3(200f, 60f, 0f),
            size = new Vector3(38f, 120f, 30f),
            yaw = 0f,
            band = AirCombatBuildingBand.High
        }, coordinator);
        Physics.SyncTransforms();

        Assert.That(replacementDestructible.DestructionBounds.size.y,
            Is.GreaterThan(115f));
        Collider hitCollider = lower.GetComponent<Collider>();
        Vector3 hitPoint = new Vector3(200f, 60f, 15f);
        ApplyDemolitionHits(
            hitCollider,
            hitPoint,
            Vector3.forward,
            Vector3.back,
            4);

        Assert.That(replacementDestructible.IsCollapsed, Is.False);
        Assert.That(replacementDestructible.BrokenStructuralCells,
            Is.GreaterThan(0));
        Assert.That(replacementDestructible.HasLocalizedDamageVisual, Is.True,
            "替换后的多子网格美术也必须复用同一套局部缺口表现。");
        Assert.That(replacementDestructible.ActiveCollisionProxyCount,
            Is.GreaterThan(0));
        Assert.That(root.GetComponentsInChildren<MeshCollider>(true), Is.Empty);
    }

    [Test]
    public void SkybridgeIgnoresOrdinaryFireAndSmallBlastButDemolitionSeversIt()
    {
        GameObject bridgeObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bridgeObject.name = "Skybridge_SelectiveDamage_Test";
        bridgeObject.transform.SetParent(root.transform, false);
        bridgeObject.transform.localPosition = new Vector3(180f, 90f, 0f);
        bridgeObject.transform.localScale = new Vector3(9f, 3f, 58f);
        BoxCollider collider = bridgeObject.GetComponent<BoxCollider>();
        UrbanDestructibleBridge bridge =
            bridgeObject.AddComponent<UrbanDestructibleBridge>();
        bridge.Configure(coordinator, 92f, 18f);
        Physics.SyncTransforms();

        Assert.That(UrbanDestructionWorld.TryApplyDirect(
            collider, bridge.DestructionBounds.center, Vector3.forward,
            999f, null), Is.False);
        Assert.That(bridge.IsUrbanDestroyed, Is.False);

        UrbanDestructionWorld.ApplyExplosion(
            bridge.DestructionBounds.center, 12f, 500f, null);
        Assert.That(bridge.IsUrbanDestroyed, Is.False,
            "半径不足的大数值爆炸不能绕过大型爆炸规则。");

        Assert.That(UrbanDestructionWorld.TryApplyDemolition(
            collider,
            bridge.DestructionBounds.center,
            Vector3.forward,
            Vector3.back,
            120f,
            16f,
            null), Is.True);
        Assert.That(bridge.IsUrbanDestroyed, Is.True);
        Assert.That(bridgeObject.GetComponent<Renderer>().enabled, Is.False);
        Assert.That(collider.enabled, Is.False);
    }

    [Test]
    public void SkybridgeAcceptsOnlyAGenuinelyLargeExplosion()
    {
        GameObject bridgeObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bridgeObject.name = "Skybridge_LargeExplosion_Test";
        bridgeObject.transform.SetParent(root.transform, false);
        bridgeObject.transform.localPosition = new Vector3(260f, 95f, 0f);
        bridgeObject.transform.localScale = new Vector3(9f, 3f, 58f);
        UrbanDestructibleBridge bridge =
            bridgeObject.AddComponent<UrbanDestructibleBridge>();
        bridge.Configure(coordinator, 92f, 18f);
        Physics.SyncTransforms();

        int affected = UrbanDestructionWorld.ApplyExplosion(
            bridge.DestructionBounds.center,
            24f,
            140f,
            null);

        Assert.That(affected, Is.GreaterThanOrEqualTo(1));
        Assert.That(bridge.IsUrbanDestroyed, Is.True);
    }

    [Test]
    public void ConfirmedBossRamCreatesTwoCollisionlessTemporaryHalves()
    {
        UrbanDestructibleBridge bridge = CreateBridge(
            "Skybridge_BossRam_Test",
            new Vector3(300f, 92f, 0f));
        Bounds before = bridge.DestructionBounds;

        Assert.That(
            bridge.TryBreakFromBossRam(
                before.center + Vector3.forward * 5f,
                Vector3.forward),
            Is.True);
        Assert.That(bridge.IsUrbanDestroyed, Is.True);
        Assert.That(
            bridge.GetComponentsInChildren<Collider>(true)
                .All(item => !item.enabled),
            Is.True);
        Transform[] halves = root.GetComponentsInChildren<Transform>(true)
            .Where(item => item.name.StartsWith(
                "BossRamBridgeHalf_",
                StringComparison.Ordinal))
            .ToArray();
        Assert.That(halves, Has.Length.EqualTo(2));
        Assert.That(
            halves.SelectMany(item =>
                item.GetComponentsInChildren<Collider>(true))
                .All(item => !item.enabled),
            Is.True);
        Assert.That(
            halves.All(item => item.GetComponent<Rigidbody>() != null),
            Is.True);
        Assert.That(
            UrbanDestructibleBridge.ActiveBossRamDebrisHalfCount,
            Is.LessThanOrEqualTo(24));
    }

    [Test]
    public void RuntimeStaticBatchingKeepsDestructibleBuildingMeshIndependent()
    {
        var generated = new GameObject("GeneratedCity_SafeBatchingTest");
        generated.transform.SetParent(root.transform, false);
        var destructibleRoot = new GameObject("DestructibleBuildingRoot");
        destructibleRoot.transform.SetParent(generated.transform, false);
        destructibleRoot.AddComponent<UrbanDestructibleBuilding>();
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "ReadableFacade";
        visual.transform.SetParent(destructibleRoot.transform, false);
        MeshFilter filter = visual.GetComponent<MeshFilter>();
        Mesh originalMesh = filter.sharedMesh;

        System.Reflection.MethodInfo method = typeof(AirCombatCityPcgLab)
            .GetMethod(
                "ApplySafeRuntimeStaticBatching",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method.Invoke(null, new object[] { generated });

        Assert.That(filter.sharedMesh, Is.SameAs(originalMesh));
        Assert.That(filter.sharedMesh.isReadable, Is.True);
        Assert.That(visual.GetComponent<Renderer>().isPartOfStaticBatch, Is.False,
            "可破坏建筑不能被运行时合批成不可读的城市级 Combined Mesh。 ");
    }

    [Test]
    public void CrescentTerrainFallbackSkipsUnreadableMeshWithoutLosingHitHandling()
    {
        var terrain = new GameObject("Terrain_UnreadableCombinedMesh");
        terrain.transform.SetParent(root.transform, false);
        MeshFilter filter = terrain.AddComponent<MeshFilter>();
        BoxCollider collider = terrain.AddComponent<BoxCollider>();
        var mesh = new Mesh
        {
            name = "Combined Mesh (root: Generated_AirCombatCity_Test)"
        };
        mesh.vertices = new[]
        {
            new Vector3(-2f, 0f, -2f),
            new Vector3(2f, 0f, -2f),
            new Vector3(0f, 0f, 2f)
        };
        mesh.triangles = new[] { 0, 1, 2 };
        mesh.RecalculateBounds();
        filter.sharedMesh = mesh;
        mesh.UploadMeshData(true);

        try
        {
            Assert.That(mesh.isReadable, Is.False);
            Type runtime = typeof(CombatTerrainDestructionRuntime);
            System.Reflection.MethodInfo deform = runtime.GetMethod(
                "DeformRuntimeMesh",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic,
                null,
                new[]
                {
                    typeof(Transform),
                    typeof(Mesh),
                    typeof(Vector3),
                    typeof(Vector3),
                    typeof(float),
                    typeof(float)
                },
                null);
            System.Reflection.MethodInfo isTerrain = runtime.GetMethod(
                "IsTerrainSurface",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic);

            Assert.That(deform, Is.Not.Null);
            Assert.That(isTerrain, Is.Not.Null);
            Assert.That((bool)isTerrain.Invoke(
                null,
                new object[] { collider }), Is.True,
                "不可读的地形碰撞仍应被月牙光刃识别，从而保留命中特效和阻挡反馈。");
            Assert.That((bool)deform.Invoke(
                null,
                new object[]
                {
                    terrain.transform,
                    mesh,
                    Vector3.zero,
                    Vector3.up,
                    8f,
                    4f
                }), Is.False,
                "不可读网格只能跳过顶点凹陷，不能再访问 mesh.vertices 抛错。");
        }
        finally
        {
            filter.sharedMesh = null;
            Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void DemolitionDestroysOnlyTheHitSkybridge()
    {
        UrbanDestructibleBridge first = CreateBridge(
            "Skybridge_Independent_A",
            new Vector3(340f, 90f, 0f));
        UrbanDestructibleBridge second = CreateBridge(
            "Skybridge_Independent_B",
            new Vector3(420f, 90f, 0f));
        Collider firstCollider = first.GetComponent<Collider>();
        Vector3 hitPoint = first.DestructionBounds.center + Vector3.forward *
                           first.DestructionBounds.extents.z;

        Assert.That(UrbanDestructionWorld.TryApplyDemolition(
            firstCollider,
            hitPoint,
            Vector3.forward,
            Vector3.back,
            120f,
            16f,
            null), Is.True);

        Assert.That(first.IsUrbanDestroyed, Is.True);
        Assert.That(second.IsUrbanDestroyed, Is.False,
            "命中一条廊桥不能删除同一区域的其他廊桥。");
        Assert.That(Vector3.Distance(first.LastBreakPoint, hitPoint), Is.LessThan(0.01f));
        Assert.That(second.GetComponent<Renderer>().enabled, Is.True);
        Assert.That(second.GetComponent<Collider>().enabled, Is.True);
    }

    UrbanDestructibleBuilding CreateAdditionalBuilding(
        string objectName,
        string stableId,
        Vector3 position)
    {
        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        target.name = objectName;
        target.transform.SetParent(root.transform, false);
        target.transform.localPosition = position;
        target.transform.localScale = new Vector3(42f, 120f, 38f);
        UrbanDestructibleBuilding destructible =
            target.AddComponent<UrbanDestructibleBuilding>();
        destructible.Configure(new AirCombatBuildingLot
        {
            stableId = stableId,
            center = position,
            size = new Vector3(42f, 120f, 38f),
            yaw = 0f,
            band = AirCombatBuildingBand.High
        }, coordinator);
        Physics.SyncTransforms();
        return destructible;
    }

    static void ApplyDemolitionHits(
        Collider target,
        Vector3 point,
        Vector3 normal,
        Vector3 direction,
        int count)
    {
        for (int index = 0; index < count; index++)
        {
            UrbanDestructionWorld.TryApplyDemolition(
                target,
                point,
                normal,
                direction,
                150f,
                20f,
                null);
        }
    }

    UrbanDestructibleBridge CreateBridge(string objectName, Vector3 position)
    {
        GameObject bridgeObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bridgeObject.name = objectName;
        bridgeObject.transform.SetParent(root.transform, false);
        bridgeObject.transform.localPosition = position;
        bridgeObject.transform.localScale = new Vector3(9f, 3f, 58f);
        UrbanDestructibleBridge bridge =
            bridgeObject.AddComponent<UrbanDestructibleBridge>();
        bridge.Configure(coordinator, 92f, 18f);
        Physics.SyncTransforms();
        return bridge;
    }
}
