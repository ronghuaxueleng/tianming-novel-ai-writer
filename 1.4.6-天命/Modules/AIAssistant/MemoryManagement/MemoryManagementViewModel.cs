using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.MVVM;
using TM.Services.Framework.AI.SemanticKernel;

namespace TM.Modules.AIAssistant.MemoryManagement
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class MemoryManagementViewModel : INotifyPropertyChanged
    {
        private readonly SKChatService _chatService;
        private bool _isExtracting;
        private string _statusText = string.Empty;

        public MemoryManagementViewModel(SKChatService chatService)
        {
            _chatService = chatService;
            RefreshCommand = new RelayCommand(Refresh);
            ClearMemoryCommand = new RelayCommand(ClearMemory);
            ExtractWithLLMCommand = new AsyncRelayCommand(ExtractWithLLMAsync);
            Refresh();
        }

        public ICommand RefreshCommand { get; }
        public ICommand ClearMemoryCommand { get; }
        public ICommand ExtractWithLLMCommand { get; }

        private StructuredMemoryExtractor.StructuredMemory? _memory;

        public bool HasMemory => _memory != null && _chatService.HasMemory();

        public DateTime LastUpdated => _memory?.LastUpdated ?? DateTime.MinValue;

        public string LastUpdatedText => HasMemory
            ? _memory!.LastUpdated.ToString("MM-dd HH:mm:ss")
            : "暂无记忆数据";

        public IReadOnlyList<CharacterRow> Characters
        {
            get
            {
                if (_memory == null) return Array.Empty<CharacterRow>();
                return _memory.Characters
                    .Select(kv => new CharacterRow(kv.Key, kv.Value))
                    .ToList();
            }
        }

        public IReadOnlyList<string> PlotMilestones => _memory?.Plot.Milestones ?? new List<string>();
        public IReadOnlyList<string> ForeshadowingPending => _memory?.Plot.ForeshadowingPending ?? new List<string>();
        public IReadOnlyList<string> WorldRules => _memory?.World.Rules ?? new List<string>();
        public string? CurrentTask => _memory?.Task.Current;
        public IReadOnlyList<string> PendingTasks => _memory?.Task.Pending ?? new List<string>();

        public bool IsExtracting
        {
            get => _isExtracting;
            private set { _isExtracting = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(); }
        }

        private void Refresh()
        {
            _memory = _chatService.HasMemory() ? _chatService.GetMemory() : null;
            OnPropertyChanged(nameof(HasMemory));
            OnPropertyChanged(nameof(LastUpdatedText));
            OnPropertyChanged(nameof(Characters));
            OnPropertyChanged(nameof(PlotMilestones));
            OnPropertyChanged(nameof(ForeshadowingPending));
            OnPropertyChanged(nameof(WorldRules));
            OnPropertyChanged(nameof(CurrentTask));
            OnPropertyChanged(nameof(PendingTasks));
            StatusText = HasMemory ? $"已提取记忆，更新于 {LastUpdatedText}" : "当前会话暂无记忆数据";
        }

        private void ClearMemory()
        {
            _chatService.ClearMemory();
            Refresh();
            GlobalToast.Info("记忆已清空", "当前会话的结构化记忆已清除");
        }

        private async Task ExtractWithLLMAsync()
        {
            if (IsExtracting) return;
            IsExtracting = true;
            StatusText = "正在提取…";
            try
            {
                await _chatService.TriggerLLMMemoryExtractionAsync();
                Refresh();
                GlobalToast.Success("提取完成", "已通过 AI 深度提取结构化记忆");
            }
            catch (Exception ex)
            {
                StatusText = $"提取失败: {ex.Message}";
                GlobalToast.Error("提取失败", ex.Message);
            }
            finally
            {
                IsExtracting = false;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class CharacterRow
    {
        public string Name { get; }
        public string? Location { get; }
        public string? Emotion { get; }
        public string? Goal { get; }
        public string? Status { get; }

        public CharacterRow(string name, StructuredMemoryExtractor.CharacterState state)
        {
            Name = name;
            Location = state.Location;
            Emotion = state.Emotion;
            Goal = state.Goal;
            Status = state.Status;
        }
    }
}
