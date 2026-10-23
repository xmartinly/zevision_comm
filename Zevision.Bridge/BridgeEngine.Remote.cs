using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Commands;
using Zevision.Xtc3;

namespace Zevision.Bridge
{
    /// <summary>远程动作 (R),ZEV-FAR-01 表 A.4。</summary>
    internal sealed partial class BridgeEngine
    {
        private async Task<Xtc3CommandResult> RemoteAsync(Xtc3Command command, CancellationToken cancellationToken)
        {
            var id = command.Id!.Value;
            var sample = LatestFreshSample();
            string action;
            switch (id)
            {
                case 0:
                case 1:
                case 2:
                    return await StartStopResetAsync(id, cancellationToken).ConfigureAwait(false);
                case 3:
                case 4:
                    action = ZevisionCommandSet.Update("G0E02", id == 3 ? "1" : "0"); // 前面板锁定 (效果待实测)
                    break;
                case 9:
                case 10:
                    action = ZevisionCommandSet.Update("C06", id == 9 ? "1" : "0");
                    break;
                case 11:
                    // 只在手动状态有效;状态当场读取 (同一个包中的 R9 刚切换到手动)。
                    var manual = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
                    if (manual.Known && manual.State != Xtc3State.Manual)
                    {
                        return Xtc3CommandResult.Fail(Xtc3ErrorCode.CannotDoNow);
                    }

                    var value = command.Parameters[0];
                    if (!Xtc3Parameter.TryToDecimal(value, 2, out var power) || power < 0 || power > 100)
                    {
                        return Xtc3CommandResult.Fail(Xtc3ErrorCode.IllegalValue);
                    }

                    action = ZevisionCommandSet.Update("C07", power.ToString("F2", CultureInfo.InvariantCulture));
                    break;
                case 12:
                    action = Remote("C03"); // IMC300 清零全部传感器
                    break;
                case 13:
                    action = Remote("C1E");
                    break;
                case 14:
                    action = Remote(SensorItem(sample, 0x18));
                    break;
                case 17:
                case 18:
                    // 通讯 I/O 模式 (R15) 本版本未实现,模式从未进入:与 XTC/3S 不在该模式时相同,回答 F。
                    var output = command.Parameters[0].Integer;
                    return Xtc3CommandResult.Fail(output < 1 || output > 20 ? Xtc3ErrorCode.IllegalValue : Xtc3ErrorCode.CannotDoNow);
                case 19:
                case 20:
                    action = ZevisionCommandSet.Update("G0E01", id == 19 ? "1" : "0"); // 显示开/关
                    break;
                case 21:
                    action = ZevisionCommandSet.Build(CommandActions.Remote, "G16", "1");
                    break;
                case 22:
                    UpdateLocal(l => l.PowerUpErrors = 0);
                    return Xtc3CommandResult.Ok();
                case 26:
                    action = Remote(SensorItem(sample, 0x1B));
                    break;
                case 27:
                    action = Remote(SensorItem(sample, 0x19));
                    break;
                case 28:
                    action = Remote("C05");
                    break;
                default:
                    // R5~R8 (晶振失效抑制、Soak 2 保持):IMC300 没有对应功能;R15、R16 (通讯 I/O 模式) 本版本未实现。
                    return Xtc3CommandResult.Fail(Xtc3ErrorCode.IllegalCommand);
            }

            var error = await ActAsync(action, cancellationToken).ConfigureAwait(false);
            if (error.HasValue)
            {
                return Xtc3CommandResult.Fail(error.Value);
            }

            AfterRemote(id);
            return Xtc3CommandResult.Ok();
        }

        /// <summary>
        /// R0 启动、R1 停止、R2 复位。IMC300 的行为 (2026-10-23 实测):一层结束 (Idle) 后 <c>UC01:1</c> 回答 <c>-B</c>,
        /// <c>UC01:0</c> 进入 Stopped,<c>RC04</c> 从 Stopped 回到 Ready;未运行时 <c>UC01:0</c> 回答 <c>-B</c>。
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item>R0:Idle 时 (影子工艺只有一层,一层已结束) 先停止、复位再启动;层计数在启动时加 1 (XTC/3S 在 Idle 时启动下一层),不等设备开始运行。</item>
        /// <item>R1:设备回答 <c>-B</c> 且已停止或 Ready 时回答 ACK (已经停止)。</item>
        /// <item>R2:停止时复位;Idle 时先停止再复位;Ready 时回答 ACK;运行中回答 F。</item>
        /// </list>
        /// 动作成功后立即采样一次:主机紧接着查询的 S4、S6、S18 反映设备的新状态,不用等下一个 tick
        /// (否则 R0 之后的第一次查询可能还是停止、复位过程中的采样,S18 = 1;2026-10-23 实测)。
        /// </remarks>
        private async Task<Xtc3CommandResult> StartStopResetAsync(int id, CancellationToken cancellationToken)
        {
            // 状态当场读取:采样最多晚一个 tick,主机在 R0 之后紧接着发 R1、R2 时按采样会判断错 (实测)。
            var current = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
            var state = current.State;
            var stopped = current.Stopped;
            Xtc3ErrorCode? error;
            switch (id)
            {
                case 0:
                    // 一层结束 (Idle) 后启动下一层:层计数先加 1,复位过程中的 Ready 不把它清为 1,设备开始运行时不再加。
                    var nextLayer = state == Xtc3State.Idle && !stopped;
                    if (nextLayer)
                    {
                        lock (_sync)
                        {
                            _layer++;
                            _startPending = true;
                        }
                    }

                    error = await ActAsync(ZevisionCommandSet.Update("C01", "1"), cancellationToken).ConfigureAwait(false);
                    if (error == Xtc3ErrorCode.IllegalValue && nextLayer)
                    {
                        error = await StopAndResetAsync(cancellationToken).ConfigureAwait(false)
                            ?? await ActAsync(ZevisionCommandSet.Update("C01", "1"), cancellationToken).ConfigureAwait(false);
                    }

                    if (error.HasValue && nextLayer)
                    {
                        lock (_sync)
                        {
                            if (_startPending)
                            {
                                _layer--;
                                _startPending = false;
                            }
                        }
                    }

                    break;

                case 1:
                    error = await ActAsync(ZevisionCommandSet.Update("C01", "0"), cancellationToken).ConfigureAwait(false);
                    if (error == Xtc3ErrorCode.IllegalValue && (stopped || state == Xtc3State.Ready))
                    {
                        error = null; // 已经停止
                    }

                    break;

                default:
                    if (current.Known && !stopped && state != Xtc3State.Idle)
                    {
                        // 复位只在停止时有效;已经 Ready 时不需要。
                        return state == Xtc3State.Ready ? Xtc3CommandResult.Ok() : Xtc3CommandResult.Fail(Xtc3ErrorCode.CannotDoNow);
                    }

                    error = state == Xtc3State.Idle && !stopped
                        ? await StopAndResetAsync(cancellationToken).ConfigureAwait(false)
                        : await ActAsync(Remote("C04"), cancellationToken).ConfigureAwait(false);
                    break;
            }

            if (error.HasValue)
            {
                return Xtc3CommandResult.Fail(error.Value);
            }

            AfterRemote(id);
            await SampleAsync(cancellationToken).ConfigureAwait(false);
            return Xtc3CommandResult.Ok();
        }

        /// <summary>停止 (<c>UC01:0</c>,等到 Stopped),再复位 (<c>RC04</c>,等到 Ready)。各最多等 <see cref="StateWaitLimit"/>。</summary>
        /// <returns>成功时为 null。</returns>
        private async Task<Xtc3ErrorCode?> StopAndResetAsync(CancellationToken cancellationToken)
        {
            var stop = await ActAsync(ZevisionCommandSet.Update("C01", "0"), cancellationToken).ConfigureAwait(false);
            if (stop.HasValue && stop != Xtc3ErrorCode.IllegalValue)
            {
                return stop;
            }

            if (!await WaitForStateAsync(text => DeviceStates.IsStopped(text) || IsReadyText(text), cancellationToken).ConfigureAwait(false))
            {
                return Xtc3ErrorCode.DataNotAvailable;
            }

            var reset = await ActAsync(Remote("C04"), cancellationToken).ConfigureAwait(false);
            if (reset.HasValue)
            {
                return reset;
            }

            return await WaitForStateAsync(IsReadyText, cancellationToken).ConfigureAwait(false) ? (Xtc3ErrorCode?)null : Xtc3ErrorCode.DataNotAvailable;
        }

        /// <summary>每 <see cref="StatePollInterval"/> 查询一次 <c>C09</c>,直到满足条件;超过 <see cref="StateWaitLimit"/> 时为 false。</summary>
        private async Task<bool> WaitForStateAsync(Func<string, bool> condition, CancellationToken cancellationToken)
        {
            var deadline = _clock() + StateWaitLimit;
            while (true)
            {
                var (values, _) = await QueryAsync(new[] { "C09" }, cancellationToken).ConfigureAwait(false);
                if (values != null && condition(values[0]))
                {
                    return true;
                }

                if (_clock() >= deadline)
                {
                    _log(BridgeLogLevel.Warning, "The IMC300 state did not change in " + StateWaitLimit.TotalSeconds + " s (C09 = " + (values?[0] ?? "?") + ").", null);
                    return false;
                }

                await Task.Delay(StatePollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        private static bool IsReadyText(string text) => DeviceStates.TryMap(text, out var state) && state == Xtc3State.Ready;

        /// <summary>设备的当前状态 (查询 <c>C09</c>、<c>C1F</c>)。</summary>
        private readonly struct DeviceState
        {
            public DeviceState(bool known, Xtc3State? state, bool stopped)
            {
                Known = known;
                State = state;
                Stopped = stopped;
            }

            /// <summary>读到了状态;为 false 时 (未连接、文本不认识) 不按状态限制,由设备决定。</summary>
            public bool Known { get; }

            /// <summary>XTC/3 状态;停止时为停止前的状态 (最近的采样)。</summary>
            public Xtc3State? State { get; }

            /// <summary>已停止 (<c>Stopped</c> 或停止原因不为 0)。</summary>
            public bool Stopped { get; }
        }

        private async Task<DeviceState> ReadStateAsync(CancellationToken cancellationToken)
        {
            var (values, _) = await QueryAsync(new[] { "C09", "C1F" }, cancellationToken).ConfigureAwait(false);
            if (values == null)
            {
                return default;
            }

            var stopped = DeviceStates.IsStopped(values[0]) || (ParseInt(values[1]) ?? 0) != 0;
            if (DeviceStates.IsStopped(values[0]))
            {
                return new DeviceState(true, LatestSample()?.State, true);
            }

            return DeviceStates.TryMap(values[0], out var state) ? new DeviceState(true, state, stopped) : new DeviceState(false, null, stopped);
        }

        /// <summary>动作成功后更新本地状态:R0、R2 清除工艺结束 (S17),R2 层计数回到 1,R3、R4 远程锁定标志。</summary>
        private void AfterRemote(int id)
        {
            switch (id)
            {
                case 0:
                case 2:
                    lock (_sync)
                    {
                        _endOfProcess = false;
                        if (id == 2)
                        {
                            _layer = 1;
                            _startPending = false;
                        }
                    }

                    break;
                case 3:
                case 4:
                    UpdateLocal(l => l.RemoteLock = id == 3);
                    break;
            }
        }

        /// <summary>当前传感器 (最近采样的,没有时为当前 Film 的) 下的设置项,如 <c>N0118</c>。</summary>
        private string SensorItem(BridgeSample? sample, int item)
        {
            var sensor = sample?.Sensor ?? (CurrentFilm() is { } film ? _filmInfo[film.Number - 1].Sensor : 1);
            return "N" + sensor.ToString("X2", CultureInfo.InvariantCulture) + item.ToString("X2", CultureInfo.InvariantCulture);
        }

        private static string Remote(string id) => ZevisionCommandSet.Build(CommandActions.Remote, id);
    }
}
