using System;
using System.Windows;
using System.Windows.Input;

namespace TM.Framework.Common.Controls.DataManagement
{
    public partial class TwoColumnEditorLayout
    {
        private static void OnAutomationPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TwoColumnEditorLayout layout && layout._originalSaveCommand != null)
            {
                if (layout.AutoRefreshAfterSave)
                {
                    layout._wrappedSaveCommand = layout.WrapSaveCommand(layout._originalSaveCommand);
                    TM.App.Log("[TwoColumnEditorLayout] 自动化属性已更新，重新包装SaveCommand");
                }
                else
                {
                    layout._wrappedSaveCommand = null;
                }

                layout.UpdateInternalSaveCommand();
            }
        }

        public static readonly DependencyProperty RefreshCallbackProperty =
            DependencyProperty.Register(
                nameof(RefreshCallback),
                typeof(Action),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null));

        public Action RefreshCallback
        {
            get => (Action)GetValue(RefreshCallbackProperty);
            set => SetValue(RefreshCallbackProperty, value);
        }

        public event EventHandler? SaveCompleted;

        #region DataTreeView 配置

        public static readonly DependencyProperty ParentClickModeProperty =
            DependencyProperty.Register(
                nameof(ParentClickMode),
                typeof(ParentNodeClickMode),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(ParentNodeClickMode.Toggle));

        public ParentNodeClickMode ParentClickMode
        {
            get => (ParentNodeClickMode)GetValue(ParentClickModeProperty);
            set => SetValue(ParentClickModeProperty, value);
        }

        public static readonly DependencyProperty TreeLevel1HorizontalAlignmentProperty =
            DependencyProperty.Register(
                nameof(TreeLevel1HorizontalAlignment),
                typeof(HorizontalAlignment),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(HorizontalAlignment.Center));

        public HorizontalAlignment TreeLevel1HorizontalAlignment
        {
            get => (HorizontalAlignment)GetValue(TreeLevel1HorizontalAlignmentProperty);
            set => SetValue(TreeLevel1HorizontalAlignmentProperty, value);
        }

        public static readonly DependencyProperty ShowActionButtonsProperty =
            DependencyProperty.Register(
                nameof(ShowActionButtons),
                typeof(bool),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(true));

        public bool ShowActionButtons
        {
            get => (bool)GetValue(ShowActionButtonsProperty);
            set => SetValue(ShowActionButtonsProperty, value);
        }

        public static readonly DependencyProperty AIGenerateCommandProperty =
            DependencyProperty.Register(
                nameof(AIGenerateCommand),
                typeof(ICommand),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null));

        public ICommand? AIGenerateCommand
        {
            get => (ICommand?)GetValue(AIGenerateCommandProperty);
            set => SetValue(AIGenerateCommandProperty, value);
        }

        public static readonly DependencyProperty IsAIGenerateEnabledProperty =
            DependencyProperty.Register(
                nameof(IsAIGenerateEnabled),
                typeof(bool),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(false));

        public bool IsAIGenerateEnabled
        {
            get => (bool)GetValue(IsAIGenerateEnabledProperty);
            set => SetValue(IsAIGenerateEnabledProperty, value);
        }

        public static readonly DependencyProperty ShowAIGenerateButtonProperty =
            DependencyProperty.Register(
                nameof(ShowAIGenerateButton),
                typeof(bool),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(true));

        public bool ShowAIGenerateButton
        {
            get => (bool)GetValue(ShowAIGenerateButtonProperty);
            set => SetValue(ShowAIGenerateButtonProperty, value);
        }

        public static readonly DependencyProperty AIGenerateButtonTextProperty =
            DependencyProperty.Register(
                nameof(AIGenerateButtonText),
                typeof(string),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata("AI单次"));

        public string AIGenerateButtonText
        {
            get => (string)GetValue(AIGenerateButtonTextProperty);
            set => SetValue(AIGenerateButtonTextProperty, value);
        }

        private static void OnActionPermissionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TwoColumnEditorLayout layout)
            {
                layout.UpdateActionPermissionStates();
            }
        }

        public static readonly DependencyProperty EnableCategoryActionsProperty =
            DependencyProperty.Register(
                nameof(EnableCategoryActions),
                typeof(bool),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(true, OnActionPermissionChanged));

        public bool EnableCategoryActions
        {
            get => (bool)GetValue(EnableCategoryActionsProperty);
            set => SetValue(EnableCategoryActionsProperty, value);
        }

        public static readonly DependencyProperty EnableContentActionsProperty =
            DependencyProperty.Register(
                nameof(EnableContentActions),
                typeof(bool),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(true, OnActionPermissionChanged));

        public bool EnableContentActions
        {
            get => (bool)GetValue(EnableContentActionsProperty);
            set => SetValue(EnableContentActionsProperty, value);
        }

        public static readonly DependencyProperty IsAddActionEnabledProperty =
            DependencyProperty.Register(
                nameof(IsAddActionEnabled),
                typeof(bool),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(false));

        public bool IsAddActionEnabled
        {
            get => (bool)GetValue(IsAddActionEnabledProperty);
            private set => SetValue(IsAddActionEnabledProperty, value);
        }

        public static readonly DependencyProperty IsDeleteActionEnabledProperty =
            DependencyProperty.Register(
                nameof(IsDeleteActionEnabled),
                typeof(bool),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(false));

        public bool IsDeleteActionEnabled
        {
            get => (bool)GetValue(IsDeleteActionEnabledProperty);
            private set => SetValue(IsDeleteActionEnabledProperty, value);
        }

        public static readonly DependencyProperty CategoryActionDisabledMessageProperty =
            DependencyProperty.Register(
                nameof(CategoryActionDisabledMessage),
                typeof(string),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata("分类由分类配置中心维护"));

        public string CategoryActionDisabledMessage
        {
            get => (string)GetValue(CategoryActionDisabledMessageProperty);
            set => SetValue(CategoryActionDisabledMessageProperty, value);
        }

        public static readonly DependencyProperty MaxLevelProperty =
            DependencyProperty.Register(
                nameof(MaxLevel),
                typeof(int),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(5));

        public int MaxLevel
        {
            get => (int)GetValue(MaxLevelProperty);
            set => SetValue(MaxLevelProperty, value);
        }

        #endregion

        #region 命令绑定

        public static readonly DependencyProperty NodeDoubleClickCommandProperty =
            DependencyProperty.Register(
                nameof(NodeDoubleClickCommand),
                typeof(ICommand),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null));

        public ICommand NodeDoubleClickCommand
        {
            get => (ICommand)GetValue(NodeDoubleClickCommandProperty);
            set => SetValue(NodeDoubleClickCommandProperty, value);
        }

        public static readonly DependencyProperty TreeAfterActionCommandProperty =
            DependencyProperty.Register(
                nameof(TreeAfterActionCommand),
                typeof(ICommand),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null));

        public ICommand TreeAfterActionCommand
        {
            get => (ICommand)GetValue(TreeAfterActionCommandProperty);
            set => SetValue(TreeAfterActionCommandProperty, value);
        }

        public static readonly DependencyProperty AddCommandProperty =
            DependencyProperty.Register(
                nameof(AddCommand),
                typeof(ICommand),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null));

        public ICommand AddCommand
        {
            get => (ICommand)GetValue(AddCommandProperty);
            set => SetValue(AddCommandProperty, value);
        }

        public static readonly DependencyProperty SaveCommandProperty =
            DependencyProperty.Register(
                nameof(SaveCommand),
                typeof(ICommand),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null, OnSaveCommandChanged));

        public ICommand SaveCommand
        {
            get => (ICommand)GetValue(SaveCommandProperty);
            set => SetValue(SaveCommandProperty, value);
        }

        private static void OnSaveCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TwoColumnEditorLayout layout)
            {
                var newCommand = e.NewValue as ICommand;

                if (newCommand != null && layout.AutoRefreshAfterSave)
                {
                    layout._originalSaveCommand = newCommand;
                    layout._wrappedSaveCommand = layout.WrapSaveCommand(newCommand);

                    TM.App.Log("[TwoColumnEditorLayout] SaveCommand已包装，启用自动化功能");
                }
                else
                {
                    layout._originalSaveCommand = newCommand;
                    layout._wrappedSaveCommand = null;
                }

                layout.UpdateInternalSaveCommand();
            }
        }

        public ICommand GetEffectiveSaveCommand()
        {
            return _wrappedSaveCommand ?? SaveCommand;
        }

        public static readonly DependencyProperty InternalSaveCommandProperty =
            DependencyProperty.Register(
                nameof(InternalSaveCommand),
                typeof(ICommand),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null));

        public ICommand InternalSaveCommand
        {
            get => (ICommand)GetValue(InternalSaveCommandProperty);
            private set => SetValue(InternalSaveCommandProperty, value);
        }

        private void UpdateInternalSaveCommand()
        {
            InternalSaveCommand = _wrappedSaveCommand ?? SaveCommand;
        }

        public static readonly DependencyProperty DeleteCommandProperty =
            DependencyProperty.Register(
                nameof(DeleteCommand),
                typeof(ICommand),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null));

        public ICommand DeleteCommand
        {
            get => (ICommand)GetValue(DeleteCommandProperty);
            set => SetValue(DeleteCommandProperty, value);
        }

        public static readonly DependencyProperty DeleteAllCommandProperty =
            DependencyProperty.Register(
                nameof(DeleteAllCommand),
                typeof(ICommand),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null));

        public ICommand DeleteAllCommand
        {
            get => (ICommand)GetValue(DeleteAllCommandProperty);
            set => SetValue(DeleteAllCommandProperty, value);
        }

        #endregion
    }
}

