using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.MVVM;
using TM.Services.Framework.Settings;

namespace TM.Framework.SystemSettings.Logging.LogFormat
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class LogFormatViewModel : INotifyPropertyChanged
    {
        private LogFormatSettings _settings;
        private readonly string _settingsFilePath;
        private readonly LogManager _logManager;
        private string _previewText = string.Empty;
        private string _validationMessage = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public LogFormatViewModel(LogManager logManager)
        {
            _logManager = logManager;
            _settings = new LogFormatSettings();
            _settingsFilePath = StoragePathHelper.GetFilePath(
                "Framework",
                "SystemSettings/Logging/LogFormat",
                "settings.json"
            );

            OutputFormatTypes = new List<OutputFormatType>
            {
                OutputFormatType.Text,
                OutputFormatType.JSON,
                OutputFormatType.XML
            };

            FieldDataTypes = new List<FieldDataType>
            {
                FieldDataType.String,
                FieldDataType.Integer,
                FieldDataType.DateTime,
                FieldDataType.Boolean,
                FieldDataType.Double
            };

            PresetTemplates = LogFormatSettings.PresetTemplates.Keys.ToList();

            CustomFields = new ObservableCollection<CustomField>();
            SavedTemplates = new ObservableCollection<FormatTemplate>();
            ValidationResults = new ObservableCollection<ValidationResult>();

            AsyncSettingsLoader.LoadOrDefer<LogFormatSettings>(_settingsFilePath, s =>
            {
                _settings = s;
                OnAllPropertiesChanged();
                LoadCustomFields();
                LoadSavedTemplates();
                GeneratePreview();
                ValidateTemplate();
            }, "LogFormat");

            SaveCommand = new RelayCommand(SaveSettings);
            ResetCommand = new RelayCommand(ResetSettings);
            ApplyPresetCommand = new RelayCommand(param => ApplyPreset((param as string)!));
            PreviewCommand = new RelayCommand(GeneratePreview);
            ValidateCommand = new RelayCommand(ValidateTemplate);

            AddCustomFieldCommand = new RelayCommand(AddCustomField);
            RemoveCustomFieldCommand = new RelayCommand(RemoveCustomField);

            SaveAsTemplateCommand = new RelayCommand(SaveAsTemplate);
            LoadTemplateCommand = new RelayCommand(LoadTemplate);
            DeleteTemplateCommand = new RelayCommand(DeleteTemplate);
            ExportTemplateCommand = new RelayCommand(ExportTemplate);
            ImportTemplateCommand = new RelayCommand(ImportTemplate);
        }

        public string FormatTemplate
        {
            get => _settings.FormatTemplate;
            set
            {
                _settings.FormatTemplate = value;
                OnPropertyChanged(nameof(FormatTemplate));
                GeneratePreview();
            }
        }

        public string TimestampFormat
        {
            get => _settings.TimestampFormat;
            set
            {
                _settings.TimestampFormat = value;
                OnPropertyChanged(nameof(TimestampFormat));
                GeneratePreview();
            }
        }

        public string FieldSeparator
        {
            get => _settings.FieldSeparator;
            set { _settings.FieldSeparator = value; OnPropertyChanged(nameof(FieldSeparator)); }
        }

        public OutputFormatType OutputFormat
        {
            get => _settings.OutputFormat;
            set
            {
                _settings.OutputFormat = value;
                OnPropertyChanged(nameof(OutputFormat));
                GeneratePreview();
            }
        }

        public bool EnableFieldAlignment
        {
            get => _settings.EnableFieldAlignment;
            set { _settings.EnableFieldAlignment = value; OnPropertyChanged(nameof(EnableFieldAlignment)); }
        }

        public int TimestampWidth
        {
            get => _settings.TimestampWidth;
            set { _settings.TimestampWidth = value; OnPropertyChanged(nameof(TimestampWidth)); }
        }

        public int LevelWidth
        {
            get => _settings.LevelWidth;
            set { _settings.LevelWidth = value; OnPropertyChanged(nameof(LevelWidth)); }
        }

        public bool EnableMultilineIndent
        {
            get => _settings.EnableMultilineIndent;
            set { _settings.EnableMultilineIndent = value; OnPropertyChanged(nameof(EnableMultilineIndent)); }
        }

        public string MultilineIndent
        {
            get => _settings.MultilineIndent;
            set { _settings.MultilineIndent = value; OnPropertyChanged(nameof(MultilineIndent)); }
        }

        public string PreviewText
        {
            get => _previewText;
            set { _previewText = value; OnPropertyChanged(nameof(PreviewText)); }
        }

        public string ValidationMessage
        {
            get => _validationMessage;
            set { _validationMessage = value; OnPropertyChanged(nameof(ValidationMessage)); }
        }

        public List<OutputFormatType> OutputFormatTypes { get; }
        public List<FieldDataType> FieldDataTypes { get; }
        public List<string> PresetTemplates { get; }
        public ObservableCollection<CustomField> CustomFields { get; }
        public ObservableCollection<FormatTemplate> SavedTemplates { get; }
        public ObservableCollection<ValidationResult> ValidationResults { get; }

        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand ApplyPresetCommand { get; }
        public ICommand PreviewCommand { get; }
        public ICommand ValidateCommand { get; }
        public ICommand AddCustomFieldCommand { get; }
        public ICommand RemoveCustomFieldCommand { get; }
        public ICommand SaveAsTemplateCommand { get; }
        public ICommand LoadTemplateCommand { get; }
        public ICommand DeleteTemplateCommand { get; }
        public ICommand ExportTemplateCommand { get; }
        public ICommand ImportTemplateCommand { get; }

        private async void SaveSettings()
        {
            try
            {
                var directory = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_settings, JsonHelper.Default);
                var tmpLfs = _settingsFilePath + ".tmp";
                await File.WriteAllTextAsync(tmpLfs, json);
                File.Move(tmpLfs, _settingsFilePath, overwrite: true);

                _logManager.Reload();

                TM.App.Log($"[LogFormat] 保存设置成功");
                GlobalToast.Success("保存成功", "日志格式设置已保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LogFormat] 保存设置失败: {ex.Message}");
                GlobalToast.Error("保存失败", $"无法保存日志格式设置: {ex.Message}");
            }
        }

        private void ResetSettings()
        {
            var result = StandardDialog.ShowConfirm(
                "是否将日志格式设置恢复为默认值？",
                "确认重置"
            );

            if (result)
            {
                _settings = new LogFormatSettings();
                OnAllPropertiesChanged();
                SaveSettings();
                GeneratePreview();
                TM.App.Log($"[LogFormat] 重置设置成功");
                GlobalToast.Info("已重置", "日志格式设置已恢复为默认值");
            }
        }

        private void ApplyPreset(string presetName)
        {
            if (!string.IsNullOrEmpty(presetName) && LogFormatSettings.PresetTemplates.ContainsKey(presetName))
            {
                FormatTemplate = LogFormatSettings.PresetTemplates[presetName];
                TM.App.Log($"[LogFormat] 应用预设模板: {presetName}");
                GlobalToast.Success("已应用", $"已应用预设模板 '{presetName}'");
            }
        }

        private void GeneratePreview()
        {
            try
            {
                var now = DateTime.Now;
                var timestamp = now.ToString(TimestampFormat);
                var level = "INFO";
                var message = "这是一条示例日志消息";
                var caller = "LogFormatViewModel.GeneratePreview";
                var threadId = "1234";
                var processId = "5678";

                var preview = FormatTemplate
                    .Replace("{timestamp}", timestamp)
                    .Replace("{level}", level)
                    .Replace("{message}", message)
                    .Replace("{caller}", caller)
                    .Replace("{threadid}", threadId)
                    .Replace("{processid}", processId);

                if (OutputFormat == OutputFormatType.JSON)
                {
                    preview = $"{{\n  \"timestamp\": \"{timestamp}\",\n  \"level\": \"{level}\",\n  \"message\": \"{message}\"\n}}";
                }
                else if (OutputFormat == OutputFormatType.XML)
                {
                    preview = $"<log>\n  <timestamp>{timestamp}</timestamp>\n  <level>{level}</level>\n  <message>{message}</message>\n</log>";
                }

                PreviewText = preview;
            }
            catch (Exception ex)
            {
                PreviewText = $"预览生成失败: {ex.Message}";
            }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void ValidateTemplate()
        {
            ValidationResults.Clear();

            try
            {
                var template = FormatTemplate;
                var allFields = new List<string>(LogFormatSettings.BuiltInFields);
                allFields.AddRange(CustomFields.Select(f => f.Placeholder));

                var placeholders = Regex.Matches(template, @"\{(\w+)(?::[\w\-:\.]+)?\}");

                foreach (Match match in placeholders)
                {
                    var fieldName = match.Groups[1].Value.ToLower();

                    if (!allFields.Contains(fieldName))
                    {
                        ValidationResults.Add(new ValidationResult
                        {
                            IsValid = false,
                            Severity = ValidationSeverity.Error,
                            Message = $"未知字段: {{{fieldName}}}",
                            Position = match.Index,
                            Suggestion = $"可用字段: {string.Join(", ", allFields)}"
                        });
                    }
                }

                var openCount = template.Count(c => c == '{');
                var closeCount = template.Count(c => c == '}');
                if (openCount != closeCount)
                {
                    ValidationResults.Add(new ValidationResult
                    {
                        IsValid = false,
                        Severity = ValidationSeverity.Error,
                        Message = "大括号不匹配",
                        Suggestion = $"打开: {openCount}, 关闭: {closeCount}"
                    });
                }

                if (placeholders.Count > 10)
                {
                    ValidationResults.Add(new ValidationResult
                    {
                        IsValid = true,
                        Severity = ValidationSeverity.Warning,
                        Message = "字段数量较多，可能影响性能",
                        Suggestion = "考虑减少字段数量或使用更简洁的模板"
                    });
                }

                if (ValidationResults.Count == 0)
                {
                    ValidationMessage = "✅ 模板格式正确";
                }
                else
                {
                    var errorCount = ValidationResults.Count(r => r.Severity == ValidationSeverity.Error);
                    var warningCount = ValidationResults.Count(r => r.Severity == ValidationSeverity.Warning);
                    ValidationMessage = $"❌ {errorCount} 个错误, ⚠️ {warningCount} 个警告";
                }
            }
            catch (Exception ex)
            {
                ValidationMessage = $"验证失败: {ex.Message}";
                TM.App.Log($"[LogFormat] 验证模板失败: {ex.Message}");
            }
        }

        private void LoadCustomFields()
        {
            CustomFields.Clear();
            foreach (var field in _settings.CustomFields)
            {
                CustomFields.Add(field);
            }
        }

        private void AddCustomField()
        {
            var fieldName = StandardDialog.ShowInput("请输入自定义字段名称（英文，无空格）", "添加自定义字段");
            if (!string.IsNullOrWhiteSpace(fieldName))
            {
                if (!Regex.IsMatch(fieldName, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
                {
                    GlobalToast.Error("格式错误", "字段名称只能包含字母、数字和下划线，且不能以数字开头");
                    return;
                }

                if (CustomFields.Any(f => f.Placeholder.Equals(fieldName, StringComparison.OrdinalIgnoreCase)))
                {
                    GlobalToast.Warning("字段已存在", $"字段 '{fieldName}' 已经存在");
                    return;
                }

                var field = new CustomField
                {
                    Name = fieldName,
                    Placeholder = fieldName.ToLower(),
                    DataType = FieldDataType.String,
                    Description = "自定义字段",
                    CreatedTime = DateTime.Now
                };

                CustomFields.Add(field);
                _settings.CustomFields.Add(field);

                TM.App.Log($"[LogFormat] 添加自定义字段: {fieldName}");
                GlobalToast.Success("添加成功", $"已添加自定义字段 '{fieldName}'");
                ValidateTemplate();
            }
        }

        private void RemoveCustomField()
        {
            if (CustomFields.Count > 0)
            {
                var lastField = CustomFields[CustomFields.Count - 1];
                var result = StandardDialog.ShowConfirm(
                    $"是否删除自定义字段 '{lastField.Name}'？",
                    "确认删除"
                );

                if (result)
                {
                    CustomFields.Remove(lastField);
                    _settings.CustomFields.Remove(lastField);
                    TM.App.Log($"[LogFormat] 删除自定义字段: {lastField.Name}");
                    GlobalToast.Success("删除成功", $"已删除自定义字段 '{lastField.Name}'");
                    ValidateTemplate();
                }
            }
            else
            {
                GlobalToast.Warning("无可删除项", "没有自定义字段");
            }
        }

        private void LoadSavedTemplates()
        {
            SavedTemplates.Clear();
            foreach (var template in _settings.SavedTemplates)
            {
                SavedTemplates.Add(template);
            }
        }

        private void SaveAsTemplate()
        {
            var templateName = StandardDialog.ShowInput("请输入模板名称", "保存为模板");
            if (!string.IsNullOrWhiteSpace(templateName))
            {
                var existing = SavedTemplates.FirstOrDefault(t => t.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    var overwrite = StandardDialog.ShowConfirm(
                        $"模板 '{templateName}' 已存在，是否覆盖？",
                        "确认覆盖"
                    );

                    if (!overwrite)
                    {
                        return;
                    }

                    SavedTemplates.Remove(existing);
                    _settings.SavedTemplates.Remove(existing);
                }

                var template = new FormatTemplate
                {
                    Name = templateName,
                    Template = FormatTemplate,
                    Description = StandardDialog.ShowInput("请输入模板描述（可选）", "模板描述") ?? "",
                    CreatedTime = DateTime.Now,
                    LastUsedTime = DateTime.Now,
                    UsageCount = 1
                };

                SavedTemplates.Insert(0, template);
                _settings.SavedTemplates.Insert(0, template);
                SaveSettings();

                TM.App.Log($"[LogFormat] 保存模板: {templateName}");
                GlobalToast.Success("保存成功", $"已保存模板 '{templateName}'");
            }
        }

        private void LoadTemplate()
        {
            if (SavedTemplates.Count == 0)
            {
                GlobalToast.Info("无模板", "没有保存的模板");
                return;
            }

            var template = SavedTemplates.OrderByDescending(t => t.LastUsedTime).FirstOrDefault();
            if (template != null)
            {
                FormatTemplate = template.Template;
                template.LastUsedTime = DateTime.Now;
                template.UsageCount++;
                SaveSettings();

                TM.App.Log($"[LogFormat] 加载模板: {template.Name}");
                GlobalToast.Success("加载成功", $"已加载模板 '{template.Name}'");
            }
        }

        private void DeleteTemplate()
        {
            if (SavedTemplates.Count > 0)
            {
                var template = SavedTemplates[0];
                var result = StandardDialog.ShowConfirm(
                    $"是否删除模板 '{template.Name}'？",
                    "确认删除"
                );

                if (result)
                {
                    SavedTemplates.Remove(template);
                    _settings.SavedTemplates.Remove(template);
                    SaveSettings();

                    TM.App.Log($"[LogFormat] 删除模板: {template.Name}");
                    GlobalToast.Success("删除成功", $"已删除模板 '{template.Name}'");
                }
            }
            else
            {
                GlobalToast.Warning("无可删除项", "没有保存的模板");
            }
        }

        private async void ExportTemplate()
        {
            try
            {
                var exportPath = StoragePathHelper.GetFilePath("Framework", "SystemSettings/Logging/LogFormat/Exports", "templates_export.json");
                var directory = Path.GetDirectoryName(exportPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var exportData = new
                {
                    Templates = SavedTemplates.ToList(),
                    CustomFields = CustomFields.ToList(),
                    ExportTime = DateTime.Now
                };

                var json = JsonSerializer.Serialize(exportData, JsonHelper.Default);
                var tmpLfe = exportPath + ".tmp";
                await File.WriteAllTextAsync(tmpLfe, json);
                File.Move(tmpLfe, exportPath, overwrite: true);

                TM.App.Log($"[LogFormat] 导出模板到: {exportPath}");
                GlobalToast.Success("导出成功", $"已导出到: {exportPath}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LogFormat] 导出模板失败: {ex.Message}");
                GlobalToast.Error("导出失败", $"无法导出模板: {ex.Message}");
            }
        }

        private async void ImportTemplate()
        {
            try
            {
                var importPath = StoragePathHelper.GetFilePath("Framework", "SystemSettings/Logging/LogFormat/Exports", "templates_export.json");

                if (!File.Exists(importPath))
                {
                    GlobalToast.Warning("文件不存在", "找不到导出的模板文件");
                    return;
                }

                var json = await File.ReadAllTextAsync(importPath).ConfigureAwait(true);
                var importData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                if (importData != null && importData.ContainsKey("Templates"))
                {
                    var templates = JsonSerializer.Deserialize<List<FormatTemplate>>(importData["Templates"].GetRawText());
                    if (templates != null)
                    {
                        foreach (var template in templates)
                        {
                            if (!SavedTemplates.Any(t => t.Name.Equals(template.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                SavedTemplates.Add(template);
                                _settings.SavedTemplates.Add(template);
                            }
                        }
                    }
                }

                if (importData != null && importData.ContainsKey("CustomFields"))
                {
                    var fields = JsonSerializer.Deserialize<List<CustomField>>(importData["CustomFields"].GetRawText());
                    if (fields != null)
                    {
                        foreach (var field in fields)
                        {
                            if (!CustomFields.Any(f => f.Placeholder.Equals(field.Placeholder, StringComparison.OrdinalIgnoreCase)))
                            {
                                CustomFields.Add(field);
                                _settings.CustomFields.Add(field);
                            }
                        }
                    }
                }

                SaveSettings();
                TM.App.Log($"[LogFormat] 导入模板成功");
                GlobalToast.Success("导入成功", "已导入模板和自定义字段");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LogFormat] 导入模板失败: {ex.Message}");
                GlobalToast.Error("导入失败", $"无法导入模板: {ex.Message}");
            }
        }

        private void OnAllPropertiesChanged()
        {
            OnPropertyChanged(nameof(FormatTemplate));
            OnPropertyChanged(nameof(TimestampFormat));
            OnPropertyChanged(nameof(FieldSeparator));
            OnPropertyChanged(nameof(OutputFormat));
            OnPropertyChanged(nameof(EnableFieldAlignment));
            OnPropertyChanged(nameof(TimestampWidth));
            OnPropertyChanged(nameof(LevelWidth));
            OnPropertyChanged(nameof(EnableMultilineIndent));
            OnPropertyChanged(nameof(MultilineIndent));
        }
    }
}

