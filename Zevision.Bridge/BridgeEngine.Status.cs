using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Commands;
using Zevision.Xtc3;

namespace Zevision.Bridge
{
    /// <summary>周期采样、采样缓冲、推导状态和状态命令 (S),ZEV-FAR-01 的 5.4 和表 A.3。</summary>
    internal sealed partial class BridgeEngine
    {
        /// <summary>采样缓冲的格数:256 个 tick (约 64 s),S0 最多回溯这么多。</summary>
        internal const int BufferSize = 256;

        /// <summary>S1 的平均窗口:1 s = 4 个 tick。</summary>
        internal const int OneSecondTicks = 4;

        /// <summary>S31 的平均窗口:6.25 s = 25 个 tick。</summary>
        internal const int RollingAverageTicks = 25;

        private const int MaxOutputs = 20;
        private const int MaxInputs = 9;

        private readonly BridgeSample?[] _buffer = new BridgeSample?[BufferSize];
        private BridgeSample? _latest;
        private Xtc3State? _lastState;
        private TimeSpan _stateSince;
        private int _layer = 1;
        private bool _endOfProcess;

        /// <summary>R0 在一层结束 (Idle) 后启动下一层:层计数已经加 1,设备还没有开始运行 (运行后清除)。</summary>
        private bool _startPending;

        /// <summary>
        /// 采样一次:每个 tick 一帧 <c>SC11;SC12;SC13;SC15;SC16;SNss20;QC09;QC18;QC1F;QNss09</c>,并按 tick 轮流加上
        /// 晶振寿命、S/Q 计数、活动工艺和测试模式 (tick % 4 = 0)、各输出的状态 (= 1)、各输入的状态 (= 2)。
        /// </summary>
        /// <returns>采样成功时为 true;未连接、通讯失败或状态读不到时为 false。</returns>
        public async Task<bool> SampleAsync(CancellationToken cancellationToken)
        {
            var tick = AbsoluteTick;
            BridgeSample? previous;
            Xtc3State? lastState;
            lock (_sync)
            {
                previous = _latest;
                lastState = _lastState;
            }

            var sensor = CurrentFilm() is { } film ? _filmInfo[film.Number - 1].Sensor : 1;
            var ss = sensor.ToString("X2", CultureInfo.InvariantCulture);
            var commands = new List<string>
            {
                Status("C11"), Status("C12"), Status("C13"), Status("C15"), Status("C16"), Status("N" + ss + "20"),
                ZevisionCommandSet.Query("C09"), ZevisionCommandSet.Query("C18"), ZevisionCommandSet.Query("C1F"), ZevisionCommandSet.Query("N" + ss + "09"),
            };
            var fastCount = commands.Count;
            var phase = previous == null ? -1 : (int)(tick % OneSecondTicks);
            if (phase <= 0)
            {
                commands.AddRange(new[] { "C17", "C20", "C21", "P01", "C1C" }.Select(ZevisionCommandSet.Query));
            }

            var outputs = Math.Min(ParseInt(SnapshotValue("OFE")) ?? 0, MaxOutputs);
            var inputs = Math.Min(ParseInt(SnapshotValue("IFE")) ?? 0, MaxInputs);
            var outputStart = commands.Count;
            if (phase == -1 || phase == 1)
            {
                commands.AddRange(Enumerable.Range(1, outputs).Select(n => ZevisionCommandSet.Query("O" + n.ToString("X2", CultureInfo.InvariantCulture) + "05")));
            }

            var inputStart = commands.Count;
            if (phase == -1 || phase == 2)
            {
                commands.AddRange(Enumerable.Range(1, inputs).Select(n => ZevisionCommandSet.Query("I" + n.ToString("X2", CultureInfo.InvariantCulture) + "04")));
            }

            var answers = await ExchangeAsync(commands, cancellationToken).ConfigureAwait(false);
            if (answers == null || !answers[6].IsSuccess)
            {
                return false;
            }

            string? Value(int index) => index < fastCount - 4 ? answers[index].StatusValue : answers[index].Data;
            float Single(int index) => answers[index].IsSuccess && ParseDecimal(Value(index)) is { } number ? (float)number : 0f;
            int? Int(int index) => answers[index].IsSuccess ? ParseInt(answers[index].Data) : null;

            var stateText = answers[6].Data ?? string.Empty;
            var stopped = DeviceStates.IsStopped(stateText);
            var mapped = stopped ? lastState ?? Xtc3State.Ready : DeviceStates.TryMap(stateText, out var state) ? state : (Xtc3State?)null;
            var sample = new BridgeSample
            {
                Tick = tick,
                Time = _clock(),
                Power = Single(0),
                FilteredRate = Single(1),
                RawRate = Single(2),
                Thickness = Single(3),
                Frequency = answers[4].IsSuccess && ParseDecimal(Value(4)) is { } hz ? (double)hz : 0,
                RateDeviation = Single(5),
                StateText = stateText,
                State = mapped,
                StoppedText = stopped,
                CrystalFailed = Int(7) == 1,
                StopCause = Int(8) ?? 0,
                Sensor = sensor,
                ActiveCrystal = Int(9) ?? 1,
                CrystalLife = phase <= 0 ? Int(fastCount) ?? 0 : previous?.CrystalLife ?? 0,
                StabilityTotal = phase <= 0 ? Int(fastCount + 1) ?? 0 : previous?.StabilityTotal ?? 0,
                QualityCounts = phase <= 0 ? Int(fastCount + 2) ?? 0 : previous?.QualityCounts ?? 0,
                ActiveProcess = phase <= 0 ? Int(fastCount + 3) ?? 0 : previous?.ActiveProcess ?? 0,
                TestMode = phase <= 0 ? Int(fastCount + 4) == 1 : previous?.TestMode ?? false,
                Outputs = phase == -1 || phase == 1 ? Bits(answers, outputStart, outputs, n => 1 << (n - 1)) : previous?.Outputs ?? 0,
                Inputs = phase == -1 || phase == 2 ? Bits(answers, inputStart, inputs, InputBit) : previous?.Inputs ?? 0,
            };

            Record(sample);
            return true;
        }

        /// <summary>保存采样并更新推导状态:状态开始时刻 (S5)、层计数 (S6)、工艺结束 (S17)。</summary>
        internal void Record(BridgeSample sample)
        {
            string? unknown = null;
            lock (_sync)
            {
                _buffer[sample.Tick % BufferSize] = sample;
                _latest = sample;
                if (sample.ActiveProcess > 0)
                {
                    _activeProcess = sample.ActiveProcess;
                }

                if (!(sample.State is { } state))
                {
                    if (_unknownStates.Add(sample.StateText))
                    {
                        unknown = sample.StateText;
                    }
                }
                else if (state != _lastState)
                {
                    if (_lastState is { } last)
                    {
                        if (state == Xtc3State.Ready)
                        {
                            // 复位回到第 1 层;R0 已经计入下一层、设备还没有开始运行时 (中间层停止、复位再启动) 不算复位。
                            _layer = _startPending ? _layer : 1;
                        }
                        else if (DeviceStates.IsRunning(state))
                        {
                            if (last == Xtc3State.Idle && !_startPending)
                            {
                                _layer++; // 一层结束 (空闲) 后不经 R0 再次启动 (如前面板):下一层
                            }

                            _startPending = false;
                        }

                        if (state == Xtc3State.Idle && DeviceStates.IsRunning(last) && !sample.IsStopped)
                        {
                            _endOfProcess = true;
                        }
                    }

                    _lastState = state;
                    _stateSince = sample.Time;
                }
            }

            if (unknown != null)
            {
                _log(BridgeLogLevel.Warning, "Unknown IMC300 state text \"" + unknown + "\" (C09); S4 answers E for it.", null);
            }
        }

        /// <summary>最近的采样;没有时为 null。</summary>
        private BridgeSample? LatestSample()
        {
            lock (_sync)
            {
                return _latest;
            }
        }

        /// <summary>不超过 <see cref="Xtc3BridgeOptions.MaxSampleAge"/> 的最近采样;未连接或采样太旧时为 null。</summary>
        private BridgeSample? LatestFreshSample()
        {
            var sample = LatestSample();
            return _online && sample != null && _clock() - sample.Time <= _options.MaxSampleAge ? sample : null;
        }

        private async Task<Xtc3CommandResult> StatusAsync(Xtc3Command command, CancellationToken cancellationToken)
        {
            var id = command.Id!.Value;
            switch (id)
            {
                case 0:
                    return await ProcessInformationAsync((byte)command.Parameters[0].Integer, cancellationToken).ConfigureAwait(false);
                case 19:
                    // 数据日志未实现:与 XTC/3 在第一层结束前相同,回答 E (ZEV-FAR-01 的 5.4 f)。
                    return Xtc3CommandResult.Fail(Xtc3ErrorCode.DataNotAvailable);
                case 21:
                    return Xtc3CommandResult.Ok(Xtc3Value.FromByte((byte)ReadLocal(l => l.PowerUpErrors)));
                case 30:
                    return await CrystalStatesAsync(cancellationToken).ConfigureAwait(false);
                case 35:
                    return await EthernetParametersAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!(LatestFreshSample() is { } sample))
            {
                return Xtc3CommandResult.Fail(Xtc3ErrorCode.DataNotAvailable);
            }

            var film = CurrentFilm();
            FilmInfo? info = film == null ? null : _filmInfo[film.Number - 1];
            Xtc3State? lastState;
            TimeSpan since;
            int layer;
            bool endOfProcess;
            lock (_sync)
            {
                lastState = _lastState;
                since = _stateSince;
                layer = _layer;
                endOfProcess = _endOfProcess;
            }

            switch (id)
            {
                case 1:
                    return Ok(Xtc3Value.FromFloat(AverageRate(sample, OneSecondTicks)));
                case 2:
                    return Ok(Xtc3Value.FromFloat(sample.Power));
                case 3:
                    return Ok(Xtc3Value.FromFloat(sample.Thickness));
                case 4:
                    return sample.State is { } state ? Ok(Xtc3Value.FromByte((byte)state)) : Xtc3CommandResult.Fail(Xtc3ErrorCode.DataNotAvailable);
                case 5:
                    return Ok(Xtc3Value.FromInteger(lastState == null ? 0 : (int)Math.Max(0, (_clock() - since).TotalSeconds)));
                case 6:
                    return Ok(Xtc3Value.FromInteger(layer));
                case 7:
                case 20:
                    return Ok(Xtc3Value.FromByte((byte)(film?.Number ?? 0))); // 活动工艺不是影子工艺时为 0
                case 8:
                    return Ok(Xtc3Value.FromByte((byte)sample.Sensor));
                case 9:
                    return Ok(Xtc3Value.FromByte((byte)Math.Max(0, Math.Min(255, sample.CrystalLife))));
                case 10:
                    return info == null ? Xtc3CommandResult.Fail(Xtc3ErrorCode.DataNotAvailable) : Ok(Xtc3Value.FromByte((byte)info.Source));
                case 11:
                    return Ok(Xtc3Value.FromInteger(sample.Outputs));
                case 12:
                    return Ok(Xtc3Value.FromWord((ushort)sample.Inputs));
                case 13:
                    return Ok(Xtc3Value.FromInteger(DdsUnits(sample.Frequency)));
                case 14:
                    return Ok(Flag(sample.CrystalFailed));
                case 15:
                    return Ok(Flag(IsAtMaxPower(sample, info)));
                case 16:
                    return Ok(Flag(false)); // 晶振切换中:IMC300 没有对应的状态项 (待实测)
                case 17:
                    return Ok(Flag(endOfProcess));
                case 18:
                    return Ok(Flag(sample.IsStopped));
                case 31:
                    return Ok(Xtc3Value.FromFloat(AverageRate(sample, RollingAverageTicks)));
                case 32:
                    return Ok(Xtc3Value.FromByte((byte)Math.Max(0, Math.Min(255, sample.ActiveCrystal))));
                case 33:
                    return Ok(Xtc3Value.FromInteger(unchecked((int)StatusMessages(sample, info, endOfProcess))));
                case 34:
                    return Ok(Xtc3Value.FromFloat(sample.RawRate));
                default:
                    return Xtc3CommandResult.Fail(Xtc3ErrorCode.IllegalId);
            }
        }

        /// <summary>
        /// S0:要的是当前 tick、而这个 tick 还没有采样时,当场采样一次再回答。采样在 tick 开始时发出,经 TCP 约 26 ms 后才有结果
        /// (经 RS232 更久);主机在同一个 tick 内用刚收到的 Timer 查询时,不这样会回答 E (2026-10-23 实测)。
        /// </summary>
        private async Task<Xtc3CommandResult> ProcessInformationAsync(byte tick, CancellationToken cancellationToken)
        {
            var now = AbsoluteTick;
            if (tick == unchecked((byte)now))
            {
                BridgeSample? current;
                lock (_sync)
                {
                    current = _buffer[now % BufferSize];
                }

                if (current == null || current.Tick != now)
                {
                    await SampleAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            return ProcessInformation(tick);
        }

        /// <summary>S0:指定 tick 的过程信息;该 tick 没有采样 (未采到、超过 256 个 tick 或通讯中断期间) 时为 E。</summary>
        private Xtc3CommandResult ProcessInformation(byte tick)
        {
            var now = AbsoluteTick;
            BridgeSample? sample;
            lock (_sync)
            {
                sample = _buffer[tick % BufferSize];
            }

            if (sample == null || unchecked((byte)sample.Tick) != tick || now - sample.Tick >= BufferSize)
            {
                return Xtc3CommandResult.Fail(Xtc3ErrorCode.DataNotAvailable);
            }

            var depositing = sample.State is { } state && DeviceStates.IsDepositing(state) && !sample.IsStopped;
            return Xtc3CommandResult.Ok(
                Xtc3Value.FromByte((byte)(sample.CrystalFailed ? Xtc3CrystalStatus.Failed : Xtc3CrystalStatus.None)),
                Xtc3Value.FromByte((byte)(sample.State ?? Xtc3State.Ready)),
                Xtc3Value.FromByte((byte)sample.Sensor),
                Xtc3Value.FromByte((byte)Math.Max(0, Math.Min(255, sample.ActiveCrystal))),
                Xtc3Value.FromFloat(depositing ? AverageRate(sample, OneSecondTicks) : 0f),
                Xtc3Value.FromFloat(sample.Thickness),
                Xtc3Value.FromFloat(sample.Power),
                Xtc3Value.FromFloat(depositing ? sample.RateDeviation : 0f),
                Xtc3Value.FromInteger(DdsUnits(sample.Frequency)));
        }

        /// <summary>S30:当前传感器各晶振的状态位,健康度 (<c>QNss10nn02</c>) 大于 0 为 1 (待实测)。</summary>
        private async Task<Xtc3CommandResult> CrystalStatesAsync(CancellationToken cancellationToken)
        {
            var sensor = (LatestSample()?.Sensor ?? 1).ToString("X2", CultureInfo.InvariantCulture);
            var (count, countError) = await QueryAsync(new[] { "N" + sensor + "10FE" }, cancellationToken).ConfigureAwait(false);
            if (count == null || !(ParseInt(count[0]) is { } crystals))
            {
                return Xtc3CommandResult.Fail(count == null ? countError : Xtc3ErrorCode.DataNotAvailable);
            }

            crystals = Math.Max(0, Math.Min(crystals, 16));
            var ids = Enumerable.Range(1, crystals).Select(n => "N" + sensor + "10" + n.ToString("X2", CultureInfo.InvariantCulture) + "02").ToList();
            var (health, error) = await QueryAsync(ids, cancellationToken).ConfigureAwait(false);
            if (health == null)
            {
                return Xtc3CommandResult.Fail(error);
            }

            var bits = 0;
            for (var i = 0; i < health.Length; i++)
            {
                if ((ParseDecimal(health[i]) ?? 0) > 0)
                {
                    bits |= 1 << i;
                }
            }

            return Xtc3CommandResult.Ok(Xtc3Value.FromWord((ushort)bits));
        }

        /// <summary>S35:IMC300 的 IP 地址 (<c>G1402</c>) 和子网掩码 (<c>G1403</c>),各 4 字节,最左一段在前 (ZEV-FAR-01 的 Q-05)。</summary>
        private async Task<Xtc3CommandResult> EthernetParametersAsync(CancellationToken cancellationToken)
        {
            var (values, error) = await QueryAsync(new[] { "G1402", "G1403" }, cancellationToken).ConfigureAwait(false);
            if (values == null)
            {
                return Xtc3CommandResult.Fail(error);
            }

            if (!IPAddress.TryParse(values[0], out var address) || !IPAddress.TryParse(values[1], out var mask)
                || address.GetAddressBytes().Length != 4 || mask.GetAddressBytes().Length != 4)
            {
                return Xtc3CommandResult.Fail(Xtc3ErrorCode.DataNotAvailable);
            }

            return Xtc3CommandResult.Ok(Xtc3Value.FromBytes(address.GetAddressBytes().Concat(mask.GetAddressBytes()).ToArray()));
        }

        /// <summary>截至 <paramref name="last"/> 的 <paramref name="ticks"/> 个 tick 中已采样的原始速率 (<c>C13</c>) 的平均。</summary>
        private float AverageRate(BridgeSample last, int ticks)
        {
            var sum = 0.0;
            var count = 0;
            lock (_sync)
            {
                for (var t = last.Tick; t > last.Tick - ticks && t >= 0; t--)
                {
                    if (_buffer[t % BufferSize] is { } sample && sample.Tick == t)
                    {
                        sum += sample.RawRate;
                        count++;
                    }
                }
            }

            return count == 0 ? 0f : (float)(sum / count);
        }

        /// <summary>S33 中能由采样和本地参数得出的位;其余位取 0 (ZEV-FAR-01 的 5.4 h)。</summary>
        private Xtc3StatusMessages StatusMessages(BridgeSample sample, FilmInfo? info, bool endOfProcess)
        {
            var messages = Xtc3StatusMessages.None;
            if (IsAtMaxPower(sample, info))
            {
                messages |= Xtc3StatusMessages.MaxPower;
            }

            if (sample.CrystalFailed)
            {
                messages |= Xtc3StatusMessages.CrystalFail;
            }

            if (endOfProcess)
            {
                messages |= Xtc3StatusMessages.EndOfProcess;
            }

            if (info?.Etch == true)
            {
                messages |= Xtc3StatusMessages.EtchMode;
            }

            if (ReadLocal(l => l.RemoteLock))
            {
                messages |= Xtc3StatusMessages.RemoteLock;
            }

            if (sample.TestMode)
            {
                messages |= Xtc3StatusMessages.Test;
            }

            return messages;
        }

        /// <summary>S15:功率 (<c>C11</c>) 不小于当前 Film 的最大功率 (<c>Fkk0906</c>)。</summary>
        private static bool IsAtMaxPower(BridgeSample sample, FilmInfo? info) =>
            info != null && info.MaxPower > 0 && (decimal)sample.Power >= info.MaxPower;

        /// <summary>频率 (Hz) 换算为 DDS 单位 (S0、S13)。</summary>
        internal static int DdsUnits(double hertz)
        {
            var units = Math.Round(hertz / Xtc3Protocol.HertzPerDdsUnit, MidpointRounding.AwayFromZero);
            return units >= int.MaxValue ? int.MaxValue : units <= 0 ? 0 : (int)units;
        }

        /// <summary>S12 中输入 n 的位:输入 1~8 为 0x0100~0x8000,输入 9 为 0x0001。</summary>
        internal static int InputBit(int input) => input == 9 ? 0x0001 : 0x0100 << (input - 1);

        private static int Bits(DeviceAnswer[] answers, int start, int count, Func<int, int> bitOf)
        {
            var bits = 0;
            for (var n = 1; n <= count; n++)
            {
                if (answers[start + n - 1].IsSuccess && ParseInt(answers[start + n - 1].Data) == 1)
                {
                    bits |= bitOf(n);
                }
            }

            return bits;
        }

        private static string Status(string id) => ZevisionCommandSet.Build(CommandActions.Status, id);

        private static Xtc3Value Flag(bool value) => Xtc3Value.FromByte(value ? (byte)1 : (byte)0);

        private static Xtc3CommandResult Ok(Xtc3Value value) => Xtc3CommandResult.Ok(value);
    }
}
