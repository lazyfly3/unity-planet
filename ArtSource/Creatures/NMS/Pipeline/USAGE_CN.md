# 生物家族管线说明

## 固定原则

- Mesh、InvBindMatrix、Armature、Bind Pose 和动画必须来自同一个骨架家族。
- 不允许依据 Joint 显示位置重新猜骨架。
- 不允许用通用 IK 覆盖整条原生腿部动作。
- 承重骨链必须逐级满足直属父子关系，不允许自动猜替代骨。
- 骨架哈希不同的 Scene 必须拆成不同家族。
- validate/review 默认不导出 FBX；只有携带完全匹配审核哈希的 unity_publish 才允许发布到 Unity。

## 自动阶段

1. `extract`：只提取目标家族资源，按 SHA-256 复用文件，转换 Descriptor/材质 MXML，并补齐跨包纹理引用。
2. `baseline`：导入 Scene，读取几何 InvBindMatrix，创建正确 Rest Pose 并重绑网格。
3. `actions`：用完整父子矩阵关系烘焙 Idle、Walk、Run。
4. `library`：递归解析 Descriptor，输出模块清单和 8 个不同的确定性组合。
5. `validate`：检查 Rest Pose、权重骨、直属承重骨链、骨长、骨骼缩放、Bounds、有限顶点和几何拉伸。
6. `review`：保存可播放 Showcase，并渲染 Walk 起点、中点、终点审核图。
7. `unity_publish`：校验人工确认哈希，从同一 SpeciesLibrary 导出 FBX、动作、清单与最后写入的发布请求。

重绑 Armature 前会保存每个 Mesh 的原始 Descriptor ancestry。禁止依靠重绑后的
Hierarchy 推断模块路径，否则互斥头部、牙齿和身体会被错误地同时启用。

## Blender 中运行

```python
import sys
sys.path.insert(0, r"D:\unity planet\unity-planet\ArtSource\Creatures\NMS\Pipeline")
from run_pipeline import run_family

review = run_family(
    "TrexBiped",
    stages=("extract", "baseline", "actions", "library", "validate", "review"),
)
print(review["validationHash"])
```

只重跑验证和审核：

```python
run_family("TrexBiped", stages=("validate", "review"))
```

## 审核后发布

```python
run_family(
    "TrexBiped",
    stages=("unity_publish",),
    approval=review["validationHash"],
)
```

发布清单导入 Unity 后会自动生成 Runtime Prefab、Idle/Walk/Run Animator、
NmsCreatureFamilyDefinition 并按稳定 ID 更新全局 Catalog。bio 始终只使用一个
NmsRandomCreatureGenerator，因此新增家族不需要增加枚举或修改生成器分支。

## 添加新家族

在 `creature_families.json` 增加独立家族 ID、输入/输出根目录、Scene、Descriptor、
Idle/Walk/Run、运动类型、腿数和每条承重腿的真实连续骨链。需要自动解包的家族还要
提供 PAK 根目录、家族过滤路径与纹理包。第一次运行生成骨架哈希；模块只有在权重骨
全部属于该哈希时才能进入家族。

## 人工审核

- 打开 `<Family>SpeciesShowcase.blend`，按空格播放 Walk。
- 打开 `<Family>Baseline.blend`，在 Action Editor 切换 Idle、Walk、Run。
- 检查静止姿态、左右腿交替、膝盖方向、脚底方向、尾巴运动和模块连接。
- 查看 `Validation/<Family>_validation.json`，必须 `passed: true`。
- 审核通过后，将 validationHash 原样传入 unity_publish；任何文件或哈希变化都会拒绝发布。
