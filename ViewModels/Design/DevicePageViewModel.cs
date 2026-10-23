using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Zevision.Core.Design;
using Zevision.Core.Localization;
using Zevision.ViewModels.Common;

namespace Zevision.ViewModels.Design
{
    /// <summary>
    /// 工艺编辑窗口的"设备"页 (只读):设备上的工艺 (膜层顺序、膜系、材料、速率、厚度) 和材料 (及其膜系)。
    /// 可以刷新,可以删除一项 (行末的按钮) 或全部工艺、全部材料;不改动本地的材料和工艺。内容由窗口在读取设备后更新。
    /// </summary>
    public sealed class DevicePageViewModel : ViewModelBase
    {
        private readonly ProcessDesignerViewModel _owner;
        private IReadOnlyList<DeviceObjectViewModel> _processes = Array.Empty<DeviceObjectViewModel>();
        private IReadOnlyList<DeviceObjectViewModel> _materials = Array.Empty<DeviceObjectViewModel>();
        private DeviceObjectViewModel? _selectedProcess;
        private DeviceObjectViewModel? _selectedMaterial;
        private string _statusText = string.Empty;

        internal DevicePageViewModel(ProcessDesignerViewModel owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            RefreshCommand = new AsyncRelayCommand(_ => _owner.RefreshDeviceAsync(), _owner.ReportException, _ => _owner.IsEditable);
            // 行末的删除按钮以该项为参数;没有参数时删除选中项。可用与否不看参数 (CommandParameter 可能在 Command 之后才绑定)。
            DeleteProcessCommand = new AsyncRelayCommand(
                p => (p as DeviceObjectViewModel ?? SelectedProcess) is { } process ? _owner.DeleteDeviceProcessAsync(process) : Task.CompletedTask,
                _owner.ReportException,
                _ => _owner.IsEditable);
            DeleteMaterialCommand = new AsyncRelayCommand(
                p => (p as DeviceObjectViewModel ?? SelectedMaterial) is { } material ? _owner.DeleteDeviceMaterialAsync(material) : Task.CompletedTask,
                _owner.ReportException,
                _ => _owner.IsEditable);
            DeleteAllProcessesCommand = new AsyncRelayCommand(_ => _owner.DeleteAllDeviceProcessesAsync(), _owner.ReportException, _ => _owner.IsEditable && Processes.Count > 0);
            DeleteAllMaterialsCommand = new AsyncRelayCommand(_ => _owner.DeleteAllDeviceMaterialsAsync(), _owner.ReportException, _ => _owner.IsEditable && Materials.Count > 0);
        }

        /// <summary>设备上的工艺。</summary>
        public IReadOnlyList<DeviceObjectViewModel> Processes
        {
            get => _processes;
            private set => SetProperty(ref _processes, value);
        }

        /// <summary>设备上的材料。</summary>
        public IReadOnlyList<DeviceObjectViewModel> Materials
        {
            get => _materials;
            private set => SetProperty(ref _materials, value);
        }

        /// <summary>工艺列表的标题,如"工艺 (10)"。</summary>
        public string ProcessesTitle => Lang.T($"工艺 ({Processes.Count})", $"Processes ({Processes.Count})");

        /// <summary>材料列表的标题,如"材料 (12)"。</summary>
        public string MaterialsTitle => Lang.T($"材料 ({Materials.Count})", $"Materials ({Materials.Count})");

        /// <summary>选中的工艺 (删除)。</summary>
        public DeviceObjectViewModel? SelectedProcess
        {
            get => _selectedProcess;
            set
            {
                if (SetProperty(ref _selectedProcess, value))
                {
                    RefreshCommands();
                }
            }
        }

        /// <summary>选中的材料 (删除)。</summary>
        public DeviceObjectViewModel? SelectedMaterial
        {
            get => _selectedMaterial;
            set
            {
                if (SetProperty(ref _selectedMaterial, value))
                {
                    RefreshCommands();
                }
            }
        }

        /// <summary>读取的设备和时间;上传或删除中断后提示重新读取。</summary>
        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        /// <summary>重新读取设备配置 (<c>?</c>)。</summary>
        public ICommand RefreshCommand { get; }

        /// <summary>删除设备上的工艺 (参数为 <see cref="DeviceObjectViewModel"/>,没有时为选中的工艺) 和只被它使用的膜层 (确认后)。</summary>
        public ICommand DeleteProcessCommand { get; }

        /// <summary>删除设备上的材料 (参数为 <see cref="DeviceObjectViewModel"/>,没有时为选中的材料)、引用它的膜系和用到这些膜系的膜层 (确认后;有工艺使用时不删除)。</summary>
        public ICommand DeleteMaterialCommand { get; }

        /// <summary>删除设备上的全部工艺和它们用到的膜层 (确认后)。</summary>
        public ICommand DeleteAllProcessesCommand { get; }

        /// <summary>删除设备上的全部材料、引用它们的膜系和用到这些膜系的膜层 (确认后;有工艺使用这些膜层时不删除)。</summary>
        public ICommand DeleteAllMaterialsCommand { get; }

        /// <summary>是否可以操作 (读取设备或上传期间不可以)。</summary>
        public bool IsEditable => _owner.IsEditable;

        /// <summary>显示设备的内容;选中项按 ID 保留。</summary>
        internal void Show(DeviceContents contents, string deviceName, DateTime time)
        {
            var selectedProcess = SelectedProcess?.Id;
            var selectedMaterial = SelectedMaterial?.Id;
            Processes = contents.Processes.Select(p => new DeviceObjectViewModel(p.Id, p.Name, ProcessDetails(p, contents))).ToList();
            Materials = contents.Materials.Select(m => new DeviceObjectViewModel(m.Id, m.Name, MaterialDetails(m, contents))).ToList();
            SelectedProcess = Processes.FirstOrDefault(p => p.Id == selectedProcess);
            SelectedMaterial = Materials.FirstOrDefault(m => m.Id == selectedMaterial);
            StatusText = Lang.T(
                $"设备 {deviceName} 上的工艺和材料,更新于 {time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}。",
                $"Processes and materials on the device {deviceName}, updated at {time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}.");
            OnPropertiesChanged(nameof(ProcessesTitle), nameof(MaterialsTitle));
            RefreshCommands();
        }

        /// <summary>上传或删除没有完成:设备上的内容可能已改变。</summary>
        internal void MarkStale() =>
            StatusText = Lang.T("设备上的内容可能已改变,单击“刷新”重新读取。", "The device may have changed; click \"Refresh\" to read it again.");

        internal void RefreshEditable() => OnPropertyChanged(nameof(IsEditable));

        /// <summary>
        /// 工艺的膜层,每层一行,如 <c>1. L01 L1: F01 L1 (M01 M1), 0.000 Å/s, 1.030 kÅ</c>;
        /// 膜层有几个膜系时用分号隔开,设备上没有的膜层、膜系注明"不存在"。
        /// </summary>
        internal static string ProcessDetails(DeviceProcess process, DeviceContents contents)
        {
            if (process.Layers.Count == 0)
            {
                return Lang.T("没有膜层", "No layers");
            }

            var lines = process.Layers.Select((number, index) =>
            {
                var position = (index + 1).ToString(CultureInfo.InvariantCulture) + ". ";
                var layer = contents.FindLayer(number);
                if (layer == null)
                {
                    return position + Lang.T($"膜层 {number} (不存在)", $"Layer {number} (missing)");
                }

                var films = layer.Films.Count == 0
                    ? " " + Lang.T("(没有膜系)", "(no films)")
                    : ": " + string.Join("; ", layer.Films.Select(f => LayerFilmText(f, contents)));
                return position + layer.Id + " " + layer.Name + films;
            });
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>材料的密度、Z 比和引用它的膜系,如 <c>密度 2.700 g/cm³,Z 比 1.080</c> 和 <c>膜系: F0B Al</c>。</summary>
        internal static string MaterialDetails(DeviceMaterial material, DeviceContents contents)
        {
            var films = contents.FilmsOf(material);
            return Lang.T($"密度 {material.Density} g/cm³,Z 比 {material.ZRatio}", $"Density {material.Density} g/cm³, Z ratio {material.ZRatio}")
                + Environment.NewLine
                + Lang.T("膜系: ", "Films: ")
                + (films.Count == 0 ? Lang.T("无", "none") : string.Join(", ", films.Select(f => f.Id + " " + f.Name)));
        }

        private static string LayerFilmText(DeviceLayerFilm entry, DeviceContents contents)
        {
            var film = contents.FindFilm(entry.Film);
            string text;
            if (film == null)
            {
                text = Lang.T($"膜系 {entry.Film} (不存在)", $"Film {entry.Film} (missing)");
            }
            else
            {
                var material = contents.FindMaterial(film.Material);
                text = film.Id + " " + film.Name + (material == null ? string.Empty : " (" + material.Id + " " + material.Name + ")");
            }

            return text + ", " + entry.Rate + " Å/s, " + entry.Thickness + " kÅ";
        }
    }

    /// <summary>"设备"页列表中的一项 (工艺或材料),不可变。</summary>
    public sealed class DeviceObjectViewModel : ImmutableBindingSource
    {
        internal DeviceObjectViewModel(string id, string name, string details)
        {
            Id = id;
            Name = name;
            Details = details;
        }

        /// <summary>对象 ID,如 <c>P0201</c>、<c>M01</c>。</summary>
        public string Id { get; }

        /// <summary>名称。</summary>
        public string Name { get; }

        /// <summary>标题,如 <c>P0201  P1</c>。</summary>
        public string Title => Id + "  " + Name;

        /// <summary>说明 (多行)。</summary>
        public string Details { get; }
    }
}
