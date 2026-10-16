# Zevision 链接库使用说明

INFICON IMC300 Zevision 协议的两个 .NET 链接库,供生产程序使用。作者:martin.liang@inficon.com。库版本 1.0.0 (独立于 Zevision 应用版本)。

| 文件 | 内容 | 依赖 |
|---|---|---|
| `Zevision.Communication.dll` | 通讯:RS232/TCP 传输、帧编解码 (校验和、长度、多包重组)、响应解析、请求/响应对应 | 只有 .NET Framework |
| `Zevision.Commands.dll` | 指令生成:全部指令的命令模式、动作、中英文名称、单位、范围、选项;生成命令字符串 | `Zevision.Communication.dll` |

- 目标框架 .NET Framework 4.7.2 (4.7.2 及以上,含 4.8)。两个 dll 和对应的 `.xml` (IntelliSense 文档) 放在一起引用。
- 两个库**只做**指令生成、数据收发和解析,不写日志、不读写文件、不弹界面。需要记录的内容都通过事件或返回值交给调用方。
- 通讯库的说明和异常信息只有英文。.NET 和 Windows 自己的异常信息 (如端口不存在) 按系统语言给出,原样附在说明后面。
- 示例程序:`samples/Zevision.Sample/Program.cs` (连接、握手、查询,只读不写)。

## 快速开始

```csharp
using Zevision.Commands;
using Zevision.Communication;

// 1. 通讯参数:连接目标 + 帧格式 + 响应空闲超时 (默认 1 s)
var options = new ConnectionOptions(
    new SerialSettings("COM3", 115200),               // 或 new TcpSettings("192.168.0.10", TcpSettings.DefaultPort)
    new FrameOptions(checksumEnabled: true, lengthEnabled: true),
    TimeSpan.FromSeconds(1));

// 2. 一个实例对应一台设备
using var client = new ZevisionClient(options);
client.ConnectionLost += (s, e) => log.Warn("Connection lost", e.Exception);   // 后台线程
client.HandlerFailed += (s, e) => log.Error(e.EventName + " handler failed", e.Exception);

var connected = await client.ConnectAsync(ct);
if (!connected.IsSuccess)
{
    // connected.Error.Kind == ConnectFailed, connected.Error.Exception 是原始异常
    return;
}

// 3. 用指令库生成命令,用通讯库发送
var reply = await client.SendAsync(ZevisionCommandSet.Query("M0103"), ct);      // "QM0103"
if (reply.IsSuccess)
{
    var response = reply.Value.Responses[0];
    if (response.IsSuccess)
    {
        var density = decimal.Parse(response.Data!, CultureInfo.InvariantCulture);
    }
    else
    {
        // 设备拒绝:response.ErrorCode (IllegalArgument 等),response.ErrorCode.Describe() 为英文说明
    }
}
else
{
    // 通讯失败:reply.Error.Kind (Timeout、NotConnected…),reply.Error.Message 为英文说明
}

await client.DisconnectAsync();
```

## Zevision.Communication

### ZevisionClient

| 成员 | 说明 |
|---|---|
| `ZevisionClient(ConnectionOptions options)` | 按 `options.Endpoint` 的类型打开串口或 TCP |
| `ZevisionClient(ConnectionOptions, Func<ConnectionEndpoint, IDeviceTransport>)` | 自定义传输 (测试、其他连接方式);每次连接调用一次工厂 |
| `Options` | 构造时的通讯参数,之后不变 |
| `IsConnected` | 是否已连接 |
| `ConnectAsync(ct)` | 打开连接并开始接收。失败返回 `AlreadyConnected` / `ConnectFailed`;取消时抛 `OperationCanceledException` |
| `DisconnectAsync()` | 关闭连接,等待的请求以 `Disconnected` 结束。断开后可以用同一参数再次 `ConnectAsync` |
| `SendAsync(command, ct)` | 发送命令并等待响应。一帧可以有多条命令 (`QG01;QG1304`),响应按顺序对应 |
| `SendAsync(command, minimumResponseTimeout, ct)` | 本次请求的空闲超时至少为给定值,用于 `?` (完整帮助约 116 KB,建议 ≥ 2 s) |
| `Dispose()` | 断开并释放;之后 `ConnectAsync` 抛 `ObjectDisposedException` |

- 同一时间只有一个请求在等待响应,并发调用自动排队。请求按命令回显与响应对应:超时后迟到的响应不会被当成下一条命令的响应,而是作为未请求数据报告。
- 响应超时是**空闲**超时:从发送或最近一次收到字节算起。大响应只要还在传输就不会超时。
- 帧的长度或校验和字节等于 `{ } < >` 时,库自动在命令后追加 `;H01` (设备会把这样的字节当成分隔符),多出的 H01 响应不返回。

### 事件 (全部在后台线程触发)

| 事件 | 时机 |
|---|---|
| `FrameSent` | 每帧发送前:`Command` (实际发送的命令) 和 `Raw` (完整帧字节) |
| `FrameReceived` | 每收到一帧,含错误帧、帧外数据和超时时未收完的数据;`IsSolicited` = 是否是当前请求的响应 |
| `ConnectionLost` | 连接意外中断 (拔线、对端关闭、读取出错),主动断开不触发。`Exception` 不是 `IOException` 时说明读取出现了意外错误,应记录 |
| `HandlerFailed` | 上面三个事件的处理程序抛出异常。异常不会中断通讯;这个事件自身的处理程序抛出的异常被忽略 |

界面程序要在事件里切换到 UI 线程。

### 结果和错误

预期内的失败不抛异常,以 `ZevisionResult` / `ZevisionResult<T>` 返回:`IsSuccess`、`Value` (失败时访问抛 `InvalidOperationException`)、`Error`。

`ZevisionError`:`Kind`、`Message` (英文) 和生成本地化提示所需的数据。

| `Kind` | 含义 | 附带数据 |
|---|---|---|
| `AlreadyConnected` | 已经连接 | |
| `ConnectFailed` | 端口不存在或被占用、连接被拒绝、TCP 连接超时 (5 s,`TimeoutException`) | `Exception`、`Endpoint` |
| `NotConnected` | 未连接 | |
| `InvalidCommand` | 命令为空、含非可打印 ASCII 或 `{ }`、超过 57000 字节;没有发送 | `Exception` |
| `SendFailed` | 写入失败 | `Exception` |
| `Timeout` | 空闲超时 | `Timeout`、`ReceivedIncompleteData` |
| `Disconnected` | 等待响应时连接断开 | |
| `InvalidFrame` | 响应帧校验和、长度等错误 | `FrameError` |
| `UnparseableResponse` | 帧有效但不是 Zevision 响应格式 | `DataLength` |

参数错误 (null、不支持的连接类型等) 抛 `ArgumentException` 等异常。

设备返回的错误 (`-A`、`-B`…) 不是通讯失败:`SendAsync` 成功,对应的 `ZevisionResponse.IsSuccess` 为 false,`ErrorCode` 给出类型。

### 其他公开类型

- `ProtocolText`:命令和设置值允许的字符。`CheckCommand` / `CheckValue` 返回 `ProtocolTextError`,`ValidateCommand` / `ValidateValue` 返回英文说明。
- `ZevisionFrameCodec`、`ZevisionFrameDecoder`、`ZevisionResponseParser`:不经过 `ZevisionClient` 自己处理字节流时使用。
- `HelpTableParser`:解析 `?` (或组帮助 `?M`) 返回的帮助表,得到每项的 ID、当前值和说明。

## Zevision.Commands

```csharp
ZevisionCommandSet.Query("M0103");                    // "QM0103"
ZevisionCommandSet.Update("M0103", "2.700");          // "UM0103:2.700"
ZevisionCommandSet.Add("M03");                        // "AM03" (按编号创建材料)

var density = ZevisionCommandSet.Find("M0103")!;      // 指令 M**03
density.Build(CommandActions.Update, "2.700", 10);    // "UM0A03:2.700" (编号为十进制,写成两位十六进制)
density.Min; density.Max; density.IsInteger;          // 0.100、100.000
density.GetLabel(TextLanguage.Chinese);               // "密度 (g/cm³)"
density.GetLabel(TextLanguage.English);               // "Density (g/cm^3)"
density.GetRangeText(TextLanguage.English);           // "0.100 ~ 100.000 g/cm³"

ZevisionCommandSet.GetCommands(CommandGroup.M);       // 材料组的全部指令
ZevisionCommandSet.Find("O0103")!.Options;            // 固定选项 (值 + 中英文含义)
```

- `Build` / `Query` / `Update` / `Add` 检查 ID 格式、动作是否支持、数据是否允许,不检查取值范围 (`Min` / `Max` / `Options` 由调用方检查)。错误时抛 `ArgumentException`,说明为英文。
- 文档没有列出的 ID (如设备的 Weight、Ramp Time) 只检查格式,由设备判断。
- 名称、单位、范围、选项都同时有中英文,用 `TextLanguage` 选择;英文名称取自设备帮助表 (固件 1.8.11)。

## 设备特性 (IMC300 固件 1.8.11 实测)

- 帧格式必须与设备一致:串口看 G1304 (校验和)、G1305 (长度),TCP 看 G1407、G1408 (G1406 须为 0 = Zevision)。TCP 端口 2101。
- 对象编号不连续 (如材料 M01–M0A、M0C、M10),数量 (`MFE`) 不能推出有哪些编号;用 `?M` 组帮助读取已有的对象。编号只用 01–FD;`FF` 是特殊项,不要查询。
- 查询不存在的对象返回 `-D`,存在对象的不存在字段返回 `-A`;小写命令返回 `-A`。
- 列表只能在末尾添加和删除 (`A<列表>00` / `D<列表>00`)。
- 完整帮助 `?` 约 116 KB,分 3 包传输,约 10 s (115200 bps)。组帮助 `?M` 约 0.5 s。
- 说明文字是 UTF-8 (如 `Å/s`)。

## 线程和资源

- 所有 I/O 都是异步的,`ConfigureAwait(false)`;可以在任何线程调用。
- 串口断开时 .NET Framework 的挂起读取会以 `IOException` 结束,调试器输出窗口会看到一次已处理的首次机会异常,这是正常的。
- 程序退出前调用 `DisconnectAsync` 或 `Dispose`,释放串口或套接字。
