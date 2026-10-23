using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Zevision.Core.Design;
using Zevision.Core.Localization;
using Zevision.ViewModels.Common;

namespace Zevision.ViewModels.Design
{
    /// <summary>
    /// 工艺编辑中的一个工艺:名称、自动启动下一膜层、按顺序的步骤。修改直接写入 <see cref="ProcessDesign"/>。
    /// </summary>
    public sealed class ProcessDesignViewModel : ViewModelBase
    {
        private readonly ProcessDesignerViewModel _owner;

        internal ProcessDesignViewModel(ProcessDesign model, ProcessDesignerViewModel owner)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Steps = new ObservableCollection<ProcessStepViewModel>(model.Steps.Select(s => new ProcessStepViewModel(s, this)));
            Renumber();
            AddStepCommand = new RelayCommand(_ => AddStep(), _ => _owner.IsEditable);
        }

        /// <summary>数据。</summary>
        internal ProcessDesign Model { get; }

        /// <summary>窗口。</summary>
        internal ProcessDesignerViewModel Owner => _owner;

        /// <summary>列表中显示的名称 (没有名称时为"(未命名)")。</summary>
        public string DisplayName => DesignRules.DisplayName(Model.Name);

        /// <summary>名称 (<c>P02**01</c>)。</summary>
        public string Name
        {
            get => Model.Name;
            set
            {
                if (Model.Name == value)
                {
                    return;
                }

                Model.Name = value;
                OnPropertiesChanged(nameof(Name), nameof(DisplayName), nameof(DeviceStatus));
                _owner.MarkDirty();
            }
        }

        /// <summary>设备上是否已有同名的工艺,如"设备上: P0209"、"设备上没有" (列表中名称下面的一行)。</summary>
        public string DeviceStatus => _owner.DeviceStatus(Model);

        /// <summary>自动启动下一膜层。</summary>
        public bool AutoStartNext
        {
            get => Model.AutoStartNext;
            set
            {
                if (Model.AutoStartNext == value)
                {
                    return;
                }

                Model.AutoStartNext = value;
                OnPropertyChanged();
                _owner.MarkDirty();
            }
        }

        /// <summary>上次上传的记录;没有上传过时为空。</summary>
        public string LastUpload => Model.LastUpload;

        /// <summary>是否上传过 (显示上次上传的记录)。</summary>
        public bool HasLastUpload => Model.LastUpload.Length > 0;

        /// <summary>步骤。</summary>
        public ObservableCollection<ProcessStepViewModel> Steps { get; }

        /// <summary>添加一步 (材料默认为第一种材料)。</summary>
        public ICommand AddStepCommand { get; }

        /// <summary>窗口是否可以编辑。</summary>
        public bool IsEditable => _owner.IsEditable;

        /// <summary>记录一次成功的上传。</summary>
        internal void SetLastUpload(string record)
        {
            Model.LastUpload = record;
            OnPropertiesChanged(nameof(LastUpload), nameof(HasLastUpload));
        }

        internal void RemoveStep(ProcessStepViewModel step)
        {
            if (Steps.Remove(step))
            {
                Model.Steps.Remove(step.Model);
                Renumber();
                _owner.MarkDirty();
            }
        }

        /// <summary>把一步上移 (<paramref name="offset"/> = -1) 或下移 (1)。</summary>
        internal void MoveStep(ProcessStepViewModel step, int offset)
        {
            var index = Steps.IndexOf(step);
            var target = index + offset;
            if (index < 0 || target < 0 || target >= Steps.Count)
            {
                return;
            }

            Steps.Move(index, target);
            Model.Steps.RemoveAt(index);
            Model.Steps.Insert(target, step.Model);
            Renumber();
            _owner.MarkDirty();
        }

        internal bool CanMove(ProcessStepViewModel step, int offset)
        {
            var target = Steps.IndexOf(step) + offset;
            return _owner.IsEditable && target >= 0 && target < Steps.Count;
        }

        /// <summary>材料列表或窗口的可编辑状态改变。</summary>
        internal void Refresh()
        {
            OnPropertyChanged(nameof(IsEditable));
            foreach (var step in Steps)
            {
                step.Refresh();
            }
        }

        internal void MarkDirty() => _owner.MarkDirty();

        /// <summary>设备上的内容改变 (读取、上传、删除后)。</summary>
        internal void RefreshDeviceStatus() => OnPropertyChanged(nameof(DeviceStatus));

        private void AddStep()
        {
            var model = new ProcessStepDesign { MaterialId = _owner.Materials.FirstOrDefault()?.Id ?? string.Empty };
            Model.Steps.Add(model);
            Steps.Add(new ProcessStepViewModel(model, this));
            Renumber();
            _owner.MarkDirty();
        }

        private void Renumber()
        {
            for (var i = 0; i < Steps.Count; i++)
            {
                Steps[i].Number = i + 1;
            }
        }
    }

    /// <summary>
    /// 工艺的一步 (设备上的一个膜层):材料、速率、最终厚度和速率斜坡。速率和最终厚度的问题 (如超过三位小数) 通过
    /// <see cref="IDataErrorInfo"/> 显示在输入框下。
    /// </summary>
    public sealed class ProcessStepViewModel : ViewModelBase, IDataErrorInfo
    {
        private readonly ProcessDesignViewModel _owner;
        private int _number;

        internal ProcessStepViewModel(ProcessStepDesign model, ProcessDesignViewModel owner)
        {
            Model = model;
            _owner = owner;
            RateRamps = new ObservableCollection<RateRampViewModel>(model.RateRamps.Select(r => new RateRampViewModel(r, this)));
            RenumberRamps();
            RemoveCommand = new RelayCommand(_ => _owner.RemoveStep(this), _ => _owner.IsEditable);
            MoveUpCommand = new RelayCommand(_ => _owner.MoveStep(this, -1), _ => _owner.CanMove(this, -1));
            MoveDownCommand = new RelayCommand(_ => _owner.MoveStep(this, 1), _ => _owner.CanMove(this, 1));
            AddRampCommand = new RelayCommand(_ => AddRamp(), _ => _owner.IsEditable);
        }

        /// <summary>数据。</summary>
        internal ProcessStepDesign Model { get; }

        /// <summary>序号 (从 1 起)。</summary>
        public int Number
        {
            get => _number;
            internal set => SetProperty(ref _number, value);
        }

        /// <summary>材料的选项 (窗口中的全部材料)。</summary>
        public ObservableCollection<MaterialDesignViewModel> Materials => _owner.Owner.Materials;

        /// <summary>材料;找不到 (被删除) 时为 null。</summary>
        public MaterialDesignViewModel? Material
        {
            get => _owner.Owner.Materials.FirstOrDefault(m => m.Id == Model.MaterialId);
            set
            {
                // 下拉框解除绑定时可能写回 null:不当作修改。
                if (value == null || value.Id == Model.MaterialId)
                {
                    return;
                }

                Model.MaterialId = value.Id;
                OnPropertyChanged();
                _owner.MarkDirty();
            }
        }

        /// <summary>速率,Å/s。</summary>
        public string Rate
        {
            get => Model.Rate;
            set => Set(Model.Rate, value, v => Model.Rate = v);
        }

        /// <summary>最终厚度,kÅ。</summary>
        public string Thickness
        {
            get => Model.Thickness;
            set => Set(Model.Thickness, value, v => Model.Thickness = v);
        }

        /// <summary>速率的问题 (数值、范围,最多三位小数);合法或为空时为 null。</summary>
        public string? RateError => DesignRules.CheckStepValue(StepField.Rate, Rate);

        /// <summary>最终厚度的问题;合法或为空时为 null。</summary>
        public string? ThicknessError => DesignRules.CheckStepValue(StepField.Thickness, Thickness);

        /// <inheritdoc />
        string IDataErrorInfo.Error => string.Empty;

        /// <inheritdoc />
        string IDataErrorInfo.this[string columnName] => columnName switch
        {
            nameof(Rate) => RateError,
            nameof(Thickness) => ThicknessError,
            _ => null,
        } ?? string.Empty;

        /// <summary>速率斜坡。</summary>
        public ObservableCollection<RateRampViewModel> RateRamps { get; }

        /// <summary>删除这一步。</summary>
        public ICommand RemoveCommand { get; }

        /// <summary>上移。</summary>
        public ICommand MoveUpCommand { get; }

        /// <summary>下移。</summary>
        public ICommand MoveDownCommand { get; }

        /// <summary>添加一个速率斜坡。</summary>
        public ICommand AddRampCommand { get; }

        internal bool IsEditable => _owner.IsEditable;

        internal void RemoveRamp(RateRampViewModel ramp)
        {
            if (RateRamps.Remove(ramp))
            {
                Model.RateRamps.Remove(ramp.Model);
                RenumberRamps();
                _owner.MarkDirty();
            }
        }

        /// <summary>材料改名或被删除后刷新显示。</summary>
        internal void Refresh() => OnPropertyChanged(nameof(Material));

        internal void MarkDirty() => _owner.MarkDirty();

        private void AddRamp()
        {
            var model = new RateRampDesign();
            Model.RateRamps.Add(model);
            RateRamps.Add(new RateRampViewModel(model, this));
            RenumberRamps();
            _owner.MarkDirty();
        }

        private void RenumberRamps()
        {
            for (var i = 0; i < RateRamps.Count; i++)
            {
                RateRamps[i].Number = i + 1;
            }
        }

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

    /// <summary>一个速率斜坡:起始厚度、斜坡时间、新速率。各值的问题通过 <see cref="IDataErrorInfo"/> 显示在输入框下。</summary>
    public sealed class RateRampViewModel : ViewModelBase, IDataErrorInfo
    {
        private readonly ProcessStepViewModel _owner;
        private int _number;

        internal RateRampViewModel(RateRampDesign model, ProcessStepViewModel owner)
        {
            Model = model;
            _owner = owner;
            RemoveCommand = new RelayCommand(_ => _owner.RemoveRamp(this), _ => _owner.IsEditable);
        }

        /// <summary>数据。</summary>
        internal RateRampDesign Model { get; }

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

        /// <summary>标题,如"速率斜坡 1"。</summary>
        public string Title => Lang.T("速率斜坡 ", "Rate ramp ") + Number.ToString(CultureInfo.InvariantCulture);

        /// <summary>起始厚度,kÅ。</summary>
        public string StartThickness
        {
            get => Model.StartThickness;
            set => Set(Model.StartThickness, value, v => Model.StartThickness = v);
        }

        /// <summary>斜坡时间,H:MM:SS。</summary>
        public string RampTime
        {
            get => Model.RampTime;
            set => Set(Model.RampTime, value, v => Model.RampTime = v);
        }

        /// <summary>新速率,Å/s。</summary>
        public string NewRate
        {
            get => Model.NewRate;
            set => Set(Model.NewRate, value, v => Model.NewRate = v);
        }

        /// <inheritdoc />
        string IDataErrorInfo.Error => string.Empty;

        /// <summary>起始厚度、斜坡时间或新速率的问题 (起始厚度和新速率最多三位小数);合法或为空时为空字符串。</summary>
        string IDataErrorInfo.this[string columnName] => columnName switch
        {
            nameof(StartThickness) => DesignRules.CheckStepValue(StepField.RampStartThickness, StartThickness),
            nameof(RampTime) => DesignRules.CheckStepValue(StepField.RampTime, RampTime),
            nameof(NewRate) => DesignRules.CheckStepValue(StepField.RampNewRate, NewRate),
            _ => null,
        } ?? string.Empty;

        /// <summary>删除这个斜坡。</summary>
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
