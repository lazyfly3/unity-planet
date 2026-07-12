# Craft 分支：球面方形地基建造

> 基于 `feature/quad-sphere` 的 **局部切平面 + BuildingAnchor** 方案。

## 原理

- 地形仍是弯曲的 Quad Sphere 体素星球
- 建造使用独立的 **BuildingAnchor（建造锚点）**，在命中点建立本地 `Right / Up / Forward` 切平面
- 在切平面上按 **cellSize × cellSize** 方格吸附放置地基板
- 同一锚点内所有地基共用一个朝向，视觉上为 **方形、平整** 的基地

## 操作

| 按键 | 功能 |
|------|------|
| **B** | 开关建造模式 |
| **左键** | 放置一块地基（2m × 2m 默认） |
| **N** | 下一次点击强制创建 **新地基锚点** |
| **R** | 旋转当前未放置/空锚点网格 90° |

## 挂载（star.unity）

1. 选中 **player**
2. Add Component → **Building Placer**
3. 配置：

| 字段 | 值 |
|------|-----|
| Quad Sphere World | `VoxelQuadSphereWorld` |
| Build Camera | `Main Camera` |
| Cell Size | `2` |
| Slab Height | `0.3` |
| Reach | `10` |
| Foundation Material | `VoxelStone` 或 `VoxelDirt` |

4. 可选：禁用 **Voxel Quad Sphere Dig Tool**（若不需要挖掘）

## 脚本

```
Assets/Scripts/Craft/
  BuildingAnchor.cs          — 切平面网格与占用表
  BuildingFoundationPiece.cs — 单块地基视觉
  BuildingPlacer.cs          — 玩家放置与预览
```

## 扩展方向

- 多格建筑蓝图（4×4 一次放置）
- 保存/加载锚点与地基
- 吸附到已有锚点时校验坡度 / 最大基地半径
