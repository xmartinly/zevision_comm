using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Core.Configuration;
using Zevision.Xtc3;

namespace Zevision.Bridge
{
    /// <summary>Film 参数 (QF/UF) 和通用参数 (QG/UG),ZEV-FAR-01 表 A.1、A.2。</summary>
    internal sealed partial class BridgeEngine
    {
        /// <summary>Sensor Type:XTC/3 的选项 (下标) 对应的 IMC300 <c>N**02</c> 选项。XTC/3 的 Rotary (4) 对应 Generic (5) 待确认。</summary>
        private static readonly int[] SensorTypeToDevice = { 0, 2, 3, 4, 5, 1 };

        /// <summary>Source Control Voltage:XTC/3 的选项 (下标) 对应的输出源最大输出 <c>S**0A</c> (V)。</summary>
        private static readonly decimal[] SourceVoltages = { 10m, -10m, 5m, -5m, 2.5m, -2.5m };

        // ---------------------------------------------------------------- QF / UF

        private async Task<Xtc3CommandResult> QueryFilmAsync(Xtc3Command command, CancellationToken cancellationToken)
        {
            if (!TryGetFilm(command.Parameters[0].Integer, out var film, out var info, out var error))
            {
                return Xtc3CommandResult.Fail(error);
            }

            var ids = FilmIds(command.Id!.Value);
            var reads = ids.Select(id => FilmReadItems(id, film)).ToList();
            var (values, readError) = await QueryAsync(reads.SelectMany(r => r).ToList(), cancellationToken).ConfigureAwait(false);
            if (values == null)
            {
                return Xtc3CommandResult.Fail(readError);
            }

            var results = new List<Xtc3Value>();
            var position = 0;
            for (var i = 0; i < ids.Count; i++)
            {
                var texts = values.Skip(position).Take(reads[i].Count).ToList();
                position += reads[i].Count;
                if (!(FilmValue(ids[i], film, info, texts) is { } value))
                {
                    _log(BridgeLogLevel.Warning, "QF" + (int)ids[i] + " of " + film + ": cannot convert " + string.Join(", ", texts), null);
                    return Xtc3CommandResult.Fail(Xtc3ErrorCode.DataNotAvailable);
                }

                results.Add(value);
            }

            return Xtc3CommandResult.Ok(results);
        }

        private async Task<Xtc3CommandResult> UpdateFilmAsync(Xtc3Command command, CancellationToken cancellationToken)
        {
            if (!TryGetFilm(command.Parameters[0].Integer, out var film, out var info, out var error))
            {
                return Xtc3CommandResult.Fail(error);
            }

            var ids = FilmIds(command.Id!.Value);
            var values = command.Parameters.Skip(1).ToList();
            for (var i = 0; i < ids.Count; i++)
            {
                if (!Xtc3Parameters.Film[(int)ids[i]].IsValid(values[i]))
                {
                    return Xtc3CommandResult.Fail(Xtc3ErrorCode.IllegalValue);
                }
            }

            // 同一个 UF255 中的传感器、输出源先于依赖它们的项生效 (Transfer Sensor、坩埚上限)。
            var newValues = ids.Zip(values, (id, value) => (id, value)).ToDictionary(p => p.id, p => p.value);
            var sensor = newValues.TryGetValue(Xtc3FilmParameterId.Sensor, out var s) ? s.Integer : info.Sensor;
            var source = newValues.TryGetValue(Xtc3FilmParameterId.Source, out var src) ? src.Integer : info.Source;
            var running = false;
            if (newValues.ContainsKey(Xtc3FilmParameterId.Sensor) || newValues.ContainsKey(Xtc3FilmParameterId.TransferSensor))
            {
                var current = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
                running = current.Known && current.State is { } state && DeviceStates.IsRunning(state) && !current.Stopped;
            }

            var writes = new List<DeviceWrite>();
            var localChanges = new List<Action<BridgeLocalState>>();
            foreach (var id in ids)
            {
                var planned = PlanFilmWrite(id, newValues[id], film, sensor, source, running, writes, localChanges);
                if (planned.HasValue)
                {
                    return Xtc3CommandResult.Fail(planned.Value);
                }
            }

            var writeError = await WriteAsync(writes, cancellationToken).ConfigureAwait(false);
            if (writeError.HasValue)
            {
                return Xtc3CommandResult.Fail(writeError.Value);
            }

            if (localChanges.Count > 0)
            {
                UpdateLocal(local => localChanges.ForEach(change => change(local)));
            }

            lock (_sync)
            {
                info.Sensor = sensor;
                info.Source = source;
                if (newValues.TryGetValue(Xtc3FilmParameterId.MaxPower, out var maxPower) && Xtc3Parameter.TryToDecimal(maxPower, 2, out var power))
                {
                    info.MaxPower = power;
                }
            }

            return Xtc3CommandResult.Ok();
        }

        private static IReadOnlyList<Xtc3FilmParameterId> FilmIds(int id) => id == Xtc3Parameters.AllId
            ? Enumerable.Range(0, Xtc3Parameters.Film.Count).Select(i => (Xtc3FilmParameterId)i).ToList()
            : new[] { (Xtc3FilmParameterId)id };

        /// <summary>读取一个 Film 参数要查询的设置项 (本地参数没有)。</summary>
        private static IReadOnlyList<string> FilmReadItems(Xtc3FilmParameterId id, ShadowFilm film)
        {
            if (FilmMap.IsLocal(id))
            {
                return Array.Empty<string>();
            }

            if (id == Xtc3FilmParameterId.RateWatcherHoldTime)
            {
                return new[] { FilmMap.RateWatcherEnabled.IdFor(film), FilmMap.RateWatcherTime.IdFor(film) };
            }

            return new[] { FilmMap.Find(id)!.IdFor(film) };
        }

        /// <summary>设备的值 (按 <see cref="FilmReadItems"/> 的顺序) 转为 XTC/3 的值,并更新缓存的传感器、输出源、最大功率。</summary>
        private Xtc3Value? FilmValue(Xtc3FilmParameterId id, ShadowFilm film, FilmInfo info, IReadOnlyList<string> texts)
        {
            var parameter = Xtc3Parameters.Film[(int)id];
            switch (id)
            {
                case Xtc3FilmParameterId.ToolFactor2:
                    return Xtc3Value.FromFloat(ReadLocal(l => l.ToolFactor2[film.Number - 1]));
                case Xtc3FilmParameterId.GraphLabel:
                    return Xtc3Value.FromInteger(ReadLocal(l => l.GraphLabels[film.Number - 1]));
                case Xtc3FilmParameterId.GraphScale:
                    return Xtc3Value.FromInteger(ReadLocal(l => l.GraphScales[film.Number - 1]));
                case Xtc3FilmParameterId.RateWatcherHoldTime:
                    if (ParseInt(texts[0]) == 0)
                    {
                        return Xtc3Value.FromInteger(0);
                    }

                    return ParameterValue.TryParseTime(texts[1], out var seconds) ? Xtc3Value.FromInteger(seconds) : (Xtc3Value?)null;
                case Xtc3FilmParameterId.TimePower:
                    return ParseInt(texts[0]) is { } action ? Xtc3Value.FromInteger(action == FilmMap.FailureActionTimePower ? 1 : 0) : (Xtc3Value?)null;
                case Xtc3FilmParameterId.TransferSensor:
                    return ParseInt(texts[0]) is { } transfer ? Xtc3Value.FromInteger(transfer != 0 ? 1 : 0) : (Xtc3Value?)null;
            }

            var value = FilmMap.ToXtc(parameter, FilmMap.Find(id)!, texts[0]);
            if (value.HasValue)
            {
                lock (_sync)
                {
                    switch (id)
                    {
                        case Xtc3FilmParameterId.Sensor:
                            info.Sensor = value.Value.Integer;
                            break;
                        case Xtc3FilmParameterId.Source:
                            info.Source = value.Value.Integer;
                            break;
                        case Xtc3FilmParameterId.MaxPower:
                            info.MaxPower = ParseDecimal(texts[0]) ?? info.MaxPower;
                            break;
                    }
                }
            }

            return value;
        }

        /// <summary>
        /// 一个 Film 参数的写入:加入 <paramref name="writes"/> 或 <paramref name="localChanges"/>。
        /// </summary>
        /// <returns>不能写入时的错误码 (状态限制 F、坩埚超出 B、没有第二个传感器 B)。</returns>
        private Xtc3ErrorCode? PlanFilmWrite(
            Xtc3FilmParameterId id, Xtc3Value value, ShadowFilm film, int sensor, int source, bool running, List<DeviceWrite> writes, List<Action<BridgeLocalState>> localChanges)
        {
            var parameter = Xtc3Parameters.Film[(int)id];
            var index = film.Number - 1;
            switch (id)
            {
                case Xtc3FilmParameterId.ToolFactor2:
                    localChanges.Add(l => l.ToolFactor2[index] = value.Float);
                    return null;
                case Xtc3FilmParameterId.GraphLabel:
                    localChanges.Add(l => l.GraphLabels[index] = value.Integer);
                    return null;
                case Xtc3FilmParameterId.GraphScale:
                    localChanges.Add(l => l.GraphScales[index] = value.Integer);
                    return null;

                case Xtc3FilmParameterId.SoakPower1:
                case Xtc3FilmParameterId.SoakPower2:
                case Xtc3FilmParameterId.IdlePower:
                    Xtc3Parameter.TryToDecimal(value, parameter.Decimals, out var power);
                    var item = FilmMap.Find(id)!;
                    writes.Add(FilmMap.ToDevice(item.IdFor(film), Math.Min(power, FilmMap.MaxConditionPower), item.Decimals, parameter.Decimals));
                    return null;

                case Xtc3FilmParameterId.RateWatcherHoldTime:
                    var hold = value.Integer;
                    writes.Add(FilmMap.Integer(FilmMap.RateWatcherEnabled.IdFor(film), hold > 0 ? 1 : 0));
                    if (hold > 0)
                    {
                        writes.Add(new DeviceWrite(FilmMap.RateWatcherTime.IdFor(film), ParameterValue.FormatTime(hold)));
                    }

                    return null;

                case Xtc3FilmParameterId.TimePower:
                    writes.Add(FilmMap.Integer(FilmMap.Find(id)!.IdFor(film), value.Integer == 1 ? FilmMap.FailureActionTimePower : FilmMap.FailureActionStop));
                    return null;

                case Xtc3FilmParameterId.Sensor:
                    if (running)
                    {
                        return Xtc3ErrorCode.CannotDoNow;
                    }

                    break;

                case Xtc3FilmParameterId.TransferSensor:
                    if (running)
                    {
                        return Xtc3ErrorCode.CannotDoNow;
                    }

                    // 1 = 用另一个传感器传递:设备上为另一个传感器的编号。
                    var other = sensor == 1 ? 2 : 1;
                    if (value.Integer == 1 && other > SensorCount())
                    {
                        return Xtc3ErrorCode.IllegalValue;
                    }

                    writes.Add(FilmMap.Integer(FilmMap.Find(id)!.IdFor(film), value.Integer == 1 ? other : 0));
                    return null;

                case Xtc3FilmParameterId.Crucible:
                    var count = ParseInt(SnapshotValue("S" + source.ToString("X2", CultureInfo.InvariantCulture) + "07"));
                    if (value.Integer > 0 && count.HasValue && value.Integer > Math.Max(count.Value, CrucibleRule.DefaultCount))
                    {
                        return Xtc3ErrorCode.IllegalValue;
                    }

                    break;
            }

            var mapped = FilmMap.Find(id)!;
            writes.Add(FilmMap.ToDevice(parameter, mapped, mapped.IdFor(film), value));
            return null;
        }

        // ---------------------------------------------------------------- QG / UG

        private async Task<Xtc3CommandResult> QueryGeneralAsync(Xtc3Command command, CancellationToken cancellationToken)
        {
            var ids = GeneralIds(command.Id!.Value);
            var current = CurrentFilm() ?? ReadyFilms().FirstOrDefault();
            var items = new List<string?>();
            foreach (var id in ids)
            {
                var item = GeneralReadItem(id, current);
                if (item == null && NeedsDevice(id))
                {
                    return Xtc3CommandResult.Fail(Xtc3ErrorCode.DataNotAvailable);
                }

                items.Add(item);
            }

            var (values, error) = await QueryAsync(items.OfType<string>().ToList(), cancellationToken).ConfigureAwait(false);
            if (values == null)
            {
                return Xtc3CommandResult.Fail(error);
            }

            var results = new List<Xtc3Value>();
            var position = 0;
            for (var i = 0; i < ids.Count; i++)
            {
                var text = items[i] == null ? null : values[position++];
                if (!(GeneralValue(ids[i], text) is { } value))
                {
                    _log(BridgeLogLevel.Warning, "QG" + (int)ids[i] + ": cannot convert " + text, null);
                    return Xtc3CommandResult.Fail(Xtc3ErrorCode.DataNotAvailable);
                }

                results.Add(Xtc3Value.FromInteger(value));
            }

            return Xtc3CommandResult.Ok(results);
        }

        private async Task<Xtc3CommandResult> UpdateGeneralAsync(Xtc3Command command, CancellationToken cancellationToken)
        {
            var ids = GeneralIds(command.Id!.Value);
            var values = command.Parameters.Select(p => p.Integer).ToList();
            for (var i = 0; i < ids.Count; i++)
            {
                if (!Xtc3Parameters.General[(int)ids[i]].IsValid(Xtc3Value.FromInteger(values[i])))
                {
                    return Xtc3CommandResult.Fail(Xtc3ErrorCode.IllegalValue);
                }
            }

            var writes = new List<DeviceWrite>();
            var localChanges = new List<Action<BridgeLocalState>>();
            int? baud = null;
            bool? etch = null;
            int? activeProcess = null;
            for (var i = 0; i < ids.Count; i++)
            {
                var value = values[i];
                switch (ids[i])
                {
                    case Xtc3GeneralParameterId.FilmToRun:
                        if (!TryGetFilm(value, out var target, out _, out var filmError))
                        {
                            return Xtc3CommandResult.Fail(filmError);
                        }

                        if (CurrentFilm()?.Number == value)
                        {
                            break; // 已是该 Film,不写入,也不受状态限制
                        }

                        var current = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
                        if (current.Known && !current.Stopped && !(current.State == Xtc3State.Ready || current.State == Xtc3State.Idle))
                        {
                            return Xtc3CommandResult.Fail(Xtc3ErrorCode.CannotDoNow);
                        }

                        writes.Add(FilmMap.Integer("P01", target.ObjectNumber));
                        activeProcess = target.ObjectNumber;
                        break;

                    case Xtc3GeneralParameterId.StartLayerWithoutBackupXtal:
                    case Xtc3GeneralParameterId.StopOnMaxPower:
                    case Xtc3GeneralParameterId.DepositEtchMode:
                        var films = ReadyFilms();
                        if (films.Count == 0)
                        {
                            return Xtc3CommandResult.Fail(Xtc3ErrorCode.DataNotAvailable);
                        }

                        var (item, deviceValue) = ids[i] switch
                        {
                            Xtc3GeneralParameterId.StartLayerWithoutBackupXtal => (FilmMap.StartWithoutBackup, value),
                            Xtc3GeneralParameterId.StopOnMaxPower => (FilmMap.MaxPowerOption, value == 1 ? 2 : 0),
                            _ => (FilmMap.EtchMode, value),
                        };
                        writes.AddRange(films.Select(f => FilmMap.Integer(item.IdFor(f), deviceValue)));
                        if (ids[i] == Xtc3GeneralParameterId.DepositEtchMode)
                        {
                            etch = value == 1;
                        }

                        break;

                    case Xtc3GeneralParameterId.StopOnAlarms:
                        writes.Add(FilmMap.Integer("C02", value));
                        break;
                    case Xtc3GeneralParameterId.Test:
                        writes.Add(FilmMap.Integer("C1C", value));
                        break;
                    case Xtc3GeneralParameterId.Sensor1Type:
                        writes.Add(FilmMap.Integer("N0102", SensorTypeToDevice[value]));
                        break;
                    case Xtc3GeneralParameterId.Sensor2Type:
                        if (SensorCount() < 2)
                        {
                            return Xtc3CommandResult.Fail(Xtc3ErrorCode.DataNotAvailable);
                        }

                        writes.Add(FilmMap.Integer("N0202", SensorTypeToDevice[value]));
                        break;
                    case Xtc3GeneralParameterId.SourceControlVoltage:
                        writes.AddRange(Enumerable.Range(1, Math.Max(SourceCount(), 1))
                            .Select(s => FilmMap.ToDevice("S" + s.ToString("X2", CultureInfo.InvariantCulture) + "0A", SourceVoltages[value], 2, 1)));
                        break;
                    case Xtc3GeneralParameterId.RecorderMode:
                        localChanges.Add(l => l.RecorderMode = value);
                        break;
                    case Xtc3GeneralParameterId.RecorderRange:
                        localChanges.Add(l => l.RecorderRange = value);
                        break;
                    case Xtc3GeneralParameterId.RecorderFilter:
                        localChanges.Add(l => l.RecorderFilter = value);
                        break;
                    case Xtc3GeneralParameterId.AudioFeedback:
                        writes.Add(FilmMap.Integer("G0F01", value));
                        break;
                    case Xtc3GeneralParameterId.DimLcd:
                        writes.Add(FilmMap.Integer("G0E04", value));
                        break;
                    case Xtc3GeneralParameterId.BaudRate:
                        localChanges.Add(l => l.BaudRate = value);
                        baud = Xtc3Parameters.BaudRates[value];
                        break;
                    case Xtc3GeneralParameterId.Protocol:
                        if (value != 0)
                        {
                            return Xtc3CommandResult.Fail(Xtc3ErrorCode.IllegalValue); // 只支持 Standard 协议
                        }

                        break;
                }
            }

            var writeError = await WriteAsync(writes, cancellationToken).ConfigureAwait(false);
            if (writeError.HasValue)
            {
                return Xtc3CommandResult.Fail(writeError.Value);
            }

            if (localChanges.Count > 0)
            {
                UpdateLocal(local => localChanges.ForEach(change => change(local)));
            }

            lock (_sync)
            {
                if (etch.HasValue)
                {
                    foreach (var info in _filmInfo)
                    {
                        info.Etch = etch.Value;
                    }
                }

                _activeProcess = activeProcess ?? _activeProcess;
            }

            if (baud.HasValue)
            {
                PendingBaudRate = baud; // 主机侧通道在发出响应后切换 (波特率不变时不切换)
            }

            return Xtc3CommandResult.Ok();
        }

        private static IReadOnlyList<Xtc3GeneralParameterId> GeneralIds(int id) => id == Xtc3Parameters.AllId
            ? Enumerable.Range(0, Xtc3Parameters.General.Count).Select(i => (Xtc3GeneralParameterId)i).ToList()
            : new[] { (Xtc3GeneralParameterId)id };

        /// <summary>通用参数是否需要读设备 (本地参数不需要)。</summary>
        private static bool NeedsDevice(Xtc3GeneralParameterId id) =>
            !(id == Xtc3GeneralParameterId.RecorderMode || id == Xtc3GeneralParameterId.RecorderRange || id == Xtc3GeneralParameterId.RecorderFilter
              || id == Xtc3GeneralParameterId.BaudRate || id == Xtc3GeneralParameterId.Protocol);

        /// <summary>读取通用参数要查询的设置项;本地参数为 null,按 Film 的参数在没有可用的影子 Film 时也为 null。</summary>
        private static string? GeneralReadItem(Xtc3GeneralParameterId id, ShadowFilm? film) => id switch
        {
            Xtc3GeneralParameterId.FilmToRun => "P01",
            Xtc3GeneralParameterId.StartLayerWithoutBackupXtal => film == null ? null : FilmMap.StartWithoutBackup.IdFor(film),
            Xtc3GeneralParameterId.StopOnAlarms => "C02",
            Xtc3GeneralParameterId.StopOnMaxPower => film == null ? null : FilmMap.MaxPowerOption.IdFor(film),
            Xtc3GeneralParameterId.DepositEtchMode => film == null ? null : FilmMap.EtchMode.IdFor(film),
            Xtc3GeneralParameterId.Test => "C1C",
            Xtc3GeneralParameterId.Sensor1Type => "N0102",
            Xtc3GeneralParameterId.Sensor2Type => "N0202",
            Xtc3GeneralParameterId.SourceControlVoltage => "S010A",
            Xtc3GeneralParameterId.AudioFeedback => "G0F01",
            Xtc3GeneralParameterId.DimLcd => "G0E04",
            _ => null,
        };

        /// <summary>设备的值 (本地参数为 null) 转为通用参数的值;无法换算时为 null。</summary>
        private int? GeneralValue(Xtc3GeneralParameterId id, string? text)
        {
            switch (id)
            {
                case Xtc3GeneralParameterId.FilmToRun:
                    // 活动工艺不是影子工艺 (如 P01 = 0) 时为 0,QG255 仍可读出其他参数。
                    return ParseInt(text) is { } process ? FilmOfProcess(process) ?? 0 : (int?)null;
                case Xtc3GeneralParameterId.StopOnMaxPower:
                    return ParseInt(text) is { } option ? (option == 2 ? 1 : 0) : (int?)null;
                case Xtc3GeneralParameterId.Sensor1Type:
                case Xtc3GeneralParameterId.Sensor2Type:
                    var type = ParseInt(text);
                    var index = type.HasValue ? Array.IndexOf(SensorTypeToDevice, type.Value) : -1;
                    return index >= 0 ? index : (int?)null;
                case Xtc3GeneralParameterId.SourceControlVoltage:
                    if (!(ParseDecimal(text) is { } volts))
                    {
                        return null;
                    }

                    return Enumerable.Range(0, SourceVoltages.Length).OrderBy(i => Math.Abs(SourceVoltages[i] - volts)).First();
                case Xtc3GeneralParameterId.RecorderMode:
                    return ReadLocal(l => l.RecorderMode);
                case Xtc3GeneralParameterId.RecorderRange:
                    return ReadLocal(l => l.RecorderRange);
                case Xtc3GeneralParameterId.RecorderFilter:
                    return ReadLocal(l => l.RecorderFilter);
                case Xtc3GeneralParameterId.BaudRate:
                    // 主机未设定过时为中间层设置的波特率对应的选项;不是选项之一时为 115200 (4)。
                    var initial = _options.HostPort == null ? -1 : Xtc3Parameters.BaudRates.ToList().IndexOf(_options.HostPort.BaudRate);
                    return ReadLocal(l => l.BaudRate) ?? (initial >= 0 ? initial : Xtc3Parameters.BaudRates.Count - 1);
                case Xtc3GeneralParameterId.Protocol:
                    return 0;
                default:
                    return ParseInt(text);
            }
        }

        /// <summary>
        /// 当前 Film:活动工艺 (<c>P01</c>,连接时读取,由采样和 UG0 更新) 是可用的影子工艺时为它的 Film,否则为 null。
        /// </summary>
        private ShadowFilm? CurrentFilm()
        {
            int process;
            lock (_sync)
            {
                process = _activeProcess;
            }

            if (FilmOfProcess(process) is { } number && TryGetFilm(number, out var film, out _, out _))
            {
                return film;
            }

            return null;
        }

        private int SensorCount() => ParseInt(SnapshotValue("NFE")) ?? 1;

        private int SourceCount() => ParseInt(SnapshotValue("SFE")) ?? 1;
    }
}
