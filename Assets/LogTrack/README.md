# LogTrack Unity 插件

复制 `Assets/LogTrack/` 到 Unity 项目的 `Assets/LogTrack/` 即可。

## 格式（Jacky 评审对齐）

- 每逻辑帧三个 Phase：`FixedUpdate` → `Update` → `LateUpdate`
- 调用行：`0[---depth]Namespace.Class::Method(args)`（无 file/line）
- 解析 JSON：`{ frameIndex, phases[{ phase, calls[{ depth, call }] }] }`

## 快速开始

1. **Tools → LogTrack → 插入日志代码**（默认 `Assets/Scripts` → `Assets/LogTrackGenerated/LogPdb.pdb.json`）
2. **Tools → LogTrack → 运行时设置** → 勾选 **Play 时自动启动**
3. Play → Stop → 日志在 `Application.persistentDataPath/LogTrack/`

## 运行时驱动

`LogTrackPhaseDriver` 在 Play 时自动创建（`LogTrackRuntimeBootstrap`），按 Unity game update 分段录制。

## 解析

```powershell
python LogTrackUnity/tools/parse_logtrack.py --log <exported.log> --out analysis.json
python LogTrackUnity/tools/demo_run.py
python -m pytest LogTrackUnity/tools/test_parse_logtrack.py -q
```

## Vulcan Batch

`LogTrackBatch.InsertLogTrack` / `FindLatestTrackLog` 供 `logtrack_demo_run` BatchMode 调用。
