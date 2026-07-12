# Quad Sphere 六扇区分支（feature/quad-sphere）

实验性实现：参考 Blocky Planet / Planetcraft 思路，将星球拆成 **6 个立方体面扇区**，每个扇区独立网格，方块 **沿径向对齐重力**。

## 与 main 分支区别

| | main（VoxelWorld） | feature/quad-sphere |
|--|-------------------|---------------------|
| 坐标 | 单套世界轴对齐网格 | 6 扇区 × (U, V, Depth) |
| 方块朝向 | 平行世界 X/Y/Z | **沿球面法线旋转** |
| 组件 | `VoxelWorld` | `VoxelQuadSphereWorld` |
| 挖掘 | `VoxelDigTool` | `VoxelQuadSphereDigTool` |

## 挂载方式

### 1. 禁用旧世界

- 禁用或删除场景里的 **VoxelWorld**（及 VoxelSaveSystem、VoxelChunkStreamer）
- 禁用 **VoxelDigTool**

### 2. 新建 VoxelQuadSphereWorld

空物体 `VoxelQuadSphereWorld`，Position `(0,0,0)`：

| 参数 | 建议 |
|------|------|
| Planet Radius | `100` |
| Surface Gravity | `9.8` |
| Face Grid Size | `100`（每扇区 U/V 格子数） |
| Max Depth | `64`（向球心层数） |
| Player Spawn | → Player |
| Dirt / Stone Material | 拖入材质 |

### 3. Player

| 组件 | 设置 |
|------|------|
| VoxelPlanetPlayerController | **Quad Sphere World** → VoxelQuadSphereWorld；**Voxel World 留空** |
| VoxelQuadSphereDigTool | Quad Sphere World → 同上；Dig Camera → Main Camera |
| CharacterController | 保留 |

### Hierarchy

```
VoxelQuadSphereWorld
Player
  └── Main Camera
```

## 已知限制（实验分支）

- [ ] 扇区 **边缘 12 条棱** 邻居未完全缝合，交界处可能有缝或挖洞异常
- [ ] 暂无存档
- [ ] 暂无准星高亮
- [ ] 全量生成，大半径较慢

## 文件结构

```
Assets/Scripts/Voxel/QuadSphere/
  VoxelQuadSphereTypes.cs      — 扇区 / 体素地址
  VoxelQuadSphereMapping.cs    — cube-to-sphere 映射
  VoxelQuadSphereTerrain.cs    — 地形密度
  VoxelQuadSphereMesher.cs     — 径向对齐方块 mesh
  VoxelQuadSphereChunk.cs
  VoxelQuadSphereWorld.cs
  VoxelQuadSphereDigTool.cs
```

## 切回 main 方案

使用 `VoxelWorld` + `VoxelDigTool`，禁用 Quad Sphere 组件即可。
