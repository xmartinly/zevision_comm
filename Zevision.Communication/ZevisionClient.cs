using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Zevision.Communication
{
    /// <summary>
    /// 与一台 IMC300 的 Zevision 通讯:按构造时给出的通讯参数 (串口或 TCP、帧格式、响应超时) 连接,
    /// 发送命令并等待响应。断开后可以用同一参数再次连接。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>一个后台读取循环负责拆帧 (含多包消息的重组);同一时间只有一个请求在等待响应,并发调用会排队。</item>
    /// <item>响应按命令回显与请求对应:回显不符的帧 (超时后迟到的响应、取消 <c>?</c> 后设备仍在发送的剩余数据)
    /// 作为未请求数据报告 (<see cref="FrameReceivedEventArgs.IsSolicited"/> 为 false),不会被当成当前请求的响应。</item>
    /// <item>帧的长度或校验和字节等于 <c>{ } &lt; &gt;</c> 时自动追加 <see cref="ZevisionFrameCodec.FillerCommand"/>
    /// (设备会把这样的字节当成分隔符),多出的 H01 响应不返回给调用方。</item>
    /// <item><b>所有事件都在后台线程上触发。</b><see cref="FrameReceived"/> 依次触发,不会同时进入两个处理程序;
    /// 处理程序应尽快返回 (如把数据交给界面线程),不要在其中同步等待发送。
    /// 处理程序抛出的异常不会中断通讯,而是通过 <see cref="HandlerFailed"/> 报告。</item>
    /// <item>本类不写任何日志或文件;需要记录的内容 (收发的帧、连接中断的原因、事件处理程序的异常) 都通过事件交给调用方。</item>
    /// <item>预期内的失败 (端口被占用、超时、设备无响应等) 以 <see cref="ZevisionResult"/> 返回,说明为英文;
    /// 参数错误 (null 等) 抛出异常。</item>
    /// </list>
    /// </remarks>
    public sealed class ZevisionClient : IDisposable
    {
        private const int ReadBufferSize = 4096;

        /// <summary>一次等待的上限 (<see cref="Task.Delay(TimeSpan)"/> 不接受超过 int.MaxValue 毫秒);更长的超时分多次等待。</summary>
        private static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(int.MaxValue - 1);

        private readonly Func<ConnectionEndpoint, IDeviceTransport> _transportFactory;

        // 不释放:SemaphoreSlim 只有在用到 AvailableWaitHandle 时才持有非托管资源;释放它会让排队中的发送永远等待。
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        /// <summary>保护 <see cref="_session"/>、<see cref="_connecting"/>、<see cref="_disposed"/> 和会话的 <see cref="Session.Pending"/>。</summary>
        private readonly object _sync = new object();
        private Session? _session;
        private bool _connecting;
        private bool _disposed;

        /// <summary>
        /// 创建客户端。连接时按 <see cref="ConnectionOptions.Endpoint"/> 的类型打开串口或 TCP 连接
        /// (<see cref="DeviceTransports.Create"/>)。
        /// </summary>
        /// <param name="options">通讯参数:连接目标、帧格式 (须与设备 G1304/G1305 或 G1407/G1408 一致)、响应超时。</param>
        public ZevisionClient(ConnectionOptions options)
            : this(options, DeviceTransports.Create)
        {
        }

        /// <summary>
        /// 用自定义传输创建客户端 (测试,或串口/TCP 以外的连接方式)。每次连接时调用 <paramref name="transportFactory"/>
        /// 创建传输,断开 (或连接失败) 时调用它的 <see cref="IDeviceTransport.Close"/>。
        /// </summary>
        /// <param name="options">通讯参数。</param>
        /// <param name="transportFactory">按连接目标创建 (尚未打开的) 传输。</param>
        public ZevisionClient(ConnectionOptions options, Func<ConnectionEndpoint, IDeviceTransport> transportFactory)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        }

        /// <summary>帧发送前触发 (后台线程,发送方的线程)。</summary>
        public event EventHandler<FrameSentEventArgs>? FrameSent;

        /// <summary>收到一帧 (含错误帧、帧外数据和超时时未收完的数据) 时触发 (后台线程;依次触发,不会同时进入)。</summary>
        public event EventHandler<FrameReceivedEventArgs>? FrameReceived;

        /// <summary>
        /// 连接意外中断 (串口拔出、TCP 断开、读取出错) 时触发 (后台线程)。主动断开不触发。
        /// 触发时连接已经关闭,<see cref="IsConnected"/> 为 false。
        /// </summary>
        public event EventHandler<ConnectionLostEventArgs>? ConnectionLost;

        /// <summary>
        /// <see cref="FrameSent"/>、<see cref="FrameReceived"/> 或 <see cref="ConnectionLost"/> 的处理程序抛出异常时触发 (后台线程)。
        /// 这个事件自身的处理程序抛出的异常被忽略。
        /// </summary>
        public event EventHandler<HandlerFailedEventArgs>? HandlerFailed;

        /// <summary>通讯参数。</summary>
        public ConnectionOptions Options { get; }

        /// <summary>是否已连接。</summary>
        public bool IsConnected
        {
            get
            {
                lock (_sync)
                {
                    return _session != null;
                }
            }
        }

        /// <summary>
        /// 打开连接并开始接收。
        /// </summary>
        /// <param name="cancellationToken">取消令牌;取消时抛出 <see cref="OperationCanceledException"/> (已打开的部分会关闭)。</param>
        /// <returns>成功,或 <see cref="ZevisionErrorKind.AlreadyConnected"/>、<see cref="ZevisionErrorKind.ConnectFailed"/>。</returns>
        /// <exception cref="ObjectDisposedException">已释放 (包括连接过程中被释放)。</exception>
        /// <remarks><see cref="DisconnectAsync"/> 不会中止正在进行的连接;要中止,取消 <paramref name="cancellationToken"/>。</remarks>
        public async Task<ZevisionResult> ConnectAsync(CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_session != null || _connecting)
                {
                    return ZevisionResult.Fail(new ZevisionError(ZevisionErrorKind.AlreadyConnected, "Already connected."));
                }

                _connecting = true;
            }

            try
            {
                IDeviceTransport? transport = null;
                try
                {
                    transport = _transportFactory(Options.Endpoint);
                    await transport.OpenAsync(Options.Endpoint, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsConnectFailure(ex))
                {
                    transport?.Close();
                    return ZevisionResult.Fail(new ZevisionError(ZevisionErrorKind.ConnectFailed, $"Cannot connect to {Options.Endpoint}: {ex.Message}")
                    {
                        Exception = ex,
                        Endpoint = Options.Endpoint,
                    });
                }
                catch
                {
                    transport?.Close(); // 取消或意外错误:不留下打开了一半的连接。
                    throw;
                }

                var session = new Session(transport, Options.Frame);
                lock (_sync)
                {
                    if (!_disposed)
                    {
                        // 在同一个锁中发布会话并启动读取循环:断开时要么看不到会话,要么能等到它的读取循环结束。
                        _session = session;
                        session.ReadLoop = Task.Run(() => ReadLoopAsync(session));
                        return ZevisionResult.Ok();
                    }
                }

                // 连接期间被释放:关闭刚打开的连接,不能留下打开的端口。
                transport.Close();
                throw new ObjectDisposedException(nameof(ZevisionClient));
            }
            finally
            {
                lock (_sync)
                {
                    _connecting = false;
                }
            }
        }

        /// <summary>
        /// 关闭连接,并等待读取循环结束。正在等待的请求以 <see cref="ZevisionErrorKind.Disconnected"/> 结束。未连接时直接返回。
        /// </summary>
        public async Task DisconnectAsync()
        {
            var session = EndSession(null);
            if (session?.ReadLoop != null)
            {
                await session.ReadLoop.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 发送命令并等待响应。
        /// </summary>
        /// <param name="command">命令数据,不含起止符,如 <c>H01</c>、<c>QM0103</c>、<c>QG01;QG1304</c>。</param>
        /// <param name="cancellationToken">取消令牌;取消时抛出 <see cref="OperationCanceledException"/>。</param>
        /// <returns>设备的响应 (一条命令一个 <see cref="ZevisionResponse"/>),或失败原因。</returns>
        public Task<ZevisionResult<ZevisionReply>> SendAsync(string command, CancellationToken cancellationToken = default) =>
            SendAsync(command, TimeSpan.Zero, cancellationToken);

        /// <summary>
        /// 发送命令并等待响应,响应空闲超时至少为 <paramref name="minimumResponseTimeout"/>;
        /// <see cref="ConnectionOptions.ResponseTimeout"/> 更长时用它。用于已知响应前或分包之间会停顿的指令,
        /// 如 <c>?</c> (完整帮助约 116 KB,首字节约 0.4 s,包间约 0.35 s,建议至少 2 s)。
        /// </summary>
        /// <param name="command">命令数据,不含起止符。</param>
        /// <param name="minimumResponseTimeout">本次请求的最低空闲超时。</param>
        /// <param name="cancellationToken">取消令牌;取消时抛出 <see cref="OperationCanceledException"/>。</param>
        /// <returns>设备的响应,或失败原因。</returns>
        public async Task<ZevisionResult<ZevisionReply>> SendAsync(string command, TimeSpan minimumResponseTimeout, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return Fail(ZevisionErrorKind.InvalidCommand, "The command must not be empty.");
            }

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Session? session;
                lock (_sync)
                {
                    session = _session;
                }

                if (session == null)
                {
                    return Fail(ZevisionErrorKind.NotConnected, "Not connected.");
                }

                var timeout = Options.ResponseTimeout > minimumResponseTimeout ? Options.ResponseTimeout : minimumResponseTimeout;
                return await SendOnSessionAsync(session, command, timeout, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// 断开连接并释放客户端。之后调用 <see cref="ConnectAsync"/> 会抛出 <see cref="ObjectDisposedException"/>,
        /// 发送返回 <see cref="ZevisionErrorKind.NotConnected"/>。
        /// </summary>
        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
            }

            EndSession(null);
        }

        private async Task<ZevisionResult<ZevisionReply>> SendOnSessionAsync(Session session, string command, TimeSpan responseTimeout, CancellationToken cancellationToken)
        {
            byte[] frame;
            string sentCommand;
            try
            {
                // 设备会把等于分隔符的校验和误当作停止符,必要时追加 ;H01 改变校验和 (见 EncodeAvoidingDelimiters)。
                frame = ZevisionFrameCodec.EncodeAvoidingDelimiters(command, Options.Frame, out sentCommand);
            }
            catch (ArgumentException ex)
            {
                return ZevisionResult<ZevisionReply>.Fail(new ZevisionError(ZevisionErrorKind.InvalidCommand, ex.Message) { Exception = ex });
            }

            var pending = new PendingRequest(ZevisionResponseParser.EchoOf(command));
            lock (_sync)
            {
                // 与 EndSession 互斥:会话在取得发送锁之后已结束时立即失败,否则这个请求不会被取消,只能等到超时。
                if (_session != session)
                {
                    return Fail(ZevisionErrorKind.Disconnected, "Disconnected.");
                }

                session.Pending = pending;
            }

            try
            {
                Raise(FrameSent, new FrameSentEventArgs(sentCommand, frame), nameof(FrameSent));
                try
                {
                    await session.Transport.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException || ex is TimeoutException || ex is InvalidOperationException || ex is ObjectDisposedException)
                {
                    // ObjectDisposedException:写入时连接被读取循环关闭 (拔线、对端关闭)。
                    return ZevisionResult<ZevisionReply>.Fail(new ZevisionError(ZevisionErrorKind.SendFailed, $"Sending failed: {ex.Message}") { Exception = ex });
                }

                var timedOut = await WaitForReplyAsync(session, pending, responseTimeout, cancellationToken).ConfigureAwait(false);
                if (timedOut != null)
                {
                    var detail = timedOut.Value ? "; the incomplete data received was reported through FrameReceived" : string.Empty;
                    return ZevisionResult<ZevisionReply>.Fail(new ZevisionError(ZevisionErrorKind.Timeout, $"Timed out waiting for a response (no data within {responseTimeout.TotalMilliseconds:0} ms{detail}).")
                    {
                        Timeout = responseTimeout,
                        ReceivedIncompleteData = timedOut.Value,
                    });
                }

                ReceivedReply received;
                try
                {
                    received = await pending.Reply.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // EndSession 会取消挂起的请求。
                    return Fail(ZevisionErrorKind.Disconnected, "Disconnected.");
                }

                return ToReply(received, command, sentCommand);
            }
            finally
            {
                lock (_sync)
                {
                    if (session.Pending == pending)
                    {
                        session.Pending = null;
                    }
                }
            }
        }

        /// <summary>收到的响应帧 → 结果:帧无效、无法解析或没有响应时失败;去掉为避开分隔符追加的 H01 的响应。</summary>
        private static ZevisionResult<ZevisionReply> ToReply(ReceivedReply received, string command, string sentCommand)
        {
            var frame = received.Frame;
            if (!frame.IsValid)
            {
                return ZevisionResult<ZevisionReply>.Fail(new ZevisionError(ZevisionErrorKind.InvalidFrame, $"Invalid response frame: {frame.Error}") { FrameError = frame.Error });
            }

            var responses = received.Responses;
            if (responses == null || responses.Count == 0)
            {
                // 原始数据已经通过 FrameReceived 报告,这里不再附带 (可能很大的) 内容。
                return ZevisionResult<ZevisionReply>.Fail(new ZevisionError(ZevisionErrorKind.UnparseableResponse, $"Cannot parse the response ({frame.Data.Length} bytes).") { DataLength = frame.Data.Length });
            }

            if (!string.Equals(sentCommand, command, StringComparison.Ordinal)
                && responses.Count > 1
                && responses[responses.Count - 1].Command == ZevisionFrameCodec.FillerCommand)
            {
                responses = responses.Take(responses.Count - 1).ToList();
            }

            return ZevisionResult<ZevisionReply>.Ok(new ZevisionReply(frame, responses));
        }

        private static ZevisionResult<ZevisionReply> Fail(ZevisionErrorKind kind, string message) =>
            ZevisionResult<ZevisionReply>.Fail(new ZevisionError(kind, message));

        private static bool IsConnectFailure(Exception ex) =>
            ex is IOException || ex is UnauthorizedAccessException || ex is TimeoutException || ex is ArgumentException || ex is InvalidOperationException;

        /// <summary>
        /// 等待响应。超时按"空闲"计算:只要仍在收到字节就继续等待,因此大响应 (如 <c>?</c> 帮助) 在低波特率下传输数秒也不会超时。
        /// 超时时与读取循环互斥地确认请求还没有被响应,再把解码器里没收完的消息报告出去 (否则已收到的数据会悄无声息地丢失)。
        /// </summary>
        /// <returns>收到响应 (或会话已结束) 时为 null;超时时为是否报告了未收完的数据。</returns>
        private async Task<bool?> WaitForReplyAsync(Session session, PendingRequest pending, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var sentTimestamp = Stopwatch.GetTimestamp();
            var timeoutTicks = (long)(timeout.TotalSeconds * Stopwatch.Frequency);
            while (true)
            {
                var lastActivity = Math.Max(sentTimestamp, Interlocked.Read(ref session.LastReceiveTimestamp));
                var remainingTicks = timeoutTicks - (Stopwatch.GetTimestamp() - lastActivity);
                if (remainingTicks <= 0)
                {
                    return ReportTimeout(session, pending);
                }

                var remaining = TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency);
                using (var delayCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    var completed = await Task.WhenAny(pending.Reply.Task, Task.Delay(remaining < MaxDelay ? remaining : MaxDelay, delayCancel.Token)).ConfigureAwait(false);
                    delayCancel.Cancel(); // 响应先到时停止计时器,不让它留到超时
                    if (completed == pending.Reply.Task)
                    {
                        return null;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        /// <summary>超时:请求仍未被响应时撤下它,报告未收完的数据。</summary>
        /// <returns>请求已被响应 (或会话已结束) 时为 null;否则为是否报告了未收完的数据。</returns>
        private bool? ReportTimeout(Session session, PendingRequest pending)
        {
            // 与读取循环的分发互斥:分发到一半 (响应已认领、事件处理程序还在运行) 时等它完成,而不是把已收到的响应当成超时。
            lock (session.ReceiveLock)
            {
                lock (_sync)
                {
                    if (session.Pending != pending)
                    {
                        return null;
                    }

                    session.Pending = null;
                }

                var partial = session.Decoder.FlushIncomplete();
                foreach (var frame in partial)
                {
                    Raise(FrameReceived, new FrameReceivedEventArgs(frame, frame.Error != FrameError.UnexpectedData), nameof(FrameReceived));
                }

                return partial.Count > 0;
            }
        }

        private async Task ReadLoopAsync(Session session)
        {
            var buffer = new byte[ReadBufferSize];
            try
            {
                while (!session.Cancellation.IsCancellationRequested)
                {
                    var count = await session.Transport.ReadAsync(buffer, session.Cancellation.Token).ConfigureAwait(false);
                    if (count == 0)
                    {
                        if (session.Cancellation.IsCancellationRequested)
                        {
                            break; // 主动断开:TCP 关闭时挂起的读取返回 0,属于正常结束。
                        }

                        throw new IOException("The connection was closed.");
                    }

                    Interlocked.Exchange(ref session.LastReceiveTimestamp, Stopwatch.GetTimestamp());
                    lock (session.ReceiveLock)
                    {
                        foreach (var frame in session.Decoder.Push(buffer, 0, count))
                        {
                            Dispatch(session, frame);
                        }
                    }
                }
            }
            catch (Exception) when (session.Cancellation.IsCancellationRequested)
            {
                // 主动断开:关闭端口使挂起的读取抛出异常 (ERROR_OPERATION_ABORTED),属于正常结束。
            }
#pragma warning disable CA1031 // 读取循环是后台任务的顶层:任何异常都必须转换为连接中断 (由 ConnectionLost 交给调用方),否则调用方会以为已连接但再也收不到数据。
            catch (Exception ex)
#pragma warning restore CA1031
            {
                if (EndSession(session) != null)
                {
                    Raise(ConnectionLost, new ConnectionLostEventArgs(ex), nameof(ConnectionLost));
                }
            }
        }

        /// <summary>
        /// 分发一帧 (在 <see cref="Session.ReceiveLock"/> 中):回显与当前请求相符时完成请求,否则作为未请求数据报告。
        /// 只在有请求等待时解析,解析结果随帧交给发送方,不再解析第二次 (<c>?</c> 的响应约 116 KB)。
        /// </summary>
        private void Dispatch(Session session, ZevisionFrame frame)
        {
            PendingRequest? pending;
            lock (_sync)
            {
                pending = session.Pending;
            }

            PendingRequest? claimed = null;
            IReadOnlyList<ZevisionResponse>? responses = null;
            if (pending != null
                && frame.Error != FrameError.UnexpectedData
                && ZevisionResponseParser.IsReplyTo(frame, pending.ExpectedEcho, out responses))
            {
                lock (_sync)
                {
                    if (session.Pending == pending)
                    {
                        session.Pending = null;
                        claimed = pending;
                    }
                }
            }

            // 先通知 FrameReceived,再完成请求,保证调用方先看到收到的帧,再看到该请求的结果。
            Raise(FrameReceived, new FrameReceivedEventArgs(frame, claimed != null), nameof(FrameReceived));
            claimed?.Reply.TrySetResult(new ReceivedReply(frame, responses));
        }

        /// <summary>
        /// 触发事件并隔离处理程序的异常:处理程序出错不能中断读取循环或发送流程。异常交给 <see cref="HandlerFailed"/>。
        /// </summary>
        private void Raise<T>(EventHandler<T>? handler, T args, string eventName)
            where T : EventArgs
        {
            try
            {
                handler?.Invoke(this, args);
            }
#pragma warning disable CA1031 // 事件处理程序的异常与通讯无关:交给 HandlerFailed 后继续。
            catch (Exception ex)
            {
                try
                {
                    HandlerFailed?.Invoke(this, new HandlerFailedEventArgs(eventName, ex));
                }
                catch (Exception)
                {
                    // HandlerFailed 的处理程序出错:已经没有可以报告的地方,忽略,保证通讯继续。
                }
            }
#pragma warning restore CA1031
        }

        /// <summary>
        /// 结束会话。<paramref name="expected"/> 不为 null 时,只有当前会话仍是它才结束,避免与主动断开竞争。
        /// </summary>
        /// <returns>被结束的会话;没有会话被结束时为 null。</returns>
        private Session? EndSession(Session? expected)
        {
            Session? session;
            PendingRequest? pending;
            lock (_sync)
            {
                session = _session;
                if (session == null || (expected != null && session != expected))
                {
                    return null;
                }

                _session = null;
                pending = session.Pending;
                session.Pending = null;
            }

            // 先结束等待中的请求,再关闭连接:关闭出错时请求也不会一直等到超时。
            pending?.Reply.TrySetCanceled();
            session.Cancellation.Cancel();
            session.Transport.Close();
            return session;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ZevisionClient));
            }
        }

        /// <summary>收到的响应帧,及其解析结果 (帧无效或无法解析时为 null)。</summary>
        private sealed class ReceivedReply
        {
            public ReceivedReply(ZevisionFrame frame, IReadOnlyList<ZevisionResponse>? responses)
            {
                Frame = frame;
                Responses = responses;
            }

            public ZevisionFrame Frame { get; }

            public IReadOnlyList<ZevisionResponse>? Responses { get; }
        }

        /// <summary>正在等待响应的请求。</summary>
        private sealed class PendingRequest
        {
            public PendingRequest(string expectedEcho)
            {
                ExpectedEcho = expectedEcho;
            }

            /// <summary>响应中应有的命令回显。</summary>
            public string ExpectedEcho { get; }

            public TaskCompletionSource<ReceivedReply> Reply { get; } =
                new TaskCompletionSource<ReceivedReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>一次连接的状态:传输、解码器、读取循环、等待中的请求。断开后丢弃,重新连接时新建。</summary>
        private sealed class Session
        {
            /// <summary>最近一次收到数据的时间 (<see cref="Stopwatch.GetTimestamp"/>),用于空闲超时;用 <see cref="Interlocked"/> 读写。</summary>
            public long LastReceiveTimestamp;

            public Session(IDeviceTransport transport, FrameOptions frame)
            {
                Transport = transport;
                Decoder = new ZevisionFrameDecoder(frame);
            }

            public IDeviceTransport Transport { get; }

            /// <summary>解码器非线程安全。</summary>
            public ZevisionFrameDecoder Decoder { get; }

            /// <summary>
            /// 读取循环的解码和分发,与发送方超时时报告未收完的数据互斥:<see cref="FrameReceived"/> 依次触发,
            /// 超时也不会打断分发到一半的响应。
            /// </summary>
            public object ReceiveLock { get; } = new object();

            public CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();

            /// <summary>等待响应的请求;由客户端的 <c>_sync</c> 保护。</summary>
            public PendingRequest? Pending { get; set; }

            public Task? ReadLoop { get; set; }
        }
    }
}
