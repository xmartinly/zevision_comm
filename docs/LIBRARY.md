# Zevision 链接库调用文档

INFICON IMC300 Zevision 协议的两个 .NET 链接库,供生产程序使用。作者:martin.liang@inficon.com。

| 文件 | 内容 | 依赖 |
|---|---|---|
| `Zevision.Communication.dll` | 通讯:RS232/TCP 传输、帧编解码 (校验和、长度、多包重组)、响应解析、请求与响应的对应 | 只有 .NET Framework |
| `Zevision.Commands.dll` | 指令集:全部指令的命令模式、动作、中英文名称、单位、范围、选项;生成命令字符串 | `Zevision.Communication.dll` |

- 库版本 **1.1.0** (两个库相同,独立于 Zevision 应用版本)。目标框架 .NET Framework 4.7.2 (4.7.2 及以上,含 4.8)。
- 引用两个 dll,并把同名的 `.xml` 放在 dll 旁边 (Visual Studio 的 IntelliSense 说明)。
- 示例程序:`sample\Program.cs` (连接、握手、查询,只读不写)。

## 目录

1. [概述](#1-概述)
2. [快速开始](#2-快速开始)
3. [协议要点](#3-协议要点)
4. [连接参数](#4-连接参数)
5. [ZevisionClient:连接与收发](#5-zevisionclient连接与收发)
6. [帮助表](#6-帮助表)
7. [底层 API:编解码与解析](#7-底层-api编解码与解析)
8. [Zevision.Commands:指令集](#8-zevisioncommands指令集)
9. [常见任务](#9-常见任务)
10. [设备特性与故障排查](#10-设备特性与故障排查)
11. [性能与内存](#11-性能与内存)
12. [版本与兼容性](#12-版本与兼容性)
13. [API 参考](#13-api-参考)

## 1. 概述

### 1.1 设计原则

- 两个库**只做**指令生成、数据收发和解析:不写日志、不读写文件、不弹界面、不依赖 WPF/WinForms。需要记录的内容都通过事件或返回值交给调用方。
- **预期内的失败不抛异常**:端口被占用、超时、设备无响应等以结果类型 (`ZevisionResult` / `ZevisionResult<T>`) 返回;**调用错误抛异常**:参数为 null、ID 格式不对、不支持的动作等 (`ArgumentException` 及其子类)。
- 说明文字:通讯库的结果说明和异常信息只有英文 (`ZevisionError.Message`);.NET 和 Windows 自己的异常信息 (如端口不存在) 按系统语言给出,原样附在后面。指令集的名称、单位、选项同时有中英文,用 `TextLanguage` 选择;其异常信息为英文。
- 全部 I/O 是异步的 (`async`/`await`,内部 `ConfigureAwait(false)`),可以在任何线程调用;所有异步方法都接受 `CancellationToken`。

### 1.2 结构

```
调用方
  │  ZevisionCommandSet.Query("M0103")            ← Zevision.Commands:生成命令 "QM0103"
  ▼
ZevisionClient.SendAsync("QM0103")               ← Zevision.Communication
  │  ZevisionFrameCodec   编码:{ [长度] 数据 [校验和] }
  │  IDeviceTransport     SerialPortTransport / TcpTransport (或自定义)
  │  读取循环 (后台)       ZevisionFrameDecoder 拆帧、多包重组
  │                        按命令回显对应到等待中的请求
  │  ZevisionResponseParser 拆成每条命令的响应
  ▼
ZevisionResult<ZevisionReply>                     ← Responses[i]:回显、+/-、错误代码、数据
```

| 层 | 类型 | 作用 |
|---|---|---|
| 指令 | `ZevisionCommandSet`、`CommandDefinition` | 查找指令定义,生成并检查命令字符串 |
| 会话 | `ZevisionClient` | 连接、排队发送、等待响应 (空闲超时)、事件 |
| 协议 | `ZevisionFrameCodec`、`ZevisionFrameDecoder`、`ZevisionResponseParser`、`HelpTableParser`、`ProtocolText` | 帧编解码、响应解析、帮助表解析、字符规则 |
| 传输 | `IDeviceTransport`、`SerialPortTransport`、`TcpTransport`、`DeviceTransports` | 字节流的读写 |
| 参数 | `ConnectionOptions`、`SerialSettings`、`TcpSettings`、`FrameOptions` | 不可变的通讯参数 |

## 2. 快速开始

```csharp
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Commands;
using Zevision.Communication;

internal static class Program
{
    private static async Task<int> Main()
    {
        // 1. 通讯参数:连接目标 + 帧格式 (须与设备一致) + 响应空闲超时
        var options = new ConnectionOptions(
            new SerialSettings("COM3", 115200),            // 或 new TcpSettings("192.168.0.10", TcpSettings.DefaultPort)
            new FrameOptions(checksumEnabled: true, lengthEnabled: true),
            TimeSpan.FromSeconds(1));

        // 2. 一个实例对应一台设备
        using (var client = new ZevisionClient(options))
        using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            client.ConnectionLost += (s, e) => Console.WriteLine("Connection lost: " + e.Exception.Message);  // 后台线程
            client.HandlerFailed += (s, e) => Console.WriteLine(e.EventName + " handler failed: " + e.Exception);

            var connected = await client.ConnectAsync(cancel.Token);
            if (!connected.IsSuccess)
            {
                Console.WriteLine(connected.Error);     // 如 "ConnectFailed: Cannot connect to COM3, 115200, 8N1: ..."
                return 1;
            }

            // 3. 用指令集生成命令,用客户端发送
            var reply = await client.SendAsync(ZevisionCommandSet.Query("M0103"), cancel.Token);   // "QM0103"
            if (!reply.IsSuccess)
            {
                // 通讯失败:Kind = Timeout、NotConnected…,Message 为英文说明
                Console.WriteLine(reply.Error.Kind + ": " + reply.Error.Message);
                return 1;
            }

            var response = reply.Value.Responses[0];
            if (response.IsSuccess)
            {
                var density = decimal.Parse(response.Data, CultureInfo.InvariantCulture);  // 设备的小数点是 '.'
                Console.WriteLine("Density = " + density);
            }
            else
            {
                // 设备拒绝 (不是通讯失败):如 IllegalArgument,Describe() 为英文说明
                Console.WriteLine(response.ErrorCode + ": " + response.ErrorCode.Describe());
            }

            await client.DisconnectAsync();
            return 0;
        }
    }
}
```

## 3. 协议要点

完整定义见协议文档《INFICON IMC300 Zevision 通讯协议说明》,这里只列出调用时需要知道的部分。

| 项目 | 格式 | 说明 |
|---|---|---|
| 帧 | `{` [长度 2 字节] 命令数据 [校验和 1 字节] `}` | 长度 (高字节在前) 和校验和是否存在由设备设置决定:RS232 为 G1305/G1304,TCP 为 G1408/G1407。校验和 = 命令数据逐字节求和取模 256 |
| 命令 | 动作字母 + ID [`:` 数据] | 如 `QM0103` (查询)、`UM0103:2.700` (更新)、`AM03` (添加)、`H01` (无动作字母) |
| 多条命令 | `QG01;QG1304;QG1305` | 一帧最多 57000 字节;响应按顺序一一对应 |
| 响应 | 回显 `+` [`:` 数据] 或 回显 `-` 错误代码 | 如 `QM0103+:5.890`、`UM0103-B`;校验和错误时设备不回显命令,只回 `-C` |
| 多包消息 | `{`…`>` `<`…`>` … `<`…`}` | 大响应 (如完整帮助约 116 KB) 分成多个包,每包最多 57000 字节,各带长度和校验和;库自动重组 |
| 动作字母 | `Q` 查询、`U` 更新、`A` 添加、`D` 删除、`S` 状态、`R` 远程动作、`E` 回显、`H` 握手、`?` 帮助 | 由 `ZevisionCommandSet` 生成,不需要手工拼接 |

设备错误代码 (`ZevisionErrorCode`):

| 代码 | 枚举值 | `Describe()` | 含义 |
|---|---|---|---|
| A | `IllegalCommand` | Illegal command, not supported | 命令不存在或不支持该动作;小写命令也返回 A |
| B | `IllegalArgument` | Illegal parameter value | 数据不合法 (如引用了不存在的对象) |
| C | `ChecksumError` | Checksum error | 校验和错误 (设备不回显命令) |
| D | `IllegalFormat` | Illegal format | 格式错误;查询不存在的对象也返回 D |
| F | `StateError` | State error, the system is in the wrong state | 状态不允许 (如工艺运行中) |
| L | `LengthError` | Length error | 长度错误 |
| T | `Timeout` | Timeout, no stop character received | 设备 200 ms 内没有收到停止符 (多为帧格式与设备不一致) |
| 其他 | `Unknown` | Unknown error | 否定响应但没有或无法识别错误代码 |

## 4. 连接参数

全部参数在创建时检查,创建后不变 (不可变对象,可以在线程间共享)。

```csharp
// 串口:IMC300 只能设置波特率 (G1303),固定 8N1
var serial = new SerialSettings("COM3", 115200);       // 端口名不能为空,波特率须 > 0

// TCP:端口固定为 2101 (TcpSettings.DefaultPort);设备的 G1406 须为 0 (Zevision)
var tcp = new TcpSettings("192.168.0.10", TcpSettings.DefaultPort);   // 主机不能为空,端口 1~65535

// 帧格式:必须与设备一致,否则设备拒绝命令或响应无法解析 (见 10.1)
var frame = new FrameOptions(checksumEnabled: true, lengthEnabled: true);
var plain = FrameOptions.Plain;                          // 不带长度和校验和 (设备出厂设置)

// 响应空闲超时:省略时为 ConnectionOptions.DefaultResponseTimeout (1 s),必须 > 0
var options = new ConnectionOptions(serial, frame, TimeSpan.FromSeconds(1));
```

- **响应超时是空闲超时**:从发送或最近一次收到字节算起。大响应只要还在传输就不会超时 (115200 bps 下完整帮助约 10 s 也不会超时)。
- 个别指令响应前或分包之间停顿较长 (如 `?`),用 `SendAsync(command, minimumResponseTimeout, ct)` 为单次请求放宽 (见 5.2)。
- `ToString()`:`SerialSettings` 为 `COM3, 115200, 8N1`,`TcpSettings` 为 `192.168.0.10:2101`,可用于日志和状态栏。

## 5. ZevisionClient:连接与收发

### 5.1 生命周期

```
new ZevisionClient(options)
        │
        ▼
   未连接 ──ConnectAsync──▶ 已连接 ──DisconnectAsync / 连接中断──▶ 未连接 (可以再次 ConnectAsync)
        │                     │
        └──────── Dispose ────┴──▶ 已释放 (ConnectAsync 抛 ObjectDisposedException,发送返回 NotConnected)
```

| 方法 | 说明 |
|---|---|
| `ZevisionClient(ConnectionOptions)` | 按 `options.Endpoint` 的类型打开串口或 TCP (`DeviceTransports.Create`) |
| `ZevisionClient(ConnectionOptions, Func<ConnectionEndpoint, IDeviceTransport>)` | 自定义传输 (测试、其他连接方式,见 5.7);每次连接调用一次工厂 |
| `ConnectAsync(ct)` | 打开连接并启动后台读取循环。失败返回 `AlreadyConnected` / `ConnectFailed`;取消时抛 `OperationCanceledException`,已打开的部分会关闭 |
| `DisconnectAsync()` | 关闭连接并等待读取循环结束;等待中的请求以 `Disconnected` 结束;未连接时直接返回。**不会中止正在进行的 `ConnectAsync`**,要中止连接请取消它的令牌 |
| `IsConnected` | 是否已连接 |
| `Options` | 构造时的通讯参数 |
| `Dispose()` | 断开并释放。连接过程中释放时,连接完成后立即关闭,`ConnectAsync` 抛 `ObjectDisposedException` |

- 一个实例对应一台设备、一组通讯参数。要换端口或帧格式,断开后用新参数创建新实例。
- 程序退出前调用 `DisconnectAsync` 或 `Dispose`,释放串口或套接字。

### 5.2 发送与响应

```csharp
Task<ZevisionResult<ZevisionReply>> SendAsync(string command, CancellationToken ct = default);
Task<ZevisionResult<ZevisionReply>> SendAsync(string command, TimeSpan minimumResponseTimeout, CancellationToken ct = default);
```

- `command` 是命令数据,不含起止符:`H01`、`QM0103`、`QG01;QG1304;QG1305`。用 `ZevisionCommandSet` 生成 (见第 8 章),不要手工拼接。
- **一次只有一个请求在等待响应**,并发调用自动排队 (先到先发)。可以在多个线程同时调用。
- **按命令回显对应**:响应的回显与请求不符 (超时后迟到的响应、取消 `?` 后设备仍在发送的剩余数据) 时,不会被当成当前请求的响应,而是作为未请求数据报告 (`FrameReceived` 的 `IsSolicited` 为 false)。无法判断的帧 (帧无效、无法解析、设备只回 `-C`) 交给当前请求报告。
- **超时**:见第 4 章。`minimumResponseTimeout` 只作用于本次请求,与 `ResponseTimeout` 取较长者。
- **取消**:取消令牌后 `SendAsync` 抛 `OperationCanceledException`。命令可能已经发出,设备可能仍会响应;迟到的响应作为未请求数据报告,不会影响下一个请求。
- **分隔符规避**:帧的长度或校验和字节恰好等于 `{ } < >` 时,设备会把它误当作分隔符 (实测 0x7D `}`)。库自动在命令后追加 `;H01` (`ZevisionFrameCodec.FillerCommand`,只读) 改变校验和,多出的 H01 响应不返回给调用方;`FrameSent` 报告的是实际发送的命令。

`ZevisionReply`:

| 成员 | 说明 |
|---|---|
| `Responses` | 按命令顺序的响应 (`ZevisionResponse`),一条命令一个;**成功时至少有一条** |
| `Frame` | 收到的响应帧 (`ZevisionFrame`,含原始字节,见第 7 章) |

`ZevisionResponse`:

| 成员 | 说明 |
|---|---|
| `Command` | 设备回显的命令,如 `QM0103`;设备未回显时 (如 `-C`) 为空字符串 |
| `IsSuccess` | `+` 为 true,`-` 为 false |
| `ErrorCode` / `RawErrorCode` | 错误代码 (`ZevisionErrorCode`) 和原始字符;成功时为 `None` / null |
| `Data` | `:` 之后的数据 (查询结果);没有时为 null。数字用 `CultureInfo.InvariantCulture` 解析 |

> 设备拒绝 (`-A`、`-B`…) **不是**通讯失败:`SendAsync` 成功,对应的 `ZevisionResponse.IsSuccess` 为 false。

### 5.3 结果与错误

`ZevisionResult` / `ZevisionResult<T>` 是结构体:

| 成员 | 说明 |
|---|---|
| `IsSuccess` | 是否成功 |
| `Value` | 返回值 (仅 `ZevisionResult<T>`);失败时访问抛 `InvalidOperationException` |
| `Error` | 失败原因 (`ZevisionError`);成功时为 null |
| `ToString()` | 成功为 `OK` (带返回值时为 `OK: 值`),失败为 `Kind: Message` |

`ZevisionError`:`Kind`、`Message` (英文) 和生成本地化提示所需的数据:

| `Kind` | 何时 | 附带数据 |
|---|---|---|
| `AlreadyConnected` | 已连接或正在连接时再次 `ConnectAsync` | |
| `ConnectFailed` | 端口不存在或被占用、连接被拒绝、TCP 5 s 内没有连上 (`TimeoutException`) | `Exception`、`Endpoint` |
| `NotConnected` | 未连接时发送 | |
| `InvalidCommand` | 命令为空、含非可打印 ASCII 或 `{ }`、超过 57000 字节;**没有发送** | `Exception` |
| `SendFailed` | 写入失败 (如写入时连接被关闭) | `Exception` |
| `Timeout` | 空闲超时 | `Timeout` (本次使用的超时)、`ReceivedIncompleteData` (未收完的数据已通过 `FrameReceived` 报告) |
| `Disconnected` | 等待响应时连接断开 (主动断开或中断) | |
| `InvalidFrame` | 响应帧校验和、长度等错误 | `FrameError` |
| `UnparseableResponse` | 帧有效,但内容不是 Zevision 响应格式,或没有任何响应 | `DataLength` |

处理方式示例:

```csharp
var reply = await client.SendAsync(command, ct);
if (!reply.IsSuccess)
{
    switch (reply.Error.Kind)
    {
        case ZevisionErrorKind.Timeout:
            // 设备没有响应:检查连线、波特率、帧格式;ReceivedIncompleteData 为 true 时多为帧格式不一致
            break;
        case ZevisionErrorKind.Disconnected:
        case ZevisionErrorKind.NotConnected:
            // 连接已断开:重新连接
            break;
        default:
            log.Error(reply.Error.ToString(), reply.Error.Exception);
            break;
    }
}
```

### 5.4 事件

| 事件 | 时机 | 参数 |
|---|---|---|
| `FrameSent` | 每帧发送前 (发送方的线程) | `Command`:实际发送的命令 (可能带 `;H01`);`Raw`:完整帧字节 |
| `FrameReceived` | 每收到一帧,包括错误帧、帧外数据和超时时未收完的数据 | `Frame`:帧;`IsSolicited`:是否是当前请求的响应 |
| `ConnectionLost` | 连接意外中断 (串口拔出、TCP 断开、读取出错);**主动断开不触发**;触发时 `IsConnected` 已为 false | `Exception`:通常是 `IOException`;其他类型说明读取时出现了意外错误,应记录 |
| `HandlerFailed` | 上面三个事件的处理程序抛出异常 | `EventName`、`Exception` |

- **全部在后台线程触发**。界面程序要切换到界面线程再更新控件。
- `FrameReceived` **依次触发**,不会同时进入两个处理程序;同一请求的 `FrameReceived` 总在 `SendAsync` 返回之前触发。
- 处理程序应尽快返回 (例如把数据交给界面线程),**不要在处理程序中同步等待发送** (`client.SendAsync(...).Wait()`),否则会死锁。
- 处理程序抛出的异常不会中断通讯,而是交给 `HandlerFailed`;`HandlerFailed` 自身的处理程序抛出的异常被忽略。

WPF 中显示收发数据:

```csharp
var dispatcher = Application.Current.Dispatcher;
client.FrameReceived += (s, e) =>
{
    var text = e.Frame.IsValid ? e.Frame.Payload : e.Frame.Error.ToString();   // 在后台线程格式化
    dispatcher.BeginInvoke(new Action(() => log.Items.Add(text)));              // 在界面线程更新
};
```

WinForms 中用 `control.BeginInvoke(...)`;控制台程序可以直接输出。

### 5.5 线程与并发

- `ConnectAsync`、`DisconnectAsync`、`SendAsync`、`Dispose`、`IsConnected` 都可以在任意线程调用。
- 并发的 `SendAsync` 排队执行,不会交错;响应按回显对应,不会错配。
- 库内部所有 `await` 都用 `ConfigureAwait(false)`:在界面线程调用 `await client.SendAsync(...)` 不会死锁,`await` 之后回到界面线程。**不要**用 `.Result` / `.Wait()` 阻塞等待。

### 5.6 资源与异常

- 串口:断开时 .NET Framework 的 `SerialPort.BaseStream.ReadAsync` 只能通过关闭端口结束,调试器输出窗口会看到一次**已处理的**首次机会异常 `System.IO.IOException` (已中止 I/O 操作),这是正常的。
- TCP:启用了 keep-alive (空闲 5 s 后每 1 s 探测),拔网线约 15 s 后触发 `ConnectionLost`。对端关闭时立即触发。
- 参数错误 (null、`ConnectionOptions` 中不支持的连接类型等) 抛异常;`DeviceTransports.Create` 对未知的 `ConnectionEndpoint` 子类抛 `ArgumentException`。

### 5.7 自定义传输

实现 `IDeviceTransport` 即可用于测试 (模拟设备) 或其他连接方式:

| 成员 | 约定 |
|---|---|
| `OpenAsync(endpoint, ct)` | 打开连接;失败抛 `IOException`、`UnauthorizedAccessException`、`TimeoutException` 等 (转为 `ConnectFailed`) |
| `ReadAsync(buffer, ct)` | 读取至少 1 个字节,返回字节数;连接关闭时返回 0 或抛异常 |
| `WriteAsync(data, ct)` | 写入全部字节 |
| `Close()` | 关闭连接,使挂起的 `ReadAsync` 结束;重复调用无副作用;**不能抛出异常** |
| `IsOpen` | 是否已打开 |

客户端在每次 `ConnectAsync` 时调用工厂创建传输,断开或连接失败时调用 `Close()`。

```csharp
/// <summary>模拟设备 (测试用):帧格式为 FrameOptions.Plain,每条命令由 responder 给出响应,如 "H01" → "H01+:IMC300"。</summary>
internal sealed class SimulatedDevice : IDeviceTransport
{
    private readonly Func<string, string> _responder;
    private readonly ConcurrentQueue<byte[]> _replies = new ConcurrentQueue<byte[]>();
    private readonly SemaphoreSlim _available = new SemaphoreSlim(0);
    private readonly CancellationTokenSource _closed = new CancellationTokenSource();
    private byte[] _reply = new byte[0];                                         // 正在读出的回复 (只有读取循环访问)
    private int _offset;

    public SimulatedDevice(Func<string, string> responder) { _responder = responder; }

    public bool IsOpen { get; private set; }

    public Task OpenAsync(ConnectionEndpoint endpoint, CancellationToken cancellationToken)
    {
        IsOpen = true;
        return Task.CompletedTask;
    }

    public Task WriteAsync(byte[] data, CancellationToken cancellationToken)
    {
        var command = Encoding.ASCII.GetString(data, 1, data.Length - 2);       // 去掉 { }
        // FrameOptions.Plain 的响应帧就是 { 数据 }。不能用 ZevisionFrameCodec.Encode:它检查的是命令文本,
        // 响应中的换行 (帮助表) 和 UTF-8 文字 (如 Å/s) 会被拒绝。
        _replies.Enqueue(Encoding.UTF8.GetBytes("{" + _responder(command) + "}"));
        _available.Release();
        return Task.CompletedTask;
    }

    public async Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        if (_offset == _reply.Length)
        {
            try
            {
                await _available.WaitAsync(_closed.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return 0;                                                        // 已关闭
            }

            _replies.TryDequeue(out _reply);
            _offset = 0;
        }

        var count = Math.Min(buffer.Length, _reply.Length - _offset);            // 比读取缓冲区大的回复分几次读出
        Array.Copy(_reply, _offset, buffer, 0, count);
        _offset += count;
        return count;
    }

    public void Close()
    {
        IsOpen = false;
        _closed.Cancel();
    }

    public void Dispose() => Close();
}

var options = new ConnectionOptions(new SerialSettings("SIM", 115200), FrameOptions.Plain);
var client = new ZevisionClient(options, _ => new SimulatedDevice(command => command + "+:simulated"));
```

## 6. 帮助表

`?` (`ZevisionCommandSet.HelpCommand`) 返回全部设置项及当前值的帮助表,`?M`、`?F` 等组帮助只返回一组。表格为固定宽度的文本,`HelpTableParser.Parse` 解析为 `HelpTableEntry` (ID、当前值、说明)。

| 请求 | 大小 (固件 1.8.11 实测) | 时间 (115200 bps) | 建议的 `minimumResponseTimeout` |
|---|---|---|---|
| `?` | 约 116 KB,分 3 包 | 约 10 s (首字节约 0.4 s,包间约 0.35 s) | 2 s |
| `?M` (材料) | 约 5 KB,1 包 | 约 0.5 s | 2 s |
| `?F` (膜系) | 约 59 KB | 约 5 s | 2 s |

```csharp
var reply = await client.SendAsync("?M", TimeSpan.FromSeconds(2), ct);
if (reply.IsSuccess && reply.Value.Responses[0].IsSuccess)
{
    foreach (var entry in HelpTableParser.Parse(reply.Value.Responses[0].Data))
    {
        // entry.Id = "M0103", entry.Value = "5.890", entry.Description = "Density (g/cm^3)"
        // 分组标题和动作命令的 Value 为空字符串
    }
}
```

- 组帮助只列出**已有的**对象 (如材料 M01–M09、M10、M18、MA5、MFA),是确定"有哪些对象"的可靠方法 (对象编号不连续,见 10.2)。
- 取消正在进行的 `?` 后,设备仍会把剩余数据发完 (约数秒),这些数据作为未请求数据报告。

## 7. 底层 API:编解码与解析

不经过 `ZevisionClient`、自己处理字节流时使用 (如已有的通讯框架、离线分析抓包)。

### 7.1 编码

```csharp
byte[] frame = ZevisionFrameCodec.Encode("QM0103", new FrameOptions(true, true));
// { 00 06 'Q' 'M' '0' '1' '0' '3' cs }

byte[] safe = ZevisionFrameCodec.EncodeAvoidingDelimiters("AL0702010600", options, out var sent);
// 校验和恰好是 '}' 时 sent = "AL0702010600;H01" (见 5.2)

byte cs = ZevisionFrameCodec.ComputeChecksum(data, 0, data.Length);    // 逐字节求和取模 256
```

- `Encode` 对空命令、非可打印 ASCII、`{ }`、超过 `MaxDataLength` (57000) 抛 `ArgumentException`。

### 7.2 解码

```csharp
var decoder = new ZevisionFrameDecoder(options);          // 非线程安全:只在一个读取循环中使用
foreach (var frame in decoder.Push(buffer, 0, count))       // 一次读取可能是半帧,也可能是多帧
{
    if (frame.IsValid) { /* frame.Payload */ }
    else { /* frame.Error:ChecksumMismatch、MissingStopByte、UnexpectedData… */ }
}
// 请求超时时:把没收完的数据作为 FrameError.Incomplete 输出,便于诊断
var partial = decoder.FlushIncomplete();
```

`ZevisionFrame`:

| 成员 | 说明 |
|---|---|
| `IsValid` / `Error` | 是否有效 / 解码错误 (`FrameError`) |
| `Payload` | 命令数据的文本 (UTF-8 解码,首次读取时解码并缓存) |
| `Data` | 命令数据字节 (不含起止符、长度、校验和、包分隔符) |
| `Raw` | 原始字节 (含帧结构),用于显示 |
| `DataRanges` | 每个包的数据在 `Raw` 中的位置 (`ByteRange`),显示时区分数据和帧结构字节 |
| `PacketCount` / `DeclaredLengths` | 包数、各包长度字段的值 (诊断分包问题) |

`FrameError`:`ChecksumMismatch`、`InvalidLength` (长度为 0、超过 57000 或消息超过 1 MB)、`MissingStopByte`、`Truncated` (帧未结束又收到起始符)、`UnexpectedData` (帧外字节)、`TooShort`、`MissingContinuation`、`Incomplete` (超时时未收完)。

- 解码器能处理半帧、多帧、分包重组、帧外垃圾字节,以及设备在长度模式下偶尔发来的不带长度的长响应 (实测 RS232 的 `?`)。

### 7.3 解析响应

```csharp
IReadOnlyList<ZevisionResponse> responses = ZevisionResponseParser.Parse("QG03+:IMC300;QC09+:Ready");   // 格式错误抛 FormatException
if (ZevisionResponseParser.TryParse(payload, out var parsed)) { … }                                   // 格式错误返回 false

ZevisionResponseParser.EchoOf("UG1304:1;H01");                     // "UG1304":设备对这条命令的回显
ZevisionResponseParser.IsReplyTo(frame, "QM0103");                  // 这一帧是否是对它的响应 (无法判断时为 true)
```

- 数据中的 `;` 没有转义;不像响应头 (回显 + 状态符) 的片段会并回上一条响应的数据。

### 7.4 文本规则

`ProtocolText` 是可以放进帧的文本规则,命令和设置值的检查都以它为准:

```csharp
ProtocolText.CheckCommand("QM0103", out var bad);    // ProtocolTextError.None
ProtocolText.CheckValue("a;b", out bad);              // ProtocolTextError.Separator,bad = ';'
string error = ProtocolText.ValidateValue("Å");       // 英文说明;合法时为 null
```

| 规则 | 命令 | 设置值 |
|---|---|---|
| 不能为空 | ✓ | ✓ |
| 只能是可打印 ASCII (0x20~0x7E) | ✓ | ✓ |
| 不能含起止符 `{ }` | ✓ | ✓ |
| 不能含命令分隔符 `;` | (可以,用于多条命令) | ✓ |

## 8. Zevision.Commands:指令集

### 8.1 概念

- **模式**:每条指令有一个模式,如 `M**03` (材料的密度)。`**` 是一级编号,用两位十六进制表示:第一级通常是对象编号 (材料、膜系、膜层、工艺…),之后的是子列表中的序号 (如 `F**03**01` 中的预熔序号)。
- **编号**:API 中的编号是十进制 1~253 (`CommandDefinition.MinIndex`~`MaxIndex`),生成命令时写成两位十六进制:材料 10 → `M0A03`。`00` 是列表的增删命令,`FE` 是数量,`FF` 保留,都不是对象编号。
- **ID**:不带动作字母的完整编号,如 `M0103`、`P020107FE`;格式为大写组字母 + 若干两位大写十六进制 (`IsWellFormedId`)。区分大小写:设备只认大写。
- **组** (`CommandGroup`):ID 的第一个字符。C 工艺控制、E 回显、F 膜系、G 通用、H 握手、I 输入、L 膜层、M 材料、N 传感器、O 输出、P 工艺、S 输出源、T 逻辑。
- **动作** (`CommandActions`,可组合):Query、Update、Add、Delete、Status、Remote;`None` 表示命令本身不带动作字母 (`H01`、`E`)。
- 来源:协议文档第 4 章和《U 类型指令汇总》;与文档不同、按实机 (IMC300 固件 1.8.11) 补充的地方在定义的 `Remark` 中注明。英文名称取自设备 `?` 帮助表。

### 8.2 ZevisionCommandSet

| 成员 | 说明 |
|---|---|
| `Query(id)` / `Update(id, value)` / `Add(id)` | 由完整 ID 生成命令:`QM0103`、`UM0103:2.700`、`AM03` |
| `Build(action, id, value = null)` | 同上,任意单个动作;ID 有定义时检查动作是否支持 |
| `Find(id)` | 查找 ID 对应的指令定义;匹配多个模式时取最具体的 (`MFE` 是材料数量,不是 `M**`);找不到返回 null |
| `IsWellFormedId(id)` | ID 格式是否正确 (不检查组是否存在) |
| `TryGetGroup(id, out group)` | 由第一个字符得到命令组 (不区分大小写) |
| `GetCommands(group)` / `All` | 一个组 / 全部的指令定义 (只读,按协议文档顺序) |
| `HelpCommand` | `?` |
| `ActionLetter(action)` | 动作字母:Query → `Q`,None → 空 |
| `ObjectKind(group, language)` | 对象类型名称:`M` → 材料 / Material |
| `IndexName(prefixPattern)` | 某级编号 (列表) 的名称:`F**03` → 预熔 / Pre Condition |

```csharp
ZevisionCommandSet.Query("M0103");                      // "QM0103"
ZevisionCommandSet.Update("M0103", "2.700");            // "UM0103:2.700"
ZevisionCommandSet.Add("M03");                          // "AM03"      按编号创建材料
ZevisionCommandSet.Add("F010300");                      // "AF010300"  在膜系 F01 的预熔列表末尾追加
ZevisionCommandSet.Build(CommandActions.Delete, "M03"); // "DM03"      按编号删除材料 (文档未说明,见 10.2)
```

`Build` / `Query` / `Update` / `Add` 的检查 (不通过时抛 `ArgumentException`,说明为英文;`id` 为 null 抛 `ArgumentNullException`):

- ID 格式正确,且组字母是已知的命令组;
- ID 在指令集中有定义时,动作必须受支持 (如 `MFE` 只能查询,`M**` 只能添加/删除);
- 只能是单个动作;更新必须带数据,查询/添加/删除/状态不能带数据;
- 数据符合 `ProtocolText.CheckValue`。

**不检查取值范围**:范围、整数、选项由调用方用 `CommandDefinition` 检查 (见 8.3)。指令集中没有定义的 ID (逻辑组的其他项、文档未列出的设置项) 只检查格式,由设备判断。

### 8.3 CommandDefinition

```csharp
var density = ZevisionCommandSet.Find("M0103");          // 模式 M**03

density.Pattern;            // "M**03"
density.Group;              // CommandGroup.M
density.Actions;            // Query | Update
density.CanUpdate;          // true
density.Kind;               // CommandValueKind.Number
density.Min; density.Max;   // 0.100 ~ 100.000
density.IsInteger;          // false
density.Unit.Get(TextLanguage.Chinese);                   // "g/cm³"
density.GetName(TextLanguage.Chinese);                    // "密度"
density.GetLabel(TextLanguage.English);                   // "Density (g/cm^3)"
density.GetRangeText(TextLanguage.English);               // "0.100 ~ 100.000 g/cm³"

// 由编号生成 (十进制编号,按顺序填入 **)
density.IndexCount;                                       // 1
density.FormatId(10);                                     // "M0A03"
density.Build(CommandActions.Query, 10);                  // "QM0A03"
density.Build(CommandActions.Update, "2.700", 10);        // "UM0A03:2.700"
density.ParseIndices("M0A03");                            // [10]
density.IsMatch("M0A03");                                 // true
```

| 成员 | 说明 |
|---|---|
| `Kind` | `None` (动作、列表节点)、`Text`、`Number`、`Enum` (固定选项)、`Time` (`H:MM:SS`)、`Reference` (另一个对象的编号) |
| `Min` / `Max` | 数字为数值,时间为**秒数**;没有时为 null |
| `IsInteger` | 是否只能是整数 |
| `Options` | 固定选项 (`CommandOption`:`Value` + 中英文 `Text`),只读 |
| `ReferenceGroup` / `AllowNone` | 引用的对象组 (如膜系的材料 `M`) / 是否可以为 0 (未选择) |
| `Unit` / `Remark` | 单位 / 补充说明 (`BilingualText`) |
| `GetIndexNames(language)` | 各级编号的含义:`F**03**01` → [膜系, 预熔] |
| `FirstIndexIsObject` | 第一级是否是对象编号 (`T0101**` 的第一级是输入编号,不是对象) |
| `LabelFor(id, language, deviceDescription)` | 某一项的显示名称,子列表中的项带列表名和序号:`F01030201` → `预熔 2 · 起始斜坡/上升时间`;英文优先用设备帮助表的说明 |
| `Supports(action)` | 是否支持该动作 |

检查用户输入的值:

```csharp
static string Check(CommandDefinition d, string text)
{
    switch (d.Kind)
    {
        case CommandValueKind.Number:
            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)) return "not a number";
            if (d.IsInteger && number != decimal.Truncate(number)) return "must be an integer";
            if (number < d.Min || number > d.Max) return "out of range " + d.GetRangeText(TextLanguage.English);
            return null;
        case CommandValueKind.Enum:
            return d.Options.Any(o => o.Value == text) ? null : "one of " + d.GetRangeText(TextLanguage.English);
        default:
            return ProtocolText.ValidateValue(text);   // 至少保证能放进帧
    }
}
```

`BilingualText`:`Chinese`、`English`、`Get(language)`;`TextLanguage`:`Chinese`、`English`。

## 9. 常见任务

### 9.1 一帧查询多项

```csharp
var command = string.Join(";", new[] { "G03", "C09", "MFE" }.Select(ZevisionCommandSet.Query));   // "QG03;QC09;QMFE"
var reply = await client.SendAsync(command, ct);
if (reply.IsSuccess)
{
    // 响应按命令顺序;每条单独判断成功与否 (某一条被拒绝不影响其他条的结果)
    foreach (var r in reply.Value.Responses)
        Console.WriteLine(r.Command + " = " + (r.IsSuccess ? r.Data : r.ErrorCode.ToString()));
}
```

### 9.2 写入并读回校验

```csharp
async Task<bool> WriteAsync(ZevisionClient client, string id, string value, CancellationToken ct)
{
    var write = await client.SendAsync(ZevisionCommandSet.Update(id, value), ct);
    if (!write.IsSuccess || !write.Value.Responses[0].IsSuccess) return false;

    var read = await client.SendAsync(ZevisionCommandSet.Query(id), ct);
    return read.IsSuccess && read.Value.Responses[0].IsSuccess
        && decimal.Parse(read.Value.Responses[0].Data, CultureInfo.InvariantCulture) == decimal.Parse(value, CultureInfo.InvariantCulture);
}
```

- 数字按数值比较 (`2.7` 与 `2.700` 相等);设备会把数字按自己的小数位数返回。
- 修改设置前建议确认设备空闲:`QC09` 的值为 `Ready`。

### 9.3 新建、删除对象和列表项

```csharp
// 按编号创建材料 M03 (实机验证:AM01~AM03、AF01、AF02)
await client.SendAsync(ZevisionCommandSet.Add("M03"), ct);

// 列表只能在末尾追加/删除:膜系 F01 的预熔列表
await client.SendAsync(ZevisionCommandSet.Add("F010300"), ct);                          // 追加一条 (设备默认值)
await client.SendAsync(ZevisionCommandSet.Build(CommandActions.Delete, "F010300"), ct); // 删除最后一条
var count = await client.SendAsync(ZevisionCommandSet.Query("F0103FE"), ct);              // 条数
```

- 删除被其他设置引用的对象时,设备会把引用改掉 (实测删除材料后膜系的材料变为 0)。删除前先检查引用:膜系的材料 `F??02`、膜层中的膜系 `L??02??01`、工艺中的膜层 `P02??07??`。
- 新建后用组帮助 (`?M`) 重新读取,确认对象已出现并取得设备默认值。

### 9.4 连接中断后重新连接

```csharp
// 事件在后台线程触发;不要写成 async 事件处理程序 (其中的异常无法捕获,会结束进程),而是启动一个任务。
client.ConnectionLost += (s, e) => { var reconnect = ReconnectAsync(client); };

static async Task ReconnectAsync(ZevisionClient client)
{
    // 等设备恢复后用同一个实例重新连接 (参数不变)
    for (var attempt = 0; attempt < 10 && !client.IsConnected; attempt++)
    {
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        try
        {
            if ((await client.ConnectAsync().ConfigureAwait(false)).IsSuccess)
            {
                return;
            }
        }
        catch (ObjectDisposedException)
        {
            return;                                                              // 程序正在退出,客户端已释放
        }
    }
}
```

## 10. 设备特性与故障排查

以下为 IMC300 固件 1.8.11 的实测结果。

### 10.1 帧格式必须与设备一致

| 连接 | 校验和 | 长度字节 | 其他 |
|---|---|---|---|
| RS232 | G1304 | G1305 | 波特率 G1303,8N1 |
| TCP | G1407 | G1408 | 端口 2101,协议类型 G1406 须为 0 (Zevision) |

不一致时的现象:

| 现象 | 原因 |
|---|---|
| `UnparseableResponse`,收到的帧形如 `{<00><02>-Cp}` | 程序未启用长度,设备启用了长度和校验和:设备拒绝命令 (`-C`),响应带长度字节 |
| 读取帮助表时收到 `{<00><02>-T<81>}` | 同上,设备把 `?}` 当成长度字节,等不到数据 (`-T`) |
| `Timeout` 且 `ReceivedIncompleteData` 为 true,未收完的数据是完整的 `{H01+:…}` | 程序启用了长度,设备没有:`H0` 被当成长度 18480 |

TCP 上设备只接受与 G1407/G1408 一致的帧。RS232 上设备两种格式的命令都能识别,但实测连接后第一条响应可能仍是上一次连接的格式,之后恢复正常 (未完全确认)。

### 10.2 对象和列表

- 对象编号不连续 (如材料 M01–M0A、M0C、M10);数量 (`MFE`) 推不出有哪些编号,用组帮助 `?M` 读取。
- 编号只用 01–FD;查询 `QMFF01` 返回的是材料数量,不要查询 FE/FF 作为对象编号。
- 材料编号可以到 FA (250),协议文档写的 01–C8 不对。
- 查询不存在的对象返回 `-D`,存在对象的不存在字段返回 `-A`;小写命令返回 `-A`。
- 列表只能在末尾添加和删除 (`A<列表>00` / `D<列表>00`)。按编号删除对象 (`DM03`) 文档没有说明,使用前请在设备上确认。

### 10.3 其他

- 说明文字是 UTF-8 (如 `Å/s`),`ZevisionFrame.Payload` 已按 UTF-8 解码。
- 设备即使启用了长度,也会把等于 `}` 的校验和当成停止符 (回 `-C` 且不回显命令),库自动规避 (见 5.2)。
- 设备的小数点是 `.`,解析数字用 `CultureInfo.InvariantCulture`。
- 时间格式 `H:MM:SS`,实机系统时间 (G0A) 返回 `MM-DD-YYYY HH:MM:SS`。

## 11. 性能与内存

| 场景 | 实测 (Release,.NET Framework 4.8) |
|---|---|
| 解码完整帮助 (116 KB,3 包) | 约 1 ms;一条 `ZevisionReply` 约 700 KB (原始字节、数据、文本各一份),请求结束后可回收 |
| 帮助表解析 (约 1500 行) | 约 1 ms |
| `ZevisionCommandSet.Find` | 约 1.7 µs/次,不分配内存 |
| 指令集常驻内存 | 约 220 KB (首次使用时生成,之后只读,线程安全) |
| 每次发送 | 少量短期对象;等待用的计时器在收到响应后立即释放 |

- 解码器的缓冲区在会话期间保留 (避免反复扩容),断开后释放。
- 每个连接一个后台读取循环;读取缓冲区 4 KB。

## 12. 版本与兼容性

两个库的 `AssemblyVersion` 保持 1.0.0.0,文件版本和产品版本随发布变化。引用 1.0.x 的程序直接替换两个 dll 即可,不需要重新编译。

| 版本 | 日期 | 修改 |
|---|---|---|
| 1.1.0 | 2026-10-18 | 通讯:连接过程中释放客户端时关闭刚打开的连接 (以前会留下打开的端口);连接失败或取消时关闭传输;收到的响应不会因事件处理程序慢而被判为超时,超时后也不会再被认领;`FrameReceived` 依次触发;断开时先结束等待中的请求;收到响应后立即释放计时器;大响应只解析一次;空响应返回 `UnparseableResponse`;`Push`/`ComputeChecksum` 检查范围。指令集:不再用正则匹配 (`Find` 约快 3 倍、不分配内存,常驻内存约 600 KB → 220 KB);新增 `IsWellFormedId`;`F**0902` (坩埚) 的说明改为 "0=任意, 1~所选输出源的坩埚数量 (S**07)" (设备上的选项为 Any、1~最大数量;文档写 0=disabled);`All`、`GetCommands`、`Options` 为只读集合;末尾带换行的 ID 不再被接受;null ID 抛 `ArgumentNullException` |
| 1.0.1 | 2026-10-18 | 通讯:启用长度时设备偶尔发来不带长度的 `{数据}` (RS232 的 `?`),解码器按无长度读出。指令集:中文术语 预条件 → 预熔、后条件 → 后处理 |
| 1.0.0 | 2026-10-16 | 第一个版本 |

## 13. API 参考

以下由程序集和 XML 文档生成,列出全部公开类型和成员。`T?`、可空注解没有显示,以说明为准。

### 13.1 Zevision.Communication

#### struct `ByteRange`
`Raw` 中的一段字节区间。
- `ByteRange(int offset, int length)`: 创建区间。
- `int Offset { get; }`: 起始位置。
- `int Length { get; }`: 长度。

#### class `ConnectionEndpoint`
连接目标:串口 (`SerialSettings`) 或 TCP (`TcpSettings`)。 `ToString` 给出用于日志和状态栏的简短描述。

#### class `ConnectionLostEventArgs` : EventArgs
`ConnectionLost` 的参数。
- `ConnectionLostEventArgs(Exception exception)`: 创建参数。
- `Exception Exception { get; }`: 导致断开的异常。通常是 `IOException` (拔线、对端关闭); 其他类型表示读取时出现了意外错误,调用方应记录它。

#### class `ConnectionOptions`
一次连接的全部参数,连接时一次性传入,连接期间不变。
- `ConnectionOptions(ConnectionEndpoint endpoint, FrameOptions frame, TimeSpan? responseTimeout = default)`: 创建连接参数。
- `static readonly TimeSpan DefaultResponseTimeout`: 默认响应空闲超时。
- `ConnectionEndpoint Endpoint { get; }`: 连接目标 (`SerialSettings` 或 `TcpSettings`)。
- `FrameOptions Frame { get; }`: 帧格式。
- `TimeSpan ResponseTimeout { get; }`: 等待响应的空闲超时:发送后或最近一次收到字节后,超过该时间仍无数据即判定超时。

#### static class `DeviceTransports`
按连接目标创建传输:`SerialSettings` → 串口,`TcpSettings` → TCP。
- `static IDeviceTransport Create(ConnectionEndpoint endpoint)`: 创建与 `endpoint` 对应的传输 (尚未打开)。

#### enum `FrameError` : IComparable, IFormattable, IConvertible
接收帧的解码错误。
- `None` = 0: 帧完整有效。
- `ChecksumMismatch` = 1: 校验和不匹配 (多包消息中任一包不匹配即报告)。
- `InvalidLength` = 2: 长度字节为 0、超过 57000,或消息总长超过接收上限。
- `MissingStopByte` = 3: 按长度读完数据和校验和后,下一个字节既不是 `}` 也不是 `>`。
- `Truncated` = 4: 帧未结束就收到了新的起始符,前一帧被丢弃。
- `UnexpectedData` = 5: 帧外的多余字节 (起始符之前的数据)。
- `TooShort` = 6: 启用校验和时,帧中没有足够的字节容纳命令数据和校验和。
- `MissingContinuation` = 7: 包以 `>` 结尾表示还有续包,但下一个字节既不是续包起始符 `<` 也不是结束符 `}`。
- `Incomplete` = 8: 等待响应超时时,消息还没接收完整 (解码器仍在某个包的中间)。

#### class `FrameOptions`
帧格式选项,必须与设备的 G1304 (校验和) 和 G1305 (长度字节) 设置一致。
- `FrameOptions(bool checksumEnabled, bool lengthEnabled)`: 创建帧格式选项。
- `static readonly FrameOptions Plain`: 不带长度和校验和的基本帧格式 (设备默认)。
- `bool ChecksumEnabled { get; }`: 帧尾 (停止符之前) 是否带 1 字节校验和。
- `bool LengthEnabled { get; }`: 起始符之后是否带 2 字节长度 (高字节在前)。

#### class `FrameReceivedEventArgs` : EventArgs
`FrameReceived` 的参数。
- `FrameReceivedEventArgs(ZevisionFrame frame, bool isSolicited)`: 创建参数。
- `ZevisionFrame Frame { get; }`: 收到的帧。
- `bool IsSolicited { get; }`: 是否是对当前请求的响应;false 表示设备主动发送或迟到的响应。

#### class `FrameSentEventArgs` : EventArgs
`FrameSent` 的参数。
- `FrameSentEventArgs(string command, byte[] raw)`: 创建参数。
- `string Command { get; }`: 实际发送的命令数据 (可能追加了 `FillerCommand`)。
- `byte[] Raw { get; }`: 完整帧字节。

#### class `HandlerFailedEventArgs` : EventArgs
`HandlerFailed` 的参数。
- `HandlerFailedEventArgs(string eventName, Exception exception)`: 创建参数。
- `string EventName { get; }`: 出错的事件,如 `FrameReceived`。
- `Exception Exception { get; }`: 事件处理程序抛出的异常。

#### class `HelpTableEntry`
`?` 帮助响应中的一行:命令 ID、当前值、说明。
- `HelpTableEntry(string id, string value, string description)`: 创建条目。
- `string Id { get; }`: 命令 ID (不含动作字母),如 `F0101`。
- `string Value { get; }`: 当前值,原样保留设备给出的文本;分组标题和动作命令为空字符串。
- `string Description { get; }`: 说明。

#### static class `HelpTableParser`
解析 `?` 帮助响应 (`?+:` 之后的数据)。
- `static IReadOnlyList<HelpTableEntry> Parse(string text)`: 解析帮助表。无法识别的行 (空行、表头、ID 格式不对的行) 被忽略。

#### interface `IDeviceTransport` : IDisposable
与设备之间字节流的最小抽象 (串口或 TCP),便于用假实现测试协议与服务层。
- `bool IsOpen { get; }`: 连接是否已打开。
- `Task OpenAsync(ConnectionEndpoint endpoint, CancellationToken cancellationToken)`: 打开连接。
- `void Close()`: 关闭连接。会使挂起的 `ReadAsync` 以异常结束或返回 0。重复调用无副作用, 不能抛出异常 (设备已拔出等情况下关闭失败也应忽略:连接反正要丢弃)。`ZevisionClient` 在断开和连接失败时调用。
- `Task WriteAsync(byte[] data, CancellationToken cancellationToken)`: 写入全部字节。
- `Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken)`: 读取至少 1 个字节,返回读取的字节数。返回 0 表示连接已关闭。

#### static class `ProtocolText`
可以放进 Zevision 帧的文本规则。命令和设置值的校验都以此为准,避免各处规则不一致。
- `static ProtocolTextError CheckCommand(string command, out char offending)`: 检查一条或多条 (`;` 分隔) 命令:不能为空,只能是可打印 ASCII,不能含起止符 `{ }`。
- `static ProtocolTextError CheckValue(string value, out char offending)`: 检查一个设置值 (`U<id>:<值>` 中的值):不能为空,只能是可打印 ASCII, 不能含命令分隔符 `;` 和起止符 `{ }`。
- `static string ValidateCommand(string command)`: 校验命令 (规则见 `CheckCommand`)。
- `static string ValidateValue(string value)`: 校验设置值 (规则见 `CheckValue`)。

#### enum `ProtocolTextError` : IComparable, IFormattable, IConvertible
`ProtocolText` 的检查结果。
- `None` = 0: 合法。
- `Empty` = 1: 为空。
- `NotPrintableAscii` = 2: 含有可打印 ASCII 以外的字符。
- `Delimiter` = 3: 含有起止符 `{` 或 `}`。
- `Separator` = 4: 设置值中含有命令分隔符 `;`。

#### class `SerialPortTransport` : IDeviceTransport, IDisposable
基于 `SerialPort` 的串口实现。
- `SerialPortTransport()`: 默认构造函数。
- `bool IsOpen { get; }`: 同接口/基类的说明。
- `Task OpenAsync(ConnectionEndpoint endpoint, CancellationToken cancellationToken)`: 同接口/基类的说明。
- `void Close()`: 同接口/基类的说明。
- `Task WriteAsync(byte[] data, CancellationToken cancellationToken)`: 同接口/基类的说明。
- `Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken)`: 同接口/基类的说明。
- `void Dispose()`: 同接口/基类的说明。

#### class `SerialSettings` : ConnectionEndpoint
串口参数。IMC300 只支持配置波特率 (G1303),帧格式固定为 8 位数据位、无校验、1 位停止位 (8N1)。
- `SerialSettings(string portName, int baudRate)`: 创建串口参数。
- `string PortName { get; }`: 端口名,如 COM3。
- `int BaudRate { get; }`: 波特率。设备默认 115200 (G1303)。
- `int DataBits { get; }`: 数据位,固定为 8。
- `Parity Parity { get; }`: 校验位,固定为无。
- `StopBits StopBits { get; }`: 停止位,固定为 1。
- `string ToString()`: 同接口/基类的说明。

#### class `TcpSettings` : ConnectionEndpoint
TCP 连接参数。IMC300 的以太网接口默认端口为 2101 (协议文档 1.1); 帧格式由设备的 G1406 (协议类型,须为 0=Zevision)、G1407 (校验和)、G1408 (长度检查) 决定。
- `TcpSettings(string host, int port)`: 创建 TCP 连接参数。
- `const int DefaultPort`: IMC300 以太网接口的默认端口。
- `string Host { get; }`: IP 地址或主机名。
- `int Port { get; }`: 端口。
- `string ToString()`: 同接口/基类的说明。

#### class `TcpTransport` : IDeviceTransport, IDisposable
基于 `TcpClient` 的 TCP 实现 (IMC300 以太网接口)。
- `TcpTransport()`: 默认构造函数。
- `static readonly TimeSpan ConnectTimeout`: 建立连接的超时。
- `bool IsOpen { get; }`: 同接口/基类的说明。
- `Task OpenAsync(ConnectionEndpoint endpoint, CancellationToken cancellationToken)`: 同接口/基类的说明。
- `void Close()`: 同接口/基类的说明。
- `Task WriteAsync(byte[] data, CancellationToken cancellationToken)`: 同接口/基类的说明。
- `Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken)`: 同接口/基类的说明。
- `void Dispose()`: 同接口/基类的说明。

#### class `ZevisionClient` : IDisposable
与一台 IMC300 的 Zevision 通讯:按构造时给出的通讯参数 (串口或 TCP、帧格式、响应超时) 连接, 发送命令并等待响应。断开后可以用同一参数再次连接。
- `ZevisionClient(ConnectionOptions options)`: 创建客户端。连接时按 `Endpoint` 的类型打开串口或 TCP 连接 (`Create`)。
- `ZevisionClient(ConnectionOptions options, Func<ConnectionEndpoint, IDeviceTransport> transportFactory)`: 用自定义传输创建客户端 (测试,或串口/TCP 以外的连接方式)。每次连接时调用 `transportFactory` 创建传输,断开 (或连接失败) 时调用它的 `Close`。
- `ConnectionOptions Options { get; }`: 通讯参数。
- `bool IsConnected { get; }`: 是否已连接。
- `event EventHandler<FrameSentEventArgs> FrameSent`: 帧发送前触发 (后台线程,发送方的线程)。
- `event EventHandler<FrameReceivedEventArgs> FrameReceived`: 收到一帧 (含错误帧、帧外数据和超时时未收完的数据) 时触发 (后台线程;依次触发,不会同时进入)。
- `event EventHandler<ConnectionLostEventArgs> ConnectionLost`: 连接意外中断 (串口拔出、TCP 断开、读取出错) 时触发 (后台线程)。主动断开不触发。 触发时连接已经关闭,`IsConnected` 为 false。
- `event EventHandler<HandlerFailedEventArgs> HandlerFailed`: `FrameSent`、`FrameReceived` 或 `ConnectionLost` 的处理程序抛出异常时触发 (后台线程)。 这个事件自身的处理程序抛出的异常被忽略。
- `Task<ZevisionResult> ConnectAsync(CancellationToken cancellationToken = default)`: 打开连接并开始接收。
- `Task DisconnectAsync()`: 关闭连接,并等待读取循环结束。正在等待的请求以 `Disconnected` 结束。未连接时直接返回。
- `Task<ZevisionResult<ZevisionReply>> SendAsync(string command, CancellationToken cancellationToken = default)`: 发送命令并等待响应。
- `Task<ZevisionResult<ZevisionReply>> SendAsync(string command, TimeSpan minimumResponseTimeout, CancellationToken cancellationToken = default)`: 发送命令并等待响应,响应空闲超时至少为 `minimumResponseTimeout`; `ResponseTimeout` 更长时用它。用于已知响应前或分包之间会停顿的指令, 如 `?` (完整帮助约 116 KB,首字节约 0.4 s,包间约 0.35 s,建议至少 2 s)。
- `void Dispose()`: 断开连接并释放客户端。之后调用 `ConnectAsync` 会抛出 `ObjectDisposedException`, 发送返回 `NotConnected`。

#### class `ZevisionError`
`ZevisionClient` 操作失败的详细信息。`Message` 是英文说明; 其他属性给出生成本地化文本所需的数据。
- `ZevisionError(ZevisionErrorKind kind, string message)`: 创建错误。
- `ZevisionErrorKind Kind { get; }`: 类型。
- `string Message { get; }`: 英文说明。
- `Exception Exception { get; init; }`: 引起错误的异常 (`ConnectFailed`、`SendFailed`、 `InvalidCommand`);没有时为 null。
- `ConnectionEndpoint Endpoint { get; init; }`: 连接目标 (`ConnectFailed`)。
- `TimeSpan Timeout { get; init; }`: 本次请求使用的空闲超时 (`Timeout`)。
- `bool ReceivedIncompleteData { get; init; }`: 超时前是否收到了不完整的数据;这些数据已通过 `FrameReceived` 报告。
- `FrameError FrameError { get; init; }`: 帧错误 (`InvalidFrame`)。
- `int DataLength { get; init; }`: 响应数据的字节数 (`UnparseableResponse`)。
- `string ToString()`: 同接口/基类的说明。

#### enum `ZevisionErrorCode` : IComparable, IFormattable, IConvertible
设备否定响应 (`-`) 附带的错误代码。
- `None` = 0: 无错误 (肯定响应)。
- `IllegalCommand` = 1: A:非法命令,不支持该命令。
- `IllegalArgument` = 2: B:非法参数值。
- `ChecksumError` = 3: C:校验和错误 (仅启用校验和时)。
- `IllegalFormat` = 4: D:非法格式。
- `StateError` = 5: F:状态错误,系统处于错误状态。
- `LengthError` = 6: L:长度错误 (仅启用长度时)。
- `Timeout` = 7: T:超时,收到起始符后 200 ms 内未收到停止符。
- `Unknown` = 8: 否定响应,但没有或无法识别错误代码。

#### static class `ZevisionErrorCodeExtensions`
`ZevisionErrorCode` 的辅助方法。
- `static string Describe(ZevisionErrorCode code)`: 返回错误代码的英文说明,如 `Illegal parameter value`。

#### enum `ZevisionErrorKind` : IComparable, IFormattable, IConvertible
`ZevisionClient` 操作失败的类型。调用方按类型分支或生成自己的提示文本。
- `AlreadyConnected` = 0: 已经连接。
- `ConnectFailed` = 1: 无法打开连接 (端口不存在或被占用、连接被拒绝、连接超时)。见 `Exception`。
- `NotConnected` = 2: 未连接。
- `InvalidCommand` = 3: 命令不合法 (为空、含非法字符或超长),没有发送。
- `SendFailed` = 4: 写入失败。见 `Exception`。
- `Timeout` = 5: 在 `Timeout` 内没有收到数据 (空闲超时)。
- `Disconnected` = 6: 等待响应时连接已断开 (主动断开或连接中断)。
- `InvalidFrame` = 7: 响应帧无效 (校验和、长度等)。见 `FrameError`。
- `UnparseableResponse` = 8: 响应帧有效,但内容无法按 Zevision 响应格式解析。

#### class `ZevisionFrame`
解码器输出的一条完整消息 (可能由多个包组成,可能带错误)。
- `ZevisionFrame(byte[] raw, byte[] data, IReadOnlyList<ByteRange> dataRanges, int packetCount, IReadOnlyList<int> declaredLengths, FrameError error)`: 创建帧。
- `IReadOnlyList<int> DeclaredLengths { get; }`: 各包长度字段的值,按包顺序 (未启用长度时为空)。用于诊断分包问题。
- `byte[] Raw { get; }`: 原始字节,用于显示。
- `byte[] Data { get; }`: 命令数据 (不含起止符、长度、校验和、包分隔符)。
- `IReadOnlyList<ByteRange> DataRanges { get; }`: 每个包的命令数据在 `Raw` 中的位置,用于显示时区分数据和帧结构字节。
- `int PacketCount { get; }`: 消息由几个包组成。
- `FrameError Error { get; }`: 解码错误。
- `bool IsValid { get; }`: 帧是否有效。
- `string Payload { get; }`: 命令数据的文本。设备的说明文字含 UTF-8 字符 (如 `Å`),因此按 UTF-8 解码;纯 ASCII 时与 ASCII 解码相同。 首次读取时解码并缓存:大响应 (`?` 约 116 KB) 在回显匹配和响应解析中都要用,避免重复解码出两份大字符串。

#### static class `ZevisionFrameCodec`
Zevision 帧的编码与校验和计算。显示格式化由调用方负责。
- `const byte StartByte`: 起始符 `{`。
- `const byte StopByte`: 停止符 `}`。
- `const int MaxDataLength`: 命令数据的最大长度 (字节)。
- `const string FillerCommand`: 附加到命令末尾、用来改变校验和的无副作用指令 (Hello:只读,总是成功)。
- `static byte[] Encode(string commandData, FrameOptions options)`: 将命令数据编码为完整帧。
- `static byte[] EncodeAvoidingDelimiters(string command, FrameOptions options, out string sentCommand)`: 编码命令;若帧头/帧尾的二进制字节 (长度、校验和) 恰好等于帧分隔符,则在命令后追加 `;H01` 后重新编码。
- `static bool HasDelimiterInBinaryFields(byte[] frame, FrameOptions options)`: 帧中的长度字节或校验和是否等于帧分隔符 `{ } < >`。
- `static byte ComputeChecksum(byte[] data, int offset, int count)`: 计算校验和:对命令数据逐字节求和,取模 256。

#### class `ZevisionFrameDecoder`
从串口字节流中拆出 Zevision 消息。一次读取可能只有半帧,也可能包含多帧,解码器会跨调用累积。
- `ZevisionFrameDecoder(FrameOptions options)`: 创建解码器。
- `const int MaxReceiveLength`: 一条消息 (所有包合计) 的命令数据上限,防止异常数据无限累积。
- `const byte ContinueByte`: 包结束、后面还有续包。
- `const byte ContinuationStartByte`: 续包开始。
- `IReadOnlyList<ZevisionFrame> Push(byte[] buffer, int offset, int count)`: 送入新收到的字节,返回其中已完整的消息 (按接收顺序)。
- `IReadOnlyList<ZevisionFrame> FlushIncomplete()`: 把尚未完成的消息 (以 `Incomplete`) 和帧外数据立即输出,并回到等待起始符状态。 用于请求超时后显示已收到的内容,便于诊断。
- `void Reset()`: 丢弃尚未完成的消息和帧外数据。

#### class `ZevisionReply`
一次请求的完整回复:原始帧及其中每条命令的响应。
- `ZevisionReply(ZevisionFrame frame, IReadOnlyList<ZevisionResponse> responses)`: 创建回复。
- `ZevisionFrame Frame { get; }`: 收到的响应帧。
- `IReadOnlyList<ZevisionResponse> Responses { get; }`: 按命令顺序排列的响应。

#### class `ZevisionResponse`
一帧响应中单条命令的响应,格式为:命令回显 + 状态(+/-) [+ 错误代码] [: 数据]。
- `ZevisionResponse(string command, bool isSuccess, ZevisionErrorCode errorCode, char? rawErrorCode, string data)`: 创建响应。
- `string Command { get; }`: 设备回显的命令 (数据分隔符之前的部分)。
- `bool IsSuccess { get; }`: 是否为肯定响应 (`+`)。
- `ZevisionErrorCode ErrorCode { get; }`: 错误代码;肯定响应时为 `None`。
- `char? RawErrorCode { get; }`: 原始错误代码字符;没有时为 null。
- `string Data { get; }`: 查询返回的数据 (`:` 之后的部分);没有时为 null。

#### static class `ZevisionResponseParser`
解析响应帧的命令数据。
- `static IReadOnlyList<ZevisionResponse> Parse(string payload)`: 将响应帧的命令数据解析为单条响应列表。
- `static bool TryParse(string payload, out IReadOnlyList<ZevisionResponse> responses)`: 尝试解析;格式错误时返回 false (不抛出异常)。
- `static string EchoOf(string command)`: 设备对一条指令的回显:第一条命令 (`;` 之前) 中数据分隔符 `:` 之前的部分, 如 `UG1304:1;H01` → `UG1304`。
- `static bool IsReplyTo(ZevisionFrame frame, string expectedEcho)`: 这一帧是否是对回显为 `expectedEcho` 的请求的响应。 无法判断时 (帧无效、无法解析、设备未回显命令,如 `-C`) 视为是,交给调用方报告。

#### struct `ZevisionResult`
不带返回值的操作结果。预期内的失败 (端口被占用、超时等) 不抛异常,而是返回失败结果。
- `bool IsSuccess { get; }`: 是否成功。
- `ZevisionError Error { get; }`: 失败原因;成功时为 null。
- `static ZevisionResult Ok()`: 成功结果。
- `static ZevisionResult Fail(ZevisionError error)`: 失败结果。
- `string ToString()`: 同接口/基类的说明。

#### struct `ZevisionResult<T>`
带返回值的操作结果。
- `bool IsSuccess { get; }`: 是否成功。
- `ZevisionError Error { get; }`: 失败原因;成功时为 null。
- `T Value { get; }`: 返回值。
- `static ZevisionResult<T> Ok(T value)`: 成功结果。
- `static ZevisionResult<T> Fail(ZevisionError error)`: 失败结果。
- `string ToString()`: 同接口/基类的说明。

### 13.2 Zevision.Commands

#### class `BilingualText`
一段中英文文本。
- `BilingualText(string chinese, string english)`: 创建文本。
- `static readonly BilingualText Empty`: 空文本。
- `string Chinese { get; }`: 中文。
- `string English { get; }`: 英文。
- `static BilingualText Same(string text)`: 中英文相同的文本 (如 `Z-ratio`、`Off`)。
- `string Get(TextLanguage language)`: 取指定语言的文本。
- `string ToString()`: 英文。

#### enum `CommandActions` : IComparable, IFormattable, IConvertible
指令支持的动作 (可组合)。`None` 表示指令本身不带动作字母,如 `H01`、`E:data`。
- `None` = 0: 无动作字母,直接发送命令 ID。
- `Query` = 1: Q,查询。
- `Update` = 2: U,更新。
- `Add` = 4: A,添加:在列表末尾追加一项 (如 `AM00`),或按编号创建对象 (如 `AM03`)。
- `Delete` = 8: D,删除:删除列表的最后一项 (如 `DM00`),或按编号删除对象 (如 `DM03`)。
- `Status` = 16: S,读取运行时状态 (带同步计数)。
- `Remote` = 32: R,远程执行动作。

#### class `CommandDefinition`
一条指令的定义:命令模式、支持的动作、说明、单位、取值范围或选项。
- `const int MinIndex`: 编号的最小值。00 是列表的"添加/删除"命令,不是对象编号。
- `const int MaxIndex`: 编号的最大值。FE 是列表的"数量"项,FF 保留,都不是对象编号。
- `string Pattern { get; }`: 命令模式,如 `M**03`。
- `CommandGroup Group { get; }`: 命令组 (模式的第一个字符)。
- `CommandActions Actions { get; }`: 支持的动作。
- `string ChineseName { get; }`: 中文说明 (与《U 类型指令汇总》一致)。
- `string EnglishName { get; }`: 英文说明:设备 `?` 帮助表的 DESCRIPTION (IMC300 固件 1.8.11)。
- `CommandValueKind Kind { get; }`: 值的类型。
- `BilingualText Unit { get; }`: 单位,如 `g/cm³`、`秒`/`s`;没有时为空。
- `decimal? Min { get; }`: 最小值:数字为数值,时间为秒数。
- `decimal? Max { get; }`: 最大值:数字为数值,时间为秒数。
- `bool IsInteger { get; }`: 是否只能是整数 (仅数字)。
- `IReadOnlyList<CommandOption> Options { get; }`: 固定选项 (枚举,或带参数的动作如 G16)。
- `char ReferenceGroup { get; }`: 被引用对象的组字母,如 `M` (仅引用)。
- `bool AllowNone { get; }`: 引用是否可以为 0 (未选择)。
- `BilingualText Remark { get; }`: 补充说明,如 `0=disabled`、与实机不同之处;没有时为空。
- `int IndexCount { get; }`: 模式中 `**` 编号的级数,即 `Build` 需要的编号个数。
- `bool FirstIndexIsObject { get; }`: 第一级编号是否是对象编号:`**` 紧跟在组字母后 (`M**03`) 或是工艺 (`P02**`)。 否则是固定对象下的序号,如 `T0101**` 的输入编号。
- `bool CanUpdate { get; }`: 是否可以更新 (U)。
- `string GetName(TextLanguage language)`: 说明:中文为"密度",英文为设备帮助表中的 DESCRIPTION,如 `Density (g/cm^3)`。
- `IReadOnlyList<string> GetIndexNames(TextLanguage language)`: 各级编号的含义,如 `F**03**01` → 膜系、预熔 / Film、Pre Condition。
- `string GetLabel(TextLanguage language)`: 带单位的名称,如 `密度 (g/cm³)`。英文的设备说明多已带单位 (如 `Density (g/cm^3)`), 已含括号时不再追加单位。
- `string GetRangeText(TextLanguage language)`: 范围或选项说明,如 `0.100 ~ 100.000 g/cm³`、`0=Off, 1=On`;没有时为说明或空。
- `bool IsMatch(string id)`: 命令 ID 是否属于这条指令,如 `M0103` 属于 `M**03` (`**` 为两位大写十六进制;区分大小写)。
- `string LabelFor(string id, TextLanguage language, string deviceDescription = null)`: 某一项的显示名称:子列表中的项在前面加上列表名和序号,以区分同名的项, 如 `F01030201` → `预熔 2 · 起始斜坡/上升时间`。第一级编号是对象本身时不显示 (`T010103` 的第一级是输入编号:`输入 3 · 基本事件`)。
- `IReadOnlyList<int> ParseIndices(string id)`: 从命令 ID 中取出各级编号 (十进制),如 `M**03` 的 `M0A03` → [10]。
- `string FormatId(params int[] indices)`: 生成命令 ID:按顺序把编号 (十进制,如材料 10) 以两位十六进制填入 `**`, 如 `M**03` 与 10 → `M0A03`。
- `string Build(CommandActions action, params int[] indices)`: 生成不带数据的指令,如 `Build(Query, 1)` → `QM0103`、`Build(Remote)` → `RC03`、 `Build(None)` → `H01`。
- `string Build(CommandActions action, string value, params int[] indices)`: 生成带数据的指令,如 `Build(Update, "2.700", 1)` → `UM0103:2.700`、`Build(None, "abc")` → `E:abc`。 只检查数据中没有协议不允许的字符 (`ValidateValue`),不检查范围。
- `string ToString()`: 同接口/基类的说明。
- `bool Supports(CommandActions action)`: 是否支持该动作。`None` 只适用于不带动作字母的指令 (H01、E)。

#### enum `CommandGroup` : IComparable, IFormattable, IConvertible
命令组:命令 ID 的第一个字符 (协议文档 1.5)。
- `C` = 0: Process Control,工艺控制。
- `E` = 1: Echo,回显。
- `F` = 2: Film,膜系。
- `G` = 3: General,通用。
- `H` = 4: Hello,握手/版本。
- `I` = 5: Inputs,输入。
- `L` = 6: Layer,膜层。
- `M` = 7: Materials,材料。
- `N` = 8: Sensors,传感器 (QCM)。
- `O` = 9: Outputs,输出。
- `P` = 10: Process,工艺。
- `S` = 11: Sources,输出源。
- `T` = 12: Logic,逻辑 (协议文档未列出具体指令)。

#### class `CommandOption`
固定选项中的一项。
- `CommandOption(string value, string text)`: 创建选项 (中英文相同的含义,如 `Off`)。
- `CommandOption(string value, BilingualText text)`: 创建选项。
- `string Value { get; }`: 发送给设备的值。
- `BilingualText Text { get; }`: 含义,如 `Off` / `关`;没有时为空。
- `string GetLabel(TextLanguage language)`: 显示文本,如 `0 - Off`;没有含义时只有值。
- `string ToString()`: 英文显示文本,如 `0 - Off`。

#### enum `CommandValueKind` : IComparable, IFormattable, IConvertible
指令数据 (值) 的类型。
- `None` = 0: 没有值 (动作命令、列表节点)。
- `Text` = 1: 字符串。
- `Number` = 2: 数字,可带范围。
- `Enum` = 3: 固定选项,如 `0=Off, 1=On`。
- `Time` = 4: 时间 `H:MM:SS`;范围见 `Min`/`Max` (秒数)。
- `Reference` = 5: 引用另一个对象的编号 (十进制),如膜系的材料。

#### enum `TextLanguage` : IComparable, IFormattable, IConvertible
文本的语言。指令集中的名称、单位、选项等同时有中英文,由调用方选择。
- `English` = 0: 英文 (设备帮助表的说明)。
- `Chinese` = 1: 中文。

#### static class `ZevisionCommandSet`
Zevision 指令集:每条指令的命令模式、动作、说明、单位和取值范围。用法: ZevisionCommandSet.TryGetGroup("M0103", out var group); // group = CommandGroup.M var commands = ZevisionCommandSet.GetCommands(group); // 材料组的全部指令 var density = ZevisionCommandSet.Find("M0103"); // M**03 密度 density.Build(CommandActions.Update, "2.700", 1); // "UM0103:2.700" ZevisionCommandSet.Update("M0103", "2.700"); // 已有完整 ID 时:"UM0103:2.700"
- `const string HelpCommand`: 帮助指令:返回全部设置项及当前值的帮助表 (约 116 KB)。不属于任何命令组。
- `static IReadOnlyList<CommandDefinition> All { get; }`: 全部指令 (只读),按协议文档的顺序。
- `static IReadOnlyList<CommandDefinition> GetCommands(CommandGroup group)`: 取一个命令组的全部指令 (只读),如 `M` → 材料的全部指令 (M00、M**、M**01、M**03…)。
- `static bool TryGetGroup(string id, out CommandGroup group)`: 由命令 ID 的第一个字符得到命令组,如 `M0103` → `M`。不区分大小写。
- `static CommandDefinition Find(string id)`: 查找命令 ID 对应的指令,如 `M0103` → `M**03` (密度)。只在该 ID 所属命令组中查找; 匹配多个模式时取固定字符最多的 (如 `MFE` 是材料数量而不是 `M**`)。ID 不带动作字母,区分大小写 (设备只认大写)。
- `static bool IsWellFormedId(string id)`: 命令 ID 的格式是否正确:大写组字母后跟若干两位大写十六进制编号,如 `M0103`、`P02FE`、`E`。 不检查组字母是否是已知的命令组。
- `static string Build(CommandActions action, string id, string value = null)`: 由完整的命令 ID 生成指令,如 `Build(Update, "M0103", "2.700")` → `UM0103:2.700`。 ID 在指令集中有定义时检查动作是否支持;没有定义的 ID (如逻辑组、文档未列出的设置项) 只检查格式,由设备判断。
- `static string Query(string id)`: 查询指令,如 `QM0103`。
- `static string Update(string id, string value)`: 更新指令,如 `UM0103:2.700`。
- `static string Add(string id)`: 添加指令,如 `AM03` (按编号创建材料)、`AF010300` (在膜系 F01 的预熔列表末尾追加)。
- `static string ActionLetter(CommandActions action)`: 动作字母:Query → `Q`,None → 空。
- `static string ObjectKind(char group, TextLanguage language)`: 对象类型的名称,如 `M` → 材料 / Material;未知时为"对象"/Object。
- `static BilingualText IndexName(string prefixPattern)`: 某一级编号 (列表) 的名称,参数为该级 `**` 之前的模式,如 `F**03` → 预熔 / Pre Condition、 `P02**07` → 膜层 / Layer;未收录时为 `#`。名称可能含编号占位符 `{0}` (如 `F**07`)。
