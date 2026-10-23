using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Commands;
using Zevision.Communication;
using Zevision.Core;
using Zevision.Core.Configuration;
using Zevision.Core.Devices;
using Zevision.Xtc3;

namespace Zevision.Bridge
{
    /// <summary>IMC300 对一条 Zevision 指令的回答。</summary>
    internal readonly struct DeviceAnswer
    {
        public static readonly DeviceAnswer NoResponse = new DeviceAnswer(false, null, null);

        private DeviceAnswer(bool isSuccess, string? data, char? code)
        {
            IsSuccess = isSuccess;
            Data = data;
            Code = code;
        }

        public bool IsSuccess { get; }

        /// <summary>数据 (已去除首尾空白)。</summary>
        public string? Data { get; }

        /// <summary>设备的错误码 (A、B、D、F…);没有响应时为 null。</summary>
        public char? Code { get; }

        /// <summary>S 指令的值:<c>SC11+:90,0.00</c> 中逗号后的部分 (逗号前为同步计数)。</summary>
        public string? StatusValue => Data == null ? null : Data.IndexOf(',') is var comma && comma >= 0 ? Data.Substring(comma + 1).Trim() : Data;

        /// <summary>写入或动作被拒绝时对应的 XTC/3 错误码:B → B,F → F,其他 (对象缺失 A/D、传输错误) → E (ZEV-FAR-01 表 7)。</summary>
        public Xtc3ErrorCode Error => Code switch
        {
            'B' => Xtc3ErrorCode.IllegalValue,
            'F' => Xtc3ErrorCode.CannotDoNow,
            _ => Xtc3ErrorCode.DataNotAvailable,
        };

        public static DeviceAnswer From(ZevisionResponse response) => new DeviceAnswer(response.IsSuccess, response.Data?.Trim(), response.RawErrorCode);

        public override string ToString() => IsSuccess ? "+" + Data : "-" + (Code?.ToString() ?? "(no response)");
    }

    /// <summary>影子 Film 的配置中中间层要用到的值 (连接时从设备配置读取,写入时更新)。</summary>
    internal sealed class FilmInfo
    {
        /// <summary>传感器 <c>Fkk070101</c> (S8、采样的 ss)。</summary>
        public int Sensor { get; set; } = 1;

        /// <summary>输出源 <c>Fkk0901</c> (S10、坩埚上限)。</summary>
        public int Source { get; set; } = 1;

        /// <summary>最大功率 <c>Fkk0906</c> (S15)。</summary>
        public decimal MaxPower { get; set; } = 100;

        /// <summary>刻蚀模式 <c>P02kk08</c> (S33)。</summary>
        public bool Etch { get; set; }
    }

    /// <summary>
    /// 中间层的命令执行器:按 ZEV-FAR-01 附录 A 把 XTC/3 命令转为对 IMC300 的 Zevision 指令,
    /// 维护影子对象、采样缓冲和推导状态。设备连接由 <see cref="Xtc3Bridge"/> 管理。
    /// </summary>
    /// <remarks>
    /// 线程:主机命令一次只执行一个包 (调用方保证);采样在后台线程上,与主机命令共用 <see cref="IDeviceService"/>
    /// (其发送锁按顺序排队)。共享的状态由 <see cref="_sync"/> 保护。
    /// </remarks>
    internal sealed partial class BridgeEngine
    {
        /// <summary>一帧最多的 Zevision 指令数 (与 <see cref="InstrumentConfigReader.QueryBatchSize"/> 相同;写入时为 10 组写入 + 读回)。</summary>
        internal const int MaxCommandsPerFrame = InstrumentConfigReader.QueryBatchSize;

        /// <summary>停止、复位时等待设备状态变化的最长时间 (实测各约 0.25 s 内变化)。</summary>
        internal static readonly TimeSpan StateWaitLimit = TimeSpan.FromSeconds(3);

        /// <summary>等待状态变化时查询 <c>C09</c> 的间隔。</summary>
        internal static readonly TimeSpan StatePollInterval = TimeSpan.FromMilliseconds(50);

        private readonly object _sync = new object();
        private readonly Xtc3BridgeOptions _options;
        private readonly IDeviceService _device;
        private readonly IBridgeStateStore _store;
        private readonly Func<TimeSpan> _clock;
        private readonly Action<BridgeLogLevel, string, Exception?> _log;
        private readonly ShadowFilm[] _films;
        private readonly ShadowCheck[] _checks;
        private readonly FilmInfo[] _filmInfo;
        private readonly HashSet<string> _unknownStates = new HashSet<string>(StringComparer.Ordinal);
        private InstrumentSnapshot? _snapshot;
        private int _activeProcess;
        private BridgeLocalState _local;
        private volatile bool _online;

        public BridgeEngine(Xtc3BridgeOptions options, IDeviceService device, IBridgeStateStore store, Func<TimeSpan> clock, Action<BridgeLogLevel, string, Exception?> log)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _films = Enumerable.Range(1, Xtc3Protocol.FilmCount).Select(n => new ShadowFilm(n, options.ShadowBase)).ToArray();
            _checks = _films.Select(f => new ShadowCheck(f, ShadowStatus.Unknown, "not connected", Array.Empty<string>())).ToArray();
            _filmInfo = _films.Select(_ => new FilmInfo()).ToArray();
            _local = LoadLocal();
        }

        /// <summary>与 IMC300 的连接已就绪 (连接、握手、核对影子对象都已完成)。为 false 时需要设备的命令回答 E (ZEV-FAR-01 的 P-06)。</summary>
        public bool IsOnline
        {
            get => _online;
            set => _online = value;
        }

        /// <summary>从中间层启动起的 tick 数 (每 250 ms 加 1,不回绕)。</summary>
        public long AbsoluteTick => _clock().Ticks / Xtc3Protocol.TickInterval.Ticks;

        /// <summary>响应中的 Timer:当前 tick 的低 8 位。</summary>
        public byte Timer => unchecked((byte)AbsoluteTick);

        /// <summary>各 Film 的影子对象核对结果。</summary>
        public IReadOnlyList<ShadowCheck> ShadowChecks
        {
            get
            {
                lock (_sync)
                {
                    return _checks.ToList();
                }
            }
        }

        /// <summary>UG14 修改的主机侧波特率;应在发出该响应之后切换,由主机侧通道取走。</summary>
        public int? PendingBaudRate { get; private set; }

        /// <summary>主机侧串口应使用的波特率:主机经 UG14 设定过的,否则为 <paramref name="initial"/> (中间层的设置)。</summary>
        public int HostBaudRate(int initial)
        {
            lock (_sync)
            {
                return _local.BaudRate is int index ? Xtc3Parameters.BaudRates[index] : initial;
            }
        }

        /// <summary>取走 <see cref="PendingBaudRate"/>。</summary>
        public int? TakePendingBaudRate()
        {
            var baud = PendingBaudRate;
            PendingBaudRate = null;
            return baud;
        }

        /// <summary>
        /// 按顺序执行一个命令包的命令。一条命令失败后,其后的命令不执行,回答 <see cref="Xtc3ErrorCode.PriorCommandFailed"/>。
        /// </summary>
        public async Task<IReadOnlyList<Xtc3CommandResult>> ExecuteAsync(IReadOnlyList<Xtc3Command> commands, CancellationToken cancellationToken)
        {
            var results = new List<Xtc3CommandResult>(commands.Count);
            var failed = false;
            foreach (var command in commands)
            {
                Xtc3CommandResult result;
                if (failed)
                {
                    result = Xtc3CommandResult.Fail(Xtc3ErrorCode.PriorCommandFailed);
                }
                else if (command.ParseError is { } error)
                {
                    result = Xtc3CommandResult.Fail(error);
                }
                else
                {
                    result = await ExecuteOneAsync(command, cancellationToken).ConfigureAwait(false);
                }

                results.Add(result);
                failed = !result.IsSuccess;
            }

            return results;
        }

        private async Task<Xtc3CommandResult> ExecuteOneAsync(Xtc3Command command, CancellationToken cancellationToken)
        {
            try
            {
                return command.Group switch
                {
                    Xtc3Command.EchoGroup => Xtc3CommandResult.Ok(command.Parameters[0]),
                    Xtc3Command.HelloGroup => Hello(command.Id!.Value),
                    Xtc3Command.StatusGroup => await StatusAsync(command, cancellationToken).ConfigureAwait(false),
                    Xtc3Command.RemoteGroup => await RemoteAsync(command, cancellationToken).ConfigureAwait(false),
                    Xtc3Command.QueryGroup when command.SubGroup == Xtc3Command.FilmSubGroup => await QueryFilmAsync(command, cancellationToken).ConfigureAwait(false),
                    Xtc3Command.UpdateGroup when command.SubGroup == Xtc3Command.FilmSubGroup => await UpdateFilmAsync(command, cancellationToken).ConfigureAwait(false),
                    Xtc3Command.QueryGroup when command.SubGroup == Xtc3Command.GeneralSubGroup => await QueryGeneralAsync(command, cancellationToken).ConfigureAwait(false),
                    Xtc3Command.UpdateGroup when command.SubGroup == Xtc3Command.GeneralSubGroup => await UpdateGeneralAsync(command, cancellationToken).ConfigureAwait(false),
                    _ => Xtc3CommandResult.Fail(Xtc3ErrorCode.IllegalCommand),
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _log(BridgeLogLevel.Error, "Command " + command + " failed: " + ex.Message, ex);
                return Xtc3CommandResult.Fail(Xtc3ErrorCode.DataNotAvailable);
            }
        }

        private Xtc3CommandResult Hello(int id) => id switch
        {
            1 => Xtc3CommandResult.Ok(Xtc3Value.FromString("XTC/3S Version " + _options.Version)),
            2 => Xtc3CommandResult.Ok(_options.HelloNumbers.Select(Xtc3Value.FromInteger)),
            _ => Xtc3CommandResult.Ok(Xtc3Value.FromFloat(float.Parse(_options.Version, NumberStyles.Float, CultureInfo.InvariantCulture))),
        };

        // ---------------------------------------------------------------- 连接后的准备

        /// <summary>
        /// 连接、握手之后:读取设备配置 (<c>?</c>),核对影子对象 (按设置创建缺失的),读取中间层要用的设备信息,检查开机次数 (S21)。
        /// </summary>
        /// <returns>读取设备配置失败时失败;影子对象有问题不算失败 (相应 Film 回答 E)。</returns>
        public async Task<Result> PrepareAsync(CancellationToken cancellationToken)
        {
            var config = new InstrumentConfigService(_device);
            var snapshot = await config.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (!snapshot.IsSuccess)
            {
                return Result.Fail(snapshot.Error);
            }

            var checks = _films.Select(f => ShadowObjects.Check(f, snapshot.Value.GetValue)).ToList();
            var missing = checks.Where(c => c.Status == ShadowStatus.Missing).ToList();
            if (missing.Count > 0 && _options.CreateMissingShadowObjects)
            {
                var created = await CreateAsync(config, snapshot.Value, missing, cancellationToken).ConfigureAwait(false);
                if (created)
                {
                    snapshot = await config.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
                    if (!snapshot.IsSuccess)
                    {
                        return Result.Fail(snapshot.Error);
                    }

                    checks = _films.Select(f => ShadowObjects.Check(f, snapshot.Value.GetValue)).ToList();
                }
            }

            Apply(snapshot.Value, checks);
            foreach (var check in checks.Where(c => c.Status != ShadowStatus.Ready))
            {
                _log(check.Status == ShadowStatus.Missing ? BridgeLogLevel.Warning : BridgeLogLevel.Error,
                    check.Film + " is not usable (" + check.Status + "): " + check.Detail, null);
            }

            CheckPowerCycle(snapshot.Value);
            return Result.Ok();
        }

        /// <summary>
        /// 创建缺失的影子对象 (设备应处于 Ready):按影子对象的配置走恢复流程 (按编号新建对象、追加子列表、写入初值并读回)。
        /// </summary>
        public async Task<Result> CreateShadowObjectsAsync(CancellationToken cancellationToken)
        {
            var config = new InstrumentConfigService(_device);
            var snapshot = await config.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (!snapshot.IsSuccess)
            {
                return Result.Fail(snapshot.Error);
            }

            var missing = _films.Select(f => ShadowObjects.Check(f, snapshot.Value.GetValue)).Where(c => c.Status == ShadowStatus.Missing).ToList();
            if (missing.Count > 0 && !await CreateAsync(config, snapshot.Value, missing, cancellationToken).ConfigureAwait(false))
            {
                return Result.Fail("Creating the shadow objects failed; see the log.");
            }

            return await PrepareAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> CreateAsync(IInstrumentConfigService config, InstrumentSnapshot snapshot, IReadOnlyList<ShadowCheck> missing, CancellationToken cancellationToken)
        {
            var recipe = ShadowObjects.BuildRecipe(missing, snapshot);
            var plan = RestorePlanner.Plan(recipe, snapshot);
            if (!plan.IsSuccess)
            {
                _log(BridgeLogLevel.Error, "Cannot create the shadow objects: " + plan.Error, null);
                return false;
            }

            foreach (var skip in plan.Value.Skipped)
            {
                _log(BridgeLogLevel.Warning, "Shadow object setting skipped: " + skip, null);
            }

            _log(BridgeLogLevel.Info, "Creating shadow objects for " + string.Join(", ", missing.Select(m => m.Film)) + ": " + string.Join(", ", missing.SelectMany(m => m.MissingObjects)), null);
            var report = await config.ExecuteRestoreAsync(recipe, plan.Value, null, cancellationToken).ConfigureAwait(false);
            if (!report.IsSuccess)
            {
                _log(BridgeLogLevel.Error, "Creating the shadow objects failed" + (report.FailedId != null ? " at " + report.FailedId : string.Empty) + ": "
                    + (report.Error ?? string.Join("; ", report.VerifyMismatches)), null);
                return false;
            }

            _log(BridgeLogLevel.Info, "Created " + report.CreatedCount + " objects, wrote " + report.WrittenCount + " settings.", null);
            return true;
        }

        /// <summary>保存核对结果和设备配置,取出各影子 Film 的传感器、输出源、最大功率和刻蚀模式。</summary>
        internal void Apply(InstrumentSnapshot snapshot, IReadOnlyList<ShadowCheck> checks)
        {
            lock (_sync)
            {
                _snapshot = snapshot;
                _activeProcess = ParseInt(snapshot.GetValue("P01")) ?? 0;
                for (var i = 0; i < _films.Length; i++)
                {
                    _checks[i] = checks[i];
                    var film = _films[i];
                    var info = _filmInfo[i];
                    info.Sensor = ParseInt(snapshot.GetValue(ShadowFilm.Item(film.FilmId, 0x07, 1, 1))) ?? 1;
                    info.Source = ParseInt(snapshot.GetValue(ShadowFilm.Item(film.FilmId, 0x09, 1))) ?? 1;
                    info.MaxPower = ParseDecimal(snapshot.GetValue(ShadowFilm.Item(film.FilmId, 0x09, 6))) ?? 100;
                    info.Etch = ParseInt(snapshot.GetValue(FilmMap.EtchMode.IdFor(film))) == 1;
                }
            }
        }

        /// <summary>开机次数 (<c>G0C</c>) 比上次连接时多:置 S21 的 bit2 (ZEV-FAR-01 的 5.4 g)。</summary>
        private void CheckPowerCycle(InstrumentSnapshot snapshot)
        {
            if (!long.TryParse(snapshot.GetValue("G0C")?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            {
                return;
            }

            UpdateLocal(local =>
            {
                if (local.PowerOnCount.HasValue && count > local.PowerOnCount.Value)
                {
                    local.PowerUpErrors |= (int)Xtc3PowerUpErrors.PowerCycle;
                }

                local.PowerOnCount = count;
            });
        }

        // ---------------------------------------------------------------- 与 IMC300 的收发

        /// <summary>
        /// 发送 Zevision 指令 (每 <see cref="MaxCommandsPerFrame"/> 条一帧) 并按回显取出各条的回答。
        /// </summary>
        /// <returns>与 <paramref name="commands"/> 一一对应的回答;未连接或通讯失败时为 null。</returns>
        internal async Task<DeviceAnswer[]?> ExchangeAsync(IReadOnlyList<string> commands, CancellationToken cancellationToken)
        {
            if (!_online)
            {
                return null;
            }

            var answers = new DeviceAnswer[commands.Count];
            for (var start = 0; start < commands.Count; start += MaxCommandsPerFrame)
            {
                var batch = commands.Skip(start).Take(MaxCommandsPerFrame).ToList();
                var reply = await _device.SendAsync(string.Join(";", batch), TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
                if (!reply.IsSuccess)
                {
                    _log(BridgeLogLevel.Warning, "IMC300: " + reply.Error, null);
                    return null;
                }

                var pool = reply.Value.Responses.ToList();
                for (var i = 0; i < batch.Count; i++)
                {
                    var echo = ZevisionResponseParser.EchoOf(batch[i]);
                    var index = pool.FindIndex(r => string.Equals(r.Command, echo, StringComparison.OrdinalIgnoreCase));
                    answers[start + i] = index < 0 ? DeviceAnswer.NoResponse : DeviceAnswer.From(pool[index]);
                    if (index >= 0)
                    {
                        pool.RemoveAt(index);
                    }
                }
            }

            return answers;
        }

        /// <summary>查询多项 (<c>Q</c>)。任一项失败时整体失败 (E)。</summary>
        private async Task<(string[]? Values, Xtc3ErrorCode Error)> QueryAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken)
        {
            if (ids.Count == 0)
            {
                return (Array.Empty<string>(), default);
            }

            var answers = await ExchangeAsync(ids.Select(ZevisionCommandSet.Query).ToList(), cancellationToken).ConfigureAwait(false);
            if (answers == null)
            {
                return (null, Xtc3ErrorCode.DataNotAvailable);
            }

            for (var i = 0; i < answers.Length; i++)
            {
                if (!answers[i].IsSuccess)
                {
                    _log(BridgeLogLevel.Warning, "Q" + ids[i] + " answered " + answers[i], null);
                    return (null, answers[i].Code == 'F' ? Xtc3ErrorCode.CannotDoNow : Xtc3ErrorCode.DataNotAvailable);
                }
            }

            return (answers.Select(a => a.Data ?? string.Empty).ToArray(), default);
        }

        /// <summary>
        /// 写入并读回核对:先查询各项的当前值,与要写入的值相同的项不写 (实测 IMC300 对写入相同的传感器编号回答 <c>-B</c>);
        /// 其余每项一条 <c>U</c> 加一条 <c>Q</c>,合并成帧。设备拒绝写入时回答对应的错误码,读回不一致时回答 B。
        /// 一帧中的指令都会执行 (Zevision 遇到错误继续执行),失败项之后同一帧中的写入不回滚。
        /// </summary>
        /// <returns>成功时为 null。</returns>
        private async Task<Xtc3ErrorCode?> WriteAsync(IReadOnlyList<DeviceWrite> requested, CancellationToken cancellationToken)
        {
            if (requested.Count == 0)
            {
                return null;
            }

            var current = await ExchangeAsync(requested.Select(w => ZevisionCommandSet.Query(w.Id)).ToList(), cancellationToken).ConfigureAwait(false);
            if (current == null)
            {
                return Xtc3ErrorCode.DataNotAvailable;
            }

            var writes = requested.Where((w, i) => !(current[i].IsSuccess && w.Matches(current[i].Data))).ToList();
            if (writes.Count == 0)
            {
                return null;
            }

            var commands = writes.SelectMany(w => new[] { ZevisionCommandSet.Update(w.Id, w.Value), ZevisionCommandSet.Query(w.Id) }).ToList();
            var answers = await ExchangeAsync(commands, cancellationToken).ConfigureAwait(false);
            if (answers == null)
            {
                return Xtc3ErrorCode.DataNotAvailable;
            }

            for (var i = 0; i < writes.Count; i++)
            {
                var update = answers[2 * i];
                if (!update.IsSuccess)
                {
                    _log(BridgeLogLevel.Warning, "U" + writes[i] + " answered " + update, null);
                    return update.Error;
                }

                var check = answers[(2 * i) + 1];
                if (!check.IsSuccess || !writes[i].Matches(check.Data))
                {
                    _log(BridgeLogLevel.Warning, "U" + writes[i] + " read back " + check, null);
                    return Xtc3ErrorCode.IllegalValue;
                }
            }

            return null;
        }

        /// <summary>发送一条动作指令 (<c>R</c> 或 <c>U</c>,不读回)。设备回答 A、B、F 时回答同样的错误码,其他失败回答 E。</summary>
        private async Task<Xtc3ErrorCode?> ActAsync(string command, CancellationToken cancellationToken)
        {
            var answers = await ExchangeAsync(new[] { command }, cancellationToken).ConfigureAwait(false);
            if (answers == null)
            {
                return Xtc3ErrorCode.DataNotAvailable;
            }

            var answer = answers[0];
            if (answer.IsSuccess)
            {
                return null;
            }

            _log(BridgeLogLevel.Warning, command + " answered " + answer, null);
            return answer.Code == 'A' ? Xtc3ErrorCode.IllegalCommand : answer.Error;
        }

        // ---------------------------------------------------------------- 影子 Film、本地参数

        /// <summary>主机给出的 Film 编号对应的影子 Film:编号不在 1~9 时为 B,影子对象不可用时为 E。</summary>
        private bool TryGetFilm(int number, out ShadowFilm film, out FilmInfo info, out Xtc3ErrorCode error)
        {
            film = _films[0];
            info = _filmInfo[0];
            if (number < 1 || number > _films.Length)
            {
                error = Xtc3ErrorCode.IllegalValue;
                return false;
            }

            film = _films[number - 1];
            info = _filmInfo[number - 1];
            lock (_sync)
            {
                error = _checks[number - 1].Status == ShadowStatus.Ready ? default : Xtc3ErrorCode.DataNotAvailable;
                return _checks[number - 1].Status == ShadowStatus.Ready;
            }
        }

        /// <summary>影子对象可用的 Film。</summary>
        private IReadOnlyList<ShadowFilm> ReadyFilms()
        {
            lock (_sync)
            {
                return _films.Where((f, i) => _checks[i].Status == ShadowStatus.Ready).ToList();
            }
        }

        /// <summary>活动工艺 (<c>P01</c>) 对应的 Film 编号;活动工艺不是影子工艺时为 null。</summary>
        private int? FilmOfProcess(int processNumber)
        {
            var film = processNumber - _options.ShadowBase;
            return film >= 1 && film <= _films.Length ? film : (int?)null;
        }

        /// <summary>设备配置中的值 (连接时读取的 <c>?</c>);没有时为 null。</summary>
        private string? SnapshotValue(string id)
        {
            lock (_sync)
            {
                return _snapshot?.GetValue(id);
            }
        }

        private BridgeLocalState LoadLocal()
        {
            try
            {
                var state = _store.Load();
                state.Normalize();
                return state;
            }
            catch (IOException ex)
            {
                _log(BridgeLogLevel.Error, "Cannot read the local parameters, using defaults: " + ex.Message, ex);
                return new BridgeLocalState();
            }
        }

        /// <summary>修改本地参数并保存;保存失败只记录 (值在内存中有效)。</summary>
        private void UpdateLocal(Action<BridgeLocalState> change)
        {
            BridgeLocalState copy;
            lock (_sync)
            {
                change(_local);
                copy = _local.Clone();
            }

            try
            {
                _store.Save(copy);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _log(BridgeLogLevel.Error, "Cannot save the local parameters: " + ex.Message, ex);
            }
        }

        private T ReadLocal<T>(Func<BridgeLocalState, T> read)
        {
            lock (_sync)
            {
                return read(_local);
            }
        }

        internal static int? ParseInt(string? text) =>
            decimal.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number >= int.MinValue && number <= int.MaxValue
                ? (int)Math.Round(number, 0, MidpointRounding.AwayFromZero)
                : (int?)null;

        internal static decimal? ParseDecimal(string? text) =>
            decimal.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : (decimal?)null;
    }
}
