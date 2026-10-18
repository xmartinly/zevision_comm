using Zevision.Core.Localization;

namespace Zevision.Storage
{
    /// <summary>
    /// 数据存储层的提示和错误文本。内容在 Zevision.Core 的 <c>Localization/Texts.json</c> 中 (键 = 成员名),按界面语言取 (<see cref="TextCatalog"/>)。
    /// </summary>
    internal static class Texts
    {
        public static string CannotCreateCaptureFile(string path, string reason) => TextCatalog.Format(nameof(CannotCreateCaptureFile), path, reason);

        public static string PathEmpty => TextCatalog.Get(nameof(PathEmpty));

        public static string LogSourceSaveCommunicationData => TextCatalog.Get(nameof(LogSourceSaveCommunicationData));

        public static string CaptureWriteFailed(string path, string reason) => TextCatalog.Format(nameof(CaptureWriteFailed), path, reason);

        public static string CaptureCloseFailed(string path, string reason) => TextCatalog.Format(nameof(CaptureCloseFailed), path, reason);

        public static string Truncated => TextCatalog.Get(nameof(Truncated));

        public static string SettingsFileEmpty(string path) => TextCatalog.Format(nameof(SettingsFileEmpty), path);

        public static string SettingsReadFailed(string path, string reason) => TextCatalog.Format(nameof(SettingsReadFailed), path, reason);

        public static string SettingsSaveFailed(string reason) => TextCatalog.Format(nameof(SettingsSaveFailed), reason);

        public static string RecipeSaveFailed(string? reason) => TextCatalog.Format(nameof(RecipeSaveFailed), reason);

        public static string RecipeReadFailed(string reason) => TextCatalog.Format(nameof(RecipeReadFailed), reason);

        public static string RecipeInvalid => TextCatalog.Get(nameof(RecipeInvalid));

        public static string Md5NotAllowed => TextCatalog.Get(nameof(Md5NotAllowed));

        public static string RecipeEmpty => TextCatalog.Get(nameof(RecipeEmpty));

        public static string RecipeFormat1 => TextCatalog.Get(nameof(RecipeFormat1));

        public static string RecipeFormatUnsupported(int version, int supported) => TextCatalog.Format(nameof(RecipeFormatUnsupported), version, supported);

        public static string Md5Missing => TextCatalog.Get(nameof(Md5Missing));

        public static string Md5Malformed => TextCatalog.Get(nameof(Md5Malformed));

        public static string Md5Mismatch => TextCatalog.Get(nameof(Md5Mismatch));

        public static string HeaderMissing => TextCatalog.Get(nameof(HeaderMissing));

        public static string DeviceNameMissing => TextCatalog.Get(nameof(DeviceNameMissing));

        public static string NoEntries => TextCatalog.Get(nameof(NoEntries));

        public static string EntryMissingFields(string id) => TextCatalog.Format(nameof(EntryMissingFields), id);

        public static string EmptyValue => TextCatalog.Get(nameof(EmptyValue));

        public static string InvalidEntryId(string id) => TextCatalog.Format(nameof(InvalidEntryId), id);
    }
}
