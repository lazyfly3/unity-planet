# LogTrack Unity 插件

复制 `Assets/LogTrack/` 到 Unity 项目的 `Assets/LogTrack/` 即可。

## 格式（Jacky 评审对齐）

- 每逻辑帧三个 Phase：`FixedUpdate` → `Update` → `LateUpdate`
- 调用行：`0[---depth]Namespace.Class::Method(args)`（无 file/line）
- 解析 JSON：`{ frameIndex, phases[{ phase, calls[{ depth, call }] }] }`

## 快速开始

1. 在 `Assets/LogTrack/Editor/LogTrackSetting.txt`（或 `Assets/Scripts/LogTrackSetting.txt`）配置 `IncludeAssemblies`（默认 `Assembly-CSharp`）
2. **Tools → LogTrack → 打开工具窗口**：
   - **IL 插桩**：勾选程序集（默认 `Assembly-CSharp`）→ **执行 IL 插桩**
   - **录制**：先 **Unity Play**，Play 中点击 **开始录制**；可调整 **Ring Buffer Size**
3. **Unity Play** → **开始录制** → **结束录制**（导出并弹窗）→ 可再次 **开始录制** 录下一段，无需退出 Play
4. 日志目录：`Application.persistentDataPath/LogTrack/`（工具窗口可点「打开日志目录」）

插桩在 `Library/ScriptAssemblies/*.dll` 上就地改写 IL，**不会修改** `Assets/**/*.cs` 源码。

### 工具窗口布局

| 区域 | 内容 |
|------|------|
| 录制 | 自动启动、Ring Buffer、开始/结束录制、打开日志目录 |
| IL 插桩 | 程序集筛选列表、执行 IL 插桩 |
| 开发者选项（默认折叠） | Pdb 路径、自动插桩、还原 DLL、性能基准只读 |

Stop 后会自动导出日志，并弹窗显示文本/二进制日志路径。

### 编译后自动插桩开关

| 层级 | 位置 | 说明 |
|------|------|------|
| 项目默认 | `LogTrackSetting.txt` → `AutoInstrumentOnCompile: false` | 可提交 Git，团队统一 |
| 本机覆盖 | `Tools → LogTrack → 编译后自动插桩` 或工具窗口 Toggle | 仅本机 EditorPrefs |
| 重置 | `Tools → LogTrack → 重置编译后自动插桩为项目默认` | 清除本机覆盖 |

生效值：**本机覆盖 ?? 项目默认 ?? false**。自动插桩读取 `IncludeAssemblies` / `ExcludeAssemblies`。

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

可选参数 `-logtrackAssemblies Assembly-CSharp,My.Assembly` 覆盖配置中的 Include 列表。
