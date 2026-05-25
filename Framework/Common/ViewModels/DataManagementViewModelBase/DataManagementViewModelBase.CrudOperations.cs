using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TM.Services.Framework.Settings;

namespace TM.Framework.Common.ViewModels
{
    public abstract partial class DataManagementViewModelBase<TData, TCategory, TService>
    {

        protected virtual string NewItemTypeName => string.Empty;

        protected void ExecuteAddWithCreateMode()
        {
            EnterCreateMode();

            if (!string.IsNullOrEmpty(NewItemTypeName))
            {
                var typeName = NewItemTypeName;
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                dispatcher?.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ContextIdle,
                    new Action(() => GlobalToast.Info("新建", $"选择「主页导航」创建分类，选择具体分类创建{typeName}")));
            }
        }

        protected bool ExecuteSaveWithCreateEditMode(
            Func<bool> validateForm,
            Action createCategoryCore,
            Action createDataCore,
            Func<bool> hasEditingCategory,
            Func<bool> hasEditingData,
            Action updateCategoryCore,
            Action updateDataCore)
        {
            ArgumentNullException.ThrowIfNull(validateForm);
            ArgumentNullException.ThrowIfNull(createCategoryCore);
            ArgumentNullException.ThrowIfNull(createDataCore);
            ArgumentNullException.ThrowIfNull(hasEditingCategory);
            ArgumentNullException.ThrowIfNull(hasEditingData);
            ArgumentNullException.ThrowIfNull(updateCategoryCore);
            ArgumentNullException.ThrowIfNull(updateDataCore);

            if (!IsCreateMode && hasEditingCategory() && _currentEditingCategory?.IsBuiltIn == true)
            {
                EndBusinessSessionAndResetBatchNames();
                GlobalToast.Success("批量保存成功", $"分类『{_currentEditingCategory.Name}』下的批量数据已全部保存");
                return true;
            }

            bool isDataSaveIntent = hasEditingData()
                                    || (IsCreateMode && string.Equals(FormType, "数据", StringComparison.Ordinal));
            if (isDataSaveIntent
                && HasCoherenceHardConflict
                && string.Equals(_coherenceConflictScopeId, GetCurrentCoherenceScopeId(), StringComparison.Ordinal))
            {
                GlobalToast.Error("保存已阻止", CoherenceConflictMessage);
                return false;
            }

            if (!validateForm())
            {
                return false;
            }

            if (!ConfirmSaveWillEndAISessionIfNeeded())
            {
                return false;
            }

            var forceCreateData = !hasEditingData()
                                  && !hasEditingCategory()
                                  && string.Equals(FormType, "数据", StringComparison.Ordinal);

            if (IsCreateMode || forceCreateData)
            {
                bool isCreatingCategory = !string.Equals(FormType, "数据", StringComparison.Ordinal);

                if (isCreatingCategory)
                {
                    if (GetAllCategoriesFromService().Count >= GetMaxCategoryCount())
                    {
                        GlobalToast.Warning("创建受限", GetCategoryLimitMessage());
                        return false;
                    }
                    createCategoryCore();
                }
                else
                {
                    var currentCategory = GetCurrentCategoryValue() ?? string.Empty;
                    var count = GetAllDataItems().Count(d => string.Equals(GetDataCategory(d), currentCategory, StringComparison.Ordinal));
                    if (count >= GetMaxDataCountPerCategory())
                    {
                        GlobalToast.Warning("创建受限", GetDataLimitMessage());
                        return false;
                    }
                    createDataCore();
                    ApplyPendingDependencyVersions(GetCurrentEditingDataObject());
                }

                IsCreateMode = false;
                OnPropertyChanged(nameof(IsCreateMode));
                RefreshTreeData();
                EndBusinessSessionAndResetBatchNames();
                return true;
            }

            if (hasEditingCategory())
            {
                updateCategoryCore();
                EndBusinessSessionAndResetBatchNames();
                return true;
            }

            if (hasEditingData())
            {
                updateDataCore();
                EndBusinessSessionAndResetBatchNames();
                return true;
            }

            GlobalToast.Warning("保存失败", "请先点击\"新建\"，或在左侧选择要编辑的分类或数据");
            return false;
        }

        private bool ConfirmSaveWillEndAISessionIfNeeded()
        {
            try
            {
                if (!_aiService.HasDirtyBusinessSessionsByPrefix(GetType().Name))
                {
                    return true;
                }

                if (AINavigationSessionSaveConfirmState.SuppressThisRun)
                {
                    return true;
                }

                var settings = ServiceLocator.Get<SettingsManager>();
                if (settings.Get(SuppressSaveEndAISessionConfirmSettingKey, false))
                {
                    return true;
                }

                var dialog = new StandardDialog();
                StandardDialog.EnsureOwnerAndTopmost(dialog, null);
                dialog.SetTitle("保存确认");
                dialog.SetIcon("Icon.Warning");

                var fg = dialog.TryFindResource("TextPrimary") as System.Windows.Media.Brush;
                var panel = new StackPanel
                {
                    Margin = new Thickness(0)
                };

                panel.Children.Add(new TextBlock
                {
                    Text = "检测到当前业务存在未保存的AI对话上下文。\n\n保存将结束本次上下文，后续生成的连贯性可能下降。\n如果你希望继续保持连贯生成，可先继续调用AI生成，而不是保存。\n\n是否仍要继续保存？",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14,
                    MaxWidth = 520,
                    Foreground = fg
                });

                var cbThisRun = new CheckBox
                {
                    Content = "本次不再提示（重启后恢复）",
                    Margin = new Thickness(0, 12, 0, 0),
                    Foreground = fg
                };
                panel.Children.Add(cbThisRun);

                var cbRemember = new CheckBox
                {
                    Content = "记住选择（下次启动也不再提示）",
                    Margin = new Thickness(0, 6, 0, 0),
                    Foreground = fg
                };
                panel.Children.Add(cbRemember);

                dialog.SetContent(new ScrollViewer
                {
                    Content = panel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    MaxHeight = 360
                });

                var confirmed = false;
                dialog.AddButton("取消", () => { confirmed = false; dialog.Close(); });
                dialog.AddButton("继续保存", () => { confirmed = true; dialog.Close(); }, true);
                dialog.ShowDialog();

                if (!confirmed)
                {
                    return false;
                }

                var remember = cbRemember.IsChecked == true;
                var suppressThisRun = cbThisRun.IsChecked == true;
                if (remember)
                {
                    settings.Set(SuppressSaveEndAISessionConfirmSettingKey, true);
                }

                if (remember || suppressThisRun)
                {
                    AINavigationSessionSaveConfirmState.SuppressThisRun = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 保存确认弹窗异常: {ex.Message}");
                return true;
            }
        }

        protected async System.Threading.Tasks.Task<bool> ExecuteSaveWithCreateEditModeAsync(
            Func<bool> validateForm,
            Func<System.Threading.Tasks.Task> createCategoryCore,
            Func<System.Threading.Tasks.Task> createDataCore,
            Func<bool> hasEditingCategory,
            Func<bool> hasEditingData,
            Func<System.Threading.Tasks.Task> updateCategoryCore,
            Func<System.Threading.Tasks.Task> updateDataCore)
        {
            ArgumentNullException.ThrowIfNull(validateForm);
            ArgumentNullException.ThrowIfNull(createCategoryCore);
            ArgumentNullException.ThrowIfNull(createDataCore);
            ArgumentNullException.ThrowIfNull(hasEditingCategory);
            ArgumentNullException.ThrowIfNull(hasEditingData);
            ArgumentNullException.ThrowIfNull(updateCategoryCore);
            ArgumentNullException.ThrowIfNull(updateDataCore);

            if (!IsCreateMode && hasEditingCategory() && _currentEditingCategory?.IsBuiltIn == true)
            {
                EndBusinessSessionAndResetBatchNames();
                GlobalToast.Success("批量保存成功", $"分类『{_currentEditingCategory.Name}』下的批量数据已全部保存");
                return true;
            }

            bool isDataSaveIntent = hasEditingData()
                                    || (IsCreateMode && string.Equals(FormType, "数据", StringComparison.Ordinal));
            if (isDataSaveIntent
                && HasCoherenceHardConflict
                && string.Equals(_coherenceConflictScopeId, GetCurrentCoherenceScopeId(), StringComparison.Ordinal))
            {
                GlobalToast.Error("保存已阻止", CoherenceConflictMessage);
                return false;
            }

            if (!validateForm())
            {
                return false;
            }

            if (!ConfirmSaveWillEndAISessionIfNeeded())
            {
                return false;
            }

            var forceCreateData = !hasEditingData()
                                  && !hasEditingCategory()
                                  && string.Equals(FormType, "数据", StringComparison.Ordinal);

            if (IsCreateMode || forceCreateData)
            {
                bool isCreatingCategory = !string.Equals(FormType, "数据", StringComparison.Ordinal);

                if (isCreatingCategory)
                {
                    if (GetAllCategoriesFromService().Count >= GetMaxCategoryCount())
                    {
                        GlobalToast.Warning("创建受限", GetCategoryLimitMessage());
                        return false;
                    }
                    await createCategoryCore();
                }
                else
                {
                    var currentCategory = GetCurrentCategoryValue() ?? string.Empty;
                    var count = GetAllDataItems().Count(d => string.Equals(GetDataCategory(d), currentCategory, StringComparison.Ordinal));
                    if (count >= GetMaxDataCountPerCategory())
                    {
                        GlobalToast.Warning("创建受限", GetDataLimitMessage());
                        return false;
                    }
                    await ResolveEntityReferencesBeforeSaveAsync();
                    await createDataCore();
                    ApplyPendingDependencyVersions(GetCurrentEditingDataObject());
                }

                IsCreateMode = false;
                OnPropertyChanged(nameof(IsCreateMode));
                RefreshTreeData();
                EndBusinessSessionAndResetBatchNames();
                return true;
            }

            if (hasEditingCategory())
            {
                await updateCategoryCore();
                EndBusinessSessionAndResetBatchNames();
                return true;
            }

            if (hasEditingData())
            {
                await ResolveEntityReferencesBeforeSaveAsync();
                await updateDataCore();
                EndBusinessSessionAndResetBatchNames();
                return true;
            }

            GlobalToast.Warning("保存失败", "请先点击\"新建\"，或在左侧选择要编辑的分类或数据");
            return false;
        }
    }
}

