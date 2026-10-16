using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Commands;
using Zevision.Communication;
using Zevision.Localization;
using Zevision.Models;

namespace Zevision.Services
{
    /// <summary>
    /// <see cref="IInstrumentConfigReader"/> 的实现,也是修改配置 (<see cref="InstrumentConfigService"/>) 时与设备单条通讯的基础:
    /// 每条指令只取第一条响应,通讯失败、没有响应、设备拒绝都作为失败的 <see cref="Result"/> 返回,原因可直接显示。
    /// </summary>
    public class InstrumentConfigReader : IInstrumentConfigReader
    {
        /// <summary>批量查询时每帧的指令数。响应很短,20 条约 600 字节。</summary>
        public const int QueryBatchSize = 20;

        /// <summary>
        /// 读取 <c>?</c> 和批量查询的最低空闲超时。实机 (固件 1.8.11) 抓包:<c>?</c> 发出后约 380 ms 才收到第一个字节,
        /// 每个 57000 字节的包之间停顿约 350 ms。用户把响应超时设为 500 ms 时余量不足,因此这两类请求至少等 2 秒;
        /// 连接设置更长时用连接设置。批量查询一帧 20 条,处理时间尚未实测,同样按慢响应处理。
        /// </summary>
        public static readonly TimeSpan SlowReplyTimeout = TimeSpan.FromSeconds(2);

        private readonly IDeviceService _device;

        /// <summary>创建读取器。</summary>
        public InstrumentConfigReader(IDeviceService device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
        }

        /// <inheritdoc />
        public async Task<Result<InstrumentSnapshot>> ReadSnapshotAsync(CancellationToken cancellationToken)
        {
            var reply = await SendForDataAsync(ZevisionCommandSet.HelpCommand, SlowReplyTimeout, cancellationToken).ConfigureAwait(false);
            if (!reply.IsSuccess)
            {
                return Result<InstrumentSnapshot>.Fail(Lang.T("读取设备配置失败: ", "Failed to read the device configuration: ") + reply.Error);
            }

            var entries = HelpTableParser.Parse(reply.Value);
            if (entries.Count == 0)
            {
                return Result<InstrumentSnapshot>.Fail(Lang.T("读取设备配置失败: 无法解析帮助表。", "Failed to read the device configuration: the help table could not be parsed."));
            }

            return Result<InstrumentSnapshot>.Ok(new InstrumentSnapshot(entries));
        }

        /// <inheritdoc />
        public async Task<Result<IReadOnlyList<RecipeEntry>>> ReadGroupAsync(char group, CancellationToken cancellationToken)
        {
            var command = ZevisionCommandSet.HelpCommand + group.ToString();
            var failure = Lang.T($"读取设备配置 ({command}) 失败: ", $"Failed to read the device configuration ({command}): ");
            var reply = await SendForDataAsync(command, SlowReplyTimeout, cancellationToken).ConfigureAwait(false);
            if (!reply.IsSuccess)
            {
                return Result<IReadOnlyList<RecipeEntry>>.Fail(failure + reply.Error);
            }

            // 帮助表总有该组的数量项 (MFE、FFE、P02FE…):没有说明没解析出来,不能当作"该组为空"。
            var parsed = HelpTableParser.Parse(reply.Value).Where(e => e.Id[0] == group).ToList();
            return parsed.Any(e => DeviceIds.IsCount(e.Id))
                ? Result<IReadOnlyList<RecipeEntry>>.Ok(RecipeRules.SelectRestorable(parsed))
                : Result<IReadOnlyList<RecipeEntry>>.Fail(failure + Lang.T("无法解析帮助表。", "the help table could not be parsed."));
        }

        /// <inheritdoc />
        public async Task<Result<IReadOnlyList<QueryValue>>> QueryAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken)
        {
            if (ids is null)
            {
                throw new ArgumentNullException(nameof(ids));
            }

            var values = new List<QueryValue>(ids.Count);
            for (var start = 0; start < ids.Count; start += QueryBatchSize)
            {
                var batch = ids.Skip(start).Take(QueryBatchSize).ToList();
                var responses = await SendQueriesAsync(batch, SlowReplyTimeout, cancellationToken).ConfigureAwait(false);
                if (!responses.IsSuccess)
                {
                    return Result<IReadOnlyList<QueryValue>>.Fail(Lang.T("查询失败: ", "Query failed: ") + responses.Error);
                }

                values.AddRange(batch.Select((id, i) => responses.Value[i] is { } response
                    ? new QueryValue(id, response.IsSuccess ? response.Data?.Trim() ?? string.Empty : null, response.IsSuccess ? null : response.ErrorCode.Localized())
                    : new QueryValue(id, null, Lang.T("设备没有返回响应", "The device did not respond"))));
            }

            return Result<IReadOnlyList<QueryValue>>.Ok(values);
        }

        /// <summary>
        /// 修改设备前的检查:设备型号与配置一致 (G03),且处于空闲状态 (C09 = Ready)。两项合为一帧查询 (<c>QG03;QC09</c>)。
        /// </summary>
        /// <returns>不能修改时的原因;可以修改时为 null。</returns>
        protected async Task<string?> CheckDeviceReadyAsync(string deviceName, CancellationToken cancellationToken)
        {
            var replies = await SendQueriesAsync(new[] { "G03", "C09" }, TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
            var name = replies.IsSuccess ? ValueOf(replies.Value[0]) : Result<string>.Fail(replies.Error!); // 失败的 Result 总有原因
            if (!name.IsSuccess)
            {
                return Lang.T("无法读取设备名称 (G03): ", "Cannot read the device name (G03): ") + name.Error;
            }

            if (!string.Equals(name.Value, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                return Lang.T($"设备型号不符:文件来自 {deviceName},当前设备是 {name.Value}。", $"Device model mismatch: the file is from {deviceName}, the connected device is {name.Value}.");
            }

            var state = ValueOf(replies.Value[1]);
            if (!state.IsSuccess || !string.Equals(state.Value, RestorePlanner.ReadyState, StringComparison.OrdinalIgnoreCase))
            {
                var current = state.IsSuccess ? state.Value : state.Error;
                return Lang.T($"设备当前状态为 {current},只有在 {RestorePlanner.ReadyState} (空闲) 状态下才能修改配置。", $"The device state is {current}; the configuration can only be changed in the {RestorePlanner.ReadyState} (idle) state.");
            }

            return null;
        }

        /// <summary>
        /// 用 <c>Q&lt;id&gt;</c> 读取一项的当前值。
        /// </summary>
        /// <returns>值 (已去除首尾空白)。</returns>
        protected async Task<Result<string>> QueryValueAsync(string id, CancellationToken cancellationToken)
        {
            var reply = await SendForResponseAsync(ZevisionCommandSet.Query(id), TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
            return reply.IsSuccess
                ? Result<string>.Ok(reply.Value.Data?.Trim() ?? string.Empty)
                : Result<string>.Fail(reply.Error!); // 失败的 Result 总有原因
        }

        /// <summary>读取列表的条数 (<c>Q&lt;list&gt;FE</c>)。</summary>
        protected async Task<Result<int>> ReadListCountAsync(string listId, CancellationToken cancellationToken)
        {
            var count = await QueryValueAsync(DeviceIds.CountOf(listId), cancellationToken).ConfigureAwait(false);
            return count.IsSuccess && DeviceIds.TryParseCount(count.Value, out var number)
                ? Result<int>.Ok(number)
                : Result<int>.Fail(Lang.T($"无法读取 {listId} 的条数 ({DeviceIds.CountOf(listId)}): ", $"Cannot read the number of items in {listId} ({DeviceIds.CountOf(listId)}): ") + (count.IsSuccess ? count.Value : count.Error));
        }

        /// <summary>发送一条指令,要求设备接受。</summary>
        /// <returns>失败原因 (通讯失败、没有响应或设备拒绝及其错误码);成功时为 null。</returns>
        protected async Task<string?> SendExpectingSuccessAsync(string command, CancellationToken cancellationToken)
        {
            var reply = await SendForResponseAsync(command, TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
            return reply.IsSuccess ? null : reply.Error;
        }

        /// <summary>发送一条指令,要求设备接受并返回数据 (如 <c>?</c>、<c>?M</c>)。</summary>
        private async Task<Result<string>> SendForDataAsync(string command, TimeSpan minimumResponseTimeout, CancellationToken cancellationToken)
        {
            var reply = await SendForResponseAsync(command, minimumResponseTimeout, cancellationToken).ConfigureAwait(false);
            if (!reply.IsSuccess)
            {
                return Result<string>.Fail(reply.Error!); // 失败的 Result 总有原因
            }

            return reply.Value.Data == null
                ? Result<string>.Fail(Lang.T("无响应数据", "no response data"))
                : Result<string>.Ok(reply.Value.Data);
        }

        /// <summary>
        /// 发送一条指令并取第一条响应 (避开分隔符时附加的 <c>H01</c> 的响应在其后)。
        /// 通讯失败、没有响应或设备拒绝 (含原始错误码) 都作为失败返回,原因可直接显示。
        /// </summary>
        private async Task<Result<ZevisionResponse>> SendForResponseAsync(string command, TimeSpan minimumResponseTimeout, CancellationToken cancellationToken)
        {
            var result = await _device.SendAsync(command, minimumResponseTimeout, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return Result<ZevisionResponse>.Fail(result.Error!); // 失败的 Result 总有原因
            }

            var response = result.Value.Responses.FirstOrDefault();
            return response == null ? Result<ZevisionResponse>.Fail(NoResponse)
                : response.IsSuccess ? Result<ZevisionResponse>.Ok(response)
                : Result<ZevisionResponse>.Fail(DescribeRejection(response));
        }

        /// <summary>
        /// 把多条查询合并为一帧发送,按回显找出每条的响应 (响应顺序与指令相同,但用回显匹配更稳妥:设备可能少回或多回一条)。
        /// </summary>
        /// <returns>与 <paramref name="ids"/> 一一对应的响应,设备没有回应的为 null;通讯失败时为失败。</returns>
        private async Task<Result<IReadOnlyList<ZevisionResponse?>>> SendQueriesAsync(IReadOnlyList<string> ids, TimeSpan minimumResponseTimeout, CancellationToken cancellationToken)
        {
            var commands = ids.Select(ZevisionCommandSet.Query).ToList();
            var result = await _device.SendAsync(string.Join(";", commands), minimumResponseTimeout, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return Result<IReadOnlyList<ZevisionResponse?>>.Fail(result.Error!); // 失败的 Result 总有原因
            }

            var responses = new Dictionary<string, ZevisionResponse>(StringComparer.OrdinalIgnoreCase);
            foreach (var response in result.Value.Responses.Where(r => !responses.ContainsKey(r.Command)))
            {
                responses.Add(response.Command, response);
            }

            return Result<IReadOnlyList<ZevisionResponse?>>.Ok(
                commands.Select(c => responses.TryGetValue(ZevisionResponseParser.EchoOf(c), out var response) ? response : null).ToList());
        }

        /// <summary>查询的响应转为值 (已去除首尾空白);没有响应或设备拒绝时为失败,原因与单条发送相同。</summary>
        private static Result<string> ValueOf(ZevisionResponse? response) =>
            response == null ? Result<string>.Fail(NoResponse)
            : response.IsSuccess ? Result<string>.Ok(response.Data?.Trim() ?? string.Empty)
            : Result<string>.Fail(DescribeRejection(response));

        private static string NoResponse => Lang.T("设备没有返回响应。", "The device did not respond.");

        /// <summary>设备拒绝的说明,含原始错误码,如"设备拒绝: B 非法参数值"。</summary>
        private static string DescribeRejection(ZevisionResponse response)
        {
            var code = response.RawErrorCode.HasValue ? response.RawErrorCode.Value + " " : string.Empty;
            return Lang.T($"设备拒绝: {code}{response.ErrorCode.Localized()}", $"Rejected by the device: {code}{response.ErrorCode.Localized()}");
        }
    }
}
