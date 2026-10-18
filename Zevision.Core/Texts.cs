using Zevision.Core.Localization;

namespace Zevision.Core
{
    /// <summary>
    /// 业务层的提示和错误文本。内容在 <c>Localization/Texts.json</c> 中 (键 = 成员名),按界面语言取 (<see cref="TextCatalog"/>)。
    /// 有参数的文本是 <see cref="string.Format(System.IFormatProvider, string, object[])"/> 的格式,参数的个数与顺序由测试检查。
    /// </summary>
    internal static class Texts
    {
        public static string ReadConfigFailed => TextCatalog.Get(nameof(ReadConfigFailed));

        public static string ReadConfigUnparseable => TextCatalog.Get(nameof(ReadConfigUnparseable));

        public static string ReadGroupFailed(string command) => TextCatalog.Format(nameof(ReadGroupFailed), command);

        public static string HelpTableNotParsed => TextCatalog.Get(nameof(HelpTableNotParsed));

        public static string QueryFailed => TextCatalog.Get(nameof(QueryFailed));

        public static string QueryNoResponse => TextCatalog.Get(nameof(QueryNoResponse));

        public static string CannotReadDeviceName => TextCatalog.Get(nameof(CannotReadDeviceName));

        public static string DeviceModelMismatch(string fileDevice, string connectedDevice) => TextCatalog.Format(nameof(DeviceModelMismatch), fileDevice, connectedDevice);

        public static string DeviceNotReadyForChange(string? state, string readyState) => TextCatalog.Format(nameof(DeviceNotReadyForChange), state, readyState);

        public static string CannotReadListCount(string listId, string countId) => TextCatalog.Format(nameof(CannotReadListCount), listId, countId);

        public static string NoResponseData => TextCatalog.Get(nameof(NoResponseData));

        public static string NoResponse => TextCatalog.Get(nameof(NoResponse));

        public static string RejectedByDevice(string code, string description) => TextCatalog.Format(nameof(RejectedByDevice), code, description);

        public static string NoItemToRemove(string listId) => TextCatalog.Format(nameof(NoItemToRemove), listId);

        public static string ListCountAfterRemove(string command, string listId, int expected) => TextCatalog.Format(nameof(ListCountAfterRemove), command, listId, expected);

        public static string StageWritingSystem => TextCatalog.Get(nameof(StageWritingSystem));

        public static string StageWriting => TextCatalog.Get(nameof(StageWriting));

        public static string CannotChange(string id, string reason) => TextCatalog.Format(nameof(CannotChange), id, reason);

        public static string ValueHasProtocolChars(string id) => TextCatalog.Format(nameof(ValueHasProtocolChars), id);

        public static string StageCheckingDevice => TextCatalog.Get(nameof(StageCheckingDevice));

        public static string AppendedInvalidCount(string listId, int count) => TextCatalog.Format(nameof(AppendedInvalidCount), listId, count);

        public static string AppendedItem(string listId) => TextCatalog.Format(nameof(AppendedItem), listId);

        public static string AppendedBut(string itemId) => TextCatalog.Format(nameof(AppendedBut), itemId);

        public static string WriteFailed(string command, string error) => TextCatalog.Format(nameof(WriteFailed), command, error);

        public static string ReadBackDiffers(string id, string value) => TextCatalog.Format(nameof(ReadBackDiffers), id, value);

        public static string VerifyMismatch(string id, string expected, string? actual) => TextCatalog.Format(nameof(VerifyMismatch), id, expected, actual);

        public static string StageVerifying => TextCatalog.Get(nameof(StageVerifying));

        public static string CreateRoundsExceeded(int rounds) => TextCatalog.Format(nameof(CreateRoundsExceeded), rounds);

        public static string StageCreating => TextCatalog.Get(nameof(StageCreating));

        public static string StageCreatingRound(int round) => TextCatalog.Format(nameof(StageCreatingRound), round);

        public static string CreateFailed(string kind, string id, string command, string error) => TextCatalog.Format(nameof(CreateFailed), kind, id, command, error);

        public static string StageRereading => TextCatalog.Get(nameof(StageRereading));

        public static string AfterCreatingObjects => TextCatalog.Get(nameof(AfterCreatingObjects));

        public static string ReplanFailed => TextCatalog.Get(nameof(ReplanFailed));

        public static string CreatedObjectMissing(string kind, string id) => TextCatalog.Format(nameof(CreatedObjectMissing), kind, id);

        public static string IdEmpty => TextCatalog.Get(nameof(IdEmpty));

        public static string OutOfRange => TextCatalog.Get(nameof(OutOfRange));

        public static string MustBeOneOf => TextCatalog.Get(nameof(MustBeOneOf));

        public static string MustBeObjectNumber => TextCatalog.Get(nameof(MustBeObjectNumber));

        public static string MustBeInteger => TextCatalog.Get(nameof(MustBeInteger));

        public static string MustBeNumber => TextCatalog.Get(nameof(MustBeNumber));

        public static string CannotReadDeviceListCount(string listId) => TextCatalog.Format(nameof(CannotReadDeviceListCount), listId);

        public static string ListCountDiffers(string listId, int deviceCount, int fileCount) => TextCatalog.Format(nameof(ListCountDiffers), listId, deviceCount, fileCount);

        public static string NotRestorableNoValue => TextCatalog.Get(nameof(NotRestorableNoValue));

        public static string NotRestorableGroup => TextCatalog.Get(nameof(NotRestorableGroup));

        public static string NotRestorableMalformedId => TextCatalog.Get(nameof(NotRestorableMalformedId));

        public static string NotRestorableReadOnly => TextCatalog.Get(nameof(NotRestorableReadOnly));

        public static string ObjectIdEmpty => TextCatalog.Get(nameof(ObjectIdEmpty));

        public static string ExcludedListCount => TextCatalog.Get(nameof(ExcludedListCount));

        public static string ExcludedSensorPosition => TextCatalog.Get(nameof(ExcludedSensorPosition));

        public static string ExcludedQcmRuntime => TextCatalog.Get(nameof(ExcludedQcmRuntime));

        public static string ExcludedQcmState => TextCatalog.Get(nameof(ExcludedQcmState));

        public static string ExcludedXiu => TextCatalog.Get(nameof(ExcludedXiu));

        public static string ExcludedRateDeviation => TextCatalog.Get(nameof(ExcludedRateDeviation));

        public static string ExcludedSourceOutput => TextCatalog.Get(nameof(ExcludedSourceOutput));

        public static string ExcludedShutter => TextCatalog.Get(nameof(ExcludedShutter));

        public static string ExcludedCrucible => TextCatalog.Get(nameof(ExcludedCrucible));

        public static string ExcludedInputState => TextCatalog.Get(nameof(ExcludedInputState));

        public static string ExcludedOutputState => TextCatalog.Get(nameof(ExcludedOutputState));

        public static string ExcludedProcessRunNumber => TextCatalog.Get(nameof(ExcludedProcessRunNumber));

        public static string ExcludedLastCrystal => TextCatalog.Get(nameof(ExcludedLastCrystal));

        public static string DeviceUnidentified => TextCatalog.Get(nameof(DeviceUnidentified));

        public static string RestoreModelMismatch(string fileDevice, string fileModel, string connectedDevice, string connectedModel) => TextCatalog.Format(nameof(RestoreModelMismatch), fileDevice, fileModel, connectedDevice, connectedModel);

        public static string FirmwareDiffers(string fileVersion, string deviceVersion) => TextCatalog.Format(nameof(FirmwareDiffers), fileVersion, deviceVersion);

        public static string EntrySuffix => TextCatalog.Get(nameof(EntrySuffix));

        public static string SubListEntry => TextCatalog.Get(nameof(SubListEntry));

        public static string DuplicateInFile => TextCatalog.Get(nameof(DuplicateInFile));

        public static string NotRestorablePrefix => TextCatalog.Get(nameof(NotRestorablePrefix));

        public static string SkipProtocolChars => TextCatalog.Get(nameof(SkipProtocolChars));

        public static string NotOnDevice => TextCatalog.Get(nameof(NotOnDevice));

        public static string ReferenceMissing(string kind, string id) => TextCatalog.Format(nameof(ReferenceMissing), kind, id);

        public static string OwnerObjectMissing(string objectId) => TextCatalog.Format(nameof(OwnerObjectMissing), objectId);

        public static string OwnerEntryMissing(string entryId, string listId) => TextCatalog.Format(nameof(OwnerEntryMissing), entryId, listId);

        public static string SubListNotContiguous(string listId, string entryId) => TextCatalog.Format(nameof(SubListNotContiguous), listId, entryId);

        public static string SettingNotOnDevice => TextCatalog.Get(nameof(SettingNotOnDevice));

        public static string AlreadyConnected => TextCatalog.Get(nameof(AlreadyConnected));

        public static string NotConnected => TextCatalog.Get(nameof(NotConnected));

        public static string LogSourceReadLoop => TextCatalog.Get(nameof(LogSourceReadLoop));

        public static string ReadLoopEnded => TextCatalog.Get(nameof(ReadLoopEnded));

        public static string LogSourceEventHandler => TextCatalog.Get(nameof(LogSourceEventHandler));

        public static string HandlerFailed(string eventName, string message) => TextCatalog.Format(nameof(HandlerFailed), eventName, message);

        public static string ConnectTimeout(double seconds) => TextCatalog.Format(nameof(ConnectTimeout), seconds);

        public static string CannotConnect(string endpoint, string detail) => TextCatalog.Format(nameof(CannotConnect), endpoint, detail);

        public static string SendFailed(string reason) => TextCatalog.Format(nameof(SendFailed), reason);

        public static string PartialDataShown => TextCatalog.Get(nameof(PartialDataShown));

        public static string ResponseTimeout(double milliseconds, string partial) => TextCatalog.Format(nameof(ResponseTimeout), milliseconds, partial);

        public static string Disconnected => TextCatalog.Get(nameof(Disconnected));

        public static string InvalidFrame => TextCatalog.Get(nameof(InvalidFrame));

        public static string UnparseableResponse(int length) => TextCatalog.Format(nameof(UnparseableResponse), length);

        public static string FramingReplyHasLength(string lengthId, string checksumId) => TextCatalog.Format(nameof(FramingReplyHasLength), lengthId, checksumId);

        public static string FramingReplyWithoutLength(string lengthId, string checksumId) => TextCatalog.Format(nameof(FramingReplyWithoutLength), lengthId, checksumId);

        public static string ErrorCodeNone => TextCatalog.Get(nameof(ErrorCodeNone));

        public static string ErrorCodeIllegalCommand => TextCatalog.Get(nameof(ErrorCodeIllegalCommand));

        public static string ErrorCodeIllegalArgument => TextCatalog.Get(nameof(ErrorCodeIllegalArgument));

        public static string ErrorCodeChecksum => TextCatalog.Get(nameof(ErrorCodeChecksum));

        public static string ErrorCodeIllegalFormat => TextCatalog.Get(nameof(ErrorCodeIllegalFormat));

        public static string ErrorCodeState => TextCatalog.Get(nameof(ErrorCodeState));

        public static string ErrorCodeLength => TextCatalog.Get(nameof(ErrorCodeLength));

        public static string ErrorCodeTimeout => TextCatalog.Get(nameof(ErrorCodeTimeout));

        public static string ErrorCodeUnknown => TextCatalog.Get(nameof(ErrorCodeUnknown));

        public static string FrameChecksumMismatch => TextCatalog.Get(nameof(FrameChecksumMismatch));

        public static string FrameInvalidLength => TextCatalog.Get(nameof(FrameInvalidLength));

        public static string FrameMissingStopByte => TextCatalog.Get(nameof(FrameMissingStopByte));

        public static string FrameTruncated => TextCatalog.Get(nameof(FrameTruncated));

        public static string FrameUnexpectedData => TextCatalog.Get(nameof(FrameUnexpectedData));

        public static string FrameTooShort => TextCatalog.Get(nameof(FrameTooShort));

        public static string FrameMissingContinuation => TextCatalog.Get(nameof(FrameMissingContinuation));

        public static string FrameIncomplete => TextCatalog.Get(nameof(FrameIncomplete));

        public static string FrameError => TextCatalog.Get(nameof(FrameError));

        public static string CommandEmpty => TextCatalog.Get(nameof(CommandEmpty));

        public static string CommandNotAscii(int codePoint) => TextCatalog.Format(nameof(CommandNotAscii), codePoint);

        public static string CommandHasDelimiters => TextCatalog.Get(nameof(CommandHasDelimiters));

        public static string ValueEmpty => TextCatalog.Get(nameof(ValueEmpty));

        public static string ValueNotAscii => TextCatalog.Get(nameof(ValueNotAscii));

        public static string ValueHasDelimiters => TextCatalog.Get(nameof(ValueHasDelimiters));

        public static string CommandTooLong(int maxLength) => TextCatalog.Format(nameof(CommandTooLong), maxLength);

        public static string FailedResultHasNoValue => TextCatalog.Get(nameof(FailedResultHasNoValue));

        public static string InvalidTime(int maxHours) => TextCatalog.Format(nameof(InvalidTime), maxHours);

        public static string RestoreNotReady(string state, string readyState) => TextCatalog.Format(nameof(RestoreNotReady), state, readyState);

        public static string UnknownValue => TextCatalog.Get(nameof(UnknownValue));
    }
}
