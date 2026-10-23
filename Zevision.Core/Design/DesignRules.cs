using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Zevision.Commands;
using Zevision.Core.Configuration;
using Zevision.Core.Localization;

namespace Zevision.Core.Design
{
    /// <summary>
    /// 材料和工艺的检查。取值范围、选项和字符限制都按指令集 (<see cref="ZevisionCommandSet"/>、<see cref="ParameterValue"/>),
    /// 设置项与上传时 (<see cref="DesignBuilder"/>) 写入的完全相同。每条错误为"对象: 设置项: 原因"。
    /// </summary>
    public static class DesignRules
    {
        /// <summary>速率和厚度 (步骤的速率、最终厚度,速率斜坡的起始厚度、新速率) 最多的小数位数 (设备显示三位小数)。</summary>
        public const int StepDecimals = 3;

        /// <summary>
        /// 名称 (材料、膜系、工艺,以及生成的膜层名称) 最多的字符数。实机 (固件 1.8.11,2026-10-23):名称最多保存 31 个字符 (更长的被截断),
        /// 而帮助表 <c>?</c> 只显示值的前 30 个字符;复用和删除按 <c>?</c> 读到的名称比较,所以不超过 30 个。
        /// </summary>
        public const int MaxNameLength = 30;

        // 检查用的样例对象 (编号 01):只用来取设置项的定义和标签。
        private const string SampleMaterial = "M01";
        private const string SampleFilm = "F01";
        private const string SampleLayer = "L01";
        private const string SampleProcess = "P0201";

        /// <summary>
        /// 检查全部材料和工艺 (保存前):名称不能为空、不能重名,数值在设备的范围内,工艺至少一步且每步选了材料。
        /// 不检查设备 (传感器、输出源是否存在),见 <see cref="ValidateUpload"/>。
        /// </summary>
        /// <returns>错误,没有时为空。</returns>
        public static IReadOnlyList<string> Validate(DesignDocument document)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var errors = new List<string>();
            var materialNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var material in document.Materials)
            {
                CheckMaterial(errors, material, materialNames);
            }

            var processNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var process in document.Processes)
            {
                CheckProcess(errors, document, process, processNames);
            }

            return errors;
        }

        /// <summary>
        /// 上传一个工艺前的检查:工艺及其用到的材料 (同 <see cref="Validate"/>),以及设备上有所选的传感器和输出源、
        /// 坩埚号不超过输出源的坩埚数量。
        /// </summary>
        /// <returns>错误,没有时为空。</returns>
        public static IReadOnlyList<string> ValidateUpload(DesignDocument document, ProcessDesign process, InstrumentSnapshot snapshot)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (process is null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var errors = new List<string>();
            var materialNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var material in DesignBuilder.MaterialsOf(document, process))
            {
                CheckMaterial(errors, material, materialNames);
                CheckDevice(errors, material, snapshot);
            }

            CheckProcess(errors, document, process, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            return errors;
        }

        /// <summary>
        /// 工艺一步中一个输入值的问题,与保存前的检查相同 (数值、范围、字符,速率和厚度最多 <see cref="StepDecimals"/> 位小数),用于输入框的提示。
        /// 合法或为空时为 null:空值在保存前的检查中指出,刚添加的步骤不提示。
        /// </summary>
        /// <param name="field">哪一个值。</param>
        /// <param name="value">输入的文本。</param>
        public static string? CheckStepValue(StepField field, string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : FieldError(DesignBuilder.StepValue(SampleLayer, field, field >= StepField.RampStartThickness ? 1 : 0, value!));

        /// <summary>材料的显示名称 (没有名称时为"(未命名)")。</summary>
        public static string DisplayName(string? name) => string.IsNullOrWhiteSpace(name) ? Texts.DesignUnnamed : name!.Trim();

        private static void CheckMaterial(List<string> errors, MaterialDesign material, ISet<string> names)
        {
            var context = Texts.DesignMaterialContext(DisplayName(material.Name));
            CheckName(errors, context, DeviceIds.NameOf(SampleMaterial), material.Name, names);
            // 名称已检查;膜系名称与材料名称相同。
            var fields = DesignBuilder.MaterialObjectFields(material, SampleMaterial).Skip(1)
                .Concat(DesignBuilder.FilmFields(material, SampleFilm, SampleMaterial).Skip(1));
            foreach (var field in fields)
            {
                CheckField(errors, context, field);
            }
        }

        private static void CheckProcess(List<string> errors, DesignDocument document, ProcessDesign process, ISet<string> names)
        {
            var name = DisplayName(process.Name);
            var context = Texts.DesignProcessContext(name);
            CheckName(errors, context, DeviceIds.NameOf(SampleProcess), process.Name, names);
            if (process.Steps.Count == 0)
            {
                errors.Add(context + ": " + Texts.DesignNoSteps);
            }

            var firstSteps = new List<ProcessStepDesign>();
            for (var i = 0; i < process.Steps.Count; i++)
            {
                var step = process.Steps[i];
                var stepContext = Texts.DesignStepContext(name, i + 1);
                var material = document.FindMaterial(step.MaterialId);
                if (material == null)
                {
                    errors.Add(stepContext + ": " + Texts.DesignStepNoMaterial);
                }
                else if (!firstSteps.Any(s => DesignBuilder.IsSameLayer(s, step)))
                {
                    // 新建的膜层名称 (材料名称-工艺名称-步骤号) 也不能超过设备能完整读回的长度;参数相同的后续步骤用第一步的名称。
                    firstSteps.Add(step);
                    var layerName = DesignBuilder.LayerName(material, process, i + 1);
                    if (layerName.Length > MaxNameLength)
                    {
                        errors.Add(stepContext + ": " + Texts.DesignLayerNameTooLong(layerName, layerName.Length, MaxNameLength));
                    }
                }

                foreach (var field in DesignBuilder.StepFields(step, SampleLayer, null, SampleFilm))
                {
                    CheckField(errors, field.Ramp > 0 ? Texts.DesignRampContext(name, i + 1, field.Ramp) : stepContext, field);
                }
            }
        }

        private static void CheckName(List<string> errors, string context, string nameId, string? value, ISet<string> names)
        {
            var label = Label(nameId, isListItem: false);
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(context + ": " + label + ": " + Texts.DesignNameEmpty);
                return;
            }

            var error = ParameterValue.Validate(ZevisionCommandSet.Find(nameId), value!);
            if (error != null)
            {
                errors.Add(context + ": " + label + ": " + error);
            }
            else if (value!.Trim().Length > MaxNameLength)
            {
                errors.Add(context + ": " + label + ": " + Texts.DesignNameTooLong(MaxNameLength));
            }
            else if (!names.Add(value!.Trim()))
            {
                errors.Add(context + ": " + label + ": " + Texts.DesignNameDuplicate);
            }
        }

        private static void CheckField(List<string> errors, string context, DesignField field)
        {
            if (field.IsGenerated)
            {
                return;
            }

            var error = FieldError(field);
            if (error != null)
            {
                errors.Add(context + ": " + Label(field.Id, field.IsListItem) + ": " + error);
            }
        }

        /// <summary>设置项的值的问题 (指令集的类型、范围、字符,以及小数位数);合法时为 null。</summary>
        private static string? FieldError(DesignField field)
        {
            var error = ParameterValue.Validate(ZevisionCommandSet.Find(field.Id), field.Value);
            if (error == null && field.MaxDecimals is int decimals && ParameterValue.DecimalPlaces(field.Value) > decimals)
            {
                error = Texts.DesignTooManyDecimals(decimals);
            }

            return error;
        }

        /// <summary>设备上要有所选的传感器、输出源,坩埚号不超过输出源的坩埚数量。</summary>
        private static void CheckDevice(List<string> errors, MaterialDesign material, InstrumentSnapshot snapshot)
        {
            var context = Texts.DesignMaterialContext(DisplayName(material.Name));
            if (snapshot.GetValue(DeviceIds.Item("N", material.Sensor)) == null)
            {
                errors.Add(context + ": " + Texts.DesignSensorMissing(material.Sensor));
            }

            if (material.HasDelaySettings && material.TransferSensor != 0 && snapshot.GetValue(DeviceIds.Item("N", material.TransferSensor)) == null)
            {
                errors.Add(context + ": " + Label(DeviceIds.Item(DeviceIds.Item(SampleFilm, 0x04), 5), isListItem: false) + ": "
                    + Texts.DesignSensorMissing(material.TransferSensor));
            }

            var source = DeviceIds.Item("S", material.Source);
            if (snapshot.GetValue(source) == null)
            {
                errors.Add(context + ": " + Texts.DesignSourceMissing(material.Source));
                return;
            }

            // 坩埚数量按设备上该输出源的 S**07;膜系的输出源是要写入的值。
            var filmSource = DeviceIds.Item(DeviceIds.Item(SampleFilm, 0x09), 1);
            var crucibleId = DeviceIds.Item(DeviceIds.Item(SampleFilm, 0x09), 2);
            var crucible = material.Crucible.ToString(CultureInfo.InvariantCulture);
            var error = CrucibleRule.Check(crucibleId, crucible, id => id == filmSource ? material.Source.ToString(CultureInfo.InvariantCulture) : snapshot.GetValue(id));
            if (error != null)
            {
                errors.Add(context + ": " + Label(crucibleId, isListItem: false) + ": " + error);
            }
        }

        /// <summary>设置项的标签,如"密度 (g/cm³)";子列表中的项带列表名称和序号,如"预熔 2 · 斜坡功率 (%)"。</summary>
        private static string Label(string id, bool isListItem)
        {
            var definition = ZevisionCommandSet.Find(id);
            if (definition == null)
            {
                return id;
            }

            return isListItem ? definition.LabelFor(id) : definition.Label();
        }
    }
}
