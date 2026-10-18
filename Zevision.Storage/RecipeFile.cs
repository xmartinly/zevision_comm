using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Zevision.Core;
using Zevision.Core.Configuration;

namespace Zevision.Storage
{
    /// <summary>
    /// recipe 文件的读写 (缩进的 UTF-8 JSON,见 <see cref="JsonFile"/>)。
    /// </summary>
    /// <remarks>
    /// 文件带 MD5 校验 (<see cref="InstrumentRecipe.Md5"/>),读取时校验不通过即拒绝,确保恢复的数据与保存时完全一致。
    /// MD5 能发现手工修改、传输损坏等意外改动;但算法是公开的,有意篡改者可以重新计算校验值。
    /// 如需防篡改,应改为带密钥的签名 (如 HMAC) 并妥善保管密钥。
    /// </remarks>
    public static class RecipeFile
    {
        /// <summary>文件扩展名。</summary>
        public const string Extension = ".json";

        private static readonly Regex Md5Pattern = new Regex("^[0-9a-fA-F]{32}$", RegexOptions.CultureInvariant);

        /// <summary>
        /// 计算并写入 MD5 后保存到文件 (覆盖)。先写临时文件再替换,避免写到一半时出错留下损坏的文件。
        /// </summary>
        public static Result Save(InstrumentRecipe recipe, string path)
        {
            if (recipe is null)
            {
                throw new ArgumentNullException(nameof(recipe));
            }

            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException(Texts.PathEmpty, nameof(path));
            }

            var md5 = ComputeMd5(recipe);
            if (!md5.IsSuccess)
            {
                return Result.Fail(Texts.RecipeSaveFailed(md5.Error));
            }

            recipe.Md5 = md5.Value;
            try
            {
                JsonFile.Write(path, recipe);
                return Result.Ok();
            }
            catch (Exception ex) when (JsonFile.IsExpectedFailure(ex))
            {
                return Result.Fail(Texts.RecipeSaveFailed(ex.Message));
            }
        }

        /// <summary>
        /// 读取并校验文件。格式版本不符、字段缺失、MD5 不符或 ID 不合法都返回失败。
        /// </summary>
        public static Result<InstrumentRecipe> Load(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException(Texts.PathEmpty, nameof(path));
            }

            InstrumentRecipe? recipe;
            try
            {
                recipe = JsonFile.Read<InstrumentRecipe>(path);
            }
            catch (Exception ex) when (JsonFile.IsExpectedFailure(ex))
            {
                return Result<InstrumentRecipe>.Fail(Texts.RecipeReadFailed(ex.Message));
            }

            var error = Validate(recipe);
            return error == null
                ? Result<InstrumentRecipe>.Ok(recipe!)
                : Result<InstrumentRecipe>.Fail(Texts.RecipeInvalid + error);
        }

        /// <summary>
        /// 计算除 <see cref="InstrumentRecipe.Md5"/> 以外所有数据的 MD5,返回 32 位小写十六进制。
        /// </summary>
        /// <remarks>
        /// 不对 JSON 原文计算:缩进、换行、字段顺序等格式差异不应被视为数据修改。
        /// 而是按固定顺序把每个字段写成 <c>长度:内容\n</c> (长度为 UTF-16 字符数),再对其 UTF-8 编码求 MD5。
        /// 带长度前缀保证编码无歧义 (例如值 "a\nb" 与两个字段 "a"、"b" 不会得到相同的输入)。
        /// 字段顺序:formatVersion、deviceName、modelNumber、firmwareVersion、serialNumber、savedAt、
        /// 条目数,然后每个条目的 id、value、description。修改此算法必须提升 <see cref="InstrumentRecipe.CurrentFormatVersion"/>。
        /// </remarks>
        /// <returns>失败只发生在系统启用了 FIPS 策略、禁止使用 MD5 时。</returns>
        public static Result<string> ComputeMd5(InstrumentRecipe recipe)
        {
            if (recipe is null)
            {
                throw new ArgumentNullException(nameof(recipe));
            }

            var canonical = new StringBuilder();
            Append(canonical, recipe.FormatVersion.ToString(CultureInfo.InvariantCulture));
            Append(canonical, recipe.DeviceName);
            Append(canonical, recipe.ModelNumber);
            Append(canonical, recipe.FirmwareVersion);
            Append(canonical, recipe.SerialNumber);
            Append(canonical, recipe.SavedAt);
            Append(canonical, (recipe.Entries?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
            if (recipe.Entries != null)
            {
                foreach (var entry in recipe.Entries)
                {
                    Append(canonical, entry?.Id);
                    Append(canonical, entry?.Value);
                    Append(canonical, entry?.Description);
                }
            }

            try
            {
#pragma warning disable CA5351 // 用户要求使用 MD5;这里只用于发现文件被意外修改,不用于安全防护 (见类说明)。
                using (var md5 = MD5.Create())
#pragma warning restore CA5351
                {
                    var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
                    var hex = new StringBuilder(hash.Length * 2);
                    foreach (var b in hash)
                    {
                        hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                    }

                    return Result<string>.Ok(hex.ToString());
                }
            }
            catch (InvalidOperationException ex)
            {
                // Windows 启用"使用 FIPS 兼容算法"策略时,.NET Framework 的 MD5 实现会抛出此异常。
                return Result<string>.Fail(Texts.Md5NotAllowed + ex.Message);
            }
        }

        private static void Append(StringBuilder sb, string? value)
        {
            // null 与空字符串区分开:null 写成 "-1:",保证缺失字段也会改变校验值。
            if (value == null)
            {
                sb.Append("-1:\n");
                return;
            }

            sb.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('\n');
        }

        private static string? Validate(InstrumentRecipe? recipe)
        {
            // DataContract 反序列化不调用构造函数,缺失的字段会是 null。
            if (recipe == null)
            {
                return Texts.RecipeEmpty;
            }

            if (recipe.FormatVersion == 1)
            {
                return Texts.RecipeFormat1;
            }

            if (recipe.FormatVersion != InstrumentRecipe.CurrentFormatVersion)
            {
                return Texts.RecipeFormatUnsupported(recipe.FormatVersion, InstrumentRecipe.CurrentFormatVersion);
            }

            if (string.IsNullOrEmpty(recipe.Md5))
            {
                return Texts.Md5Missing;
            }

            if (!Md5Pattern.IsMatch(recipe.Md5))
            {
                return Texts.Md5Malformed;
            }

            // 先校验 MD5 再检查内容:任何修改 (包括删除字段) 都应报告为"文件被修改",而不是某个具体字段的错误。
            var actual = ComputeMd5(recipe);
            if (!actual.IsSuccess)
            {
                return actual.Error;
            }

            if (!string.Equals(actual.Value, recipe.Md5, StringComparison.OrdinalIgnoreCase))
            {
                return Texts.Md5Mismatch;
            }

            if (recipe.DeviceName == null || recipe.ModelNumber == null || recipe.FirmwareVersion == null
                || recipe.SerialNumber == null || recipe.SavedAt == null)
            {
                return Texts.HeaderMissing;
            }

            if (recipe.DeviceName.Length == 0)
            {
                return Texts.DeviceNameMissing;
            }

            if (recipe.Entries == null || recipe.Entries.Count == 0)
            {
                return Texts.NoEntries;
            }

            foreach (var entry in recipe.Entries)
            {
                if (entry == null || !DeviceIds.IsWellFormed(entry.Id))
                {
                    return Texts.InvalidEntryId(entry?.Id ?? Texts.EmptyValue);
                }

                if (entry.Value == null || entry.Description == null)
                {
                    return Texts.EntryMissingFields(entry.Id);
                }
            }

            return null;
        }
    }
}
