# WASD 到 IFCS 力学输出过程

本文档专门解释一个问题：

```text
玩家按 W / A / S / D 之后，IFCS 是怎么一步一步算出力学参数，并让飞船动起来的？
```

可以先记住一句话：

```text
WASD 不是直接控制推进器。
WASD 先表示玩家想往哪个方向飞。
IFCS 再把这个方向变成加速度、力，最后交给推进器执行。
```

完整流程是：

```text
WASD 输入
    ↓
translation 方向
    ↓
目标速度或目标加速度
    ↓
期望加速度 desiredAcceleration
    ↓
期望力 desiredForce
    ↓
推进器分配器
    ↓
具体推进器喷火
    ↓
Rigidbody 物理运动
```

## 1. 第一步：WASD 变成方向

源码位置：

```text
Assets/SpacecraftEditor/Runtime/KeyboardMouseFlightInput.cs
```

关键代码：

```csharp
command.translation = Vector3.ClampMagnitude(new Vector3(
    DigitalAxis(KeyCode.A, KeyCode.D),
    DigitalAxis(KeyCode.LeftControl, KeyCode.Space),
    DigitalAxis(KeyCode.S, KeyCode.W)), 1f);
```

这段代码的作用是：

```text
把按键转换成一个三维方向向量。
```

三个轴分别是：

| 轴 | 按键 | 含义 |
| --- | --- | --- |
| X 轴 | A / D | 左 / 右 |
| Y 轴 | LeftControl / Space | 下 / 上 |
| Z 轴 | S / W | 后 / 前 |

所以：

```text
按 W      -> translation = (0, 0, 1)
按 S      -> translation = (0, 0, -1)
按 D      -> translation = (1, 0, 0)
按 A      -> translation = (-1, 0, 0)
按 Space  -> translation = (0, 1, 0)
按 Ctrl   -> translation = (0, -1, 0)
```

如果同时按 `W + D`：

```text
translation = (1, 0, 1)
```

但是代码用了：

```csharp
Vector3.ClampMagnitude(..., 1f)
```

它会把方向长度限制到最大为 `1`。

为什么要限制？

因为如果不限制，同时按 `W + D` 的长度会比只按 `W` 更大，斜着飞就会比直着飞更快。限制长度后，斜向输入不会获得额外速度优势。

## 2. DigitalAxis 是什么

源码：

```csharp
static float DigitalAxis(KeyCode negative, KeyCode positive)
{
    return (Input.GetKey(positive) ? 1f : 0f) - (Input.GetKey(negative) ? 1f : 0f);
}
```

它的意思是：

```text
如果按正方向键，返回 1。
如果按负方向键，返回 -1。
如果都没按，返回 0。
如果两个都按，相互抵消，返回 0。
```

例如：

```csharp
DigitalAxis(KeyCode.S, KeyCode.W)
```

含义是：

```text
S 是负方向
W 是正方向
```

结果：

```text
只按 W      -> 1
只按 S      -> -1
都不按      -> 0
W 和 S 都按 -> 0
```

所以 `WASD` 最终不是直接变成力，而是先变成：

```csharp
command.translation
```

也就是：

```text
玩家想移动的方向
```

## 3. 第二步：IFCS 读取 translation

源码位置：

```text
Assets/SpacecraftEditor/Runtime/SpacecraftIfcsMotor.cs
```

在 `FixedUpdate` 中，IFCS 读取输入命令：

```csharp
SpacecraftFlightCommand command = flightInput == null ? default : flightInput.Command;
```

这里的 `command.translation` 就是上一节从 `WASD` 得到的方向。

接下来 IFCS 会读取飞船当前速度：

```csharp
Vector3 localVelocity = transform.InverseTransformDirection(shipBody.velocity);
```

这里有一个重点：

```text
shipBody.velocity 是世界坐标速度。
localVelocity 是飞船自身坐标里的速度。
```

为什么要转换成本地速度？

因为 `W` 的意思不是“世界 Z 轴前进”，而是：

```text
沿飞船自己的头部方向前进。
```

飞船可能已经旋转了，所以必须把速度转换到飞船自己的坐标系里，才能和 `WASD` 输入方向对应起来。

## 4. 第三步：根据飞控模式计算期望加速度

IFCS 有两个常用模式：

```text
Coupled   辅助模式
Decoupled 惯性模式
```

它们对 `WASD` 的理解不同。

## 5. Coupled 模式：WASD 表示目标速度

`Coupled` 模式可以理解成：

```text
玩家按键不是直接加速，而是告诉 IFCS：我希望飞船达到某个速度。
```

关键代码：

```csharp
Vector3 targetVelocity = command.brake
    ? Vector3.zero
    : Vector3.Scale(command.translation, new Vector3(0.72f, 0.72f, 1f)) * speedLimit;

desiredAcceleration = (targetVelocity - localVelocity) * profile.VelocityResponse;
```

分开看。

### 5.1 先算目标速度

```csharp
Vector3 targetVelocity = Vector3.Scale(command.translation, new Vector3(0.72f, 0.72f, 1f)) * speedLimit;
```

假设：

```text
按 W
translation = (0, 0, 1)
speedLimit = 120
```

那么：

```text
targetVelocity = (0, 0, 1) * 120
targetVelocity = (0, 0, 120)
```

意思是：

```text
我希望飞船向前达到 120 m/s。
```

如果按 `D`：

```text
translation = (1, 0, 0)
```

因为代码里 X 轴乘了 `0.72`：

```text
targetVelocity = (1, 0, 0) * 0.72 * 120
targetVelocity = (86.4, 0, 0)
```

意思是：

```text
侧向移动速度上限比前进低一些。
```

这是为了飞船手感：向前飞更强，侧移稍弱。

### 5.2 再算速度差

假设你按 `W`：

```text
targetVelocity = (0, 0, 120)
```

但是飞船当前速度是：

```text
localVelocity = (0, 0, 40)
```

那么速度差是：

```text
targetVelocity - localVelocity
= (0, 0, 120) - (0, 0, 40)
= (0, 0, 80)
```

意思是：

```text
飞船还差 80 m/s 才达到目标速度。
```

### 5.3 速度差变成期望加速度

代码：

```csharp
desiredAcceleration = (targetVelocity - localVelocity) * profile.VelocityResponse;
```

假设：

```text
速度差 = (0, 0, 80)
VelocityResponse = 3.2
```

那么：

```text
desiredAcceleration = (0, 0, 80) * 3.2
desiredAcceleration = (0, 0, 256)
```

这表示：

```text
系统希望用很大的向前加速度，快速追上目标速度。
```

但是飞船能力有限，不能真的给这么大加速度，所以还要限幅。

### 5.4 限制最大加速度

代码：

```csharp
return ClampComponents(desiredAcceleration, maximumAcceleration);
```

假设最大加速度是：

```text
maximumAcceleration = (8, 8, 5)
```

刚才算出的：

```text
desiredAcceleration = (0, 0, 256)
```

会被限制成：

```text
desiredAcceleration = (0, 0, 5)
```

因为 Z 轴最大只能是 `5`。

所以最后得到：

```text
IFCS 希望飞船向前获得 5 m/s² 的加速度。
```

## 6. Decoupled 模式：WASD 表示直接加速度

`Decoupled` 模式更简单。

关键代码：

```csharp
desiredAcceleration = Vector3.Scale(command.translation, maximumAcceleration);
```

假设：

```text
按 W
translation = (0, 0, 1)
maximumAcceleration = (8, 8, 5)
```

那么：

```text
desiredAcceleration = (0, 0, 1) * (8, 8, 5)
desiredAcceleration = (0, 0, 5)
```

意思是：

```text
按 W 时，直接请求向前 5 m/s² 的加速度。
```

Coupled 和 Decoupled 的区别：

| 模式 | WASD 的含义 | 松开 W 后 |
| --- | --- | --- |
| Coupled | 我要达到某个目标速度 | 会自动减速，尝试停下 |
| Decoupled | 我要直接加速 | 不主动减速，会继续漂移 |

## 7. 刹车键 X 的特殊情况

代码：

```csharp
if (assistMode == SpacecraftAssistMode.Coupled || command.brake)
```

这表示：

```text
只要按了刹车键 X，不管是不是 Coupled 模式，都会使用目标速度控制。
```

刹车时：

```csharp
Vector3 targetVelocity = Vector3.zero;
```

意思是：

```text
目标速度是 0。
```

如果飞船当前还在向前飞：

```text
localVelocity = (0, 0, 40)
```

那么：

```text
targetVelocity - localVelocity
= (0, 0, 0) - (0, 0, 40)
= (0, 0, -40)
```

IFCS 会请求反方向加速度，让飞船减速。

## 8. 第四步：加速度变成力

前面算出来的是：

```text
desiredAcceleration
```

也就是期望加速度。

但是 Unity 的 Rigidbody 最终需要的是力。

物理公式：

```text
力 = 质量 × 加速度
F = m × a
```

代码：

```csharp
Vector3 desiredForce = desiredAcceleration * shipBody.mass;
```

假设：

```text
desiredAcceleration = (0, 0, 5)
shipBody.mass = 100
```

那么：

```text
desiredForce = (0, 0, 5) * 100
desiredForce = (0, 0, 500)
```

意思是：

```text
IFCS 希望飞船受到一个向前 500 牛的力。
```

这就是从 `WASD` 到力学参数的关键一步。

## 9. 第五步：力交给推进器分配器

IFCS 算出：

```text
desiredForce = (0, 0, 500)
```

但这还不是具体哪个推进器喷火。

接下来调用：

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

推进器分配器会检查：

```text
飞船上有哪些推进器？
每个推进器朝哪个方向？
每个推进器在飞船哪个位置？
每个推进器最大推力是多少？
```

然后它会算：

```text
为了得到这个总力，每个推进器应该开多少油门？
```

例如：

```text
后方左推进器：80%
后方右推进器：80%
侧向小喷口：10%
```

这一步的输出不是一个简单的力，而是：

```text
每个推进器的 throttle，也就是油门比例。
```

## 10. 第六步：推进器真正让飞船动起来

真实推进器代码在：

```text
Assets/SpacecraftEditor/Runtime/ThrusterPart.cs
```

关键代码：

```csharp
body.AddForceAtPosition(
    ThrustDirection * (ActualThrust * multiplier * currentThrottle),
    transform.position,
    ForceMode.Force);
```

意思是：

```text
在推进器所在的位置，沿推进器推力方向，给 Rigidbody 施加一个力。
```

这里为什么用 `AddForceAtPosition`？

因为推进器不一定在飞船中心。

如果力作用在中心，飞船主要平移。

如果力作用在偏离中心的位置，飞船除了平移，还可能旋转。

所以这个 API 可以同时产生：

```text
平移运动
旋转运动
```

## 11. 完整例子：按 W

假设当前条件：

```text
按键：W
模式：Coupled
speedLimit = 120
localVelocity = (0, 0, 40)
VelocityResponse = 3.2
maximumAcceleration = (8, 8, 5)
shipBody.mass = 100
```

完整过程：

```text
按 W
↓
translation = (0, 0, 1)
↓
targetVelocity = (0, 0, 120)
↓
当前速度 localVelocity = (0, 0, 40)
↓
速度差 = targetVelocity - localVelocity
速度差 = (0, 0, 80)
↓
期望加速度 = 速度差 × VelocityResponse
期望加速度 = (0, 0, 256)
↓
限制最大加速度
最终 desiredAcceleration = (0, 0, 5)
↓
期望力 = 质量 × 加速度
desiredForce = 100 × 5
desiredForce = (0, 0, 500)
↓
推进器分配器计算每个推进器油门
↓
推进器调用 AddForceAtPosition
↓
飞船向前加速
```

## 12. 完整例子：松开 W

### Coupled 模式

松开 `W` 后：

```text
translation = (0, 0, 0)
```

目标速度变成：

```text
targetVelocity = (0, 0, 0)
```

如果飞船当前还在向前飞：

```text
localVelocity = (0, 0, 40)
```

速度差：

```text
targetVelocity - localVelocity
= (0, 0, 0) - (0, 0, 40)
= (0, 0, -40)
```

IFCS 会请求反向加速度：

```text
让飞船减速
```

所以 Coupled 模式松开按键后会自动刹车。

### Decoupled 模式

松开 `W` 后：

```text
translation = (0, 0, 0)
desiredAcceleration = (0, 0, 0)
```

IFCS 不主动刹车。

所以飞船会继续保持原来的速度漂移。

## 13. 完整例子：按 D

假设：

```text
按键：D
translation = (1, 0, 0)
speedLimit = 120
```

Coupled 模式下：

```text
targetVelocity = (1, 0, 0) × (0.72, 0.72, 1) × 120
targetVelocity = (86.4, 0, 0)
```

意思是：

```text
希望飞船向右达到 86.4 m/s。
```

然后它继续根据当前右向速度算速度差，再算期望加速度，再乘质量变成力。

## 14. 总结：WASD 到输出的本质

最核心的转换链路：

```text
WASD
↓
translation 方向
↓
Coupled：translation -> targetVelocity -> desiredAcceleration
Decoupled：translation -> desiredAcceleration
↓
desiredForce = desiredAcceleration × mass
↓
allocator.SolveAndApply
↓
每个推进器 throttle
↓
Rigidbody.AddForceAtPosition
```

一句话总结：

```text
WASD 表示玩家想往哪个方向飞；
IFCS 把这个方向变成目标加速度；
目标加速度乘飞船质量变成目标力；
推进器分配器把目标力分配给具体推进器；
Unity Rigidbody 根据这些力让飞船真实运动。
```

## 15. 答辩时可以这样讲

如果老师问：

```text
WASD 输入是怎么变成飞船运动的？
```

可以回答：

```text
首先，KeyboardMouseFlightInput 会把 W、A、S、D 转成 command.translation，
也就是一个三维方向向量。比如 W 是 (0,0,1)，D 是 (1,0,0)。

然后 SpacecraftIfcsMotor 在 FixedUpdate 中读取这个 translation。
如果是 Coupled 模式，它会把 translation 乘以速度限制，得到目标速度，
再用目标速度减去飞船当前局部速度，得到速度误差，
最后乘以响应系数得到期望加速度。

如果是 Decoupled 模式，它会直接把 translation 乘以最大加速度，
得到期望加速度。

得到期望加速度以后，系统用 F = m × a，
也就是用飞船质量乘以加速度，得到目标力 desiredForce。

最后，这个目标力会交给 SpacecraftThrusterAllocator。
分配器根据推进器的位置、方向和最大推力，计算每个推进器的油门，
然后通过 Rigidbody.AddForceAtPosition 让飞船真正受到力并运动。
```
