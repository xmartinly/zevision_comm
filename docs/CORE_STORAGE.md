# Zevision.Core / Zevision.Storage 调用文档

Zevision 应用 (IMC300通讯测试) 的业务层和数据存储层,供维护本程序的开发者使用。界面层 (`Zevision.exe`) 只通过这里的接口工作。作者:martin.liang@inficon.com。

| 文件 | 内容 | 依赖 |
|---|---|---|
| `Zevision.Core.dll` | 业务逻辑:设备通讯服务,读取、保存、恢复、修改仪器配置,配置项规则,修改窗口的编辑会话,中英文文本,通讯配置模型;定义存储接口 | .NET Framework、`Zevision.Communication.dll`、`Zevision.Commands.dll` |
| `Zevision.Storage.dll` | 数据存储:用文件实现 Core 的存储接口 (配置文件、`settings.json`、`error.txt`、通讯数据文件),读取 `Texts.json` | .NET Framework、`Zevision.Core.dll` |

- 版本与应用相同 (**1.0.0**)。目标框架 .NET Framework 4.7.2,C# 9 (VS 2019)。
- 这两个程序集不单独发布,接口随应用修改。单独发布的通讯库和指令库见 [LIBRARY.md](LIBRARY.md)。
- 运行时需要程序旁边的 `Texts.json`:Core 和 Storage 的全部提示和错误文本都在其中。
- 本文的示例都已对照 Release 版本编译运行 (只用公开 API);"→"后是实际输出。

## 目录

1. [概述](#1-概述)
2. [组装与启动](#2-组装与启动)
3. [通用约定](#3-通用约定)
4. [设备通讯:IDeviceService](#4-设备通讯ideviceservice)
5. [读取设备配置](#5-读取设备配置)
6. [保存与恢复](#6-保存与恢复)
7. [修改设备配置](#7-修改设备配置)
8. [规则与 ID 工具](#8-规则与-id-工具)
9. [修改窗口的编辑会话](#9-修改窗口的编辑会话)
10. [中英文文本](#10-中英文文本)
11. [通讯配置 (settings.json)](#11-通讯配置-settingsjson)
12. [存储层](#12-存储层)
13. [测试与替换实现](#13-测试与替换实现)
14. [常见修改](#14-常见修改)
15. [性能与内存](#15-性能与内存)
16. [API 参考](#16-api-参考)

## 1. 概述

### 1.1 分层

```
Zevision.exe            界面:Views、ViewModels、App (组装根)
   │ 只使用 Core 的接口;只有 App 创建 Storage 的实现
   ▼
Zevision.Core.dll       业务逻辑;定义 IRecipeStore、IPreferencesStore、IErrorLog、ICommunicationRecorder(Factory)
   │          ▲
   │          └──────── Zevision.Storage.dll   用文件实现上面的接口
   ▼
Zevision.Commands.dll ─► Zevision.Communication.dll     指令集、协议与传输 (LIBRARY.md)
```

依赖只向下,单元测试 (`LibraryBoundaryTests`) 检查:

- Core 只引用 .NET Framework 和两个库,不引用 WPF 和 Storage;元数据中没有文件类型 (`File`、`Directory`、`FileStream`…),即 Core 不读写文件。
- Storage 只引用 .NET Framework 和 Core。
- 界面层的 `ViewModels/`、`Views/` 不引用 `Zevision.Storage`,只有 `App.xaml.cs` 引用。
- Core 和 Storage 的代码中没有中文字面量,文本都在 `Texts.json`。

### 1.2 设计原则

- **预期内的失败返回 `Result`**:端口被占用、设备拒绝、超时、文件被修改等,`Error` 是可以直接显示的说明 (界面语言)。程序错误 (参数为 null、ID 格式错误) 抛异常。
- **接口只用在需要替换实现的地方**:设备、存储、日志。纯计算 (配置项规则、恢复计划、ID 工具、值的校验) 是静态类,没有状态,直接调用、直接测试。
- **修改设备前先确认**:设备型号 (G03) 与配置一致,且处于空闲状态 (C09 = `Ready`)。写入后用 `Q` 读回校验;遇到第一个错误就停止,不回滚。
- **指令只由指令集生成**:`ZevisionCommandSet.Query/Update/Add/Build`,不手工拼接 `"Q" + id`。
- **一个通讯入口**:所有收发经过 `IDeviceService`,同一时间只有一个请求在等待响应。
- 异步方法都接受 `CancellationToken`;Core 和 Storage 内部一律 `ConfigureAwait(false)`。

### 1.3 命名空间

| 命名空间 | 主要类型 | 用途 |
|---|---|---|
| `Zevision.Core` | `Result`、`Result<T>`、`IErrorLog`、`ICommunicationRecorder`、`ICommunicationRecorderFactory` | 结果类型;错误日志和通讯数据记录的接口 |
| `Zevision.Core.Devices` | `IDeviceService`、`ZevisionDeviceService`、`DeviceServiceExtensions`、`ManualCommandPolicy`、`FramingCheck` | 设备连接与收发 |
| `Zevision.Core.Configuration` | `InstrumentConfigService` (`IInstrumentConfigReader`/`Writer`/`Service`)、`InstrumentSnapshot`、`InstrumentRecipe`、`RestorePlanner`、`RecipeRules`、`CrucibleRule`、`ObjectTypes`、`DeviceIds`、`ParameterValue`、`RecipeDocument`、`RecipeEditSession`、`IRecipeStore` | 仪器配置 |
| `Zevision.Core.Localization` | `Lang`、`TextCatalog`、`CommandText`、`CommunicationText` | 中英文 |
| `Zevision.Core.Settings` | `ConnectionPreferences`、`IPreferencesStore` | 通讯配置 |
| `Zevision.Storage` | `AppFolders`、`TextFile`、`JsonFilePreferencesStore`、`RecipeFileStore`、`FileErrorLog`、`CommunicationLogFiles` | 文件实现 |

## 2. 组装与启动

`App.OnStartup` 是唯一的组装位置 (composition root),不使用 DI 容器,全部是构造函数注入。顺序有要求:

1. **文本**:`TextFile.Load` → `TextCatalog.Initialize`,在任何 Core/Storage 文本被使用之前。读不到时不能继续运行 (提示只会显示键名);这时 `TextCatalog` 是空的,提示要用界面层自己的 `Lang.T`。
2. **语言**:读取 `settings.json`,`Lang.Initialize(Lang.Detect(...))`。在创建日志和任何窗口之前确定,之后不再改变 (切换语言 = 重新启动程序)。
3. **错误日志**:`FileErrorLog`,然后挂接全局异常处理。
4. **服务**:设备服务、配置服务、存储,交给视图模型。

```csharp
// 1. 文本 (Texts.json 在程序旁边)
var texts = TextFile.Load(TextFile.DefaultPath);
if (!texts.IsSuccess)
{
    // 提示 (不能用 TextCatalog) 后退出
    return;
}

TextCatalog.Initialize(texts.Value);

// 2. 语言:settings.json 的 language (auto / zh / en) 优先;读取失败时按系统
var preferences = new JsonFilePreferencesStore(AppFolders.GetSettingsFilePath());
var saved = preferences.Load();
Lang.Initialize(Lang.Detect(CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture, saved.IsSuccess ? saved.Value.Language : null));

// 3. 错误日志:第二个参数是会话标题 (AppInfo 在界面层:版本、系统、CLR、位数),第一次写入前输出为 "==== 标题 ===="
IErrorLog errorLog = new FileErrorLog(Path.Combine(AppFolders.GetLogsFolder(), "error.txt"), AppInfo.Description);

// 4. 服务
var device = new ZevisionDeviceService(DeviceTransports.Create, errorLog);   // 每次连接按 RS232 / TCP 创建传输
IInstrumentConfigService config = new InstrumentConfigService(device);
IRecipeStore recipes = new RecipeFileStore();
ICommunicationRecorderFactory recorders = new CommunicationLogFiles(errorLog);
```

退出时 (`App.OnExit`) 依次保存通讯配置、释放视图模型、`device.Dispose()` (关闭连接)。每一步单独捕获异常并写入错误日志,一步失败不影响后面的步骤。

## 3. 通用约定

### 3.1 Result

```csharp
var loaded = recipes.Load(path);
if (!loaded.IsSuccess)
{
    ShowError(loaded.Error);     // 失败时 Error 一定不为 null,可空分析也知道 (不需要 !)
    return;
}

var recipe = loaded.Value;       // 失败时访问 Value 抛 InvalidOperationException
```

- `IsSuccess` 就是 `Error == null`;`default(Result)` 视为成功;`Fail(null)` 抛 `ArgumentNullException`。
- `IsSuccess` 标有 `[MemberNotNullWhen(false, nameof(Error))]`,`if (!r.IsSuccess)` 分支中 `r.Error` 是非空的。
- 这是 Core 自己的类型 (说明已本地化)。通讯库的 `ZevisionResult` (英文说明、带错误类型 `ZevisionErrorKind`) 只在 `ZevisionDeviceService` 内部使用,由 `CommunicationText.Describe` 转换为 `Result`。
- 带更多信息的结果:`RestoreReport` (恢复、写入)、`ObjectCreation` (新建对象)。少数方法直接返回 `string?` (失败原因,成功为 null),如 `RecipeEditSession.Commit`。

### 3.2 异常与取消

| 情况 | 处理方式 |
|---|---|
| 参数为 null、ID 格式错误、指令集不支持的动作 | 抛 `ArgumentNullException` / `ArgumentException` (程序错误,调用方应修正代码) |
| 通讯失败、设备拒绝、超时、文件读写失败、文件校验失败 | 返回失败的 `Result`,说明可以直接显示 |
| 取消 | `ExecuteRestoreAsync`、`ApplyEditsAsync` 返回 `RestoreReport.Cancelled = true`;其他异步方法抛 `OperationCanceledException` |
| 错误日志、通讯数据文件写入失败 | 不抛出 (只是记录用途,不能影响通讯) |

取消不会撤销已经发送的指令:取消前设备上已经执行的修改保留。

### 3.3 线程

- Core 和 Storage 的方法可以在任何线程调用,没有 UI 线程要求。内部的 `await` 用 `ConfigureAwait(false)`;调用方 (视图模型) 自己的 `await` 不加,完成后回到 UI 线程。
- `IDeviceService` 的事件在后台线程触发,更新界面前要切到 UI 线程 (见 4.3)。
- `IProgress<RestoreProgress>` 在后台线程报告;在 UI 线程创建的 `Progress<T>` 会自动切回 UI 线程。
- 同一个 `IDeviceService` 上并发的 `SendAsync` 会自动排队。但保存、恢复、修改由多条指令组成,不要同时运行两个,否则指令会交错 (界面在操作期间禁用其他发送)。
- 静态类 (`RecipeRules`、`ObjectTypes`、`DeviceIds`、`ParameterValue`、`RestorePlanner`、`ManualCommandPolicy`、`TextCatalog`、`Lang`) 线程安全。`RecipeDocument`、`RecipeEditSession`、`InstrumentRecipe` 不是线程安全的,只在一个线程上使用。

### 3.4 文本的语言

返回的说明都按 `Lang.Current` (界面语言) 给出。测试中用 `Lang.Override(UiLanguage.English)` 临时切换,它只作用于当前异步流,不影响并行的其他测试。

## 4. 设备通讯:IDeviceService

`ZevisionDeviceService` 实现 `IDeviceService`,包装通讯库的 `ZevisionClient` (见 LIBRARY.md §5),增加了:

- 每次 `ConnectAsync` 按当时的参数新建一个 `ZevisionClient`:连接期间参数不变,换参数重连不需要重建服务;
- 结果说明按界面语言给出;
- 不显示的发送 (`SendHiddenAsync`);
- 帧格式不一致时的提示;
- 把读取循环和事件处理程序的意外异常写入 `IErrorLog`。

### 4.1 连接与断开

```csharp
var options = new ConnectionOptions(
    new SerialSettings("COM3", 115200),          // TCP:new TcpSettings("192.168.1.10", TcpSettings.DefaultPort)
    new FrameOptions(checksumEnabled: true, lengthEnabled: true),
    TimeSpan.FromSeconds(1));                     // 响应空闲超时

var connected = await device.ConnectAsync(options, cancellationToken);
if (!connected.IsSuccess)
{
    ShowError(connected.Error);                   // 如 "无法连接 COM3, 115200, 8N1: 对端口“COM3”的访问被拒绝。"
}

// …
await device.DisconnectAsync();                   // 未连接时直接返回;不触发 ConnectionLost
```

- 已连接或正在连接时再调用 `ConnectAsync` → 失败 "已经连接。"。
- 连接中断 (拔线、TCP 断开) 后直接再次 `ConnectAsync`:上一个连接的客户端被释放,不再转发它的事件。
- `Dispose()` 关闭连接,之后不再使用这个服务。
- 帧格式 (`FrameOptions`) 必须与设备一致:RS232 为 G1304 (校验和) / G1305 (长度),TCP 为 G1407 / G1408。

### 4.2 发送

```csharp
var reply = await device.SendAsync(ZevisionCommandSet.Query("M0103"), cancellationToken);   // 扩展方法,超时用连接设置
if (!reply.IsSuccess)
{
    ShowError(reply.Error);                       // 通讯失败:未连接、超时、帧无效…
    return;
}

var response = reply.Value.Responses[0];
if (!response.IsSuccess)
{
    ShowError(response.ErrorCode.Localized());    // 设备拒绝 (-A、-B、-D…) 不是通讯失败,如 "非法格式"
    return;
}

var density = response.Data;                      // "2.700"
```

| 方法 | 用途 |
|---|---|
| `SendAsync(command, minimumResponseTimeout, ct)` | 发送并等待响应,显示在数据显示区 (触发 `FrameSent` / `FrameReceived`) |
| `SendAsync(command, ct)` (扩展方法) | 同上,`minimumResponseTimeout = TimeSpan.Zero` (只用连接设置的超时) |
| `SendHiddenAsync(command, minimumResponseTimeout, ct)` | 同上,但不显示 (见 4.3);程序内部读取配置的帮助指令 (`?`、`?M`) 用它 |

- 一帧可以包含多条指令 (`QG03;QC09`),每条的响应都在 `Responses` 中,按回显对应 (`ZevisionResponseParser.EchoOf`)。
- 超时是空闲超时:从发送或最后收到的字节算起,大响应不会因为总时间长而超时。响应前停顿较长的指令传 `minimumResponseTimeout`:帮助指令用 `InstrumentConfigReader.SlowReplyTimeout` (2 秒)。
- 失败的说明已本地化。超时、帧无效、无法解析时,如果这次发送期间收到过与帧格式设置不一致的帧 (4.4),说明后附上检查设置项的提示。
- 未连接时 → 失败 "未连接。" (不抛出)。

### 4.3 事件

| 事件 | 触发 | 说明 |
|---|---|---|
| `FrameSent` | 帧发送前 | 后台线程 |
| `FrameReceived` | 收到一帧 (含错误帧、帧外数据) | 后台线程,按收到的顺序依次触发 |
| `ConnectionLost` | 读取意外结束 (拔线、TCP 断开、读取出错) | 后台线程;主动 `DisconnectAsync` 不触发 |

```csharp
var ui = SynchronizationContext.Current;                       // 在 UI 线程上取得
device.FrameReceived += (s, e) =>
{
    var lines = FormatForDisplay(e.Frame);                     // 格式化在后台线程做
    ui.Post(_ => log.AddRange(lines), null);                   // 再切到 UI 线程更新界面
};
device.ConnectionLost += (s, e) => ui.Post(_ => OnConnectionLost(e.Exception), null);
```

- 事件处理程序抛出的异常不会中断读取循环,写入 error.txt。读取循环本身的意外异常 (不是拔线等 I/O 异常) 也写入 error.txt,并触发 `ConnectionLost`。
- `SendHiddenAsync` 的收发不触发事件,因此也不保存到通讯数据文件、不计入收发计数:不触发 `FrameSent`;它的响应、超时时报告的不完整数据,以及之后收到的同回显帧 (如取消读取 `?` 后设备还在发送的剩余数据) 都不触发 `FrameReceived`,直到同一指令再用 `SendAsync` 发送。

### 4.4 帧格式不一致 (FramingCheck)

`FramingCheck.Check(frame, options)` 判断收到的帧是否说明设备的长度设置与程序不同:

| 结果 | 情况 |
|---|---|
| `ReplyHasLength` | 程序未启用长度,设备的响应带两个长度字节 |
| `ReplyWithoutLength` | 程序启用了长度,设备的响应是 `{文本}` |
| `None` | 一致,或无法判断 |

`ZevisionDeviceService` 对每个收到的帧做这个检查;发送失败时把 `CommunicationText.FramingHint(mismatch, endpoint)` 附在说明后面。`FramingCheck.SettingIds(endpoint)` 给出要检查的设置项:RS232 → `(G1304, G1305)`,TCP → `(G1407, G1408)`。

### 4.5 手动输入的指令 (ManualCommandPolicy)

```csharp
var text = ManualCommandPolicy.ToUpperCommands("qg01;um0101:Al");   // → "QG01;UM0101:Al" (数据部分不改)
var commands = ManualCommandPolicy.Split(" QG01；QG02\n\nQG03 ");    // → QG01 | QG02 | QG03 (; ； 换行都是分隔符)
var frame = string.Join(";", commands);
var timeout = ManualCommandPolicy.MinimumResponseTimeout(frame);    // 含 ? 时 2 秒,否则 TimeSpan.Zero
if (ManualCommandPolicy.ContainsFullHelpCommand(frame))
{
    // 完整的 ? 约 116 KB、10 秒,先提示用户 (?M 等组帮助不算)
}

var reply = await device.SendAsync(frame, timeout, cancellationToken);
```

设备只认大写命令 (小写回 `-A`);数据可能是名称,保持原样。

## 5. 读取设备配置

`InstrumentConfigService` (构造参数为 `IDeviceService`) 同时实现读取 (`IInstrumentConfigReader`) 和修改 (`IInstrumentConfigWriter`)。只需要读取的类 (如修改窗口的"信息"页) 依赖 `IInstrumentConfigReader`。

| 方法 | 发送的指令 | 耗时 (115200 bps) | 结果 |
|---|---|---|---|
| `ReadSnapshotAsync(ct)` | `?` (不显示) | 约 10 秒,116 KB (实测) | `InstrumentSnapshot`:全部项及当前值 |
| `ReadGroupAsync(group, ct)` | `?M` 等组帮助 (不显示) | 材料约 0.5 秒 (实测);膜系最大,按大小估算约 5 秒 | 该组可保存的项 (挑选规则同 `ToRecipe`) |
| `QueryAsync(ids, ct)` | `Q` 指令,每帧 20 条 (`QueryBatchSize`) | 很短 | 每项的值或设备拒绝的原因 |

```csharp
var snapshot = await config.ReadSnapshotAsync(cancellationToken);
if (!snapshot.IsSuccess)
{
    ShowError(snapshot.Error);                     // 如 "读取设备配置失败: …"
    return;
}

var device = snapshot.Value;
var line = $"{device.DeviceName} {device.FirmwareVersion} {device.CurrentState}";   // "IMC300 1.8.11.202606101229 Ready"
var density = device.GetValue("M0103");            // "2.700";设备上没有该项时为 null

var values = await config.QueryAsync(new[] { "M0101", "M0103", "M0901" }, cancellationToken);
if (values.IsSuccess)
{
    foreach (var value in values.Value)
    {
        Console.WriteLine(value.Error == null ? $"{value.Id} = {value.Value}" : $"{value.Id}: {value.Error}");
    }
}
// → M0101 = Al / M0103 = 2.700 / M0901: 非法格式      (单项被拒绝不影响其他项)
```

- `InstrumentSnapshot` 的常量:`DeviceNameId` = `G03`、`StateId` = `C09`、`ReadyState` = `Ready`。属性:`DeviceName` (G03)、`ModelNumber` (G05)、`SerialNumber` (G06)、`FirmwareVersion` (G08)、`CurrentState` (C09)、`Entries` (保持设备给出的顺序)。`GetValue` / `GetDescription` 按 ID 查找,没有时为 null;ID 重复时取第一次出现的。
- `ReadGroupAsync`:响应中没有该组的数量项 (`MFE`、`FFE`、`P02FE`…) 时返回失败,不会把解析失败当成"该组为空"。
- `QueryAsync`:通讯失败时整体失败;单项被拒绝时该项 `Value` 为 null、`Error` 为原因 (如 "非法格式")。

## 6. 保存与恢复

### 6.1 保存

```csharp
var snapshot = await config.ReadSnapshotAsync(cancellationToken);      // 约 10 秒
if (!snapshot.IsSuccess) { ShowError(snapshot.Error); return; }

var recipe = snapshot.Value.ToRecipe(DateTimeOffset.Now);             // 按 RecipeRules 挑出可恢复的项
var saved = recipes.Save(recipe, path);                                 // 计算 MD5;先写临时文件再替换
if (!saved.IsSuccess) { ShowError(saved.Error); }
```

实机:帮助表 1525 行 → 配置文件 940 项,约 92 KB。

### 6.2 恢复

```csharp
var loaded = recipes.Load(path);                                         // 校验格式版本、MD5、字段、ID
if (!loaded.IsSuccess) { ShowError(loaded.Error); return; }              // 如 "…MD5 校验失败,文件在保存后被修改过或已损坏…"

var snapshot = await config.ReadSnapshotAsync(cancellationToken);
if (!snapshot.IsSuccess) { ShowError(snapshot.Error); return; }

var planned = RestorePlanner.Plan(loaded.Value, snapshot.Value);        // 纯计算,不通讯
if (!planned.IsSuccess) { ShowError(planned.Error); return; }           // 型号不符、设备不在 Ready

var plan = planned.Value;
if (plan.Creates.Count == 0 && plan.Writes.Count == 0)
{
    return;                                                              // 设备与文件一致 (plan.UnchangedCount 项)
}

if (!Confirm(plan))                                                      // 显示 Writes、Creates、Skipped、Warnings
{
    return;
}

var report = await config.ExecuteRestoreAsync(loaded.Value, plan, progress, cancellationToken);
```

界面中读取和执行各有 60 秒时限 (`OperationLimits.PhaseTimeLimit`,界面层),用户阅读确认框的时间不计入。

### 6.3 恢复计划:RestorePlanner.Plan

不与设备通讯,比较文件与快照:

1. 设备未识别 (没有 G03)、型号不同 (G03;两边都有 G05 时也比较 G05)、状态不是 `Ready` → 失败。
2. 固件版本不同 → `Warnings` (不阻止恢复)。
3. 文件中有、设备上没有的项:
   - 所属的顶层对象 (材料、膜系、膜层、工艺) 不存在 → 在 `Creates` 中按原编号创建 (`AM03`),顺序为 材料 → 膜系 → 膜层 → 工艺;
   - 子列表的条目不存在 (如膜系的第 2 条预熔 `F010302`,或工艺的膜层条目 `P02010701`) → 在 `Creates` 中追加到列表末尾 (`AF010300`)。要求设备有 `<列表>00` 项,且现有条目从 01 连续编号;
   - 都不能创建 → `Skipped`。

   这些项不在 `Writes` 中:新对象带的是设备默认值,要等创建后重新读取才知道该写哪些。
4. 与设备当前值相同 → 计入 `UnchangedCount`。
5. 不同 → `Writes`,按 `RecipeRules.GroupRank` 排序 (N S I O T G M F L P),组内保持文件中的顺序。
6. 跳过 (`Skipped`):文件中重复的 ID;不可恢复的项 (8.1);值含协议字符 (`;`、`{`、`}` 或非 ASCII);引用的对象 (如 `F0302 = 3` → `M03`) 既不在设备上、也不会被创建 (设备必然回 `-B`);膜系的坩埚号超出所选输出源的坩埚数量 (按恢复后的输出源和坩埚数量判断,见 8.5)。

`RestoreSkip` 带 ID、文件中的值、说明和原因,`ToString()` 为单行文本,如 `F01030301 Start Ramp/Rise time = 0:00:30 (原因)`。

### 6.4 执行恢复:ExecuteRestoreAsync

1. 再次确认型号和 `Ready`:计划是在确认框之前做的,用户确认期间设备可能已开始工艺。不满足时不写任何项。
2. 写入系统设置 (`RecipeRules.IsSystemSetting`:N S I O T G)。
3. 有 `Creates` 时:发送创建指令 → 重新读取 `?` → 重新计划,循环直到不再需要创建,最多 4 轮 (嵌套的列表要等上级创建后才出现)。创建的对象没有出现在预期编号上时停止。
4. 写入其余的项:对象的设置,以及创建后才出现的系统项 (如协议文档中输出源的坩埚材料 `S**09**`,追加后才出现)。第 2 步写过的项不重复写。
5. 用 `Q` 逐项读回,与写入的值比较 (`ParameterValue.AreEquivalent`)。

- 遇到第一个设备拒绝就停止,不回滚。设备上比文件多出的对象或条目不删除。
- 取消时返回 `Cancelled = true` (不抛出),已写入的项保留在设备上。
- 进度 `RestoreProgress(Stage, Completed, Total)`:`Stage` 依次为"写入系统设置"、"创建" ("创建 (第 2 轮)"…)、"重新读取设备"、"写入"、"校验" (`ApplyEditsAsync` 先有"检查设备");`Total = 0` 表示进度未知。例:只改一项材料时依次报告 `写入 1/1`、`校验 1/1`。

### 6.5 RestoreReport

| 属性 | 含义 |
|---|---|
| `IsSuccess` | 没有错误、没有取消,且读回全部一致 |
| `Error`、`FailedId` | 停止的原因和项 |
| `FailedDuringCreate` | 失败发生在创建阶段 (含其后的重新读取、重新计划),而不是写入阶段 |
| `Cancelled` | 被取消 |
| `CreatedCount`、`WrittenCount`、`PlannedWriteCount` | 创建的对象数、写入的项数、计划写入的项数 (含重新计划的结果) |
| `VerifyMismatches` | 读回与写入值不一致的项 (已格式化的文本) |
| `AdditionalSkipped` | 创建后重新计划时新出现的跳过项 (如新对象子列表的条数与文件不同),应作为错误提示 |

## 7. 修改设备配置

`IInstrumentConfigWriter` 的每个方法都带 `deviceName` (配置所属的设备,与 G03 比较),开始时用一帧 `QG03;QC09` 确认型号和 `Ready`,不满足时不发送任何修改指令。

| 方法 | 发送的指令 (例) | 结果 |
|---|---|---|
| `ApplyEditsAsync(recipe, edits, progress, ct)` | 检查每项 → `QG03;QC09` → `UM0103:2.710`… → `QM0103`… | `RestoreReport` |
| `CreateObjectAsync(type, id, defaultValue, deviceName, ct)` | `AF02`,再设默认引用 `UF0202:1` → `QF0202` | `ObjectCreation(Error, DefaultsError)` |
| `DeleteObjectAsync(id, deviceName, ct)` | `DM03` | `Result` |
| `AppendListItemAsync(listId, value, deviceName, ct)` | `AP02010700` → `QP020107FE`;`value` 不为 null 时 `UP0201070n:值` → `QP0201070n` | 新条目的 ID |
| `RemoveLastListItemAsync(listId, deviceName, ct)` | `QF0103FE` → `DF010300` → `QF0103FE` (须少一条) | 被删除的条目 ID |

```csharp
var definition = ZevisionCommandSet.Find("M0103");
var value = ParameterValue.Normalize(definition, "2.71", "2.700");          // → "2.710" (与设备的小数位数一致)
var edits = new[] { new RecipeEntry { Id = "M0103", Value = value, Description = "Density" } };

var report = await config.ApplyEditsAsync(recipe, edits, progress: null, cancellationToken);
if (!report.IsSuccess)
{
    ShowError(report.Cancelled ? "已取消" : report.Error ?? string.Join(Environment.NewLine, report.VerifyMismatches));
}
// 设备不在 Ready 时 → "设备当前状态为 Running,只有在 Ready (空闲) 状态下才能修改配置。"
```

- `ApplyEditsAsync`:先检查每一项可以写 (`RecipeRules.IsRestorable`)、不含协议字符,膜系的坩埚号不超过所选输出源的坩埚数量 (同时修改的输出源和坩埚数量按新值,见 8.5),任何一项不合格都不发送;按恢复的顺序写入,逐项读回。不创建对象:设备上不存在的项会被设备拒绝。
- `CreateObjectAsync` 的默认引用按 `type.NewDefault` (8.2):膜系写材料项 `F..02`;膜层先追加膜系列表一项 (`AL..0200`) 再写其膜系 (`L..02nn01`);工艺追加膜层列表一项并写入 (`AP02..0700` → `UP02..07nn`)。`defaultValue` 为 null 时只创建。对象创建成功但默认引用失败时 `Error` 为 null、`DefaultsError` 为原因 (对象保留在设备上)。
- 只有 `M**`、`F**`、`L**`、`P02**` 可以按编号新建/删除 (指令集检查,其他 ID 抛 `ArgumentException`)。按编号删除 (`DM03`) 协议文档没有说明;按编号新建已在实机确认 M、F,L、P 是推断的。
- 修改后调用方应重新读取该组 (`ReadGroupAsync`) 确认结果,编辑会话就是这样做的 (9.3)。

## 8. 规则与 ID 工具

纯函数,静态类,没有状态,线程安全。

### 8.1 RecipeRules:哪些项保存和恢复

`IsRestorable(id, value, out reason)` 依次检查:

1. 值为空 → 否 (分组标题、动作命令)。
2. 组白名单:M F L P N S T I O,以及系统组中的 G0E (显示)、G0F (声音)、G12 (图表)。通讯 (G13、G14)、时间 (G0A)、复位 (G0D)、锁定 (G11)、工艺控制 (C) 都不保存。
3. ID 格式 (`DeviceIds.IsWellFormed`)。
4. 排除规则:列表计数 (`…FE`);传感器的运行状态;源的当前输出、开关挡板、切换坩埚 (物理动作);输入输出的分配和当前状态 (写输出状态会直接动作继电器);工艺运行次数;最后使用的晶振。
5. 指令集中定义为只读的项 (如 `N**08`、`N**1F`)。

规则依据 IMC300 固件 1.8.11 的帮助表推断,没有逐项在实机上验证。恢复时设备拒绝某项会停止并报告 ID,据此调整这里的规则即可。

| 方法 | 用途 |
|---|---|
| `IsRestorable(id, value, out reason)` | 上面的规则 |
| `SelectRestorable(entries)` | 从帮助表中挑选 (ID 重复时取第一次出现的);`ToRecipe` 和 `ReadGroupAsync` 都用它 |
| `IsSystemSetting(id)` | N S I O T G 组:恢复时最先写入 |
| `GroupRank(id)` | 恢复顺序的排序键:N S I O T G M F L P,其他组排最后 |
| `TryGetReference(id, value, out targetId, out kind)` | 恢复计划使用的引用:`F??02` → 材料,`L??02??01` → 膜系 |
| `FindReferencesTo(entries, objectId)` | 删除前检查:上面两条,再加上 `P02??07??` → 膜层、`P01` 和 `P02??0A` → 工艺、`S??09??` → 材料 |

```csharp
RecipeRules.IsRestorable("M0103", "2.700", out _);              // → true
RecipeRules.IsRestorable("G1303", "115200", out var reason);    // → false,"不在保存范围内的组 (工艺控制、系统、通讯、Hello 等)"
RecipeRules.IsRestorable("MFE", "3", out reason);               // → false,"列表计数"
RecipeRules.IsRestorable("S0106", "0", out reason);             // → false,"开/关挡板 (物理动作)"
RecipeRules.IsRestorable("N0108", "4", out reason);             // → false,"只读项 (协议只允许查询)"
RecipeRules.TryGetReference("F0302", "3", out var target, out var kind);   // → true,"M03","材料";值 0 (未选择) → false
RecipeRules.FindReferencesTo(entries, "M03");                   // entries 含 F0102=3、F0202=1、S010901=3 → F0102、S010901
new[] { "P0101", "M0101", "G0E01", "N0101", "X01" }.OrderBy(RecipeRules.GroupRank);   // → N0101 G0E01 M0101 P0101 X01
```

删除检查比恢复计划多用几条规则,是因为实机上工艺的膜层列表有指向不存在膜层的值 (31/32/33,而只有 L01–L09):恢复计划若用它,会跳过这些写入;删除检查只比较是否等于要删除的对象,不受影响。坩埚材料 `S??09??` (协议文档) 属于系统设置,在创建材料之前写入,恢复计划不使用;固件 1.8.11 的设备上也没有这个列表 (保存的配置和抓包中都没有)。

### 8.2 ObjectTypes:可以新建/删除的对象

| 类型 | 组 | 对象 ID | 可增删的子列表 (`Lists`) | 新建后的默认引用 (`NewDefault`) |
|---|---|---|---|---|
| `ObjectTypes.Material` | M | `M01`… | 无 | 无 |
| `ObjectTypes.Film` | F | `F01`… | `03` 预熔、`05` 后处理 (分组标题中的 + / −) | `F..02` (材料) = 第一个材料 |
| `ObjectTypes.Layer` | L | `L01`… | `02` 膜系列表 (为空时显示添加行) | 追加 `L..02` 一项,其 `01` (膜系) = 第一个膜系 |
| `ObjectTypes.Process` | P | `P0201`… (`P01` 是活动工艺,不是对象) | `07` 膜层列表 (每项可删除) | 追加 `P02..07` 一项 = 第一个膜层 |

层级 (用户确认):工艺包含膜层,膜层包含膜系,膜系包含材料。`ObjectTypes.All` 按被引用的在前排列 (材料 → 膜系 → 膜层 → 工艺)。修改窗口、恢复计划和引用检查都按这张表处理对象,不各自判断组字母。

```csharp
ObjectTypes.Find('M');                                    // → Material;不能新建的组 (如 'G') → null
ObjectTypes.TryGetObject("F01030101", out var type, out var objectId);   // → true,Film,"F01"
ObjectTypes.TryGetObject("P020107", out type, out objectId);            // → true,Process,"P0201"
ObjectTypes.OfObjectId("P0201");                          // → Process;"P01" → null
ObjectTypes.GroupKey("F01030101");                        // → "F01" (修改窗口中的分组)
ObjectTypes.GroupKey("P020101");                          // → "P0201"
ObjectTypes.GroupKey("G0E02");                            // → "G0E" (非对象组:组字母 + 第一级编号)
ObjectTypes.Material.NextFreeId(new HashSet<string> { "M01", "M03" });  // → "M02";用完 (01–FD) 时为 null
ObjectTypes.Film.ListPattern(ObjectTypes.Film.Lists[0]);  // → "F**03" (在指令集中查列表名称)
ObjectTypes.Film.Kind;                                    // → "膜系" / "Film" (界面语言)
```

### 8.3 DeviceIds:ID 的组成

ID = 组字母 + 若干级两位十六进制编号。列表条目编号为 01–FD;`00` 是"在末尾追加 / 删除最后一项"的指令项,`FE` 是条数项。程序中拼接和拆分 ID 都用这些方法,不手写后缀。

```csharp
DeviceIds.Item("F0103", 2);        // → "F010302"  列表的第 2 项
DeviceIds.CountOf("F0103");        // → "F0103FE"  条数项;组的对象数:CountOf("M") → "MFE"
DeviceIds.AppendSlotOf("F0103");   // → "F010300"  A 追加 / D 删除最后一项
DeviceIds.NameOf("M03");           // → "M0301"    对象的名称项
DeviceIds.LastNumber("M0A");       // → 10
DeviceIds.IsItemNumber("0A");      // → true;"00"、"FE" → false
DeviceIds.IsCount("F0103FE");      // → true
DeviceIds.TryParseCount("12", out var count);   // → true,12 (设备返回的十进制条数)
DeviceIds.IsWellFormed("F0103");   // → true;"F013" (不是两位一级)、"m01" (小写) → false
```

### 8.4 ParameterValue:值的校验、规整和比较

数字一律按 `InvariantCulture` (小数点为 `.`)。

```csharp
var density = ZevisionCommandSet.Find("M0103");          // 密度 (g/cm³),范围 0.100 ~ 100.000 g/cm³
ParameterValue.Validate(density, "2.7");                 // → null (合法)
ParameterValue.Validate(density, "abc");                 // → "应为数字 (小数点用 .)"
ParameterValue.Validate(density, "500");                 // → "超出范围 0.100 ~ 100.000 g/cm³"
ParameterValue.Validate(null, "a;b");                    // → "不能包含 ; { }" (文档未列出的项只检查协议字符)
ParameterValue.Normalize(density, " 2.7 ", "5.890");     // → "2.700" (按原值的小数位数)
ParameterValue.AreEquivalent("5.890", "5.89");           // → true (数值相等)
ParameterValue.AreEquivalent("0:00:05", "00:00:05");     // → true (秒数相等)
ParameterValue.AreEquivalent("Al", "al");                // → false (文本区分大小写)
ParameterValue.TryParseTime("1:30", out var seconds);    // → true,90 (小时为 0 时可写 M:SS)
ParameterValue.FormatTime(7205);                         // → "2:00:05"
```

- 类型和范围来自指令集 (`CommandDefinition`):数字、时间 (`H:MM:SS`,小时 ≤ 8760,部分项有自己的上下限)、选项、引用、文本。
- 设备上的值可能超出文档的范围:界面只在值被修改后才检查类型和范围,协议字符总是检查。
- 写入前用 `Normalize` 规整:设备按固定小数位保存,读回校验才能一致。

### 8.5 CrucibleRule:膜系的坩埚号

膜系的坩埚号 `F**0902` 的取值为 0 = 任意 (Any),或 1 ~ 膜系所选输出源 (`F**0901`) 的坩埚数量 (`S**07`)。设备上的选项就是 Any、1 ~ 最大数量 (用户确认);协议文档写的 "0 = disabled" 与设备不符。输出源未选择、不存在,或坩埚数量未知或为 0 时,最大坩埚号按 `DefaultCount` (1) 计。

```csharp
// 实机:S01 有 6 个坩埚,S02 有 8 个;膜系 F01 选输出源 1
Func<string, string?> valueOf = id => values.TryGetValue(id, out var v) ? v : null;
CrucibleRule.IsCrucible("F010902");                  // → true;"F010901" (输出源) → false
CrucibleRule.MaxFor("F010902", valueOf);             // → 6;F01 未选择输出源 (F010901 = 0) 时 → 1
CrucibleRule.Check("F010902", "6", valueOf);         // → null (可以);"0" (任意) 总是可以
CrucibleRule.Check("F010902", "7", valueOf);         // → "坩埚 7 超出输出源 S01 的坩埚数量 (6)"
```

| 使用处 | `valueOf` 取的值 | 超出时 |
|---|---|---|
| 修改窗口的下拉框 (界面层 `ParameterOptions`) | 页面中的配置 | 选项为 `0 - 任意`、1 ~ `MaxFor`;超出的当前值保留并标为"(未知)" |
| `ApplyEditsAsync` 写入前 | 修改的新值,其他取配置 | 不发送任何指令,`Error` = "F010902 不能修改: 坩埚 7 超出…" |
| `RestorePlanner.Plan` | 文件中的值 (恢复后),文件中没有的取设备 | 放入 `Skipped`,原因同上 |

只在坩埚数量已知时检查;0 和不是数字的值不检查 (后者由 `ParameterValue` 校验)。

## 9. 修改窗口的编辑会话

### 9.1 RecipeDocument

修改窗口中正在编辑的配置 (`InstrumentRecipe`) 及对它的操作。不与设备通讯,不读写文件,可以单独测试。

| 方法 | 用途 |
|---|---|
| `ObjectIds(group)` / `ObjectIdsIn(entries, group)` | 现有的对象 ID,如 `M01`、`P0201` |
| `ObjectName(objectId)` | 对象的名称 (第 01 项),没有时为 null |
| `ListItemCount(listId)` | 列表的条数 (不含 00 和 FE 项) |
| `ReplaceGroup(group, entries)` | 用重新读取的数据替换该组 (保持原来的位置) |
| `AddObject(objectId, source)` / `RemoveObject(objectId)` | 加入 (从重新读取的组复制) 或删除对象的全部项 |
| `AddListItem(listId, itemId, source)` / `RemoveListItem(itemId)` | 加入或删除列表条目及其子项 |
| `SetValues(values)` | 按 ID 修改值 |
| `Clone()` | 复制整份配置,修改副本不影响原文档 |
| `CompareObjectChange(action, objectId, before, after)` | 核对新建/删除的结果:返回 `(missing, extra)`,都为空表示只有预期的变化 |

### 9.2 RecipeEditSession:两种数据来源

`RecipeEditSession.Open(recipe, path, config, recipes)` 按 `path` 选择:

| | 实机数据 (`path = null`) | 保存的配置 (`path` 为文件) |
|---|---|---|
| 页面内容 | 刚读取的设备数据 (`InstrumentSnapshot.ToRecipe`) | 文件内容 (已通过 `IRecipeStore.Load` 校验) |
| `ReadDeviceGroupAsync` (新建/删除前) | 页面中的该组 (不通讯) | 读取设备上的该组 (`?M`…) |
| `UsedObjectIds` (新建时避开的编号) | 设备上的编号 | 设备和文件中的编号 |
| `RereadGroupAsync` (修改后) | 读取并替换页面中的该组 | 读取,页面不变 (页面是文件) |
| `ApplyObjectChange` / `ApplyListAppend` / `ApplyListRemove` | 不做什么 (已由重新读取更新) | 把变化写进文件并立即保存 |
| `CheckListCountAsync` (列表增删前) | 不检查 | 设备与文件的条数必须相同,否则编号对不上 |
| `Commit(change)` | 直接修改页面 | 先改副本并保存文件,成功后才修改页面 |

设备操作 (`CreateObjectAsync`、`DeleteObjectAsync`、`AppendListItemAsync`、`RemoveLastListItemAsync`、`ApplyEditsAsync`) 都转给 `IInstrumentConfigService`,设备名取自配置。`CreateObjectAsync` 自动带上默认引用:`DefaultReferenceId(type)` 是被引用组中编号最小的对象 (如 `M01`),写入的值是它的十进制编号。

`Commit` 是写时复制:保存失败时页面仍与文件一致,不需要回滚代码;但设备可能已经改了,错误说明会提到这一点。

### 9.3 典型流程

新建对象 (修改窗口的"新建"):

```csharp
var session = RecipeEditSession.Open(recipe, path, config, recipes);
var type = ObjectTypes.Film;

var group = await session.ReadDeviceGroupAsync(type.Group, ct);            // 设备上现有的对象
if (!group.IsSuccess) { ShowError(group.Error); return; }
var before = RecipeDocument.ObjectIdsIn(group.Value, type.Group);
var id = type.NextFreeId(session.UsedObjectIds(type.Group, before));     // 如 "F02"

var created = await session.CreateObjectAsync(type, id, ct);             // AF02,并把第一个材料写入 F0202
var after = await session.RereadGroupAsync(type.Group, ct);              // 无论成功与否都重新读取:设备状态以读取为准
if (!after.IsSuccess) { ShowError(after.Error); return; }

var error = created.Error;
if (error == null)
{
    var (missing, extra) = RecipeDocument.CompareObjectChange(
        CommandActions.Add, id, before, RecipeDocument.ObjectIdsIn(after.Value, type.Group));
    // missing / extra 不为空:设备的变化与预期不同,提示
    error = session.ApplyObjectChange(CommandActions.Add, id, after.Value);   // 保存的配置:写进文件;失败为原因
}
// 重新生成页面;created.DefaultsError 不为 null 时提示默认引用没有设置成功
```

- 删除:先 `RecipeRules.FindReferencesTo(session.Document.Entries, id)`,有引用时拒绝并列出引用的项;然后 `DeleteObjectAsync` → `RereadGroupAsync` → 核对 → `ApplyObjectChange(CommandActions.Delete, …)`。保存的配置中删除设备上没有的对象时只改文件,不发指令。
- 列表追加:`CheckListCountAsync` → `AppendListItemAsync(listId, value)` (预熔、后处理一项有多个设置,`value` 传 null 使用设备默认值;工艺的膜层传膜层编号) → `RereadGroupAsync` → `ApplyListAppend`。
- 删除列表中间的一项:设备只能删除最后一项,先用 `ApplyEditsAsync` 把后面的项前移一位,再 `RemoveLastListItemAsync`,最后 `ApplyListRemove(removedId, shifted)`。
- 保存修改的值:`ApplyEditsAsync(edits, progress, ct)`,全部成功后 `Commit(d => d.SetValues(values))`。失败时页面和文件都不变。
- 新建、删除、列表增删之前,页面上不能有勾选 (未保存) 的项:页面重新生成会丢弃它们。

## 10. 中英文文本

### 10.1 Lang

```csharp
Lang.Initialize(Lang.Detect(CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture, setting));
Lang.Detect(new CultureInfo("en-US"), new CultureInfo("en-US"), "auto");   // → English
Lang.Detect(new CultureInfo("en-US"), new CultureInfo("zh-CN"), null);     // → Chinese (区域格式为中国)
var text = Lang.T("已连接", "Connected");                                  // 界面层成对给出的文本
using (Lang.Override(UiLanguage.English)) { /* 测试英文 */ }
```

- 规则:`setting` 为 `zh` / `en` 时按设置;否则系统显示语言是中文、或区域格式是中国 (CN) 时用中文,其他用英文。
- 未调用 `Initialize` 时为中文 (现有测试依赖这一点)。
- 语言在启动时确定一次。切换语言是重新启动程序,不在运行中重新翻译。

### 10.2 TextCatalog 与 Texts.json

Core 和 Storage 的提示和错误文本放在翻译文件 `Texts.json` (源文件 `Zevision.Core/Localization/Texts.json`,编译时复制到每个引用 Core 的项目的输出目录):

```json
{
  "DeviceModelMismatch": { "zh": "设备型号不符:文件来自 {0},当前设备是 {1}。", "en": "Device model mismatch: the file is from {0}, the connected device is {1}." },
  "NotConnected": { "zh": "未连接。", "en": "Not connected." }
}
```

调用处用类型化的访问器 (`Zevision.Core/Texts.cs`、`Zevision.Storage/Texts.cs`,都是 `internal`),键就是成员名:

```csharp
internal static class Texts
{
    public static string NotConnected => TextCatalog.Get(nameof(NotConnected));

    public static string DeviceModelMismatch(string fileDevice, string connectedDevice) =>
        TextCatalog.Format(nameof(DeviceModelMismatch), fileDevice, connectedDevice);
}

return Result.Fail(Texts.DeviceModelMismatch(recipe.DeviceName, name));
```

- 有参数的文本是 `string.Format` 格式 (`{0}`、`{0:0}`,字面的大括号写成 `{{ }}`,按当前区域设置格式化);没有参数的原样使用。
- 新增文本:在 JSON 中加一条 (zh 和 en 都要有),在对应的 `Texts.cs` 中加一个成员。
- `TextCatalogTests` 检查:键与访问器一一对应 (Core 和 Storage 之间不重复)、中英文都有、英文中没有中文、占位符与参数个数一致、每个访问器在两种语言下都能生成。
- 运行时缺键或格式错误时返回键名或原始模板,不抛出 (测试保证不会发生)。
- 文件为 UTF-8 (带不带 BOM 都可以)。

### 10.3 CommandText 与 CommunicationText

两个库本身不依赖界面语言:指令库同时给出中英文 (`TextLanguage` 参数),通讯库只给英文和错误类型。这两个适配器按 `Lang.Current` 选择:

```csharp
var density = ZevisionCommandSet.Find("M0103");
CommandText.Label(density);                                  // → "密度 (g/cm³)";英文 → "Density (g/cm^3)"
CommandText.RangeText(density);                              // → "0.100 ~ 100.000 g/cm³"
CommandText.LabelFor(ZevisionCommandSet.Find("F01030101"), "F01030101");   // → "预熔 1 · 起始斜坡/上升时间"
CommandText.ObjectKind('P');                                 // → "工艺"
ZevisionErrorCode.IllegalArgument.Localized();               // → "非法参数值";英文 → "Illegal parameter value"
CommunicationText.ValidateValue("a;b");                      // → "不能包含 ; { }"
CommunicationText.Describe(error, command);                  // ZevisionError → 界面语言的说明
```

## 11. 通讯配置 (settings.json)

`ConnectionPreferences` 是 DataContract,保存为 `settings.json`。缺少的字段保持默认值 (`[OnDeserializing]` 先设默认值)。

| 属性 | JSON 键 | 默认值 | 说明 |
|---|---|---|---|
| `PortName` | `portName` | 空 | 上次使用的串口 |
| `BaudRate` | `baudRate` | 115200 | |
| `ResponseTimeoutMs` | `responseTimeoutMs` | 1000 | 响应空闲超时 |
| `SerialChecksumEnabled` | `checksumEnabled` | false | RS232 的校验和 (G1304) |
| `SerialLengthEnabled` | `lengthEnabled` | false | RS232 的长度字节 (G1305) |
| `SerialSaveCommunicationData` | `rawCaptureEnabled` | false | RS232 连接时保存通讯数据;键名沿用开发版本 |
| `TcpChecksumEnabled` | `tcpChecksumEnabled` | 同 RS232 | TCP 的校验和 (G1407) |
| `TcpLengthEnabled` | `tcpLengthEnabled` | 同 RS232 | TCP 的长度字节 (G1408) |
| `TcpSaveCommunicationData` | `tcpSaveCommunicationData` | 同 RS232 | TCP 连接时保存通讯数据 |
| `EditSource` | `editSource` | `Device` | 修改配置的数据来源 (`Device` / `File`) |
| `ConnectionType` | `connectionType` | `Serial` | `Serial` / `Tcp` |
| `TcpHost` | `tcpHost` | 空 | 设备 IP;端口固定 2101,不保存 |
| `Language` | `language` | `auto` | `auto` / `zh` / `en` |
| `DisplayFormat` | `displayFormat` | `Ascii` | `Ascii` / `Hex` / `Binary` |
| `RepeatSend` | `repeatSend` | false | |
| `RepeatIntervalMs` | `repeatIntervalMs` | 1000 | |

不认识的值 (如旧版本或手工编辑) 由视图模型按默认处理。

校验和、长度字节和保存通讯数据按连接方式分别保存,因为设备上 RS232 (G1304/G1305) 与 TCP (G1407/G1408) 是独立的设置。RS232 沿用 0.9.x 的键名;0.9.x 的文件没有 TCP 的三项,读取时 TCP 取与 RS232 相同的值 (以前两种连接共用一组),升级后界面不变。

```csharp
IPreferencesStore store = new JsonFilePreferencesStore(AppFolders.GetSettingsFilePath());
var loaded = store.Load();          // 文件不存在 → 成功,默认值;文件损坏 → 失败 (调用方记录后使用默认值)
var preferences = loaded.IsSuccess ? loaded.Value : new ConnectionPreferences();
preferences.PortName = "COM3";
var saved = store.Save(preferences);
```

## 12. 存储层

### 12.1 AppFolders

| 方法 | 位置 |
|---|---|
| `GetSettingsFilePath()` | `<程序目录>\settings.json` |
| `GetRecipeFolder()` | `<程序目录>\recipe` |
| `GetLogsFolder()` | `<程序目录>\logs` (`error.txt`、`capture_*.txt`) |

程序目录不可写时 (如装在 Program Files) 改用 `文档\Zevision\…`;日志目录再不可写时用系统临时目录。目录不存在时创建。每次调用都会探测一次可写性 (创建并删除一个临时文件),不要在循环中调用。

### 12.2 RecipeFileStore:仪器配置文件

`IRecipeStore` 的实现。`Folder` 为 `AppFolders.GetRecipeFolder()` (第一次使用时确定),`FileExtension` 为 `.json`。

文件格式 (`formatVersion` 2),缩进的 UTF-8 JSON:

```json
{
  "formatVersion": 2,
  "deviceName": "IMC300",
  "modelNumber": "IMC",
  "firmwareVersion": "1.8.11.202606101229",
  "serialNumber": "…",
  "savedAt": "2026-10-16T15:26:48.6674462+08:00",
  "md5": "543a532802cc86c013d1b99f7db26d23",
  "entries": [
    { "id": "F0101", "value": "F_SiO2", "description": "Name" },
    { "id": "F0102", "value": "1", "description": "Material" }
  ]
}
```

- **保存** (`Save`):计算 MD5 写入 `recipe.Md5`,先写同目录的临时文件 (`.tmp`) 再替换目标文件。写到一半出错时原文件完好。
- **读取** (`Load`) 按顺序检查:能否读取和解析 → 格式版本 (版本 1 没有校验值,拒绝并提示重新保存;其他版本不支持) → MD5 存在且格式正确 → **MD5 一致** → 头部字段齐全、设备名不为空 → 至少一项 → 每项 ID 合法 (`DeviceIds.IsWellFormed`)、值和说明不为 null。先校验 MD5,任何改动 (包括删字段) 都报告为"文件被修改"。
- **MD5 的计算**:不对 JSON 原文计算 (缩进、字段顺序不影响)。按固定顺序把每个字段写成 `<UTF-16 长度>:<内容>\n` (null 写成 `-1:\n`),对其 UTF-8 编码求 MD5,输出 32 位小写十六进制。顺序:formatVersion、deviceName、modelNumber、firmwareVersion、serialNumber、savedAt、条目数,然后每个条目的 id、value、description。修改算法或字段必须提升 `InstrumentRecipe.CurrentFormatVersion`。
- MD5 只能发现意外的改动 (手工编辑、传输损坏)。算法公开,有意篡改的人可以重新计算;需要防篡改时应改用带密钥的签名 (如 HMAC)。
- Windows 启用 FIPS 策略时 MD5 不可用,保存和读取返回失败 (说明原因)。
- 文件可以带 UTF-8 BOM (记事本"另存为 UTF-8"会加上)。

### 12.3 JsonFilePreferencesStore

`IPreferencesStore` 的实现,读写方式与配置文件相同 (DataContract JSON、临时文件 + 替换、可带 BOM)。文件不存在时 `Load` 成功并返回默认值;内容损坏或无法读取时返回失败。

### 12.4 FileErrorLog

`IErrorLog` 的实现。线程安全,写入失败时静默放弃,永远不抛出。

```csharp
var log = new FileErrorLog(Path.Combine(AppFolders.GetLogsFolder(), "error.txt"), AppInfo.Description);   // 会话标题
log.Write("连接", "无法连接 COM3, 115200, 8N1: 对端口“COM3”的访问被拒绝。", exception);
```

```
==== Zevision 1.0.0  |  Microsoft Windows NT 10.0.26200.0  |  .NET CLR 4.0.30319.42000  |  64 位进程 ====
2026-10-18 23:28:29.123 [连接] 无法连接 COM3, 115200, 8N1: 对端口“COM3”的访问被拒绝。
System.UnauthorizedAccessException: …(含堆栈)
```

- 每行 `yyyy-MM-dd HH:mm:ss.fff [来源] 消息`;消息截断到 500 字符 (`MaxMessageLength`),换行压成一行;有异常时其后附 `Exception.ToString()`。
- 每次运行的第一条之前写会话标题 (`==== 标题 ====`)。文件和目录在第一次写入时创建,程序正常运行时不产生文件。
- 超过 5 MB (`MaxFileBytes`) 时转存为 `error.1.txt` (覆盖上一次的转存),再从空文件开始。转存失败 (如 `error.1.txt` 被编辑器占用) 时照常追加。

### 12.5 CommunicationLogFiles:通讯数据文件

`ICommunicationRecorderFactory` 的实现。每次连接 `Create()` 一个记录,文件为日志目录中的 `capture_yyyyMMdd_HHmmss.txt`,永久保留,不自动清理。

```csharp
var recorders = new CommunicationLogFiles(errorLog);           // 可选第二个参数:目录 (Func<string>),测试用
var opened = recorders.Create();
if (!opened.IsSuccess) { ShowError(opened.Error); return; }   // 无法创建文件
var recorder = opened.Value;
recorder.Append(new[] { "10:00:00.000 TX {QG03}", "10:00:00.020 RX {QG03+:IMC300}" });   // 不阻塞,后台按顺序写入
// …
await recorder.CloseAsync();                                   // 写完已排队的行后关闭;之后的 Append 被忽略
```

- 保存的是数据显示区的内容 (显示的样子,含当时的显示格式),由界面层把每批显示的行交给 `Append`。
- 写入在后台按顺序进行,每批写完立即刷新。写入失败时记入 error.txt 并停止记录,不影响通讯。

### 12.6 TextFile

`TextFile.Load(path)` 读取并检查 `Texts.json`:必须是 `{ "键": { "zh": "…", "en": "…" } }`,每条都有 zh 和 en。失败的说明是原因本身 (英文或系统语言的异常消息),因为这时文本还没有读到。`TextFile.DefaultPath` 是程序旁边的 `Texts.json`。

## 13. 测试与替换实现

| 要替换的 | 测试中的实现 (`Zevision.Tests/Fakes`) |
|---|---|
| `IDeviceService` | `FakeDeviceService`:模拟设备的配置表,响应 `?`、组帮助和 Q/U/A/D;可以指定拒绝的写入和创建 (`RejectWrites`、`RejectCreates`)、追加条目的默认项 (`AppendTemplate`) |
| 传输 (`IDeviceTransport`) | `FakeSerialTransport`;或 LIBRARY.md §5.7 的 `SimulatedDevice`。用 `new ZevisionDeviceService(transport)` 或传工厂 |
| `IPreferencesStore` | `MemoryPreferencesStore` |
| `ICommunicationRecorderFactory` | `NoCommunicationRecorders` |
| `IRecipeStore` | `RecipeFileStore` + 临时目录 |
| `IErrorLog` | `FileErrorLog` + 临时目录,或自己的列表实现 |

- 测试程序集在 C# 9 模块初始化器 (`Zevision.Tests/TestSetup.cs`) 中读取 `Texts.json` 并初始化 `TextCatalog`。
- 未初始化 `Lang` 时为中文;测试英文用 `Lang.Override`。
- 纯函数 (`RestorePlanner`、`RecipeRules`、`RecipeDocument`…) 直接构造数据测试,不需要设备。`InstrumentSnapshot` 可以用 `HelpTableEntry` 列表构造。
- 不用真实设备也能走完整流程:`ZevisionDeviceService` + 模拟传输 (帧格式 `FrameOptions.Plain`) + 一个按 ID 读写的小表。响应 `?` 时按实机格式生成帮助表:先是 `\n`,每行 ID、值各占 30 列,再是说明,以 `\n` 结束,第一行为表头 `[CMD ID]`、`[VALUE]`、`[DESCRIPTION]`。

```csharp
var service = new ZevisionDeviceService(_ => new SimulatedDevice(table.Respond), errorLog);
await service.ConnectAsync(new ConnectionOptions(new SerialSettings("SIM", 115200), FrameOptions.Plain), ct);
var config = new InstrumentConfigService(service);
var snapshot = await config.ReadSnapshotAsync(ct);          // → IMC300 Ready
```

## 14. 常见修改

| 要做的 | 改哪里 | 同时检查 |
|---|---|---|
| 新增或修改提示文本 | `Localization/Texts.json` + `Texts.cs` (Core 或 Storage) | `TextCatalogTests` |
| 某项恢复时被设备拒绝 / 不应保存 | `RecipeRules` 的组白名单或 `Exclusions` | `RecipeRulesTests`、`InstrumentConfigServiceTests` (恢复计划和执行) |
| 新的引用关系 (值是另一个对象的编号) | `RecipeRules.References` (恢复计划也用) 或 `DeleteCheckReferences` (只用于删除检查);修改窗口的下拉框由指令集的引用类型决定 | 两个都要考虑:恢复计划会据此跳过写入 |
| 新的可新建对象或可增删的子列表 | `ObjectTypes` (以及 `ZevisionCommandSet` 允许该 ID 的 A/D 动作) | `ObjectTypesTests`、修改窗口的按钮 |
| 恢复的顺序 | `RecipeRules.GroupOrder` | 被引用的对象必须在前 |
| 配置文件新增字段 | `InstrumentRecipe` (`DataMember`) + `RecipeFile.ComputeMd5` + 提升 `CurrentFormatVersion`,并决定是否接受旧版本 | `RecipeFileTests` 的参考 MD5 |
| `settings.json` 新增字段 | `ConnectionPreferences` (`DataMember` + `SetDefaults`) | 旧文件缺字段时用默认值 |
| 换一种存储 (如数据库) | 实现 `IRecipeStore` / `IPreferencesStore`,只改 `App.OnStartup` | `LibraryBoundaryTests` |
| 新的连接方式 | 实现 `IDeviceTransport` (LIBRARY.md §5.7),在传给 `ZevisionDeviceService` 的工厂中选择 | `ZevisionDeviceServiceTests` |

## 15. 性能与内存

实机帮助表 (固件 1.8.11,116 KB),Release,.NET Framework 4.8:

| 操作 | 实测 |
|---|---|
| 解析帮助表 + `ToRecipe` + `RestorePlanner.Plan` | 约 6 ms (1525 行 → 940 项) |
| `RecipeFileStore.Save` (约 92 KB,含 MD5) | 约 12 ms |
| `RecipeFileStore.Load` (含 MD5 校验) | 约 3 ms |
| `InstrumentSnapshot` 常驻内存 | 约 320 KB |
| `InstrumentRecipe` (由快照生成) | 约 45 KB (字符串与快照共享) |

- 与设备通讯的时间远大于计算:完整的 `?` 约 10 秒,组帮助 0.5–5 秒。能用组帮助或批量 `Q` 时不要读完整的 `?` (修改配置的写入前检查就用一帧 `QG03;QC09` 代替 `?`)。
- 规则、对象类型表、文本表都是静态只读数据,首次使用时生成;正则表达式只编译一次。
- 快照、配置和恢复计划在操作结束后即可回收;编辑会话随修改窗口关闭而释放。
- `CommunicationLogWriter` 复制每批行后在后台写入,不占用 UI 线程。

## 16. API 参考

以下由程序集和 XML 文档生成,列出全部公开类型和成员。`T?`、可空注解没有显示,以说明为准。

### 16.1 Zevision.Core

#### interface `ICommunicationRecorder`
保存通讯数据:把数据显示区的内容按显示的样子追加保存。只是记录用途,写入失败不影响通讯。
- `string Path { get; }`: 保存位置 (显示给用户)。
- `void Append(IReadOnlyList<string> lines)`: 追加一批行 (一次通讯显示的全部行)。不阻塞调用方,不抛出 I/O 异常。
- `Task CloseAsync()`: 写完已排队的行后关闭。之后的 `Append` 被忽略。

#### interface `ICommunicationRecorderFactory`
为一次连接创建 `ICommunicationRecorder`。
- `Result<ICommunicationRecorder> Create()`: 创建新的记录 (如以当前时间命名的文件)。无法创建时返回失败,原因可直接显示。

#### interface `IErrorLog`
运行时错误日志。实现必须线程安全,且写入失败时不能抛出异常。
- `void Write(string source, string message, Exception exception = null)`: 记录一条错误。

#### struct `Result`
不带返回值的操作结果。用于预期内的失败(端口被占用、设备返回错误、超时等)。
- `bool IsSuccess { get; }`: 是否成功。
- `string Error { get; }`: 失败原因;成功时为 null。
- `static Result Ok()`: 创建成功结果。
- `static Result Fail(string error)`: 创建失败结果。

#### struct `Result<T>`
带返回值的操作结果。
- `bool IsSuccess { get; }`: 是否成功。
- `string Error { get; }`: 失败原因;成功时为 null。
- `T Value { get; }`: 返回值。失败时访问会抛出 `InvalidOperationException`。
- `static Result<T> Ok(T value)`: 创建成功结果。
- `static Result<T> Fail(string error)`: 创建失败结果。

#### static class `CrucibleRule`
膜系的坩埚号 (`F**0902`):0 = 任意 (Any),1 ~ 膜系所选输出源 (`F**0901`) 的坩埚数量 (`S**07`)。 输出源未选择、不存在,或其坩埚数量未知或为 0 时,最大坩埚号按 `DefaultCount` 计。
- `const string Any`: "任意坩埚"的值。
- `const int DefaultCount`: 输出源的坩埚数量未知或为 0 时的最大坩埚号。
- `static bool IsCrucible(string id)`: 是否是膜系的坩埚号项,如 `F010902`。
- `static int MaxFor(string crucibleId, Func<string, string> valueOf)`: 坩埚号的最大值:膜系所选输出源的坩埚数量;未知或为 0 时为 `DefaultCount`。
- `static string Check(string crucibleId, string value, Func<string, string> valueOf)`: 检查坩埚号是否超出所选输出源的坩埚数量。只在坩埚数量已知时检查;0 (任意) 和非数字的值不检查。

#### static class `DeviceIds`
配置项 ID 的组成:组字母 + 若干级两位十六进制编号 (如 `F0103` = 膜系 F01 的预熔列表)。 列表条目编号为 01~FD;`00` 是"在末尾追加/删除最后一项"的指令项,`FE` 是条数项。 程序中拼接、拆分 ID 都用这里的方法,不手写这些编号。
- `static int LastNumber(string id)`: ID 末尾的两位十六进制编号,如 `M0A` → 10。
- `static string Item(string listId, int number)`: 列表的第 `number` 项,如 `F0103` 的第 2 项为 `F010302`。
- `static string CountOf(string listId)`: 列表的条数项,如 `F0103FE`;组的对象数如 `MFE`。
- `static string AppendSlotOf(string listId)`: 列表的追加/删除项,如 `F010300` (`A` 在末尾追加,`D` 删除最后一项)。
- `static string NameOf(string objectId)`: 对象的名称项 (第 01 项),如材料 `M0301`。
- `static bool IsItemNumber(string number)`: 两位编号是否是列表条目,而不是 `00` (追加/删除) 或 `FE` (条数)。
- `static bool IsCount(string id)`: 是否是条数项 (以 `FE` 结尾)。
- `static bool TryParseCount(string text, out int count)`: 解析设备返回的条数 (十进制非负整数)。
- `static bool IsWellFormed(string id)`: ID 格式是否正确:大写组字母后跟若干两位十六进制 (与指令集生成命令时检查的格式相同)。

#### interface `IInstrumentConfigReader`
读取设备配置 (只发送 `?` 和 `Q`,不修改设备)。
- `Task<Result<InstrumentSnapshot>> ReadSnapshotAsync(CancellationToken cancellationToken)`: 发送 `?` 读取设备全部设置项的当前值。响应约 116 KB,115200 bps 下约需 10 秒。
- `Task<Result<IReadOnlyList<RecipeEntry>>> ReadGroupAsync(char group, CancellationToken cancellationToken)`: 用组帮助 (如 `?M`) 读取一个组的当前配置,按 `RecipeRules` 挑出可恢复的项 (与 `ToRecipe` 相同)。 比完整的 `?` 快得多 (材料约 0.5 秒)。响应中没有该组的数量项 (如 `MFE`) 时视为解析失败。
- `Task<Result<IReadOnlyList<QueryValue>>> QueryAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken)`: 用 Q 指令查询多项的当前值。每 `QueryBatchSize` 条合并为一帧 (`QG01;QG02;…`),比逐条查询快得多。设备拒绝某一项 (如 `-A` 不存在) 时该项带错误说明,不影响其他项; 通讯失败时返回失败。

#### interface `IInstrumentConfigService` : IInstrumentConfigReader, IInstrumentConfigWriter
保存和恢复仪器配置:读取和修改设备。生成配置文件内容 (`ToRecipe`) 和 恢复计划 (`Plan`) 不与设备通讯,不在这里。

#### interface `IInstrumentConfigWriter`
修改设备配置 (恢复、写入编辑的项、新建/删除对象、列表追加)。修改前都先确认设备型号与空闲状态。
- `Task<RestoreReport> ExecuteRestoreAsync(InstrumentRecipe recipe, RestorePlan plan, IProgress<RestoreProgress> progress, CancellationToken cancellationToken)`: 执行恢复,顺序为:系统设置 (传感器、源等) → 创建缺失对象并重新读取、重新计划 → 材料 → 膜层 → 层 → 工艺 的设置 → 逐项读回校验。遇到第一个错误即停止。
- `Task<RestoreReport> ApplyEditsAsync(InstrumentRecipe recipe, IReadOnlyList<RecipeEntry> edits, IProgress<RestoreProgress> progress, CancellationToken cancellationToken)`: 把编辑过的配置项写入设备 (修改配置功能):先逐项检查可写性和字符,再用 Q 指令确认设备型号 (G03) 与 空闲状态 (C09),然后按恢复顺序写入并逐项读回校验。遇到第一个错误即停止,不回滚。 不创建对象:要修改的项在设备上不存在时,设备会拒绝。
- `Task<ObjectCreation> CreateObjectAsync(ObjectType type, string objectId, string defaultValue, string deviceName, CancellationToken cancellationToken)`: 在设备上按编号新建对象 (如 `AF02`),`defaultValue` 不为 null 时再按 `NewDefault` 设置默认引用 (如 `UF0202:1`,或先在列表末尾追加一项),写入后读回校验。 只在开始时确认一次设备型号 (G03) 与空闲状态 (C09)。结果由调用方重新读取该组来确认。
- `Task<Result> DeleteObjectAsync(string objectId, string deviceName, CancellationToken cancellationToken)`: 在设备上按编号删除对象 (如 `DM03`)。先确认设备型号与空闲状态。结果由调用方重新读取该组来确认。
- `Task<Result<string>> AppendListItemAsync(string listId, string value, string deviceName, CancellationToken cancellationToken)`: 在列表末尾追加一项 (如工艺增加膜层:`AP020107 00`,读数量 `QP020107FE` 得到新条目 `P0201070n`); `value` 不为 null 时再 `UP0201070n:值` 并读回校验 (膜系的预熔/后处理一项有多个设置,追加后使用设备默认值,传 null)。 先确认设备型号与空闲状态。
- `Task<Result<string>> RemoveLastListItemAsync(string listId, string deviceName, CancellationToken cancellationToken)`: 删除列表的最后一项 (如膜系的最后一条预熔:`DF010300`)。设备只支持从末尾删除。 先确认设备型号与空闲状态,删除前后读取数量 (`QF0103FE`) 确认少了一条。

#### class `InstrumentConfigReader` : IInstrumentConfigReader
`IInstrumentConfigReader` 的实现,也是修改配置 (`InstrumentConfigService`) 时与设备单条通讯的基础: 每条指令只取第一条响应,通讯失败、没有响应、设备拒绝都作为失败的 `Result` 返回,原因可直接显示。
- `static readonly TimeSpan SlowReplyTimeout`: 读取 `?` 和批量查询的最低空闲超时。实机 (固件 1.8.11) 抓包:`?` 发出后约 380 ms 才收到第一个字节, 每个 57000 字节的包之间停顿约 350 ms。用户把响应超时设为 500 ms 时余量不足,因此这两类请求至少等 2 秒; 连接设置更长时用连接设置。批量查询一帧 20 条,处理时间尚未实测,同样按慢响应处理。
- `const int QueryBatchSize`: 批量查询时每帧的指令数。响应很短,20 条约 600 字节。
- `Task<Result<InstrumentSnapshot>> ReadSnapshotAsync(CancellationToken cancellationToken)`: 同接口/基类的说明。
- `Task<Result<IReadOnlyList<RecipeEntry>>> ReadGroupAsync(char group, CancellationToken cancellationToken)`: 同接口/基类的说明。
- `Task<Result<IReadOnlyList<QueryValue>>> QueryAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken)`: 同接口/基类的说明。

#### class `InstrumentConfigService` : InstrumentConfigReader, IInstrumentConfigReader, IInstrumentConfigService, IInstrumentConfigWriter
`IInstrumentConfigService` 的实现:修改设备 (创建对象、写入、校验);读取由 `InstrumentConfigReader` 完成, 恢复计划的计算见 `RestorePlanner`。
- `InstrumentConfigService(IDeviceService device)`: 创建服务。
- `Task<ObjectCreation> CreateObjectAsync(ObjectType type, string objectId, string defaultValue, string deviceName, CancellationToken cancellationToken)`: 同接口/基类的说明。
- `Task<Result> DeleteObjectAsync(string objectId, string deviceName, CancellationToken cancellationToken)`: 同接口/基类的说明。
- `Task<Result<string>> RemoveLastListItemAsync(string listId, string deviceName, CancellationToken cancellationToken)`: 同接口/基类的说明。
- `Task<Result<string>> AppendListItemAsync(string listId, string value, string deviceName, CancellationToken cancellationToken)`: 同接口/基类的说明。
- `Task<RestoreReport> ExecuteRestoreAsync(InstrumentRecipe recipe, RestorePlan plan, IProgress<RestoreProgress> progress, CancellationToken cancellationToken)`: 同接口/基类的说明。
- `Task<RestoreReport> ApplyEditsAsync(InstrumentRecipe recipe, IReadOnlyList<RecipeEntry> edits, IProgress<RestoreProgress> progress, CancellationToken cancellationToken)`: 同接口/基类的说明。

#### class `InstrumentRecipe`
仪器配置文件 (recipe) 的内容。以 JSON 保存,字段名固定,修改结构时必须提升 `FormatVersion`。
- `InstrumentRecipe()`: 默认构造函数。
- `const int CurrentFormatVersion`: 当前程序写出和能读取的格式版本。 版本 2 增加了 `Md5`;版本 1 的文件没有校验值,无法确认未被修改,不再接受。
- `int FormatVersion { get; set; }`: 文件格式版本。
- `string DeviceName { get; set; }`: 设备名称 (G03),如 `IMC300`。
- `string ModelNumber { get; set; }`: 型号 (G05)。
- `string FirmwareVersion { get; set; }`: 固件版本 (G08)。
- `string SerialNumber { get; set; }`: 序列号 (G06)。
- `string SavedAt { get; set; }`: 保存时间,ISO 8601 (本地时间带时区)。
- `string Md5 { get; set; }`: 除本字段外所有数据的 MD5 (32 位小写十六进制),保存时计算,恢复前校验。 计算方法见存储层的 `RecipeFile.ComputeMd5`。
- `List<RecipeEntry> Entries { get; set; }`: 配置项。

#### class `InstrumentSnapshot`
通过 `?` 读取的设备配置快照。
- `InstrumentSnapshot(IReadOnlyList<HelpTableEntry> entries)`: 创建快照。ID 重复时以第一次出现为准。
- `const string DeviceNameId`: 设备名 (型号) 的 ID。
- `const string StateId`: 当前状态的 ID (工艺状态)。
- `const string ReadyState`: 空闲状态的值:只有在这个状态下才修改设置。
- `IReadOnlyList<HelpTableEntry> Entries { get; }`: 帮助表中的全部条目,保持设备给出的顺序。
- `string DeviceName { get; }`: 设备名称 (G03)。
- `string ModelNumber { get; }`: 型号 (G05)。
- `string FirmwareVersion { get; }`: 固件版本 (G08)。
- `string SerialNumber { get; }`: 序列号 (G06)。
- `string CurrentState { get; }`: 当前运行状态 (C09),空闲时为 `Ready`。
- `InstrumentRecipe ToRecipe(DateTimeOffset savedAt)`: 生成配置文件的内容:设备信息,以及按 `SelectRestorable` 挑出的可恢复项。
- `string GetValue(string id)`: 按 ID 取当前值;设备上没有该项时返回 null。
- `string GetDescription(string id)`: 按 ID 取说明;设备上没有该项时返回 null。

#### interface `IRecipeStore`
仪器配置文件 (recipe) 的读写。文件格式、MD5 校验和安全覆盖由实现负责 (数据存储层)。
- `string Folder { get; }`: 配置文件的默认目录 (文件对话框的初始目录)。
- `string FileExtension { get; }`: 配置文件的扩展名,如 `.json`。
- `Result Save(InstrumentRecipe recipe, string path)`: 计算并写入 `Md5` 后保存到文件 (覆盖)。失败时原因可直接显示。
- `Result<InstrumentRecipe> Load(string path)`: 读取并校验文件 (格式版本、MD5、字段、ID)。不合格时返回失败,原因可直接显示。

#### record `ObjectCreation`
新建对象的结果。
- `ObjectCreation(string Error, string DefaultsError)`: 新建对象的结果。
- `string Error { get; init; }`: 新建失败的原因;成功时为 null。
- `string DefaultsError { get; init; }`: 对象已新建,但默认引用没有设置成功的原因;成功或不需要设置时为 null。

#### record `ObjectDefault`
新建对象后程序设置的默认引用:把被引用组中编号最小的对象写入新对象的一项。
- `ObjectDefault(char ReferencedGroup, string ListSuffix, string SettingSuffix)`: 新建对象后程序设置的默认引用:把被引用组中编号最小的对象写入新对象的一项。
- `char ReferencedGroup { get; init; }`: 被引用的组,如膜系引用材料 `M`。
- `string ListSuffix { get; init; }`: 先在新对象的这个列表末尾追加一项 (如膜层的膜系列表 `02`);为 null 时直接写对象自己的项。
- `string SettingSuffix { get; init; }`: 写入的项,相对于新对象或追加的条目 (如膜系的材料 `02`);为空时写追加的条目本身。

#### record `ObjectList`
对象的一个子列表,如膜系的预熔 (`F**03`)。
- `ObjectList(string Suffix, ObjectListStyle Style)`: 对象的一个子列表,如膜系的预熔 (`F**03`)。
- `string Suffix { get; init; }`: 列表在对象中的编号,如 `03`。
- `ObjectListStyle Style { get; init; }`: 修改窗口中的操作方式。

#### enum `ObjectListStyle` : IComparable, IFormattable, IConvertible
对象的子列表在修改窗口中的操作方式。
- `HeaderButtons` = 0: 分组标题中的"+ / −"按钮:在末尾追加一项 (设备默认值)、删除最后一项。
- `HeaderButtonsWithEmptyRow` = 1: 同 `HeaderButtons`;列表为空时在分组中显示说明和添加按钮。
- `ItemRows` = 2: 每一项有删除按钮 (删除中间项时后面的项前移),标题中有选择被引用对象的"添加"行。

#### class `ObjectType`
可以按编号新建/删除的对象类型:材料、膜系、膜层、工艺。层级 (用户确认):工艺包含膜层,膜层包含膜系,膜系包含材料。
- `char Group { get; }`: 组字母,如 `M`。
- `string Prefix { get; }`: 对象 ID 的前缀:组字母;工艺为 `P02` (`P01` 是活动工艺,不是对象)。
- `ObjectDefault NewDefault { get; }`: 新建后设置的默认引用;没有时为 null。
- `IReadOnlyList<ObjectList> Lists { get; }`: 修改窗口中可以增删的子列表。
- `string Kind { get; }`: 名称 (界面语言),如"材料" / "Material"。
- `string ListPattern(ObjectList list)`: 子列表在指令集中的模式,如 `F**03` (用于取列表名称和条目的定义)。
- `string NextFreeId(ISet<string> used)`: 第一个未使用的编号 (1~`MaxIndex`),如 `M02`、`P0203`;用完时为 null。
- `bool TryGetObjectId(string id, out string objectId)`: 配置项所属的对象,如 `M0301` → `M03`。编号 00 (增删指令) 和 FE (数量) 不是对象。
- `string ToString()`: 同接口/基类的说明。

#### static class `ObjectTypes`
对象类型表:哪些组可以新建/删除、对象 ID 的前缀、可增删的子列表和新建后的默认引用。 修改窗口、恢复计划和引用检查都按这张表处理对象,不各自判断组字母。
- `static readonly ObjectType Material`: 材料:`M**`。
- `static readonly ObjectType Film`: 膜系:`F**`,材料 ID (`F**02`) 默认为第一个材料;预熔、后处理可以增删。
- `static readonly ObjectType Layer`: 膜层:`L**`,膜系列表 (`L**02`) 默认添加一项,其膜系为第一个膜系。 膜层的速率和最终厚度属于膜系列表中的每一项,新膜层的列表为空 (实机 `L02FE = 0`),所以空列表要提示。
- `static readonly ObjectType Process`: 工艺:`P02**`,膜层列表 (`P02**07`) 默认添加第一个膜层,可以添加任意膜层、删除任意一项。
- `static IReadOnlyList<ObjectType> All { get; }`: 全部类型,按被引用的在前排列 (材料 → 膜系 → 膜层 → 工艺)。
- `static ObjectType Find(char group)`: 组字母对应的类型;不能新建/删除的组为 null。
- `static bool TryGetObject(string id, out ObjectType type, out string objectId)`: 配置项所属的对象,如 `F01030101` → 膜系 `F01`。
- `static ObjectType OfObjectId(string objectId)`: 对象 ID (如 `M03`、`P0201`) 的类型;不是对象 ID 时 (如 `P01`) 为 null。
- `static string GroupKey(string id)`: 配置项在修改窗口中所属的分组:对象 ID (如 `F01030101` → `F01`,`P020101` → `P0201`), 其他组为组字母 + 第一级编号 (如 `G0E02` → `G0E`)。

#### static class `ParameterValue`
按 `CommandDefinition` 校验、规整和比较配置值。数字一律按 InvariantCulture (小数点为 `.`)。
- `const int MaxHours`: 时间的最大小时数 (协议文档:小时最大 8760)。
- `static string Validate(CommandDefinition definition, string value)`: 校验值。先检查协议字符 (`ValidateValue`),再按类型检查格式和范围; `definition` 为 null (文档未列出的项) 时只检查协议字符。 引用类型的选项由调用方的下拉框保证,这里只检查是非负整数。
- `static string Normalize(CommandDefinition definition, string value, string original)`: 写入前规整值:去除首尾空白;数字按 `original` 的小数位数格式化 (设备按固定位数保存, 如 `5.89` → `5.890`,读回校验才能一致);时间格式化为设备的 `H:MM:SS`。 值不合法时只去除空白。
- `static bool AreEquivalent(string expected, string actual)`: 读回的值是否与写入的值一致:字符串相同,或者都是数字且数值相等 (`5.890` = `5.89`), 或者都是时间且秒数相等 (`0:00:05` = `00:00:05`)。
- `static bool TryParseTime(string text, out int totalSeconds)`: 解析时间 `H:MM:SS` 或 `M:SS` (小时为 0 时可省略)。小时 0~8760,分、秒 0~59。
- `static string FormatTime(int totalSeconds)`: 按设备格式显示时间,如 `0:00:05`。

#### record `QueryValue`
批量查询中一项的结果。
- `QueryValue(string Id, string Value, string Error)`: 批量查询中一项的结果。
- `string Id { get; init; }`: 命令 ID。
- `string Value { get; init; }`: 当前值;查询失败时为 null。
- `string Error { get; init; }`: 设备拒绝的原因;成功时为 null。

#### class `RecipeDocument`
修改窗口中正在编辑的配置 (`InstrumentRecipe`) 及对它的操作:查找对象和列表、替换重新读取的组、 加入或删除对象的项。不与设备通讯,也不读写文件。
- `RecipeDocument(InstrumentRecipe recipe)`: 创建文档。
- `InstrumentRecipe Recipe { get; }`: 配置。
- `List<RecipeEntry> Entries { get; }`: 全部配置项。
- `string ObjectName(string objectId)`: 对象的名称 (第 01 项,如材料 `M0301`);没有时为 null。
- `HashSet<string> ObjectIds(char group)`: 配置中该组现有的对象 ID,如 `M01`、`P0201`。
- `static HashSet<string> ObjectIdsIn(IEnumerable<RecipeEntry> entries, char group)`: 配置项中该组的对象 ID。
- `int ListItemCount(string listId)`: 列表的条数,如 `F0103` 的预熔数 (不含 00 增删项和 FE 数量项)。
- `void ReplaceGroup(char group, IReadOnlyList<RecipeEntry> entries)`: 用重新读取的数据替换该组的全部项 (保持在原来的位置)。
- `void AddObject(string objectId, IEnumerable<RecipeEntry> source)`: 从 `source` (如重新读取的组) 复制对象的全部项,放在同组之后。
- `void RemoveObject(string objectId)`: 删除对象的全部项。
- `void AddListItem(string listId, string itemId, IEnumerable<RecipeEntry> source)`: 从 `source` 复制以 `itemId` 开头的项 (如新增的一条预熔),放在列表 `listId` 之后。
- `void RemoveListItem(string itemId)`: 删除列表条目 (如 `F010302`) 及其子项。
- `void SetValues(IReadOnlyDictionary<string, string> values)`: 修改项的值 (按 ID)。
- `RecipeDocument Clone()`: 复制整份配置 (含全部项),修改副本不影响本文档。
- `static ValueTuple<IReadOnlyList<string>, IReadOnlyList<string>> CompareObjectChange(CommandActions action, string objectId, ISet<string> before, ISet<string> after)`: 核对新建/删除后的对象集合:新建的对象出现、删除的对象消失,且没有其他对象变化。

#### class `RecipeEditSession`
修改配置窗口的一次编辑:正在编辑的配置 (`Document`) 与设备 (以及配置文件) 之间的同步。 设备操作都用配置所属的设备型号 (`DeviceName`) 先确认设备。
- `RecipeDocument Document { get; }`: 正在编辑的配置。
- `string FilePath { get; }`: 配置文件路径;实机数据时为 null。
- `IInstrumentConfigReader Reader { get; }`: 读取设备 (供"信息"页查询)。
- `static RecipeEditSession Open(InstrumentRecipe recipe, string path, IInstrumentConfigService config, IRecipeStore recipes)`: 打开编辑。
- `string DefaultReferenceId(ObjectType type)`: 新对象的默认引用 (`NewDefault`):配置中被引用组里编号最小的对象,如 `M01`; 没有默认引用或被引用的组中没有对象时为 null。
- `Task<Result<IReadOnlyList<RecipeEntry>>> ReadDeviceGroupAsync(char group, CancellationToken cancellationToken)`: 新建/删除前设备上该组的项 (用来确定设备上有哪些对象)。实机数据:页面就是设备数据。
- `ISet<string> UsedObjectIds(char group, ISet<string> onDevice)`: 新建对象时要避开的编号。
- `Task<ObjectCreation> CreateObjectAsync(ObjectType type, string objectId, CancellationToken cancellationToken)`: 在设备上按编号新建对象,并设置默认引用 (`DefaultReferenceId`,值为十进制编号)。
- `Task<Result> DeleteObjectAsync(string objectId, CancellationToken cancellationToken)`: 在设备上按编号删除对象。
- `Task<Result<string>> AppendListItemAsync(string listId, string value, CancellationToken cancellationToken)`: 在设备的列表末尾追加一项,`value` 不为 null 时写入新条目。
- `Task<Result<string>> RemoveLastListItemAsync(string listId, CancellationToken cancellationToken)`: 删除设备上列表的最后一项。
- `Task<RestoreReport> ApplyEditsAsync(IReadOnlyList<RecipeEntry> edits, IProgress<RestoreProgress> progress, CancellationToken cancellationToken)`: 把修改的项写入设备并读回校验 (见 `ApplyEditsAsync`)。
- `Task<Result<IReadOnlyList<RecipeEntry>>> RereadGroupAsync(char group, CancellationToken cancellationToken)`: 设备修改之后重新读取所属的组 (无论之前的指令是否成功,设备状态以读取为准)。 实机数据用它替换该组;保存的配置不变 (页面是文件内容)。
- `string ApplyObjectChange(CommandActions action, string objectId, IReadOnlyList<RecipeEntry> deviceGroup)`: 设备上新建/删除对象成功后更新配置 (实机数据已在 `RereadGroupAsync` 中更新)。
- `Task<string> CheckListCountAsync(string listId, CancellationToken cancellationToken)`: 在设备的列表末尾追加或删除之前的检查。
- `string ApplyListAppend(string listId, string itemId, IReadOnlyList<RecipeEntry> deviceGroup)`: 设备上追加一条 (`itemId`) 成功后更新配置;`deviceGroup` 为重新读取的组。
- `string ApplyListRemove(string removedItemId, IReadOnlyDictionary<string, string> shifted)`: 设备上删除了列表的最后一项 (`removedItemId`) 后更新配置; `shifted` 是删除中间一项时前移写入的值 (没有时为空)。
- `string Commit(Action<RecipeDocument> change)`: 修改配置。保存的配置先在副本上修改并保存文件,成功后才修改本文档,因此保存失败时页面仍与文件一致。

#### class `RecipeEntry`
一个配置项。
- `RecipeEntry()`: 默认构造函数。
- `string Id { get; set; }`: 命令 ID (不含动作字母),如 `F0101`。
- `string Value { get; set; }`: 值,原样保留设备给出的文本,不做数值转换以免受区域设置影响。
- `string Description { get; set; }`: 说明,仅供阅读。

#### static class `RecipeRules`
哪些配置项可以保存到 recipe 并恢复到设备。所有规则集中在这里维护。
- `static bool IsSystemSetting(string id)`: 是否是系统设置 (传感器、源、输入输出、逻辑、系统偏好)。系统设置不需要创建对象,恢复时最先写入。
- `static bool IsRestorable(string id, string value, out string reason)`: 判断帮助表中的一项是否保存到 recipe。
- `static List<RecipeEntry> SelectRestorable(IEnumerable<HelpTableEntry> entries)`: 从帮助表中挑出保存到 recipe 的项 (`IsRestorable`);ID 重复时只取第一次出现的。
- `static IReadOnlyList<RecipeEntry> FindReferencesTo(IEnumerable<RecipeEntry> entries, string objectId)`: 找出引用了 `objectId` 的设置项 (删除前检查)。层级关系:工艺包含膜层,膜层包含膜系,膜系包含材料, 即膜系的材料 (F??02)、膜层中的膜系 (L??02??01)、工艺中的膜层 (P02??07??),以及活动工艺 (P01)、链接工艺 (P02??0A) 和输出源的坩埚材料 (S??09??)。值为 0 (未选择) 的不算。
- `static bool TryGetReference(string id, string value, out string targetId, out string kind)`: 设置项的值是否引用了另一个对象,如 `F0302 = 3` → `M03`。
- `static int GroupRank(string id)`: 恢复时的排序键:先按组顺序,组内保持设备帮助表中的原始顺序 (由调用方稳定排序保证)。

#### record `RestoreCreate`
恢复时需要重新创建的列表对象或子列表条目。
- `RestoreCreate(string Id, string Kind, string Command)`: 恢复时需要重新创建的列表对象或子列表条目。
- `string Id { get; init; }`: 创建后应出现的对象 ID,如 `M03` 或 `F010302`。
- `string Kind { get; init; }`: 对象类型,如"材料"。
- `string Command { get; init; }`: 创建指令,如 `AM03` (指定编号) 或 `AF010300` (追加到子列表末尾)。

#### record `RestorePlan`
恢复计划:只包含与设备当前值不同的项,按依赖顺序排列。生成计划不会与设备通讯。
- `RestorePlan(IReadOnlyList<RestoreCreate> Creates, IReadOnlyList<RestoreWrite> Writes, int UnchangedCount, IReadOnlyList<RestoreSkip> Skipped, IReadOnlyList<string> Warnings)`: 恢复计划:只包含与设备当前值不同的项,按依赖顺序排列。生成计划不会与设备通讯。
- `IReadOnlyList<RestoreCreate> Creates { get; init; }`: 文件中有、设备上已被删除的对象,恢复时先按依赖顺序创建。 它们的设置项要等创建后重新读取设备才能确定写入哪些,因此不在 `Writes` 中。
- `IReadOnlyList<RestoreWrite> Writes { get; init; }`: 需要写入的项。
- `int UnchangedCount { get; init; }`: 与设备当前值相同、无需写入的项数。
- `IReadOnlyList<RestoreSkip> Skipped { get; init; }`: 跳过的项。
- `IReadOnlyList<string> Warnings { get; init; }`: 需要用户注意但不阻止恢复的问题,如固件版本不同。

#### static class `RestorePlanner`
恢复计划:对比配置文件与设备快照,算出需要创建的对象/子列表条目、需要写入的项和跳过的项。 纯计算,不与设备通讯,便于测试。执行见 `InstrumentConfigService`。
- `static Result<RestorePlan> Plan(InstrumentRecipe recipe, InstrumentSnapshot snapshot)`: 生成恢复计划。设备型号不符或设备不在空闲状态时返回失败。

#### record `RestoreProgress`
恢复进度。
- `RestoreProgress(string Stage, int Completed, int Total)`: 恢复进度。
- `string Stage { get; init; }`: 阶段,如"写入"、"校验"。
- `int Completed { get; init; }`: 已完成数。
- `int Total { get; init; }`: 总数;为 0 表示进度未知。

#### class `RestoreReport`
恢复结果。
- `RestoreReport()`: 默认构造函数。
- `int CreatedCount { get; }`: 成功创建的对象数。
- `int WrittenCount { get; }`: 成功写入的项数。
- `int PlannedWriteCount { get; }`: 计划写入的项数 (创建对象后重新计划的结果)。
- `string FailedId { get; }`: 创建或写入失败而停止时的对象/项 ID。
- `string Error { get; }`: 失败原因。
- `bool Cancelled { get; }`: 是否被用户取消。
- `bool FailedDuringCreate { get; }`: 失败发生在创建对象 (及其后的重新读取、重新计划) 阶段,而不是写入阶段。
- `List<string> VerifyMismatches { get; }`: 读回校验时与期望值不一致的项 (描述文本)。
- `List<RestoreSkip> AdditionalSkipped { get; }`: 创建对象后重新计划时跳过的项 (例如新对象的子列表条目数与文件不同)。
- `bool IsSuccess { get; }`: 全部写入成功且校验一致。

#### class `RestoreSkip`
文件中被跳过 (无法写入) 的一项、它在文件中的值及原因。
- `RestoreSkip(RecipeEntry entry, string reason)`: 由文件中的配置项创建跳过项。
- `string Id { get; }`: 命令 ID。
- `string Value { get; }`: 文件中的值 (未能写入设备的数据)。
- `string Description { get; }`: 说明。
- `string Reason { get; }`: 原因。
- `string ToString()`: 单行显示,如 `F01030301 Start Ramp/Rise time = 0:00:30 (原因)`。

#### record `RestoreWrite`
恢复计划中的一次写入。
- `RestoreWrite(string Id, string Value, string CurrentValue, string Description)`: 恢复计划中的一次写入。
- `string Id { get; init; }`: 命令 ID。
- `string Value { get; init; }`: 要写入的值 (来自文件)。
- `string CurrentValue { get; init; }`: 设备当前值。
- `string Description { get; init; }`: 说明。
- `string Command { get; }`: 写入指令,如 `UF0101:L1` (由 `Update` 生成)。

#### static class `DeviceServiceExtensions`
`IDeviceService` 的便捷方法。
- `static Task<Result<ZevisionReply>> SendAsync(IDeviceService device, string command, CancellationToken cancellationToken)`: 发送命令并等待响应,使用连接设置的响应超时。

#### static class `FramingCheck`
判断收到的帧是否说明设备的帧格式 (长度字节) 与程序的设置不同,通讯出错时据此提示检查设置。
- `static FramingMismatch Check(ZevisionFrame frame, FrameOptions options)`: 检查收到的帧。
- `static ValueTuple<string, string> SettingIds(ConnectionEndpoint endpoint)`: 帧格式设置项的 ID:TCP 为 G1407 (校验和)、G1408 (长度),RS232 为 G1304、G1305。

#### enum `FramingMismatch` : IComparable, IFormattable, IConvertible
收到的帧与程序的帧格式设置 (长度字节) 不一致的情况。
- `None` = 0: 一致,或无法判断。
- `ReplyHasLength` = 1: 程序未启用长度,设备的响应带长度字节。
- `ReplyWithoutLength` = 2: 程序启用了长度,设备的响应不带长度字节。

#### interface `IDeviceService` : IDisposable
与 IMC300 设备通讯的服务:连接管理、发送命令并等待响应。失败说明按界面语言给出。
- `bool IsConnected { get; }`: 是否已连接。
- `event EventHandler<FrameSentEventArgs> FrameSent`: 帧发送前触发。
- `event EventHandler<FrameReceivedEventArgs> FrameReceived`: 收到一帧 (含错误帧和帧外数据) 时触发。
- `event EventHandler<ConnectionLostEventArgs> ConnectionLost`: 连接因传输错误 (串口拔出、TCP 断开等) 意外中断时触发。主动断开不触发。
- `Task<Result> ConnectAsync(ConnectionOptions options, CancellationToken cancellationToken)`: 打开连接 (串口或 TCP) 并开始接收。连接参数 (连接目标、帧格式、响应超时) 在连接期间不变。
- `Task DisconnectAsync()`: 关闭连接。未连接时直接返回。
- `Task<Result<ZevisionReply>> SendAsync(string command, TimeSpan minimumResponseTimeout, CancellationToken cancellationToken)`: 发送命令并等待响应。同一时间只有一个请求在等待响应,并发调用会排队。 响应超时 (空闲超时) 至少为 `minimumResponseTimeout`,连接设置的超时更长时用连接设置; 用于已知响应前或分包之间会停顿的指令,如 `?`。其他指令传 `Zero`。
- `Task<Result<ZevisionReply>> SendHiddenAsync(string command, TimeSpan minimumResponseTimeout, CancellationToken cancellationToken)`: 与 `SendAsync` 相同,但不在数据显示区显示 (因此也不保存到通讯数据文件、不计数): 不触发 `FrameSent`;这次的响应、超时时报告的不完整数据,以及之后收到的回显相同的帧 (如取消后设备仍在发送的剩余数据) 都不触发 `FrameReceived`,直到再次用 `SendAsync` 发送同一指令。 用于程序内部读取设备配置的帮助指令 (`?`、`?M`):它们只在指令输入框中发送时显示。

#### static class `ManualCommandPolicy`
手动输入指令的整理。帮助指令 `?` (含 `?M` 等分组帮助) 允许手动发送,用于调试。
- `static string ToUpperCommands(string input)`: 把每条指令的命令部分 (第一个 `:` 之前) 转为大写,数据部分 (`:` 之后,到分隔符 `;`、`；` 或换行为止) 保持原样。 设备只认大写命令 (小写回复 `-A`);数据可能是名称等文本,不能改动。分隔符和换行原样保留。
- `static IReadOnlyList<string> Split(string input)`: 把输入拆成单条指令:按 `;` 和换行分隔 (多行输入框中每行一条;中文输入法的全角 `；` 也视为分隔符), 去掉每条首尾空白,丢弃空段。多条指令合并发送时用 `;` 重新连接。
- `static bool ContainsHelpCommand(string command)`: 指令 (含用 `;` 分隔的多条) 中是否有帮助指令 `?`;有时发送使用较长的最低响应超时。
- `static bool ContainsFullHelpCommand(string command)`: 指令中是否有完整的帮助指令 `?` (不是 `?M` 这样的分组帮助)。完整帮助约 116 KB、10 秒,发送前提示用户。
- `static TimeSpan MinimumResponseTimeout(string command)`: 发送时的最低响应超时:帮助指令的响应大、首字节前和分包之间有停顿,与保存配置读取 `?` 时一样用 `SlowReplyTimeout`;其他指令为 `Zero` (用连接设置的超时)。

#### class `ZevisionDeviceService` : IDeviceService, IDisposable
`IDeviceService` 的实现:每次连接按当时的通讯参数创建一个 `ZevisionClient` (通讯库), 转发它的事件,把它的英文结果转换为界面语言的说明 (`CommunicationText`), 并把通讯库交出的意外异常 (读取循环出错、事件处理程序出错) 写入错误日志。
- `ZevisionDeviceService(Func<ConnectionEndpoint, IDeviceTransport> transportFactory, IErrorLog errorLog = null)`: 创建服务。每次连接时用 `transportFactory` 按连接目标 (串口或 TCP) 创建传输, 断开时关闭。
- `ZevisionDeviceService(IDeviceTransport transport, IErrorLog errorLog = null)`: 用固定的传输创建服务 (每次连接都使用同一个实例,用于测试)。
- `bool IsConnected { get; }`: 同接口/基类的说明。
- `event EventHandler<FrameSentEventArgs> FrameSent`: 同接口/基类的说明。
- `event EventHandler<FrameReceivedEventArgs> FrameReceived`: 同接口/基类的说明。
- `event EventHandler<ConnectionLostEventArgs> ConnectionLost`: 同接口/基类的说明。
- `Task<Result> ConnectAsync(ConnectionOptions options, CancellationToken cancellationToken)`: 同接口/基类的说明。
- `Task DisconnectAsync()`: 同接口/基类的说明。
- `Task<Result<ZevisionReply>> SendAsync(string command, TimeSpan minimumResponseTimeout, CancellationToken cancellationToken)`: 同接口/基类的说明。
- `Task<Result<ZevisionReply>> SendHiddenAsync(string command, TimeSpan minimumResponseTimeout, CancellationToken cancellationToken)`: 同接口/基类的说明。
- `void Dispose()`: 同接口/基类的说明。

#### static class `CommandText`
指令集 (Zevision.Commands) 的界面文本:按 `Current` 选择中英文。 指令库本身不依赖界面语言,名称、单位、选项都同时给出中英文。
- `static TextLanguage Language { get; }`: 当前界面语言对应的指令文本语言。
- `static string Name(CommandDefinition definition)`: 说明,如 `密度` / `Density (g/cm^3)`。
- `static string Label(CommandDefinition definition)`: 带单位的名称,如 `密度 (g/cm³)`。
- `static string UnitText(CommandDefinition definition)`: 单位,如 `秒` / `s`。
- `static string RemarkText(CommandDefinition definition)`: 补充说明。
- `static string RangeText(CommandDefinition definition)`: 范围或选项说明,如 `0.100 ~ 100.000 g/cm³`。
- `static IReadOnlyList<string> IndexNames(CommandDefinition definition)`: 各级编号的含义,如 膜系、预熔。
- `static string LabelFor(CommandDefinition definition, string id, string deviceDescription = null)`: 某一项的显示名称,如 `预熔 2 · 起始斜坡/上升时间`。
- `static string TextValue(CommandOption option)`: 选项的含义,如 `关` / `Off`。
- `static string Value(BilingualText text)`: 中英文文本中当前语言的一段。
- `static string ObjectKind(char group)`: 对象类型的名称,如 `M` → 材料 / Material。

#### static class `CommunicationText`
通讯库 (Zevision.Communication) 结果的界面文本。通讯库只给出英文说明和错误类型, 这里按 `Current` 生成中英文提示,内容与拆分前的程序一致。
- `static string Describe(ZevisionError error, string command = null)`: `ZevisionClient` 操作失败的说明。
- `static string FramingHint(FramingMismatch mismatch, ConnectionEndpoint endpoint)`: 帧格式不一致的提示 (`FramingCheck`),附在通讯错误之后,如"设备的响应带长度字节,而程序未启用长度…"。
- `static string Localized(ZevisionErrorCode code)`: 设备错误代码的说明,如 `B` → 非法参数值。
- `static string Localized(FrameError error)`: 帧解码错误的说明,如 `ChecksumMismatch` → 校验和不匹配。
- `static string ValidateCommand(string command)`: 校验命令 (规则见 `CheckCommand`)。
- `static string ValidateValue(string value)`: 校验设置值 (规则见 `CheckValue`)。

#### static class `Lang`
中英文切换:启动时按系统确定一次语言 (中国地区或中文系统用中文,其他用英文),之后不再改变。 文本在调用处成对给出 (`T`),不依赖 resx 代码生成。
- `static UiLanguage Current { get; }`: 当前语言。
- `static bool IsChinese { get; }`: 是否为中文。
- `static string T(string chinese, string english)`: 按当前语言选择文本。
- `static void Initialize(UiLanguage language)`: 设置程序的语言 (启动时调用一次,在创建任何窗口之前)。
- `static UiLanguage Detect(CultureInfo uiCulture, CultureInfo formatCulture, string setting)`: 确定语言:`setting` 为 `zh`/`en` 时按设置;否则 (`auto`、空、无法识别) 系统显示语言是中文,或区域格式是中国 (CN) 时用中文,其他用英文。
- `static IDisposable Override(UiLanguage language)`: 在当前异步流中临时使用指定语言 (用于测试),Dispose 后恢复。

#### record `LocalizedText`
一条文本的中英文。
- `LocalizedText(string Chinese, string English)`: 一条文本的中英文。
- `string Chinese { get; init; }`: 中文。
- `string English { get; init; }`: 英文。

#### static class `TextCatalog`
业务层和存储层的中英文文本,来自程序 (exe) 旁边的翻译文件 `FileName`: `{ "键": { "zh": "中文", "en": "English" } }`。按 `Current` 取当前语言。
- `const string FileName`: 翻译文件名,放在程序 (exe) 所在的文件夹。
- `static IEnumerable<string> Keys { get; }`: 全部键。
- `static void Initialize(IReadOnlyDictionary<string, LocalizedText> entries)`: 设置文本 (程序启动时调用一次,在任何文本被使用之前)。
- `static string Get(string key)`: 当前语言的文本 (没有参数)。
- `static string Format(string key, params object[] args)`: 当前语言的文本,用 `args` 替换其中的 `{0}`、`{1}`…
- `static string Template(string key, UiLanguage language)`: 某种语言的文本模板;没有该键时为 null。

#### enum `UiLanguage` : IComparable, IFormattable, IConvertible
界面语言。
- `Chinese` = 0: 简体中文。
- `English` = 1: English.

#### class `ConnectionPreferences`
需要在两次运行之间保留的通讯配置,保存为 `settings.json`。默认值与设备出厂设置一致。 校验和、长度字节和保存通讯数据按连接方式 (RS232 / TCP) 分别保存:设备上两者是独立的设置 (G1304/G1305 与 G1407/G1408)。
- `ConnectionPreferences()`: 创建默认配置。
- `const string DefaultEditSource`: `EditSource` 的默认值:实机数据。
- `const string DefaultConnectionType`: `ConnectionType` 的默认值:RS232。
- `const string DefaultDisplayFormat`: `DisplayFormat` 的默认值:ASCII。
- `const string DefaultLanguage`: `Language` 的默认值:按系统自动选择。
- `string PortName { get; set; }`: 上次使用的端口名;为空表示没有记录。
- `int BaudRate { get; set; }`: 波特率。
- `int ResponseTimeoutMs { get; set; }`: 响应空闲超时 (毫秒)。
- `bool SerialChecksumEnabled { get; set; }`: RS232 连接是否启用校验和 (设备 G1304)。
- `bool SerialLengthEnabled { get; set; }`: RS232 连接是否启用长度字节 (设备 G1305)。
- `bool SerialSaveCommunicationData { get; set; }`: RS232 连接是否保存通讯数据 (数据显示区的内容) 到文本文件。JSON 键名沿用开发版本的 rawCaptureEnabled。
- `bool TcpChecksumEnabled { get; set; }`: TCP 连接是否启用校验和 (设备 G1407)。0.9.x 的文件没有这一项 (两种连接共用 RS232 的设置),这时与 RS232 相同。
- `bool TcpLengthEnabled { get; set; }`: TCP 连接是否启用长度字节 (设备 G1408)。文件中没有这一项时与 RS232 相同。
- `bool TcpSaveCommunicationData { get; set; }`: TCP 连接是否保存通讯数据。文件中没有这一项时与 RS232 相同。
- `string EditSource { get; set; }`: 修改配置的数据来源 (`Device` 实机数据 / `File` 保存的配置)。不认识的值按默认处理。
- `string ConnectionType { get; set; }`: 连接方式 (`Serial` RS232 / `Tcp`)。不认识的值按 RS232 处理。
- `string TcpHost { get; set; }`: TCP 连接的设备 IP 地址或主机名。
- `string DisplayFormat { get; set; }`: 收发数据的显示格式 (`Ascii` / `Hex` / `Binary`)。不认识的值按 ASCII 处理。
- `bool RepeatSend { get; set; }`: 是否勾选重复发送。
- `int RepeatIntervalMs { get; set; }`: 重复发送间隔 (毫秒)。不在选项中的值按默认处理。
- `string Language { get; set; }`: 界面语言:`auto` (按系统:中国地区或中文系统用中文,其他用英文)、`zh` 或 `en`。

#### interface `IPreferencesStore`
读写持久化的通讯配置。
- `Result<ConnectionPreferences> Load()`: 读取配置。文件不存在 (首次运行) 时成功返回默认值;文件损坏或无法读取时返回失败,调用方应使用默认值并提示。
- `Result Save(ConnectionPreferences preferences)`: 保存配置。

### 16.2 Zevision.Storage

#### static class `AppFolders`
程序数据位置:通讯配置 (settings.json)、仪器配置文件 (recipe)、日志 (logs)。
- `static string GetSettingsFilePath()`: 通讯配置文件路径。
- `static string GetRecipeFolder()`: 仪器配置文件目录。
- `static string GetLogsFolder()`: 日志目录 (收发记录、error.txt)。

#### class `CommunicationLogFiles` : ICommunicationRecorderFactory
为每次连接在日志目录中创建一个通讯数据文件 `capture_yyyyMMdd_HHmmss.txt` (`CommunicationLogWriter`)。 文件永久保留,不自动清理。
- `CommunicationLogFiles(IErrorLog errorLog, Func<string> folder = null)`: 创建工厂。
- `Result<ICommunicationRecorder> Create()`: 同接口/基类的说明。

#### class `FileErrorLog` : IErrorLog
以追加方式把错误写入文本文件 (UTF-8)。线程安全;写入失败时静默放弃,不影响程序运行。
- `FileErrorLog(string path, string sessionHeader = null, long maxFileBytes = 5242880)`: 创建日志。文件和目录在第一次写入时创建。
- `const long MaxFileBytes`: 单个文件的大小上限 (字节)。
- `const int MaxMessageLength`: 单条消息的最大长度,超出部分截断 (完整数据见收发记录)。
- `void Write(string source, string message, Exception exception = null)`: 同接口/基类的说明。

#### class `JsonFilePreferencesStore` : IPreferencesStore
把通讯配置保存为软件目录下的 `settings.json` (见 `GetSettingsFilePath`)。
- `JsonFilePreferencesStore(string path)`: 创建存储。
- `Result<ConnectionPreferences> Load()`: 同接口/基类的说明。
- `Result Save(ConnectionPreferences preferences)`: 同接口/基类的说明。

#### class `RecipeFileStore` : IRecipeStore
`IRecipeStore` 的实现:文件格式见 `RecipeFile`,默认目录见 `GetRecipeFolder`。
- `RecipeFileStore()`: 默认构造函数。
- `string Folder { get; }`: 同接口/基类的说明。
- `string FileExtension { get; }`: 同接口/基类的说明。
- `Result Save(InstrumentRecipe recipe, string path)`: 同接口/基类的说明。
- `Result<InstrumentRecipe> Load(string path)`: 同接口/基类的说明。

#### static class `TextFile`
读取翻译文件 (`FileName`,程序旁边),交给 `Initialize`。 文件为 UTF-8 JSON (可以带 BOM):`{ "键": { "zh": "中文", "en": "English" } }`,每条都必须有 zh 和 en。
- `static string DefaultPath { get; }`: 程序旁边的翻译文件路径。
- `static Result<IReadOnlyDictionary<string, LocalizedText>> Load(string path)`: 读取并检查文件。失败时的说明是原因本身 (英文或系统语言的异常消息):文本还没有读到,调用方自己给出提示。
