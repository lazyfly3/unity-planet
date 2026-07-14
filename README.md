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

本仓库已内嵌 [LogTrack](Assets/LogTrack/README.md)（函数调用链记录与导出），clone 后即可使用：

1. Unity 菜单 `Tools → LogTrack → 打开工具窗口` 对 `Assets/Scripts` 插桩
2. 在「运行时设置」开启自动启动后 Play 即可录制（默认关闭）；Ring Buffer 在「运行时设置」里调整
3. 用 `tools/logtrack/parse_logtrack.py` 解析日志，或配合 `tools/logtrack/prompts/logtrack_analyst.md` 做 AI 分析

详细说明见 [Assets/LogTrack/README.md](Assets/LogTrack/README.md)。
