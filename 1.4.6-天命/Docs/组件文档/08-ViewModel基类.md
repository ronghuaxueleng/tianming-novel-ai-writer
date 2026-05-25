# ViewModel基类体系

**最后更新**: 2025-11-07  
**类型**: 标准化ViewModel基类（完整的业务逻辑封装）  
**命名空间**: `TM.Framework.Common.ViewModels`

---

## 📑 基类体系导航

本文档包含天命项目的**三大ViewModel基类**（按推荐优先级排序）：

| 基类名称 | 版本 | 适用场景 | 代码量 | 代码减少 |
|---------|------|---------|---------|---------||
| [DataManagementViewModelBase](#1-datamanagementviewmodelbase---数据管理视图模型基类) | v1.0 ⭐ | 分类+数据管理 | **10-15行** | **70%** |
| [SinglePageViewModelBase](#2-singlepageviewmodelbase---单页功能视图模型基类) | v1.0 ⭐ | 单页功能界面 | **按需** | **50%** |
| [TreeDataViewModelBase](#3-treedataviewmodelbase---树形数据视图模型基类) | v3.0 | 复杂自定义场景（内部基类） | 30-50行 | 60% |

---

## 1. DataManagementViewModelBase - 数据管理视图模型基类

**版本**: v1.0 ⭐ **新增**  
**更新日期**: 2025-11-04  
**适用场景**: 分类+数据管理（如知识块管理、角色管理等）  
**继承关系**: `DataManagementViewModelBase<TData, TCategory, TService>` → `TreeDataViewModelBase<TData, TCategory>`

---

### 📖 概述

`DataManagementViewModelBase` 是为**分类+数据管理**场景设计的标准化基类，进一步简化了既需要管理分类又需要管理分类下数据项的模块开发。

### 🎯 核心价值

**之前（手动实现）**：30-50行代码  
**现在（继承基类）**：**10-15行代码** ⭐  
**代码减少**：**70%** 🎉

### ✅ 内置功能

- ✅ **服务自动初始化** - 自动创建 `TService` 实例
- ✅ **分类数据获取** - 自动调用服务的 `GetAllCategories()`
- ✅ **分类数据加载** - 自动加载分类数据
- ✅ **数据筛选** - 自动按分类筛选数据项
- ✅ **搜索过滤** - 自动应用搜索关键词
- ✅ **树形转换** - 自动转换数据为树节点
- ✅ **内容操作命令** - 内置 `AddCommand` / `DeleteCommand` / `DeleteAllCommand`，统一驱动 `DataTreeView`
- ✅ **一键启用/禁用（BulkToggle）** - 内置 `BulkToggleCommand` / `BulkToggleButtonText` / `IsBulkToggleEnabled` / `BulkToggleToolTip`，配合 `TwoColumnEditorLayout` 实现业务零接入
- ✅ **集合刷新辅助** - `NotifyDataCollectionChanged()` 一键刷新按钮状态 + 触发 UI 更新
- ✅ **聚焦新节点** - `FocusOnDataItem()` 配合 `TreeDataViewModelBase.FocusTreeNode()` 自动选中新建/更新数据
- ✅ **分类兜底** - `AlignSelection()` 保障 ComboBox 默认选项合法，避免空白显示
- ✅ **树形分类下拉** - 内置 `CategorySelectionTree` / `SelectedCategoryTreePath` / `CategoryTreeNodeSelectCommand`，与 `TreeComboBoxStyle` 联动展示完整目录路径

### 🚀 v3.3 增强亮点

- **内容操作解锁**：`EnableContentActions` + 内置命令集让内容模块拥有独立的新建/删除/清空能力
- **全量删除流程**：新增抽象 `ClearAllDataItems()` 与虚方法 `OnAfterDeleteAll()`，与服务层 `ClearAll*()` 对齐
- **刷新即所见**：保存/删除后调用 `NotifyDataCollectionChanged()` + `FocusOnDataItem()` 即可即时刷新树并选中最新项
- **统一回调通道**：实现 `ITreeActionHost` 后，`TwoColumnEditorLayout` 会自动绑定 `TreeAfterActionCommand`

---

### 📖 快速开始

#### 步骤1：创建ViewModel（10-15行代码）

```csharp
using TM.Framework.Common.ViewModels;
using TM.Framework.Common.Controls;
using TM.Services.Knowledge;
using TM.Services.Knowledge.Models;

namespace TM.Modules.Knowledge.MaterialLibrary.CreativeMaterial
{
    public class CreativeMaterialViewModel : DataManagementViewModelBase<KnowledgeBlock, KnowledgeCategory, KnowledgeBlockService>
    {
        protected override string DefaultDataIcon => "📄";

        protected override List<KnowledgeBlock> GetAllDataItems()
        {
            return Service.GetAllBlocks();
        }

        protected override string GetDataCategory(KnowledgeBlock data)
        {
            return data.Category;
        }

        protected override TreeNodeItem ConvertToTreeNode(KnowledgeBlock data)
        {
            return new TreeNodeItem
            {
                Name = data.Title,
                Icon = DefaultDataIcon,
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override bool MatchesSearchKeyword(KnowledgeBlock data, string keyword)
        {
            return data.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   data.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }
    }
}
```

#### 步骤2：创建View（使用DualTabContainer）

```xml
<layout:TwoColumnEditorLayout
    LeftTitle="创作素材"
    ItemsSource="{Binding TreeData}"
    SearchKeyword="{Binding SearchKeyword, Mode=TwoWay}"
    EnableContentActions="True"
    AddCommand="{Binding AddCommand}"
    SaveCommand="{Binding SaveCommand}"
    DeleteCommand="{Binding DeleteCommand}"
    DeleteAllCommand="{Binding DeleteAllCommand}">
    
    <layout:TwoColumnEditorLayout.MultiPageContent>
        <layout:DualTabContainer
            Tab1Header="📝 编辑"
            Tab2Header="🔍 详情">
            <!-- Tab1: 编辑表单 -->
            <!-- Tab2: 详细信息 -->
        </layout:DualTabContainer>
    </layout:TwoColumnEditorLayout.MultiPageContent>
</layout:TwoColumnEditorLayout>
```

> ℹ️ ViewModel 实现 `ITreeActionHost` 后即可提供 `TreeAfterActionCommand`，`TwoColumnEditorLayout` 会自动透传给 `DataTreeView.AfterActionCommand`，用于在新增/保存/删除/全部删除后刷新右栏或弹提示。

### 🔁 分类管理标准流程

1. **服务层**：让 `TService` 继承 `ModuleServiceBase<TCategory, TData>`，自动获得分类和数据的本地CRUD管理。
2. **ViewModel 层**：
   - 使用 `Service.GetAllCategories()` 获取所有分类；
   - 使用 `Service.GetAllData()` 获取所有数据；
   - 分类不存在时提示用户在本地添加分类。

```csharp
private bool TryApplyCategory()
{
    var name = NormalizeCategory(FormCategory);
    if (string.IsNullOrEmpty(name))
    {
        GlobalToast.Warning("保存失败", "请选择分类");
        return false;
    }

    if (!CategoryOptions.Contains(name))
    {
        GlobalToast.Warning("保存失败", $"分类 \"{name}\" 不存在，请先创建该分类");
        return false;
    }

    FormCategory = name;
    return true;
}
```

> ✅ 通过该流程即可实现「业务服务 → ViewModel → UI」的标准化数据流转。

---

### 📖 实际案例

#### ✅ 已实现案例

| 模块 | ViewModel | 优化前 | 优化后 | 代码减少 | 文件路径 |
|------|-----------|--------|--------|---------|---------|
| 创作素材 | CreativeMaterialViewModel | ~400行 | ~160行 | **60%** | `Modules/Creation/Materials/CreativeMaterial/` |

**关键优化点**：
- ✅ 不再需要手动实现 `GetAllCategories()`
- ✅ 不再需要手动实现搜索过滤逻辑
- ✅ 不再需要手动管理 `Service` 实例
- ✅ 自动继承树形数据刷新机制

---

### 📖 核心方法

#### 必须实现的抽象方法

| 方法名 | 说明 | 返回值 |
|--------|------|--------|
| `GetAllDataItems()` | 获取所有数据项 | `List<TData>` |
| `GetDataCategory(TData)` | 获取数据项所属分类 | `string` |
| `ConvertToTreeNode(TData)` | 转换为树节点 | `TreeNodeItem` |
| `MatchesSearchKeyword(TData, string)` | 搜索匹配逻辑 | `bool` |
| `ClearAllDataItems()` ⭐ | 清空当前模板创建的数据项（需调用服务层 `ClearAll*()`） | `int`（返回删除数量） |
| `GetCurrentCategoryValue()` ⭐ | 返回当前表单所选分类，用于同步树形下拉显示 | `string?` |
| `ApplyCategorySelection(string categoryName)` ⭐ | 处理树形下拉选中事件（通常写回表单字段） | `void` |

#### 可选重写的属性

| 属性名 | 说明 | 默认值 |
|--------|------|--------|
| `DefaultDataIcon` | 数据项默认图标 | "📄" |

#### 可选重写/调用的方法（v3.3）

| 方法名 | 作用 | 建议用法 |
|--------|------|----------|
| `OnAfterDeleteAll(int deletedCount)` | 全部删除后回调 | 清空表单、重载下拉、展示提示 |
| `NotifyDataCollectionChanged()` | 刷新按钮状态与树数据 | 在 `Save` / `Delete` / `ClearAll` 成功后调用 |
| `FocusOnDataItem(TData data)` | 聚焦树节点 | 保存成功后选中新建/更新的节点 |
| `AlignSelection(string? value, ObservableCollection<string> options)` | 校准下拉值 | 赋值给 `FormCategory` 等 ComboBox 绑定字段 |
| `OnCategoryValueChanged(string? value)` | 同步树形下拉显示 | 在分类属性 setter 中调用，确保 `SelectedCategoryTreePath` 即时更新 |

```csharp
public string FormCategory
{
    get => _formCategory;
    set
    {
        if (_formCategory != value)
        {
            _formCategory = value;
            OnPropertyChanged();
            OnCategoryValueChanged(_formCategory); // ⭐ 必须调用，保持下拉路径同步
        }
    }
}

protected override int ClearAllDataItems()
{
    return Service.ClearAllBlocks();
}

protected override void OnAfterDeleteAll(int deletedCount)
{
    ResetForm();
    LoadCategoryOptions();
    GlobalToast.Info("清空完成", $"已删除 {deletedCount} 条素材");
}

protected override string? GetCurrentCategoryValue()
{
    return FormCategory;
}

protected override void ApplyCategorySelection(string categoryName)
{
    FormCategory = categoryName;
}

private void SaveCurrent()
{
    if (!TryApplyCategory())
    {
        return;
    }

    var saved = Service.SaveBlock(EditBlock);
    FocusOnDataItem(saved);
    NotifyDataCollectionChanged();
}

private void EnsureDefaultCategory()
{
    FormCategory = AlignSelection(FormCategory, CategoryOptions);
}
```

---

### 🎓 总结

**DataManagementViewModelBase v1.0** 是为**分类+数据管理**场景设计的标准化基类。

**核心价值**：
- ✅ **代码简化** - 30-50行 → 10-15行（70%减少）
- ✅ **服务集成** - 自动处理服务初始化
- ✅ **搜索过滤** - 自动实现搜索逻辑
- ✅ **统一体验** - 所有数据管理模块行为一致

### 🔌 ITreeActionHost 接口

- **文件位置**: `Framework/Common/ViewModels/ITreeActionHost.cs`
- **用途**: 暴露 `TreeAfterActionCommand`，让 `TwoColumnEditorLayout` → `DataTreeView` 在执行操作后通知 ViewModel
- **标准实现**:

```csharp
public class CreativeMaterialViewModel : DataManagementViewModelBase<...>, ITreeActionHost
{
    public ICommand TreeAfterActionCommand { get; }

    public CreativeMaterialViewModel()
    {
        TreeAfterActionCommand = new RelayCommand<string?>(OnTreeActionCompleted);
    }

    private void OnTreeActionCompleted(string? action)
    {
        if (action == "Save")
        {
            LoadBodyPreview();
        }
    }
}
```

> 🧩 `TwoColumnEditorLayout` 会在 `DataContextChanged` 时自动检测该接口，无需额外绑定；如需自定义绑定，可直接对 `TreeAfterActionCommand` 依赖属性赋值。

---

## 2. SinglePageViewModelBase - 单页功能视图模型基类

**版本**: v1.0 ⭐ **新增**  
**更新日期**: 2025-11-04  
**适用场景**: 单页功能界面（不是分类管理，不是数据管理）  
**继承关系**: `SinglePageViewModelBase` → `INotifyPropertyChanged`  
**代码量**: **按需扩展**

---

### 📖 概述

`SinglePageViewModelBase` 是为**单页功能界面**设计的通用基类，提供了MVVM开发所需的所有基础设施（属性通知、标准命令、状态管理等）。

### 🎯 核心价值

**之前（手动实现）**：需要写50-100行基础代码  
**现在（继承基类）**：**只写业务逻辑** ⭐  
**代码减少**：**约50%** 🎉

### ✅ 内置功能

#### 1. 自动属性通知

```csharp
private string _title;
public string Title
{
    get => _title;
    set => SetProperty(ref _title, value); // 自动OnPropertyChanged + 标记HasUnsavedChanges
}
```

#### 2. 标准命令（已内置）

- `SaveCommand` - 保存操作
- `DeleteCommand` - 删除操作
- `CancelCommand` - 取消操作
- `RefreshCommand` - 刷新操作

#### 3. 页面状态管理（已内置）

- `IsBusy` - 是否繁忙
- `IsLoading` - 是否加载中
- `HasUnsavedChanges` - 是否有未保存更改
- `StatusMessage` - 状态消息

#### 4. 虚方法（可选重写）

- `OnInitialize()` - 初始化
- `Save()` - 保存逻辑
- `Delete()` - 删除逻辑
- `Cancel()` - 取消逻辑
- `Refresh()` - 刷新逻辑
- `CanSave()` - 判断是否可保存
- `CanDelete()` - 判断是否可删除

---

### 📖 快速开始

#### 步骤1：创建ViewModel

```csharp
using TM.Framework.Common.ViewModels;

namespace TM.Modules.Analysis
{
    public class AnalysisViewModel : SinglePageViewModelBase
    {
        // 业务属性（自动属性通知）
        private string _analysisResult;
        public string AnalysisResult
        {
            get => _analysisResult;
            set => SetProperty(ref _analysisResult, value);
        }

        // 重写初始化
        protected override void OnInitialize()
        {
            // 加载初始数据
            LoadAnalysisData();
        }

        // 重写保存逻辑
        protected override void Save()
        {
            IsBusy = true;
            try
            {
                // 保存分析结果...
                GlobalToast.Success("保存成功", "分析结果已保存");
                HasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                GlobalToast.Error("保存失败", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void LoadAnalysisData()
        {
            // 加载数据逻辑...
        }
    }
}
```

#### 步骤2：创建View（使用SingleTabContainer）

```xml
<layout:TwoColumnEditorLayout
    LeftTitle="分析工具"
    ItemsSource="{Binding TreeData}"
    SaveCommand="{Binding SaveCommand}">
    
    <layout:TwoColumnEditorLayout.SinglePageContent>
        <layout:SingleTabContainer>
            <layout:SingleTabContainer.PageContent>
                <StackPanel>
                    <!-- 业务UI -->
                    <TextBox Text="{Binding AnalysisResult, Mode=TwoWay}"/>
                    
                    <!-- 状态显示 -->
                    <TextBlock Text="{Binding StatusMessage}"/>
                </StackPanel>
            </layout:SingleTabContainer.PageContent>
        </layout:SingleTabContainer>
    </layout:TwoColumnEditorLayout.SinglePageContent>
</layout:TwoColumnEditorLayout>
```

---

### 📖 核心属性

| 属性名 | 类型 | 说明 |
|--------|------|------|
| `IsBusy` | bool | 页面是否繁忙 |
| `IsLoading` | bool | 是否正在加载 |
| `HasUnsavedChanges` | bool | 是否有未保存更改 |
| `StatusMessage` | string | 状态消息 |

### 📖 核心命令

| 命令名 | 说明 | 默认行为 |
|--------|------|---------|
| `SaveCommand` | 保存 | 调用 `Save()` |
| `DeleteCommand` | 删除 | 调用 `Delete()` |
| `CancelCommand` | 取消 | 重置 `HasUnsavedChanges` |
| `RefreshCommand` | 刷新 | 调用 `OnInitialize()` |

---

### 🎓 总结

**SinglePageViewModelBase v1.0** 是为**单页功能界面**设计的通用基类。

**核心价值**：
- ✅ **基础设施完备** - 属性通知、命令、状态管理全自动
- ✅ **减少重复代码** - 不需要每次都写INotifyPropertyChanged
- ✅ **标准化行为** - 所有单页功能UI交互一致
- ✅ **灵活扩展** - 只重写需要的虚方法

**适用场景**：
- 拆解分析工具
- 配置页面
- 工具界面
- 任何不涉及分类/数据管理的单页功能

---

### 📖 实际案例

#### ✅ 已实现案例

| 模块 | ViewModel | 优化前 | 优化后 | 代码减少 | 文件路径 |
|------|-----------|--------|--------|---------|---------|
| 拆解分析 | AnalysisViewModel | ~800行（错误使用TreeDataViewModelBase） | ~800行（使用SinglePageViewModelBase） | 架构修复 | `Modules/Creation/Materials/Analysis/` |

**关键修复点**：
- ❌ **之前**：错误使用 `TreeDataViewModelBase`（这是为分类+数据管理设计的）
- ✅ **现在**：正确使用 `SinglePageViewModelBase`（提供标准命令和状态管理）
- ✅ 基类提供了 `SaveCommand`、`DeleteCommand`、`RefreshCommand`
- ✅ 基类提供了 `IsBusy`、`IsLoading`、`HasUnsavedChanges` 状态管理
- ✅ 不再需要手动实现 `INotifyPropertyChanged`

**代码对比**：
```csharp
// ❌ 之前（架构错误）
public class AnalysisViewModel : TreeDataViewModelBase<AnalysisMaterial, AnalysisCategory>
{
    // 必须实现不需要的抽象方法...
    protected override List<AnalysisCategory> GetAllCategories() { ... }
    protected override List<AnalysisMaterial> GetChildrenDataForCategory(...) { ... }
}

// ✅ 现在（架构正确）
public class AnalysisViewModel : SinglePageViewModelBase
{
    // 只重写需要的虚方法
    protected override void Save()
    {
        SaveMaterial();
    }
    
    protected override void Refresh()
    {
        LoadData();
    }
}
```

---

## 3. TreeDataViewModelBase - 树形数据视图模型基类

**版本**: v3.0  
**更新日期**: 2025-10-28  
**适用场景**: 复杂自定义场景（需要完全自定义树构建逻辑）  
**代码量**: 30-50行

---

### 📖 概述

`TreeDataViewModelBase<TData, TCategory>` 是配合 `DataTreeView` 使用的**通用框架基类**，提供了树形数据管理的基础框架，但**不包含具体业务逻辑**。

### ✅ v3.3 更新

- **节点聚焦**：新增 `FocusTreeNode(Predicate<TreeNodeItem>)`，供上层调用 `FocusOnDataItem()` 自动选中树节点
- **展开状态恢复**：`RefreshTreeData()` 会在刷新前后记录/还原 `IsExpanded`，避免刷新后折叠
- **动作回调桥接**：新增 `TreeAfterActionCommand` 依赖通道，配合 `ITreeActionHost` 统一回调

### 🎯 核心理念

**v3.0**: 提供完整的通用逻辑，但保留自定义空间  
**适用场景**: 当前三个基类都无法满足需求时使用

### 🏗️ 泛型参数

```csharp
public abstract class TreeDataViewModelBase<TData, TCategory>
    where TCategory : ICategory
{
    // TData: 子项数据类型（如KnowledgeBlock、Character）
    // TCategory: 分类数据类型（必须实现ICategory接口）
}
```

### ICategory 接口

```csharp
public interface ICategory
{
    string Name { get; }            // 分类名称
    string Icon { get; }            // 分类图标
    string? ParentCategory { get; } // 父分类名称
    int Level { get; }              // 分类层级
    int Order { get; }              // 排序顺序
    bool IsEnabled { get; set; }     // 是否启用
}
```

---

## IBulkToggleSelectionHost - 一键启用/禁用选中同步接口

**用途**：让 `TwoColumnEditorLayout` 在节点双击时把当前选中节点同步给 ViewModel，以驱动 `DataManagementViewModelBase` 的 BulkToggle 状态刷新（按钮文本/可用性/提示）。

**文件位置**：`Framework/Common/ViewModels/IBulkToggleSelectionHost.cs`

**接口定义**：

```csharp
public interface IBulkToggleSelectionHost
{
    void OnTreeNodeSelected(TreeNodeItem? node);
}
```

**接入方式**：
- `DataManagementViewModelBase` 已实现该接口；
- `TwoColumnEditorLayout` 内部包装 `NodeDoubleClickCommand`，在调用业务 `SelectNodeCommand` 前，会先调用 `IBulkToggleSelectionHost.OnTreeNodeSelected(...)`；
- 业务模块无需手写 `UpdateBulkToggleState()` 或任何 BulkToggle 逻辑。

---

### 📖 使用指南

#### 步骤1：继承基类

```csharp
public class CustomViewModel : TreeDataViewModelBase<CustomData, CustomCategory>
{
    // 实现3个抽象方法
    protected override List<CustomCategory> GetAllCategories() { ... }
    protected override List<CustomData> GetChildrenDataForCategory(string categoryName) { ... }
    protected override TreeNodeItem ConvertDataToTreeNode(CustomData data) { ... }
}
```

#### 步骤2：加载数据

```csharp
public CustomViewModel()
{
    LoadData();
}

private void LoadData()
{
    DataSource.Clear();
    var items = _service.GetAllItems();
    foreach (var item in items)
    {
        DataSource.Add(item); // 自动触发RefreshTreeData
    }
}

public void FocusLatest()
{
    FocusTreeNode(node => ReferenceEquals(node.Tag, _latestItem));
}
```

---

### 📖 基类提供的属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `DataSource` | `ObservableCollection<TData>` | 原始数据集合 |
| `TreeData` | `ObservableCollection<TreeNodeItem>` | 树形显示数据 |
| `SearchKeyword` | `string` | 搜索关键词 |

---

### 🔧 基类内置功能

- ✅ 孤儿节点过滤
- ✅ 5级深度限制
- ✅ 搜索过滤规则
- ✅ 递归树构建
- ✅ 自动刷新机制

---

### 🎓 总结

**TreeDataViewModelBase v3.0** 是最底层的通用框架基类。

**使用建议**：
- ⚠️ 优先使用两个专用基类（DataManagement/SinglePage）
- ✅ 仅在有特殊需求时使用此基类
- ✅ 提供最大灵活性，但需要更多代码

---

## 📚 基类选择指南

| 场景 | 推荐基类 | 代码量 | 特点 |
|------|---------|---------|------|
| 分类+数据管理 | **DataManagementViewModelBase** | 10-15行 | ⭐ 标准化 |
| 单页功能界面 | **SinglePageViewModelBase** | 按需 | ⭐ 基础设施完备 |
| 完全自定义（内部基类） | TreeDataViewModelBase | 30-50行 | 最大灵活性 |

---

## 📋 更新日志

### v1.0 - 2025-11-04
- ✅ 新增 `DataManagementViewModelBase` - 数据管理基类
- ✅ 新增 `SinglePageViewModelBase` - 单页功能基类
- ✅ 完善 `TreeDataViewModelBase` - 通用框架基类

---

**🎉 完整的三层基类体系，覆盖所有开发场景！**

---

## 4. AI生成配置化 - v4.3新增 ⭐ + 智能字段提取 - v4.4新增 ⭐⭐

**版本**: v4.4  
**更新日期**: 2025-12-11  
**适用场景**: 需要AI智能生成功能的ViewModel  
**文件位置**: 
- `Framework/Common/ViewModels/AIGenerationConfig.cs`
- `Framework/Common/Services/SmartFieldExtractor.cs`

---

### 📖 概述

AI生成配置化将41个ViewModel中重复的AI生成代码统一为配置化方式，子类只需提供配置即可。

### 🎯 核心价值

| 指标 | 改造前 | 改造后 | 收益 |
|-----|-------|-------|------|
| 每个ViewModel代码量 | ~80行 | ~35行 | **-56%** |
| 新增功能工作量 | 复制+修改80行 | 填写配置35行 | **-56%** |
| 修改AI流程工作量 | 改41个文件 | 改1个基类 | **-98%** |

---

### 📖 枚举定义

```csharp
/// <summary>AI服务类型</summary>
public enum AIServiceType
{
    /// <summary>ChatEngine（支持对话上下文）</summary>
    ChatEngine,
    
    /// <summary>TextGenerationService（纯文本生成）</summary>
    TextGeneration
}

/// <summary>返回格式解析方式</summary>
public enum ResponseFormat
{
    /// <summary>Markdown格式（### 标题 解析）</summary>
    Markdown,
    
    /// <summary>JSON格式</summary>
    Json
}
```

---

### 📖 配置模型

```csharp
public class AIGenerationConfig
{
    /// <summary>提示词模板分类名（从PromptService获取）</summary>
    public string Category { get; set; } = string.Empty;
    
    /// <summary>AI服务类型（默认ChatEngine）</summary>
    public AIServiceType ServiceType { get; set; } = AIServiceType.ChatEngine;
    
    /// <summary>返回格式（默认Markdown）</summary>
    public ResponseFormat ResponseFormat { get; set; } = ResponseFormat.Markdown;
    
    /// <summary>AI调用时的消息前缀</summary>
    public string MessagePrefix { get; set; } = "AI生成";
    
    /// <summary>进行中提示</summary>
    public string ProgressMessage { get; set; } = "正在生成...";
    
    /// <summary>完成提示</summary>
    public string CompleteMessage { get; set; } = "生成完成";
    
    /// <summary>输入变量映射：模板变量名 → 取值函数</summary>
    public Dictionary<string, Func<string>> InputVariables { get; set; } = new();
    
    /// <summary>输出字段映射：返回标题/键名 → 赋值函数</summary>
    public Dictionary<string, Action<string>> OutputFields { get; set; } = new();
    
    /// <summary>上下文获取函数（可选）</summary>
    public Func<Task<string>>? ContextProvider { get; set; }
    
    // ========== v4.4 新增：智能字段提取 ==========
    
    /// <summary>输出字段读取映射（用于判断是否覆盖用户已填内容）</summary>
    public Dictionary<string, Func<string>>? OutputFieldGetters { get; set; }
    
    /// <summary>字段别名表（提高识别率）</summary>
    public Dictionary<string, string[]>? FieldAliases { get; set; }
    
    /// <summary>是否启用关键词匹配策略（默认关闭）</summary>
    public bool EnableKeywordExtract { get; set; } = false;
}
```

---

### 📖 基类虚方法

```csharp
// DataManagementViewModelBase 新增虚方法

/// <summary>获取AI生成配置（子类重写提供配置）</summary>
/// <returns>返回null表示使用基类原有的反射式生成</returns>
protected virtual AIGenerationConfig? GetAIGenerationConfig() => null;

/// <summary>获取提示词仓库（子类重写提供实例）</summary>
protected virtual IPromptRepository? GetPromptRepository() => null;
```

---

### 🚀 快速开始

#### 改造前（~80行）

```csharp
protected override async Task ExecuteAIGenerateAsync()
{
    if (_currentEditingData == null) { GlobalToast.Warning(...); return; }
    var template = _promptService.Value.GetTemplatesByCategory("世界观一致性").FirstOrDefault();
    if (template == null) { GlobalToast.Warning(...); return; }
    var prompt = template.SystemPrompt
        .Replace("{校验名称}", FormName)
        .Replace("{校验目标}", FormVerificationTarget);
    GlobalToast.Info("AI校验中", "正在进行世界观一致性校验...");
    var result = await ChatEngine.Instance.SendMessageAsync(...);
    ApplyAIResult(result);
    GlobalToast.Success("校验完成", "AI已完成校验");
}

private void ApplyAIResult(string result) { ... }  // 每个都要写
private Dictionary<string, string> ParseMarkdownSections(string markdown) { ... }  // 每个都要写
```

#### 改造后（~35行）

```csharp
// 1. 提供提示词仓库
private static readonly Lazy<PromptService> _promptService 
    = new(() => new PromptService());
protected override IPromptRepository? GetPromptRepository() 
    => _promptService.Value;

// 2. 缓存配置实例
private AIGenerationConfig? _cachedConfig;
protected override AIGenerationConfig? GetAIGenerationConfig()
{
    return _cachedConfig ??= new AIGenerationConfig
    {
        Category = "世界观一致性",
        ServiceType = AIServiceType.ChatEngine,
        ResponseFormat = ResponseFormat.Markdown,
        MessagePrefix = "校验世界观一致性",
        ProgressMessage = "正在进行世界观一致性校验...",
        CompleteMessage = "AI已完成校验",
        InputVariables = new()
        {
            ["校验名称"] = () => FormName,
            ["校验目标"] = () => FormVerificationTarget,
        },
        OutputFields = new()
        {
            ["校验结果"] = v => FormVerificationResult = v,
            ["问题描述"] = v => FormIssueDescription = v,
            ["修复建议"] = v => FormFixSuggestion = v,
        },
        ContextProvider = async () =>
        {
            var ctx = await _contextService.Value.GetWorldviewContextAsync();
            return $"世界规则: {ctx.WorldRules?.Count ?? 0}项";
        }
    };
}

protected override bool CanExecuteAIGenerate() => _currentEditingData != null;
```

---

### 📖 已改造模块统计

| 模块 | 数量 | 状态 |
|-----|------|-----|
| Validate（校验模块） | 15个 | ✅ 完成 |
| Generate（生成模块） | 12个 | ✅ 完成 |
| Design（设计模块） | 14个 | ✅ 完成 |
| **总计** | **41个** | **✅** |

---

---

### 📖 SmartFieldExtractor - 智能字段提取器（v4.4新增）⭐⭐

**文件位置**: `Framework/Common/Services/SmartFieldExtractor.cs`

#### 核心能力

从 AI 返回内容中智能提取字段，支持多种格式：

| 策略 | 说明 | 优先级 |
|------|------|--------|
| **策略1: JSON解析** | 自动提取 `{}` 或 ` ```json ``` ` 块 | 最高 |
| **策略2: Markdown分段** | 按 `## 标题` 或 `### 标题` 分割 | 次高 |
| **策略3: 关键词匹配** | 搜索字段名出现位置（需显式开启） | 兜底 |

#### 匹配规则

```
精确匹配 > 别名精确匹配 > 包含匹配（最长优先）
```

#### 填充逻辑

```
提取成功 → 直接填充字段
提取失败 + 原值为空 → 填入"[待补充]"
提取失败 + 原值非空 → 保持不动（不覆盖用户输入）
```

#### 使用示例

```csharp
protected override AIGenerationConfig? GetAIGenerationConfig()
{
    return new AIGenerationConfig
    {
        Category = "智能拆书",
        OutputFields = new()
        {
            ["世界构建手法"] = v => FormWorldBuildingMethod = v,
            ["力量体系设计"] = v => FormPowerSystemDesign = v,
        },
        // v4.4 新增：字段读取映射（保护用户已填内容）
        OutputFieldGetters = new()
        {
            ["世界构建手法"] = () => FormWorldBuildingMethod,
            ["力量体系设计"] = () => FormPowerSystemDesign,
        },
        // v4.4 新增：字段别名（提高识别率）
        FieldAliases = new()
        {
            ["世界构建手法"] = new[] { "世界观构建", "世界设定" },
        },
        // v4.4 新增：关键词匹配开关（默认关闭）
        EnableKeywordExtract = false,
        // 上下文提供函数
        ContextProvider = async () => await LoadCrawledContentAsync()
    };
}
```

---

### 🎓 总结

**AI生成配置化 v4.3 + 智能字段提取 v4.4** 统一了41个ViewModel的AI生成逻辑。

**核心价值**：
- ✅ **代码简化** - 80行 → 35行（-56%）
- ✅ **统一维护** - 修改基类即可影响全部ViewModel
- ✅ **灵活配置** - 支持ChatEngine/TextGeneration两种服务
- ✅ **向后兼容** - 返回null使用基类原有反射式生成
- ✅ **智能提取** - 多策略解析AI返回，自动匹配字段
- ✅ **保护输入** - 不覆盖用户已有内容，空字段填占位符
