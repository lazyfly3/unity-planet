# LogTrack Unity 接入说明（unity-planet）

基于腾讯 IEG LogTrack 思路的复现版，已内嵌在本仓库 `Assets/LogTrack/`。

## 第一次使用

### 1. 等待 Unity 编译完成

用 Unity 2022.3 打开本工程，确认 Console 无编译错误。

### 2. 插入日志代码

菜单：**Tools → LogTrack → 打开工具窗口**

切到 **「插入日志代码」** 页：

- 目标目录：`Assets/Scripts`
- Pdb 目录：`Assets/LogTrackGenerated`
- 默认已排除：`Editor/PortalPrefabCreator.cs`（Editor 脚本不应插桩）
- 点击 **「插入日志代码」**

完成后业务脚本的函数里会自动加上 `FSPDebuger.LogTrack(...)` 调用。

### 3. 开始记录

1. 在场景中创建空物体（如 `LogTrackSession`）
2. 添加组件 **LogTrackSession**
3. 点击 **Play**，运行一段时间后停止
4. 停止 Play 时会自动导出日志（可在 Inspector 关闭 `Export On Destroy`）

日志位置：

- 二进制 / 文本：`Application.persistentDataPath/LogTrack/`
- Pdb：`Assets/LogTrackGenerated/LogPdb.pdb.json`

也可在组件上右键 **Export LogTrack Now** 手动导出。

## 可选：编译后自动插桩

**Tools → LogTrack → 编译后自动插桩**（开关）

开启后，每次 Unity 编译完成会自动对 `Assets/Scripts` 插桩。

## 导出已有日志

**Tools → LogTrack → 打开工具窗口 → 导出日志文本**

填入 `.bin` / `.json` 日志路径和 `LogPdb.pdb.json` 路径即可。

## 命令行解析

```powershell
python tools/logtrack/parse_logtrack.py ^
  --log "<track.bin.json 路径>" ^
  --pdb Assets/LogTrackGenerated/LogPdb.pdb.json ^
  --last 100 ^
  --out analysis.json ^
  --md report.md
```

配合 `tools/logtrack/prompts/logtrack_analyst.md` 可交给 AI 分析调用链。

## 目录说明

```
Assets/
├── LogTrack/
│   ├── Runtime/          # 运行时（随包体）
│   └── Editor/           # 仅编辑器
├── Scripts/              # 业务脚本（手动插桩）
└── LogTrackGenerated/    # 插桩后生成的 Pdb
tools/logtrack/           # Python 解析工具
```

## 参考来源

- 工蜂仓库：https://git.woa.com/kungfu/LogTrack
- 文章：LogTrack 帧同步一致性问题检测方案
