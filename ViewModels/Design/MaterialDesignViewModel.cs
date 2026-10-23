using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Zevision.Commands;
using Zevision.Core.Design;
using Zevision.Core.Localization;
using Zevision.ViewModels.Common;
using Zevision.ViewModels.Editor;
using Zevision.ViewModels.Main;

namespace Zevision.ViewModels.Design
{
    /// <summary>
    /// 工艺编辑中的一种材料 (设备上的材料 + 膜系)。修改直接写入 <see cref="MaterialDesign"/>,并通知窗口有未保存的修改。
    /// </summary>
    public sealed class MaterialDesignViewModel : ViewModelBase
    {
        /// <summary>延迟选项和最大功率处理的选项:界面语言在启动时确定,所有材料共用一份。</summary>
        private static readonly IReadOnlyList<ParameterOption> DelayOptionList =
            (ZevisionCommandSet.Find("F010401")?.Options ?? Array.Empty<CommandOption>()).Select(ParameterOption.From).ToList();

        private static readonly IReadOnlyList<ParameterOption> MaxPowerOptionList =
            (ZevisionCommandSet.Find("F010907")?.Options ?? Array.Empty<CommandOption>()).Select(ParameterOption.From).ToList();

        private readonly ProcessDesignerViewModel _owner;
        private Choice<MaterialLibraryEntry>? _libraryPick;

        /// <summary>名称是从材料库填入的,之后没有改过:再次选择时跟着更新。</summary>
        private bool _nameFromLibrary;

        /// <summary>中文名是按输入的名称 (材料库的化学式) 自动填入的;名称不再是化学式时撤回。</summary>
        private bool _chineseFromName;

        /// <summary>密度和 Z 比是按输入的名称从这一项自动填入的;仍是这两个值 (没有改过) 时跟着名称更新或撤回。</summary>
        private MaterialLibraryEntry? _valuesFromName;

        private string _librarySearch = string.Empty;
        private bool _isLibraryOpen;

        internal MaterialDesignViewModel(MaterialDesign model, ProcessDesignerViewModel owner)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            PreConditions = new ObservableCollection<ConditionStepViewModel>(model.PreConditions.Select(s => new ConditionStepViewModel(s, this, isPre: true)));
            PostConditions = new ObservableCollection<ConditionStepViewModel>(model.PostConditions.Select(s => new ConditionStepViewModel(s, this, isPre: false)));
            Renumber();
            AddPreConditionCommand = new RelayCommand(_ => AddStep(isPre: true), _ => _owner.IsEditable);
            AddPostConditionCommand = new RelayCommand(_ => AddStep(isPre: false), _ => _owner.IsEditable);
        }

        /// <summary>数据。</summary>
        internal MaterialDesign Model { get; }

        /// <summary>标识 (工艺步骤按它引用材料)。</summary>
        public string Id => Model.Id;

        /// <summary>列表和下拉框中显示的名称:名称,中文界面加中文名,如"Al 铝" (没有名称时为"(未命名)")。</summary>
        public string DisplayName =>
            DesignRules.DisplayName(Model.Name) + (Lang.IsChinese && Model.ChineseName.Trim().Length > 0 ? " " + Model.ChineseName.Trim() : string.Empty);

        /// <summary>
        /// 名称 (写入设备)。是材料库中某一项的化学式 (只有一项) 时填入它的中文名;密度、Z 比还没有填,或仍是按名称自动填入的值时,
        /// 也填入密度和 Z 比 (逐字输入 <c>SiO2</c> 时中途填入的 <c>Si</c> 的数值会跟着换成 <c>SiO2</c> 的)。名称不再是化学式时,
        /// 撤回按名称自动填入、没有改过的中文名和数值;从材料库选择后填入的值改名时保留。
        /// </summary>
        public string Name
        {
            get => Model.Name;
            set
            {
                if (!SetName(value))
                {
                    return;
                }

                _nameFromLibrary = false;
                if (_owner.FindLibraryEntry(value) is MaterialLibraryEntry entry)
                {
                    SetChineseName(entry.ChineseName);
                    _chineseFromName = true;
                    if ((Model.Density.Length == 0 && Model.ZRatio.Length == 0) || HasValuesOf(_valuesFromName))
                    {
                        Density = entry.Density;
                        ZRatio = entry.ZRatio;
                        _valuesFromName = entry;
                    }

                    return;
                }

                if (_chineseFromName)
                {
                    SetChineseName(string.Empty);
                    _chineseFromName = false;
                }

                if (HasValuesOf(_valuesFromName))
                {
                    Density = string.Empty;
                    ZRatio = string.Empty;
                }

                _valuesFromName = null;
            }
        }

        /// <summary>中文名 (从材料库填入),只在本地显示,不写入设备。</summary>
        public string ChineseName => Model.ChineseName;

        /// <summary>设备上是否已有同名的材料和膜系,如"设备上: M10, F0B"、"设备上没有" (列表中名称下面的一行)。</summary>
        public string DeviceStatus => _owner.DeviceStatus(Model);

        /// <summary>
        /// 从材料库选择:填入密度、Z 比和中文名;名称为空,或是上一次从材料库填入的 (没有改过) 时,填入化学式。
        /// </summary>
        public Choice<MaterialLibraryEntry>? LibraryPick
        {
            get => _libraryPick;
            set
            {
                if (SetProperty(ref _libraryPick, value) && value != null)
                {
                    ApplyLibrary(value.Value);
                }
            }
        }

        /// <summary>
        /// 材料库下拉框中输入的关键字:按化学式、英文名、中文名查找 (包含即可,不区分大小写),并打开下拉框。
        /// 选择一项后下拉框显示该项的文字,不作为关键字。
        /// </summary>
        public string LibrarySearch
        {
            get => _librarySearch;
            set
            {
                value ??= string.Empty;
                if (_librarySearch == value)
                {
                    return;
                }

                _librarySearch = value;
                OnPropertyChanged();
                if (_libraryPick != null && value == _libraryPick.Text)
                {
                    return;
                }

                OnPropertyChanged(nameof(LibraryMatches));
                IsLibraryOpen = value.Trim().Length > 0;
            }
        }

        /// <summary>符合关键字的材料库项 (没有关键字时为全部),化学式完全相同、以关键字开头的排在前面。</summary>
        public IReadOnlyList<Choice<MaterialLibraryEntry>> LibraryMatches =>
            _owner.SearchLibrary(_libraryPick != null && _librarySearch == _libraryPick.Text ? string.Empty : _librarySearch);

        /// <summary>材料库下拉框是否打开 (输入关键字时打开)。</summary>
        public bool IsLibraryOpen
        {
            get => _isLibraryOpen;
            set => SetProperty(ref _isLibraryOpen, value);
        }

        /// <summary>密度,g/cm³。</summary>
        public string Density
        {
            get => Model.Density;
            set => Set(Model.Density, value, v => Model.Density = v);
        }

        /// <summary>Z 比。</summary>
        public string ZRatio
        {
            get => Model.ZRatio;
            set => Set(Model.ZRatio, value, v => Model.ZRatio = v);
        }

        /// <summary>材料的工具因子,%。</summary>
        public string Tooling
        {
            get => Model.Tooling;
            set => Set(Model.Tooling, value, v => Model.Tooling = v);
        }

        /// <summary>传感器编号。</summary>
        public int Sensor
        {
            get => Model.Sensor;
            set => Set(Model.Sensor, value, v => Model.Sensor = v);
        }

        /// <summary>传感器的选项:设备上的传感器;当前值不在设备上时另加一项"(不存在)"。</summary>
        public IReadOnlyList<Choice<int>> SensorChoices => WithCurrent(_owner.Sensors, Sensor);

        /// <summary>晶振位置。</summary>
        public string CrystalPosition
        {
            get => Model.CrystalPosition;
            set => Set(Model.CrystalPosition, value, v => Model.CrystalPosition = v);
        }

        /// <summary>输出源编号。改变后坩埚的选项随之改变。</summary>
        public int Source
        {
            get => Model.Source;
            set
            {
                if (Set(Model.Source, value, v => Model.Source = v))
                {
                    OnPropertyChanged(nameof(CrucibleChoices));
                }
            }
        }

        /// <summary>输出源的选项。</summary>
        public IReadOnlyList<Choice<int>> SourceChoices => WithCurrent(_owner.Sources, Source);

        /// <summary>坩埚:0 = 任意,1~所选输出源的坩埚数量。</summary>
        public int Crucible
        {
            get => Model.Crucible;
            set => Set(Model.Crucible, value, v => Model.Crucible = v);
        }

        /// <summary>坩埚的选项:"0 - 任意",1~所选输出源的坩埚数量 (未知时为 1)。</summary>
        public IReadOnlyList<Choice<int>> CrucibleChoices
        {
            get
            {
                var count = _owner.CrucibleCount(Source);
                var choices = new List<Choice<int>> { new Choice<int>(0, "0 - " + Lang.T("任意", "Any"), string.Empty) };
                choices.AddRange(Enumerable.Range(1, Math.Max(count, 1)).Select(n => new Choice<int>(n, n.ToString(CultureInfo.InvariantCulture), string.Empty)));
                return WithCurrent(choices, Crucible);
            }
        }

        /// <summary>延迟选项的值 (0~3)。</summary>
        public string DelayOption
        {
            get => Model.DelayOption;
            set
            {
                // 下拉框解除绑定或切换材料时可能写回 null:不当作修改。
                if (value != null && Set(Model.DelayOption, value, v => Model.DelayOption = v))
                {
                    OnPropertyChanged(nameof(HasDelaySettings));
                }
            }
        }

        /// <summary>是否显示延迟设置 (延迟选项不为 0)。</summary>
        public bool HasDelaySettings => Model.HasDelaySettings;

        /// <summary>控制延迟时间,H:MM:SS。</summary>
        public string ControlDelayTime
        {
            get => Model.ControlDelayTime;
            set => Set(Model.ControlDelayTime, value, v => Model.ControlDelayTime = v);
        }

        /// <summary>挡板延迟精度,%。</summary>
        public string ShutterDelayAccuracy
        {
            get => Model.ShutterDelayAccuracy;
            set => Set(Model.ShutterDelayAccuracy, value, v => Model.ShutterDelayAccuracy = v);
        }

        /// <summary>挡板延迟超时,H:MM:SS。</summary>
        public string ShutterDelayTimeout
        {
            get => Model.ShutterDelayTimeout;
            set => Set(Model.ShutterDelayTimeout, value, v => Model.ShutterDelayTimeout = v);
        }

        /// <summary>传输传感器编号,0 = 无。</summary>
        public int TransferSensor
        {
            get => Model.TransferSensor;
            set => Set(Model.TransferSensor, value, v => Model.TransferSensor = v);
        }

        /// <summary>传输传感器的选项:"0 - 无"和设备上的传感器。</summary>
        public IReadOnlyList<Choice<int>> TransferSensorChoices =>
            WithCurrent(new[] { new Choice<int>(0, "0 - " + Lang.T("无", "None"), string.Empty) }.Concat(_owner.Sensors).ToList(), TransferSensor);

        /// <summary>传输工具因子,%。</summary>
        public string TransferTooling
        {
            get => Model.TransferTooling;
            set => Set(Model.TransferTooling, value, v => Model.TransferTooling = v);
        }

        /// <summary>延迟选项 (指令集 <c>F**0401</c> 的选项;所有材料共用)。</summary>
        public IReadOnlyList<ParameterOption> DelayOptions => DelayOptionList;

        /// <summary>PID 的 P 项。</summary>
        public string PTerm
        {
            get => Model.PTerm;
            set => Set(Model.PTerm, value, v => Model.PTerm = v);
        }

        /// <summary>PID 的 I 项,秒。</summary>
        public string ITerm
        {
            get => Model.ITerm;
            set => Set(Model.ITerm, value, v => Model.ITerm = v);
        }

        /// <summary>PID 的 D 项,秒。</summary>
        public string DTerm
        {
            get => Model.DTerm;
            set => Set(Model.DTerm, value, v => Model.DTerm = v);
        }

        /// <summary>最大功率,%。</summary>
        public string MaxPower
        {
            get => Model.MaxPower;
            set => Set(Model.MaxPower, value, v => Model.MaxPower = v);
        }

        /// <summary>达到最大功率时的处理 (0~3)。</summary>
        public string MaxPowerOption
        {
            get => Model.MaxPowerOption;
            set
            {
                // 下拉框解除绑定或切换材料时可能写回 null:不当作修改。
                if (value != null)
                {
                    Set(Model.MaxPowerOption, value, v => Model.MaxPowerOption = v);
                }
            }
        }

        /// <summary>最大功率处理的选项 (指令集 <c>F**0907</c> 的选项;所有材料共用)。</summary>
        public IReadOnlyList<ParameterOption> MaxPowerOptions => MaxPowerOptionList;

        /// <summary>预熔。</summary>
        public ObservableCollection<ConditionStepViewModel> PreConditions { get; }

        /// <summary>后处理。</summary>
        public ObservableCollection<ConditionStepViewModel> PostConditions { get; }

        /// <summary>添加一段预熔。</summary>
        public ICommand AddPreConditionCommand { get; }

        /// <summary>添加一段后处理。</summary>
        public ICommand AddPostConditionCommand { get; }

        /// <summary>窗口是否可以编辑 (上传期间不可以)。</summary>
        public bool IsEditable => _owner.IsEditable;

        /// <summary>删除一段预熔或后处理。</summary>
        internal void RemoveStep(ConditionStepViewModel step)
        {
            var steps = step.IsPre ? PreConditions : PostConditions;
            var models = step.IsPre ? Model.PreConditions : Model.PostConditions;
            if (steps.Remove(step))
            {
                models.Remove(step.Model);
                Renumber();
                _owner.MarkDirty();
            }
        }

        /// <summary>窗口的可编辑状态改变 (上传开始/结束)。</summary>
        internal void RefreshEditable() => OnPropertyChanged(nameof(IsEditable));

        /// <summary>设备上的内容改变 (读取、上传、删除后)。</summary>
        internal void RefreshDeviceStatus() => OnPropertyChanged(nameof(DeviceStatus));

        internal void MarkDirty() => _owner.MarkDirty();

        private void AddStep(bool isPre)
        {
            var model = new ConditionStep();
            (isPre ? Model.PreConditions : Model.PostConditions).Add(model);
            (isPre ? PreConditions : PostConditions).Add(new ConditionStepViewModel(model, this, isPre));
            Renumber();
            _owner.MarkDirty();
        }

        private void Renumber()
        {
            for (var i = 0; i < PreConditions.Count; i++)
            {
                PreConditions[i].Number = i + 1;
            }

            for (var i = 0; i < PostConditions.Count; i++)
            {
                PostConditions[i].Number = i + 1;
            }
        }

        /// <summary>填入材料库中的一项 (见 <see cref="LibraryPick"/>)。</summary>
        /// <summary>密度和 Z 比仍是 <paramref name="entry"/> 的值 (自动填入后没有改过)。</summary>
        private bool HasValuesOf(MaterialLibraryEntry? entry) =>
            entry != null && Model.Density == entry.Density && Model.ZRatio == entry.ZRatio;

        private void ApplyLibrary(MaterialLibraryEntry entry)
        {
            // 从材料库选择的值:改名时保留。
            _chineseFromName = false;
            _valuesFromName = null;
            if (string.IsNullOrWhiteSpace(Model.Name) || _nameFromLibrary)
            {
                SetName(entry.Formula);
                _nameFromLibrary = true;
            }

            SetChineseName(entry.ChineseName);
            Density = entry.Density;
            ZRatio = entry.ZRatio;
        }

        private void SetChineseName(string value)
        {
            if (Set(Model.ChineseName, value, v => Model.ChineseName = v, nameof(ChineseName)))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        private bool SetName(string value)
        {
            if (!Set(Model.Name, value, v => Model.Name = v, nameof(Name)))
            {
                return false;
            }

            OnPropertiesChanged(nameof(DisplayName), nameof(DeviceStatus));
            return true;
        }

        private bool Set<T>(T current, T value, Action<T> assign, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(current, value))
            {
                return false;
            }

            assign(value);
            OnPropertyChanged(propertyName);
            _owner.MarkDirty();
            return true;
        }

        /// <summary>选项中没有当前值时 (设备上没有该对象,或坩埚超出数量) 在末尾加一项"n (不存在)",值不会丢失。</summary>
        private static IReadOnlyList<Choice<int>> WithCurrent(IReadOnlyList<Choice<int>> choices, int current)
        {
            if (choices.Any(c => c.Value == current))
            {
                return choices;
            }

            return choices.Concat(new[] { new Choice<int>(current, current.ToString(CultureInfo.InvariantCulture) + Lang.T(" (不存在)", " (missing)"), string.Empty) }).ToList();
        }
    }

    /// <summary>一段预熔或后处理。</summary>
    public sealed class ConditionStepViewModel : ViewModelBase
    {
        private readonly MaterialDesignViewModel _owner;
        private int _number;

        internal ConditionStepViewModel(ConditionStep model, MaterialDesignViewModel owner, bool isPre)
        {
            Model = model;
            _owner = owner;
            IsPre = isPre;
            RemoveCommand = new RelayCommand(_ => _owner.RemoveStep(this), _ => _owner.IsEditable);
        }

        /// <summary>数据。</summary>
        internal ConditionStep Model { get; }

        /// <summary>是否是预熔 (否则是后处理)。</summary>
        internal bool IsPre { get; }

        /// <summary>序号 (从 1 起)。</summary>
        public int Number
        {
            get => _number;
            internal set
            {
                if (SetProperty(ref _number, value))
                {
                    OnPropertyChanged(nameof(Title));
                }
            }
        }

        /// <summary>标题,如"预熔 2"、"后处理 1"。</summary>
        public string Title => (IsPre ? Lang.T("预熔 ", "Pre condition ") : Lang.T("后处理 ", "Post condition ")) + Number.ToString(CultureInfo.InvariantCulture);

        /// <summary>斜坡/上升时间,H:MM:SS。</summary>
        public string RampTime
        {
            get => Model.RampTime;
            set => Set(Model.RampTime, value, v => Model.RampTime = v);
        }

        /// <summary>斜坡功率,%。</summary>
        public string RampPower
        {
            get => Model.RampPower;
            set => Set(Model.RampPower, value, v => Model.RampPower = v);
        }

        /// <summary>保持时间,H:MM:SS。</summary>
        public string HoldTime
        {
            get => Model.HoldTime;
            set => Set(Model.HoldTime, value, v => Model.HoldTime = v);
        }

        /// <summary>删除这一段。</summary>
        public ICommand RemoveCommand { get; }

        private void Set(string current, string value, Action<string> assign, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            if (current == value)
            {
                return;
            }

            assign(value);
            OnPropertyChanged(propertyName);
            _owner.MarkDirty();
        }
    }
}
