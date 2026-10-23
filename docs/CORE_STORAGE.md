# Zevision 业务层与存储层编程手册

| 项目 | 内容 |
|---|---|
| 文档标识 | ZEV-CPM-02 |
| 文档版本 | 1.3 |
| 适用软件 | `Zevision.Core` 1.3.1、`Zevision.Storage` 1.3.1（随 Zevision 1.3.1） |
| 编制单位 | INFICON |
| 日期 | 2026-10-23 |

## 目次

1. [范围](#1-范围)
2. [引用文件](#2-引用文件)
3. [软件编程环境](#3-软件编程环境)
4. [编程信息](#4-编程信息)
5. [注解](#5-注解)
6. 附录 A（资料性）API 参考

## 1 范围

### 1.1 标识

本文档的标识为 ZEV-CPM-02，名称为《Zevision 业务层与存储层编程手册》，版本 1.1。适用的软件如下：

- `Zevision.Core.dll` 1.1.0：业务层；
- `Zevision.Storage.dll` 1.1.0：数据存储层。

两个程序集随 Zevision（IMC300 通信测试）1.1.0 发布，版本号与应用相同，不单独发布，接口随应用版本变化。单独发布的通信库和指令库见 ZEV-CPM-01《Zevision 链接库编程手册》。

### 1.2 系统概述

Zevision 是 INFICON IMC300 膜厚控制仪的上位机软件，经 RS232 或 TCP 按 Zevision 协议与设备通信，用于发送协议指令，以及保存、恢复和修改仪器配置。界面层（`Zevision.exe`）只通过本手册所述的接口访问设备和数据。

表 1  程序集

| 程序集 | 内容 | 依赖 |
|---|---|---|
| `Zevision.Core.dll` | 业务逻辑：设备通信服务；仪器配置的读取、保存、恢复和修改；配置项规则；修改窗口的编辑会话；中英文文本；通信配置模型；存储接口的定义 | .NET Framework、`Zevision.Communication.dll`、`Zevision.Commands.dll` |
| `Zevision.Storage.dll` | 数据存储：以文件实现业务层的存储接口（配置文件、`settings.json`、`error.txt`、通信数据文件）；读取 `Texts.json` | .NET Framework、`Zevision.Core.dll` |

分层结构见图 1。

```
Zevision.exe            界面：Views、ViewModels、App（组装根）
   │ 只使用 Core 的接口；只有 App 创建 Storage 的实现
   ▼
Zevision.Core.dll       业务逻辑；定义 IRecipeStore、IPreferencesStore、IErrorLog、ICommunicationRecorder(Factory)
   │          ▲
   │          └──────── Zevision.Storage.dll   以文件实现上述接口
   ▼
Zevision.Commands.dll ─► Zevision.Communication.dll     指令集、协议与传输（ZEV-CPM-01）
```

图 1  分层结构

依赖只指向下层。单元测试 `LibraryBoundaryTests` 检查以下约束：

- `Zevision.Core` 只引用 .NET Framework 和两个链接库，不引用 WPF 和 `Zevision.Storage`；其元数据中没有文件类型（`File`、`Directory`、`FileStream` 等），即业务层不读写文件。
- `Zevision.Storage` 只引用 .NET Framework 和 `Zevision.Core`。
- 界面层的 `ViewModels/`、`Views/` 不引用 `Zevision.Storage`，只有 `App.xaml.cs` 引用。
- 业务层和存储层的代码中没有中文字面量，文本全部位于 `Texts.json`。

### 1.3 文档概述

本文档依据 GB/T 8567-2006 中的计算机编程手册（CPM）编制，供维护 Zevision 的开发人员使用，不随链接库发布。对模板的剪裁如下：

- 原模板中面向计算机指令系统的条目（指令、输入输出控制）改为面向程序集公开接口的“编程接口”（4.2）；
- 增加“测试与替换实现”（4.5）、“常见修改”（4.6）和“性能与内存”（4.7）；
- 全部公开类型和成员列于附录 A。

示例代码只使用公开 API，已按 Release 版本编译运行；代码注释中“→”之后为实际输出。

## 2 引用文件

表 2  引用文件

| 标识 | 名称 | 位置 |
|---|---|---|
| GB/T 8567-2006 | 计算机软件文档编制规范 | — |
| GB/T 1.1-2020 | 标准化工作导则 第 1 部分：标准化文件的结构和起草规则 | — |
| ZEV-CPM-01 | Zevision 链接库编程手册 | `docs/LIBRARY.md` |
| ZEV-SDD-01 | Zevision 概要设计说明书 | `docs/概要设计说明书.md` |
| ZEV-IDD-01 | IMC300 Zevision 通信协议规格书 | `docs/协议规格书.md` |
| ZEV-STR-01 | Zevision 联调记录 | `docs/联调记录.md` |
| — | IMC300 / Zevision 上位机软件开发规范 | `docs/开发文档.md` |
| — | Zevision 协议译稿 | `docs/Zervison_Protocol.md`、`docs/INFICON_IMC300_Zevision通讯协议说明.md` |

## 3 软件编程环境

### 3.1 组成部件

表 3  编程环境

| 项目 | 内容 |
|---|---|
| 目标框架 | .NET Framework 4.7.2 |
| 语言版本 | C# 9（Visual Studio 2019，MSBuild 16.11） |
| 引用的链接库 | `Zevision.Communication.dll` 1.2.0、`Zevision.Commands.dll` 1.1.0（ZEV-CPM-01） |
| 运行时文件 | `Texts.json`，位于程序目录，包含业务层和存储层的全部提示和错误文本 |
| 单元测试 | `Zevision.Tests` |

### 3.2 命名空间

表 4  命名空间

| 命名空间 | 主要类型 | 用途 |
|---|---|---|
| `Zevision.Core` | `Result`、`Result<T>`、`IErrorLog`、`ICommunicationRecorder`、`ICommunicationRecorderFactory` | 结果类型；错误日志和通信数据记录的接口 |
| `Zevision.Core.Devices` | `IDeviceService`、`ZevisionDeviceService`、`DeviceServiceExtensions`、`DeviceIdentity`、`FrameFormatDetector`、`FrameSettings`、`ReconnectPolicy`、`ManualCommandPolicy`、`FramingCheck` | 设备连接、握手与收发 |
| `Zevision.Core.Configuration` | `InstrumentConfigService`（`IInstrumentConfigReader`/`Writer`/`Service`）、`InstrumentSnapshot`、`InstrumentRecipe`、`RestorePlanner`、`RecipeRules`、`CrucibleRule`、`ObjectTypes`、`DeviceIds`、`ParameterValue`、`RecipeDocument`、`RecipeEditSession`、`IRecipeStore` | 仪器配置 |
| `Zevision.Core.Design` | `DesignDocument`、`MaterialDesign`、`ProcessDesign`、`DesignRules`、`DesignBuilder`、`DesignDevice`、`DeviceContents`、`DeviceDeletion`、`IDesignStore` | 工艺编辑：材料和工艺的检查，转换为设备上新建的对象；设备上的工艺和材料；删除时设备上一起删除的对象 |
| `Zevision.Core.Localization` | `Lang`、`TextCatalog`、`CommandText`、`CommunicationText` | 中英文文本 |
| `Zevision.Core.Settings` | `ConnectionPreferences`、`IPreferencesStore` | 通信配置 |
| `Zevision.Storage` | `AppFolders`、`TextFile`、`JsonFilePreferencesStore`、`RecipeFileStore`、`DesignFileStore`、`FileErrorLog`、`CommunicationLogFiles` | 文件实现 |

### 3.3 组装与启动

`App.OnStartup` 是唯一的组装位置（组装根），不使用依赖注入容器，全部采用构造函数注入。初始化应按以下顺序进行：

1. 文本：`TextFile.Load` → `TextCatalog.Initialize`，在任何业务层或存储层文本被使用之前完成。读取失败时程序不能继续运行（提示只会显示键名）；此时 `TextCatalog` 为空，提示文本由界面层的 `Lang.T` 给出。
2. 语言：读取 `settings.json`，调用 `Lang.Initialize(Lang.Detect(...))`。语言在创建日志和任何窗口之前确定，之后不再改变；切换语言即重新启动程序。
3. 错误日志：创建 `FileErrorLog`，然后挂接全局异常处理程序。
4. 服务：创建设备服务、配置服务和存储，交给视图模型。

```csharp
// 1. 文本（Texts.json 位于程序目录）
var texts = TextFile.Load(TextFile.DefaultPath);
if (!texts.IsSuccess)
{
    // 提示（不能使用 TextCatalog）后退出
    return;
}

TextCatalog.Initialize(texts.Value);

// 2. 语言：settings.json 的 language（auto / zh / en）优先；读取失败时按系统设置
var preferences = new JsonFilePreferencesStore(AppFolders.GetSettingsFilePath());
var saved = preferences.Load();
Lang.Initialize(Lang.Detect(CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture, saved.IsSuccess ? saved.Value.Language : null));

// 3. 错误日志：第二个参数为会话标题（AppInfo 属于界面层：版本、系统、CLR、位数），第一次写入前输出为 "==== 标题 ===="
IErrorLog errorLog = new FileErrorLog(Path.Combine(AppFolders.GetLogsFolder(), "error.txt"), AppInfo.Description);

// 4. 服务
var device = new ZevisionDeviceService(DeviceTransports.Create, errorLog);   // 每次连接按 RS232 / TCP 创建传输
IInstrumentConfigService config = new InstrumentConfigService(device);
IRecipeStore recipes = new RecipeFileStore();
ICommunicationRecorderFactory recorders = new CommunicationLogFiles(errorLog);
```

退出时（`App.OnExit`）依次保存通信配置、释放视图模型、调用 `device.Dispose()`（关闭连接）。每一步单独捕获异常并写入错误日志，一步失败不影响后续步骤。

## 4 编程信息

### 4.1 编程特征

#### 4.1.1 设计原则

- 预期内的失败返回 `Result`，如端口被占用、设备拒绝、超时、文件被修改。`Error` 为可显示的说明（界面语言）。程序错误（参数为 null、ID 格式错误）抛出异常。
- 接口只用于需要替换实现之处：设备、存储、日志。纯计算（配置项规则、恢复计划、ID 工具、值的校验）由无状态的静态类实现，调用和测试无需构造服务对象。
- 修改设备之前确认设备型号（G03）与配置一致，且设备处于空闲状态（C09 = `Ready`）。写入后用 `Q` 读回校验；遇到第一个错误即停止，不回滚。
- 指令只由指令集生成（`ZevisionCommandSet.Query/Update/Add/Build`），不以 `"Q" + id` 的方式拼接。
- 所有收发经过同一个 `IDeviceService`，同一时间只有一个请求等待响应。
- 异步方法都接受 `CancellationToken`；业务层和存储层内部的 `await` 一律使用 `ConfigureAwait(false)`。

#### 4.1.2 结果类型 Result

```csharp
var loaded = recipes.Load(path);
if (!loaded.IsSuccess)
{
    ShowError(loaded.Error);     // 失败时 Error 不为 null，可空分析可以识别（无需 !）
    return;
}

var recipe = loaded.Value;       // 失败时访问 Value 抛出 InvalidOperationException
```

- `IsSuccess` 等价于 `Error == null`；`default(Result)` 视为成功；`Fail(null)` 抛出 `ArgumentNullException`。
- `IsSuccess` 标有 `[MemberNotNullWhen(false, nameof(Error))]`，在 `if (!r.IsSuccess)` 分支中 `r.Error` 为非空。
- `Result` 是业务层的类型，说明已本地化。通信库的 `ZevisionResult`（英文说明，带错误类型 `ZevisionErrorKind`）只在 `ZevisionDeviceService` 内部使用，由 `CommunicationText.Describe` 转换为 `Result`。
- 带附加信息的结果：`RestoreReport`（恢复、写入）、`ObjectCreation`（新建对象）。少数方法返回 `string?`（失败原因，成功时为 null），如 `RecipeEditSession.Commit`。

#### 4.1.3 异常与取消

表 5  异常与取消的处理

| 情况 | 处理方式 |
|---|---|
| 参数为 null、ID 格式错误、指令集不支持的动作 | 抛出 `ArgumentNullException` / `ArgumentException`（程序错误，调用方应修正代码） |
| 通信失败、设备拒绝、超时、文件读写失败、文件校验失败 | 返回失败的 `Result`，说明可用于显示 |
| 取消 | `ExecuteRestoreAsync`、`ApplyEditsAsync` 返回 `RestoreReport.Cancelled = true`；其他异步方法抛出 `OperationCanceledException` |
| 错误日志、通信数据文件写入失败 | 不抛出异常（仅用于记录，不影响通信） |

取消不撤销已发送的指令：取消前设备上已执行的修改保留。

#### 4.1.4 线程

- 业务层和存储层的方法可在任何线程调用，没有 UI 线程要求。内部的 `await` 使用 `ConfigureAwait(false)`；调用方（视图模型）自身的 `await` 不使用，完成后回到 UI 线程。
- `IDeviceService` 的事件在后台线程触发，更新界面前应切换到 UI 线程（见 4.2.1.4）。
- `IProgress<RestoreProgress>` 在后台线程报告；在 UI 线程创建的 `Progress<T>` 自动切换回 UI 线程。
- 同一个 `IDeviceService` 上并发的 `SendAsync` 自动排队。保存、恢复、修改由多条指令组成，不应同时运行两个，否则指令会交错；界面在操作期间禁用其他发送。
- 静态类（`RecipeRules`、`ObjectTypes`、`DeviceIds`、`ParameterValue`、`RestorePlanner`、`ManualCommandPolicy`、`TextCatalog`、`Lang`）线程安全。`RecipeDocument`、`RecipeEditSession`、`InstrumentRecipe` 不是线程安全的，只应在一个线程上使用。

#### 4.1.5 文本的语言

返回的说明按 `Lang.Current`（界面语言）给出。测试中用 `Lang.Override(UiLanguage.English)` 临时切换语言，其作用范围限于当前异步流，不影响并行的其他测试。

### 4.2 编程接口

#### 4.2.1 设备通信接口 IDeviceService

##### 4.2.1.1 功能

`ZevisionDeviceService` 实现 `IDeviceService`，封装通信库的 `ZevisionClient`（见 ZEV-CPM-01 中 `ZevisionClient` 的连接与收发部分），并增加以下功能：

- 每次 `ConnectAsync` 按当时的参数新建一个 `ZevisionClient`：连接期间参数不变，更换参数重新连接时无需重建服务；
- 结果说明按界面语言给出；
- 握手（`HandshakeAsync`，见 4.2.1.7）：按设备的回答确定帧格式并改用该格式，读取型号；
- 不显示的发送（`SendHiddenAsync`）；
- 帧格式不一致时的提示；
- 转发重发事件（`Retrying`；重发本身由通信库按 `ConnectionOptions.RetryCount` 完成）；
- 把读取循环和事件处理程序的意外异常写入 `IErrorLog`（心跳无应答引起的中断不写入）。

##### 4.2.1.2 连接与断开

```csharp
var options = new ConnectionOptions(
    new SerialSettings("COM3", 115200),          // TCP：new TcpSettings("192.168.1.10", TcpSettings.DefaultPort)
    new FrameOptions(checksumEnabled: true, lengthEnabled: true),
    TimeSpan.FromSeconds(1));                     // 响应空闲超时

var connected = await device.ConnectAsync(options, cancellationToken);
if (!connected.IsSuccess)
{
    ShowError(connected.Error);                   // 如 "无法连接 COM3, 115200, 8N1: 对端口“COM3”的访问被拒绝。"
}

// …
await device.DisconnectAsync();                   // 未连接时立即返回；不触发 ConnectionLost
```

- 已连接或正在连接时再次调用 `ConnectAsync`，返回失败“已经连接。”。
- 连接中断（拔线、TCP 断开）后可再次调用 `ConnectAsync`：上一个连接的客户端被释放，其事件不再转发。
- `Dispose()` 关闭连接，之后不再使用该服务。
- 帧格式（`FrameOptions`）应与设备一致：RS232 为 G1304（校验和）/ G1305（长度），TCP 为 G1407 / G1408。连接后调用 `HandshakeAsync` 可按设备的实际设置自动改正（见 4.2.1.7）。
- 重发和心跳由 `ConnectionOptions` 决定（`RetryCount`、`HeartbeatInterval`、`HeartbeatMissLimit`，见 ZEV-CPM-01 中的会话功能部分）。启用握手时，应用设置为重发 2 次、心跳间隔 5 s、连续 3 次无应答判定连接中断（`ConnectionSettingsViewModel.HandshakeRetryCount`、`HandshakeHeartbeatInterval`）。

##### 4.2.1.3 发送

```csharp
var reply = await device.SendAsync(ZevisionCommandSet.Query("M0103"), cancellationToken);   // 扩展方法，超时取连接设置
if (!reply.IsSuccess)
{
    ShowError(reply.Error);                       // 通信失败：未连接、超时、帧无效等
    return;
}

var response = reply.Value.Responses[0];
if (!response.IsSuccess)
{
    ShowError(response.ErrorCode.Localized());    // 设备拒绝（-A、-B、-D 等）不属于通信失败，如 "非法格式"
    return;
}

var density = response.Data;                      // "2.700"
```

表 6  发送方法

| 方法 | 用途 |
|---|---|
| `SendAsync(command, minimumResponseTimeout, ct)` | 发送并等待响应，收发显示在数据显示区（触发 `FrameSent` / `FrameReceived`） |
| `SendAsync(command, ct)`（扩展方法） | 同上，`minimumResponseTimeout = TimeSpan.Zero`（只使用连接设置的超时） |
| `SendHiddenAsync(command, minimumResponseTimeout, ct)` | 同上，但不显示（见 4.2.1.4）；程序内部读取配置的帮助指令（`?`、`?M`）使用该方法 |

- 一帧可包含多条指令（`QG03;QC09`），每条的响应都在 `Responses` 中，按回显对应（`ZevisionResponseParser.EchoOf`）。
- 超时为空闲超时：从发送或最后收到的字节起计时，大响应不因总时间长而超时。响应前停顿较长的指令应传入 `minimumResponseTimeout`：帮助指令使用 `InstrumentConfigReader.SlowReplyTimeout`（2 s）。
- 失败的说明已本地化。超时、帧无效、无法解析时，若本次发送期间收到过与帧格式设置不一致的帧（见 4.2.1.5），说明之后附加检查设置项的提示。
- 未连接时返回失败“未连接。”，不抛出异常。

##### 4.2.1.4 事件

表 7  IDeviceService 的事件

| 事件 | 触发条件 | 说明 |
|---|---|---|
| `FrameSent` | 帧发送前 | 后台线程 |
| `FrameReceived` | 收到一帧（含错误帧、帧外数据） | 后台线程，按收到的顺序依次触发 |
| `ConnectionLost` | 读取意外结束（拔线、TCP 断开、读取出错）或心跳无应答（`Reason = HeartbeatTimeout`） | 后台线程；主动调用 `DisconnectAsync` 不触发。说明由 `CommunicationText.DescribeConnectionLost(e, missLimit)` 生成 |
| `Retrying` | 一次发送传输出错、即将重发 | 发送方的线程；`SendHiddenAsync` 和握手的发送不触发。说明由 `CommunicationText.DescribeRetry(e)` 生成 |
| `CommandsFailed` | 一次发送结束，其中有指令没有得到成功响应 | 发送方的线程；`Count` 为失败的指令数，用于数据显示区的错误指令数（ERR）。设备的否定响应（含不回显的 `-C`）计该指令；发出后没有有效响应（超时、写入失败、响应帧无效或无法解析）计帧中全部指令；重发后成功的、未发出的、等待时连接断开的不计。`SendHiddenAsync` 和握手的发送不触发；处理程序的异常写入 `error.txt`，不影响发送结果 |

```csharp
var ui = SynchronizationContext.Current;                       // 在 UI 线程上取得
device.FrameReceived += (s, e) =>
{
    var lines = FormatForDisplay(e.Frame);                     // 在后台线程格式化
    ui.Post(_ => log.AddRange(lines), null);                   // 切换到 UI 线程更新界面
};
device.ConnectionLost += (s, e) => ui.Post(_ => OnConnectionLost(e.Exception), null);
```

- 事件处理程序抛出的异常不中断读取循环，并写入 `error.txt`。读取循环本身的意外异常（拔线等 I/O 异常除外）同样写入 `error.txt`，并触发 `ConnectionLost`。
- `SendHiddenAsync` 的收发不触发事件，因此不保存到通信数据文件，也不计入收发计数：不触发 `FrameSent`；其响应、超时时报告的不完整数据，以及之后收到的回显相同的帧（如取消读取 `?` 后设备仍在发送的剩余数据）都不触发 `FrameReceived`，直至同一指令再次以 `SendAsync` 发送。

##### 4.2.1.5 帧格式不一致检查（FramingCheck）

`FramingCheck.Check(frame, options)` 判断收到的帧是否表明设备的长度设置与程序不同，结果见表 8。

表 8  FramingCheck 的结果

| 结果 | 情况 |
|---|---|
| `ReplyHasLength` | 程序未启用长度，设备的响应带两个长度字节 |
| `ReplyWithoutLength` | 程序启用了长度，设备的响应为 `{文本}` |
| `None` | 一致，或无法判断 |

`ZevisionDeviceService` 对每个收到的帧（与当前帧格式比较）进行该检查；发送失败时把 `CommunicationText.FramingHint(mismatch, endpoint)` 附加在说明之后。`FrameSettings.Ids(endpoint)` 给出应检查的设置项：RS232 为 `(G1304, G1305)`，TCP 为 `(G1407, G1408)`。启用握手时，连接时已按设备的设置改正帧格式，该提示主要用于关闭握手的调试连接。

##### 4.2.1.6 手动输入的指令（ManualCommandPolicy）

```csharp
var text = ManualCommandPolicy.ToUpperCommands("qg01;um0101:Al");   // → "QG01;UM0101:Al"（数据部分不变）
var commands = ManualCommandPolicy.Split(" QG01；QG02\n\nQG03 ");    // → QG01 | QG02 | QG03（;、；和换行都是分隔符）
var frame = string.Join(";", commands);
var timeout = ManualCommandPolicy.MinimumResponseTimeout(frame);    // 含 ? 时为 2 s，否则为 TimeSpan.Zero
if (ManualCommandPolicy.ContainsFullHelpCommand(frame))
{
    // 完整的 ? 约 116 KB、10 s，发送前提示操作者（?M 等组帮助除外）
}

var reply = await device.SendAsync(frame, timeout, cancellationToken);
```

设备只接受大写命令（小写命令回答 `-A`）；数据部分可能是名称，保持原样。

##### 4.2.1.7 握手（HandshakeAsync）

```csharp
var connected = await device.ConnectAsync(options, ct);
var identity = await device.HandshakeAsync(options.Frame, ct);   // 先尝试界面上的帧格式
if (!identity.IsSuccess)
{
    await device.DisconnectAsync();
    ShowError(identity.Error);              // 如 "设备没有应答 H01 (…)。请检查端口、波特率或 IP 地址…"
    return;
}

var frame = identity.Value.Frame;           // 设备实际使用的帧格式，连接已改用该格式
var hello = identity.Value.Hello;           // "Zevision IMC300 1.8.11.202606101229"
foreach (var warning in identity.Value.Warnings) { Log(warning); }   // 型号不是 IMC300、设置与实际不一致
```

握手由内部类 `DeviceHandshake` 执行，步骤如下（设备行为见 ZEV-STR-01 中 2026-10-19 的记录）：

1. 以 `preferred` 发送 `H01`（不重发）。回答为 `H01+:…` 时帧格式正确。
2. 否则等待 300 ms（设备对格式不符的输入最迟约 230 ms 回答 `-T`），用 `FrameFormatDetector.Detect` 从此期间收到的字节识别设备的帧格式，改用该格式后再次发送 `H01`；无法识别时依次尝试其余格式（最多 4 次）。第一次未收到任何字节时表明设备不在线（设备对任何输入都会回答），握手失败。
3. 以一帧查询 `QG03;QG06;QG1304;QG1305`（TCP 为 G1407/G1408）。读不到 G03 时握手失败；G06（系统序列号，用于通信数据文件名）读不到时 `SerialNumber` 为 null，不影响握手；型号不是 `DeviceIdentity.ExpectedModel`（IMC300），或设置与实际使用的帧格式不一致时，作为 `Warnings` 返回。

- 握手期间的收发不触发 `FrameSent`/`FrameReceived`（不显示、不保存、不计数）；收到的字节另行记录（最多 4 KB），用于识别帧格式。
- 耗时：帧格式一致时为两帧（约 50 ms）；不一致时增加一次探测和 300 ms 等待（实测，RS232 约 460 ms，TCP 约 380 ms）。
- 设备的帧格式设置被修改后（如手动发送的 `UG1304:1` 被接受），以 `FrameSettings.AfterUpdates(endpoint, current, commands, responses)` 得到新格式，再以新格式为 `preferred` 调用 `HandshakeAsync`。设备对该 `U` 命令仍按旧格式回答，从下一帧起只接受新格式。

表 9  握手相关类型

| 类型 | 作用 |
|---|---|
| `DeviceIdentity` | 握手结果：`Hello`、`Model`、`Frame`、`Warnings`；`ExpectedModel = "IMC300"` |
| `FrameFormatDetector` | `Detect(bytes)`：依次以“长度+校验和”“长度”“校验和”“无”解码，返回第一个能解出有效响应的格式；`All`：四种格式 |
| `FrameSettings` | `Ids(endpoint)`：帧格式设置项的 ID；`AfterUpdates(…)`：一帧指令被接受后的新帧格式（未改变时为 null） |
| `ReconnectPolicy` | `DelayBefore(attempt)`：自动重连前的等待，依次为 1 s、2 s、4 s、8 s、16 s、32 s，之后为 60 s（`MaxDelay`） |

#### 4.2.2 设备配置读取接口

`InstrumentConfigService`（构造参数为 `IDeviceService`）同时实现读取（`IInstrumentConfigReader`）和修改（`IInstrumentConfigWriter`）。仅读取配置的类（如修改窗口的“信息”页）依赖 `IInstrumentConfigReader`。读取方法见表 10。

表 10  设备配置读取方法

| 方法 | 发送的指令 | 耗时（115200 bit/s） | 结果 |
|---|---|---|---|
| `ReadSnapshotAsync(ct)` | `?`（不显示） | 约 10 s，116 KB（实测） | `InstrumentSnapshot`：全部项及当前值 |
| `ReadGroupAsync(group, ct)` | `?M` 等组帮助（不显示） | 材料约 0.5 s（实测）；膜系最大，按数据量估算约 5 s | 该组可保存的项（挑选规则与 `ToRecipe` 相同） |
| `QueryAsync(ids, ct)` | `Q` 指令，每帧 20 条（`QueryBatchSize`） | 每帧一次往返 | 每项的值或设备拒绝的原因 |

```csharp
var snapshot = await config.ReadSnapshotAsync(cancellationToken);
if (!snapshot.IsSuccess)
{
    ShowError(snapshot.Error);                     // 如 "读取设备配置失败: …"
    return;
}

var device = snapshot.Value;
var line = $"{device.DeviceName} {device.FirmwareVersion} {device.CurrentState}";   // "IMC300 1.8.11.202606101229 Ready"
var density = device.GetValue("M0103");            // "2.700"；设备上没有该项时为 null

var values = await config.QueryAsync(new[] { "M0101", "M0103", "M0901" }, cancellationToken);
if (values.IsSuccess)
{
    foreach (var value in values.Value)
    {
        Console.WriteLine(value.Error == null ? $"{value.Id} = {value.Value}" : $"{value.Id}: {value.Error}");
    }
}
// → M0101 = Al / M0103 = 2.700 / M0901: 非法格式      （单项被拒绝不影响其他项）
```

- `InstrumentSnapshot` 的常量：`DeviceNameId` = `G03`、`SerialNumberId` = `G06`、`StateId` = `C09`、`ReadyState` = `Ready`。属性：`DeviceName`（G03）、`ModelNumber`（G05）、`SerialNumber`（G06）、`FirmwareVersion`（G08）、`CurrentState`（C09）、`Entries`（保持设备给出的顺序）。`GetValue` / `GetDescription` 按 ID 查找，没有时为 null；ID 重复时取第一次出现的项。
- `ReadGroupAsync`：响应中没有该组的数量项（`MFE`、`FFE`、`P02FE` 等）时返回失败，不把解析失败视为“该组为空”。
- `QueryAsync`：通信失败时整体失败；单项被拒绝时该项的 `Value` 为 null，`Error` 为原因（如“非法格式”）。

#### 4.2.3 保存与恢复

##### 4.2.3.1 保存

```csharp
var snapshot = await config.ReadSnapshotAsync(cancellationToken);      // 约 10 s
if (!snapshot.IsSuccess) { ShowError(snapshot.Error); return; }

var recipe = snapshot.Value.ToRecipe(DateTimeOffset.Now);             // 按 RecipeRules 挑选可恢复的项
var saved = recipes.Save(recipe, path);                                 // 计算 MD5；先写临时文件再替换
if (!saved.IsSuccess) { ShowError(saved.Error); }
```

实机数据：帮助表 1525 行，生成的配置文件 940 项，约 92 KB。

##### 4.2.3.2 恢复

```csharp
var loaded = recipes.Load(path);                                         // 校验格式版本、MD5、字段、ID
if (!loaded.IsSuccess) { ShowError(loaded.Error); return; }              // 如 "…MD5 校验失败，文件在保存后被修改过或已损坏…"

var snapshot = await config.ReadSnapshotAsync(cancellationToken);
if (!snapshot.IsSuccess) { ShowError(snapshot.Error); return; }

var planned = RestorePlanner.Plan(loaded.Value, snapshot.Value);        // 纯计算，不通信
if (!planned.IsSuccess) { ShowError(planned.Error); return; }           // 型号不符、设备不在 Ready 状态

var plan = planned.Value;
if (plan.Creates.Count == 0 && plan.Writes.Count == 0)
{
    return;                                                              // 设备与文件一致（plan.UnchangedCount 项）
}

if (!Confirm(plan))                                                      // 显示 Writes、Creates、Skipped、Warnings
{
    return;
}

var report = await config.ExecuteRestoreAsync(loaded.Value, plan, progress, cancellationToken);
```

界面层为读取和执行两个阶段各设 60 s 时限（`OperationLimits.PhaseTimeLimit`），操作者阅读确认框的时间不计入。

##### 4.2.3.3 恢复计划（RestorePlanner.Plan）

`RestorePlanner.Plan` 不与设备通信，比较文件与快照：

1. 设备未识别（没有 G03）、型号不同（比较 G03；两边都有 G05 时也比较 G05）或状态不是 `Ready` 时，返回失败。
2. 固件版本不同时，加入 `Warnings`（不阻止恢复）。
3. 文件中有而设备上没有的项：
   - 所属的顶层对象（材料、膜系、膜层、工艺）不存在时，在 `Creates` 中按原编号创建（`AM03`），顺序为材料、膜系、膜层、工艺；
   - 子列表的条目不存在时（如膜系的第 2 条预熔 `F010302`，或工艺的膜层条目 `P02010701`），在 `Creates` 中追加到列表末尾（`AF010300`）。要求设备有 `<列表>00` 项，且现有条目从 01 起连续编号；
   - 无法创建时，加入 `Skipped`。

   上述项不在 `Writes` 中：新对象带设备默认值，创建后重新读取才能确定应写入的项。
4. 与设备当前值相同的项计入 `UnchangedCount`。
5. 值不同的项加入 `Writes`，按 `RecipeRules.GroupRank` 排序（N S I O T G M F L P），组内保持文件中的顺序。
6. 以下项加入 `Skipped`：文件中重复的 ID；不可恢复的项（见 4.2.5.1）；值含协议字符（`;`、`{`、`}` 或非 ASCII 字符）的项；引用的对象（如 `F0302 = 3` 引用 `M03`）既不在设备上、也不会被创建的项（设备将回答 `-B`）；膜系的坩埚号超出所选输出源坩埚数量的项（按恢复后的输出源和坩埚数量判断，见 4.2.5.5）。

`RestoreSkip` 包含 ID、文件中的值、说明和原因，`ToString()` 为单行文本，如 `F01030301 Start Ramp/Rise time = 0:00:30 (原因)`。

##### 4.2.3.4 执行恢复（ExecuteRestoreAsync）

1. 再次确认型号和 `Ready` 状态：恢复计划在确认框之前生成，操作者确认期间设备可能已开始工艺。条件不满足时不写入任何项。
2. 写入系统设置（`RecipeRules.IsSystemSetting`：N S I O T G）。
3. 有 `Creates` 时：发送创建指令，重新读取 `?`，重新计划；循环至不再需要创建，最多 4 轮（嵌套的列表在上级创建后才出现）。创建的对象未出现在预期编号上时停止。
4. 写入其余的项：对象的设置，以及创建后才出现的系统项（如协议译稿中输出源的坩埚材料 `S**09**`，追加后才出现）。第 2 步已写入的项不重复写入。
5. 用 `Q` 逐项读回，与写入的值比较（`ParameterValue.AreEquivalent`）。

- 遇到第一个设备拒绝即停止，不回滚。设备上比文件多出的对象或条目不删除。
- 取消时返回 `Cancelled = true`（不抛出异常），已写入的项保留在设备上。
- 进度为 `RestoreProgress(Stage, Completed, Total)`：`Stage` 依次为 `写入系统设置`、`创建`（`创建 (第 2 轮)` 等）、`重新读取设备`、`写入`、`校验`（`ApplyEditsAsync` 之前另有 `检查设备`）；`Total = 0` 表示进度未知。例如只修改一项材料时，依次报告 `写入 1/1`、`校验 1/1`。

##### 4.2.3.5 恢复报告（RestoreReport）

表 11  RestoreReport 的属性

| 属性 | 含义 |
|---|---|
| `IsSuccess` | 没有错误、没有取消，且读回全部一致 |
| `Error`、`FailedId` | 停止的原因和项 |
| `FailedDuringCreate` | 失败发生在创建阶段（含其后的重新读取、重新计划），而不是写入阶段 |
| `Cancelled` | 被取消 |
| `CreatedCount`、`WrittenCount`、`PlannedWriteCount` | 创建的对象数、写入的项数、计划写入的项数（含重新计划的结果） |
| `VerifyMismatches` | 读回值与写入值不一致的项（已格式化的文本） |
| `AdditionalSkipped` | 创建后重新计划时新出现的跳过项（如新对象子列表的条数与文件不同），应作为错误提示 |

#### 4.2.4 修改设备配置接口

`IInstrumentConfigWriter` 的每个方法都带 `deviceName`（配置所属的设备，与 G03 比较），开始时用一帧 `QG03;QC09` 确认型号和 `Ready` 状态，不满足时不发送任何修改指令。方法见表 12。

表 12  IInstrumentConfigWriter 的方法

| 方法 | 发送的指令（示例） | 结果 |
|---|---|---|
| `ApplyEditsAsync(recipe, edits, progress, ct)` | 检查每项 → `QG03;QC09` → `UM0103:2.710` 等 → `QM0103` 等 | `RestoreReport` |
| `CreateObjectAsync(type, id, defaultValue, deviceName, ct)` | `AF02`，再设置默认引用 `UF0202:1` → `QF0202` | `ObjectCreation(Error, DefaultsError)` |
| `DeleteObjectAsync(id, deviceName, ct)` | `DM03` | `Result` |
| `AppendListItemAsync(listId, value, deviceName, ct)` | `AP02010700` → `QP020107FE`；`value` 不为 null 时 `UP0201070n:值` → `QP0201070n` | 新条目的 ID |
| `RemoveLastListItemAsync(listId, deviceName, ct)` | `QF0103FE` → `DF010300` → `QF0103FE`（条数应减少一条） | 被删除的条目 ID |

```csharp
var definition = ZevisionCommandSet.Find("M0103");
var value = ParameterValue.Normalize(definition, "2.71", "2.700");          // → "2.710"（与设备的小数位数一致）
var edits = new[] { new RecipeEntry { Id = "M0103", Value = value, Description = "Density" } };

var report = await config.ApplyEditsAsync(recipe, edits, progress: null, cancellationToken);
if (!report.IsSuccess)
{
    ShowError(report.Cancelled ? "已取消" : report.Error ?? string.Join(Environment.NewLine, report.VerifyMismatches));
}
// 设备不在 Ready 状态时 → "设备当前状态为 Running，只有在 Ready (空闲) 状态下才能修改配置。"
```

- `ApplyEditsAsync`：先检查每一项均可写入（`RecipeRules.IsRestorable`）、不含协议字符，且膜系的坩埚号不超过所选输出源的坩埚数量（同时修改的输出源和坩埚数量按新值计算，见 4.2.5.5）；任何一项不合格时不发送任何指令。按恢复的顺序写入，逐项读回。不创建对象：设备上不存在的项由设备拒绝。
- `CreateObjectAsync` 按 `type.NewDefault`（见 4.2.5.2）设置默认引用：膜系写入材料项 `F..02`；膜层先追加膜系列表一项（`AL..0200`），再写入其膜系（`L..02nn01`）；工艺追加膜层列表一项并写入（`AP02..0700` → `UP02..07nn`）。`defaultValue` 为 null 时只创建对象。对象创建成功但默认引用设置失败时，`Error` 为 null，`DefaultsError` 为原因（对象保留在设备上）。
- 只有 `M**`、`F**`、`L**`、`P02**` 可按编号新建和删除（由指令集检查，其他 ID 抛出 `ArgumentException`）。协议译稿未说明按编号删除（`DM03`）。按编号新建和删除材料、膜系、膜层、工艺已在实机上确认（ZEV-STR-01 的 STR-2026-10-23-01）：删除后该对象不再出现，其他对象的编号不变。
- 修改后调用方应重新读取该组（`ReadGroupAsync`）确认结果；编辑会话按此方式处理（见 4.3）。

#### 4.2.5 规则与 ID 工具

本节的类型为静态类，由纯函数组成，无状态，线程安全。

##### 4.2.5.1 RecipeRules：保存和恢复的范围

`IsRestorable(id, value, out reason)` 依次检查：

1. 值为空的项（分组标题、动作命令）不保存。
2. 组白名单：M F L P N S T I O，以及系统组中的 G0E（显示）、G0F（声音）、G12（图表）。通信（G13、G14）、时间（G0A）、复位（G0D）、锁定（G11）和工艺控制（C）不保存。
3. ID 格式（`DeviceIds.IsWellFormed`）。
4. 排除规则：列表计数（`…FE`）；传感器的运行状态；输出源的当前输出、开关挡板、切换坩埚（物理动作）；输入输出的分配和当前状态（写入输出状态会使继电器动作）；工艺运行次数；最后使用的晶振。
5. 指令集中定义为只读的项（如 `N**08`、`N**1F`）。

上述规则依据 IMC300 固件 1.8.11 的帮助表推断，未逐项在实机上验证。恢复时设备拒绝某项会停止并报告其 ID，可据此调整本节的规则。

表 13  RecipeRules 的方法

| 方法 | 用途 |
|---|---|
| `IsRestorable(id, value, out reason)` | 上述规则 |
| `SelectRestorable(entries)` | 从帮助表中挑选可保存的项（ID 重复时取第一次出现的项）；`ToRecipe` 和 `ReadGroupAsync` 均使用该方法 |
| `IsSystemSetting(id)` | N S I O T G 组：恢复时最先写入 |
| `GroupRank(id)` | 恢复顺序的排序键：N S I O T G M F L P，其他组排在最后 |
| `TryGetReference(id, value, out targetId, out kind)` | 恢复计划使用的引用：`F??02` → 材料，`L??02??01` → 膜系 |
| `FindReferencesTo(entries, objectId)` | 删除前的检查：上述两条，另加 `P02??07??` → 膜层、`P01` 和 `P02??0A` → 工艺、`S??09??` → 材料 |

```csharp
RecipeRules.IsRestorable("M0103", "2.700", out _);              // → true
RecipeRules.IsRestorable("G1303", "115200", out var reason);    // → false，"不在保存范围内的组 (工艺控制、系统、通讯、Hello 等)"
RecipeRules.IsRestorable("MFE", "3", out reason);               // → false，"列表计数"
RecipeRules.IsRestorable("S0106", "0", out reason);             // → false，"开/关挡板 (物理动作)"
RecipeRules.IsRestorable("N0108", "4", out reason);             // → false，"只读项 (协议只允许查询)"
RecipeRules.TryGetReference("F0302", "3", out var target, out var kind);   // → true，"M03"，"材料"；值 0（未选择）→ false
RecipeRules.FindReferencesTo(entries, "M03");                   // entries 含 F0102=3、F0202=1、S010901=3 → F0102、S010901
new[] { "P0101", "M0101", "G0E01", "N0101", "X01" }.OrderBy(RecipeRules.GroupRank);   // → N0101 G0E01 M0101 P0101 X01
```

删除检查比恢复计划多使用几条规则，原因是实机上工艺的膜层列表存在指向不存在膜层的值（31/32/33，而设备上只有 L01–L09）：恢复计划若使用这些规则，将跳过这些写入；删除检查只比较值是否等于要删除的对象，不受影响。坩埚材料 `S??09??`（协议译稿）属于系统设置，在创建材料之前写入，恢复计划不使用；固件 1.8.11 的设备上也没有该列表（保存的配置和抓包中均未出现）。

##### 4.2.5.2 ObjectTypes：可新建和删除的对象

表 14  可新建和删除的对象类型

| 类型 | 组 | 对象 ID | 可增删的子列表（`Lists`） | 新建后的默认引用（`NewDefault`） |
|---|---|---|---|---|
| `ObjectTypes.Material` | M | `M01` 等 | 无 | 无 |
| `ObjectTypes.Film` | F | `F01` 等 | `03` 预熔、`05` 后处理（分组标题中的 + / −） | `F..02`（材料）= 第一个材料 |
| `ObjectTypes.Layer` | L | `L01` 等 | `02` 膜系列表（为空时显示添加行） | 追加 `L..02` 一项，其 `01`（膜系）= 第一个膜系 |
| `ObjectTypes.Process` | P | `P0201` 等（`P01` 是活动工艺，不是对象） | `07` 膜层列表（每项可删除） | 追加 `P02..07` 一项 = 第一个膜层 |

对象的层级为：工艺包含膜层，膜层包含膜系，膜系包含材料。`ObjectTypes.All` 按被引用者在前的顺序排列（材料、膜系、膜层、工艺）。修改窗口、恢复计划和引用检查均按该表处理对象，不各自判断组字母。

```csharp
ObjectTypes.Find('M');                                    // → Material；不能新建的组（如 'G'）→ null
ObjectTypes.TryGetObject("F01030101", out var type, out var objectId);   // → true，Film，"F01"
ObjectTypes.TryGetObject("P020107", out type, out objectId);            // → true，Process，"P0201"
ObjectTypes.OfObjectId("P0201");                          // → Process；"P01" → null
ObjectTypes.GroupKey("F01030101");                        // → "F01"（修改窗口中的分组）
ObjectTypes.GroupKey("P020101");                          // → "P0201"
ObjectTypes.GroupKey("G0E02");                            // → "G0E"（非对象组：组字母 + 第一级编号）
ObjectTypes.Material.NextFreeId(new HashSet<string> { "M01", "M03" });  // → "M02"；编号用尽（01–FD）时为 null
ObjectTypes.Film.ListPattern(ObjectTypes.Film.Lists[0]);  // → "F**03"（用于在指令集中查找列表名称）
ObjectTypes.Film.Kind;                                    // → "膜系" / "Film"（界面语言）
```

##### 4.2.5.3 DeviceIds：ID 的组成

ID 由组字母和若干级两位十六进制编号组成。列表条目编号为 01–FD；`00` 是“在末尾追加 / 删除最后一项”的指令项，`FE` 是条数项。程序中拼接和拆分 ID 应使用以下方法，不应手写后缀。

```csharp
DeviceIds.Item("F0103", 2);        // → "F010302"  列表的第 2 项
DeviceIds.CountOf("F0103");        // → "F0103FE"  条数项；组的对象数：CountOf("M") → "MFE"
DeviceIds.AppendSlotOf("F0103");   // → "F010300"  A 追加 / D 删除最后一项
DeviceIds.NameOf("M03");           // → "M0301"    对象的名称项
DeviceIds.LastNumber("M0A");       // → 10
DeviceIds.IsItemNumber("0A");      // → true；"00"、"FE" → false
DeviceIds.IsCount("F0103FE");      // → true
DeviceIds.TryParseCount("12", out var count);   // → true，12（设备返回的十进制条数）
DeviceIds.IsWellFormed("F0103");   // → true；"F013"（不是两位一级）、"m01"（小写）→ false
```

##### 4.2.5.4 ParameterValue：值的校验、规整和比较

数字一律按 `InvariantCulture` 处理（小数点为 `.`）。

```csharp
var density = ZevisionCommandSet.Find("M0103");          // 密度 (g/cm³)，范围 0.100 ~ 100.000 g/cm³
ParameterValue.Validate(density, "2.7");                 // → null（合法）
ParameterValue.Validate(density, "abc");                 // → "应为数字 (小数点用 .)"
ParameterValue.Validate(density, "500");                 // → "超出范围 0.100 ~ 100.000 g/cm³"
ParameterValue.Validate(null, "a;b");                    // → "不能包含 ; { }"（文档未列出的项只检查协议字符）
ParameterValue.Normalize(density, " 2.7 ", "5.890");     // → "2.700"（按原值的小数位数）
ParameterValue.AreEquivalent("5.890", "5.89");           // → true（数值相等）
ParameterValue.AreEquivalent("0:00:05", "00:00:05");     // → true（秒数相等）
ParameterValue.AreEquivalent("Al", "al");                // → false（文本区分大小写）
ParameterValue.TryParseTime("1:30", out var seconds);    // → true，90（小时为 0 时可写作 M:SS）
ParameterValue.FormatTime(7205);                         // → "2:00:05"
```

- 类型和范围来自指令集（`CommandDefinition`）：数字、时间（`H:MM:SS`，小时不大于 8760，部分项有各自的上下限）、选项、引用、文本。
- 设备上的值可能超出文档给出的范围：界面只在值被修改后检查类型和范围，协议字符始终检查。
- 写入前应以 `Normalize` 规整：设备按固定小数位保存，规整后读回校验才能一致。

##### 4.2.5.5 CrucibleRule：膜系的坩埚号

膜系的坩埚号 `F**0902` 取值为 0（任意，Any），或 1 至膜系所选输出源（`F**0901`）的坩埚数量（`S**07`）。设备提供的选项为 Any 和 1 ~ 最大数量；协议译稿中的“0 = disabled”与设备不符。输出源未选择或不存在，或坩埚数量未知或为 0 时，最大坩埚号按 `DefaultCount`（1）计。

```csharp
// 实机：S01 有 6 个坩埚，S02 有 8 个；膜系 F01 选择输出源 1
Func<string, string?> valueOf = id => values.TryGetValue(id, out var v) ? v : null;
CrucibleRule.IsCrucible("F010902");                  // → true；"F010901"（输出源）→ false
CrucibleRule.MaxFor("F010902", valueOf);             // → 6；F01 未选择输出源（F010901 = 0）时 → 1
CrucibleRule.Check("F010902", "6", valueOf);         // → null（允许）；"0"（任意）始终允许
CrucibleRule.Check("F010902", "7", valueOf);         // → "坩埚 7 超出输出源 S01 的坩埚数量 (6)"
```

表 15  CrucibleRule 的使用位置

| 使用位置 | `valueOf` 取值来源 | 超出时 |
|---|---|---|
| 修改窗口的下拉框（界面层 `ParameterOptions`） | 页面中的配置 | 选项为 `0 - 任意`、1 ~ `MaxFor`；超出的当前值保留并标为 `(未知)` |
| `ApplyEditsAsync` 写入前 | 修改的新值，其余取配置 | 不发送任何指令，`Error` 为 `F010902 不能修改: 坩埚 7 超出…` |
| `RestorePlanner.Plan` | 文件中的值（恢复后），文件中没有的取设备的值 | 加入 `Skipped`，原因同上 |

只在坩埚数量已知时检查；0 和非数字的值不检查（非数字由 `ParameterValue` 校验）。

#### 4.2.6 修改窗口的编辑会话

##### 4.2.6.1 RecipeDocument

`RecipeDocument` 表示修改窗口中正在编辑的配置（`InstrumentRecipe`）及对其的操作。该类型不与设备通信，不读写文件，可单独测试。方法见表 16。

表 16  RecipeDocument 的方法

| 方法 | 用途 |
|---|---|
| `ObjectIds(group)` / `ObjectIdsIn(entries, group)` | 现有的对象 ID，如 `M01`、`P0201` |
| `ObjectName(objectId)` | 对象的名称（第 01 项），没有时为 null |
| `ListItemCount(listId)` | 列表的条数（不含 00 和 FE 项） |
| `ReplaceGroup(group, entries)` | 用重新读取的数据替换该组（保持原位置） |
| `AddObject(objectId, source)` / `RemoveObject(objectId)` | 加入（从重新读取的组中复制）或删除对象的全部项 |
| `AddListItem(listId, itemId, source)` / `RemoveListItem(itemId)` | 加入或删除列表条目及其子项 |
| `SetValues(values)` | 按 ID 修改值 |
| `Clone()` | 复制整份配置，修改副本不影响原文档 |
| `CompareObjectChange(action, objectId, before, after)` | 核对新建或删除的结果：返回 `(missing, extra)`，两者都为空表示只有预期的变化 |

##### 4.2.6.2 RecipeEditSession：两种数据来源

`RecipeEditSession.Open(recipe, path, config, recipes)` 按 `path` 选择数据来源，差异见表 17。

表 17  两种数据来源的差异

| 项目 | 实机数据（`path = null`） | 保存的配置（`path` 为文件） |
|---|---|---|
| 页面内容 | 刚读取的设备数据（`InstrumentSnapshot.ToRecipe`） | 文件内容（已通过 `IRecipeStore.Load` 校验） |
| `ReadDeviceGroupAsync`（新建、删除前） | 页面中的该组（不通信） | 读取设备上的该组（`?M` 等） |
| `UsedObjectIds`（新建时避开的编号） | 设备上的编号 | 设备和文件中的编号 |
| `RereadGroupAsync`（修改后） | 读取并替换页面中的该组 | 读取，页面不变（页面即文件） |
| `ApplyObjectChange` / `ApplyListAppend` / `ApplyListRemove` | 不处理（已由重新读取更新） | 把变化写入文件并立即保存 |
| `CheckListCountAsync`（列表增删前） | 不检查 | 设备与文件的条数应相同，否则条目编号与文件不对应 |
| `Commit(change)` | 修改页面 | 先修改副本并保存文件，成功后才修改页面 |

设备操作（`CreateObjectAsync`、`DeleteObjectAsync`、`AppendListItemAsync`、`RemoveLastListItemAsync`、`ApplyEditsAsync`）均转交 `IInstrumentConfigService`，设备名取自配置。`CreateObjectAsync` 自动带默认引用：`DefaultReferenceId(type)` 为被引用组中编号最小的对象（如 `M01`），写入的值为其十进制编号。

`Commit` 采用写时复制：保存失败时页面仍与文件一致，无需回滚代码；但设备可能已被修改，错误说明中会指出这一点。

#### 4.2.7 中英文文本

##### 4.2.7.1 Lang

```csharp
Lang.Initialize(Lang.Detect(CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture, setting));
Lang.Detect(new CultureInfo("en-US"), new CultureInfo("en-US"), "auto");   // → English
Lang.Detect(new CultureInfo("en-US"), new CultureInfo("zh-CN"), null);     // → Chinese（区域格式为中国）
var text = Lang.T("已连接", "Connected");                                  // 界面层成对给出的文本
using (Lang.Override(UiLanguage.English)) { /* 测试英文 */ }
```

- 规则：`setting` 为 `zh` / `en` 时按设置；否则系统显示语言为中文，或区域格式为中国（CN）时使用中文，其他情况使用英文。
- 未调用 `Initialize` 时为中文（现有测试依赖这一点）。
- 语言在启动时确定一次。切换语言即重新启动程序，运行中不重新翻译。

##### 4.2.7.2 TextCatalog 与 Texts.json

业务层和存储层的提示和错误文本位于翻译文件 `Texts.json`（源文件 `Zevision.Core/Localization/Texts.json`，编译时复制到每个引用 `Zevision.Core` 的项目的输出目录）：

```json
{
  "DeviceModelMismatch": { "zh": "设备型号不符:文件来自 {0},当前设备是 {1}。", "en": "Device model mismatch: the file is from {0}, the connected device is {1}." },
  "NotConnected": { "zh": "未连接。", "en": "Not connected." }
}
```

调用处使用类型化的访问器（`Zevision.Core/Texts.cs`、`Zevision.Storage/Texts.cs`，均为 `internal`），键即成员名：

```csharp
internal static class Texts
{
    public static string NotConnected => TextCatalog.Get(nameof(NotConnected));

    public static string DeviceModelMismatch(string fileDevice, string connectedDevice) =>
        TextCatalog.Format(nameof(DeviceModelMismatch), fileDevice, connectedDevice);
}

return Result.Fail(Texts.DeviceModelMismatch(recipe.DeviceName, name));
```

- 有参数的文本为 `string.Format` 格式（`{0}`、`{0:0}`，字面的大括号写作 `{{ }}`，按当前区域设置格式化）；没有参数的文本按原样使用。
- 新增文本时，应在 JSON 中增加一条（zh 和 en 均不可缺少），并在对应的 `Texts.cs` 中增加一个成员。
- `TextCatalogTests` 检查：键与访问器一一对应（`Zevision.Core` 与 `Zevision.Storage` 之间不重复）、中英文都存在、英文中没有中文、占位符与参数个数一致、每个访问器在两种语言下都能生成文本。
- 运行时缺少键或格式错误时，返回键名或原始模板，不抛出异常（由测试保证不出现此情况）。
- 文件编码为 UTF-8，可带或不带 BOM。

##### 4.2.7.3 CommandText 与 CommunicationText

两个链接库本身不依赖界面语言：指令库同时给出中英文（`TextLanguage` 参数），通信库只给出英文和错误类型。以下两个适配器按 `Lang.Current` 选择语言：

```csharp
var density = ZevisionCommandSet.Find("M0103");
CommandText.Label(density);                                  // → "密度 (g/cm³)"；英文 → "Density (g/cm^3)"
CommandText.RangeText(density);                              // → "0.100 ~ 100.000 g/cm³"
CommandText.LabelFor(ZevisionCommandSet.Find("F01030101"), "F01030101");   // → "预熔 1 · 起始斜坡/上升时间"
CommandText.ObjectKind('P');                                 // → "工艺"
ZevisionErrorCode.IllegalArgument.Localized();               // → "非法参数值"；英文 → "Illegal parameter value"
CommunicationText.ValidateValue("a;b");                      // → "不能包含 ; { }"
CommunicationText.Describe(error, command);                  // ZevisionError → 界面语言的说明
```

#### 4.2.8 通信配置（settings.json）

`ConnectionPreferences` 是 DataContract 类型，保存为 `settings.json`。缺少的字段保持默认值（`[OnDeserializing]` 先设置默认值）。字段见表 18。

表 18  settings.json 的字段

| 属性 | JSON 键 | 默认值 | 说明 |
|---|---|---|---|
| `PortName` | `portName` | 空 | 上次使用的串口 |
| `BaudRate` | `baudRate` | 115200 | |
| `ResponseTimeoutMs` | `responseTimeoutMs` | 1000 | 响应空闲超时 |
| `SerialChecksumEnabled` | `checksumEnabled` | false | RS232 的校验和（G1304） |
| `SerialLengthEnabled` | `lengthEnabled` | false | RS232 的长度字节（G1305） |
| `SerialSaveCommunicationData` | `rawCaptureEnabled` | false | RS232 连接时保存通信数据；键名沿用早期版本 |
| `TcpChecksumEnabled` | `tcpChecksumEnabled` | 同 RS232 | TCP 的校验和（G1407） |
| `TcpLengthEnabled` | `tcpLengthEnabled` | 同 RS232 | TCP 的长度字节（G1408） |
| `TcpSaveCommunicationData` | `tcpSaveCommunicationData` | 同 RS232 | TCP 连接时保存通信数据 |
| `EditSource` | `editSource` | `Device` | 修改配置的数据来源（`Device` / `File`） |
| `ConnectionType` | `connectionType` | `Serial` | `Serial` / `Tcp` |
| `TcpHost` | `tcpHost` | 空 | 设备 IP 地址；端口固定为 2101，不保存 |
| `Language` | `language` | `auto` | `auto` / `zh` / `en` |
| `DisplayFormat` | `displayFormat` | `Ascii` | `Ascii` / `Hex` / `Binary` |
| `RepeatSend` | `repeatSend` | false | |
| `RepeatIntervalMs` | `repeatIntervalMs` | 1000 | |
| `HandshakeEnabled` | `handshakeEnabled` | true | 连接时握手（并启用重发和心跳）；1.0 版的文件中没有此项，读取后为 true |
| `AutoReconnect` | `autoReconnect` | false | 连接意外中断后自动重连 |
| `LastCommand` | `lastCommand` | 空 | 最后一次从指令输入框发送的内容（转为大写后，保留换行），启动时填入输入框 |

无法识别的值（如旧版本写入或手工编辑的值）由视图模型按默认值处理。

校验和、长度字节和保存通信数据按连接方式分别保存，因为设备上 RS232（G1304/G1305）与 TCP（G1407/G1408）是独立的设置。RS232 沿用 0.9.x 版的键名；0.9.x 版的文件中没有 TCP 的三项，读取时 TCP 取与 RS232 相同的值（0.9.x 版两种连接共用一组开关），升级后界面不变。

```csharp
IPreferencesStore store = new JsonFilePreferencesStore(AppFolders.GetSettingsFilePath());
var loaded = store.Load();          // 文件不存在 → 成功，返回默认值；文件损坏 → 失败（调用方记录后使用默认值）
var preferences = loaded.IsSuccess ? loaded.Value : new ConnectionPreferences();
preferences.PortName = "COM3";
var saved = store.Save(preferences);
```

#### 4.2.9 存储层

##### 4.2.9.1 AppFolders

表 19  AppFolders 的方法

| 方法 | 位置 |
|---|---|
| `GetSettingsFilePath()` | `<程序目录>\settings.json` |
| `GetConfigFolder()` | `<程序目录>\configs`（仪器配置文件） |
| `GetDesignFolder()` | `<程序目录>\recipes`（工艺编辑，其下为 `material`、`film`、`layer`、`process` 四个目录） |
| `GetLogsFolder()` | `<程序目录>\logs`（`error.txt`、通信数据文件 `<序列号>_*.txt`） |

程序目录不可写时（如安装在 Program Files 下）改用 `文档\Zevision\…`；日志目录仍不可写时使用系统临时目录。目录不存在时自动创建。每次调用都探测一次可写性（创建并删除一个临时文件），不宜在循环中调用。

配置文件目录在 1.1.0 之前名为 `recipe`，1.1.0～1.2.0 名为 `recipes`（之后用于工艺编辑）。`GetConfigFolder()` 和 `GetDesignFolder()` 在实际尝试的每个位置（先程序目录；程序目录不可写时再 `文档\Zevision`）先调用 `MoveLegacyConfigFolder`：`configs` 尚不存在时，把 `recipes`（其中没有工艺编辑的四个目录，`DesignFileStore.HasDesignFolders`）改名为 `configs`，没有 `recipes` 时把 `recipe` 改名为 `configs`。`configs` 已存在，或改名失败（目录只读，或其中的文件被其他程序打开）时，保留旧目录。两个方法都先迁移，工艺文件不会与旧的配置文件混在 `recipes` 中。该行为由 `AppFoldersTests` 测试。

##### 4.2.9.2 RecipeFileStore：仪器配置文件

`RecipeFileStore` 实现 `IRecipeStore`。`Folder` 为 `AppFolders.GetConfigFolder()`（第一次使用时确定），`FileExtension` 为 `.json`。

文件格式（`formatVersion` 2）为缩进的 UTF-8 JSON：

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

- 保存（`Save`）：计算 MD5 并写入 `recipe.Md5`，先写入同目录的临时文件（`.tmp`），再替换目标文件。写入中途出错时原文件保持完好。
- 读取（`Load`）按顺序检查：能否读取和解析；格式版本（版本 1 没有校验值，拒绝并提示重新保存；其他版本不支持）；MD5 存在且格式正确；MD5 一致；头部字段齐全、设备名不为空；至少有一项；每项的 ID 合法（`DeviceIds.IsWellFormed`），值和说明不为 null。MD5 先于其他内容校验，任何改动（包括删除字段）都报告为“文件被修改”。
- MD5 的计算：不对 JSON 原文计算（缩进、字段顺序不影响结果）。按固定顺序把每个字段写成 `<UTF-16 长度>:<内容>\n`（null 写成 `-1:\n`），对其 UTF-8 编码求 MD5，输出 32 位小写十六进制。字段顺序：formatVersion、deviceName、modelNumber、firmwareVersion、serialNumber、savedAt、条目数，然后是每个条目的 id、value、description。修改算法或字段时，应提升 `InstrumentRecipe.CurrentFormatVersion`。
- MD5 只能发现意外的改动（手工编辑、传输损坏）。算法公开，有意篡改者可重新计算；需要防篡改时，应改用带密钥的签名（如 HMAC）。
- Windows 启用 FIPS 策略时 MD5 不可用，保存和读取返回失败并说明原因。
- 文件可带 UTF-8 BOM（Windows 记事本以 UTF-8 另存时写入 BOM）。

##### 4.2.9.3 JsonFilePreferencesStore

`JsonFilePreferencesStore` 实现 `IPreferencesStore`，读写方式与配置文件相同（DataContract JSON、临时文件加替换、可带 BOM）。文件不存在时 `Load` 成功并返回默认值；内容损坏或无法读取时返回失败。

##### 4.2.9.4 FileErrorLog

`FileErrorLog` 实现 `IErrorLog`。该类型线程安全；写入失败时放弃该条记录，不抛出异常。

```csharp
var log = new FileErrorLog(Path.Combine(AppFolders.GetLogsFolder(), "error.txt"), AppInfo.Description);   // 第二个参数为会话标题
log.Write("连接", "无法连接 COM3, 115200, 8N1: 对端口“COM3”的访问被拒绝。", exception);
```

```
==== Zevision 1.2.0  |  Microsoft Windows NT 10.0.26200.0  |  .NET CLR 4.0.30319.42000  |  64 位进程 ====
2026-10-18 23:28:29.123 [连接] 无法连接 COM3, 115200, 8N1: 对端口“COM3”的访问被拒绝。
System.UnauthorizedAccessException: …（含堆栈）
```

- 每行格式为 `yyyy-MM-dd HH:mm:ss.fff [来源] 消息`；消息截断到 500 个字符（`MaxMessageLength`），换行合并为一行；有异常时其后附加 `Exception.ToString()`。
- 每次运行的第一条记录之前写入会话标题（`==== 标题 ====`）。文件和目录在第一次写入时创建，程序正常运行时不产生文件。
- 文件超过 5 MB（`MaxFileBytes`）时转存为 `error.1.txt`（覆盖上一次的转存），再从空文件开始。转存失败（如 `error.1.txt` 被编辑器占用）时继续追加。

##### 4.2.9.5 CommunicationLogFiles：通信数据文件

`CommunicationLogFiles` 实现 `ICommunicationRecorderFactory`。每次连接由 `Create(deviceSerialNumber)` 创建一个记录，文件为日志目录中的 `<序列号>_yyyyMMdd_HHmm.txt`（`FileName`），如 `XXXXXXXX_20261021_1337.txt`；超过 30 MB 时续写到 `XXXXXXXX_20261021_1337_1.txt`、`_2.txt`…。文件永久保留，不自动清理。

- 序列号为握手读取的 G06（`DeviceIdentity.SerialNumber`）。其中的空白和不能用于文件名的字符替换为 `_`，超过 32 个字符的部分截去；为 null 或空白（未启用握手、设备未回答 G06）时为 `unknown`（`UnknownSerialNumber`）。
- 时间到分钟：同一分钟内再次连接（含自动重连）时追加到同一文件。

```csharp
var recorders = new CommunicationLogFiles(errorLog);           // 可选的第二个参数：目录（Func<string>），用于测试
var opened = recorders.Create(identity?.SerialNumber);         // 握手得到的序列号；未知时为 null，文件名为 unknown_…
if (!opened.IsSuccess) { ShowError(opened.Error); return; }   // 无法创建文件
var recorder = opened.Value;
recorder.Append(new[] { "10:00:00.000 TX {QG03}", "10:00:00.020 RX {QG03+:IMC300}" });   // 不阻塞：先缓存在内存中
// …
await recorder.CloseAsync();                                   // 断开连接：在后台写完缓存后关闭；之后的 Append 被忽略
recorder.Dispose();                                            // 程序退出：在调用线程上写完缓存后关闭（可在 CloseAsync 之后调用）
```

- 保存的内容为数据显示区的显示内容（含当时的显示格式），由界面层把每批显示的行交给 `Append`。
- 内容先缓存在内存中，缓存的 UTF-8 字节数达到 `CommunicationLogWriter.BufferLimit`（50 KB）时作为一块在后台写入并刷新；`CloseAsync` 在后台、`Dispose` 在调用线程上写完剩余内容后关闭文件。各块在文件锁中按排队顺序写入，顺序不变。
- 界面层断开连接（含连接中断）时调用 `CloseAsync`；程序退出时（`MainViewModel.Dispose` → `ConnectionLifecycle.CloseRecorder`）调用 `Dispose`，因为注销、关机时窗口不经过 `Closing`，后台写入可能来不及完成。
- 写入一块会使当前文件超过 `CommunicationLogWriter.FileSizeLimit`（30 MB）时，先关闭当前文件，在原文件名后加 `_1`、`_2`…（`NumberedPath`）打开下一个文件后再写入。一块由完整的若干批显示行组成，不拆到两个文件中；单独一块超过上限时写入空文件。编号文件已存在（同一秒内重新连接）时追加，放不下时继续使用下一个编号。`Path` 为当前文件；界面层只在连接时显示第一个文件。
- 写入失败（含无法创建下一个文件）时记入 `error.txt` 并停止记录（丢弃缓存），不影响通信。程序异常终止时，缓存中尚未写入的内容（最多约 50 KB）丢失。

##### 4.2.9.6 TextFile

`TextFile.Load(path)` 读取并检查 `Texts.json`：格式应为 `{ "键": { "zh": "…", "en": "…" } }`，每条都有 zh 和 en。失败说明为原因本身（英文或系统语言的异常消息），因为此时文本尚未读取。`TextFile.DefaultPath` 为程序目录中的 `Texts.json`。

#### 4.2.10 工艺编辑（Zevision.Core.Design）

工艺编辑窗口（`ViewModels/Design/ProcessDesignerViewModel`）编辑的内容是 `DesignDocument`：材料（`MaterialDesign`，设备上的一个材料和一个同名膜系）和工艺（`ProcessDesign`，按顺序的步骤 `ProcessStepDesign`，每一步在设备上是一个膜层）。数值都是设备的文本格式。上传一个工艺时：

1. `DesignBuilder.Build(document, process, snapshot)` 先按 `DesignRules.ValidateUpload` 检查工艺、用到的材料和设备（传感器、输出源是否存在，坩埚号是否超过输出源的坩埚数量），再取设备上空闲的最小编号（`ObjectType.NextFreeId`），生成只含新对象设置项的 `InstrumentRecipe`（文件头取自设备）和新建对象的列表 `DesignObject`。
2. `RestorePlanner.Plan(recipe, snapshot)` 得到新建对象的 `A` 指令；新对象的设置项在新建、重新读取之后写入。
3. `IInstrumentConfigWriter.ExecuteRestoreAsync` 新建对象、追加子列表条目（预熔、后处理、传感器、膜层的膜系、工艺的膜层），写入并逐项读回校验。

```csharp
var designs = DesignFileStore.CreateDefault();                 // recipes\{material,film,layer,process}、程序目录的 materials.json
var document = designs.Load().Value;
var al = new MaterialDesign { Name = "Al", Density = "2.700", ZRatio = "1.080", Sensor = 1, Source = 1 };
var process = new ProcessDesign { Name = "AR" };
process.Steps.Add(new ProcessStepDesign { MaterialId = al.Id, Rate = "5.0", Thickness = "1.000" });
document.Materials.Add(al);
document.Processes.Add(process);

var errors = DesignRules.Validate(document);                   // 保存前:名称、范围、步骤
if (errors.Count == 0 && designs.Save(document).IsSuccess)
{
    var snapshot = (await config.ReadSnapshotAsync(ct)).Value;    // ? 约 10 s
    var build = DesignBuilder.Build(document, process, snapshot);  // 复用或新建 M、F、L,新建 P02
    if (build.IsSuccess)
    {
        var plan = RestorePlanner.Plan(build.Value.Recipe, snapshot).Value;
        var report = await config.ExecuteRestoreAsync(build.Value.Recipe, plan, null, ct);
    }
}
```

- 映射：材料名称写入 `M**01` 和 `F**01`；密度、Z 比、工具因子写入 `M**03`、`M**04`、`M**07`；膜系的材料 `F**02` 为材料的编号（十进制）；传感器和晶振位置写入传感器列表第 1 项 `F**070101`、`F**070102`；输出源、坩埚、P、I、D、最大功率、最大功率选项写入 `F**0901`～`F**0907`；延迟选项写入 `F**0401`，不为 0 时（`MaterialDesign.HasDelaySettings`）还写入控制延迟时间、挡板延迟精度、挡板延迟超时、传输传感器（0 = 无，非 0 时上传前检查设备上有该传感器）、传输工具因子 `F**0402`～`F**0406`；预熔、后处理写入 `F**03**01`～`03`、`F**05**01`～`03`。新建的膜层名称为 `材料名称-工艺名称-步骤号`（`DesignBuilder.LayerName`，如 `Al-AR-1`；同一工艺中参数相同的步骤共用一个膜层，名称取第一步的），膜系列表第 1 项为该材料的膜系、速率、最终厚度（`L**020101`～`03`），速率斜坡为 `L**020106**01`～`03`。工艺写入名称、活动膜层 1（`P02**02`）、自动启动下一膜层（`P02**04`）和膜层列表 `P02**07**`（膜层的编号）。不写 `P01`（活动工艺）。
- 复用：设备上第一个（按编号）所有设置项都与要写入的值相同（`ParameterValue.AreEquivalent`：名称按字符串，数值、时间按值）、子列表条数也相同的对象被复用，不写入（`DesignObject.IsNew` 为 false）。材料比较名称、密度、Z 比、工具因子；膜系只在材料被复用时查找，比较上面的全部膜系设置项（含 `F**02` 为该材料），预熔、后处理条数相同，传感器 1 个；膜层只在膜系被复用时查找，比较膜系 1 个且为该膜系、速率、最终厚度、速率斜坡（条数和数值），不比较名称（`StepFields` 不带名称），复用的膜层在 `DesignObject.Name` 中为设备上的名称。同一工艺中材料、速率、最终厚度、速率斜坡都相同的步骤共用一个膜层（`P02**07**` 中出现多次）。工艺总是新建。
- 每次上传都新建对象，不修改设备上已有的对象；检查用的取值范围、选项和字符规则都来自指令集（`ParameterValue.Validate`）。名称（材料、工艺和生成的膜层名称）最多 `DesignRules.MaxNameLength`（30）个字符：设备最多保存 31 个字符，帮助表 `?` 只显示前 30 个，复用和删除按 `?` 读到的名称比较。
- 上传失败（`RestoreReport` 不成功、有 `AdditionalSkipped`、取消、超时）时，窗口删除本次新建的对象（`DesignObject.IsNew`，编号在上传开始时空闲）：用 `ReadGroupAsync` 读取这几组，按工艺、膜层、膜系、材料的顺序（组内编号大的在前）对仍在设备上的对象调用 `DeleteObjectAsync`，再读取确认。这一步在视图模型中（`ProcessDesignerViewModel`），Core 不回滚；`ExecuteRestoreAsync` 本身仍在第一个错误处停止，不回滚。
- `DesignDevice.EntriesOf(snapshot)` 把 `?` 快照的全部设置项转换为 `RecipeEntry`；`DesignDevice.Objects(entries, group)` 从中取出传感器和输出源（编号、名称、坩埚数量 `S**07`），用于窗口的下拉框。窗口打开时读取一次 `?`（约 10 s）。
- 设备上的内容：`DeviceContents.From(entries)`（纯函数）取出材料（名称、密度、Z 比）、膜系（名称、材料编号 `F**02`）、膜层（名称、膜系列表 `L**02**01`～`03`）和工艺（名称、膜层列表 `P02**07**`）。列表按条目 ID 取，不用数量项 `…FE`，所以分组帮助（`ReadGroupAsync` 只返回可恢复项）也可以。窗口的“设备”页和本地项的设备状态（同名的对象，`MaterialsNamed`、`ProcessesNamed`）用它；删除时用分组帮助重新读取相关的组，替换这几组的设置项（`RecipeDocument.ReplaceGroup`）；上传成功后把新建对象的设置项加入（`RecipeDocument.AddObject`），不再读取。
- 删除：`DeviceDeletion`（纯函数）找出设备上一起删除的对象，按删除顺序排列（引用别人的组在前，组内编号大的在前：按编号删除的指令协议文档没有规定，设备万一在删除后重新编号也不会删到后面的对象）。引用按 `RecipeRules.IndexReferences` 一次找出。`ForMaterialNamed`/`ForMaterials`：这些材料、`F**02` 引用它们的膜系、膜系列表含这些膜系的膜层（不论名称和膜系数量），顺序为膜层、膜系、材料；`ForProcessNamed`/`ForProcesses`：这些工艺和只被它们使用的膜层，其他工艺（或其他设置）也引用的膜层放在 `Kept`，不删除。`References` 列出要删除的集合之外对它们的引用（`RecipeRules.FindReferencesTo`），属于对象的写为“工艺 P0202 P2: P02020701 = 1”，其他写为“坩埚 1 · 材料 ID S010901 = 1”；`IsBlocked` 时窗口不删除。活动工艺（`ActiveProcessSetting` = `P01`）不算引用：它指向要删除的工艺时记在 `ActiveProcess`，窗口在确认框中说明，删除后显示重新读取的 `P01`（用户决定 2026-10-23；设备接受删除并把 `P01` 改为 0，ZEV-STR-01 的 STR-2026-10-23-05）。窗口读取相关的组（材料：`?M`、`?F`、`?L`、`?P`、`?S`；工艺：`?P`、`?L`），确认后逐个调用 `IInstrumentConfigWriter.DeleteObjectAsync`，再用 `ReadGroupAsync` 读取删除的对象所在的组，确认对象已不在，全部成功后才删除本地的材料或工艺。
- 存储接口 `IDesignStore`（实现 `DesignFileStore`）：`recipes` 下按对象分目录，每个对象一个缩进的 UTF-8 JSON 文件（`formatVersion` 1，先写临时文件再替换）：`material\名称.json`（`id`、名称、中文名、密度、Z 比、工具因子）、`film\名称.json`（`materialId`、传感器、晶振位置、输出源、坩埚、预熔、后处理、PID、最大功率及其选项、延迟选项和延迟设置）、`layer\材料名称-工艺名称-步骤号.json`（工艺中每个不同的步骤一个文件，同一工艺中相同的步骤 `DesignBuilder.IsSameLayer` 共用第一步的文件：`name` 为上传时新建的膜层名称、`materialId`、速率、最终厚度、速率斜坡）、`process\名称.json`（`id`、名称、自动启动下一膜层、按顺序的膜层文件名 `layers`、上次上传的记录）。文件名由名称得到（不允许的字符换成 `_`，空名称为 `unnamed`，系统保留名前加 `_`，重名时不区分大小写加 `_2`、`_3`…）；读取时膜系按 `materialId`（没有时按文件名）对应材料，工艺按文件名找膜层（找不到的膜层读为没有材料的步骤，保存前的检查会指出），缺少的字段取默认值。`Save` 写入全部对象（内容与现有文件相同时不重写，`JsonFile.WriteIfChanged`），再删除四个目录中这次没有写、以前读取或写入过的 `.json` 文件（窗口打开期间别处放进来的文件和其他文件不动）；第一个点之前是系统保留名的名称（如 `CON.1`）前加 `_`。`Load` 在没有文件时返回空内容，有文件损坏或格式版本高于 `DesignFileStore.CurrentFormatVersion` 时失败，原因中有该文件的路径；`LoadMaterialLibrary` 读取程序目录的 `materials.json`（IMC300 操作手册附录 A 的材料表，源文件 `docs\materials.json`：`{ "materials": [ { "formula", "material_name", "material_name_zh", "density", "z_ratio", "z_ratio_established" } ] }`，数值是 JSON 数字），密度和 Z 比转为三位小数的文本（`2.700`），没有化学式或数值的行跳过；文件不存在时为空。

### 4.3 程序设计实例

#### 4.3.1 新建对象

以下代码对应修改窗口中的“新建”操作：

```csharp
var session = RecipeEditSession.Open(recipe, path, config, recipes);
var type = ObjectTypes.Film;

var group = await session.ReadDeviceGroupAsync(type.Group, ct);            // 设备上现有的对象
if (!group.IsSuccess) { ShowError(group.Error); return; }
var before = RecipeDocument.ObjectIdsIn(group.Value, type.Group);
var id = type.NextFreeId(session.UsedObjectIds(type.Group, before));     // 如 "F02"

var created = await session.CreateObjectAsync(type, id, ct);             // AF02，并把第一个材料写入 F0202
var after = await session.RereadGroupAsync(type.Group, ct);              // 无论成功与否都重新读取：设备状态以读取结果为准
if (!after.IsSuccess) { ShowError(after.Error); return; }

var error = created.Error;
if (error == null)
{
    var (missing, extra) = RecipeDocument.CompareObjectChange(
        CommandActions.Add, id, before, RecipeDocument.ObjectIdsIn(after.Value, type.Group));
    // missing 或 extra 不为空时，设备的变化与预期不同，应提示
    error = session.ApplyObjectChange(CommandActions.Add, id, after.Value);   // 保存的配置：写入文件；失败时为原因
}
// 重新生成页面；created.DefaultsError 不为 null 时提示默认引用未设置成功
```

#### 4.3.2 其他编辑操作

- 删除对象：先调用 `RecipeRules.FindReferencesTo(session.Document.Entries, id)`，有引用时拒绝并列出引用的项；然后依次调用 `DeleteObjectAsync`、`RereadGroupAsync`，核对结果，再调用 `ApplyObjectChange(CommandActions.Delete, …)`。数据来源为保存的配置时，删除设备上不存在的对象只修改文件，不发送指令。
- 列表追加：依次调用 `CheckListCountAsync`、`AppendListItemAsync(listId, value)`、`RereadGroupAsync`、`ApplyListAppend`。预熔、后处理的一项有多个设置，`value` 传入 null 以使用设备默认值；工艺的膜层传入膜层编号。
- 删除列表中间的一项：设备只能删除最后一项。先用 `ApplyEditsAsync` 把后面的项前移一位，再调用 `RemoveLastListItemAsync`，最后调用 `ApplyListRemove(removedId, shifted)`。
- 保存修改的值：调用 `ApplyEditsAsync(edits, progress, ct)`，全部成功后调用 `Commit(d => d.SetValues(values))`。失败时页面和文件都不变。
- 新建、删除和列表增删之前，页面上不应有已勾选（未保存）的项：页面重新生成时会丢弃这些项。

### 4.4 错误检测和诊断特征

- 预期内的失败以 `Result.Error`、`RestoreReport.Error` / `FailedId`、`ObjectCreation.DefaultsError` 返回界面语言的说明（见 4.1.2）。
- 通信失败（超时、帧无效、无法解析）且发送期间收到格式不一致的帧时，说明之后附加检查帧格式设置项的提示（见 4.2.1.5）。
- 握手失败时返回原因：设备无应答、无法识别帧格式、读不到型号（见 4.2.1.7）；型号不符和设置不一致作为警告返回。
- 写入和恢复后逐项读回，不一致的项列于 `RestoreReport.VerifyMismatches`（见 4.2.3.5）；创建后新出现的跳过项列于 `AdditionalSkipped`。
- 新建、删除对象后，以 `RecipeDocument.CompareObjectChange` 核对设备的变化（见 4.2.6.1、4.3.1）。
- 读取循环的意外异常和事件处理程序抛出的异常写入 `error.txt`；拔线等 I/O 异常和心跳无应答引起的中断不写入（见 4.2.1.4）。错误日志的格式和转存规则见 4.2.9.4。
- 通信数据文件保存连接期间的显示内容，用于诊断通信问题（见 4.2.9.5）。
- 配置文件的 MD5 校验用于发现文件的意外改动（见 4.2.9.2）。

### 4.5 测试与替换实现

表 20  测试中的替换实现

| 被替换的接口 | 测试中的实现（`Zevision.Tests/Fakes`） |
|---|---|
| `IDeviceService` | `FakeDeviceService`：模拟设备的配置表，响应 `?`、组帮助和 Q/U/A/D 指令；可指定被拒绝的写入和创建（`RejectWrites`、`RejectCreates`）、追加条目的默认项（`AppendTemplate`） |
| 传输（`IDeviceTransport`） | `FakeSerialTransport`（可配合 `SimulatedImc300` 模拟设备的帧格式行为），或 ZEV-CPM-01 自定义传输部分的 `SimulatedDevice` 示例；以 `new ZevisionDeviceService(transport)` 或传输工厂创建服务 |
| `IPreferencesStore` | `MemoryPreferencesStore` |
| `ICommunicationRecorderFactory` | `NoCommunicationRecorders` |
| `IRecipeStore` | `RecipeFileStore` 加临时目录 |
| `IErrorLog` | `FileErrorLog` 加临时目录，或基于列表的实现 |

- 测试程序集在 C# 9 模块初始化器（`Zevision.Tests/TestSetup.cs`）中读取 `Texts.json` 并初始化 `TextCatalog`。
- 未初始化 `Lang` 时为中文；测试英文时使用 `Lang.Override`。
- 纯函数（`RestorePlanner`、`RecipeRules`、`RecipeDocument` 等）以构造的数据测试，不需要设备。`InstrumentSnapshot` 可由 `HelpTableEntry` 列表构造。
- 不连接设备也能运行完整流程：`ZevisionDeviceService` 加模拟传输（帧格式 `FrameOptions.Plain`）和一个按 ID 读写的表。响应 `?` 时按实机格式生成帮助表：以 `\n` 开头，每行的 ID、值各占 30 列，其后为说明，以 `\n` 结束；第一行为表头 `[CMD ID]`、`[VALUE]`、`[DESCRIPTION]`。

```csharp
var service = new ZevisionDeviceService(_ => new SimulatedDevice(table.Respond), errorLog);
await service.ConnectAsync(new ConnectionOptions(new SerialSettings("SIM", 115200), FrameOptions.Plain), ct);
var config = new InstrumentConfigService(service);
var snapshot = await config.ReadSnapshotAsync(ct);          // → IMC300 Ready
```

### 4.6 常见修改

表 21  常见修改

| 修改内容 | 修改位置 | 同时检查 |
|---|---|---|
| 新增或修改提示文本 | `Localization/Texts.json` 和 `Texts.cs`（`Zevision.Core` 或 `Zevision.Storage`） | `TextCatalogTests` |
| 某项恢复时被设备拒绝，或不应保存 | `RecipeRules` 的组白名单或 `Exclusions` | `RecipeRulesTests`、`InstrumentConfigServiceTests`（恢复计划和执行） |
| 新的引用关系（值为另一个对象的编号） | `RecipeRules.References`（恢复计划也使用）或 `DeleteCheckReferences`（只用于删除检查）；修改窗口的下拉框由指令集的引用类型决定 | 两处均需考虑：恢复计划据此跳过写入 |
| 新的可新建对象或可增删的子列表 | `ObjectTypes`（以及 `ZevisionCommandSet` 中该 ID 允许的 A/D 动作） | `ObjectTypesTests`、修改窗口的按钮 |
| 恢复的顺序 | `RecipeRules.GroupOrder` | 被引用的对象应排在前面 |
| 配置文件新增字段 | `InstrumentRecipe`（`DataMember`）、`RecipeFile.ComputeMd5`，提升 `CurrentFormatVersion`，并确定是否接受旧版本 | `RecipeFileTests` 的参考 MD5 |
| `settings.json` 新增字段 | `ConnectionPreferences`（`DataMember` 和 `SetDefaults`） | 旧文件缺少该字段时使用默认值 |
| 更换存储方式（如数据库） | 实现 `IRecipeStore` / `IPreferencesStore`，只修改 `App.OnStartup` | `LibraryBoundaryTests` |
| 新的连接方式 | 实现 `IDeviceTransport`（见 ZEV-CPM-01 自定义传输部分），在传给 `ZevisionDeviceService` 的工厂中选择 | `ZevisionDeviceServiceTests` |

### 4.7 性能与内存

表 22  性能实测（实机帮助表，固件 1.8.11，116 KB；Release，.NET Framework 4.8）

| 操作 | 实测 |
|---|---|
| 解析帮助表、`ToRecipe` 和 `RestorePlanner.Plan` | 约 6 ms（1525 行 → 940 项） |
| `RecipeFileStore.Save`（约 92 KB，含 MD5） | 约 12 ms |
| `RecipeFileStore.Load`（含 MD5 校验） | 约 3 ms |
| `InstrumentSnapshot` 常驻内存 | 约 320 KB |
| `InstrumentRecipe`（由快照生成） | 约 45 KB（字符串与快照共享） |

- 与设备通信的时间远大于计算时间：完整的 `?` 约 10 s，组帮助 0.5 s ~ 5 s。能使用组帮助或批量 `Q` 时，不宜读取完整的 `?`（修改配置写入前的检查以一帧 `QG03;QC09` 代替 `?`）。
- 规则、对象类型表、文本表均为静态只读数据，首次使用时生成；正则表达式只编译一次。
- 快照、配置和恢复计划在操作结束后即可回收；编辑会话随修改窗口关闭而释放。
- `CommunicationLogWriter` 在内存中最多缓存约 50 KB，超过时在后台写入，不占用 UI 线程；程序退出时的写入在 UI 线程上进行。

## 5 注解

表 23  术语和缩略语

| 术语 / 缩略语 | 含义 |
|---|---|
| 帮助表 | `?` 指令返回的全部设置项列表（ID、当前值、说明） |
| 组帮助 | `?M` 等只返回一组设置项的帮助指令 |
| 快照 | `InstrumentSnapshot`，一次读取得到的帮助表 |
| 配置文件 | 保存的仪器配置，即 `InstrumentRecipe` 的 JSON 文件，位于 `configs` 目录 |
| 实机数据 / 保存的配置 | 修改窗口的两种数据来源：设备当前配置 / 配置文件 |
| 帧格式 | 帧是否带长度字节和校验和（RS232：G1304/G1305；TCP：G1407/G1408） |
| 握手 | 连接后确认设备应答、确定帧格式并读取型号的过程 |
| 重发 | 传输出错后由通信库自动再次发送同一帧 |
| 心跳 | 连接空闲时定期发送的 `H01`，用于检测连接中断 |
| Ready | 设备的空闲状态（C09 的值） |
| 组装根 | 创建并连接全部服务对象的唯一位置（`App.OnStartup`） |
| 写时复制 | 先修改副本并保存，成功后才修改原对象 |
| BOM | 字节顺序标记（Byte Order Mark） |
| CPM | 计算机编程手册（Computer Programming Manual） |
| DataContract | .NET 的数据协定序列化（`System.Runtime.Serialization`） |
| FIPS | 美国联邦信息处理标准（Federal Information Processing Standards） |
| HMAC | 基于散列的消息认证码（Hash-based Message Authentication Code） |
| MD5 | 消息摘要算法第 5 版（Message-Digest Algorithm 5） |
| RS232 | 串行通信接口标准 |
| TCP | 传输控制协议（Transmission Control Protocol） |

## 附录 A（资料性）API 参考

本附录由程序集和 XML 文档注释生成，列出 `Zevision.Core` 和 `Zevision.Storage` 的全部公开类型和成员。可空注解（`T?`）未显示，以成员说明为准。条目说明取自源代码的 XML 文档注释。

### A.1 Zevision.Core

#### interface `ICommunicationRecorder` : IDisposable
保存通讯数据:把数据显示区的内容按显示的样子追加保存。只是记录用途,写入失败不影响通讯。实现可以先缓存在内存中,积累到一定大小、关闭或释放时再写入。`Dispose` 在调用线程上写完缓存和已排队的行后关闭,用于程序退出;不抛出 I/O 异常,可以在 `CloseAsync` 之后调用。
- `string Path { get; }`: 当前保存位置 (显示给用户)。实现可以在记录过程中换到新文件 (如文件超过大小上限)。
- `void Append(IReadOnlyList<string> lines)`: 追加一批行 (一次通讯显示的全部行)。不阻塞调用方,不抛出 I/O 异常。
- `Task CloseAsync()`: 在后台写完缓存和已排队的行后关闭 (断开连接时)。之后的 `Append` 被忽略。
- `void Dispose()`: 在调用线程上写完缓存和已排队的行后关闭 (程序退出时)。

#### interface `ICommunicationRecorderFactory`
为一次连接创建 `ICommunicationRecorder`。
- `Result<ICommunicationRecorder> Create(string deviceSerialNumber)`: 创建新的记录 (如以设备序列号和当前时间命名的文件)。无法创建时返回失败,原因可直接显示。

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
- `const string SerialNumberId`: 系统序列号的 ID。
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
- `static ILookup<string, RecipeEntry> IndexReferences(IEnumerable<RecipeEntry> entries)`: 一次找出全部引用 (规则同 `FindReferencesTo`):被引用对象的 ID → 引用它的设置项;检查多个对象时用它。
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

#### class `DesignDocument`
工艺编辑的内容:用户定义的材料和工艺。上传一个工艺时由 `DesignBuilder` 转换为设备上的材料、膜系、膜层和工艺。保存的位置和格式由 `IDesignStore` 决定。
- `List<MaterialDesign> Materials { get; set; }`: 材料。
- `List<ProcessDesign> Processes { get; set; }`: 工艺。
- `MaterialDesign FindMaterial(string id)`: 按 `Id` 找材料;没有时为 null。

#### class `MaterialDesign`
一种材料:设备上的材料加膜系,两者同名。新建时有两段预熔、一段后处理,PID 为 10/0/0,坩埚为 0 (任意)。
- `string Id`、`Name`、`Density`、`ZRatio`、`Tooling`、`CrystalPosition`、`PTerm`、`ITerm`、`DTerm`、`MaxPower`、`MaxPowerOption`、`DelayOption`、`ControlDelayTime`、`ShutterDelayAccuracy`、`ShutterDelayTimeout`、`TransferTooling { get; set; }`: 设置项 (设备的文本格式)。
- `string ChineseName { get; set; }`: 中文名 (从材料库填入),只在本地显示,不写入设备。
- `int Sensor`、`Source`、`Crucible`、`TransferSensor { get; set; }`: 传感器、输出源编号,坩埚号,传输传感器编号 (0 = 无)。
- `bool HasDelaySettings { get; }`: 延迟选项不为 0:上传时写入延迟设置 (`F**0402`~`F**0406`),窗口显示这些设置。
- `List<ConditionStep> PreConditions`、`PostConditions { get; set; }`: 预熔、后处理 (`RampTime`、`RampPower`、`HoldTime`)。
- `static string NewId()`: 新的标识。

#### class `ProcessDesign`
一个工艺。
- `string Id`、`Name { get; set; }`: 标识、名称。
- `bool AutoStartNext { get; set; }`: 自动启动下一膜层,默认 true。
- `List<ProcessStepDesign> Steps { get; set; }`: 步骤 (`MaterialId`、`Rate`、`Thickness`、`RateRamps`:`StartThickness`、`RampTime`、`NewRate`)。
- `string LastUpload { get; set; }`: 上次上传的记录 (时间和新建的对象),只用于显示。

#### record `MaterialLibraryEntry`
材料库中的一项 (IMC300 操作手册附录 A 的材料表)。
- `MaterialLibraryEntry(string Formula, string Name, string ChineseName, string Density, string ZRatio, bool ZRatioEstablished)`: 化学式 (选择后作为材料名称)、英文名称、中文名称、密度和 Z 比 (三位小数的文本)、Z 比是否已确定 (手册中标 * 的为 false,按 1.000)。

#### record `DesignObject`
上传一个工艺用到的设备对象。
- `DesignObject(string Id, char Group, string Name, bool IsNew)`: 对象 ID (如 `M0B`)、组字母 (M、F、L、P)、名称、是否新建 (false 为复用设备上已有、名称和参数都相同的对象)。

#### record `DeviceDeletion`
在设备上一起删除的对象 (工艺编辑中删除材料或工艺时)。纯函数。
- `DeviceDeletion(IReadOnlyList<DesignObject> Objects, IReadOnlyList<DesignObject> Kept, IReadOnlyList<string> References)`: 要删除的对象 (按删除顺序:工艺、膜层、膜系、材料,组内编号大的在前);删除工艺时保留的膜层 (其他工艺也在用);设备上其他设置对要删除的对象的引用 (不含活动工艺)。
- `const string ActiveProcessSetting`: `P01`。`string? ActiveProcess { get; init; }`: 要删除的工艺中设备的活动工艺 (如 `P0201`),没有时为 null。
- `bool IsBlocked { get; }`: 有引用,不能删除。
- `static DeviceDeletion ForMaterialNamed(string name, IReadOnlyList<RecipeEntry> entries)`、`ForMaterials(IEnumerable<string> materialIds, IReadOnlyList<RecipeEntry> entries)`: 这些材料 (按名称时为同名的全部材料)、引用它们的膜系、用到这些膜系的膜层。
- `static DeviceDeletion ForProcessNamed(string name, IReadOnlyList<RecipeEntry> entries)`、`ForProcesses(IEnumerable<string> processIds, IReadOnlyList<RecipeEntry> entries)`: 这些工艺和只被它们使用的膜层。

#### class `DeviceContents`
设备上的材料、膜系、膜层和工艺,从帮助表的设置项中取出 (列表按条目 ID,不用数量项)。纯函数。
- `static DeviceContents From(IEnumerable<RecipeEntry> entries)`、`static DeviceContents Empty { get; }`: 创建;空的内容。
- `IReadOnlyList<DeviceMaterial> Materials`、`IReadOnlyList<DeviceFilm> Films`、`IReadOnlyList<DeviceLayer> Layers`、`IReadOnlyList<DeviceProcess> Processes { get; }`: 按 ID 排列。`DeviceMaterial(Id, Name, Density, ZRatio)`、`DeviceFilm(Id, Name, int Material)`、`DeviceLayer(Id, Name, IReadOnlyList<DeviceLayerFilm> Films)` (`DeviceLayerFilm(int Film, string Rate, string Thickness)`)、`DeviceProcess(Id, Name, IReadOnlyList<int> Layers)`;编号为十进制,0 = 未选择。
- `DeviceMaterial? FindMaterial(int number)`、`DeviceFilm? FindFilm(int number)`、`DeviceLayer? FindLayer(int number)`: 按编号找对象;没有时为 null。
- `IReadOnlyList<DeviceFilm> FilmsOf(DeviceMaterial material)`: 引用该材料的膜系。
- `IReadOnlyList<DeviceMaterial> MaterialsNamed(string? name)`、`IReadOnlyList<DeviceProcess> ProcessesNamed(string? name)`: 同名的材料、工艺 (去掉首尾空格,区分大小写;空名称时没有)。

#### record `DesignBuild`
一个工艺的上传内容。
- `DesignBuild(InstrumentRecipe Recipe, IReadOnlyList<DesignObject> Objects)`: 只含新建对象的配置;用到的对象 (新建和复用的),按材料、膜系、膜层、工艺排列。

#### static class `DesignBuilder`
把工艺转换为设备上的对象:复用名称和参数都相同的材料、膜系、膜层,其余新建。纯函数。
- `const string LayerNameSeparator`: `-`。
- `static Result<DesignBuild> Build(DesignDocument document, ProcessDesign process, InstrumentSnapshot snapshot)`: 检查 (`ValidateUpload`)、找可复用的对象、为其余对象分配空闲编号、生成设置项;失败时原因每行一条。
- `static IReadOnlyList<MaterialDesign> MaterialsOf(DesignDocument document, ProcessDesign process)`: 工艺用到的材料,按第一次出现的顺序。
- `static string LayerName(MaterialDesign material, ProcessDesign process, int stepNumber)`: 新建的膜层名称 `材料名称-工艺名称-步骤号`,如 `Al-AR-1`。
- `static bool IsSameLayer(ProcessStepDesign a, ProcessStepDesign b)`: 两步是否是同一个膜层 (材料、速率、最终厚度、速率斜坡都相同,数值按值比较);上传时共用一个膜层,保存时共用一个膜层文件。

#### static class `DesignRules`
材料和工艺的检查;每条错误为"对象: 设置项: 原因"。
- `static IReadOnlyList<string> Validate(DesignDocument document)`: 保存前检查全部内容 (名称不能为空、不能重名,数值在设备范围内,工艺至少一步且每步选了材料)。
- `static IReadOnlyList<string> ValidateUpload(DesignDocument document, ProcessDesign process, InstrumentSnapshot snapshot)`: 上传前检查工艺、用到的材料和设备 (传感器、输出源、坩埚数量)。
- `static string DisplayName(string name)`: 显示名称 (空时为"(未命名)")。
- `const int StepDecimals`: 速率和厚度 (步骤的速率、最终厚度,速率斜坡的起始厚度、新速率) 最多的小数位数 (3);`Validate` 和 `ValidateUpload` 也检查。
- `const int MaxNameLength`: 名称 (材料、膜系、工艺,以及生成的膜层名称) 最多的字符数 (30);`Validate` 检查名称和每个工艺第一次出现的步骤生成的膜层名称。
- `static string? CheckStepValue(StepField field, string? value)`: 工艺一步中一个输入值的问题 (与保存前的检查相同),用于输入框的提示;合法或为空时为 null。`StepField`:`Rate`、`Thickness`、`RampStartThickness`、`RampTime`、`RampNewRate`。

#### static class `DesignDevice`
从设备的设置项中取出传感器和输出源。
- `const char SensorGroup`、`SourceGroup`: `N`、`S`。
- `static List<RecipeEntry> EntriesOf(InstrumentSnapshot snapshot)`: 快照的全部设置项 (含数量项、只读项);ID 重复时取第一次出现的。
- `static IReadOnlyList<DeviceObjectInfo> Objects(IEnumerable<RecipeEntry> entries, char group)`: 该组的对象 (`DeviceObjectInfo(int Number, string Name, int CrucibleCount)`),按编号排列。

#### interface `IDesignStore`
工艺编辑的存储。
- `string Path { get; }`: 材料和工艺的保存位置 (目录)。
- `Result<DesignDocument> Load()`: 读取;还没有保存过时为空的内容,有文件损坏或格式版本不受支持时失败。
- `Result Save(DesignDocument document)`: 保存全部材料和工艺 (每个文件先写临时文件再替换;删除的对象的文件随之删除)。
- `Result<IReadOnlyList<MaterialLibraryEntry>> LoadMaterialLibrary()`: 读取材料库;文件不存在时为空。

#### class `CommandsFailedEventArgs` : EventArgs
`CommandsFailed` 的参数:一次发送中没有得到成功响应的指令数。
- `CommandsFailedEventArgs(string command, int count)`: 创建参数。
- `string Command { get; }`: 发送的指令 (多条时用 `;` 分隔)。
- `int Count { get; }`: 其中没有得到成功响应的指令数 (大于零)。

#### static class `DeviceServiceExtensions`
`IDeviceService` 的便捷方法。
- `static Task<Result<ZevisionReply>> SendAsync(IDeviceService device, string command, CancellationToken cancellationToken)`: 发送命令并等待响应,使用连接设置的响应超时。

#### record `DeviceIdentity`
握手 (`HandshakeAsync`) 得到的设备信息。
- `DeviceIdentity(string Hello, string Model, string SerialNumber, FrameOptions Frame, IReadOnlyList<string> Warnings)`: 握手 (`HandshakeAsync`) 得到的设备信息。
- `const string ExpectedModel`: 本程序适用的设备型号。
- `string Hello { get; init; }`: `H01` 的回答,如 `Zevision IMC300 1.8.11.202606101229`。
- `string Model { get; init; }`: 设备型号 (G03),如 `IMC300`。
- `string SerialNumber { get; init; }`: 系统序列号 (G06),如 `XXXXXXXX`;设备未回答或为空时为 null。
- `FrameOptions Frame { get; init; }`: 设备在这个接口上使用的帧格式,当前连接已改用它。
- `IReadOnlyList<string> Warnings { get; init; }`: 不妨碍通讯、但应当提示的情况 (界面语言),如型号不是 IMC300。

#### static class `FrameFormatDetector`
从设备的回答识别它在这个接口上使用的帧格式 (长度字节、校验和)。
- `static IReadOnlyList<FrameOptions> All { get; }`: 所有帧格式,按结构由强到弱排列:带长度、校验和的格式几乎不会把其他格式的数据当成有效帧, 而不带两者的格式什么都能读,所以识别时依次尝试,先成功的为准。
- `static FrameOptions Detect(byte[] received)`: 识别收到的字节 (按收到的顺序拼接) 使用的帧格式:按 `All` 的顺序,第一个能把它们解成 至少一个响应帧、且每一帧都有效并能按响应格式解析的格式 (起始符之前的帧外数据不计)。无法识别时为 null。

#### static class `FrameSettings`
设备上决定帧格式的设置项:RS232 为 G1304 (校验和)、G1305 (长度字节),TCP 为 G1407、G1408 (两个接口各自独立)。
- `static ValueTuple<string, string> Ids(ConnectionEndpoint endpoint)`: 设置项的 ID:TCP 为 G1407 (校验和)、G1408 (长度),RS232 为 G1304、G1305。
- `static FrameOptions AfterUpdates(ConnectionEndpoint endpoint, FrameOptions current, IEnumerable<string> commands, IReadOnlyList<ZevisionResponse> responses)`: 一帧指令修改了当前接口的帧格式设置 (如 `UG1304:1`) 并被设备接受时,修改后的帧格式;没有改变时为 null。

#### static class `FramingCheck`
判断收到的帧是否说明设备的帧格式 (长度字节) 与程序的设置不同,通讯出错时据此提示检查设置。
- `static FramingMismatch Check(ZevisionFrame frame, FrameOptions options)`: 检查收到的帧。

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
- `event EventHandler<ConnectionLostEventArgs> ConnectionLost`: 连接因传输错误 (串口拔出、TCP 断开等) 或心跳无应答意外中断时触发。主动断开不触发。
- `event EventHandler<RetryEventArgs> Retrying`: 一次发送传输出错、即将重发时触发 (见 `RetryCount`;不显示的发送不触发)。
- `event EventHandler<CommandsFailedEventArgs> CommandsFailed`: 一次发送结束,其中有指令没有得到成功响应时触发 (发送方的线程)。设备的否定响应 (含不回显的 `-C`) 计该指令;发出后没有有效响应 (超时、写入失败、响应帧无效或无法解析) 计帧中全部指令;重发后成功的、未发出的、等待时连接断开的不计。不显示的发送和握手不触发。
- `Task<Result> ConnectAsync(ConnectionOptions options, CancellationToken cancellationToken)`: 打开连接 (串口或 TCP) 并开始接收。连接参数 (连接目标、帧格式、响应超时、重试、心跳) 在连接期间不变, 帧格式可由 `HandshakeAsync` 改为设备的实际设置。
- `Task<Result<DeviceIdentity>> HandshakeAsync(FrameOptions preferred, CancellationToken cancellationToken)`: 握手:按设备的回答确定它在这个接口上使用的帧格式并改用它 (先试 `preferred`), 再读取型号 (G03) 和帧格式设置。期间的收发不显示。连接后,以及设备的帧格式设置被修改后调用。
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

#### static class `ReconnectPolicy`
连接中断后自动重连的等待时间:指数退避 1 → 2 → 4 → … 秒,上限 `MaxDelay`。
- `static readonly TimeSpan MaxDelay`: 两次重连之间最长的等待。
- `static TimeSpan DelayBefore(int attempt)`: 第 `attempt` 次重连前的等待:1、2、4、8、16、32 秒,之后都是 60 秒。

#### class `ZevisionDeviceService` : IDeviceService, IDisposable
`IDeviceService` 的实现:每次连接按当时的通讯参数创建一个 `ZevisionClient` (通讯库), 转发它的事件,把它的英文结果转换为界面语言的说明 (`CommunicationText`), 并把通讯库交出的意外异常 (读取循环出错、事件处理程序出错) 写入错误日志。
- `ZevisionDeviceService(Func<ConnectionEndpoint, IDeviceTransport> transportFactory, IErrorLog errorLog = null)`: 创建服务。每次连接时用 `transportFactory` 按连接目标 (串口或 TCP) 创建传输, 断开时关闭。
- `ZevisionDeviceService(IDeviceTransport transport, IErrorLog errorLog = null)`: 用固定的传输创建服务 (每次连接都使用同一个实例,用于测试)。
- `bool IsConnected { get; }`: 同接口/基类的说明。
- `event EventHandler<FrameSentEventArgs> FrameSent`: 同接口/基类的说明。
- `event EventHandler<FrameReceivedEventArgs> FrameReceived`: 同接口/基类的说明。
- `event EventHandler<ConnectionLostEventArgs> ConnectionLost`: 同接口/基类的说明。
- `event EventHandler<RetryEventArgs> Retrying`: 同接口/基类的说明。
- `event EventHandler<CommandsFailedEventArgs> CommandsFailed`: 同接口/基类的说明。
- `Task<Result> ConnectAsync(ConnectionOptions options, CancellationToken cancellationToken)`: 同接口/基类的说明。
- `Task<Result<DeviceIdentity>> HandshakeAsync(FrameOptions preferred, CancellationToken cancellationToken)`: 同接口/基类的说明。
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
- `static string DescribeRetry(RetryEventArgs e)`: 重发的说明,如"第 1/2 次重发 QG01: 等待响应超时 …"。
- `static string DescribeConnectionLost(ConnectionLostEventArgs e, int heartbeatMissLimit)`: 连接中断的原因:心跳无应答时为"设备连续 n 次没有应答心跳",否则为异常的说明 (如系统给出的串口错误)。
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
- `bool HandshakeEnabled { get; set; }`: 连接时是否握手 (默认是):确认设备应答、按设备的实际设置确定帧格式、读取型号,并启用重发和心跳。 关闭时只打开通道 (帧格式按界面设置,不重发、不发心跳),用于调试帧格式不一致等情况。
- `bool AutoReconnect { get; set; }`: 连接意外中断后是否自动重连 (默认否)。
- `string LastCommand { get; set; }`: 上次从指令输入框发送的指令 (发送时输入框的内容,可含换行;默认空)。启动时填入输入框。

#### interface `IPreferencesStore`
读写持久化的通讯配置。
- `Result<ConnectionPreferences> Load()`: 读取配置。文件不存在 (首次运行) 时成功返回默认值;文件损坏或无法读取时返回失败,调用方应使用默认值并提示。
- `Result Save(ConnectionPreferences preferences)`: 保存配置。

### A.2 Zevision.Storage

#### static class `AppFolders`
程序数据位置:通讯配置 (settings.json)、仪器配置文件 (recipe)、日志 (logs)。
- `static string GetSettingsFilePath()`: 通讯配置文件路径。
- `static string GetConfigFolder()`: 仪器配置文件目录 (`configs`);旧版本的 `recipes`、`recipe` 先改名为它。
- `static string GetLogsFolder()`: 日志目录 (收发记录、error.txt)。
- `static string GetDesignFolder()`: 工艺编辑的文件目录 (`recipes`,材料和工艺);取用前同样迁移旧的配置文件目录。

#### class `CommunicationLogFiles` : ICommunicationRecorderFactory
为每次连接在日志目录中创建一个通讯数据文件 (`CommunicationLogWriter`):设备序列号、日期、时间 (到分钟), 如 `XXXXXXXX_20261021_1337.txt`;序列号未知时为 `UnknownSerialNumber`。 同一分钟内再次连接 (含自动重连) 时追加到同一文件。超过 30 MB 时续写到 `XXXXXXXX_20261021_1337_1.txt`、`_2`…。 文件永久保留,不自动清理。
- `CommunicationLogFiles(IErrorLog errorLog, Func<string> folder = null)`: 创建工厂。
- `const string UnknownSerialNumber`: 设备序列号未知 (未启用握手、设备未回答 G06) 时文件名中代替它的文字。
- `Result<ICommunicationRecorder> Create(string deviceSerialNumber)`: 同接口/基类的说明。

#### class `DesignFileStore` : IDesignStore
工艺编辑的文件,按设备对象分目录保存在 `recipes` 下,每个对象一个 JSON 文件:`material\名称.json`、`film\名称.json`、`layer\材料名称-工艺名称-步骤号.json` (工艺中每个不同的步骤一个)、`process\名称.json`。保存时只重写内容有变化的文件,并删除这四个目录中读取或写入过、不再对应任何对象的 `.json` 文件。材料库是程序旁边的 `materials.json` (只读,源文件 `docs\materials.json`)。
- `DesignFileStore(string folder, string materialLibraryPath)`: 创建存储;`folder` 为 `recipes` 目录。
- `static DesignFileStore CreateDefault()`: 默认位置 (`GetDesignFolder`,程序目录的材料库)。
- `const string MaterialFolder`、`FilmFolder`、`LayerFolder`、`ProcessFolder`、`MaterialLibraryFileName`: `material`、`film`、`layer`、`process`、`materials.json`。
- `const int CurrentFormatVersion`: 当前的文件格式版本 (1)。
- `static IReadOnlyList<string> ObjectFolders { get; }`: 四个对象目录的名称。
- `string Path { get; }`、`Result<DesignDocument> Load()`、`Result Save(DesignDocument document)`、`Result<IReadOnlyList<MaterialLibraryEntry>> LoadMaterialLibrary()`: 同接口的说明。

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
`IRecipeStore` 的实现:文件格式见 `RecipeFile`,默认目录见 `GetConfigFolder`。
- `RecipeFileStore()`: 默认构造函数。
- `string Folder { get; }`: 同接口/基类的说明。
- `string FileExtension { get; }`: 同接口/基类的说明。
- `Result Save(InstrumentRecipe recipe, string path)`: 同接口/基类的说明。
- `Result<InstrumentRecipe> Load(string path)`: 同接口/基类的说明。

#### static class `TextFile`
读取翻译文件 (`FileName`,程序旁边),交给 `Initialize`。 文件为 UTF-8 JSON (可以带 BOM):`{ "键": { "zh": "中文", "en": "English" } }`,每条都必须有 zh 和 en。
- `static string DefaultPath { get; }`: 程序旁边的翻译文件路径。
- `static Result<IReadOnlyDictionary<string, LocalizedText>> Load(string path)`: 读取并检查文件。失败时的说明是原因本身 (英文或系统语言的异常消息):文本还没有读到,调用方自己给出提示。
