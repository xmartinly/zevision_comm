using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Communication;
using Zevision.Core;
using Zevision.Core.Configuration;
using Zevision.Core.Devices;

namespace Zevision.Tests
{
    /// <summary>
    /// 模拟设备的 <c>?</c>、<c>U</c>、<c>Q</c> 行为,用于测试 <see cref="InstrumentConfigService"/>。
    /// </summary>
    internal sealed class FakeDeviceService : IDeviceService
    {
        /// <summary>帮助表的条目 (ID → (值, 说明)),保持插入顺序。</summary>
        private readonly List<KeyValuePair<string, (string Value, string Description)>> _entries =
            new List<KeyValuePair<string, (string Value, string Description)>>();

        public event EventHandler<FrameSentEventArgs>? FrameSent;

        public event EventHandler<FrameReceivedEventArgs>? FrameReceived;

        public event EventHandler<ConnectionLostEventArgs>? ConnectionLost;

        public event EventHandler<RetryEventArgs>? Retrying;

        public event EventHandler<CommandsFailedEventArgs>? CommandsFailed;

        public bool IsConnected => true;

        /// <summary>收到的全部指令。</summary>
        public List<string> Commands { get; } = new List<string>();

        /// <summary>对这些 ID 的写入返回 <c>-B</c>。</summary>
        public HashSet<string> RejectWrites { get; } = new HashSet<string>();

        /// <summary>对这些 ID 的写入返回 <c>-F</c> (设备状态不允许)。</summary>
        public HashSet<string> StateErrorWrites { get; } = new HashSet<string>();

        /// <summary>对这些 ID 的写入返回成功但不生效 (用于测试读回校验)。</summary>
        public HashSet<string> IgnoreWrites { get; } = new HashSet<string>();

        /// <summary>在第 N 条写入指令之前触发的回调 (用于测试取消)。</summary>
        public Action<int>? BeforeWrite { get; set; }

        /// <summary>
        /// <c>A&lt;对象&gt;</c> 创建对象时添加的条目 (含默认值)。键为组字母;没有模板的组返回 <c>-A</c>。
        /// </summary>
        public Dictionary<char, Func<string, IEnumerable<(string Id, string Value, string Description)>>> Templates { get; } =
            new Dictionary<char, Func<string, IEnumerable<(string, string, string)>>>();

        /// <summary>删除对象时实际删除的另一个对象 (模拟设备删错)。</summary>
        public Dictionary<string, string> DeleteInstead { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>对这些对象的创建返回 <c>-B</c>。</summary>
        public HashSet<string> RejectCreates { get; } = new HashSet<string>();

        /// <summary>
        /// <c>A&lt;列表&gt;00</c> 追加子列表条目时添加的条目;参数为新条目 ID。为 null 时追加指令成功但什么也不添加。
        /// </summary>
        public Func<string, IEnumerable<(string Id, string Value, string Description)>>? AppendTemplate { get; set; }

        /// <summary>删除对象及其所有设置项 (模拟用户在仪器上删除材料/膜层)。</summary>
        public FakeDeviceService Remove(string itemId)
        {
            _entries.RemoveAll(e => e.Key.StartsWith(itemId, StringComparison.Ordinal));
            return this;
        }

        public FakeDeviceService Set(string id, string value)
        {
            var index = _entries.FindIndex(e => e.Key == id);
            _entries[index] = new KeyValuePair<string, (string, string)>(id, (value, _entries[index].Value.Description));
            return this;
        }

        public FakeDeviceService With(string id, string value, string description = "")
        {
            _entries.Add(new KeyValuePair<string, (string, string)>(id, (value, description)));
            return this;
        }

        public string? Get(string id) => _entries.Where(e => e.Key == id).Select(e => e.Value.Value).FirstOrDefault();

        /// <summary>每次连接的参数。</summary>
        public List<ConnectionOptions> Connections { get; } = new List<ConnectionOptions>();

        /// <summary>连接的结果 (参数为第几次连接,从 1 开始);为 null 时成功。</summary>
        public Func<int, Result>? ConnectResult { get; set; }

        public Task<Result> ConnectAsync(ConnectionOptions options, CancellationToken cancellationToken)
        {
            Connections.Add(options);
            return Task.FromResult(ConnectResult?.Invoke(Connections.Count) ?? Result.Ok());
        }

        /// <summary>模拟连接意外中断。</summary>
        public void RaiseConnectionLost(ConnectionLostEventArgs e) => ConnectionLost?.Invoke(this, e);

        /// <summary>模拟一次重发。</summary>
        public void RaiseRetrying(RetryEventArgs e) => Retrying?.Invoke(this, e);

        /// <summary>模拟一次有指令失败的发送。</summary>
        public void RaiseCommandsFailed(CommandsFailedEventArgs e) => CommandsFailed?.Invoke(this, e);

        /// <summary>模拟发送一帧 (发送指令本身不触发收发事件)。</summary>
        public void RaiseFrameSent(FrameSentEventArgs e) => FrameSent?.Invoke(this, e);

        /// <summary>模拟收到一帧。</summary>
        public void RaiseFrameReceived(FrameReceivedEventArgs e) => FrameReceived?.Invoke(this, e);

        public Task DisconnectAsync() => Task.CompletedTask;

        /// <summary>每次握手时先尝试的帧格式。</summary>
        public List<FrameOptions> Handshakes { get; } = new List<FrameOptions>();

        /// <summary>握手的结果;为 null 时成功,设备为 IMC300、帧格式为先尝试的格式。</summary>
        public Func<FrameOptions, Result<DeviceIdentity>>? Handshake { get; set; }

        public Task<Result<DeviceIdentity>> HandshakeAsync(FrameOptions preferred, CancellationToken cancellationToken)
        {
            Handshakes.Add(preferred);
            return Task.FromResult(Handshake?.Invoke(preferred)
                ?? Result<DeviceIdentity>.Ok(new DeviceIdentity("Zevision IMC300 1.8.11", DeviceIdentity.ExpectedModel, "XXXXXXXX", preferred, Array.Empty<string>())));
        }

        /// <summary>收到的帧 (多条指令合并的帧在 <see cref="Commands"/> 中按单条记录)。</summary>
        public List<string> Frames { get; } = new List<string>();

        /// <summary>每帧请求的最低响应超时 (没有指定时为 <see cref="TimeSpan.Zero"/>),与 <see cref="Frames"/> 一一对应。</summary>
        public List<TimeSpan> MinimumTimeouts { get; } = new List<TimeSpan>();

        /// <summary>用 <see cref="SendHiddenAsync"/> (不显示) 发送的帧。</summary>
        public List<string> HiddenFrames { get; } = new List<string>();

        public Task<Result<ZevisionReply>> SendHiddenAsync(string command, TimeSpan minimumResponseTimeout, CancellationToken cancellationToken)
        {
            HiddenFrames.Add(command);
            return SendAsync(command, minimumResponseTimeout, cancellationToken);
        }

        public Task<Result<ZevisionReply>> SendAsync(string command, TimeSpan minimumResponseTimeout, CancellationToken cancellationToken)
        {
            MinimumTimeouts.Add(minimumResponseTimeout);
            cancellationToken.ThrowIfCancellationRequested();
            Frames.Add(command);

            // 与实机一致:一帧中的多条指令 (; 分隔) 逐条执行,响应用 ; 连接。
            var payload = string.Join(";", command.Split(';').Select(c => Respond(c, cancellationToken)));
            var data = Encoding.ASCII.GetBytes(payload);
            var frame = new ZevisionFrame(data, data, Array.Empty<ByteRange>(), 1, Array.Empty<int>(), FrameError.None);
            return Task.FromResult(Result<ZevisionReply>.Ok(new ZevisionReply(frame, ZevisionResponseParser.Parse(payload))));
        }

        /// <summary>收到的指令的副本;可以在其他线程发送时调用 (如重复发送的测试)。</summary>
        public IReadOnlyList<string> CommandsSnapshot()
        {
            lock (Commands)
            {
                return Commands.ToList();
            }
        }

        private string Respond(string command, CancellationToken cancellationToken)
        {
            lock (Commands)
            {
                Commands.Add(command);
            }

            string payload;
            if (command == "?")
            {
                payload = "?+:" + BuildHelpTable();
            }
            else if (command.Length == 2 && command[0] == '?')
            {
                // 组帮助 (?M 等):只列该组。
                payload = command + "+:" + BuildHelpTable(command[1]);
            }
            else if (command.StartsWith("D", StringComparison.Ordinal) && command.Length > 4 && command.EndsWith("00", StringComparison.Ordinal) && Get(command.Substring(1)) != null)
            {
                // 从子列表末尾删除 (DF010300):删除最后一条及其子项,并更新计数;列表为空时 -F。
                var list = command.Substring(1, command.Length - 3);
                var count = int.Parse(Get(list + "FE") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
                if (count == 0)
                {
                    payload = command + "-F";
                }
                else
                {
                    Remove(list + count.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                    Set(list + "FE", (count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    payload = command + "+";
                }
            }
            else if (command.StartsWith("D", StringComparison.Ordinal))
            {
                // 按编号删除对象 (DM03):删除该对象的所有项。DeleteInstead 模拟设备删错对象。
                var itemId = command.Substring(1);
                if (Get(itemId) == null)
                {
                    payload = command + "-D";
                }
                else
                {
                    Remove(DeleteInstead.TryGetValue(itemId, out var other) ? other : itemId);
                    payload = command + "+";
                }
            }
            else if (command.StartsWith("U", StringComparison.Ordinal))
            {
                BeforeWrite?.Invoke(Commands.Count(c => c.StartsWith("U", StringComparison.Ordinal)));
                cancellationToken.ThrowIfCancellationRequested();
                var separator = command.IndexOf(':');
                var id = command.Substring(1, separator - 1);
                var value = command.Substring(separator + 1);
                var index = _entries.FindIndex(e => e.Key == id);
                if (index < 0)
                {
                    payload = "U" + id + "-A";
                }
                else if (StateErrorWrites.Contains(id))
                {
                    payload = "U" + id + "-F";
                }
                else if (RejectWrites.Contains(id)
                    || (RecipeRules.TryGetReference(id, value, out var target, out _) && Get(target) == null))
                {
                    // 与实机一致:引用不存在的对象返回 B 非法参数值。
                    payload = "U" + id + "-B";
                }
                else
                {
                    if (!IgnoreWrites.Contains(id))
                    {
                        _entries[index] = new KeyValuePair<string, (string, string)>(id, (value, _entries[index].Value.Description));
                    }

                    payload = "U" + id + "+";
                }
            }
            else if (command.StartsWith("A", StringComparison.Ordinal))
            {
                var itemId = command.Substring(1);
                if (itemId.Length > 3 && itemId.EndsWith("00", StringComparison.Ordinal) && Get(itemId) != null)
                {
                    // 追加到子列表末尾:新条目编号 = 现有数量 + 1,并更新计数。
                    var list = itemId.Substring(0, itemId.Length - 2);
                    var count = int.Parse(Get(list + "FE") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
                    var newItem = list + (count + 1).ToString("X2", System.Globalization.CultureInfo.InvariantCulture);
                    if (AppendTemplate != null)
                    {
                        foreach (var (id, value, description) in AppendTemplate(newItem))
                        {
                            With(id, value, description);
                        }

                        if (Get(list + "FE") != null)
                        {
                            Set(list + "FE", (count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                    }

                    payload = command + "+";
                }
                else if (Get(itemId) != null || RejectCreates.Contains(itemId))
                {
                    payload = command + "-B";
                }
                else if (Templates.TryGetValue(itemId[0], out var template))
                {
                    foreach (var (id, value, description) in template(itemId))
                    {
                        With(id, value, description);
                    }

                    payload = command + "+";
                }
                else
                {
                    payload = command + "-A";
                }
            }
            else if (command.StartsWith("Q", StringComparison.Ordinal))
            {
                var id = command.Substring(1);
                var value = Get(id);
                payload = value == null ? command + "-A" : command + "+:" + value;
            }
            else
            {
                payload = command + "-A";
            }

            return payload;
        }

        public void Dispose()
        {
        }

        /// <summary>按实测格式 (定宽 30 列、LF 分隔) 生成帮助表;指定 <paramref name="group"/> 时只列该组 (组帮助)。</summary>
        public string BuildHelpTable(char? group = null)
        {
            var sb = new StringBuilder("\n");
            sb.Append(Row("[CMD ID]", "[VALUE]", "[DESCRIPTION]"));
            foreach (var entry in _entries.Where(e => group == null || e.Key[0] == group))
            {
                sb.Append(Row(entry.Key, entry.Value.Value, entry.Value.Description));
            }

            return sb.ToString();
        }

        public static string Row(string id, string value, string description) =>
            id.PadRight(30) + value.PadRight(30) + description + "\n";
    }
}
