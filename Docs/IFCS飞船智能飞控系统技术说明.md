# IFCS 飞船智能飞控系统技术说明

本文档说明本项目中 IFCS 的实现方式。这里的 IFCS 可以理解为 `Integrated Flight Control System`，也就是飞船的智能飞控系统。它的作用不是简单地把按键映射到某个推进器，而是把玩家输入解释成飞船的运动意图，再根据船体质量、速度、角速度、推进器位置和方向，自动计算每个推进器应该输出多少推力。

项目中的 IFCS 主要服务两类场景：

- 工作坊飞行测试：玩家在编辑器/工作坊中测试自己组装的飞船。
- 星际飞行：玩家驾驶飞船在太空中飞行，并可进入星际巡航状态。

核心源码位于：

- `Assets/SpacecraftEditor/Runtime/SpacecraftIfcsMotor.cs`
- `Assets/SpacecraftEditor/Runtime/SpacecraftThrusterAllocator.cs`
- `Assets/SpacecraftEditor/Runtime/KeyboardMouseFlightInput.cs`
- `Assets/SpacecraftEditor/Runtime/SpacecraftFlightTypes.cs`
- `Assets/SpacecraftEditor/Runtime/ThrusterPart.cs`

## 1. 一句话概括

本项目的 IFCS 是一个“运动意图控制器 + 推进器分配器”。

玩家输入的是：

- 我要向前/向后/向左/向右/向上/向下移动。
- 我要俯仰、偏航、滚转。
- 我要刹车。
- 我要加力。

IFCS 输出的是：

- 每个推进器的油门值。
- 内置 RCS 喷口的受力。
- 最终施加到 `Rigidbody` 上的力和力矩。

从程序结构看，完整数据流是：

```text
Keyboard / Mouse
    -> KeyboardMouseFlightInput
    -> SpacecraftFlightCommand
    -> SpacecraftIfcsMotor
    -> desired force / desired torque
    -> SpacecraftThrusterAllocator
    -> ThrusterPart.ApplyThrust / Rigidbody.AddForceAtPosition
```

## 2. 为什么需要 IFCS

如果没有 IFCS，飞船控制通常会变成“按哪个键，就点哪个推进器”。这种方式有几个问题：

1. 玩家需要理解每个推进器的方向和位置。
2. 推进器不对称时，飞船很容易产生额外旋转。
3. 不同船体质量、尺寸和惯性不同，手感很难统一。
4. 太空惯性环境中，刹车和姿态稳定都需要大量辅助逻辑。

IFCS 解决的问题是：

- 玩家只表达意图。
- 系统根据当前物理状态自动修正。
- 推进器布局变化后，系统重新计算控制能力。
- 飞船可以支持辅助模式、惯性模式和直接推进模式。

这也是它适合演讲的地方：它把“玩家控制”和“物理推进器”之间隔了一层智能控制系统。

## 3. 主要类职责

### 3.1 KeyboardMouseFlightInput

文件：

```text
Assets/SpacecraftEditor/Runtime/KeyboardMouseFlightInput.cs
```

职责：

- 读取键盘鼠标输入。
- 生成 `SpacecraftFlightCommand`。
- 处理模式切换请求。
- 处理速度限制调整。
- 维护虚拟摇杆 `vjoy`。

关键输入映射：

| 输入 | 作用 |
| --- | --- |
| W / S | 前后平移 |
| A / D | 左右平移 |
| Space / LeftControl | 上下平移 |
| 鼠标移动 | 俯仰 / 偏航 |
| Q / E | 滚转 |
| X | 刹车 |
| Shift | Boost 加力 |
| C | Coupled / Decoupled 切换 |
| T | Direct 模式切换 |
| B | 星际巡航 |
| 鼠标滚轮 | 调整速度限制 |

它输出的命令结构是 `SpacecraftFlightCommand`：

```csharp
public struct SpacecraftFlightCommand
{
    public Vector3 translation;
    public Vector2 vjoy;
    public float roll;
    public bool brake;
    public bool boost;
}
```

这里要注意：`translation` 不是最终力，`vjoy` 也不是最终旋转。它们只是玩家的控制意图。

### 3.2 SpacecraftIfcsMotor

文件：

```text
Assets/SpacecraftEditor/Runtime/SpacecraftIfcsMotor.cs
```

职责：

- 管理 IFCS 的开关状态。
- 管理飞控模式。
- 根据玩家输入和当前飞船状态计算期望加速度。
- 把线加速度转换为力。
- 把角加速度转换为力矩。
- 调用推进器分配器执行控制。
- 输出遥测数据给 HUD。

这是 IFCS 的主控制器。它的 `FixedUpdate` 是核心循环：

```text
读取输入命令
读取当前局部速度
读取当前局部角速度
计算期望线加速度
计算期望角加速度
线加速度 * 质量 = 期望力
角加速度 * 惯性张量 = 期望力矩
调用推进器分配器
更新遥测
```

### 3.2.1 SpacecraftIfcsMotor 的整体定位

`SpacecraftIfcsMotor` 是整个 IFCS 的中枢。它本身不直接表示某个推进器，也不负责读取最底层的键盘鼠标细节，而是站在“飞控系统”的层级上，把输入、船体、物理状态和推进器分配器连接起来。

它依赖四个主要对象：

```csharp
[SerializeField] Rigidbody shipBody;
[SerializeField] ShipAssembly assembly;
[SerializeField] ShipHullController hullController;
[SerializeField] KeyboardMouseFlightInput flightInput;
```

这四个引用分别代表：

| 字段 | 作用 |
| --- | --- |
| `shipBody` | 飞船的 Unity 刚体，最终所有力和力矩都作用在它上面 |
| `assembly` | 当前飞船装配，里面包含玩家安装的推进器 |
| `hullController` | 当前船体定义，提供质量、尺寸、飞行参数 |
| `flightInput` | 玩家输入系统，提供平移、转向、刹车、Boost 等命令 |

除此之外，它内部还持有一个推进器分配器：

```csharp
readonly SpacecraftThrusterAllocator allocator = new SpacecraftThrusterAllocator();
```

所以 `SpacecraftIfcsMotor` 的定位可以总结为：

```text
它不是输入层，也不是推进器层，而是飞船控制层。
它负责把“玩家想怎么飞”转换成“物理上应该施加什么力和力矩”。
```

答辩时可以这样说：

> SpacecraftIfcsMotor 是 IFCS 的主控制器。它位于输入系统和推进器系统之间，负责读取飞行命令、结合当前刚体状态计算目标力和目标力矩，然后把这些目标交给推进器分配器执行。

### 3.2.2 管理 IFCS 的开关状态

IFCS 的开关通过 `ControlsEnabled` 属性管理：

```csharp
public bool ControlsEnabled
{
    get => controlsEnabled;
    set
    {
        controlsEnabled = value;
        if (flightInput != null)
            flightInput.CaptureEnabled = value;
        if (!value)
            allocator.StopAll();
    }
}
```

这段代码做了三件事：

1. 保存当前 IFCS 是否启用。
2. 同步控制输入系统是否捕获玩家输入。
3. 如果关闭 IFCS，立即停止所有推进器输出。

这里的设计很关键。因为飞船控制关闭时，如果不停止推进器，之前残留的油门可能继续产生力，导致飞船在退出控制后仍然运动。因此关闭 IFCS 时必须调用：

```csharp
allocator.StopAll();
```

`StopAll` 会清空所有目标油门和当前油门，并关闭外部推进器尾焰。

IFCS 开关状态在不同场景中由外部控制器设置。

工作坊飞行进入时：

```csharp
ifcsMotor.ControlsEnabled = true;
```

工作坊飞行退出时：

```csharp
ifcsMotor.ControlsEnabled = false;
ifcsMotor.ResetControllerState();
```

星际飞行中：

```csharp
ifcsMotor.ControlsEnabled = initialized && value;
```

因此 IFCS 自己不决定“什么时候玩家可以控制飞船”，它提供开关接口，由工作坊飞行控制器或星际飞行控制器根据游戏状态启用或禁用。

答辩时可以这样说：

> IFCS 的启停通过 ControlsEnabled 管理。开启时输入系统开始捕获玩家操作；关闭时不但停止读取输入，还会调用推进器分配器 StopAll，清空所有推进器油门，避免退出控制后仍残留推力。

### 3.2.3 管理飞控模式

飞控模式由字段 `assistMode` 保存：

```csharp
[SerializeField] SpacecraftAssistMode assistMode = SpacecraftAssistMode.Coupled;
```

模式定义在 `SpacecraftFlightTypes.cs`：

```csharp
public enum SpacecraftAssistMode
{
    Coupled,
    Decoupled,
    Direct
}
```

切换模式的入口是：

```csharp
public void SetAssistMode(SpacecraftAssistMode mode)
{
    if (mode == SpacecraftAssistMode.Direct && !allowDirectMode)
        mode = SpacecraftAssistMode.Coupled;
    if (assistMode == mode)
        return;
    assistMode = mode;
    allocator.StopAll();
}
```

这个函数有两个重要保护：

1. 如果当前场景不允许 Direct，就把 Direct 强制改回 Coupled。
2. 模式改变时，停止所有推进器，避免上一个模式的油门残留到新模式。

是否允许 Direct 模式由 `Configure` 的最后一个参数决定：

```csharp
public void Configure(
    Rigidbody body,
    ShipAssembly targetAssembly,
    ShipHullController targetHull,
    KeyboardMouseFlightInput input,
    bool directModeAllowed)
```

工作坊飞行传入 `true`：

```csharp
ifcsMotor?.Configure(shipBody, assembly, hullController, flightInput, true);
```

星际飞行传入 `false`：

```csharp
ifcsMotor.Configure(shipBody, assembly, hullController, flightInput, false);
```

这说明项目把 `Direct` 模式定位成工作坊调试功能，而不是正式飞行功能。

模式切换请求在 `Update` 中处理：

```csharp
if (flightInput.ConsumeCoupledToggle())
{
    SetAssistMode(assistMode == SpacecraftAssistMode.Coupled
        ? SpacecraftAssistMode.Decoupled
        : SpacecraftAssistMode.Coupled);
}
```

这段对应 `C` 键，用于在 `Coupled` 和 `Decoupled` 之间切换。

Direct 切换：

```csharp
bool directToggleRequested = flightInput.ConsumeDirectToggle();
if (allowDirectMode && directToggleRequested)
{
    SetAssistMode(assistMode == SpacecraftAssistMode.Direct
        ? SpacecraftAssistMode.Coupled
        : SpacecraftAssistMode.Direct);
}
```

这段对应 `T` 键，并且受 `allowDirectMode` 限制。

答辩时可以这样说：

> 飞控模式由 SpacecraftAssistMode 枚举表示，SpacecraftIfcsMotor 通过 SetAssistMode 统一切换。切换时会清空推进器状态，避免模式残留。Direct 模式还受 allowDirectMode 控制，所以工作坊可以用 Direct 调试，星际飞行则强制使用辅助飞控。

### 3.2.4 根据玩家输入和当前飞船状态计算期望加速度

IFCS 的核心不是直接施力，而是先计算“期望加速度”。

在 `FixedUpdate` 中，系统先读取玩家命令：

```csharp
SpacecraftFlightCommand command = flightInput == null ? default : flightInput.Command;
```

然后读取当前飞船状态：

```csharp
Vector3 localVelocity = transform.InverseTransformDirection(shipBody.velocity);
Vector3 localAngularVelocity = transform.InverseTransformDirection(shipBody.angularVelocity);
```

这里有一个非常重要的细节：它把世界空间速度转换成飞船局部空间速度。

为什么要转到局部空间？

因为玩家输入是以飞船自身为参考系的。比如按 `W` 表示“沿飞船头部方向前进”，不是世界坐标的 Z 轴前进。因此控制计算必须在飞船局部坐标系中完成。

线性加速度由这个函数计算：

```csharp
Vector3 desiredAcceleration = LinearControlEnabled
    ? CalculateLinearAcceleration(command, localVelocity, thrustMultiplier)
    : Vector3.zero;
```

如果 `LinearControlEnabled` 为 `false`，线性控制会被关闭。这主要给星际巡航系统使用。巡航状态下，普通 IFCS 暂停线性控制，让巡航控制器接管高速加速和减速。

角加速度始终由这个函数计算：

```csharp
Vector3 desiredAngularAcceleration = CalculateAngularAcceleration(command, localAngularVelocity);
```

所以常规情况下，IFCS 同时计算：

- `desiredAcceleration`：期望线加速度。
- `desiredAngularAcceleration`：期望角加速度。

答辩时可以这样说：

> IFCS 首先把 Rigidbody 的世界速度转换到飞船局部空间，因为玩家输入是相对飞船自身方向的。然后它根据当前模式计算线性期望加速度，同时根据虚拟摇杆和滚转输入计算角加速度。

### 3.2.5 线性期望加速度的实现

线性加速度计算函数是：

```csharp
Vector3 CalculateLinearAcceleration(
    SpacecraftFlightCommand command,
    Vector3 localVelocity,
    float thrustMultiplier)
```

第一步，根据船体飞行参数和 Boost 倍率计算最大加速度：

```csharp
Vector3 maximumAcceleration = profile.RcsAcceleration * thrustMultiplier;
```

然后根据模式分两种情况。

#### Coupled 模式或刹车状态

```csharp
if (assistMode == SpacecraftAssistMode.Coupled || command.brake)
{
    Vector3 targetVelocity = command.brake
        ? Vector3.zero
        : Vector3.Scale(command.translation, new Vector3(0.72f, 0.72f, 1f)) * speedLimit;
    desiredAcceleration = (targetVelocity - localVelocity) * profile.VelocityResponse;
}
```

这段代码的控制思想是“速度反馈”。

如果没有刹车：

```text
玩家输入方向 * 速度上限 = 目标速度
目标速度 - 当前速度 = 速度误差
速度误差 * 响应系数 = 期望加速度
```

如果按下刹车：

```text
目标速度 = 0
```

所以系统会主动产生与当前速度相反的加速度，让飞船停下来。

`new Vector3(0.72f, 0.72f, 1f)` 表示侧向和上下方向的速度权重比前后方向低。这是一种手感调校，让飞船前进方向更强，侧移和上下移动稍弱。

#### Decoupled 模式

```csharp
else
{
    desiredAcceleration = Vector3.Scale(command.translation, maximumAcceleration);
}
```

Decoupled 不追踪目标速度，而是直接把玩家输入映射成加速度。松开输入后，期望加速度为 0，但飞船已有速度不会被主动抵消。

最后限幅：

```csharp
return ClampComponents(desiredAcceleration, maximumAcceleration);
```

这一步保证每个轴上的加速度不会超过当前船体能力。

答辩时可以这样说：

> 线性控制分为速度控制和加速度控制。Coupled 模式把输入解释成目标速度，并通过速度误差计算加速度；Decoupled 模式直接把输入解释成加速度。两者最后都会按船体 RCS 能力进行限幅。

### 3.2.6 把线加速度转换为力

线性加速度不能直接交给推进器分配器，因为推进器分配器处理的是力和力矩。

转换代码在 `FixedUpdate` 中：

```csharp
Vector3 desiredForce = desiredAcceleration * shipBody.mass;
```

这就是牛顿第二定律：

```text
F = m * a
```

这里使用的是飞船当前 `Rigidbody.mass`。这个质量不是固定写死的，而是由 `ShipAssembly.Recalculate()` 根据船体基础质量和玩家安装的推进器质量重新计算。

因此：

- 飞船越重，同样加速度需要越大的力。
- 推进器装得越多，质量越大，控制需求也会随之变化。
- 同一个输入在不同质量飞船上的实际表现会符合物理直觉。

答辩时可以这样说：

> IFCS 先算期望加速度，再用 Rigidbody 当前质量乘以加速度得到目标力。这样飞船质量变化会自然影响控制需求，而不是所有船都用同样推力手感。

### 3.2.7 姿态期望角加速度的实现

姿态控制函数是：

```csharp
Vector3 CalculateAngularAcceleration(
    SpacecraftFlightCommand command,
    Vector3 localAngularVelocity)
```

它不是直接设置旋转角度，而是控制角速度。

第一步，把最大角速度从角度制转换成弧度制：

```csharp
Vector3 maximumRateRadians = profile.MaximumAngularSpeed * Mathf.Deg2Rad;
```

Unity 的 `Rigidbody.angularVelocity` 使用弧度每秒，所以这里必须转换。

第二步，根据玩家输入计算目标角速度：

```csharp
Vector3 targetRate = new Vector3(
    command.vjoy.x * maximumRateRadians.x,
    command.vjoy.y * maximumRateRadians.y,
    command.roll * maximumRateRadians.z);
```

其中：

- `command.vjoy.x` 控制俯仰方向目标角速度。
- `command.vjoy.y` 控制偏航方向目标角速度。
- `command.roll` 控制滚转方向目标角速度。

第三步，用目标角速度减当前角速度，得到角速度误差：

```csharp
Vector3 acceleration = (targetRate - localAngularVelocity) * profile.AngularRateResponse;
```

这和 Coupled 模式的线性速度控制非常像：

```text
目标角速度 - 当前角速度 = 角速度误差
角速度误差 * 响应系数 = 期望角加速度
```

最后根据最大角加速度限幅：

```csharp
Vector3 maximumAccelerationRadians = profile.MaximumAngularAcceleration * Mathf.Deg2Rad;
return ClampComponents(acceleration, maximumAccelerationRadians);
```

答辩时可以这样说：

> 姿态控制采用角速度闭环。鼠标和滚转输入先映射成目标角速度，IFCS 再比较目标角速度和当前角速度，计算出需要的角加速度，并用最大角加速度限制输出。

### 3.2.8 把角加速度转换为力矩

角加速度同样不能直接分配给推进器，必须转换成目标力矩。

转换函数是：

```csharp
Vector3 AccelerationToTorque(Vector3 localAngularAcceleration)
{
    Quaternion principalRotation = shipBody.inertiaTensorRotation;
    Vector3 principalAcceleration = Quaternion.Inverse(principalRotation) * localAngularAcceleration;
    Vector3 principalTorque = Vector3.Scale(shipBody.inertiaTensor, principalAcceleration);
    return principalRotation * principalTorque;
}
```

这段代码对应旋转动力学：

```text
Torque = Inertia * AngularAcceleration
```

但是刚体的惯性张量并不一定和飞船局部坐标轴完全对齐，所以 Unity 提供了：

- `shipBody.inertiaTensor`
- `shipBody.inertiaTensorRotation`

代码先把局部角加速度转换到“主惯性轴空间”：

```csharp
Vector3 principalAcceleration = Quaternion.Inverse(principalRotation) * localAngularAcceleration;
```

再逐轴乘以惯性张量：

```csharp
Vector3 principalTorque = Vector3.Scale(shipBody.inertiaTensor, principalAcceleration);
```

最后把力矩转换回飞船局部空间：

```csharp
return principalRotation * principalTorque;
```

为什么要这样做？

因为不同飞船的质量分布不同。长条形飞船、圆盘形飞船、左右不对称飞船，它们绕不同轴旋转的难易程度不同。使用惯性张量可以让姿态控制更符合刚体物理。

答辩时可以这样说：

> 角加速度到力矩的转换使用了 Rigidbody 的惯性张量和惯性张量旋转。这样 IFCS 不会把飞船当成一个简单质点，而是考虑船体质量分布，不同形状的飞船会有不同旋转响应。

### 3.2.9 调用推进器分配器执行控制

当目标力和目标力矩都计算完成后，`SpacecraftIfcsMotor` 调用：

```csharp
float authority = allocator.SolveAndApply(
    shipBody,
    transform,
    desiredForce,
    desiredTorque,
    torqueWeight,
    thrustMultiplier,
    Time.fixedDeltaTime);
```

传入参数含义：

| 参数 | 含义 |
| --- | --- |
| `shipBody` | 要施力的刚体 |
| `transform` | 飞船坐标系，用于局部/世界空间转换 |
| `desiredForce` | IFCS 想要的局部总力 |
| `desiredTorque` | IFCS 想要的局部总力矩 |
| `torqueWeight` | 求解时力矩的重要程度 |
| `thrustMultiplier` | Boost 推力倍率 |
| `Time.fixedDeltaTime` | 物理帧时间，用于油门平滑 |

`SolveAndApply` 做两件事：

1. `Solve`：计算每个 actuator 的目标油门。
2. `Apply`：把油门实际应用到 Rigidbody 和推进器 VFX。

返回值 `authority` 是控制权威度，表示当前推进器系统满足目标控制请求的程度。

调用前，IFCS 还会检查推进器分配器是否匹配当前装配：

```csharp
if (!allocator.Matches(assembly, hullController == null ? null : hullController.CurrentHull))
    RefreshProfileAndAllocator();
```

这保证玩家改变推进器或船体后，IFCS 会重新构建 actuator 列表。

答辩时可以这样说：

> SpacecraftIfcsMotor 本身不直接决定每个推进器开多少，而是把目标力和目标力矩交给 SpacecraftThrusterAllocator。分配器根据推进器布局求解油门，并返回 controlAuthority 作为控制能力反馈。

### 3.2.10 输出遥测数据给 HUD

IFCS 每个物理帧都会更新遥测数据：

```csharp
void UpdateTelemetry(Vector3 requestedForce, Vector3 requestedTorque, float authority)
{
    Telemetry = new SpacecraftControlTelemetry
    {
        localVelocity = shipBody == null
            ? Vector3.zero
            : transform.InverseTransformDirection(shipBody.velocity),
        localAngularVelocity = shipBody == null
            ? Vector3.zero
            : transform.InverseTransformDirection(shipBody.angularVelocity),
        requestedLocalForce = requestedForce,
        requestedLocalTorque = requestedTorque,
        speedLimit = speedLimit,
        controlAuthority = authority,
        boostRatio = BoostRatio,
        assistMode = assistMode
    };
}
```

遥测结构包括：

- 当前局部速度。
- 当前局部角速度。
- 本帧请求的局部力。
- 本帧请求的局部力矩。
- 当前速度限制。
- 当前控制权威度。
- Boost 剩余比例。
- 当前飞控模式。

HUD 通过：

```csharp
SpacecraftControlTelemetry telemetry = flight.Telemetry;
```

拿到这些数据，然后显示：

- 当前模式。
- 控制权威百分比。
- 速度限制。
- Boost 百分比。
- 虚拟摇杆位置。

为什么遥测重要？

因为 IFCS 是一个中间控制系统。如果没有遥测，玩家和开发者很难知道：

- 当前模式是什么。
- 系统是否正在请求很大的力。
- 推进器布局是否足够满足控制请求。
- Boost 是否耗尽。

答辩时可以这样说：

> Telemetry 是 IFCS 对外暴露的状态快照。HUD 不直接读取推进器，也不重新计算飞控状态，而是读取 SpacecraftIfcsMotor 生成的统一遥测数据。这让显示层和控制层解耦。

### 3.2.11 SpacecraftIfcsMotor 的生命周期

`SpacecraftIfcsMotor` 的运行过程可以按 Unity 生命周期理解。

#### Awake

```csharp
void Awake()
{
    ResolveReferences();
}
```

作用：

- 自动查找 `Rigidbody`。
- 自动查找 `ShipAssembly`。
- 自动查找 `ShipHullController`。
- 自动查找 `KeyboardMouseFlightInput`。

这样组件即使没有在 Inspector 中手动拖引用，也可以尽量自动绑定。

#### OnEnable

```csharp
void OnEnable()
{
    ResolveReferences();
    if (assembly != null)
        assembly.AssemblyChanged += HandleAssemblyChanged;
    if (hullController != null)
        hullController.HullChanged += HandleHullChanged;
}
```

作用：

- 订阅飞船装配变化事件。
- 订阅船体变化事件。

#### Start

```csharp
void Start()
{
    RefreshProfileAndAllocator();
}
```

作用：

- 获取当前船体飞行参数。
- 初始化速度限制和 Boost。
- 构建推进器分配器。

#### Update

```text
处理模式切换
处理速度限制调整
```

`Update` 用于处理输入事件，因为按键按下、鼠标滚轮这类输入更适合在普通帧中读取。

#### FixedUpdate

```text
执行物理控制
计算加速度
计算力和力矩
调用推进器分配器
更新遥测
```

`FixedUpdate` 用于物理控制，因为 Unity 的 Rigidbody 物理更新发生在固定时间步。

#### OnDisable

```csharp
void OnDisable()
{
    if (assembly != null)
        assembly.AssemblyChanged -= HandleAssemblyChanged;
    if (hullController != null)
        hullController.HullChanged -= HandleHullChanged;
    allocator.StopAll();
}
```

作用：

- 取消事件订阅。
- 停止所有推进器。

答辩时可以这样说：

> 这个类把输入处理放在 Update，把物理控制放在 FixedUpdate，并在 OnEnable/OnDisable 中管理事件订阅和推进器清理。这符合 Unity 的典型物理控制结构。

### 3.2.12 七项职责和代码对应关系

| 职责 | 主要实现位置 | 核心机制 |
| --- | --- | --- |
| 管理 IFCS 开关状态 | `ControlsEnabled` | 同步输入捕获，关闭时 `allocator.StopAll()` |
| 管理飞控模式 | `assistMode`、`SetAssistMode`、`Update` | 支持 Coupled/Decoupled/Direct，限制 Direct 场景 |
| 根据输入和飞船状态计算期望加速度 | `FixedUpdate`、`CalculateLinearAcceleration`、`CalculateAngularAcceleration` | 读取 `SpacecraftFlightCommand` 和局部速度/角速度 |
| 把线加速度转换为力 | `desiredAcceleration * shipBody.mass` | 使用牛顿第二定律 |
| 把角加速度转换为力矩 | `AccelerationToTorque` | 使用惯性张量 |
| 调用推进器分配器执行控制 | `allocator.SolveAndApply` | 分配目标力/力矩到推进器油门 |
| 输出遥测数据给 HUD | `UpdateTelemetry`、`Telemetry` 属性 | 保存速度、角速度、请求力、请求力矩、模式、Boost、控制权威 |

### 3.2.13 SpacecraftIfcsMotor 关键代码逐段解释

这一节按源码顺序解释 `SpacecraftIfcsMotor` 的关键代码。答辩时如果老师要求“不要只讲概念，要讲代码怎么实现”，可以重点讲这一节。

#### 3.2.13.1 类声明和组件约束

源码：

```csharp
[DefaultExecutionOrder(-250)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class SpacecraftIfcsMotor : MonoBehaviour
```

逐行解释：

```csharp
[DefaultExecutionOrder(-250)]
```

表示这个组件的执行顺序比默认脚本更早。IFCS 需要较早读取输入、更新飞控状态，并在物理帧中稳定地产生控制输出。负数执行顺序可以减少它被其他普通脚本延后执行的风险。

```csharp
[DisallowMultipleComponent]
```

表示同一个 GameObject 上不能挂多个 `SpacecraftIfcsMotor`。这是合理的，因为一艘飞船只能有一个主飞控系统。如果允许多个 IFCS 同时控制同一个刚体，就可能重复施力或互相冲突。

```csharp
[RequireComponent(typeof(Rigidbody))]
```

表示挂载 IFCS 的对象必须有 `Rigidbody`。因为 IFCS 最终要对刚体施加力和力矩，没有 `Rigidbody` 就无法工作。

```csharp
public sealed class SpacecraftIfcsMotor : MonoBehaviour
```

`sealed` 表示这个类不允许被继承。这里可以避免其他子类修改核心飞控行为，保证控制逻辑集中在一个类中。

答辩说法：

> 这几个 Attribute 保证 IFCS 的运行环境正确：一艘飞船只有一个 IFCS，必须有 Rigidbody，并且执行顺序靠前，适合做底层飞控控制器。

#### 3.2.13.2 引用字段

源码：

```csharp
[Header("References")]
[SerializeField] Rigidbody shipBody;
[SerializeField] ShipAssembly assembly;
[SerializeField] ShipHullController hullController;
[SerializeField] KeyboardMouseFlightInput flightInput;
```

解释：

```csharp
Rigidbody shipBody;
```

飞船刚体。它提供当前速度、角速度、质量、惯性张量，也是最终受力对象。

```csharp
ShipAssembly assembly;
```

飞船装配数据。它包含玩家安装的推进器列表，推进器分配器会根据它构建 actuator。

```csharp
ShipHullController hullController;
```

船体控制器。它提供当前船体定义，例如船体尺寸、基础质量和 `ShipFlightProfile`。

```csharp
KeyboardMouseFlightInput flightInput;
```

输入系统。它把键盘鼠标转成 `SpacecraftFlightCommand`，IFCS 只读取这个抽象命令，而不直接处理每个按键。

设计意义：

```text
SpacecraftIfcsMotor 不直接创建这些对象，而是引用外部组件。
这样 IFCS 可以同时用于工作坊飞船和星际飞船。
```

#### 3.2.13.3 控制参数和运行状态

源码：

```csharp
[Header("Allocation")]
[SerializeField, Min(0.1f)] float torqueWeight = 1.35f;
[SerializeField] bool allowDirectMode = true;
[SerializeField] SpacecraftAssistMode assistMode = SpacecraftAssistMode.Coupled;

readonly SpacecraftThrusterAllocator allocator = new SpacecraftThrusterAllocator();
ShipFlightProfile profile;
float speedLimit;
float boostSeconds;
bool controlsEnabled;
```

解释：

```csharp
float torqueWeight = 1.35f;
```

推进器分配时，力和力矩需要一起求解。`torqueWeight` 用来控制“旋转控制”的权重。值越大，求解器越倾向于满足姿态力矩需求。

```csharp
bool allowDirectMode = true;
```

控制是否允许 Direct 模式。工作坊允许 Direct，星际飞行不允许 Direct。

```csharp
SpacecraftAssistMode assistMode = SpacecraftAssistMode.Coupled;
```

当前飞控模式，默认是辅助模式 `Coupled`。

```csharp
readonly SpacecraftThrusterAllocator allocator = new SpacecraftThrusterAllocator();
```

推进器分配器。它不是 MonoBehaviour，不挂在场景里，而是 IFCS 内部持有的纯逻辑对象。

```csharp
ShipFlightProfile profile;
```

当前船体的飞行参数，包括 RCS 加速度、最大角速度、Boost 参数等。

```csharp
float speedLimit;
```

Coupled 模式下的目标速度上限。鼠标滚轮可以调整它。

```csharp
float boostSeconds;
```

Boost 剩余秒数。按住 Shift 消耗，不按时恢复。

```csharp
bool controlsEnabled;
```

IFCS 是否实际接管控制。

#### 3.2.13.4 ControlsEnabled 属性

源码：

```csharp
public bool ControlsEnabled
{
    get => controlsEnabled;
    set
    {
        controlsEnabled = value;
        if (flightInput != null)
            flightInput.CaptureEnabled = value;
        if (!value)
            allocator.StopAll();
    }
}
```

逐行解释：

```csharp
get => controlsEnabled;
```

外部可以读取当前 IFCS 是否启用。

```csharp
controlsEnabled = value;
```

保存新的启用状态。

```csharp
if (flightInput != null)
    flightInput.CaptureEnabled = value;
```

IFCS 开启时，输入系统开始捕获玩家输入；IFCS 关闭时，输入系统也停止捕获。这样控制权和输入捕获状态保持一致。

```csharp
if (!value)
    allocator.StopAll();
```

如果关闭 IFCS，立即关闭所有推进器。这里非常重要，因为推进器分配器内部有 `targetThrottles` 和 `appliedThrottles`，如果不清空，可能出现退出飞行后推进器仍有残留输出。

答辩说法：

> ControlsEnabled 不只是一个布尔开关，它还同步输入系统，并在关闭时清理推进器状态，所以它是 IFCS 的安全启停入口。

#### 3.2.13.5 对外状态属性

源码：

```csharp
public SpacecraftAssistMode AssistMode => assistMode;
public bool LinearControlEnabled { get; set; } = true;
public float SpeedLimit => speedLimit;
public float BoostRatio => profile == null ? 0f : Mathf.Clamp01(boostSeconds / profile.BoostCapacitySeconds);
public float CurrentThrottle => allocator.MaximumAppliedThrottle;
public SpacecraftControlTelemetry Telemetry { get; private set; }
public KeyboardMouseFlightInput FlightInput => flightInput;
```

解释：

```csharp
public SpacecraftAssistMode AssistMode => assistMode;
```

给外部读取当前模式，例如 HUD 和飞船控制器。

```csharp
public bool LinearControlEnabled { get; set; } = true;
```

允许外部临时关闭线性控制。星际巡航使用它来接管加速和减速，同时保留 IFCS 的姿态控制。

```csharp
public float SpeedLimit => speedLimit;
```

给 HUD 或其他系统读取当前速度限制。

```csharp
public float BoostRatio => profile == null ? 0f : Mathf.Clamp01(boostSeconds / profile.BoostCapacitySeconds);
```

把 Boost 剩余秒数转换成 `0..1` 的比例，方便 UI 显示百分比。

```csharp
public float CurrentThrottle => allocator.MaximumAppliedThrottle;
```

读取当前所有推进器中最大的实际油门，可用于 HUD 显示整体推力强度。

```csharp
public SpacecraftControlTelemetry Telemetry { get; private set; }
```

遥测数据只能由 IFCS 内部写入，外部只能读取。这保证 HUD 不能误改飞控状态。

#### 3.2.13.6 Configure 方法

源码：

```csharp
public void Configure(
    Rigidbody body,
    ShipAssembly targetAssembly,
    ShipHullController targetHull,
    KeyboardMouseFlightInput input,
    bool directModeAllowed)
{
    if (assembly != null)
        assembly.AssemblyChanged -= HandleAssemblyChanged;
    if (hullController != null)
        hullController.HullChanged -= HandleHullChanged;

    shipBody = body;
    assembly = targetAssembly;
    hullController = targetHull;
    flightInput = input;
    allowDirectMode = directModeAllowed;

    if (assembly != null)
        assembly.AssemblyChanged += HandleAssemblyChanged;
    if (hullController != null)
        hullController.HullChanged += HandleHullChanged;
    RefreshProfileAndAllocator();
}
```

这段代码用于让外部控制器重新配置 IFCS。工作坊和星际飞行都会调用它。

第一段：

```csharp
if (assembly != null)
    assembly.AssemblyChanged -= HandleAssemblyChanged;
if (hullController != null)
    hullController.HullChanged -= HandleHullChanged;
```

先取消旧对象上的事件订阅，避免重复订阅或继续监听旧飞船。

第二段：

```csharp
shipBody = body;
assembly = targetAssembly;
hullController = targetHull;
flightInput = input;
allowDirectMode = directModeAllowed;
```

更新 IFCS 的核心引用和 Direct 模式权限。

第三段：

```csharp
if (assembly != null)
    assembly.AssemblyChanged += HandleAssemblyChanged;
if (hullController != null)
    hullController.HullChanged += HandleHullChanged;
```

订阅新装配和新船体的变化事件。

最后：

```csharp
RefreshProfileAndAllocator();
```

立刻刷新飞行参数和推进器分配器。否则 IFCS 可能还在使用旧船体和旧推进器列表。

答辩说法：

> Configure 是 IFCS 的重新绑定入口。它先解绑旧事件，再绑定新引用，然后重新构建飞行参数和推进器分配器，保证同一个 IFCS 可以适配工作坊和星际场景中的不同飞船。

#### 3.2.13.7 SetAssistMode 方法

源码：

```csharp
public void SetAssistMode(SpacecraftAssistMode mode)
{
    if (mode == SpacecraftAssistMode.Direct && !allowDirectMode)
        mode = SpacecraftAssistMode.Coupled;
    if (assistMode == mode)
        return;
    assistMode = mode;
    allocator.StopAll();
}
```

逐行解释：

```csharp
if (mode == SpacecraftAssistMode.Direct && !allowDirectMode)
    mode = SpacecraftAssistMode.Coupled;
```

如果请求切到 Direct，但当前场景不允许 Direct，就强制改成 Coupled。星际飞行就是这样限制的。

```csharp
if (assistMode == mode)
    return;
```

如果模式没有变化，不做任何操作。

```csharp
assistMode = mode;
```

保存新模式。

```csharp
allocator.StopAll();
```

切换模式时停止所有推进器，避免模式切换瞬间保留旧模式下的油门。

举例：

```text
如果 Coupled 模式正在主动刹车，此时切到 Decoupled，
如果不清空油门，飞船可能继续受到上一模式的刹车推力。
```

#### 3.2.13.8 ResetControllerState 方法

源码：

```csharp
public void ResetControllerState()
{
    allocator.StopAll();
    if (flightInput != null)
    {
        flightInput.ResetVJoy();
        flightInput.ClearTransientRequests();
    }
}
```

解释：

```csharp
allocator.StopAll();
```

停止所有推进器。

```csharp
flightInput.ResetVJoy();
```

重置鼠标虚拟摇杆，避免再次进入飞行时飞船继承上一次的转向输入。

```csharp
flightInput.ClearTransientRequests();
```

清空一次性输入请求，例如模式切换、巡航触发、速度限制变化等。

答辩说法：

> ResetControllerState 用于退出飞行或重置飞行时清理控制状态。它不只停推进器，还会清空虚拟摇杆和一次性输入事件，避免状态残留。

#### 3.2.13.9 Update 方法：处理非物理输入事件

源码：

```csharp
void Update()
{
    if (flightInput == null)
        return;

    if (flightInput.ConsumeCoupledToggle())
    {
        SetAssistMode(assistMode == SpacecraftAssistMode.Coupled
            ? SpacecraftAssistMode.Decoupled
            : SpacecraftAssistMode.Coupled);
    }

    bool directToggleRequested = flightInput.ConsumeDirectToggle();
    if (allowDirectMode && directToggleRequested)
    {
        SetAssistMode(assistMode == SpacecraftAssistMode.Direct
            ? SpacecraftAssistMode.Coupled
            : SpacecraftAssistMode.Direct);
    }

    if (profile != null)
    {
        speedLimit = Mathf.Clamp(
            speedLimit + flightInput.ConsumeSpeedLimitDelta(),
            profile.MinimumSpeedLimit,
            profile.MaximumSpeedLimit);
    }
}
```

解释：

```csharp
if (flightInput == null)
    return;
```

没有输入组件时，无法处理模式切换和速度限制调整。

```csharp
if (flightInput.ConsumeCoupledToggle())
```

读取 `C` 键产生的切换请求。这里使用 `Consume` 模式，表示这个请求读取一次后会被清掉，避免一个按键事件被重复处理。

```csharp
SetAssistMode(assistMode == SpacecraftAssistMode.Coupled
    ? SpacecraftAssistMode.Decoupled
    : SpacecraftAssistMode.Coupled);
```

如果当前是 Coupled，就切到 Decoupled；否则切回 Coupled。

```csharp
bool directToggleRequested = flightInput.ConsumeDirectToggle();
if (allowDirectMode && directToggleRequested)
```

读取 `T` 键产生的 Direct 切换请求，但只有 `allowDirectMode` 为真时才处理。

```csharp
speedLimit = Mathf.Clamp(
    speedLimit + flightInput.ConsumeSpeedLimitDelta(),
    profile.MinimumSpeedLimit,
    profile.MaximumSpeedLimit);
```

鼠标滚轮调整速度限制，但必须限制在当前船体飞行参数允许的最小和最大速度之间。

为什么这些逻辑放在 `Update` 而不是 `FixedUpdate`？

因为按键按下、鼠标滚轮这类输入事件是逐渲染帧产生的。放在 `Update` 中处理更符合 Unity 输入系统习惯。真正的物理施力则放在 `FixedUpdate`。

#### 3.2.13.10 FixedUpdate 方法：IFCS 的核心物理循环

源码：

```csharp
void FixedUpdate()
{
    if (!controlsEnabled || shipBody == null || assembly == null || profile == null
        || assistMode == SpacecraftAssistMode.Direct)
    {
        allocator.StopAll();
        UpdateTelemetry(Vector3.zero, Vector3.zero, 1f);
        return;
    }

    if (!allocator.Matches(assembly, hullController == null ? null : hullController.CurrentHull))
        RefreshProfileAndAllocator();

    SpacecraftFlightCommand command = flightInput == null ? default : flightInput.Command;
    bool boosting = command.boost && boostSeconds > 0.001f;
    float thrustMultiplier = boosting ? profile.BoostMultiplier : 1f;
    UpdateBoostReserve(boosting);

    Vector3 localVelocity = transform.InverseTransformDirection(shipBody.velocity);
    Vector3 localAngularVelocity = transform.InverseTransformDirection(shipBody.angularVelocity);
    Vector3 desiredAcceleration = LinearControlEnabled
        ? CalculateLinearAcceleration(command, localVelocity, thrustMultiplier)
        : Vector3.zero;
    Vector3 desiredAngularAcceleration = CalculateAngularAcceleration(command, localAngularVelocity);
    Vector3 desiredForce = desiredAcceleration * shipBody.mass;
    Vector3 desiredTorque = AccelerationToTorque(desiredAngularAcceleration);

    float authority = allocator.SolveAndApply(
        shipBody,
        transform,
        desiredForce,
        desiredTorque,
        torqueWeight,
        thrustMultiplier,
        Time.fixedDeltaTime);
    UpdateTelemetry(desiredForce, desiredTorque, authority);
}
```

这是最重要的一段，可以分成 7 步理解。

第一步：检查 IFCS 是否可以运行。

```csharp
if (!controlsEnabled || shipBody == null || assembly == null || profile == null
    || assistMode == SpacecraftAssistMode.Direct)
```

只要满足以下任一条件，就不执行 IFCS：

- 控制未开启。
- 没有刚体。
- 没有飞船装配。
- 没有飞行参数。
- 当前是 Direct 模式。

如果不能运行：

```csharp
allocator.StopAll();
UpdateTelemetry(Vector3.zero, Vector3.zero, 1f);
return;
```

系统停止推进器，并输出空请求遥测。

第二步：检查推进器分配器是否需要重建。

```csharp
if (!allocator.Matches(assembly, hullController == null ? null : hullController.CurrentHull))
    RefreshProfileAndAllocator();
```

如果当前装配或船体和分配器记录的不一致，就重新构建。这样可以适配玩家添加/删除推进器或切换船体。

第三步：读取输入命令和 Boost。

```csharp
SpacecraftFlightCommand command = flightInput == null ? default : flightInput.Command;
bool boosting = command.boost && boostSeconds > 0.001f;
float thrustMultiplier = boosting ? profile.BoostMultiplier : 1f;
UpdateBoostReserve(boosting);
```

如果没有输入组件，就使用默认命令。Boost 只有在玩家按下 Boost 且储备大于 0 时生效。

第四步：把速度转换到飞船局部空间。

```csharp
Vector3 localVelocity = transform.InverseTransformDirection(shipBody.velocity);
Vector3 localAngularVelocity = transform.InverseTransformDirection(shipBody.angularVelocity);
```

原因是玩家输入是以飞船自身方向为参考的。

第五步：计算期望加速度和期望角加速度。

```csharp
Vector3 desiredAcceleration = LinearControlEnabled
    ? CalculateLinearAcceleration(command, localVelocity, thrustMultiplier)
    : Vector3.zero;
Vector3 desiredAngularAcceleration = CalculateAngularAcceleration(command, localAngularVelocity);
```

`LinearControlEnabled` 可以让巡航系统临时关闭普通线性控制。角控制仍然继续计算。

第六步：把加速度转换成物理目标。

```csharp
Vector3 desiredForce = desiredAcceleration * shipBody.mass;
Vector3 desiredTorque = AccelerationToTorque(desiredAngularAcceleration);
```

线性部分用 `F = m * a`。旋转部分用惯性张量转换。

第七步：调用推进器分配器并更新遥测。

```csharp
float authority = allocator.SolveAndApply(...);
UpdateTelemetry(desiredForce, desiredTorque, authority);
```

`SolveAndApply` 负责求解油门并实际施力，`UpdateTelemetry` 把本帧结果保存给 HUD。

答辩说法：

> FixedUpdate 是 IFCS 的核心循环，因为它涉及 Rigidbody 物理。它先判断飞控是否可用，再读取输入和刚体状态，计算期望线加速度和角加速度，转换成目标力和目标力矩，最后交给推进器分配器求解并施力。

#### 3.2.13.11 CalculateLinearAcceleration 方法

源码：

```csharp
Vector3 CalculateLinearAcceleration(
    SpacecraftFlightCommand command,
    Vector3 localVelocity,
    float thrustMultiplier)
{
    Vector3 maximumAcceleration = profile.RcsAcceleration * thrustMultiplier;
    Vector3 desiredAcceleration;
    if (assistMode == SpacecraftAssistMode.Coupled || command.brake)
    {
        Vector3 targetVelocity = command.brake
            ? Vector3.zero
            : Vector3.Scale(command.translation, new Vector3(0.72f, 0.72f, 1f)) * speedLimit;
        desiredAcceleration = (targetVelocity - localVelocity) * profile.VelocityResponse;
    }
    else
    {
        desiredAcceleration = Vector3.Scale(command.translation, maximumAcceleration);
    }

    return ClampComponents(desiredAcceleration, maximumAcceleration);
}
```

第一行：

```csharp
Vector3 maximumAcceleration = profile.RcsAcceleration * thrustMultiplier;
```

计算每个轴允许的最大加速度。`profile.RcsAcceleration` 是船体基础能力，`thrustMultiplier` 是 Boost 倍率。

Coupled 分支：

```csharp
if (assistMode == SpacecraftAssistMode.Coupled || command.brake)
```

只要是 Coupled 模式，或者玩家按了刹车，都使用目标速度控制。

目标速度：

```csharp
Vector3 targetVelocity = command.brake
    ? Vector3.zero
    : Vector3.Scale(command.translation, new Vector3(0.72f, 0.72f, 1f)) * speedLimit;
```

如果刹车，目标速度是零；否则根据输入方向和速度上限得到目标速度。

速度误差控制：

```csharp
desiredAcceleration = (targetVelocity - localVelocity) * profile.VelocityResponse;
```

如果当前速度低于目标速度，输出加速；如果当前速度高于目标速度，输出减速；如果方向相反，输出反向加速度。

Decoupled 分支：

```csharp
desiredAcceleration = Vector3.Scale(command.translation, maximumAcceleration);
```

玩家输入直接乘最大加速度，不看当前速度。因此它不会主动消除惯性。

最后：

```csharp
return ClampComponents(desiredAcceleration, maximumAcceleration);
```

防止速度误差过大时，请求超过船体能力。

#### 3.2.13.12 CalculateAngularAcceleration 方法

源码：

```csharp
Vector3 CalculateAngularAcceleration(
    SpacecraftFlightCommand command,
    Vector3 localAngularVelocity)
{
    Vector3 maximumRateRadians = profile.MaximumAngularSpeed * Mathf.Deg2Rad;
    Vector3 targetRate = new Vector3(
        command.vjoy.x * maximumRateRadians.x,
        command.vjoy.y * maximumRateRadians.y,
        command.roll * maximumRateRadians.z);
    Vector3 acceleration = (targetRate - localAngularVelocity) * profile.AngularRateResponse;
    Vector3 maximumAccelerationRadians = profile.MaximumAngularAcceleration * Mathf.Deg2Rad;
    return ClampComponents(acceleration, maximumAccelerationRadians);
}
```

解释：

```csharp
Vector3 maximumRateRadians = profile.MaximumAngularSpeed * Mathf.Deg2Rad;
```

船体配置中的最大角速度通常以角度每秒表达，但 Unity 刚体角速度是弧度每秒，所以要乘 `Mathf.Deg2Rad`。

```csharp
Vector3 targetRate = new Vector3(
    command.vjoy.x * maximumRateRadians.x,
    command.vjoy.y * maximumRateRadians.y,
    command.roll * maximumRateRadians.z);
```

把玩家输入映射成目标角速度：

- 虚拟摇杆 X 控制一个旋转轴。
- 虚拟摇杆 Y 控制另一个旋转轴。
- `roll` 控制滚转轴。

```csharp
Vector3 acceleration = (targetRate - localAngularVelocity) * profile.AngularRateResponse;
```

这是角速度反馈控制。当前角速度越偏离目标角速度，系统请求的角加速度越大。

```csharp
Vector3 maximumAccelerationRadians = profile.MaximumAngularAcceleration * Mathf.Deg2Rad;
return ClampComponents(acceleration, maximumAccelerationRadians);
```

把最大角加速度也转成弧度单位，并对输出限幅。

答辩说法：

> 姿态控制没有直接设置旋转，而是做角速度闭环。输入先变成目标角速度，再和 Rigidbody 当前角速度比较，算出角加速度，这样飞船旋转仍然符合物理。

#### 3.2.13.13 AccelerationToTorque 方法

源码：

```csharp
Vector3 AccelerationToTorque(Vector3 localAngularAcceleration)
{
    Quaternion principalRotation = shipBody.inertiaTensorRotation;
    Vector3 principalAcceleration = Quaternion.Inverse(principalRotation) * localAngularAcceleration;
    Vector3 principalTorque = Vector3.Scale(shipBody.inertiaTensor, principalAcceleration);
    return principalRotation * principalTorque;
}
```

逐行解释：

```csharp
Quaternion principalRotation = shipBody.inertiaTensorRotation;
```

读取刚体主惯性轴相对局部坐标系的旋转。

```csharp
Vector3 principalAcceleration = Quaternion.Inverse(principalRotation) * localAngularAcceleration;
```

把局部角加速度转换到主惯性轴空间。

```csharp
Vector3 principalTorque = Vector3.Scale(shipBody.inertiaTensor, principalAcceleration);
```

在主惯性轴空间中，惯性张量可以逐轴相乘。这里就是：

```text
Torque.x = Inertia.x * AngularAcceleration.x
Torque.y = Inertia.y * AngularAcceleration.y
Torque.z = Inertia.z * AngularAcceleration.z
```

```csharp
return principalRotation * principalTorque;
```

把主惯性轴空间中的力矩转回飞船局部空间。

答辩说法：

> Unity 的惯性张量有自己的主轴方向，所以不能直接用局部角加速度逐轴相乘。代码先把角加速度转到主惯性轴空间，乘以 inertiaTensor，再转回局部空间，得到推进器分配器需要的目标力矩。

#### 3.2.13.14 UpdateBoostReserve 方法

源码：

```csharp
void UpdateBoostReserve(bool boosting)
{
    if (boosting)
        boostSeconds = Mathf.Max(0f, boostSeconds - Time.fixedDeltaTime);
    else
        boostSeconds = Mathf.Min(
            profile.BoostCapacitySeconds,
            boostSeconds + profile.BoostRegenerationPerSecond * Time.fixedDeltaTime);
}
```

解释：

```csharp
if (boosting)
    boostSeconds = Mathf.Max(0f, boostSeconds - Time.fixedDeltaTime);
```

Boost 激活时，每个物理帧按时间消耗。`Mathf.Max(0f, ...)` 保证不会变成负数。

```csharp
else
    boostSeconds = Mathf.Min(
        profile.BoostCapacitySeconds,
        boostSeconds + profile.BoostRegenerationPerSecond * Time.fixedDeltaTime);
```

没有 Boost 时，储备按恢复速度增加，但不能超过最大容量。

设计意义：

```text
Boost 不是无限加速，而是一个带容量和恢复速度的资源。
```

#### 3.2.13.15 UpdateTelemetry 方法

源码：

```csharp
void UpdateTelemetry(Vector3 requestedForce, Vector3 requestedTorque, float authority)
{
    Telemetry = new SpacecraftControlTelemetry
    {
        localVelocity = shipBody == null
            ? Vector3.zero
            : transform.InverseTransformDirection(shipBody.velocity),
        localAngularVelocity = shipBody == null
            ? Vector3.zero
            : transform.InverseTransformDirection(shipBody.angularVelocity),
        requestedLocalForce = requestedForce,
        requestedLocalTorque = requestedTorque,
        speedLimit = speedLimit,
        controlAuthority = authority,
        boostRatio = BoostRatio,
        assistMode = assistMode
    };
}
```

解释：

```csharp
Telemetry = new SpacecraftControlTelemetry
```

每次更新时创建一份新的遥测快照。

```csharp
localVelocity = shipBody == null
    ? Vector3.zero
    : transform.InverseTransformDirection(shipBody.velocity)
```

记录飞船局部速度。如果刚体不存在，就返回零向量，避免空引用。

```csharp
localAngularVelocity = shipBody == null
    ? Vector3.zero
    : transform.InverseTransformDirection(shipBody.angularVelocity)
```

记录局部角速度。

```csharp
requestedLocalForce = requestedForce;
requestedLocalTorque = requestedTorque;
```

记录本帧 IFCS 请求的力和力矩。这个信息对调试很有价值。

```csharp
controlAuthority = authority;
```

记录推进器分配器返回的控制权威度。

```csharp
boostRatio = BoostRatio;
assistMode = assistMode;
```

记录 Boost 比例和当前模式，供 HUD 展示。

答辩说法：

> UpdateTelemetry 把 IFCS 内部状态整理成一个只读快照。HUD 只依赖这份快照，不需要知道 IFCS 内部算法，所以显示层和控制层是解耦的。

#### 3.2.13.16 ResolveReferences 方法

源码：

```csharp
void ResolveReferences()
{
    if (shipBody == null)
        shipBody = GetComponent<Rigidbody>();
    if (assembly == null)
        assembly = GetComponent<ShipAssembly>();
    if (hullController == null)
        hullController = GetComponentInChildren<ShipHullController>(true);
    if (flightInput == null)
        flightInput = GetComponent<KeyboardMouseFlightInput>();
}
```

解释：

这个方法用于自动补齐组件引用。

```csharp
shipBody = GetComponent<Rigidbody>();
```

从当前 GameObject 获取刚体。

```csharp
assembly = GetComponent<ShipAssembly>();
```

从当前 GameObject 获取飞船装配组件。

```csharp
hullController = GetComponentInChildren<ShipHullController>(true);
```

从子物体中查找船体控制器，包括未激活对象。

```csharp
flightInput = GetComponent<KeyboardMouseFlightInput>();
```

获取输入组件。

设计意义：

```text
既支持 Inspector 手动拖引用，也支持运行时自动查找。
```

#### 3.2.13.17 RefreshProfileAndAllocator 方法

源码：

```csharp
void RefreshProfileAndAllocator()
{
    ResolveReferences();
    ShipHullDefinition hull = hullController == null ? null : hullController.CurrentHull;
    profile = hull == null ? ShipFlightProfile.CreateForHull(string.Empty) : hull.FlightProfile;
    speedLimit = speedLimit <= 0f
        ? profile.DefaultSpeedLimit
        : Mathf.Clamp(speedLimit, profile.MinimumSpeedLimit, profile.MaximumSpeedLimit);
    boostSeconds = boostSeconds <= 0f ? profile.BoostCapacitySeconds : Mathf.Min(boostSeconds, profile.BoostCapacitySeconds);
    allocator.Rebuild(assembly, hull);
}
```

逐行解释：

```csharp
ResolveReferences();
```

先确保引用完整。

```csharp
ShipHullDefinition hull = hullController == null ? null : hullController.CurrentHull;
```

获取当前船体定义。

```csharp
profile = hull == null ? ShipFlightProfile.CreateForHull(string.Empty) : hull.FlightProfile;
```

如果没有船体，就创建默认飞行参数；如果有船体，就使用船体自己的 `FlightProfile`。

```csharp
speedLimit = speedLimit <= 0f
    ? profile.DefaultSpeedLimit
    : Mathf.Clamp(speedLimit, profile.MinimumSpeedLimit, profile.MaximumSpeedLimit);
```

如果速度限制还没初始化，就使用默认速度限制；如果已经有值，就限制在当前船体允许范围内。

```csharp
boostSeconds = boostSeconds <= 0f ? profile.BoostCapacitySeconds : Mathf.Min(boostSeconds, profile.BoostCapacitySeconds);
```

初始化或修正 Boost 储备，不能超过当前船体最大 Boost 容量。

```csharp
allocator.Rebuild(assembly, hull);
```

重建推进器分配器。它会重新扫描外部推进器，并根据船体加入内置 RCS。

答辩说法：

> RefreshProfileAndAllocator 是 IFCS 适配模块化飞船的关键。船体或装配变化后，它会重新读取飞行参数、修正速度和 Boost 状态，并重建推进器分配器。

#### 3.2.13.18 事件回调和 OnDisable

源码：

```csharp
void HandleAssemblyChanged()
{
    RefreshProfileAndAllocator();
}

void HandleHullChanged(ShipHullDefinition hull)
{
    RefreshProfileAndAllocator();
}
```

解释：

当飞船装配变化或船体变化时，IFCS 重新刷新。这样玩家添加、删除、缩放推进器，或者更换船体后，控制系统会自动适配。

源码：

```csharp
void OnDisable()
{
    if (assembly != null)
        assembly.AssemblyChanged -= HandleAssemblyChanged;
    if (hullController != null)
        hullController.HullChanged -= HandleHullChanged;
    allocator.StopAll();
}
```

解释：

```csharp
assembly.AssemblyChanged -= HandleAssemblyChanged;
hullController.HullChanged -= HandleHullChanged;
```

取消事件订阅，避免对象禁用后仍被回调。

```csharp
allocator.StopAll();
```

组件禁用时停止所有推进器，保证飞船控制状态干净。

### 3.2.14 答辩版完整表述

如果老师问：“`SpacecraftIfcsMotor` 到底做了什么？”可以这样回答：

> SpacecraftIfcsMotor 是 IFCS 的核心控制器。它首先通过 ControlsEnabled 管理飞控启停，并同步输入捕获状态；然后通过 assistMode 和 SetAssistMode 管理 Coupled、Decoupled、Direct 三种模式。在物理帧 FixedUpdate 中，它读取 KeyboardMouseFlightInput 生成的 SpacecraftFlightCommand，同时读取 Rigidbody 当前速度和角速度，并转换到飞船局部坐标系。对于线性运动，Coupled 模式使用目标速度减当前速度得到速度误差，再乘响应系数得到期望加速度；Decoupled 模式则直接把输入映射为加速度。对于姿态运动，它把鼠标虚拟摇杆和滚转输入映射成目标角速度，再根据当前角速度计算角加速度。之后它用质量把线加速度转换为力，用惯性张量把角加速度转换为力矩，最后调用 SpacecraftThrusterAllocator 的 SolveAndApply，把目标力和目标力矩分配到具体推进器。每帧结束时，它还会更新 Telemetry，供 HUD 显示模式、速度限制、Boost 和控制权威等信息。

### 3.3 SpacecraftThrusterAllocator

文件：

```text
Assets/SpacecraftEditor/Runtime/SpacecraftThrusterAllocator.cs
```

职责：

- 收集所有可用推进器。
- 加入船体内置 RCS 喷口。
- 计算每个推进器能产生的力和力矩。
- 通过迭代算法求解每个推进器的油门。
- 把油门应用到真实推进器或内置 RCS。

它是这个技术里最核心的算法部分。

### 3.4 ThrusterPart

文件：

```text
Assets/SpacecraftEditor/Runtime/ThrusterPart.cs
```

职责：

- 表示一个真实安装在飞船上的推进器。
- 根据自身方向、推力、缩放比例施加物理力。
- 控制推进器尾焰粒子和灯光效果。

核心施力方式是：

```csharp
body.AddForceAtPosition(
    ThrustDirection * (ActualThrust * multiplier * currentThrottle),
    transform.position,
    ForceMode.Force);
```

这个 API 的重要性在于：力不只是作用在质心上，而是作用在推进器所在的位置上。因此如果推进器不在质心上，它会自然产生旋转力矩。

### 3.5 SpacecraftFlightTypes

文件：

```text
Assets/SpacecraftEditor/Runtime/SpacecraftFlightTypes.cs
```

职责：

- 定义飞行辅助模式。
- 定义船体飞行参数。
- 定义输入命令结构。
- 定义遥测数据结构。

其中 `ShipFlightProfile` 决定一艘船的飞行手感，包括：

- RCS 加速度。
- 最大角速度。
- 最大角加速度。
- 速度响应速度。
- 角速度响应速度。
- 默认速度限制。
- Boost 倍率。
- Boost 容量和恢复速度。

## 4. 三种飞控模式

IFCS 支持三种模式：

```csharp
public enum SpacecraftAssistMode
{
    Coupled,
    Decoupled,
    Direct
}
```

### 4.1 Coupled 辅助模式

`Coupled` 是辅助飞行模式，也是默认模式。

它的思想是：

```text
玩家输入 -> 目标速度
当前速度 -> 反馈
目标速度 - 当前速度 -> 需要的加速度
```

例如玩家按下 `W`，系统不是直接“向前喷”，而是认为玩家希望飞船达到一个向前的目标速度。如果当前速度低于目标速度，就加速；如果当前速度超过目标速度，就减速；如果松开按键，目标速度变为零，系统会主动刹车。

核心逻辑：

```csharp
Vector3 targetVelocity = command.brake
    ? Vector3.zero
    : Vector3.Scale(command.translation, new Vector3(0.72f, 0.72f, 1f)) * speedLimit;
desiredAcceleration = (targetVelocity - localVelocity) * profile.VelocityResponse;
```

其中：

- `command.translation` 是玩家输入方向。
- `speedLimit` 是目标速度上限。
- `localVelocity` 是飞船当前局部速度。
- `VelocityResponse` 决定速度追踪的强度。

可以把它理解成一个简单的速度控制器。

演讲时可以这样解释：

> Coupled 模式下，玩家不是控制推力，而是控制目标速度。IFCS 会持续比较目标速度和当前速度，然后自动加速或刹车，让飞船跟随玩家意图。

### 4.2 Decoupled 惯性模式

`Decoupled` 更接近太空飞行的惯性体验。

它的思想是：

```text
玩家输入 -> 直接加速度
松开输入 -> 不主动抵消已有速度
```

核心逻辑：

```csharp
desiredAcceleration = Vector3.Scale(command.translation, maximumAcceleration);
```

也就是说，按键时飞船获得加速度，松开后如果没有其他力，飞船会继续保持原来的速度滑行。

但是注意：当前项目中如果按下刹车键 `X`，即使在 `Decoupled` 模式下也会进入刹车逻辑：

```csharp
if (assistMode == SpacecraftAssistMode.Coupled || command.brake)
```

所以 `X` 是强制速度归零控制。

演讲时可以这样解释：

> Decoupled 模式下，IFCS 不再主动帮玩家抵消惯性。玩家给的是加速度，而不是目标速度。因此松开输入后，飞船会继续漂移。它保留了太空飞行的惯性感。

### 4.3 Direct 直接推进模式

`Direct` 是最原始的控制模式。

在 `SpacecraftIfcsMotor.FixedUpdate` 中，如果当前是 `Direct`，IFCS 会停止工作：

```csharp
if (... || assistMode == SpacecraftAssistMode.Direct)
{
    allocator.StopAll();
    UpdateTelemetry(Vector3.zero, Vector3.zero, 1f);
    return;
}
```

然后由 `ShipFlightController` 的老逻辑直接处理推进器：

```csharp
foreach (ThrusterPart thruster in assembly.Parts)
{
    float partThrottle = ResolveThrottle(thruster.ActivationKey, isPressed);
    thruster.ApplyThrust(shipBody, partThrottle);
}
```

这种模式下，哪个推进器被哪个按键绑定，就由哪个按键直接控制。

项目里只有工作坊飞行允许 Direct 模式：

```csharp
ifcsMotor.Configure(shipBody, assembly, hullController, flightInput, true);
```

星际飞行不允许 Direct 模式：

```csharp
ifcsMotor.Configure(shipBody, assembly, hullController, flightInput, false);
```

演讲时可以这样解释：

> Direct 模式是对比用的传统控制方式。它跳过 IFCS 求解，直接按键点火推进器。工作坊允许这个模式，方便测试推进器绑定；星际飞行禁用它，保证正式驾驶体验稳定。

## 5. 线性控制实现

线性控制负责平移运动，也就是飞船向前、向后、左右、上下移动。

入口函数：

```csharp
Vector3 CalculateLinearAcceleration(
    SpacecraftFlightCommand command,
    Vector3 localVelocity,
    float thrustMultiplier)
```

它的输入：

- `command`：玩家输入命令。
- `localVelocity`：飞船当前局部速度。
- `thrustMultiplier`：Boost 时的推力倍率。

它的输出：

- `desiredAcceleration`：飞船期望获得的局部线加速度。

### 5.1 最大加速度

```csharp
Vector3 maximumAcceleration = profile.RcsAcceleration * thrustMultiplier;
```

`profile.RcsAcceleration` 来自当前船体的飞行参数。不同船体可以有不同的 RCS 响应能力。

如果 Boost 激活，`thrustMultiplier` 会变大：

```csharp
float thrustMultiplier = boosting ? profile.BoostMultiplier : 1f;
```

### 5.2 Coupled 速度追踪

Coupled 下，系统先算目标速度：

```csharp
Vector3 targetVelocity = Vector3.Scale(command.translation, new Vector3(0.72f, 0.72f, 1f)) * speedLimit;
```

这里 `x` 和 `y` 方向乘了 `0.72`，说明侧向和上下速度上限低于前后方向。这是一种飞行手感设计：前进速度更强，侧移/升降稍弱。

然后计算需要的加速度：

```csharp
desiredAcceleration = (targetVelocity - localVelocity) * profile.VelocityResponse;
```

如果当前速度和目标速度差距大，加速度就大；差距小，加速度就小。

### 5.3 Decoupled 直接加速度

Decoupled 下：

```csharp
desiredAcceleration = Vector3.Scale(command.translation, maximumAcceleration);
```

输入为 1 的方向获得最大加速度，输入为 0 的方向没有加速度。

### 5.4 分量限幅

最后，无论哪种模式，都会调用：

```csharp
return ClampComponents(desiredAcceleration, maximumAcceleration);
```

它保证每个轴上的加速度不会超过船体能力。

## 6. 姿态控制实现

姿态控制负责俯仰、偏航、滚转。

入口函数：

```csharp
Vector3 CalculateAngularAcceleration(
    SpacecraftFlightCommand command,
    Vector3 localAngularVelocity)
```

它的思想是角速度追踪：

```text
玩家输入 -> 目标角速度
当前角速度 -> 反馈
目标角速度 - 当前角速度 -> 角加速度
```

代码：

```csharp
Vector3 maximumRateRadians = profile.MaximumAngularSpeed * Mathf.Deg2Rad;
Vector3 targetRate = new Vector3(
    command.vjoy.x * maximumRateRadians.x,
    command.vjoy.y * maximumRateRadians.y,
    command.roll * maximumRateRadians.z);
Vector3 acceleration = (targetRate - localAngularVelocity) * profile.AngularRateResponse;
```

这里：

- `command.vjoy.x` 控制俯仰。
- `command.vjoy.y` 控制偏航。
- `command.roll` 控制滚转。
- `MaximumAngularSpeed` 是最大角速度，配置时用角度每秒，代码里转成弧度每秒。
- `AngularRateResponse` 是角速度响应强度。

最后同样会限幅：

```csharp
Vector3 maximumAccelerationRadians = profile.MaximumAngularAcceleration * Mathf.Deg2Rad;
return ClampComponents(acceleration, maximumAccelerationRadians);
```

演讲时可以说：

> 姿态控制不是直接改 Transform 旋转，而是先计算目标角速度，再根据当前角速度计算所需角加速度，最后通过力矩驱动 Rigidbody 旋转。这保证了飞船姿态控制仍然遵守物理系统。

## 7. 从加速度到力和力矩

在 Unity 物理中，`Rigidbody` 受的是力，不是加速度。因此 IFCS 要把控制目标转换为物理量。

### 7.1 线加速度转力

牛顿第二定律：

```text
F = m * a
```

代码：

```csharp
Vector3 desiredForce = desiredAcceleration * shipBody.mass;
```

飞船越重，要达到同样加速度，需要的力越大。

### 7.2 角加速度转力矩

旋转运动中，类似关系是：

```text
Torque = Inertia * AngularAcceleration
```

项目中使用 Unity 的惯性张量：

```csharp
Quaternion principalRotation = shipBody.inertiaTensorRotation;
Vector3 principalAcceleration = Quaternion.Inverse(principalRotation) * localAngularAcceleration;
Vector3 principalTorque = Vector3.Scale(shipBody.inertiaTensor, principalAcceleration);
return principalRotation * principalTorque;
```

这段代码的作用是：

1. 把局部角加速度转换到刚体主惯性轴空间。
2. 用惯性张量逐轴相乘，得到主轴空间下的力矩。
3. 再转换回飞船局部空间。

演讲时不一定要展开全部数学细节，可以这样说：

> 线性运动用质量把加速度转成力，旋转运动用惯性张量把角加速度转成力矩。这样不同质量、不同形状的飞船会自然表现出不同的操控手感。

## 8. 推进器分配器

推进器分配器是 IFCS 的算法核心。

它要解决的问题是：

```text
已知我想要一个总力 F 和总力矩 T，
已知每个推进器的位置、方向和最大推力，
求每个推进器的 throttle，让所有推进器合起来尽量接近 F 和 T。
```

### 8.1 Actuator

分配器内部把每个推进器抽象成 `Actuator`：

```csharp
struct Actuator
{
    public ThrusterPart part;
    public Vector3 localPosition;
    public Vector3 localDirection;
    public float baseForce;
    public bool builtIn;
}
```

其中：

- `part`：真实推进器组件。
- `localPosition`：推进器在飞船局部空间的位置。
- `localDirection`：推进器产生推力的方向。
- `baseForce`：最大基础推力。
- `builtIn`：是否是内置 RCS。

### 8.2 外部推进器

外部推进器来自 `ShipAssembly.Parts`。玩家在工作坊中装配的推进器都会被加入分配器。

分配器会记录：

```csharp
localPosition = assembly.transform.InverseTransformPoint(part.transform.position)
localDirection = assembly.transform.InverseTransformDirection(part.ThrustDirection).normalized
baseForce = part.ActualThrust
```

### 8.3 内置 RCS

如果当前飞船有船体定义，分配器会额外加入 24 个内置 RCS 喷口：

```csharp
const int RcsActuatorCount = 24;
```

它在船体包围盒的 8 个角上，每个角放 3 个喷口，分别沿 X/Y/Z 方向产生控制力：

```text
8 个角 * 3 个方向 = 24 个 RCS actuator
```

这意味着即使玩家没有放置足够多的外部推进器，船体也仍然有基础姿态和平移控制能力。

### 8.4 力和力矩的计算

每个 actuator 能产生一个力：

```csharp
Vector3 force = localDirection * baseForce;
```

如果这个力不是作用在质心上，它还会产生力矩：

```csharp
Vector3 torque = Vector3.Cross(localPosition - localCenterOfMass, force);
```

这就是叉乘在飞船控制里的意义：

```text
力臂 x 力 = 力矩
```

离质心越远的推进器，同样推力能产生越大的旋转效果。

### 8.5 求解算法

求解入口：

```csharp
Solve(desiredLocalForce, desiredLocalTorque, torqueWeight)
```

它不是一次性矩阵求逆，而是一个迭代残差求解器。

基本思想：

1. 当前有一个目标：`desiredForce` 和 `desiredTorque`。
2. 每个推进器有一个 throttle，范围是 `0..1`。
3. 算出当前所有推进器的合力和合力矩。
4. 看还差多少，也就是 residual。
5. 逐个调整推进器 throttle，让 residual 变小。
6. 重复多轮迭代。

项目中迭代次数是：

```csharp
const int SolverIterations = 10;
```

每次调整某个推进器时，会计算这个推进器的能力方向和当前残差的匹配程度：

```csharp
float numerator = WeightedDot(
    localForces[index],
    localTorques[index],
    residualForce,
    residualTorque,
    torqueWeight);
```

然后除以这个推进器自身能力大小，得到新的 throttle：

```csharp
float next = Mathf.Clamp01(numerator / denominator);
```

`Clamp01` 保证推进器不能反向输出，也不能超过 100% 油门。

### 8.6 WeightedDot 的作用

分配器要同时处理“力”和“力矩”，但力的单位是牛顿，力矩的单位是牛顿米，不能直接相加。因此项目中用了加权点积：

```csharp
float torqueScale = torqueWeight / referenceLength;
return Vector3.Dot(forceA, forceB)
    + Vector3.Dot(torqueA, torqueB) * torqueScale * torqueScale;
```

这里：

- `torqueWeight` 控制姿态控制的重要性。
- `referenceLength` 用船体尺寸做归一化。

如果 `torqueWeight` 更大，求解器会更偏向满足力矩需求，也就是更重视旋转控制。

### 8.7 ControlAuthority

求解完成后，分配器计算控制权威度：

```csharp
ControlAuthority = 1f - residualMagnitude / desiredMagnitude;
```

含义：

- `1`：当前推进器布局基本能满足控制请求。
- `0`：几乎无法满足控制请求。
- 中间值：只能部分满足。

HUD 中显示的控制权威就是这个数据。

演讲时可以说：

> 推进器分配器本质上是在做一个受限优化问题：每个推进器油门只能在 0 到 1 之间，系统要在这些限制下尽量拼出目标力和目标力矩。它不追求完美闭式解，而是通过多轮迭代快速得到足够好的实时解。

## 9. Boost 系统

Boost 由 `SpacecraftIfcsMotor` 管理。

输入来自：

```csharp
command.boost
```

如果玩家按住 Shift 且储备大于 0：

```csharp
bool boosting = command.boost && boostSeconds > 0.001f;
float thrustMultiplier = boosting ? profile.BoostMultiplier : 1f;
```

Boost 会影响：

- 线性最大加速度。
- 推进器最大输出。
- 内置 RCS 输出。

Boost 储备会消耗和恢复：

```csharp
if (boosting)
    boostSeconds = Mathf.Max(0f, boostSeconds - Time.fixedDeltaTime);
else
    boostSeconds = Mathf.Min(
        profile.BoostCapacitySeconds,
        boostSeconds + profile.BoostRegenerationPerSecond * Time.fixedDeltaTime);
```

HUD 中显示的是：

```csharp
BoostRatio = boostSeconds / profile.BoostCapacitySeconds;
```

## 10. 飞船装配变化如何影响 IFCS

飞船的推进器可能在工作坊中被添加、删除、缩放、移动。IFCS 需要在这些变化后重建分配器。

`SpacecraftIfcsMotor` 监听：

```csharp
assembly.AssemblyChanged += HandleAssemblyChanged;
hullController.HullChanged += HandleHullChanged;
```

当船体或装配变化时：

```csharp
RefreshProfileAndAllocator();
```

这个函数会：

1. 获取当前船体。
2. 获取当前飞行参数 `ShipFlightProfile`。
3. 初始化或限制 `speedLimit`。
4. 初始化或限制 Boost 储备。
5. 调用 `allocator.Rebuild(assembly, hull)`。

这保证了 IFCS 始终使用最新的推进器布局。

## 11. 工作坊飞行接入

工作坊飞行控制器是：

```text
Assets/SpacecraftEditor/Runtime/ShipFlightController.cs
```

进入飞行时：

```csharp
ifcsMotor?.Configure(shipBody, assembly, hullController, flightInput, true);
ifcsMotor.SetAssistMode(SpacecraftAssistMode.Coupled);
ifcsMotor.ControlsEnabled = true;
```

这里最后一个参数是 `true`，表示允许 Direct 模式。

工作坊的意义是测试飞船：

- 可以使用 Coupled 辅助飞行。
- 可以使用 Decoupled 惯性飞行。
- 可以使用 Direct 直接推进器测试。

当 IFCS 不存在或处于 Direct 模式时，`ShipFlightController` 会用传统逻辑逐个推进器点火。

## 12. 星际飞行接入

星际飞行控制器是：

```text
Assets/Scripts/Spaceflight/InterstellarShipController.cs
```

启动时：

```csharp
ifcsMotor.Configure(shipBody, assembly, hullController, flightInput, false);
ifcsMotor.SetAssistMode(SpacecraftAssistMode.Coupled);
```

这里最后一个参数是 `false`，表示不允许 Direct 模式。

原因是星际飞行是正式驾驶场景，需要稳定统一的控制体验。Direct 模式更适合作为工作坊调试功能，而不是正式飞行模式。

## 13. 星际巡航和 IFCS 的关系

星际巡航控制器是：

```text
Assets/Scripts/Spaceflight/InterstellarCruiseController.cs
```

巡航系统负责长距离高速飞行，它会在进入巡航状态时接管线性控制：

```csharp
ifcsMotor.LinearControlEnabled = next == InterstellarCruiseState.Inactive
    || next == InterstellarCruiseState.Cooldown;
```

含义：

- 巡航 inactive/cooldown 时，IFCS 正常控制线性运动。
- 巡航加速/巡航/减速时，IFCS 暂停线性控制。
- 姿态控制仍可以保留。

巡航本身直接对 Rigidbody 施加加速度：

```csharp
shipBody.AddForce(direction * acceleration, ForceMode.Acceleration);
```

这说明项目中把普通机动飞行和长距离巡航分成了两套控制逻辑：

- IFCS 负责常规机动。
- CruiseController 负责星际高速航行。

## 14. 遥测和 HUD

IFCS 每帧更新 `SpacecraftControlTelemetry`：

```csharp
public struct SpacecraftControlTelemetry
{
    public Vector3 localVelocity;
    public Vector3 localAngularVelocity;
    public Vector3 requestedLocalForce;
    public Vector3 requestedLocalTorque;
    public float speedLimit;
    public float controlAuthority;
    public float boostRatio;
    public SpacecraftAssistMode assistMode;
}
```

HUD 使用这些数据展示：

- 当前模式。
- 控制权威。
- 速度限制。
- Boost 剩余。
- 虚拟摇杆位置。

文件：

```text
Assets/SpacecraftEditor/Runtime/SpacecraftIfcsWorkshopHud.cs
```

注意：当前 HUD 文本中存在中文乱码，说明该文件的中文字符串可能经历过编码错误。如果要正式演示，需要修复显示文案。

## 15. 技术亮点

### 15.1 控制意图和物理执行解耦

玩家输入并不直接控制推进器，而是先变成控制目标。这样以后即使推进器布局改变，输入层也不需要改变。

### 15.2 支持模块化飞船

飞船可以由不同推进器组成。IFCS 每次重建时都会重新扫描 `ShipAssembly.Parts`，因此天然适合飞船编辑器。

### 15.3 同时处理平移和旋转

推进器分配器不是只管向前推力，而是同时满足：

- 目标线性力。
- 目标旋转力矩。

这让飞船可以自动完成复杂姿态修正。

### 15.4 物理驱动，不直接改 Transform

所有运动最终都通过 `Rigidbody` 施力实现。这样飞船的质量、质心、惯性张量都会真实影响手感。

### 15.5 有控制能力反馈

`controlAuthority` 可以告诉玩家或开发者：当前推进器布局是否足够控制飞船。

这对飞船编辑器很有价值，因为玩家可能造出推进器严重不平衡的船。

## 16. 可以改进的地方

当前实现已经能工作，但仍有一些可以扩展的方向。

### 16.1 更严格的推进器优化

现在使用迭代残差法，优点是简单、实时、容易实现。缺点是它不是严格的最优解。

以后可以考虑：

- 非负最小二乘。
- 二次规划。
- 权重可配置的控制分配矩阵。

### 16.2 推进器分组

可以把推进器分成：

- 主推进器。
- 姿态 RCS。
- 侧向机动喷口。
- 反推喷口。

这样分配器可以更符合飞船设计逻辑。

### 16.3 能量/燃料系统

当前主要限制是 Boost 储备。未来可以加入：

- 总能源输出限制。
- 燃料消耗。
- 推进器过热。
- 损坏导致推力下降。

### 16.4 更完整的 HUD 诊断

可以显示：

- 当前请求力。
- 当前请求力矩。
- 每个轴的控制权威。
- 哪些推进器正在输出。
- 推进器饱和警告。

## 17. 演讲结构建议

可以按 7 页 PPT 讲：

### 第 1 页：IFCS 是什么

关键词：

- 智能飞控。
- 玩家输入运动意图。
- 系统自动分配推进器。

讲法：

> 传统控制是按键点火，IFCS 是输入意图、系统求解推进器。

### 第 2 页：整体架构

展示数据流：

```text
Input -> Command -> IFCS Motor -> Force/Torque -> Allocator -> Rigidbody
```

讲清每个类职责。

### 第 3 页：三种飞控模式

对比：

| 模式 | 玩家控制的是 | 特点 |
| --- | --- | --- |
| Coupled | 目标速度 | 辅助驾驶，会自动刹车 |
| Decoupled | 加速度 | 惯性飞行，会漂移 |
| Direct | 推进器开关 | 调试和传统控制 |

### 第 4 页：从输入到力和力矩

讲两个公式：

```text
F = m * a
Torque = Inertia * AngularAcceleration
```

强调项目没有直接修改 Transform，而是走 Rigidbody 物理。

### 第 5 页：推进器分配算法

讲：

```text
每个推进器 = 一个 actuator
每个 actuator 能产生 force 和 torque
求解器调整 throttle，让合力/合力矩接近目标
```

可以重点讲叉乘：

```text
torque = lever arm x force
```

### 第 6 页：场景接入

说明：

- 工作坊允许 Direct 模式。
- 星际飞行禁用 Direct 模式。
- 巡航控制器会接管线性控制。

### 第 7 页：技术价值和扩展

总结：

- 支持模块化飞船。
- 控制手感稳定。
- 物理一致。
- 可诊断控制能力。
- 可扩展到燃料、损坏、过热、复杂优化。

## 18. 面试/答辩式问答准备

### 问：为什么不直接用按键控制推进器？

答：

因为模块化飞船的推进器位置和方向不固定。直接按键控制会导致玩家需要理解每个推进器布局，而且飞船很容易失控。IFCS 把玩家输入转成目标力和力矩，再自动分配推进器，可以适配不同飞船结构。

### 问：Coupled 和 Decoupled 最大区别是什么？

答：

Coupled 控制目标速度，会自动抵消当前速度，因此松开按键后飞船会尝试停下。Decoupled 控制加速度，不主动抵消已有速度，因此松开按键后飞船会继续漂移。

### 问：推进器怎么产生旋转？

答：

推进器的力如果不经过质心，就会产生力矩。代码中使用：

```csharp
Vector3.Cross(localPosition - localCenterOfMass, force)
```

这就是物理中的“力臂叉乘力等于力矩”。

### 问：为什么要用 Rigidbody，而不是直接改 Transform？

答：

因为飞船应该受到质量、质心、惯性张量影响。直接改 Transform 会绕过物理系统，导致碰撞、惯性、旋转手感都不真实。

### 问：controlAuthority 是什么？

答：

它表示当前推进器布局满足控制请求的程度。求解器会计算目标力/力矩和剩余误差，误差越小，控制权威越接近 1。

### 问：这个算法是不是最优？

答：

当前实现是实时迭代近似求解，不是严格数学最优。它的优点是实现简单、速度快、适合游戏实时运行。未来可以升级为非负最小二乘或二次规划。

## 19. 推荐源码阅读顺序

如果要真正学会，建议按这个顺序读：

1. `SpacecraftFlightTypes.cs`
   - 先理解数据结构和飞行模式。

2. `KeyboardMouseFlightInput.cs`
   - 理解玩家输入如何变成命令。

3. `SpacecraftIfcsMotor.cs`
   - 理解 IFCS 如何把命令变成目标力和力矩。

4. `SpacecraftThrusterAllocator.cs`
   - 理解推进器分配算法。

5. `ThrusterPart.cs`
   - 理解最终如何作用到 Rigidbody。

6. `ShipFlightController.cs`
   - 理解工作坊飞行如何启动 IFCS。

7. `InterstellarShipController.cs`
   - 理解星际飞行如何启动 IFCS。

8. `InterstellarCruiseController.cs`
   - 理解巡航系统如何和 IFCS 协作。

## 20. 最终总结

本项目的 IFCS 不是一个单纯的输入脚本，而是一套完整的飞船控制架构。

它的核心思想是：

```text
玩家负责表达意图。
IFCS 负责计算控制目标。
推进器分配器负责把目标分摊给推进器。
Unity Rigidbody 负责真实物理运动。
```

从技术上看，它结合了：

- 输入抽象。
- 速度反馈控制。
- 角速度反馈控制。
- 刚体质量和惯性张量。
- 推进器力矩计算。
- 实时迭代求解。
- 模块化飞船装配系统。

因此，它非常适合作为演讲主题：既能讲游戏玩法设计，也能讲 Unity 物理，还能讲一点控制系统和优化算法。
