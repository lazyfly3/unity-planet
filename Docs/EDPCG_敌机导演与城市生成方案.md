# EDPCG：体验驱动的城市与敌机遭遇生成方案

## 1. 术语、定位与文档结论

EDPCG 的正式含义是：

> Experience-Driven Procedural Content Generation  
> 体验驱动的程序化内容生成

它不是 “Enemy Director + PCG” 的缩写。敌机 Director 是 EDPCG 的运行时执行器之一，城市 PCG 是内容生成器之一；真正位于系统上层的是玩家体验目标与体验评估模型。

本项目采用的核心定义是：先描述希望玩家获得的体验，再让城市布局和敌机遭遇共同生成能够产生这种体验的内容，并通过规则、仿真与运行时遥测验证结果。

### 1.1 本项目的目标体验

- 大部分时间有适中、可持续的压力，而不是持续压迫。
- 高潮短暂、来源清楚，并且玩家知道如何脱离。
- 城市地形能提供真实的战术解法，而不只是视觉背景。
- 星球越难，玩家需要处理的空间与敌人组合越复杂。
- 最难关拥有 64 架敌机的规模感，但不会让 64 架同时攻击玩家。
- 玩家通过路线、遮挡、速度和目标选择主动改变压力。

### 1.2 完整 EDPCG 闭环

```text
星球难度与任务目标
  → Experience Profile：期望的压力、节奏、可读性、恢复和技巧表达
  → 城市 PCG + 遭遇 PCG：生成空间与敌群候选
  → Experience Evaluator：规则检查、快速仿真和体验评分
  → 选择满足体验目标的城市与遭遇组合
  → Runtime Director：实现人口、交战、攻击和阶段节奏
  → 玩家体验遥测：实际压力、受损、路线和恢复情况
  → 仅有限调整后续调度，并为离线调参提供数据
```

EDPCG 各层职责：

| 层 | 主要职责 | 本项目对应内容 |
|---|---|---|
| Experience Specification | 把“好玩”转成可测量目标 | 压力区间、峰值时长、恢复频率、空间选择、可读性 |
| Content Representation | 表达可以被生成和评估的内容 | 城市区域、语义航路、入口、遭遇名册、状态机、编队、阶段 |
| Player Experience Model | 估计内容将给玩家造成什么体验 | `ActualPressure`、导航压力、地形压力、方向压力、玩家负担 |
| Generator / Search | 产生多组合法内容 | 城市 Seed、战术机会、入口连接、敌群批次 |
| Experience Evaluator | 淘汰不公平或体验不匹配的候选 | 硬约束、快速仿真、体验评分 |
| Runtime Director | 在实际游玩中兑现目标体验 | 三层预算、策略等级、Path Intent、攻击令牌、四阶段节奏 |
| Telemetry Calibrator | 用真实数据校准体验模型 | 死亡热点、压力超限、路线选择、恢复区使用率 |

因此，本方案的核心不是“地图生成完以后随机刷怪”，也不是由 Director 单独制造难度，而是城市生成、遭遇生成和运行时调度共同服务同一份 Experience Profile。

最终体验目标：

1. 星球难度按六档稳定上升。
2. 最难关整关累计出现 64 架敌机。
3. 敌机很多，但不会同时向玩家输出全部伤害。
4. 大部分战斗时间维持适中压力，短暂出现可理解的高潮。
5. 城市道路、楼群、遮挡链、暴露捷径、换层通道和恢复口袋会真实改变敌机行为。
6. 难度主要来自角色组合、方向、空间和决策，不依赖血量、伤害膨胀。
7. 敌机使用完整状态机执行可读的攻击轮次，不沿直线持续追射玩家。
8. AI 寻路既能通过截击和侧翼增压，也能通过脱离、错峰和断视线减压。
9. Director 不在玩家眼前修改建筑，不临时封死已经成立的有效策略。

---

## 2. 当前项目接入流程

正式流程为：

```text
StartMenu
  → 首次飞船组装 / 已有存档进入空间站
  → ModularAssemblyLab
  → InterstellarFlight 近轨章节大厅
  → 选择星球和任务
  → PlanetOrbitChapterSelectionContext
  → star 地表任务场景
  → InfinitePlanarSurfaceWorld 有限战斗区域
  → 城市或自然战场生成
  → FinitePlanetHordeCombatController
  → HordeCombatDirector
```

现有系统中可以直接保留的部分：

- `HordeCombatDirector` 的对象池、随机种子、编队、威胁费用、攻击令牌和波次规则。
- `HordeEnemyVehicle` 的物理飞行、攻击状态机、视线检查和三种角色。
- `HordeAirNavigationService` 的碰撞检测和基础路径搜索。
- `FinitePlanetHordeCombatController` 的任务生命周期、胜负和最终清场。
- `FinitePlanetUrbanCombatRuntime` 的城市生成及正式任务锚点同步。
- `AirCombatCityPlan` 的道路、航路、入口、战术区域和建筑数据。
- `CombatCityDifficultyProfile` 的六档语义化难度参数。
- `VehicleStructureGraph` 的玩家健康、CPU 连通性和战斗能力状态。

当前主要缺口：

1. 城市只把敌机入口坐标交给 Director，入口类型、目标、预警和战术连接关系丢失。
2. 城市会随难度变化，但普通敌机仍使用基本相同的时间曲线和固定属性。
3. `LocalDifficultyBudget` 尚未成为运行时约束。
4. 敌机路径只使用通用环形导航采样，没有优先消费城市航路与战术机会入口/出口。
5. 当前对象池只有 10，无法同时表现二十架以上的城市敌群。
6. 当前星球难度来自近轨界面的选择序号，适合起始六星系统，但不适合作为跨星系的长期难度身份。
7. 现有 PCG 已有 Maneuver、Occlusion、Exposure、Recovery、VerticalEscape 等语义，但尚未根据任务判断区域是否真的有体验价值。
8. 完整转弯、连续断视线、捷径时间收益、10 秒维修和受损换层仍缺少自动功能验证，区域“存在”不等于玩法“成立”。
9. 自爆兵和远程兵当前只在战术目标上有简单区别，最终仍进入同一个距离成本 A*；尚未形成动态截击搜索与火力位置搜索两套系统。

---

## 3. 设计目标与非目标

### 3.1 设计目标

- 最难关拥有 64 架敌机的完整敌群规模。
- 玩家附近的真实攻击者受到严格限制。
- 玩家能利用城市改变当前压力，而不是只能靠持续输出换血。
- 六类战术区域只在任务上下文能兑现对应玩家动作和 AI 反应时生成并计分。
- 每次高压都有可预见的来源、持续时间和退出方式。
- 不同星球逐步增加新的战术要求，而不是所有数值同时线性提高。
- 同一 Seed 和相同玩家行为可以复现相同的基础生成与 Director 决策。
- PCG 失败时可以回退到通用自然战场和通用入口逻辑。

### 3.2 非目标

- 不追求 64 架敌机同时运行完整 Rigidbody 战斗 AI。
- 不把动态难度做成实时针对玩家的作弊系统。
- 不通过临时移动建筑、生成墙体或关闭路线来改变难度。
- 不把敌机血量和伤害作为主要星球难度曲线。
- 不要求所有场上敌机同时向玩家靠拢。
- 不强制玩家按固定顺序通过城市区域。

---

## 4. 三种必须分离的预算

大量敌机和适中压力能够同时成立，前提是将三个预算彻底分开。

### 4.1 Population Budget：人口预算

控制城市中总共存在多少敌机，包括：

- 正在从入口进场的敌机。
- 在远处楼群后方编队的敌机。
- 正在占领战术位置的敌机。
- 玩家附近形成威胁但尚未攻击的敌机。
- 正在攻击的敌机。
- 攻击结束后脱离和重新编队的敌机。

### 4.2 Engagement Budget：交战预算

控制玩家附近多少敌机处于需要关注的范围内。

它决定：

- 玩家附近敌机上限。
- 近距离 Interceptor 数量。
- 进入武器有效射程的 Striker 数量。
- 当前可见的主要威胁方向。

### 4.3 Attack Budget：攻击预算

控制真正能够射击或执行自杀冲撞的敌机。

它通过现有攻击令牌实现：

- 没有令牌的敌机可以飞行、占位、追踪和制造视觉规模。
- 有令牌的敌机才能进入 Telegraph、Firing 或最终 Suicide 阶段。
- Gunship 和 Striker 可以消耗更多令牌。
- 压力过高时停止发放新令牌，不需要删除敌机。

Attack Budget 内部继续分成两个不可自由互换的角色额度：

- `SuicideCommitCap`：允许进入最终冲刺走廊的自爆兵数量。
- `RangedFireLaneCap`：允许同时建立的远程有效火力线数量。
- 两者相加不能突破总攻击令牌，但即使总令牌还有空位，任一角色额度已满也不能继续激活同类攻击。

最高难度可以同时具有：

```text
PopulationBudget = 28
EngagementBudget = 16
AttackBudget = 3，高潮短暂为 4
```

因此玩家能够看到二十多架敌机，但真正同时攻击的只有三到四架。

---

## 5. 实际压力模型

Director 不应只根据存活数量判断压力。建议每秒多次采样以下指标，并输出 `0～1` 的标准化压力值。

```text
ActualPressure =
    0.25 × ActiveThreatRatio
  + 0.22 × AttackTokenRatio
  + 0.16 × PressureDirectionRatio
  + 0.12 × ExposureRatio
  + 0.15 × NavigationPressureRatio
  + 0.10 × PlayerStrainRatio
```

### 5.1 指标定义

`ActiveThreatRatio`

- 当前交战范围内威胁费用 / 当前允许威胁上限。
- Interceptor、Striker、Gunship 沿用 1、2、3 的威胁费用。

`AttackTokenRatio`

- 当前已使用攻击令牌 / 当前令牌上限。

`PressureDirectionRatio`

- 当前有效压力方向数 / 当前方向上限。
- 同一方向内多架敌机不重复计为多个方向。

`ExposureRatio`

- 城市 PCG 对玩家当前位置计算出的暴露压力。
- 综合远程视线、遮挡中断、开放路线长度和当前战术区域。

`NavigationPressureRatio`

- 表示敌机已经通过寻路决策承诺的未来压力，而不只是敌机当前距离。
- 自爆兵单独计算预计撞击时间、最终冲刺数量、截击角度和玩家规避空间。
- 远程兵单独计算射界持续时间、有效火力线、交叉角度和断视线后的重新出现。
- 两项压力在 Director 中合并，但不使用会相互稀释的简单平均。
- 选中高压路径时立即预占压力预算，不等敌机飞到玩家身边才计入。
- 敌机取消路径、进入脱离或改走减压路线后释放这部分预算。

`PlayerStrainRatio`

- 近期受伤、模块损失、CPU 连通下降、连续碰撞、长时间失速或刹停失败的组合。

### 5.2 压力区间

| 压力 | 玩家感受 | Director 行为 |
|---:|---|---|
| `0.00～0.30` | 恢复、观察、重新定位 | 不补兵或只做入场预告 |
| `0.30～0.48` | 轻度压力 | 单方向、小编队、低令牌 |
| `0.48～0.65` | 适中压力 | 正常目标区间 |
| `0.65～0.75` | 短暂高潮 | 允许组合升级，持续 8～12 秒 |
| `> 0.75` | 压力过载 | 停止补兵并冻结升级决策 |

硬性保护规则：

- 大部分战斗时间保持在 `0.45～0.65`。
- 高于 `0.65` 的连续时间不超过 12 秒。
- 高于 `0.75` 持续 2 秒，立即停止补兵。
- 超载时不删除敌机，只让外围敌机继续占位、绕行或脱离。
- 每 35～45 秒至少出现一次 8～12 秒的恢复机会。

---

## 6. 目标压力的组成

```text
TargetPressure = Clamp(
    PlanetBaseline
  + EncounterPhaseOffset
  + BoundedAdaptiveOffset,
    PlanetFloor,
    PlanetCeiling)
```

### 6.1 Planet Baseline

星球基准定义这一关稳定的难度身份，不受当前玩家表现影响。

### 6.2 Encounter Phase Offset

单局节奏阶段提供小幅临时变化：

- 预告：`-0.12`
- 交战：`0`
- 高潮：`+0.08～+0.12`
- 恢复：`-0.20～-0.28`

### 6.3 Bounded Adaptive Offset

动态修正必须限制在：

```text
帮助玩家：最低 -0.12
增加挑战：最高 +0.08
```

帮助幅度略大于惩罚幅度，防止 Director 因玩家表现好而持续加压。

---

## 7. 六档星球数量与压力曲线

以下数值作为第一轮隔离试玩基准。

| 星球 | 整关敌机 | 同时在场 | 近身交战 | 攻击令牌 | 压力方向 | 平均压力 | 峰值压力 |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 16 | 8 | 5 | 1，教学高潮为 2 | 1 | 0.42 | 0.58 |
| 2 | 24 | 10 | 6 | 2 | 1 | 0.46 | 0.61 |
| 3 | 32 | 14 | 8 | 2 | 1～2 | 0.50 | 0.65 |
| 4 | 40 | 18 | 10 | 2，高潮为 3 | 2 | 0.54 | 0.69 |
| 5 | 52 | 22 | 12 | 3 | 2～3 | 0.58 | 0.72 |
| 6 | 64 | 24～28 | 14～16 | 3，高潮为 4 | 3 | 0.62 | 0.75 |

说明：

- “整关敌机”是该任务的确定性敌机名册数量。
- “同时在场”包括远处进场、占位、交战、脱离和重组中的敌机。
- “近身交战”是进入玩家战术关注范围的敌机。
- “攻击令牌”才是同时输出伤害的真实上限。
- 星球 6 的 64 架不是同时运行完整战斗 AI。

### 7.1 城市难度维度建议

| 星球 | 导航挑战 | 战斗压力 | 暴露压力 | 恢复慷慨度 | 主要学习要求 |
|---:|---:|---:|---:|---:|---|
| 1 | 0.20 | 0.25 | 0.20 | 0.90 | 遮挡、开阔和恢复 |
| 2 | 0.30 | 0.36 | 0.28 | 0.82 | 上下换层 |
| 3 | 0.38 | 0.48 | 0.48 | 0.70 | 暴露捷径与安全绕路 |
| 4 | 0.44 | 0.64 | 0.54 | 0.62 | 多方向压力，但不同时提高驾驶复杂度 |
| 5 | 0.62 | 0.75 | 0.66 | 0.48 | 多层航路和复杂敌人组合 |
| 6 | 0.74 | 0.86 | 0.78 | 0.38 | 受损飞行和综合决策 |

这组曲线故意避免所有维度线性同时拉满。例如星球 4 主要提高多方向敌人压力，但导航挑战只小幅上升。

### 7.2 Experience Profile

每个星球不只保存一个 Difficulty 数值，而是保存一份体验目标。建议第一版至少包含：

```text
ExperienceProfile
  targetAveragePressure
  targetPeakPressure
  maxPeakDuration
  recoveryInterval
  recoveryDuration
  desiredPressureDirections
  desiredRouteChoiceCount
  desiredOcclusionAvailability
  desiredSkillExpression
  desiredNavigationPressure
  maximumStrategyLevel
  maximumConvergingAttackers
  maximumSuicideCommits
  maximumRangedFireLanes
  missionTimePressure
  selfRepairDurationSeconds
  requiredTacticalAreaFunctions
  optionalTacticalAreaWeights
  rosterSize
  roleMix
  forbiddenCombinations
```

其中 `desiredSkillExpression` 表示这一星球主要考验什么，例如遮挡使用、上下换层、暴露捷径判断、多方向威胁管理或受损飞行。这样星球难度上升时，可以改变体验结构，而不是只提高一个总难度系数。

### 7.3 城市与遭遇的共同候选生成

对同一个星球 Seed，生成器应产生多组轻量候选，而不是第一组能生成就直接使用：

1. 根据任务目标、玩家维修能力和时间压力生成 `TacticalAreaRequirementSet`。
2. 城市 PCG 只为有体验价值的区域生成战术空间、连接航路、入口和几何支撑。
3. 遭遇 PCG 根据这些区域分配敌机角色、编队、批次、入口和允许策略。
4. AI 路径规划器为各策略生成语义航路、攻击位置、脱离路线和到达预约。
5. 快速评估器模拟玩家沿安全路线、快速路线和高风险路线移动，并让敌机执行简化状态机。
6. 检查每个 Required 区域是否真的支持目标动作和预期 AI 反应。
7. 计算每条玩家路线上的预计压力曲线、导航压力、攻击轮次、压力来源和恢复机会。
8. 淘汰违反硬规则的候选，并从合格候选中选择最接近 Experience Profile 的结果。

城市和敌群不能各自独立随机后再强行拼接。比如城市缺少连续遮挡时，遭遇生成器不能安排大量 Striker；城市驾驶负担已经很高时，敌群组合必须降低同时交战或多方向压力。

### 7.4 体验评分与硬约束

建议候选评分：

```text
ExperienceFitness =
    0.20 × PressureCurveMatch
  + 0.13 × RecoveryRhythmMatch
  + 0.12 × TacticalChoiceQuality
  + 0.10 × ThreatReadability
  + 0.10 × NavigationPressureMatch
  + 0.08 × AttackCycleReadability
  + 0.07 × SkillExpressionMatch
  + 0.06 × RouteVariety
  + 0.14 × TacticalAreaFunctionalMatch
  - HardConstraintPenalty
```

评分不是越难越高，而是越接近当前星球的目标体验越高。

以下情况直接淘汰候选，不参与软评分竞争：

- 必经路线连续超过压力上限且没有合法脱离路线。
- 高暴露、高驾驶难度和高敌人压力同时出现在同一区域。
- RecoveryPocket 可被远程敌机持续穿透，或者入口直接朝向内部。
- 玩家在没有预警的情况下会同时受到两个以上新方向攻击。
- 目标区域无法容纳当前批次的合法飞行、重组和脱离路径。
- 简化仿真中出现多支编队同步汇聚、持续直线追射或没有攻击后脱离。
- 自爆兵和远程兵最终使用相同的目标生成、搜索状态或失败回退路径。
- 自爆兵缺少合法 Commit/Break-Away 走廊，或远程兵缺少 Fire Position/Break-LOS 邻接点。
- 任一高压策略没有合法回退路线，或者减压指令无法在规定时间内生效。
- Required 区域缺失，或者虽有对应外形却无法完成规定的玩家动作、AI 反应和撤离路径。
- 普通清怪关用无实际收益的开放捷径虚增战术机会评分。
- 最难关无法完成 64 架名册的分批进入与回收。

### 7.5 运行时与生成时的边界

- 生成时决定城市结构、战术区域、入口关系、语义航路属性、敌群名册、允许策略和基础阶段计划。
- 运行时 Director 决定具体何时发放攻击令牌、何时延迟下一小队、何时让敌机脱离或重组。
- Director 可以在允许范围内选择备用入口，但不能创造未经评估的新入口。
- Director 可以减缓或延后压力，但不能偷偷删除最难关的 64 架任务名册。
- Director 不在玩家游玩时改变建筑结构，以免破坏玩家已经理解的空间规则。

这条边界保证 EDPCG 的内容可复现、可测试，同时仍能针对玩家当前状态维持适中压力。

---

## 8. 最难关 64 架敌机方案

### 8.1 敌机总构成

```text
Interceptor：40
Striker：20
Gunship：4
总计：64
```

约束：

- 同时最多一架 Gunship。
- 普通阶段同时开火的 Striker 最多两架。
- 最终高潮短暂允许三架 Striker 获得攻击资格。
- Interceptor 只有获得令牌后才能进入最终自杀冲撞阶段。

### 8.2 五个压力批次

| 批次 | 数量 | Interceptor | Striker | Gunship |
|---|---:|---:|---:|---:|
| 开场批次 | 8 | 6 | 2 | 0 |
| 第二批次 | 12 | 8 | 3 | 1 |
| 第三批次 | 14 | 9 | 4 | 1 |
| 第四批次 | 14 | 9 | 4 | 1 |
| 最终批次 | 16 | 8 | 7 | 1 |
| 合计 | 64 | 40 | 20 | 4 |

批次是压力节奏单位，不是强制“杀光当前波次才能刷新下一波”。

Director 可以在满足以下条件时预告下一小队：

- 当前压力低于目标下界一段时间。
- Population Budget 尚未达到并发上限。
- 对应入口已冷却。
- 城市航路存在合法路径。
- 玩家不处于强制恢复窗口。

### 8.3 建议时长

最难关目标时长为约 4～6 分钟：

```text
预告 5秒
→ 交战 18～25秒
→ 高潮 8～12秒
→ 恢复 8～12秒
→ 下一轮
```

64 架敌机必须全部进入任务名册，但压力过高时允许延迟入场。Director 不因帮助玩家而偷偷删减总敌机数。

任务结束条件建议使用：

```text
SpawnedRosterCount == RosterCount
&& ResolvedRosterCount == RosterCount
&& CreditedKillCount >= RequiredCreditedKills
&& ActiveEnemyCount == 0
&& SpawnQueueCount == 0
```

每个名册成员从生成名册时就获得不可复用的 `rosterMemberId`，生成、对象池回收、导航恢复和再次入场都沿用同一 ID。结算必须使用带原因的恰好一次写入：

- `KilledByPlayer`：计入 `ResolvedRosterCount` 和 `CreditedKillCount`，正常发放奖励。
- `PlayerCausedEnvironment`：玩家在有效归因窗口内诱导碰撞或自毁，计入解决与有效击杀。
- `SelfDetonated`：敌机自行撞毁或自爆，计入解决但默认不计有效击杀和奖励。
- `NavigationRecovery`：只把同一名册成员移出现场、保留生命与状态并等待安全重入；不得计入解决、击杀或奖励。
- `TechnicalFinalClear`：只作为开发期故障兜底并写入错误遥测，不进入正式关卡的正常结算路径。

`RequiredCreditedKills` 必须在生成名册时完成可达性验证，不能高于可稳定提供击杀机会的非自爆敌机与可归因自爆敌机数量。若关卡只要求清除威胁，可以把自行自爆视为已中和；若要求玩家击杀，则必须保证剩余名册仍能满足击杀数，不能在最后只剩无法补回的无归因自爆结果。

出生失败和导航失败都不能静默减少 64 架总数。前者进入 `SpawnRecovery` 并改用备用入口，后者重置到安全航段后以同一 `rosterMemberId` 重新入场。

---

## 9. 单局节奏状态机

### 9.1 Preview：预告期

持续：4～6 秒。

- 选择一个或多个合法 PCG 入口。
- 显示方向、航迹、声音或 HUD 预警。
- 第一颗星球只预告一个方向。
- 不发放 Gunship 攻击窗口。
- 不在玩家视野内生成。

### 9.2 Engage：交战期

持续：15～20 秒。

- 压力目标为 `0.45～0.60`。
- 逐步发放攻击令牌。
- 小队从城市航路进入玩家附近区域。
- 允许玩家利用遮挡链和路线选择拆解敌人组合。

### 9.3 Peak：高潮期

持续：8～12 秒。

- 压力目标为 `0.65～0.75`。
- 可以短暂增加一个攻击令牌。
- 可以激活第二或第三方向。
- 可以加入 Gunship 或 Striker 护航组合。
- 不允许人口、远程攻击、夹击和地形暴露同时达到最高值。

### 9.4 Recover：恢复期

持续：8～12 秒。

- 停止新敌机进入主交战区。
- 攻击令牌降为 0～1。
- 外围敌机仍然存在并继续占位或重新编队。
- 为玩家留下减速、恢复姿态、自修复和重新选择路线的时间。
- 恢复结束前预告下一批敌人来源。

---

## 10. 完整的分层敌机状态机

敌机不能把“导航到玩家位置”和“持续开火”当成同一行为。建议采用四层状态机，各层只负责自己的决策范围：

```text
Encounter FSM：Preview / Engage / Peak / Recover
  → Squad Strategy FSM：编队采用什么策略、从哪条语义路线执行
    → Enemy Combat FSM：单机当前处于接敌、占位、攻击还是脱离
      → Weapon FSM：是否允许预告、开火、冷却
```

上层为下层提供权限和预算，下层不能自行突破上层压力限制。这样能够避免每架敌机各自做出“最优追杀”，最终却在玩家身边形成不可控的群体过载。

### 10.1 Encounter FSM

沿用第 9 节的四阶段压力曲线。它决定：

- 当前允许的策略等级。
- 可以激活的压力方向数。
- 可以进入主交战区的编队数。
- 攻击令牌和高压寻路承诺的上限。
- 是否必须发出 `BreakContact` 或 `Regroup` 指令。

### 10.2 Squad Strategy FSM

每个编队处于以下状态之一：

| 状态 | 作用 |
|---|---|
| `Staged` | 在对象池或远端等待，不占用交战预算 |
| `Ingress` | 沿 PCG 入口和入口航路进入，并执行到达预告 |
| `FormUp` | 形成队形、错开攻击时刻，不直接冲向玩家 |
| `ExecuteStrategy` | 执行探测、截击、侧翼、牵制或夹击策略 |
| `BreakOff` | 策略窗口结束，全队释放攻击权并交叉脱离 |
| `Regroup` | 在遮挡后或外围航路重新整理队形 |
| `Withdraw` | 压力过高、路径失效或任务结束时退出主战区 |

编队策略必须有明确开始时间、最长持续时间、允许角色、压力费用、入口、主路线、退出路线和失败回退路线。

### 10.3 Enemy Combat FSM

每架完整模拟的敌机必须处于以下状态之一：

| 状态 | 行为 | 允许攻击 |
|---|---|---:|
| `Dormant` | 对象池内禁用 | 否 |
| `Ingress` | 从城市外围进入，跟随入口语义航路 | 否 |
| `Forming` | 与编队会合并建立角色间距 | 否 |
| `Positioning` | 前往角色适配的攻击位置或拦截点 | 否 |
| `Threatening` | 保持可读威胁、跟踪或假动作，等待令牌 | 否 |
| `Telegraphing` | 通过航向、航迹、锁定线、声音预告攻击 | 否 |
| `Attacking` | 在有限攻击窗口内执行一次攻击动作 | 是 |
| `Disengaging` | 交叉掠过、拉高、下潜或转入遮挡，释放令牌 | 否 |
| `Regrouping` | 到编队集合点等待下一策略 | 否 |
| `Evasive` | 避免碰撞、处理受损或航路暂时失效 | 否 |
| `Withdrawing` | 远离玩家并退出主战区 | 否 |
| `Destroyed` | 死亡、失能或等待回收 | 否 |

标准循环：

```text
Dormant
  → Ingress
  → Forming
  → Positioning
  → Threatening
  → Telegraphing
  → Attacking
  → Disengaging
  → Regrouping
  → Positioning
```

任意活动状态都可以因碰撞风险进入 `Evasive`，因压力超限进入 `Disengaging`，因任务收尾或连续寻路失败进入 `Withdrawing`。这些中断状态优先于继续攻击。

### 10.4 Weapon FSM

```text
Safe
  → RequestToken
  → Telegraph
  → FireWindow
  → Cooldown
  → Safe
```

进入 `FireWindow` 必须同时满足：

- 单机处于 `Telegraphing`，且预告已完整播放。
- 编队仍处于合法的 `ExecuteStrategy` 窗口。
- 已取得攻击令牌。
- 当前和预测压力均未超过阶段上限。
- 武器射线不会穿过 RecoveryPocket 的保护空间。
- 当前航路稳定，不处于转弯避障、脱离或紧急规避状态。

仅有视线或接近玩家不代表可以开火。运输航路、占位航路和脱离航路上的敌机默认武器为 `Safe`。

### 10.5 禁止直线追射

除 Interceptor 已取得攻击令牌后的短暂最终冲刺外，战术目标不能长期等于玩家当前位置。敌机必须先取得一个有角色含义的空间目标：

- Interceptor 取得预测截击点、掠过点和脱离点。
- Striker 取得远程射击位置、横移终点和断视线重定位点。
- Gunship 取得开阔锚点、缓慢压迫航段和退出航段。

一次攻击必须是有限动作：

```text
Approach → Telegraph → Commit → Attack → Cross / Break → Cooldown
```

攻击后强制脱离和冷却，不能立即把目标重新设为玩家并继续追射。这样玩家面对的是可以学习和预判的攻击轮次，而不是拥有完美跟踪能力的伤害源。

### 10.6 压力曲线决定策略强度

策略强度不直接等于星球难度。星球决定可用策略上限，当前压力阶段和剩余压力空间决定此刻是否能使用这些策略：

```text
PressureHeadroom = TargetPressure - PredictedPressure

AllowedStrategyLevel = Min(
    PlanetStrategyCap,
    EncounterPhaseCap,
    PressureHeadroomLevel)
```

阶段上限建议：`Preview = 1`、`Engage = 2`、`Peak = 3`、`Recover = 0`。

压力余量只授权“新策略”，不强制打断一个仍然安全的现有动作：

| `TargetPressure - PredictedPressure` | 新策略上限 |
|---:|---:|
| `<= 0.00` | 0，不再追加增压策略 |
| `0.00～0.06` | 1，只允许可见探测或单机攻击轮次 |
| `0.06～0.12` | 2，允许截击、单侧翼和角色交错 |
| `> 0.12` | 3，仅在 Peak 可使用复杂协同 |

若 `PredictedPressure > TargetPressure + 0.05`，不是只禁止升级，而是立即让尚未进入攻击窗口的编队执行 `BreakOff` 或 `Regroup`。

| 策略等级 | 可用策略 | 使用条件 |
|---:|---|---|
| 0 | 保持外围、断视线、脱离、重组 | Recover、压力超限或玩家严重受损 |
| 1 | 可见方向探测、单机攻击轮次、简单掠过 | Preview 后段与低压 Engage |
| 2 | 预测截击、单侧翼、角色错位、交错攻击 | 正常 Engage 且仍有压力空间 |
| 3 | 双侧夹击、压制加截击、多高度协同 | 仅 Peak，最多 8～12 秒 |

星球策略上限建议：

| 星球 | 策略上限 | 新增要求 |
|---:|---:|---|
| 1 | 1 | 学会识别预告和一次攻击轮次 |
| 2 | 1 | 增加垂直掠过和角色间距 |
| 3 | 2 | 首次加入单侧翼和预测截击 |
| 4 | 2 | 两种角色交错，但不同时夹击 |
| 5 | 3 | 允许短暂夹击与压制组合 |
| 6 | 3 | 三方向、多高度和复杂编队轮换 |

即使星球 6 解锁等级 3，在压力已经接近 `0.75` 时也必须降为等级 0；反过来，压力明显低于目标时，Director 才可以逐级升级策略，而不是直接增加血量、伤害或让所有敌机同时变聪明。

### 10.7 策略费用与迟滞

每个策略预先声明压力费用，例如：

| 策略 | 建议压力费用 |
|---|---:|
| 可见探测掠过 | 0.04 |
| 单机预测截击 | 0.07 |
| 单侧 Masked Flank | 0.10 |
| 多高度交错攻击 | 0.12 |
| 双侧 Pincer | 0.18 |
| Striker 压制 + Interceptor 截击 | 0.20 |

策略启动时立即预占压力费用，结束或取消后释放。策略至少承诺 3～6 秒，除碰撞、路径失效或压力保护外不频繁切换。升级需要持续低于目标至少 3 秒，降级在预测过载时立即执行，避免 AI 每半秒改变主意造成橡皮筋和航向抖动。

---

## 11. AI 寻路作为压力控制系统

寻路不是单纯的“从 A 点避开障碍到 B 点”。敌机选择哪条路线、什么时候到达、从哪个方向重新出现以及何时主动断开接触，都会直接改变玩家压力。因此 AI 寻路必须进入 Experience Model、Director 压力预算和 PCG 候选评估。

### 11.1 现有寻路实现研究

当前 `HordeAirNavigationService` 已具备一个适合继续扩展的局部导航底座：

- 使用 3 个高度层、3 个半径层和 16 个扇区，最多形成 144 个空中节点。
- 节点高度约为地面以上 60、110、160 米。
- 通过球形通道检测验证节点和边的静态碰撞安全。
- 直达通道无障碍时直接飞向目标，否则使用 A*。
- A* 目前只以几何距离作为边成本。
- 单机约每 0.5 秒检查并重新计算路径，卡住 2 秒也会触发重算。
- 已有机群分离、出生点路径验证、到达预警和寻路失败逃离。

当前战术目标仍然较简单：

- Suicide 角色主要预测玩家短时间后的位置并接近。
- 远程角色主要在玩家周围按固定角度选择期望距离位置。
- 路径负责绕过静态障碍，但不知道城市路线是主路、遮挡侧翼、远程暴露线还是垂直逃生线。
- `FinitePlanetUrbanCombatRuntime` 当前主要把 PCG 入口位置同步给战斗系统，入口类型、目标、预警和战术航路关系没有完整进入导航决策。

所以现有系统解决了“安全可达”，但尚未解决“这条路径会给玩家增加多少压力、是否可读、是否给玩家保留解法”。

### 11.2 现有城市 PCG 已有的导航语义

城市系统已经生成了可直接利用的语义：

- `Main`：清楚、可预判的主航路。
- `MaskedFlank`：利用建筑断视线的侧翼航路。
- `LongRange`：远程角色取得射界的路线。
- `EnemyIngress`：敌机进场专用航路。
- `VerticalEscape`：上下换层和释放压力的路线。
- `ManeuverBowl`、`OcclusionGate`、`ExposureLane`、`RecoveryPocket` 等战术体积。

现有战场验证器还已经检查：

- 图是否完全连通。
- 非出生节点是否至少有两个出口。
- 是否有足够循环和至少两条节点不相交路线。
- 是否存在单一必经点。
- 路线是否过度支配其他选择。
- 遮挡—重新暴露节奏、最长连续暴露时间和全图眼位数量。
- 转弯半径、回旋空间和飞行运动学是否可行。

这些数据不应只用于生成验收，也应成为敌机语义导航图和 EDPCG 体验评分的输入。

### 11.3 三层空中导航架构

```text
Director Pressure Intent
  → 按 attackKind 选择上层规划器
      ├─ Suicide Intercept Planner
      │    搜索预测截击时刻、冲刺走廊、掠过点和脱离线
      └─ Ranged Fire-Position Planner
           搜索射程带、有限射界、遮挡出口和换位线
  → Shared Semantic Route Graph
      提供城市区域、语义路线、容量和到达预约
  → Local Corridor Planner
      使用现有 3D 节点图和 A* 解决静态障碍
  → Physics Steering
      连续飞行控制、机群分离、局部避碰和姿态控制
```

两套上层规划器回答不同问题：自爆兵回答“何时从什么角度截获玩家”，远程兵回答“在哪里获得一个公平且有限的射击窗口”。共享语义图负责路线连接和容量，局部规划器回答“如何安全通过建筑”，物理控制负责“这一帧怎样飞”。分层后，局部避障不会意外把一条减压路线重新变成最短追杀路线。

直达通道优化只能在已经选定的语义航段内部使用，不能因为玩家与敌机之间暂时无遮挡，就跳过编队占位、预告、攻击轮次和退出路线。

### 11.4 Path Intent

每次寻路请求必须携带意图，而不是只有终点：

| Path Intent | 适用规划器 | 目的 | 压力作用 |
|---|---|---|---|
| `Ingress` | 两者 | 从已预告入口进入编队集合区 | 建立可读的新压力来源 |
| `FormUp` | 两者 | 形成队形并错开到达时间 | 降低意外同步汇聚 |
| `Probe` | Suicide | 从玩家可见方向进行试探掠过 | 小幅增压，教学性强 |
| `Intercept` | Suicide | 到达玩家预测航线前方 | 快速增压，但必须限制数量 |
| `CommitAttackRun` | Suicide | 进入有限转向的最终冲刺走廊 | 高压，必须持有 Commit 额度和攻击令牌 |
| `BreakAway` | Suicide | 冲刺命中或错过后完成高速掠过与脱离 | 立即释放碰撞压力 |
| `MaskedFlank` | Ranged | 断视线换位后从侧方重新建立射界 | 增加方向压力，必须二次预告 |
| `RangedPerch` | Ranged | 取得有限远程射击窗口 | 增加暴露压力，受射界与令牌限制 |
| `Suppress` | Ranged | 保持玩家对一个方向的关注 | 为另一小队创造策略空间 |
| `BreakLineOfSight` | Ranged | Burst 后进入建筑遮挡并释放火力线 | 快速降低远程压力 |
| `MaskedHold` | Ranged | 在无射界位置等待新的合法火力位置 | 保持存在感但不输出伤害 |
| `BreakContact` | 两者 | 主动拉高、下潜或进入遮挡 | 快速减压并释放令牌 |
| `Regroup` | 两者 | 返回编队集合点 | 延后再次接敌 |
| `Withdraw` | 两者 | 退出主战区或等待回收 | 移除交战与导航压力 |

### 11.5 寻路压力的预测

导航压力是一个 4～8 秒的前瞻指标，但两类敌人不能使用同一套计算：

```text
SuicideNavigationPressure =
    0.30 × TimeToImpactPressure
  + 0.25 × CommittedSuicideCount
  + 0.20 × InterceptAnglePressure
  + 0.15 × EscapeRouteOverlap
  + 0.10 × TelegraphDeficit

RangedNavigationPressure =
    0.30 × ExpectedLineOfSightUptime
  + 0.25 × ActiveFireLaneRatio
  + 0.20 × CrossfireAnglePressure
  + 0.15 × RangeBandPressure
  + 0.10 × ReappearancePressure

NavigationPressureRatio = Clamp01(
    Max(SuicideNavigationPressure, RangedNavigationPressure)
  + 0.35 × Min(SuicideNavigationPressure, RangedNavigationPressure)
  + 0.15 × SharedConvergencePressure)
```

- 自爆兵压力重点是“多久可能撞到、同时有几架进入最终冲刺、玩家是否还有规避方向”。
- 远程兵压力重点是“预计视线保持多久、有几条有效火力线、是否形成交叉火力”。
- `SharedConvergencePressure` 只计算两种规划器的结果是否会在同一时间窗口叠加。
- 合并时不取简单平均，避免一种兵种已经满压却被另一种低压兵种稀释。

路径被编队接受时就预占该压力，避免 Director 认为当前安全而继续派出更多高压路线，几秒后所有敌机同时抵达。

### 11.6 多目标路径评分

A* 不应继续只选择几何最短路径。两套上层规划器共享以下安全成本：

```text
SharedRouteCost =
    TravelTimeCost
  + KinematicCost
  + CollisionRiskCost
  + CongestionCost
  + ReadabilityPenalty
  + RecoveryViolationPenalty
```

自爆兵与远程兵分别在这个基础上增加自己的搜索状态、目标函数和硬约束。最低分路径是最接近目标体验的路径，而不一定是到玩家最快的路径。不同规划器不能关闭恢复区和压力上限等共享硬规则。

建议语义边至少保存：

```text
stableId
routeKind
waypoints
controlVolumeIds
allowedRoles
minimumClearance
minimumTurnRadius
estimatedTravelSeconds
exposureRatio
occlusionSeconds
pressureDelta
capacity
readableReentry
fallbackRouteId
```

当前 `AirCombatFlightRoute` 的 `stableId/kind/width/points` 可以作为基础，其余数据在 PCG 验证阶段烘焙。

### 11.7 寻路如何增加压力

Director 在存在压力空间时可以逐项使用以下手段：

- 缩短一支小队的预计接敌时间，而不是让所有敌机加速。
- 让一架 Interceptor 走预测截击线，迫使玩家改变航向。
- 让 Striker 前往有时间限制的远程射击位置。
- 从一个已预告的新方向重新出现。
- 让两支小队交错到达，而不是随机堆在同一方向。
- 在 Peak 阶段暂时使用 Masked Flank、Pincer 或多高度协同。
- 让敌机占据一个出口附近的威胁位置，但不能封死所有出口。

增压优先改变路径、角度、时序和角色配合，不修改敌机最大速度、转弯能力、伤害或玩家输入。

### 11.8 寻路如何减少压力

当压力接近上限或进入 Recover 时，Director 应下达可信的减压路径，而不是让敌机原地变傻：

- 将即将同时抵达的小队改走较长的集合路线，错开接敌时间。
- 让刚完成攻击的敌机强制交叉脱离并断开视线。
- 将远程敌机移到无射界的重定位路线，立即释放攻击令牌。
- 让外围敌机在远端集合航路保持存在感，但不切入玩家航线。
- 取消尚未开始的侧翼或夹击，把小队送往 Regroup 点。
- 避开玩家当前选择的 VerticalEscape 和 RecoveryPocket 连接路线。
- 降低未来 4 秒内的汇聚数量，而不从任务名册删除敌机。

减压主要通过路线长度、遮挡、到达时序和战术状态实现。除受损或编队需要外，不建议临时降低物理最大速度，以免玩家感受到明显作弊。

### 11.9 自爆兵与远程兵使用两套战术寻路系统

两类敌人只共享：PCG 语义图、航段容量、到达预约、静态通道检测、局部 A*、分离和物理飞行。上层目标生成、搜索状态、评分函数、压力指标和失败回退全部分开。

#### 11.9.1 Suicide Intercept Planner

适用：`attackKind == Suicide` 的 Interceptor。

搜索状态不是普通空间节点，而是：

```text
(SemanticNode, EstimatedArrivalTime, ApproachHeading)
```

它需要在多个未来时刻采样玩家的可达位置，再判断敌机能否以自己的速度、转弯半径和当前航向形成合法截击。目标不是持续追上玩家尾部，而是生成一条完整攻击走廊：

```text
Staging Point
  → Lateral Alignment Point
  → Telegraph Gate
  → Limited-Steering Commit Corridor
  → Impact / High-Speed Pass Point
  → Break-Away Corridor
  → Regroup Point
```

建议评分：

```text
SuicideInterceptScore =
    SharedRouteCost
  + 0.25 × InterceptTimeError
  + 0.20 × TurnFeasibilityCost
  + 0.20 × TelegraphReadabilityCost
  + 0.15 × FriendlyConvergenceCost
  + 0.10 × PlayerEscapeOverlapCost
  + 0.10 × PressureDeviationCost
```

硬规则：

- 最终冲刺前必须有攻击令牌和完整方向预告。
- `Commit Corridor` 必须通过真实碰撞检测，并预留掠过后的脱离路线。
- 进入 Commit 后只允许有限航向修正，不能继续完美追踪玩家每次转向。
- 冲刺错过后不能原地急转再次扑向玩家；必须先完成 Break-Away 和冷却。
- Commit 期间撞上静态建筑不能卡在碰撞体上继续推力。碰撞必须立即产生 `SelfDetonated`，或在玩家诱导归因窗口内产生 `PlayerCausedEnvironment`，随后释放攻击令牌、航段预约和编队占位。
- Commit 轨迹因动态障碍失效时只能提前进入 MissedPass / Break-Away，不能临时改成高转向直追，也不能穿过障碍。
- 两架自爆兵不能预约几乎相同的撞击时刻和接近航向。
- 不得把 RecoveryPocket、VerticalEscape 出口或玩家唯一撤离线作为截击点。
- 找不到合法截击走廊时降级为 `Probe` 或 `Regroup`，不能回退成直线追踪。

自爆兵状态循环：

```text
Forming
  → AcquireIntercept
  → Aligning
  → Telegraphing
  → Committing
  → Impact / MissedPass
  → Disengaging
  → Regrouping
```

压力降低时，自爆兵主要通过延迟截击时刻、改走外侧对齐点、取消 Commit 和延长 Regroup 实现减压。

#### 11.9.2 Ranged Fire-Position Planner

适用：远程 Striker；Gunship 可以共享该规划器的接口，但使用更大的净空、射程和容量约束。

搜索状态是经过 PCG 烘焙的火力位置图：

```text
(FirePositionNode, SightlineBand, EscapeNeighbor)
```

每个候选火力位置必须携带：角色射程适配、预计视线持续时间、射击方向、附近遮挡出口、横移空间、可用撤离邻接点、路线容量和重复使用冷却。目标不是导航到玩家，而是形成有限且可退出的火力窗口：

```text
Masked Reposition Route
  → Fire Position
  → Stabilize
  → Telegraph
  → Limited Burst Window
  → Lateral Break-LOS Route
  → Alternate Position / Regroup
```

建议评分：

```text
RangedPositionScore =
    SharedRouteCost
  + 0.25 × PreferredRangeError
  + 0.20 × LosWindowDeviation
  + 0.15 × MissingOcclusionExitCost
  + 0.15 × CrossfirePressureCost
  + 0.15 × PositionCongestionCost
  + 0.10 × PressureDeviationCost
```

硬规则：

- 沿转移路线飞行、局部避障、紧急规避或重新规划时武器保持 `Safe`。
- 到达火力位置后先稳定姿态并预告，再申请攻击令牌。
- 单次 Burst 结束后必须横移、断视线或更换位置，不能原地连续续杯令牌。
- 玩家切入高楼遮挡后，弹丸发射必须在当帧终止；但短暂断视线不立即重置整套预告、攻击令牌和 Burst 冷却。使用 0.25～0.5 秒遮挡滞后后进入 BreakLineOfSight / Relocate，防止玩家在同一墙角反复探头无限刷新敌机状态。
- 同一墙角连续触发短暂断视线时，应给该火力位置和该压力方向增加冷却，而不是重新免费获得完整 Burst；无真实视线时绝不允许穿墙补射。
- 同一火力位置和同一压力方向都有冷却，避免所有远程兵反复使用最优点。
- 不能用射线穿过 RecoveryPocket，也不能同时封锁低中空换层通道的入口和出口。
- 找不到合法火力位置时进入 `MaskedHold`、`Regroup` 或 `Withdraw`，不能回退成近距离追射。

远程兵状态循环：

```text
Forming
  → SelectFirePosition
  → MaskedReposition
  → Stabilizing
  → Telegraphing
  → BurstFiring
  → BreakLineOfSight
  → Relocating / Regrouping
```

压力降低时，远程兵主要通过缩短 Burst、减少火力方向、延长位置冷却、选择更长的遮挡转移路线和提前 BreakLineOfSight 实现减压。

#### 11.9.3 两套独立压力额度

除总攻击令牌外，Director 还需要分别控制两类寻路承诺：

| 星球档位 | 自爆最终冲刺上限 | 远程有效火力线上限 |
|---:|---:|---:|
| 1～2 | 1 | 1 |
| 3～4 | 普通 1，Peak 2 | 普通 1，Peak 2 |
| 5～6 | 2 | 2 |

组合约束：

- 两架自爆兵进入 Commit 时，普通阶段最多保留一条远程火力线。
- 两条远程火力线同时有效时，最多允许一架自爆兵进入 Commit。
- 两类承诺之和仍受普通 3、Peak 4 的总攻击令牌限制。
- `NavigationPressureRatio > 0.65` 时不再接受新的 Commit 或 Fire Position 激活。
- 自爆兵和远程兵的组合策略只预订一个统一到达窗口，避免两个规划器各自判断安全却在同一秒叠加。

#### 11.9.4 与压力曲线的策略差异

| 策略等级 | 自爆兵规划器 | 远程兵规划器 |
|---:|---|---|
| 0 | 取消 Commit、外侧掠过、Regroup | BreakLineOfSight、MaskedHold、Relocate |
| 1 | 单方向可见 Probe 或一次基础截击 | 单一方向、短 Burst、较长位置冷却 |
| 2 | 预测截击、侧向对齐、错峰冲刺 | 遮挡换位、角色射程位置、单次方向切换 |
| 3 | 最多两架错时、错角度协同截击 | 最多两条可读火力线、交错 Burst，不持续交叉射击 |

压力曲线提高的是规划器可使用的策略和到达协同，不是把自爆兵变成完美制导，也不是让远程兵获得无限视线。

#### 11.9.5 PCG 对两套规划器提供不同数据

自爆兵需要：

- 中央机动街区内的截击入口、掠过点和多个脱离出口。
- 航段的转弯半径、到达时间、冲刺净空和玩家逃生路线重叠度。
- 不会把多架自爆兵导向同一狭窄位置的路线容量。

远程兵需要：

- 少道路高楼掩体区的 Fire Position、Occlusion Exit 和横移路线。
- 每个位置的射程带、视线持续时间、压力方向和断视线邻接点。
- 开放火力捷径周边的有限远程位置，但不能直接占据玩家路线中心。

如果 PCG 只输出通用 Waypoint，两套规划器最终仍会退化成不同兵种沿同一路线追玩家，因此这些数据必须在候选生成和功能验证阶段烘焙。

### 11.10 城市区域的寻路公平规则

- `RecoveryPocket` 内部和保护边界不允许作为敌机攻击目标或捷径。
- 玩家进入 RecoveryPocket 后，追击者在连接出口外执行 Hold、BreakContact 或 Regroup。
- `MaskedFlank` 的重新出现点必须具备 HUD、声音或航迹预告，不能把断视线当成免费偷袭。
- `ExposureShortcut` 可以提高 Striker 路径权重，但不能同时安排 Pincer 和 Gunship 封口。
- `VerticalEscape` 是高导航压力后的优先减压路线，Recover 阶段禁止敌机抢占。
- 任一路线达到容量后，后续敌机必须排队、改道或 Regroup，不能在狭窄航路互相挤压。

### 11.11 路线容量、到达预约与 64 架规模

最高难度 64 架会放大同步寻路和汇聚问题，因此需要路径预约：

- 每条语义路线根据宽度、转弯半径和敌机尺寸计算 `capacity`。
- 编队进入路线前预订航段和预计到达窗口。
- 每条预约包含 `reservationId`、`ownerRosterMemberId`、航段、进入/离开时刻、优先级和 `expiresAt`，不能只增加一个永不回收的计数器。
- 预约租约建议为 2～4 秒，只能由实际路径进度续租；Destroy、Evasive 改道、Regroup、Withdraw、LOD 切换和规划取消都必须在同一事务中释放预约。
- 同一路口的编队到达时间至少错开 1.5～3 秒。
- 普通阶段未来 4 秒内最多允许 3 个高压接敌承诺，Peak 最多 4 个。
- 远处 Ingress 使用参数化航路或低频战术模拟，不运行完整局部 A*。
- 只有 12～16 架完整模拟敌机执行高频局部规划，其余敌机沿语义航段低频推进。

路径请求按以下优先级排队：

```text
紧急避碰 / Evasive
  > BreakContact / Disengaging
  > 当前攻击策略的局部修路
  > Positioning / Regroup
  > 远端 Ingress
```

预约采用“无持有等待”规则：低容量连续航段要么一次性获得完整窗口，要么释放已有预约后等待或改道，不能占着第一段等待第二段。紧急规避和脱离优先，普通请求按等待时间进行 aging，防止远端编队永久饥饿；超过最大等待时间后必须改走备用路线或 Regroup。双向窄路还需由稳定 ID 和预约时刻确定单向通行权，保证同 Seed 可复现且不会迎头死锁。

语义重规划建议每 1.5～3 秒并加入确定性抖动；局部路径有效性可以更频繁检查，但不要让 28 架敌机每 0.5 秒同时执行完整 A*。

### 11.12 重算、失败与防抖

只有以下情况触发语义重规划：

- 当前航段失效或被动态障碍长时间封锁。
- 单机卡住、偏离航路或连续避碰无法恢复。
- Encounter 阶段变化要求立即减压。
- 编队策略取消、目标区域失效或路线容量发生冲突。
- 玩家已经离开该策略可合理覆盖的区域。

路径至少承诺 2～4 秒；普通目标位置变化只由局部跟随处理，不重选整条语义路线。`nextReplanAt` 与 `progressSampleAt` 必须是两套独立计时，重规划不能重置卡住检测：连续 2 秒无有效进度先做局部修路，5 秒仍无进展进入 Evasive / 备用路线，8 秒仍无法恢复则执行 `NavigationRecovery`，以同一名册成员安全重入，且不得算作解决或奖励。

路径搜索失败使用 0.25、0.5、1、2 秒上限退避，不能空路径时逐帧申请 A*。所有候选路径必须验证“当前位置到首节点”“每条图边”“末节点到最终目标”三部分真实净空；缺少首段或末段连接时路径无效。连续失败时按“备用路线 → Regroup → Withdraw → NavigationRecovery”降级；等待期间制动、保持最后合法航段或飞向安全 Hold 点，绝不能把战术目标作为无碰撞检查的直飞回退。

### 11.13 寻路验收指标

- 路径分配后预测压力不会越过当前阶段硬上限。
- 进入 Recover 后 1 秒内停止新增高压路径承诺。
- 高导航压力区域会自动减少交战人数或攻击令牌。
- 玩家飞行能力损失约 30% 时仍有一条可在 8 秒内到达的减压路线。
- 不发生多支编队无预约地同时汇聚同一狭窄航路。
- 敌机攻击后都能观测到明确的脱离和冷却阶段。
- 除最终授权冲刺外，不出现长时间把玩家当前位置作为唯一终点的直线追射。
- 相同 Seed、相同玩家轨迹和相同压力采样产生相同的语义路径选择。

---

## 12. 三类敌机如何消费城市

### 12.1 Interceptor

职责：迫使玩家移动和改变航向。

- 固定使用 `Suicide Intercept Planner`，不请求远程火力位置。
- 优先使用 `Suicide` 类型入口。
- 优先经过 `ManeuverBowl` 和宽航路。
- 没有攻击令牌时只进行追踪、拦截和假冲锋。
- 不在 `RecoveryPocket` 内部生成。
- 玩家处于高导航压力区域时，减少最终冲撞数量。

### 12.2 Striker

职责：制造远程射线问题，迫使玩家主动利用遮挡。

- 固定使用 `Ranged Fire-Position Planner`，不请求预测撞击走廊。
- 优先使用 `Ranged` 类型入口。
- 优先选择 `ExposureShortcut` 边缘、`OcclusionChain` 出口和任务目标外围。
- 保持设计射程，不应永远环绕玩家当前位置。
- 失去真实视线后停止射击并重新寻找位置。
- 玩家处于狭窄或连续转弯区域时，降低同时开火数量。
- 普通阶段最多两架同时攻击。

### 12.3 Gunship

职责：构成阶段性重型威胁。

- 使用远程火力位置接口，但采用独立的大型净空、慢速锚点和射程参数。
- 只进入开阔机动区、任务中心或高潮阶段。
- 同时最多一架。
- 不将恢复区作为直接攻击目标。
- 地形导航压力较高时保留追踪，但减少主攻击窗口。
- Gunship 的难度主要来自位置和护航组合，不扩大血量倍率。

---

## 13. 城市战术区域与 Director 规则

### 13.1 六类区域的玩家体验用途

| 区域 | 主要用途 | 什么时候有价值 |
|---|---|---|
| 中央机动街区 | 提供完整转弯、刹车、重新瞄准和换方向的空间，并保留多个出口 | 被近战敌机追击、飞船高速冲过目标、需要掉头重新接敌时 |
| 少道路高楼掩体区 | 用连续错位高楼多次切断视线，再从另一侧重新出现 | 对付远程敌机的射击和锁定；重点是连续穿过多段掩体，不是躲在一栋楼后不动 |
| 开放火力捷径 | 路线更短、更直、更容易高速通过，但会长时间暴露 | 限时目标、抢占设施、拦截敌人或赶在敌人前面时；如果任务只是慢慢清怪，它的价值很弱 |
| 维修庭院 | 三面或多面遮挡，内部有制动和姿态稳定空间，并保留两个撤离出口 | 飞船受损、需要完成 10 秒自我修复、角速度过高或模块损坏后需要重新稳定时 |
| 低中空换层通道 | 让玩家从低空快速换到中空，或反向俯冲脱离，不鼓励飞到城市上方 | 被同高度敌人封锁、前方道路受阻、受损飞船需要换一条逃生路线时 |
| 出生安全空域 | 给玩家进入关卡后确认方向、恢复控制和观察城市轮廓的空间 | 防止出生即被集火或撞楼；它是开场缓冲，不是长期作战区 |

这张表定义的是区域的“体验功能”，而不是外观主题。同一种建筑组合只有在玩家动作、AI 反应和任务目标三者都支持对应用途时，才算生成了有效区域。

### 13.2 区域语义、几何契约与 AI 反应

| 中文区域 | PCG 语义映射 | 关键几何契约 | Director 与 AI 规则 |
|---|---|---|---|
| 中央机动街区 | `ManeuverBowl` | 有效直径建议不低于设计转弯半径的 2.8 倍；至少 3 个可读出口；中心不放无法预判的高障碍 | 为 Suicide Planner 烘焙对齐点、冲刺走廊和脱离出口；禁止多个方向同时封口 |
| 少道路高楼掩体区 | `OcclusionChain` + `OcclusionGate` + `MaskedFlank` | 形成连续错位的高楼序列；至少测得 4 次有效视线切断；路线本身保持可飞、可读且不形成死路 | 为 Ranged Planner 烘焙火力位置、视线窗口和断视线邻接点；Striker 失去视线后停止射击并换位 |
| 开放火力捷径 | `ExposureShortcut` + `ExposureLane` + `LongRange` | 相比安全路线节省约 20%～35% 时间；保持高速通过宽度；连续暴露目标先以 5～9 秒作为调参起点 | Ranged Planner 可以使用边缘火力位置但不能占据路线中心；Suicide Planner 不得把整条捷径变成无出口撞击走廊 |
| 维修庭院 | `RecoveryPocket` | 三面以上形成真实遮挡；内部可完成制动和姿态稳定；至少 2 个相互独立出口；容纳受损飞船完成 10 秒修复 | Suicide Planner 禁止生成内部截击点；Ranged Planner 禁止生成穿透火力线；攻击令牌 0～1 |
| 低中空换层通道 | `VerticalEscape` | 低空与中空节点连续可达；爬升角、转弯半径和净空适配受损飞船；出口重新接回城市内部而不是屋顶自由飞行 | 玩家换层时暂停新增夹击；同高度追击者错开进入；Recover 阶段禁止敌机抢占出口 |
| 出生安全空域 | `SpawnBasin` | 出生朝向可读；4～6 秒内没有直接碰撞威胁；至少 2 个离场方向；从主要敌机入口无法立即形成完整火力线 | Preview 期间禁止攻击和高压寻路承诺；只允许远处航迹、声音和方向预告；玩家离开后才进入正常节奏 |

表中的比例和秒数属于第一轮隔离试玩基准，应由飞船实际包络、速度、转弯半径和受损能力验证后冻结，不能仅凭建筑美术尺寸决定。

### 13.3 按任务决定区域是否值得生成

每个任务先生成 `TacticalAreaRequirementSet`，将区域标记为 `Required`、`Optional` 或 `Disabled`：

| 任务或遭遇条件 | 必需区域 | 条件区域 |
|---|---|---|
| 所有关卡 | 出生安全空域、中央机动街区、至少一组掩体链、至少一条低中空换层通道 | 维修庭院由玩家损伤与修复系统是否启用决定；本方案默认启用 |
| Clearance / Horde | 中央机动街区、少道路高楼掩体区、维修庭院、换层通道 | 单纯清怪时降低开放捷径权重 |
| Facility Assault | 中央机动街区、掩体区、维修庭院、开放火力捷径 | 捷径必须连接抢占或限时收益 |
| 无时间、无抢点、无拦截目标 | 不强制开放火力捷径 | 若保留，必须提供资源、位置或敌机截击上的明确收益 |

区域价值建议用以下模型评估：

```text
AreaFunctionalValue =
    ContextRelevance
  × PlayerActionSupport
  × AiResponseSupport
  × Reachability
  × Readability
```

任一因子接近 0，这个区域就是“看起来像战术区，但实际没有玩法功能”。Required 区域低于阈值时直接拒绝该 Seed；Optional 区域低于阈值时不计入路线多样性和战术机会评分。

### 13.4 区域必须组成有意义的网络

六类区域不能成为彼此孤立的打卡点。推荐基础连接：

```text
出生安全空域
  → 中央机动街区
      ├─ 安全但较长：少道路高楼掩体区
      ├─ 快速但暴露：开放火力捷径
      ├─ 受损恢复：维修庭院
      └─ 高度脱离：低中空换层通道
             → 重新接回中央街区或任务目标
```

网络规则：

- 中央机动街区承担重新选择路线的枢纽功能，但不能成为唯一必经的单点故障。
- 掩体路线和开放捷径最好连接同一目标，形成“时间收益换暴露压力”的真实选择。
- 维修庭院靠近高压网络但不位于必经主路上，从高压区以受损速度约 8 秒内可达。
- 换层通道必须重新进入城市战术网络，不能直接把最佳解变成飞到所有建筑上方。
- 出生安全空域离开后不应成为最优长期战斗位置；若玩家返回，只获得普通地形保护，不重复开场安全窗口。

### 13.5 PCG 生成顺序

城市必须围绕体验功能生成，不能先铺满道路和建筑，再从剩余缝隙中寻找战术区域：

```text
Experience Profile + Mission + Player Envelope
  → TacticalAreaRequirementSet
  → 放置功能空域和任务锚点
  → 建立安全、快速、换层和恢复连接
  → 生成道路骨架与街区边界
  → 围绕受保护空域生成建筑、连廊和遮挡结构
  → 烘焙玩家与敌机语义导航图
  → 快速状态机/路径仿真
  → 功能验证与体验评分
  → 接受、局部修复或更换候选 Seed
```

具体顺序：

1. 读取玩家正常和受损飞行包络、设计转弯半径和修复时长。
2. 根据任务生成 Required/Optional/Disabled 区域集合。
3. 先放置出生安全空域、任务目标和中央机动街区，保证基础方向与首次接敌时间。
4. 在高压节点附近放置维修庭院，并验证受损飞船约 8 秒内可达。
5. 为同一目标配对生成安全掩体路线和开放捷径；没有实际时间收益时取消捷径的战术标签。
6. 生成低中空换层通道，并封闭会把玩家直接导向城市上方的无代价出口。
7. 道路、楼群和连廊围绕上述空域生长，禁止侵入已验证的转弯、修复、换层和撤离包络。
8. 为 Suicide Planner 烘焙截击入口、对齐点、冲刺净空、预计到达时间、掠过点和脱离出口。
9. 为 Ranged Planner 烘焙火力位置、射程带、视线窗口、遮挡出口、换位邻接点和位置冷却。
10. 分别仿真自爆截击与远程换位状态机，再计算组合 NavigationPressure；硬约束失败则局部修复或拒绝 Seed。

局部修复只能移动未被玩家看到的生成候选内容，且发生在进入关卡前。运行时 Director 不修改已经成立的城市几何。

### 13.6 六类区域与可选峰值修饰的压力参考

| 中文区域 | 语义 ID | 预计压力上界 | Director 规则 |
|---|---|---:|---|
| 出生安全空域 | `SpawnBasin` | 0.00～0.03 | Preview 期间不发攻击令牌，不安排截击路径 |
| 维修庭院 | `RecoveryPocket` | 0.05 | 暂停补兵，攻击令牌 0～1 |
| 中央机动街区 | `ManeuverBowl` | 0.12 | 允许普通混合编队，但保留多个出口 |
| 少道路高楼掩体区 | `OcclusionChain` | 0.16 | 最多一个远程压力方向，断视线后释放对应火力 |
| 低中空换层通道 | `VerticalEscape` | 0.20 | 不在上下切换时追加夹击或抢占出口 |
| 开放火力捷径 | `ExposureShortcut` | 0.28 | 允许远程压力，禁止同时叠加重型夹击 |
| 可选高潮叠加区 | `DangerPlaza` | 0.30 | 不是独立必需区域；只作为短暂 Peak 修饰，必须保证退出方向 |

### 13.7 敌人可用压力预算

```text
EnemyPressureBudget = Max(
    0,
    TargetPressure
    - MeasuredEnvironmentPressure
    - CommittedNavigationPressure
    - PlayerDamageAssist)
```

表中的数值只用于 PCG 生成期估算和硬上界，不能因为玩家的坐标刚进入区域就完整扣除。运行时 `MeasuredEnvironmentPressure` 应按真实视线暴露时间、有效逃生方向、转弯/制动负荷、碰撞险情和飞船受损状态测量，并使用 1～2 秒 EMA、进入停留时间和退出滞后。玩家悬停在开放捷径边缘、实际仍使用旁边掩体时，不得获得 0.28 的敌人减压收益。

地形实际越困难、已经在路上的高压策略越多，当前允许追加的敌人输出越低。`CommittedNavigationPressure` 在策略路径被接受时预占，在敌机脱离、重组、撤退、租约到期或路径取消时释放。

所有战术区域使用统一的运行时触发状态，禁止用单帧 Trigger Enter/Exit 直接重置压力或恢复：

```text
Outside → EnterPending → Active → ExitPending → Cooldown → Outside
```

- 进入持续 0.75～1 秒才变为 Active，离开持续 1～1.5 秒才正式退出，短暂擦边不会来回切换。
- `RecoveryPocket` 的安全窗口每次使用后冷却 35～45 秒，并要求玩家确实离开区域且经历正常交战后才可重新武装。
- `VerticalEscape` 每次完整穿越只提供一次换层减压；需要离开通道并重新进入真实交战后才能再次获得。
- `SpawnBasin` 的开场保护是任务级一次性消耗状态，玩家返回时只能获得普通几何遮挡。

### 13.8 局部难度预算

每个局部区域继续使用：

```text
navigation
enemies
exposure
resourceDenial
```

第一版约束建议：

- 四项总和不得超过 `2.1`。
- 同一区域最多两项高于 `0.65`。
- `navigation > 0.70` 时，`enemies <= 0.50`。
- `navigation > 0.70` 时，普通阶段最多保留 2 个攻击令牌。
- `exposure > 0.70` 时，最多一个主要远程方向。
- `enemies > 0.70` 时，必须存在 8 秒内可达的减压路线。
- 未来 4 秒已有 3 支高压路径汇聚时，不得继续安排截击、侧翼或夹击。
- 连续高压区域之后，下一连接节点必须提高恢复权重。

---

## 14. RecoveryPocket 公平契约

恢复口袋必须通过真实城市几何产生安全性，而不是无敌区。运行时把它分为内部 `RepairCore` 与外围 `ExitContestRing`：前者只服务姿态稳定和一次完整维修，后者负责恢复正常交战但永远保留至少一个可用出口。

玩家进入后：

- `safeWindowSeconds` 建议覆盖一次 10 秒维修；窗口内禁止生成直接指向该区域的新敌人。
- 攻击令牌限制为 0～1。
- 远程敌人必须服从建筑视线遮挡。
- 附近局部敌机数量上限为 2～3。
- 外围敌机继续存在，但进入 Regrouping 或 Positioning。
- 玩家可以减速、恢复姿态和执行自修复。
- `RepairCore` 内不推进占点、计时收益或需要持续射击的任务目标，且不得形成玩家能安全向外持续刷伤害的固定射界。

防止长期驻留：

- 不能在恢复区内部刷怪。
- 安全窗口结束后不在核心区内刷新保护，也不把敌人漏斗式送到同一出口；外围敌机在遮挡外占位，先从 `ExitContestRing` 给出可读预警。
- 可以让一架 Interceptor 在一个出口外制造移动压力，但必须保留另一个安全出口；不能朝核心区穿墙攻击。
- 不能同时增加 Striker 远程压力和夹击方向。
- 玩家长期停留不会获得第二次维修、安全窗口或任务进度；离开核心进入外围环后才逐步恢复正常压力。
- 只有玩家离开足够距离、经过 35～45 秒正常交战并完成区域 Cooldown 后，下一次进入才可重新提供恢复窗口。

---

## 15. PCG 入口与出生规则

`AirCombatEnemyIngress` 的完整数据必须传给 Director：

- `stableId`
- `position`
- `target`
- `kind`
- `warningSeconds`
- 对应航路
- 连接的战术机会
- 入口危险方向
- 冷却状态

合法出生必须同时满足：

1. 不在玩家直接视野内突然出现。
2. 与玩家距离满足当前模式最小安全距离。
3. 不与建筑或其他敌机碰撞重叠。
4. 从入口到目标区域存在合法路径。
5. 不直接穿过 RecoveryPocket。
6. 入口类型与敌机角色匹配。
7. 当前入口未处于冷却。
8. 当前方向不会突破压力方向上限。
9. Population、Engagement 和 Attack 三类预算均允许。

入口选择优先级：

```text
角色类型匹配
  → 当前城市区域适配
  → 压力方向需求
  → 玩家视野外出生
  → 路径连通
  → 入口冷却与重复惩罚
```

每个待生成对象绑定稳定的 `rosterMemberId`，对象池实例 ID 不能替代名册 ID。单个入口最多尝试 8 次，失败后必须保留队列项并切换到下一个合法备用入口；所有入口都失败时进入带退避的 `SpawnRecovery`、输出失败原因并等待 PCG/碰撞条件恢复，不能静默删除队列成员，也不能在同一帧反复尝试。只有成功创建并绑定名册成员后才能递增 `SpawnedRosterCount`。

---

## 16. 编队规则

推荐基础编队：

### Interceptor Swarm

```text
3～6 Interceptor
```

- 制造数量感和移动压力。
- 同时只有少量成员获得冲撞令牌。

### Striker Fire Team

```text
2 Striker
```

- 分别寻找两个互不重叠的射击位置。
- 不允许同时形成三个以上远程射线方向。

### Escort Group

```text
1 Striker + 3 Interceptor
```

- Interceptor 迫使玩家改变方向。
- Striker 利用重新暴露窗口射击。

### Gunship Command Group

```text
1 Gunship + 4 Interceptor
```

- 只用于高潮或高档星球。
- Gunship 出现时限制其他远程攻击令牌。

### Delayed Pincer

```text
左侧 3架
延迟 3～5秒
右侧 3架
```

- 两侧不能同时无预警出现。
- 如果第一侧已经使压力超过目标，第二侧继续等待。

---

## 17. 动态调节边界

### 17.1 采样信号

可以使用：

- `VehicleStructureGraph.OverallHealthRatio`
- `VehicleStructureGraph.ConnectedCpuRatio`
- `VehicleStructureGraph.CapabilityState`
- 最近 10～15 秒玩家承受的伤害
- 最近击杀速度和输出伤害
- 碰撞频率
- 长时间低速、失速或刹停失败
- 路径重算失败
- 未来 4～8 秒的预计接敌数量与到达时间分布
- 高压路径承诺、路线容量和新方向重现数量
- 玩家可达减压路线的数量与预计到达时间
- 连续受压时间
- RecoveryPocket 的成功使用情况

### 17.2 平滑规则

```text
评估间隔：5秒
统计窗口：15秒 EMA
滞后阈值：0.08
决策冷却：10～15秒
最大帮助：-12%
最大加强：+8%
```

### 17.3 玩家压力过高时

- 下一小队延迟 20%～30%。
- 减少一个攻击令牌。
- 暂停第二或第三压力方向。
- 将 Gunship 组合降为普通编队。
- 让部分外围敌机进入 Regrouping。
- 取消尚未执行的 Intercept、MaskedFlank 或 Pincer 路径。
- 命令已攻击敌机走 BreakContact 路径并断开视线。
- 提前进入恢复阶段。
- 不减少该关固定敌机名册总数。

### 17.4 玩家明显过强时

- 优先改变角色组合。
- 激活一个额外合法方向。
- 缩短下一批次的等待时间，但不跳过预告。
- 在压力空间允许时逐级提高策略等级和战术选位质量。
- 优先增加一次可读的截击、单侧翼或交错到达，而不是增加血量或伤害。
- 不临时克制玩家正在成功使用的路线。

### 17.5 禁止事项

- 不修改已经生成并被玩家看到的城市几何。
- 不关闭玩家当前正在使用的合法出口。
- 不在玩家身后凭空生成敌机。
- 不动态提高现存敌机的血量和伤害。
- 不因玩家进入恢复区而直接生成克制单位。
- 不让所有敌机同时把玩家当前位置设为寻路终点。
- 不用无预告的最短路径穿过遮挡，从玩家身后重新出现。
- 不每几秒反向调节，造成明显橡皮筋体验。

---

## 18. 运行时数据契约建议

以下仅表达接口职责，不是最终代码。

```csharp
public sealed class EdpcgEncounterContext
{
    public string planetId;
    public string missionId;
    public int planetDifficultyTier;
    public int missionSeed;
    public int rosterCount;

    public CombatCityDifficultyProfile difficulty;
    public CityTacticalRuntimeMap tacticalMap;
    public PlayerCombatCapability playerCapability;
}
```

```csharp
public enum EdpcgAreaFunction
{
    SpawnSafeAirspace,
    CentralManeuverDistrict,
    HighRiseOcclusionChain,
    ExposedFireShortcut,
    RepairCourtyard,
    LowMidVerticalTransition
}

public enum TacticalAreaRequirementMode
{
    Required,
    Optional,
    Disabled
}

public sealed class TacticalAreaRequirement
{
    public EdpcgAreaFunction function;
    public TacticalAreaRequirementMode mode;
    public float selectionWeight;
    public int minimumExitCount;
    public float requiredActionSeconds;
    public float minimumPlayerClearance;
}
```

```csharp
public sealed class CityTacticalRuntimeMap
{
    public IReadOnlyList<RuntimeIngress> ingresses;
    public IReadOnlyList<RuntimeOpportunity> opportunities;
    public IReadOnlyList<RuntimeFlightRoute> routes;
    public IReadOnlyList<RuntimeTacticalArea> functionalAreas;

    public RuntimeOpportunity FindOpportunity(Vector3 worldPosition);
    public IReadOnlyList<RuntimeIngress> FindEligibleIngresses(
        HordeEnemyRole role,
        Vector3 playerPosition,
        DirectorPressureState pressure);
    public bool TryFindRoleDestination(
        HordeEnemyRole role,
        RuntimeOpportunity playerOpportunity,
        out Vector3 destination);
}
```

`RuntimeTacticalArea` 除世界坐标和区域类型外，还要携带：功能验证结果、入口和出口、连接路线、玩家包络可行性、预计动作时间、局部压力、允许 AI 策略以及任务相关度。Director 只消费已经通过功能验证的区域。

```csharp
public struct DirectorPressureSnapshot
{
    public float actualPressure;
    public float targetPressure;
    public float measuredEnvironmentPressure;
    public float navigationPressure;
    public float suicideNavigationPressure;
    public float rangedNavigationPressure;
    public float committedNavigationPressure;
    public float playerStrain;

    public int populationCount;
    public int engagementCount;
    public int attackTokensUsed;
    public int pressureDirections;
}
```

Difficulty Lab 不应直接读取散落在各系统中的临时字段，而应消费稳定的采样帧与事件流：

```csharp
public struct EdpcgDifficultyLabFrame
{
    public double missionTime;
    public string encounterProfileVersion;
    public int planetDifficultyTier;
    public int citySeed;
    public DirectorEncounterPhase phase;

    public float targetPressureMin;
    public float targetPressureMax;
    public float actualPressure;
    public float forecastPressure4s;
    public float forecastPressure8s;
    public float measuredEnvironmentPressure;
    public float suicideNavigationPressure;
    public float rangedNavigationPressure;
    public float playerStrain;

    public int unspawnedCount;
    public int activeCount;
    public int engagementCount;
    public int attackTokensUsed;
    public int suicideCommitCount;
    public int rangedFireLaneCount;
    public string activeTacticalAreaId;
}

public struct EdpcgDifficultyLabEvent
{
    public double missionTime;
    public string stableEventId;
    public string rosterMemberId;
    public string squadId;
    public string areaId;
    public string routeId;
    public EdpcgTelemetryEventKind kind;
    public string reasonCode;
    public float pressureDelta;
}

public enum RuntimeTuningApplyPolicy
{
    ApplyNow,
    ApplyAtSafeBoundary,
    RequiresRestart
}

public enum RuntimeTuningChangeState
{
    Draft,
    Pending,
    Applied,
    Reverted,
    Cancelled
}

public struct RuntimeTuningChange
{
    public string changeId;
    public string fieldPath;
    public string previousSerializedValue;
    public string candidateSerializedValue;
    public double submittedAt;
    public double appliedAt;
    public RuntimeTuningApplyPolicy applyPolicy;
    public RuntimeTuningChangeState state;
    public string safeBoundaryReason;
}
```

连续曲线建议以固定 5～10Hz 记录，攻击令牌、状态切换、路径预约、出生、伤害、结算和实时参数修改等关键事件按发生时完整记录。采样帧、事件和 `RuntimeTuningChange` 都携带 Profile、Seed 与公式版本，使 Play Mode、离线回放和导出报告能够对齐。回放实时调节 Session 时必须按原时间和安全边界重放修改序列。

```csharp
public enum EnemyResolutionReason
{
    KilledByPlayer,
    PlayerCausedEnvironment,
    SelfDetonated,
    NavigationRecovery,
    TechnicalFinalClear
}

public sealed class EnemyRosterMemberState
{
    public string rosterMemberId;
    public HordeEnemyRole role;
    public float health;
    public int spawnAttempts;
    public EnemyResolutionReason lastTransitionReason;
    public bool isResolved;
    public bool isCreditedKill;
    public bool rewardGranted;
}

public enum TacticalAreaRuntimeState
{
    Outside,
    EnterPending,
    Active,
    ExitPending,
    Cooldown
}

public struct RouteReservation
{
    public string reservationId;
    public string ownerRosterMemberId;
    public string edgeId;
    public float enterAt;
    public float exitAt;
    public float expiresAt;
    public int priority;
}
```

名册结算接口必须按 `rosterMemberId` 幂等：重复死亡回调、对象池重复回收、碰撞与伤害同帧触发时只接受第一个终态。`NavigationRecovery` 是迁移状态而非解决终态。区域和路线预约也要保存版本或租约标识，迟到的异步路径结果不能覆盖敌机已经切换后的策略。

```csharp
public enum EdpcgNavigationPlannerKind
{
    SuicideIntercept,
    RangedFirePosition
}

public enum EdpcgPathIntent
{
    Ingress,
    FormUp,
    Probe,
    Intercept,
    CommitAttackRun,
    BreakAway,
    MaskedFlank,
    RangedPerch,
    Suppress,
    BreakLineOfSight,
    MaskedHold,
    BreakContact,
    Regroup,
    Withdraw
}

public sealed class RuntimeSemanticRoute
{
    public string stableId;
    public AirCombatRouteKind kind;
    public Vector3[] waypoints;
    public HordeEnemyRole[] allowedRoles;
    public float minimumClearance;
    public float minimumTurnRadius;
    public float estimatedTravelSeconds;
    public float exposureRatio;
    public float pressureDelta;
    public int capacity;
    public bool readableReentry;
    public string fallbackRouteId;
}

public struct EdpcgPathRequest
{
    public int enemyId;
    public string rosterMemberId;
    public int squadId;
    public EdpcgNavigationPlannerKind plannerKind;
    public EdpcgPathIntent intent;
    public string destinationOpportunityId;
    public float desiredArrivalTime;
    public float pressureBudget;
    public int strategyLevel;
}
```

```csharp
public struct DirectorPopulationBudget
{
    public int rosterRemaining;
    public int populationCap;
    public int engagementCap;
    public int attackTokenCap;
    public int suicideCommitCap;
    public int rangedFireLaneCap;
    public int pressureDirectionCap;
    public int gunshipCap;
}
```

### 18.1 职责分配

`AirCombatCityPcg` / `CombatCityPcgDesignProfile`

- 根据 `TacticalAreaRequirementSet` 生成 Required 区域，并按权重尝试 Optional 区域。
- 把六类体验功能落实为实际战术体积、建筑结构、路线和连接关系。
- 不为 Disabled 或任务无价值的区域增加战术多样性评分。

`CombatMapValidatorV2` / EDPCG Experience Evaluator

- 继续检查连通、出口、循环、视线节奏、转弯半径和路线支配性。
- 增加六类区域的功能测试，包括完整转弯、连续断视线、捷径收益、10 秒维修和受损换层。
- Required 区域功能不成立时拒绝 Seed，而不是只给警告。

`FinitePlanetUrbanCombatRuntime`

- 将 `AirCombatCityPlan` 转换为世界坐标运行时数据。
- 暴露经过验证的区域功能、入口、航路、连接关系和局部压力查询。
- 不决定波次和敌人数量。

`FinitePlanetHordeCombatController`

- 创建 `EdpcgEncounterContext`。
- 传入任务、星球、Seed、玩家能力和城市数据。
- 管理任务完成、奖励和最终清场。

`HordeCombatDirector`

- 管理名册、人口、交战和攻击预算。
- 计算目标压力与实际压力。
- 选择编队、入口、目标区域、策略等级、Path Intent 和节奏阶段。
- 根据 `attackKind` 把战术请求派发给 Suicide 或 Ranged 规划器，并分别管理 Commit 与 Fire Lane 额度。
- 在接受路径前预测并预占导航压力。
- 压力超限时下达 BreakContact、Regroup 或 Withdraw，而不是只停止刷新。

`HordeEnemyVehicle`

- 运行单机战斗状态机并执行 Director 指定的编队策略和 Path Intent。
- 将导航、武器和紧急规避状态分离；运输和脱离过程中不自行开火。
- 报告攻击、损伤、死亡、状态转换、脱离和路径失败。
- 不自行决定全局难度。

`SuicideInterceptPlanner`

- 在带时间与接近航向的状态空间内搜索预测截击走廊。
- 输出 Telegraph Gate、Commit Corridor、Pass Point、Break-Away 和 Regroup 路线。
- 找不到合法截击时返回 Probe/Regroup，不返回玩家当前位置直线路径。

`RangedFirePositionPlanner`

- 在 PCG 烘焙的火力位置图中搜索射程带、有限视线窗口和遮挡撤离点。
- 输出 Masked Reposition、Fire Position、BreakLineOfSight 和 Alternate Position。
- 找不到合法射界时返回 MaskedHold/Regroup，不返回追踪射击路径。

`HordeAirNavigationService`

- 为两套上层规划器提供 PCG 语义图、航段容量、到达预约和路径查询。
- 在选中的语义航段内使用现有局部 A* 和真实碰撞走廊检查。
- 使用现有通用环形导航作为回退。
- 不替上层决定截击目标或远程射击位置。
- 提供路径预约、预计到达时间和压力增量给 Director。

---

## 19. 星球难度身份

### 19.1 起始六星系统

起始系统可以直接使用稳定的 `orbitIndex` 作为 0～5 难度档，而不是 UI 当前列表序号。

### 19.2 跨星系扩展

建议为星球生成并冻结：

```text
systemDangerBand
planetThreatTier
encounterProfileId
```

可以由以下因素确定：

- 星系距离起点的危险带。
- 星球在星系中的轨道序号。
- 主线章节进度。
- 星球气候或特殊规则。

该结果写入冻结后的星球定义，保证重访同一星球时难度身份不变。

动态 Director 只能在该档位允许的小范围内变化，不能把第六颗星球自动降成第一颗星球。

---

## 20. 任务类型接入

### 20.1 Clearance

- 整关敌机名册数量由星球档位决定。
- 敌机在多个城市机会之间占位，避免玩家停在出生点等待所有敌机飞来。
- 远程敌机守住战术位置，Interceptor 负责推动玩家移动。
- 中央机动街区、掩体区、维修庭院和换层通道构成基础循环。
- 没有计时、抢点或截击收益时，开放火力捷径只作为 Optional，不提高主要战术评分。
- 任务完成依据名册解决与最终清场。

### 20.2 Facility Assault

- 总名册分配到多个设施目标，但不强制固定处理顺序。
- 每个设施绑定不同的主要压力类型。
- 摧毁设施后可以关闭一个入口、减少一种压力或打开新路线。
- 设施附近的局部预算独立计算，防止目标、地形和敌人同时满压。
- 开放火力捷径连接玩家出生侧与设施争夺区，用实际抵达时间收益抵偿暴露风险。
- 掩体路线与开放捷径尽量到达同一设施，形成安全路线和快速路线的可比较选择。

---

## 21. 确定性与存档

基础生成必须由以下输入决定：

```text
worldSeed
planetId
missionId
planetDifficultyTier
missionSeed
encounterProfileVersion
```

同一输入必须稳定产生：

- 城市布局。
- 64 架敌机名册及角色构成。
- 基础批次划分。
- 候选入口顺序。
- 编队随机序列。

动态调节可以读取实时玩家状态，但应保证：

- 同一玩家遥测序列产生相同决策。
- 所有决策记录时间、原因、前后预算和所选入口。
- 配置版本变化后能明确区分旧存档与新规则。

若未来支持任务中途保存，最少保存：

- 已生成名册数量。
- 每个名册成员的稳定 ID、生命、出生/导航恢复状态、解决原因、有效击杀和奖励发放状态。
- 当前批次和节奏阶段。
- 当前活动敌机稳定 ID。
- 入口冷却。
- 尚未到期的路线预约及其租约版本。
- 战术区域运行时状态、已消费窗口和恢复冷却。
- Director 随机状态。
- 当前平滑压力状态。

---

## 22. 性能分级

最高难度不预创建 64 个完整战斗实体。建议对象池容量为 32～36，通过回收完成 64 架整关名册。

### 22.1 三级模拟

#### Full Combat Simulation

数量：最近 12～16 架。

- 完整 Rigidbody。
- 完整碰撞。
- 武器和攻击状态机。
- 高频路径与避障。
- 完整受伤和死亡反馈。

#### Tactical Simulation

数量：约 6～10 架。

- 保留可见模型。
- 低频导航，建议 5～10Hz。
- 不执行完整武器逻辑。
- 接近交战距离时升级为 Full。

#### Ingress Simulation

数量：约 4～8 架。

- 沿 PCG 航路做运动学或低频移动。
- 保留编队、尾迹和入场预告。
- 不运行连续刚体控制。
- 进入中距离后升级为 Tactical。

### 22.2 性能约束

- 路径重算进入统一队列，每帧只处理有限数量。
- 不允许 28 架在同一帧同时重新寻路。
- 分离力查询使用空间分区，避免敌机数量继续增长时出现平方级开销。
- 远距离敌机不执行高频 Physics 查询。
- 对象池稳态不产生 GC 分配。
- Director、导航调度和战术查询应有独立 Profiler 标记。

---

## 23. 遥测与调参指标

### 23.1 Director 遥测

- `actualPressure`
- `targetPressure`
- `measuredEnvironmentPressure` 与区域预计压力上界
- `navigationPressure`
- `suicideNavigationPressure`
- `rangedNavigationPressure`
- `committedNavigationPressure`
- `playerStrain`
- 当前节奏阶段
- 当前允许策略等级、编队策略和单机状态分布
- 场上、交战和攻击敌机数量
- 当前威胁费用
- 当前攻击令牌
- 当前自爆 Commit 数量与远程 Fire Lane 数量
- 当前压力方向数
- 下一批次延迟原因
- 入口选择与拒绝原因
- Path Intent、语义路线 ID、预计到达时间和预计压力增量
- 路线容量、预约数量和未来 4 秒汇聚数量
- 路径失败、回退、重算原因和状态振荡次数
- 单机 `noProgressSeconds`、`nextReplanAt`、重算退避档位、首末连接段失败原因
- 路线预约租约续期、主动释放、过期回收、等待时长、死锁破除和饥饿 aging 次数
- 每个 `rosterMemberId` 的出生尝试、备用入口、SpawnRecovery、解决原因、有效击杀与奖励恰好一次结果
- 区域运行时状态切换、边界抖动次数、恢复窗口使用次数、冷却剩余和重复进入拒绝原因
- 自爆兵静态碰撞、自毁/环境归因结果，以及所有 Commit 结束后的令牌与预约释放结果
- 远程兵短时断视线、墙角重复探头、Burst 中断、位置冷却与强制换位次数
- Suicide Planner 的候选截击数、Commit 取消和 MissedPass 次数
- Ranged Planner 的候选火力位置数、射界拒绝和强制换位次数

### 23.2 玩家体验指标

- 首次接敌时间。
- 连续受击时长。
- 每 10 秒承受伤害。
- 碰撞频率。
- 平均速度和低速困住时长。
- 模块损失与 CPU 连通率。
- 从高压到恢复区的时间。
- RecoveryPocket 使用率。
- 遮挡链造成的敌方视线中断次数。
- 玩家被连续直线追踪的最长时间。
- 每轮攻击从预告、开火到脱离的实际时长。
- 自爆兵从 Telegraph 到预计撞击的时间、成功规避率和错过后再次转向时间。
- 远程兵的单次视线窗口、Burst 时长、开火后断视线与换位时间。
- 通过换层、遮挡或路线选择主动摆脱截击的次数。
- 暴露捷径的使用率和成功率。
- 六类区域的进入率、完成目标动作次数和主动退出原因。
- 玩家在中央街区完成掉头、刹车和重新接敌的成功率。
- 维修庭院内连续安全 10 秒并完成修复的比例。
- 每种敌机的击杀时间与伤害贡献。

### 23.3 Seed 报告

每个候选 Seed 至少输出：

- 城市战术机会数量和连接度。
- `TacticalAreaRequirementSet` 及六类区域的 Required/Optional/Disabled 结果。
- 每个已生成区域的 `AreaFunctionalValue`、功能验证结果和失败原因。
- 合法入口数量及类型分布。
- 最短和最长首次接敌时间。
- 所有角色到主要区域的路径可达性。
- 各语义路线的容量、预计压力增量和角色适配度。
- 自爆兵可用截击走廊、冲刺净空、到达时刻分布和脱离出口数量。
- 远程兵可用火力位置、射程适配、视线窗口和遮挡撤离邻接数量。
- 未来 4～8 秒最大敌机汇聚数量。
- 从每个高压区域到减压区域的最短时间。
- 恢复区附近入口避让情况。
- 各局部区域压力预算。
- 模拟完成 64 架名册所需时间。
- 峰值人口、交战数、令牌和压力方向。

---

## 24. EDPCG Difficulty Lab 可视化调参工具

需要制作一个 Unity Editor 内的策划工具 `EDPCG Difficulty Lab`。它不是只画一条预设曲线，而是把压力目标、实测压力、未来承诺、敌机数量、AI 状态和 PCG 城市区域放在同一时间轴上，让策划能够解释“这一秒为什么变难”，修改参数后立即预览结果，并用相同 Seed 和玩家轨迹比较修改前后差异。

工具必须直接读取和写入 EDPCG 配置资产，运行时 Director 与离线仿真必须调用同一个压力计算核心。不得在编辑器里复制一套近似公式，否则可视化结果不能作为调参依据。

### 24.1 四种工作模式

| 模式 | 输入 | 主要用途 | 是否允许保存配置 |
|---|---|---|---|
| Profile Edit | 星球档位、任务、配置资产、可选城市 Seed | 编辑六档目标压力、名册、节奏和区域参数，获得快速预测 | 是，必须通过验证后发布 |
| Play Mode Observe | 当前真实关卡与实时遥测 | 观察实际压力、AI 状态、路径承诺和城市区域是否符合设计 | 默认只读；开发模式可复制为草稿 |
| Play Mode Live Tuning | 当前真实关卡、运行时草稿和实时遥测 | 玩家实际驾驶时调整压力、节奏、预算与未来 AI 行为，并立即观察曲线响应 | 只保存为候选草稿，不能直接覆盖发布配置 |
| Batch Simulation | Seed 集合、玩家轨迹集、候选配置版本 | 批量回放普通、高手、受损和恶意玩家，比较分布与失败 Seed | 只保存报告；确认后再应用候选配置 |

Profile Edit 的快速预测用于准备参数；Play Mode Observe 用于不干预地记录基准；Play Mode Live Tuning 用于边玩边寻找合适数值；Batch Simulation 用于判断候选方案是否对不同城市和玩家都成立。任何实时修改都只是实验事件，不能只凭单局手感直接发布。

### 24.2 主界面布局

```text
┌ Planet / Mission / Seed / ProfileVersion / LIVE / Pause / Apply / Revert ┐
├ 参数树与约束 ──────┬ 压力曲线主视图 ─────────────────┬ 当前时刻检查器 ┤
│ 六档星球           │ Target Band / Actual / Forecast │ 公式分解        │
│ Encounter 阶段     │ Environment / Navigation        │ 事件原因        │
│ 敌机与令牌         │ Player Strain / Damage Assist   │ AI 状态分布     │
│ PCG 区域与寻路     ├ 人口、Commit、Fire Lane 时间轨 ┤ 路线与区域详情  │
├────────────────────┴──────────────────────────────────┴─────────────────┤
│ Live Change Queue / City Map / Compare / Validation / Event Log / Export│
└──────────────────────────────────────────────────────────────────────────┘
```

顶部选择决定一次可复现的实验：星球档位、任务类型、城市 Seed、名册 Seed、玩家轨迹、配置版本和仿真速度必须一直可见。进入 Play Mode 后还要显示绿色 `LIVE`、当前运行时间、实时修改数量、待生效修改和一键 Revert。工具要显示当前是已发布配置、运行时草稿、未保存候选还是历史回放，避免策划误把临时参数当成正式版本。

各面板需要达到可以独立完成工作的详细程度：

- 参数树：按 Difficulty、Roster、Encounter、Pressure Formula、AI Strategy、Navigation、PCG Area、Recovery、Telemetry 分组；支持搜索、收藏、只看已修改、只看可实时调整和只看错误参数。
- 参数行：同时显示 Published、Session Start、Candidate、Effective 四个值，并显示单位、合法范围、生效方式、Pending 状态、重启要求、来源 Profile 和简短说明。
- 曲线工具栏：时间范围、自动滚动、暂停显示、曲线显隐、堆叠/分离、平滑开关、Y 轴锁定、书签、A/B、截图和导出必须一行可达。
- 当前时刻检查器：显示压力公式树、贡献排序、当前区域、Encounter 阶段、玩家状态、活动小队、攻击令牌、未来承诺和最近参数修改。
- Event Log：可以按 Enemy、Squad、Area、Route、Token、Damage、Tuning Change 分类筛选；点击记录后同步跳转曲线和城市图。
- Validation：错误和警告按发布阻断级别排序，点击后定位参数或失败 Seed，不能只显示一段不可操作的文本。
- Preset：允许保存“只看压力”“AI 寻路调试”“PCG 区域验证”“64 架数量压力”等工作区布局和曲线显隐组合。

每个数值字段都要有可拖动 Slider、精确数字输入和增减步长；按住修饰键时支持细调。修改必须先进入 Candidate，不允许仅因鼠标滚轮经过字段就改变运行参数。危险操作使用明确按钮，不用每次弹窗打断实时调参，但 Revert、Restart 和 Publish 的作用范围必须写清楚。

### 24.3 压力曲线主视图

横轴是任务时间，纵轴统一使用 `0～1` 压力值。默认同时显示：

- 半透明目标压力带：`TargetPressureMin` 到 `TargetPressureMax`。
- 实线 `ActualPressure`，表示玩家实际正在承受的压力。
- 虚线 `ForecastPressure`，显示未来 4～8 秒已生成和已预约策略预计造成的压力。
- `MeasuredEnvironmentPressure`，不能用静态区域标签代替。
- 可堆叠拆分的 `SuicideNavigationPressure`、`RangedNavigationPressure` 与 `CommittedNavigationPressure`。
- 可选显示 `PlayerStrain`、`PlayerDamageAssist`、连续受击与碰撞险情。
- `0.75` 保护阈值、阶段硬上限、Recover 下限和 15 秒平滑窗口。

背景按 Preview、Engage、Peak、Recover 分段，并可叠加玩家当前所在的 `ManeuverBowl`、`OcclusionChain`、`ExposureShortcut`、`RecoveryPocket` 和 `VerticalEscape` 区域。曲线上必须有事件标记：批次预告、出生、攻击令牌发放、Commit、Burst、路径预约、区域切换、受击、碰撞、敌机解决和 NavigationRecovery。

鼠标悬停任一采样点时，右侧显示该点完整公式分解，例如：

```text
ActualPressure 0.63
  EnemyThreat             0.31
  NavigationPressure      0.18
  MeasuredEnvironment     0.11
  PlayerStrain            0.09
  RecoveryAssist         -0.06

ForecastPressure +4s      0.72
  Pending Suicide Commit  0.07
  Pending Ranged Lane     0.05
  Scheduled Spawn         0.03
```

点击压力尖峰后，工具自动选中贡献最大的敌机、小队、路径预约或城市区域，不能只告诉策划“压力高了”。

Live Tuning 时曲线是持续向左滚动的实时窗口，默认保留最近 120 秒并显示未来 8 秒。每次修改参数都在时间轴上插入一条带编号的垂直标记，标出旧值、新值、生效策略和真正生效时刻。修改前的 Target Band 用淡色保留，候选 Target Band 从当前时刻向未来绘制，ActualPressure 不允许被重算或覆盖。

工具必须同时显示“修改已经提交但尚未生效”的 Forecast，例如降低 Attack Token Cap 时，当前已经开始的合法 Burst 可以完成，但后续令牌立即受新上限限制。这样策划能看出参数没有立刻改变曲线究竟是系统错误，还是正在等待安全边界。

### 24.4 数量与状态时间轨

压力图下方使用对齐时间轨显示：

- `Unspawned / SpawnRecovery / Active / Engaged / NavigationRecovery / Resolved` 数量。
- Population Cap、Engagement Cap 和 Attack Token Cap。
- Interceptor Commit 数、Ranged Fire Lane 数和 Gunship 活动数。
- Encounter、Squad、Enemy、Weapon 四层状态分布。
- 未来 4～8 秒入口计划和高压路径到达窗口。

将时间光标拖到任意一帧时，曲线、状态轨、事件列表和城市视图必须同步。这样能区分“场上有 28 架但只有 3 架在攻击”与“数量不多但地形和火力叠加过载”。

### 24.5 可调参数

标准模式允许调节：

| 参数组 | 可调内容 | 必须保持的约束 |
|---|---|---|
| 六档难度 | 目标压力带、Peak 上界、策略等级、恢复频率 | 高档位复杂度总体不低于低档位；不靠伤害和血量动态作弊 |
| 名册 | 六档总数、Interceptor/Striker/Gunship 构成、批次分配 | 星球 6 总数锁定为 64；构成与批次之和必须等于 64 |
| 并发预算 | Population、Engagement、Attack Token、Commit、Fire Lane | 星球 6 同时在场不超过 28，完整模拟不超过 16，攻击令牌不超过 4 |
| 节奏 | Preview/Engage/Peak/Recover 时长、批次间隔、预告时长 | 高压不连续超过 12 秒；35～45 秒至少出现一次有效减压窗口 |
| 压力模型 | 各压力分量权重、EMA、滞后、预测时域、保护阈值 | 所有权重有版本；修改后必须重跑基准轨迹 |
| AI 策略 | 策略等级门槛、攻击后冷却、截击错峰、Burst 和换位时间 | 不能关闭 Telegraph、Break-Away、BreakLOS 或路径硬规则 |
| 城市区域 | Required/Optional、生成权重、功能阈值、预计压力上界 | 运行时只按实测环境压力扣预算；Required 功能失败必须拒绝 Seed |
| 寻路调度 | 路线容量、预约时间窗、重算间隔、退避、卡住阈值 | 不能允许空路径直飞、逐帧 A* 或把 NavigationRecovery 算作解决 |
| 恢复 | RepairCore 安全时间、区域进入/退出滞后、重复使用冷却 | 支持 10 秒维修；不能重复刷窗口或封死全部出口 |

曲线参数既可以在检查器输入数值，也可以直接拖动时间轴关键点。拖动时只修改草稿，并实时刷新快速预测；无效值显示红色并阻止发布，接近风险边界显示黄色警告。标准模式不能解除 64 架、28 并发、16 完整模拟和 4 攻击令牌等已经确定的最高难度硬约束。

Live Tuning 按生效方式把参数分为三类，界面必须在每个字段旁明确显示：

| 生效方式 | 实际游玩时的行为 | 适用参数示例 |
|---|---|---|
| `ApplyNow` | 下一次 Director Tick 立即采用；降低上限时停止发放新资格，但不粗暴中断已经进入合法攻击窗口的敌机 | Target Band、未来压力保护阈值、Attack/Commit/Fire Lane 上限、补兵暂停、策略等级上限 |
| `ApplyAtSafeBoundary` | 进入下一 Encounter 阶段、下一批次、下一次状态转换或下一次路径请求时采用；界面显示预计生效时刻 | 阶段时长、批次间隔、预告时长、Burst/冷却、规划器评分权重、路线预约窗口、未出生名册构成 |
| `RequiresRestart` | 只更新候选草稿，不改变当前世界；重新开始同 Seed 或重新生成城市后生效 | 城市 Seed、建筑与道路生成、Required 区域布局、总名册数量、对象池容量、飞行物理与碰撞包络 |

星球 6 当前关卡的名册总数始终保持 64。Live Tuning 可以调整尚未出生成员的兵种比例和批次分配，但必须保证“已生成数量 + 新的未生成构成 = 64”；不能通过实时调参删除已经在名册中的成员。

### 24.6 Play Mode 实时调节与版本管理

工具至少提供：

- Undo / Redo、恢复已发布值、恢复项目默认值。
- 从相邻星球复制参数、按比例插值六档压力曲线。
- 名册角色比例调整时自动保持总数，最后一项自动补足到 64。
- `Normalize`、`Clamp To Safe Range` 和“定位第一个验证错误”。
- A/B 双配置叠加：实线显示候选配置，淡线显示基准配置，并标记压力峰值、空窗、任务时长和伤亡差异。
- 修改前后参数 Diff，包含字段、旧值、新值、修改者、时间和原因备注。
- 候选配置另存为草稿；只有自动验收通过后才能 `Publish Profile` 并递增 `encounterProfileVersion`。
- 导出 PNG/SVG 图表、独立 PNG/SVG 图标集、CSV 逐帧数据、JSON 事件以及 PDF/HTML Session 报告，便于复盘和版本评审。

进入 Live Tuning 时，工具自动从已发布 Profile 创建内存中的 `Runtime Tuning Session`。每次修改记录：修改编号、任务时间、字段、旧值、新值、生效方式、提交时刻、实际生效时刻和操作者备注。运行中的修改绝不直接写回配置资产。

实际操作支持两种方式：

- 单人边玩边调：用可配置快捷键打开紧凑 HUD，选择 `Pause & Tune` 暂停物理和 Director Tick，调节参数、查看未来 8 秒预测后继续游玩。暂停期间不生成压力采样，恢复时插入修改事件标记。
- 双人协作：一人保持 Game View 实际驾驶，另一人在独立 Difficulty Lab EditorWindow 中实时观察和调节；界面清楚显示哪些参数已经生效、哪些正在等待安全边界。

Live Tuning 必须提供以下运行控制：

- `Apply Selected`、`Apply All Safe`、`Revert Last`、`Revert To Session Start`。
- 暂停/继续、0.25×/0.5×/1× 调试速度、只暂停 Director 决策和单步执行一个 Director Tick。
- 在曲线当前位置添加文字书签，例如“这里敌机太少”“这次夹击不可读”。
- 将常用参数固定到紧凑 HUD，避免驾驶时翻找完整参数树。
- 显示 Pending Change Queue；每项都能取消，不能让已经失效的修改稍后突然应用。
- `Restart Same Seed With Current Draft`，用同一城市、名册和候选参数立即重跑。
- `Save Session As Candidate`，把运行时修改显式保存为候选 Profile；退出 Play Mode 时若未保存则提示，但绝不自动覆盖正式资产。

可以提供 Freeze Director、强制跳阶段、立即进入 Recover、暂停生成和生成指定测试小队等调试动作，但这些不是正式参数修改。使用任一调试动作后，本局标记为 `DiagnosticRun`，曲线仍可用于定位问题，但不能作为难度验收或发布证据。

Play Mode 临时调节只允许按字段声明的生效策略作用于当前关卡，不能改变已经被玩家看到的城市几何，也不能追溯修改现存敌机生命、已造成伤害或历史 ActualPressure。停止运行后不得自动覆盖正式配置，必须显式保存为候选草稿并重新经过 A/B、批量 Seed 和自动验收。

### 24.7 PCG 城市联动视图

工具提供 2D 城市图和 Scene 3D Gizmos 两种视图：

- 六类战术区域用固定颜色和轮廓显示，区分预计压力上界与运行时实测压力热力图。
- 显示入口类型、预告方向、当前冷却、出生拒绝原因和 SpawnRecovery 队列。
- 显示 Suicide 截击入口、Telegraph Gate、Commit Corridor、掠过点和脱离路线。
- 显示 Ranged Fire Position、真实视线扇区、Burst 窗口、遮挡出口和换位路线。
- 显示语义航段容量、当前预约、租约剩余、双向通行权和未来 4～8 秒汇聚数量。
- 显示玩家飞行轨迹、速度、受损包络和当时可用的减压出口。

拖动压力时间光标时，城市图同步回放敌机位置与路径承诺。点击某个区域可以反向高亮它对压力曲线的贡献；点击某次压力尖峰也能定位到对应区域、敌机和航段。这样 PCG 调参不再只看城市是否“长得合理”，而是看它实际怎样改变玩家压力。

### 24.8 仿真、回放与 Seed 对比

工具需要两级仿真：

1. 快速解析预览：参数改变后约 100 毫秒内重新计算目标曲线、预算上界和预定批次，不运行完整物理。
2. 确定性行为回放：使用真实 Director、状态机、两套寻路规划器与记录的玩家轨迹，运行单 Seed 或批量 Seed。

预置轨迹至少包括普通、高手、受损玩家，以及第 26 章定义的维修区驻留、区域边界振荡、墙角探头、全速逃离、重复窄路和诱导自爆碰撞。批量结果不能只给平均值，还要显示 P50、P90、最坏 Seed 和失败 Seed：

- 目标压力带内时间比例。
- 峰值与连续高压时长。
- 最长无威胁空窗。
- 有效恢复窗口数量。
- 首次接敌和总任务时长。
- 路径失败、预约死锁、SpawnRecovery 与 NavigationRecovery 次数。
- 64 个名册成员是否数量守恒并恰好一次结算。

### 24.9 发布护栏

验证分为三档：

- 红色 Error：违反硬约束、名册不守恒、存在不可达 Required 区域、可能软锁、压力超过硬上限或导航安全规则失效；禁止发布。
- 黄色 Warning：P90 压力偏高、恢复窗口接近下限、少量 Seed 过长或某区域使用率过低；允许保存草稿，不建议发布。
- 绿色 Pass：目标曲线、城市功能、性能和反漏洞测试均在允许范围内。

工具保存的是配置资产与版本，不保存临时 UI 状态作为游戏逻辑来源。已发布 Profile 必须记录公式版本、PCG 版本、仿真 Seed 集合和验收摘要，保证同版本可以复现。

### 24.10 图表、图标与报告导出

导出必须基于图表数据重新渲染，不能依赖操作系统截图。这样即使 EditorWindow 很小，也能导出清晰的 4K 图片、透明背景图和 SVG 矢量图。导出时的时间范围、曲线显隐、颜色、标注、A/B 状态和参数修改标记必须与预览一致。

#### 可导出内容

| 内容 | 图片/矢量格式 | 数据格式 | 必须包含 |
|---|---|---|---|
| 压力曲线 | PNG、SVG | CSV、JSON | Target Band、Actual、Forecast、压力分量、阶段背景、阈值、书签和实时修改标记 |
| 数量与状态时间轨 | PNG、SVG | CSV、JSON | 名册集合、Active/Engaged、令牌、Commit、Fire Lane、四层状态分布 |
| PCG 城市压力热力图 | PNG、SVG；3D 视图可导出 PNG | JSON | 六类区域、入口、玩家轨迹、实测压力、区域 ID、Seed 和时间点 |
| AI 路径与预约图 | PNG、SVG | JSON | Suicide/Ranged 路径、Fire Position、LOS、容量、租约、到达窗口和失败原因 |
| A/B 配置差异图 | PNG、SVG | CSV、JSON、Markdown | 基准/候选曲线、峰值、空窗、恢复、任务时长和参数 Diff |
| 自动验收结果 | PNG | CSV、JSON、Markdown、PDF | Error/Warning/Pass、失败 Seed、P50/P90/最坏值和发布结论 |
| 图例与状态图标 | 独立 PNG、SVG | JSON 图标清单 | 透明背景、名称、语义、颜色、尺寸和所属事件类型 |
| 完整 Session 报告 | PDF、可选自包含 HTML | 同目录 JSON/CSV | 封面元数据、代表性图表、修改历史、书签、验证和复现信息 |

图例与状态图标包括 Encounter 阶段、敌机角色、Commit、Fire Lane、Spawn、Damage、Collision、Recovery、NavigationRecovery、路径失败、Tuning Change、Warning 和 Error。图标既可以随图表内嵌，也可以通过 `Export Icon Set` 单独导出：

```text
icons/
  phase_preview.svg
  phase_engage.svg
  suicide_commit.svg
  ranged_fire_lane.svg
  tuning_change.svg
  warning.svg
  error.svg
```

PNG 图标支持 16、24、32、48、64、128 和 256 像素以及自定义尺寸，默认透明背景；SVG 必须使用稳定 ViewBox、语义文件名和工具统一颜色变量，方便后续放进文档、策划报告或其他调试界面。

#### 四种导出范围

1. `Quick Snapshot`：实际游玩中用快捷键导出当前时刻前 30/60/120 秒和未来 8 秒 Forecast，不暂停游戏；同时保存当前参数摘要。
2. `Selected Range`：在曲线上框选任意时间段，只导出该段曲线、事件、状态轨和城市轨迹。
3. `Full Session`：导出整局压力、所有实时修改、书签、名册结算和最终验证，形成可复现报告包。
4. `Batch Comparison`：按星球档位、Profile、Seed 或玩家轨迹生成多图表对比、P50/P90 和失败样本索引。

点击压力尖峰后可以使用 `Export Around Selection`，默认导出尖峰前 15 秒、后 15 秒、公式分解、贡献最大的五个事件以及对应城市局部图，便于直接提交问题而不必手工拼截图。

#### 图片与图表选项

- 预设尺寸：1920×1080、2560×1440、3840×2160，以及自定义宽高和 1×/2×/4× Scale。
- 浅色、深色、透明背景三种主题；透明背景下轴线、文字和图例仍可单独指定颜色。
- 允许设置标题、副标题、字体大小、线宽、采样点、网格、图例位置、Y 轴范围和阶段背景透明度。
- 可选择导出当前可见曲线、全部曲线或一个保存的 Visualization Preset。
- 可选择内嵌参数修改说明、玩家书签、Seed、Profile Version、Build Version 和生成时间。
- A/B 图必须使用一致坐标轴，不能分别自动缩放造成视觉误判。
- 长时间 Session 可导出单张超宽图，也可按固定时间分页；分页之间保留 5 秒重叠和一致 Y 轴。

#### 报告包与命名

默认导出结构建议为：

```text
EDPCGReports/
  2026-08-07_Planet6_Clearance_ProfileV12_Seed1042/
    report.pdf
    manifest.json
    pressure_full.svg
    pressure_full.png
    pressure_data.csv
    population_timeline.svg
    city_heatmap.png
    route_reservations.svg
    tuning_changes.json
    validation.json
    icons/
```

`manifest.json` 至少记录：项目/构建版本、Profile 与公式版本、星球和任务、城市/名册 Seed、玩家轨迹或 Session ID、时间范围、采样率、启用曲线、实时修改序列、导出选项、文件列表和校验结果。文件名必须清理非法字符并保持稳定，重复导出通过序号或时间戳区分，不能静默覆盖旧报告。

#### 实时导出性能与可靠性

- Quick Snapshot 先复制环形缓冲区快照，再异步渲染和写文件，不能在实际驾驶时造成明显卡顿。
- 导出队列显示 Waiting/Rendering/Writing/Complete/Failed，失败时保留内存数据并允许重试。
- PNG/SVG 和 CSV/JSON 必须来自同一数据范围与采样版本，图上时间点应能在数据文件中准确找到。
- 导出进行时可以继续游玩；退出 Play Mode 前若队列未完成要明确提示并安全完成或取消。
- 每次导出完成后显示可点击路径、复制路径和打开文件夹操作。

### 24.11 什么时候使用

Difficulty Lab 应当从压力模型开始接入，并贯穿整个制作周期：

| 制作阶段 | 使用模式 | 此时要解决的问题 | 产出 |
|---|---|---|---|
| 压力遥测刚接通 | Play Mode Observe，只读 | 当前战斗为什么有空窗或过载；压力公式是否反映真实体感 | 基准回放、压力分量校准结果 |
| Director 节奏开发 | Profile Edit + 固定轨迹回放 | Preview/Engage/Peak/Recover 是否形成适中的起伏 | 第一版目标压力带和阶段参数 |
| 敌机状态机与寻路开发 | Observe + 单 Seed Replay | Telegraph、Commit、Burst、脱离和换位是否按时发生；有无直追或卡住 | AI 事件轨、路径与令牌问题清单 |
| PCG 城市区域开发 | City Map + Batch Simulation | 区域是否真的提供转弯、遮挡、恢复、捷径和换层价值 | Seed 功能报告、区域压力上界 |
| 六档星球关卡调参 | Profile Edit + A/B Compare | 难度是否逐档增加；64 架最高关是否靠复杂度而不是持续满压 | 六档 Profile 草稿与差异报告 |
| 内部探索性试玩 | Play Mode Live Tuning | 边实际驾驶边寻找目标压力、批次、令牌、AI 时序与恢复窗口的合适范围 | 带实时修改事件的 Tuning Session 和候选草稿 |
| 真人测试之前 | Batch Simulation | 是否存在明显软锁、压力爆点或恶意轨迹漏洞 | 测试候选版本和已知风险 |
| 正式测量型真人测试 | Play Mode Observe，只读记录 | 玩家在哪些时刻迷失、受压或找到策略；保持一局内规则不变 | 带玩家反馈标记的完整回放 |
| 真人测试之后 | Replay + A/B Compare | 体感反馈对应哪一段压力、区域或 AI 决策 | 单一假设的候选修改 |
| 每日/持续回归 | Batch Simulation | 新 AI、PCG 或配置改动是否破坏既有 Seed | 自动验收报告与失败 Seed |
| 版本发布前 | 完整 Batch + Publish Profile | 硬约束、P90、最坏 Seed 和反漏洞测试是否通过 | 锁定的 Profile 版本和验收摘要 |
| 发布后修正 | 历史回放 + 草稿仿真 | 复现问题并验证修正；不能直接改线上参数碰运气 | 可复现修正、回归报告和新版本 |

接入顺序非常重要：先确认压力采样与玩家体感大致一致，再调整目标曲线；先修复导航、出生和结算硬错误，再调 AI 策略；最后才调整六档数值。如果测量公式本身错误，过早拖动曲线只会把错误隐藏到另一组参数里。

### 24.12 一次完整调参应该怎么做

每次调参都从一个明确问题开始，不能同时随意拖动多个滑杆：

```text
提出假设
  → 固定 Profile / Seed / 玩家轨迹 / 公式版本
  → 运行基准并标记异常时间段
  → 用公式分解、事件轨和城市视图定位原因
  → 在草稿中只修改一个参数组
  → 快速预测检查方向
  → 相同输入下做确定性 A/B 回放
  → 批量 Seed 与多类玩家轨迹验证
  → 真人测试确认体感
  → 自动验收通过后发布新 Profile 版本
```

具体规则：

1. 先写下可验证假设，例如“第二个 Peak 太高是因为两架自爆 Commit 与两条 Fire Lane 在 4 秒内重叠”，而不是笼统地说“这里不好玩”。
2. 基准与候选必须使用相同 Seed、名册、玩家轨迹和公式版本；只比较一个参数组，避免无法判断是哪项修改起作用。
3. 第一次只看单 Seed 回放定位因果，确认修改方向后再批量运行；不要每拖一次滑杆就跑全部 Seed。
4. 快速预测通过不代表玩法成立，涉及 AI 路径、城市视线和物理碰撞的修改必须运行确定性行为回放。
5. 批量结果通过后仍需真人测试。图表负责发现和解释问题，不能代替“攻击是否可读、躲避是否有乐趣”的实际体感。
6. 发布时同时保存基准、候选 Diff、代表性曲线、最坏 Seed、玩家反馈结论和修改原因。

压力公式权重与目标压力曲线不能在同一轮同时调整。前者改变“工具怎样测量压力”，后者改变“Director 想提供多少压力”；混在一起会失去可比较基准。

边玩边调时使用更短的实时循环：

```text
实际驾驶到问题出现
  → 在曲线上打书签
  → Pause & Tune
  → 只改一个 Live-Safe 参数
  → 查看新 Target 与未来 8秒 Forecast
  → 继续驾驶至少一个完整 Engage/Peak/Recover 循环
  → 保留或 Revert
  → 同一 Seed 重开验证
```

实时调节适合寻找合理范围和快速验证因果，不适合直接证明最终平衡。一次 Live Tuning Session 找到候选值后，仍然要用不再改参数的同 Seed 重跑、批量 Seed 和正式真人测试确认。

### 24.13 遇到不同问题时先看哪里

| 现象 | 首先打开 | 先确认 | 常见调节方向 |
|---|---|---|---|
| 玩家觉得敌机太少 | 数量与状态时间轨 | 是活动数少、接敌数少，还是很多敌机都在 Regroup | 批次间隔、Engagement Cap、入口到达时间；不要先加伤害 |
| 敌机很多但压力仍低 | Attack Token 与 AI 状态轨 | 是否缺少可执行 Fire Position/Commit，或预告和冷却过长 | PCG 候选位置、策略可达性、令牌发放时机 |
| 突然被集火 | Forecast 与事件标记 | 是否多个路径承诺、出生和区域暴露在 4 秒内叠加 | 错开到达预约、降低组合上限、提前触发保护 |
| 全程持续高压 | 阶段背景与目标压力带 | Recover 是否真的停止新承诺，敌机是否完成脱离 | Recover 时长、BreakContact、未来承诺取消规则 |
| 某些城市特别难 | Seed Compare 与区域热力图 | Required 区域功能、路线支配、真实环境压力是否异常 | PCG 功能阈值、区域连接、Seed 拒绝规则 |
| 自爆兵一直直追 | Suicide 状态轨与 Commit Corridor | 是否跳过对齐、Telegraph、MissedPass 或 Break-Away | 状态转换和路径回退；不是降低自爆伤害 |
| 远程兵没有策略 | Fire Position 与 LOS 事件 | 是否找不到位置、墙角无限重置或 Burst 后不换位 | 火力位置生成、遮挡出口、位置冷却 |
| 玩家能永久躲在维修区 | 区域状态与恢复窗口轨 | Active/Cooldown 是否被边界重复重置，是否可向外安全刷伤害 | 区域滞后、重复使用冷却、RepairCore 射界 |
| 最后一架找不到或不结束 | 名册守恒与导航事件 | 稳定 ID 位于哪个集合，是否 SpawnRecovery、卡住或预约泄漏 | 出生恢复、进度看门狗、预约释放；不能强制算击杀 |
| 实际体感与图表不一致 | 压力公式分解与真人录像 | 缺失了哪类体感信号或权重是否错误 | 先校准测量模型，不先改目标曲线 |

### 24.14 谁来使用以及使用边界

推荐职责：

- 战斗策划：负责六档目标压力、名册构成、阶段、预算和最终 Profile 发布建议。
- AI 策划/程序：负责状态机、规划器、攻击窗口、回退原因和导航事件，不用伤害数值掩盖策略错误。
- PCG/关卡策划：负责区域功能、连接、入口和 Seed 对比，不直接修改 Director 压力公式。
- QA：维护普通、高手、受损和恶意轨迹集，复现失败 Seed，验证名册与预约不变量。
- 程序：维护共享压力计算、采样正确性、确定性和工具性能；公式版本变化必须显式迁移。

使用边界：

- 内部探索性试玩允许 Live Tuning；正式测量型真人测试使用只读 Observe。两类 Session 必须在报告中明确区分。
- 不根据单个高手或单个失败 Seed 直接发布全局改动，先判断是玩家差异、城市异常还是系统性问题。
- 不把工具做成运行时偷偷修改玩家伤害、敌机血量或城市几何的动态作弊系统。
- 不为了让曲线贴住目标带而取消所有自然波动；适中的压力曲线仍应有可读 Peak 和真正 Recover。
- 不用平均值掩盖最坏情况。发布判断必须同时检查 P90、最坏 Seed、恶意轨迹和真人反馈。
- 发布权限应与普通调参分开；任何人可以保存草稿和报告，只有通过自动验收的版本才能成为正式 Profile。

### 24.15 工具验收标准

- 同一 Profile、Seed 和玩家轨迹在工具回放与实际 Director 中得到相同压力采样和关键决策；允许的浮点误差必须明确。
- 修改任一压力权重、阶段时长、令牌或名册构成后，曲线、状态轨和城市热力图同步刷新。
- Play Mode 实际驾驶时能够持续显示最近 120 秒 ActualPressure、当前 Target Band 和未来 8 秒 Forecast，不需要停止关卡才能更新。
- 每个实时参数明确标记 `ApplyNow`、`ApplyAtSafeBoundary` 或 `RequiresRestart`；Pending Change 的预计与实际生效时刻能够追踪。
- Revert Last 和 Revert To Session Start 不遗留攻击令牌、路径预约、状态权限或延迟生效项。
- 退出 Play Mode 不会自动覆盖正式 Profile；保存候选后能够用相同 Seed 与完整修改事件序列复现。
- 使用 Freeze、强制跳阶段或测试生成等调试动作后，本局自动标为 DiagnosticRun，不能误用于发布验收。
- 策划能从任一压力尖峰在两次点击内定位到贡献最大的区域、敌机策略或路径承诺。
- A/B 对比能够显示修改前后的目标带内时间、峰值、空窗、恢复窗口和任务时长差异。
- 压力图、状态轨、城市热力图、路径图和 A/B 图能够导出 PNG 与 SVG；图片支持 4K、自定义尺寸和透明背景。
- `Export Icon Set` 能按规定尺寸导出全部图例/状态 PNG，并输出可缩放 SVG 和带语义名称的清单。
- Quick Snapshot 能在实际驾驶中导出最近 30/60/120 秒而不产生超过项目允许阈值的主线程卡顿。
- 同一次导出的 PNG/SVG、CSV/JSON 和报告使用同一时间范围、采样版本与参数修改序列，任一图上事件都可定位到原始数据。
- Full Session 报告包含 manifest、修改历史、Profile/公式/构建版本、Seed、验证结果和全部文件索引，能够在另一台开发机复现。
- 导出失败不会静默丢失 Session 数据或覆盖旧报告，能够查看原因并重试。
- 星球 6 标准模式不能发布非 64 名册、超过 28 并发、超过 16 完整模拟或超过 4 攻击令牌的配置。
- 无效配置不能发布，发布成功后配置版本递增且报告可追溯。
- 运行时调试叠层可完全关闭，关闭后不产生持续采样开销或构建内容依赖。

---

## 25. 自动验收标准

### 25.1 数量与节奏

- 星球 6 的任务名册稳定生成 64 架敌机。
- 同时在场不超过 28。
- 完整战斗模拟不超过 16。
- 普通阶段攻击令牌不超过 3。
- 高潮阶段攻击令牌不超过 4。
- 同时最多一架 Gunship。
- 64 架名册成员都有稳定且唯一的 `rosterMemberId`，每个成员恰好一次进入正式解决终态。
- 出生失败不会减少名册或静默丢弃队列项；会改用备用入口或进入 SpawnRecovery。
- 导航故障不计解决、有效击杀或奖励，恢复后仍是同一名册成员。
- Clearance 的有效击杀要求在名册生成时已验证可达，不会因自爆结果造成最终软锁。

### 25.2 压力

- 至少 70% 的有效交战时间处于目标压力带附近。
- `ActualPressure > 0.75` 不连续超过 2 秒而没有触发保护。
- 高压连续时间不超过 12 秒。
- 每 35～45 秒至少出现一次 8 秒以上的减压窗口。
- 玩家进入 Critical 状态后，Director 不再升级后续组合。
- 预测压力越过阶段上限时，未执行的高压策略会被取消或延迟。
- Recover 开始后 1 秒内不再产生新的截击、侧翼或夹击承诺。
- `SuicideNavigationPressure` 和 `RangedNavigationPressure` 分别受独立额度限制，任一项满压时不会被另一项平均稀释。
- 只有实际测得的地形压力才减少敌人预算；玩家悬停在区域边缘不会获得该区域的完整减压值。
- 区域边界往返不会反复重置 Recover、取消敌人策略或重复获得换层减压。

### 25.3 城市公平性

- 不在玩家直接视野内出生。
- 不在 RecoveryPocket 内出生。
- 每个入口到目标战术区域均有合法路径。
- 高导航压力区域不同时出现最大远程火力。
- 每个主要压力至少有两种有效解法。
- 玩家飞行能力损失约 30% 后仍有可达恢复路线。
- Masked Flank 的重新出现均有可感知预告。
- 除授权的最终冲刺外，敌机不持续直线追射玩家。
- 每次攻击后均进入可观测的 Disengaging 与 Cooldown。
- 自爆兵只使用 Suicide Intercept Planner，不请求 Fire Position。
- 自爆兵最终冲刺具有有限转向，错过后先掠过和脱离，不立即掉头二次冲刺。
- 远程兵只使用 Ranged Fire-Position Planner，不把玩家当前位置作为持续寻路终点。
- 远程兵在转移、避障和重规划期间不射击，Burst 后必须断视线或换位。
- 两架自爆兵 Commit 时普通阶段最多保留一条远程火力线；两条远程火力线有效时最多一架自爆兵 Commit。
- 出生安全空域提供 4～6 秒可观察、无直接集火的开场窗口。
- 中央机动街区能让设计飞船完成一次完整转弯，并至少有 3 个有效出口。
- 高楼掩体路线能连续切断远程视线，而不是只提供单栋静态掩体。
- 开放火力捷径只有在能兑现时间、抢点或截击收益时才计入战术评分。
- 维修庭院能支持 10 秒自我修复，并保留两个独立撤离出口。
- 维修庭院安全窗口不能被驻留或边界往返重复刷新，玩家也不能从核心区持续安全刷伤害或任务进度。
- 换层通道在受损飞行条件下仍可从低空到达中空，并重新接回城市网络。
- 自爆兵 Commit 撞上建筑后会结束本次攻击并释放全部占位，不会卡在墙面持续推力或占用通关计数。
- 远程兵的短时断视线立即停火但不免费重置完整 Burst；同一墙角反复探头会触发位置冷却和换位。

### 25.4 性能

- 稳态战斗无每帧 GC 分配。
- 路径请求队列不会无限增长。
- 28 架可见敌机不会在同一帧执行完整语义重规划。
- Suicide 截击搜索与 Ranged 火力位置搜索分别排队和计时，均不能绕过每帧预算。
- 路线预约能够阻止多个编队同时挤入同一低容量航段。
- 所有路线预约都有租约并在取消、改道、死亡、LOD 切换或超时后释放；不存在永久占位。
- 低容量连续航段不允许持有并等待，双向窄路不会死锁，普通请求通过 aging 不会永久饥饿。
- 路径失败遵守退避，不会每帧重算；重算计时不会重置独立的卡住检测。
- 路径的当前位置到首节点、图内边和末节点到目标全部经过净空验证；失败后不会直飞玩家或战术目标。
- 敌机 LOD 升降级不会造成位置跳变或可见瞬移。
- 28 架可见敌机时仍能维持项目目标帧率。

---

## 26. 防卡死与反漏洞协议

本章是进入 64 架规模前的硬门槛。这里的“卡死”同时包括物理卡墙、路径请求风暴、预约死锁、出生队列漏怪和任务无法结束；“漏洞”包括无风险清场、永久恢复、区域边界刷保护以及通过 AI 状态重置无限压制敌人。

### 26.1 当前实现对接前必须消除的风险

对现有敌群导航、出生和结算流程的静态审查显示，接入本方案前要建立以下测试并修正对应实现；本文件只记录约束，不在本阶段修改代码：

1. 导航定时重算与卡住检测共用或互相重置进度采样时间时，较短的重算周期会让较长的卡住阈值永远无法达到。两套时钟必须完全独立。
2. 空路径若立即再次满足重算条件，会形成逐帧寻路；失败必须退避，并在等待时保持最后合法状态。
3. 图内航段通过验证后直接追加最终目标，会漏掉“当前位置到首节点”和“末节点到目标”的碰撞检查，形成穿楼假路径。三段都必须验证。
4. 单入口超过最大出生尝试后若直接移除队列项，会造成 64 架名册静默缩水。必须换备用入口或进入 SpawnRecovery。
5. 自爆流程若统一进入普通死亡回调，容易把自行撞毁计为玩家击杀；若静态建筑碰撞又不触发结束，则会出现自爆兵卡墙且永久占用活动数。必须使用带归因的解决原因。
6. 仅用“远离战术目标且长期无视线”作为逃逸条件，无法处理靠近目标但卡在几何体内的敌机。最终清场必须依赖独立进度看门狗和 NavigationRecovery。

上述任一项存在时，不允许把“导航保护离场”直接计入 `ResolvedRosterCount`。技术故障只能恢复同一名册成员，不能替玩家消灭敌人。

### 26.2 进度看门狗与路径有效性

每架敌机维护独立的 `progressSampleAt`、`nextReplanAt`、最近有效航段投影进度和距下一合法节点的变化。旋转、碰撞抖动或速度很高但没有沿路径前进，不算有效进度。

```text
连续 2秒无进度 → 局部修路、清理短时避障目标
连续 5秒无进度 → Evasive、倒车/侧移脱离、请求备用语义路线
连续 8秒无进度 → NavigationRecovery，同一 rosterMemberId 安全重入
```

每次路径结果提交前重新检查请求版本、角色、策略状态、首段、全部图边和末段。异步返回的旧路径、目标区域已离开后的路径或穿越失效建筑净空的路径一律丢弃。搜索失败后敌机只可制动、保持、沿最后合法段脱离或前往已验证 Hold 点，不允许向玩家位置直飞。

### 26.3 路线预约、死锁与饥饿

- 预约是有租约的资源，不是永久计数。只有沿预约航段持续取得进度才续租。
- 状态退出和预约释放必须配对；死亡、对象池回收、规划取消、改道和 LOD 切换均需验证没有残留预约。
- 低容量连续航段采用原子窗口预约或无持有等待，避免 A 占第一段等第二段、B 占第二段等第一段。
- 双向窄路按 Evasive、BreakContact、攻击修路、Positioning、Ingress 的优先级决定通行权；同优先级用等待 aging 与稳定 ID 打破平局。
- 超过最大等待时间必须选择备用路线、Regroup 或 NavigationRecovery，不能一直悬停堵住后续 64 架队列。

### 26.4 名册、出生与结算不变量

```text
RosterCount
  = UnspawnedCount
  + SpawnRecoveryCount
  + ActiveEnemyCount
  + NavigationRecoveryCount
  + ResolvedRosterCount
```

任意一帧都应满足上式，且每个稳定 ID 只能出现在一个集合中。对象池实例可以重复使用，但名册 ID、血量、奖励状态和解决原因不可串到另一个成员。所有解决、奖励和计数接口都必须幂等，防止伤害、碰撞和自爆在同帧重复回调。

清场流程不得用强制销毁活动敌机来修补计数。若正式构建触发 `TechnicalFinalClear`，应视为自动测试失败；开发构建可以结束关卡以便继续调试，但必须记录 Seed、稳定 ID、最后路径和碰撞状态。

### 26.5 区域滞后与防驻留

- 区域效果只在 `Active` 状态生效，不对单帧擦边做出完整 Director 调节。
- 区域进入和退出不会重置敌机整套策略；仅在持续处于新区域后重新评估未来承诺。
- 开放捷径只按实际暴露压力扣减敌人预算，边缘悬停或隔墙不算完整暴露。
- 维修核心不产生安全刷伤害角度或任务收益；安全窗口使用后进入 35～45 秒冷却，原地停留和反复进出都不能刷新。
- 出生安全只消费一次；换层减压每次真实穿越只触发一次。
- Director 可以在恢复区外围保持存在感，但不能封死两个出口，也不能把保护区变成敌人逐个送死的漏斗。

### 26.6 兵种专用漏洞保护

自爆兵：

- Commit 只能沿已验证走廊，动态失败时 MissedPass，不切回完美追踪。
- 撞静态结构必须结束攻击并释放所有资源；是否计入玩家击杀由近期诱导、玩家伤害和轨迹改变等归因规则决定。
- 建议环境击杀归因窗口为 3～5 秒，并要求敌机正在以玩家为 Commit 目标、受到玩家伤害或因玩家可验证的规避轨迹进入碰撞线；单纯导航故障不能冒充玩家归因。奖励仍按稳定 ID 只发一次，可低于直接击杀奖励。
- 玩家反复沿同一墙面引诱时，可以形成可学习的地形战术，但不能无奖励风险地无限刷击杀；同一诱导点增加短期路线冷却并让后续敌机改用其他可读入口。

远程兵：

- 无视线当帧停火，但短时遮挡只暂停/终止当前发射，不把敌人的 Telegraph、令牌和冷却全部重置为初始状态。
- 同一墙角反复探头会累积位置冷却并触发横移换位，不能让远程兵永远卡在“准备射击—失去视线”的零威胁循环。
- 火力位置搜索失败只能 MaskedHold、Regroup 或 Withdraw，并使用退避；禁止转成近身直追和穿墙射击。

### 26.7 恶意玩家轨迹测试矩阵

除了普通、高手和受损玩家，自动仿真还要主动寻找最优漏洞：

| 测试轨迹 | 尝试利用的漏洞 | 通过条件 |
|---|---|---|
| 维修核心驻留 60 秒并持续向外射击 | 永久安全炮台、任务刷进度 | 安全窗口不刷新，无安全刷怪漏斗，无异常任务收益，始终保留可读退出方案 |
| 在区域边界往返 30 次 | 反复 Recover、取消敌人策略、重复减压 | 只有满足停留与冷却的有限状态切换，压力无锯齿重置 |
| 在同一高楼墙角高频探头 | 无限重置远程 Burst | 无穿墙射击；远程兵进入位置冷却并换位，令牌无泄漏 |
| 全速远离城市与战术目标 | 让敌机失联或直接算作解决 | 敌机改道、Withdraw 或 NavigationRecovery；名册数量守恒且无免费击杀 |
| 持续重复同一低容量路线 | 预约死锁、AI 堵路 | 租约到期可回收，通行权稳定，等待 aging 后改道，无永久拥堵 |
| 引诱全部自爆兵撞同一面墙 | 无风险清场、重复奖励、卡墙 | 每次结果有明确归因，资源全部释放；无重复奖励、无活动数残留，击杀要求仍可达 |
| 让出生入口持续被遮挡或占用 | 出生项被静默删除、逐帧重试 | 备用入口/SpawnRecovery 保留稳定 ID，按退避重试，最终名册仍完整 |
| 动态障碍封住路径首段或末段 | 图路径有效但实际穿楼 | 候选被拒绝并安全回退，不直飞、不逐帧 A* |

每个测试至少覆盖所有已发布城市 Seed 的代表样本，并记录任务是否完成、64 个稳定 ID 的最终状态、奖励总数、路径请求峰值和预约表是否归零。

---

## 27. 推荐实施阶段

### P0：导航安全基线与压力遥测

- 在不改变现有玩法的前提下记录实际压力数据。
- 接入玩家健康、CPU 连通、伤害、击杀和碰撞信号。
- 为 Director 决策增加可读日志和调试面板。
- 同步交付 Difficulty Lab Phase A：只读显示 Target、Actual、基础压力分解、Encounter 阶段和关键事件；从第一批遥测开始积累基准回放。
- 分离进度看门狗与路径重算计时，补齐路径首段/末段净空验证和失败退避。
- 禁止空路径直飞与逐帧重算；验证自爆静态碰撞一定能结束攻击并释放资源。
- 给出生失败建立备用入口和 SpawnRecovery，禁止达到尝试次数后丢弃名册成员。

验收：能够复盘现有两分钟曲线中何时产生过载和空窗；卡住敌机可以恢复但不会被算作击杀或解决，路径与出生失败不会形成请求风暴或漏怪。

### P1：完整 PCG 数据契约

- `FinitePlanetUrbanCombatRuntime` 暴露完整入口和战术机会。
- 增加六类 `EdpcgAreaFunction` 和 `TacticalAreaRequirementSet`。
- PCG 根据任务将区域标为 Required、Optional 或 Disabled。
- 建立稳定名册 ID、带归因的解决状态、幂等奖励和任务数量守恒检查。
- 增加区域 Outside/EnterPending/Active/ExitPending/Cooldown 状态与一次性出生保护。
- 将城市计划坐标转换为世界坐标运行时图。
- 暴露 Suicide 需要的对齐点、冲刺走廊和脱离出口，以及 Ranged 需要的火力位置、视线窗口和换位邻接点。
- Director 不再只接收入口 `Vector3`。
- 保留通用自然战场回退。
- Difficulty Lab 增加 2D City Map，能检查六类区域、入口、路径连接和功能验证结果。

验收：Director 能区分 Suicide/Ranged 入口，并查询六类区域的功能、出口、路径和任务相关度。

### P2：人口、交战和攻击预算分离

- 增加 Population、Engagement、Attack 三类预算。
- 对象池扩展到 32～36。
- 实现 Encounter、Squad、Enemy、Weapon 四层状态机的基础状态与权限检查。
- 完成 Ingress、Forming、Positioning、Threatening、Telegraphing、Attacking、Disengaging、Regrouping 和 Withdrawing 主循环。
- 星球 6 能稳定完成 64 架名册。
- Difficulty Lab 增加人口、交战、攻击令牌和四层状态机时间轨。

验收：场上超过 20 架时，同时攻击者仍不超过配置上限。

### P3：语义寻路和城市角色行为

- 实现独立的 `SuicideInterceptPlanner` 和 `RangedFirePositionPlanner`，共享部分仅限语义图与局部避障。
- Interceptor 消费预测截击、对齐点、冲刺走廊和脱离出口。
- Striker 消费暴露区与遮挡出口。
- Gunship 消费开阔区域和任务中心。
- PCG 航路和战术体积形成语义导航图，通用环形图作为局部回退。
- 增加 Path Intent、路线容量、到达预约和多目标路径评分。
- 路线预约增加租约、状态退出释放、无持有等待、双向通行权和饥饿 aging。
- 实现攻击后强制脱离，移除持续直线追射。
- 让敌机正确响应中央街区、掩体链、开放捷径、维修庭院和换层通道。
- Difficulty Lab 增加 Suicide/Ranged 路径、Fire Position、LOS、航段预约和卡住恢复事件联动。

验收：改变城市 Seed 会分别改变截击走廊和远程火力位置；两套规划器没有回退到同一条直追玩家路径。

### P4：四阶段节奏和局部压力预算

- 实现 Preview、Engage、Peak、Recover。
- 将 NavigationPressure 纳入实际压力和前瞻压力。
- 分别计算 SuicideNavigationPressure 与 RangedNavigationPressure，并落实 Commit/Fire Lane 组合上限。
- 只把实际测得的环境压力与已承诺寻路压力从敌人压力预算中扣除，静态区域估值只作上界。
- 实现区域停留滞后、恢复区冷却与换层减压的一次穿越触发，防止边界刷保护。
- 压力曲线决定允许的策略等级，高压时自动执行 BreakContact。
- 落实局部最多两个高压力维度的约束。
- Difficulty Lab 增加 Forecast、完整公式分解、Profile 草稿编辑和固定输入 A/B 回放。
- Difficulty Lab 增加 Play Mode Live Tuning：运行时草稿、ApplyNow/ApplyAtSafeBoundary/RequiresRestart、生效队列、修改标记与一键回退。

验收：高驾驶难度区域不会同时出现最大远程交叉火力。

### P5：有限动态调节

- 加入 15 秒平滑窗口、滞后和冷却。
- 只修改后续编队、入口、令牌和延迟。
- 保证不会减少固定名册总数。
- 用记录的普通、高手和受损玩家轨迹回放验证动态调节；内部探索性试玩允许 Live Tuning，正式测量型测试锁定为 Observe。

验收：受损玩家获得可感知但不突兀的减压；高手不会遭遇无限加压。

### P6：自动仿真和策划工具

- 实现 `EDPCG Difficulty Lab` Unity EditorWindow 与紧凑运行时 HUD，并提供 Profile Edit、Play Mode Observe、Play Mode Live Tuning 和 Batch Simulation 四种模式。
- 抽取由 Director 与工具共同调用的压力计算核心，禁止编辑器复制近似公式。
- 实现 Target Band、Actual、Forecast、环境压力、两类导航压力和 Player Strain 的同步时间轴。
- 实现人口/交战/令牌/Commit/Fire Lane 状态轨，以及与 2D 城市图和 Scene 3D Gizmos 的时间光标联动。
- 提供受约束的曲线关键点、名册、阶段、预算、AI 策略、PCG 区域与寻路参数编辑。
- 提供草稿、Undo/Redo、恢复发布值、A/B Diff、自动验证、版本发布，以及 PNG/SVG/CSV/JSON/PDF/HTML、独立图标集和完整报告包导出。
- 提供 Pause & Tune、参数生效队列、实时修改事件、同 Seed 重开、Revert 和 DiagnosticRun 标识。
- 批量验证城市 Seed。
- 自动执行六类区域的功能测试，不只检查区域是否存在。
- 模拟普通、高手和受损玩家轨迹。
- 加入维修区驻留、区域边界振荡、墙角探头、全速逃离、重复窄路、诱导撞墙和入口封锁等恶意轨迹。
- 分别仿真自爆截击成功率、错过后的脱离，以及远程射界、Burst 和换位循环。
- 模拟普通敌群与限时设施任务上下文，验证区域价值是否成立。
- 展示人口、压力、暴露、入口和恢复热力图。
- 展示语义路径、到达预约、未来汇聚和 NavigationPressure 热力图。
- 锁定并发布六档配置。

验收：策划无需修改代码即可调整 64 架名册构成、压力曲线、区域生成条件和区域功能阈值；任一曲线尖峰都能定位到城市区域、AI 策略或路径承诺，非法配置不能发布。

---

## 28. 第一版最小可玩版本

第一版不需要一次完成全部动态系统。最小版本建议只实现：

1. 星球 1～6 的固定敌机名册：16、24、32、40、52、64。
2. 对象池扩展和三级模拟。
3. Population、Engagement、Attack 三预算。
4. 四阶段节奏。
5. 完整 PCG 入口类型、预警和路径接入。
6. Encounter、Squad、Enemy、Weapon 四层基础状态机。
7. 攻击必须经过占位、预告、有限攻击窗口、脱离和冷却。
8. Suicide 的 Probe、Intercept、CommitAttackRun、BreakAway，以及 Ranged 的 MaskedFlank、RangedPerch、BreakLineOfSight、MaskedHold。
9. 五类通用区域：出生安全空域、中央机动街区、高楼掩体区、维修庭院、低中空换层通道。
10. 设施或限时任务按上下文启用开放火力捷径。
11. Required 区域的功能验证；无上下文价值的 Optional 区域不计入战术评分。
12. 独立的 Suicide Intercept Planner 与 Ranged Fire-Position Planner。
13. 自爆 Commit、远程 Fire Lane 和总攻击令牌三层组合限制。
14. NavigationPressure、路线容量和未来接敌预约。
15. 压力过载后的停止补兵、策略降级、BreakContact 和恢复保护。
16. 星球 6 同时在场不超过 28、同时攻击不超过 4。
17. 独立进度看门狗、路径首末段验证、失败退避和禁止直飞回退。
18. 稳定名册 ID、SpawnRecovery、带归因且幂等的解决与奖励。
19. 路线预约租约、完整释放、无持有等待和等待 aging。
20. 战术区域进入/退出滞后、一次性出生保护与维修区 35～45 秒重复使用冷却。
21. `EDPCG Difficulty Lab` 基础版：实际游玩中的滚动目标/实际/预测压力曲线、数量与令牌时间轨、城市区域联动、Live Tuning 参数草稿、生效队列、回退、同 Seed 重开、A/B 对比、PNG/SVG 图表与图标导出、CSV/JSON 数据导出和发布护栏。

第一版暂不做：

- 复杂玩家画像。
- 跨多局学习。
- 动态修改敌人属性。
- 64 架同时完整物理模拟。
- 运行时改变城市结构。

---

## 29. 最终准则

```text
城市负责提供问题和解法。
敌机状态机负责用可读的攻击轮次激活这些问题。
Suicide 寻路负责制造可规避的预测截击。
Ranged 寻路负责制造有限并可切断的火力窗口。
Director 负责控制玩家一次需要处理多少问题。
星球难度负责决定问题组合有多复杂。
技术故障不能替玩家解决敌人，战术区域也不能被无限重复触发。
压力曲线必须可见、可解释、可回放，并且只能通过受验证的参数修改发布。
```

64 架敌机负责提供规模感和持续战斗内容；完整状态机让敌机进行占位、预告、攻击、脱离和重组；Suicide Planner 用截击走廊控制碰撞压力，Ranged Planner 用火力位置和断视线换位控制远程压力；攻击令牌、角色额度、局部预算和恢复阶段负责保证总压力适中。

只要玩家能够清楚感受到：

- “敌机很多，但我知道现在谁最危险。”
- “进入楼群确实切断了远程火力。”
- “敌机是在执行策略，不是排成直线追着我持续开火。”
- “自爆兵在预测并截击我，远程兵在找射击位置，它们不是换皮的同一种追踪 AI。”
- “我能看出一次攻击什么时候开始，也能抓住它脱离和重组的空档。”
- “走暴露捷径更快，但会承担短暂风险。”
- “恢复区给了我喘息，但不能永久躲藏。”
- “高难星球要求更多决策，而不是单纯让我承受更多伤害。”

EDPCG 的目标就成立。
