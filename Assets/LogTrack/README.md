# LogTrack Unity 插件

基于腾讯 IEG LogTrack 思路的复现版。复制本文件夹到任意 Unity 项目的 `Assets/LogTrack/` 即可使用。

## 安装

1. 复制 `Assets/LogTrack/` 到目标 Unity 项目
2. （推荐）复制 `Assets/LogTrackGenerated/README.txt` 所在目录结构，或首次插桩时自动创建
3. 用 Unity 2022.3+ 打开项目，等待编译完成

## 快速开始

1. **Tools → LogTrack → 打开工具窗口 → 插入日志代码**
   - 目标目录默认 `Assets/Scripts`（可在面板修改，会保存）
   - Pdb 目录默认 `Assets/LogTrackGenerated`
2. **Tools → LogTrack → 运行时设置**
   - 勾选 **Play 时自动启动**（默认关闭）
   - 调整 Ring Buffer Size
3. 点击 **Play**，运行后 **Stop**
4. 日志在 `Application.persistentDataPath/LogTrack/`

## 自动启动

插件内置 `LogTrackRuntimeBootstrap`：开启「Play 时自动启动」后，会自动创建 `[LogTrack Auto Runner]`，无需手动挂组件。

也可手动创建 `LogTrackSession`（手动模式，优先级高于自动启动）。

## 解析日志

```powershell
python tools/logtrack/parse_logtrack.py --log <track.bin> --pdb Assets/LogTrackGenerated/LogPdb.pdb.json --md report.md
```

## 参考

- https://git.woa.com/kungfu/LogTrack
