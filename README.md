# unity-planet

Unity 2022.3 体素星球项目：程序化体素地形、万有引力、球面行走、挖掘与存档。

## 打开方式

1. 用 Unity Hub 打开本目录
2. 打开 `Assets/Scenes` 中的场景（或你的主场景）
3. 参考 `Docs/程序化体素世界开发计划.md`

## 主要功能

- 体素地形生成（平地 / 星球模式）
- 星球万有引力与球面玩家控制
- 流式加载（平地模式）/ 全量生成（星球模式）
- 挖掘、准星高亮、F5/F9 存档

## LogTrack 调试插件

本仓库已内嵌 [LogTrack](Assets/LogTrack/README.md)（函数调用链记录与导出）：

1. 确认 `Assets/Scripts/LogTrackSetting.txt`（**编译后自动插桩默认关闭**，已排除体素热路径）
2. `Tools → LogTrack → 插入日志代码`（或仅本机开启「编译后自动插桩」）
3. `Tools → LogTrack → 运行时设置`：开启 **Play 时自动启动** 与 **退出 Play 时自动导出**
4. **Inspector**：`VoxelQuadSphereWorld` 上关闭 **Log Track During Generation**（避免生成阶段卡顿）
5. Play → Stop → 日志在 `%LOCALAPPDATA%\LocalLow\...\LogTrack\`
6. 解析：`python tools/logtrack/parse_logtrack.py --log "<导出的.log>" --out analysis.json`

**新版（2026-07-15）**：三 Phase + depth + `Class::Method`；`LogTrackPhaseDriver`；编译后自动插桩支持项目默认 + 本机覆盖。

若脚本曾被旧版插桩，建议先 `git checkout -- Assets/Scripts` 再重新插桩。

详细说明见 [Assets/LogTrack/README.md](Assets/LogTrack/README.md)。
