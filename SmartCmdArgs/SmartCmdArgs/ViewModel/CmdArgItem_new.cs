﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using SmartCmdArgs.Helper;

namespace SmartCmdArgs.ViewModel
{
    public class CmdBase : PropertyChangedBase
    {
        public Guid Id { get; set; }

        private CmdContainer parent;
        public CmdContainer Parent { get => parent; set => SetAndNotify(value, ref this.parent); }

        private string value;
        public string Value { get => value; set => SetAndNotify(value, ref this.value); }

        protected bool? isChecked;
        public bool? IsChecked { get => isChecked; set => OnIsCheckedChanged(isChecked, value, true); }

        protected bool isSelected;
        public bool IsSelected { get => isSelected; set => SetAndNotify(value, ref isSelected); }

        public virtual bool IsEditable => false;

        public CmdBase(Guid id, string value, bool? isChecked)
        {
            Id = id;
            this.value = value;
            this.isChecked = isChecked;
        }

        public CmdBase(string value, bool? isChecked) 
            : this(Guid.NewGuid(), value, isChecked)
        {}

        public void ToggleCheckedState()
        {
            if (IsChecked == null)
                IsChecked = false;
            else
                IsChecked = !IsChecked;
        }

        protected virtual void OnIsCheckedChanged(bool? oldValue, bool? newValue, bool notifyParent)
        {
            SetAndNotify(newValue, ref this.isChecked, nameof(IsChecked));

            if (notifyParent)
            {
                parent?.OnChildIsCheckedChanged(oldValue, newValue);
            }
        }

        protected virtual void OnChildIsCheckedChanged(bool? oldValue, bool? newValue)
        {}

        public void SetIsCheckedWithoutNotifyingParent(bool? value)
        {
            OnIsCheckedChanged(IsChecked, value, false);
        }

        private string editBackupValue;

        private bool isInEditMode;
        public bool IsInEditMode { get => isInEditMode; private set => SetAndNotify(value, ref this.isInEditMode); }

        public event EventHandler<EditMode> EditModeChanged;
        public enum EditMode
        {
            BeganEdit, BeganEditAndReset, CanceledEdit, CommitedEdit
        }

        public void BeginEdit(string initialValue = null)
        {
            ThrowIfNotEditable();

            if (!IsInEditMode)
            {
                editBackupValue = Value;
                if (initialValue != null) Value = initialValue;
                IsInEditMode = true;
                EditModeChanged?.Invoke(this, initialValue != null ? EditMode.BeganEditAndReset : EditMode.BeganEdit);
            }
        }

        public void CancelEdit()
        {
            ThrowIfNotEditable();

            if (IsInEditMode)
            {
                Value = editBackupValue;
                editBackupValue = null;
                IsInEditMode = false;
                EditModeChanged?.Invoke(this, EditMode.CanceledEdit);
            }
        }

        public void CommitEdit()
        {
            ThrowIfNotEditable();

            if (IsInEditMode)
            {
                editBackupValue = null;
                IsInEditMode = false;
                EditModeChanged?.Invoke(this, EditMode.CommitedEdit);
            }
        }

        private void ThrowIfNotEditable()
        {
            if (!IsEditable)
                throw new InvalidOperationException("Can't execute edit operation on a not editable item!");
        }
    }
    
    public class CmdContainer : CmdBase
    {
        public ObservableCollectionEx<CmdBase> Items { get; }

        public IEnumerable<CmdArgument> AllArguments => Items.Where(item => item is CmdArgument)
            .Concat(Items.Where(item => item is CmdContainer).Cast<CmdContainer>().SelectMany(container => container.AllArguments)).Cast<CmdArgument>();

        public IEnumerable<CmdBase> SelectedItems =>
            Items.Where(item => item is CmdArgument && item.IsSelected)
                .Concat(Items.Where(item => item is CmdContainer).Cast<CmdContainer>().SelectMany(container => container.SelectedItems));

        public IEnumerable<CmdArgument> SelectedArguments =>
            Items.Where(item => item is CmdArgument).Cast<CmdArgument>().Where(arg => arg.IsSelected)
                .Concat(Items.Where(item => item is CmdContainer).Cast<CmdContainer>().SelectMany(container => container.SelectedArguments));

        public IEnumerable<CmdArgument> CheckedArguments =>
            Items.Where(item => item is CmdArgument).Cast<CmdArgument>().Where(arg => arg.IsChecked == true)
                .Concat(Items.Where(item => item is CmdContainer).Cast<CmdContainer>().SelectMany(container => container.CheckedArguments));

        public CmdContainer(Guid id, string value, bool? isChecked, IEnumerable<CmdBase> items = null)
            : base(id, value, isChecked)
        {
            Items = new ObservableCollectionEx<CmdBase>();

            Items.CollectionChanged += ItemsOnCollectionChanged;

            foreach (var item in items ?? Enumerable.Empty<CmdBase>())
            {
                Items.Add(item);
            }
        }
        
        public CmdContainer(string value, bool? isChecked, IEnumerable<CmdBase> items = null) 
            : this(Guid.NewGuid(), value, isChecked, items)
        { }

        private void ItemsOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var item in Items)
                {
                    item.Parent = this;
                }
            }
            else
            {
                if (e.Action == NotifyCollectionChangedAction.Remove || e.Action == NotifyCollectionChangedAction.Replace)
                {
                    foreach (var item in e.OldItems.Cast<CmdBase>())
                    {
                        item.Parent = null;
                    }
                }

                if (e.Action == NotifyCollectionChangedAction.Add || e.Action == NotifyCollectionChangedAction.Replace)
                {
                    foreach (var item in e.NewItems.Cast<CmdBase>())
                    {
                        item.Parent = this;
                    }
                }
            }

            if (e.Action != NotifyCollectionChangedAction.Move)
            {
                if (Items.Count == 0)
                    base.OnIsCheckedChanged(IsChecked, false, true);
                else if (Items.All(item => item.IsChecked ?? false))
                    base.OnIsCheckedChanged(IsChecked, true, true);
                else if (Items.All(item => !item.IsChecked ?? false))
                    base.OnIsCheckedChanged(IsChecked, false, true);
                else
                    base.OnIsCheckedChanged(IsChecked, null, true);
            }
        }

        protected override void OnIsCheckedChanged(bool? oldValue, bool? newValue, bool notifyParent)
        {
            base.OnIsCheckedChanged(oldValue, newValue, notifyParent);
            foreach (var item in Items)
            {
                item.SetIsCheckedWithoutNotifyingParent(newValue);
            }
        }

        protected override void OnChildIsCheckedChanged(bool? oldValue, bool? newValue)
        {
            if (newValue == true)
            {
                if (Items.All(item => item.IsChecked ?? false))
                    base.OnIsCheckedChanged(IsChecked, true, true);
                else
                    base.OnIsCheckedChanged(IsChecked, null, true);
            }
            else
            {
                if (Items.Any(item => item.IsChecked ?? true))
                    base.OnIsCheckedChanged(IsChecked, null, true);
                else
                    base.OnIsCheckedChanged(IsChecked, false, true);
            }
        }


        /// <summary>
        /// Sets the IsSelected property for every child to areSelected.
        /// </summary>
        /// <returns>True if any child changed its state.</returns>
        public bool SetIsSelectedOnChildren(bool areSelected)
        {
            bool result = false;
            foreach (var item in Items)
            {
                if (item.IsSelected != areSelected)
                {
                    item.IsSelected = areSelected;
                    result = true;
                }
                
                if (item is CmdContainer container)
                {
                    if (container.SetIsSelectedOnChildren(areSelected))
                        result = true;
                }
            }

            return result;
        }

        public CmdArgument AddNewArgument(string command, bool enabled = true)
        {
            var item = new CmdArgument(command, enabled);
            Items.Add(item);
            return item;
        }

        public CmdGroup AddNewGroup(string command)
        {
            var group = new CmdGroup(command);
            Items.Add(group);
            return group;
        }
    }

    public class CmdProject : CmdContainer
    {
        private bool isStartupProject = false;
        public bool IsStartupProject { get => isStartupProject; set => SetAndNotify(value, ref isStartupProject); }

        public bool isFocusedProject = false;
        public bool IsFocusedProject { get => isFocusedProject; set => SetAndNotify(value, ref isFocusedProject); }

        public CmdProject(Guid id, string value, bool? isChecked = false, IEnumerable<CmdBase> items = null)
            : base(id, value, isChecked, items)
        { }
        
        public CmdProject(string value, bool? isChecked = false, IEnumerable<CmdBase> items = null) 
            : this(Guid.NewGuid(), value, isChecked, items)
        {  }
    }

    public class CmdGroup : CmdContainer
    { 
        public override bool IsEditable => true;

        public CmdGroup(Guid id, string value, bool? isChecked = false, IEnumerable<CmdBase> items = null)
            : base(id, value, isChecked, items)
        { }
        
        public CmdGroup(string value, bool? isChecked = false, IEnumerable<CmdBase> items = null) 
            : this(Guid.NewGuid(), value, isChecked, items)
        { }
    }

    public class CmdArgument : CmdBase
    {
        public override bool IsEditable => true;

        public CmdArgument(Guid id, string value, bool? isChecked = false) 
            : base(id, value, isChecked)
        { }
        
        public CmdArgument(string value, bool? isChecked = false) 
            : this(Guid.NewGuid(), value, isChecked)
        { }
    }
}