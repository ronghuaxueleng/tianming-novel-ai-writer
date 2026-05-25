using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Id;
using System.Windows.Input;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;

namespace TM.Services.Framework.AI.SemanticKernel
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class UIMessageItem : INotifyPropertyChanged
    {
        private string _content = string.Empty;
        private string _thinkingContent = string.Empty;
        private bool _isThinking;
        private bool _isStreaming;
        private int _thinkingChapterCount;
        private readonly StringBuilder _contentBuilder = new();
        private readonly StringBuilder _thinkingBuilder = new();
        private DispatcherTimer? _thinkingTimer;
        private DateTime _thinkingStartTime;

        private Queue<char>? _charQueue;
        private readonly StringBuilder _displayedBuilder = new();
        private DispatcherTimer? _smoothTimer;
        private int _streamingTokenEstimate;
        private int _lastTokenEstimateLength;

        private string? _oldChapterContent;
        private string? _newChapterContent;
        private bool _isError;
        private bool _isStarred;
        private bool _isThinkingExpanded;
        private IReadOnlyList<ThinkingBlock> _thinkingBlocks = Array.Empty<ThinkingBlock>();
        private string? _analysisSummary;
        private double? _analysisDurationSeconds;

        private string? _changesJson;
        private IReadOnlyList<ThinkingBlock> _changesBlocks = Array.Empty<ThinkingBlock>();
        private bool _isChangesProcessing;
        private bool _isChangesExpanded;
        private string? _changesSummary;
        private double? _changesDurationSeconds;

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (_debugLoggedKeys.Count >= 500 || !_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[UIMessageItem] {key}: {ex.Message}");
        }

        public UIMessageItem()
        {
            ThinkingPanelBindings = new ThinkingPanelBindingsAdapter(this);
            ChangesPanelBindings = new ChangesPanelBindingsAdapter(this);
        }

        public string MessageId { get; set; } = ShortIdGenerator.NewGuid().ToString("N");

        public string Id => MessageId.Length >= 8 ? MessageId[..8] : MessageId;

        public AuthorRole Role { get; set; }

        public bool IsUser => Role == AuthorRole.User;

        public bool IsAssistant => Role == AuthorRole.Assistant;

        public bool IsSystem => Role == AuthorRole.System;

        public string Content
        {
            get => _content;
            set
            {
                _content = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTypingPlaceholder));
            }
        }

        public string? ChangesJson
        {
            get => _changesJson;
            set
            {
                if (_changesJson != value)
                {
                    _changesJson = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasChanges));
                    UpdateChangesBlocks();

                    if (!string.IsNullOrWhiteSpace(_changesJson) && string.IsNullOrWhiteSpace(_changesSummary))
                    {
                        ChangesSummary = "View CHANGES";
                    }
                }
            }
        }

        public bool HasChanges => !string.IsNullOrWhiteSpace(_changesJson);

        public IReadOnlyList<ThinkingBlock> ChangesBlocks
        {
            get => _changesBlocks;
            private set
            {
                _changesBlocks = value;
                OnPropertyChanged();
            }
        }

        public bool IsChangesProcessing
        {
            get => _isChangesProcessing;
            set
            {
                if (_isChangesProcessing != value)
                {
                    _isChangesProcessing = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsChangesExpanded
        {
            get => _isChangesExpanded;
            set
            {
                if (_isChangesExpanded != value)
                {
                    _isChangesExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        private ICommand? _toggleChangesExpandedCommand;
        public ICommand ToggleChangesExpandedCommand =>
            _toggleChangesExpandedCommand ??= new RelayCommand(() => IsChangesExpanded = !IsChangesExpanded);

        public string? ChangesSummary
        {
            get => _changesSummary;
            set
            {
                if (_changesSummary != value)
                {
                    _changesSummary = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasChangesSummary));
                }
            }
        }

        public bool HasChangesSummary => !string.IsNullOrWhiteSpace(_changesSummary);

        public double? ChangesDurationSeconds
        {
            get => _changesDurationSeconds;
            set
            {
                if (_changesDurationSeconds != value)
                {
                    _changesDurationSeconds = value;
                    OnPropertyChanged();
                }
            }
        }

        public CollapsiblePanelBindings ThinkingPanelBindings { get; }

        public CollapsiblePanelBindings ChangesPanelBindings { get; }

        public string ThinkingContent
        {
            get => _thinkingContent;
            set
            {
                _thinkingContent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasThinking));
                UpdateThinkingBlocks();
            }
        }

        public bool HasThinking => !string.IsNullOrEmpty(_thinkingContent);

        public string? AnalysisSummary
        {
            get => _analysisSummary;
            set
            {
                if (_analysisSummary != value)
                {
                    _analysisSummary = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasAnalysisSummary));
                }
            }
        }

        public bool HasAnalysisSummary => !string.IsNullOrWhiteSpace(_analysisSummary);

        public double? AnalysisDurationSeconds
        {
            get => _analysisDurationSeconds;
            set
            {
                if (_analysisDurationSeconds != value)
                {
                    _analysisDurationSeconds = value;
                    OnPropertyChanged();
                }
            }
        }

        public IReadOnlyList<ThinkingBlock> ThinkingBlocks
        {
            get => _thinkingBlocks;
            private set
            {
                _thinkingBlocks = value;
                OnPropertyChanged();
            }
        }

        public bool IsThinking
        {
            get => _isThinking;
            set
            {
                _isThinking = value;
                OnPropertyChanged();

                if (value)
                {
                    IsThinkingExpanded = true;
                    StartThinkingTimer();
                }
                else
                {
                    IsThinkingExpanded = false;
                    StopThinkingTimer();
                }
            }
        }

        public DateTime ThinkingStartTime => _thinkingStartTime;

        private void StartThinkingTimer()
        {
            _thinkingStartTime = DateTime.Now;
            if (_thinkingTimer == null)
            {
                _thinkingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _thinkingTimer.Tick += (_, _) =>
                {
                    var elapsed = (DateTime.Now - _thinkingStartTime).TotalSeconds;
                    AnalysisSummary = $"Thinking {elapsed:F1} s";
                };
            }
            _thinkingTimer.Start();
        }

        private void TruncateThinkingToLastLines(int keepLines)
        {
            var content = _thinkingBuilder.ToString();
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > keepLines)
            {
                _thinkingBuilder.Clear();
                var start = lines.Length - keepLines;
                for (var i = start; i < lines.Length; i++)
                {
                    _thinkingBuilder.Append(lines[i]);
                    _thinkingBuilder.Append('\n');
                }
            }
        }

        private void StopThinkingTimer()
        {
            _thinkingTimer?.Stop();
        }

        public bool IsStreaming
        {
            get => _isStreaming;
            set
            {
                _isStreaming = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTypingPlaceholder));
            }
        }

        public bool IsError
        {
            get => _isError;
            set { _isError = value; OnPropertyChanged(); }
        }

        public bool IsStarred
        {
            get => _isStarred;
            set { _isStarred = value; OnPropertyChanged(); }
        }

        public bool IsTypingPlaceholder => IsAssistant && IsStreaming && string.IsNullOrEmpty(Content);

        public bool IsThinkingExpanded
        {
            get => _isThinkingExpanded;
            set
            {
                if (_isThinkingExpanded != value)
                {
                    _isThinkingExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        private ICommand? _toggleThinkingExpandedCommand;
        public ICommand ToggleThinkingExpandedCommand =>
            _toggleThinkingExpandedCommand ??= new RelayCommand(() => IsThinkingExpanded = !IsThinkingExpanded);

        public DateTime Timestamp { get; set; } = DateTime.Now;

        public string? ModelName { get; set; }

        public int TokenCount { get; set; }

        public IReadOnlyList<SearchResult>? References { get; set; }

        public bool HasReferences => References != null && References.Count > 0;

        public int StreamingTokenEstimate
        {
            get => _streamingTokenEstimate;
            private set
            {
                if (_streamingTokenEstimate != value)
                {
                    _streamingTokenEstimate = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StreamingTokenEstimateText));
                }
            }
        }

        public string StreamingTokenEstimateText =>
            _streamingTokenEstimate > 0 ? $"~{_streamingTokenEstimate} tokens" : string.Empty;

        public Guid RunId { get; set; }

        #region Payload 持久化

        public PayloadType PayloadType { get; set; } = PayloadType.None;

        public string? PayloadJson { get; set; }

        public ConversationMessage? SourceMessage { get; private set; }

        public void ApplyFromConversationMessage(ConversationMessage msg)
        {
            SourceMessage = msg;

            Content = msg.Summary;
            ThinkingContent = msg.AnalysisRaw;

            if (msg.Payload != null)
            {
                PayloadType = msg.Payload.Type;
                PayloadJson = JsonSerializer.Serialize(msg.Payload, JsonHelper.Compact);
            }
        }

        public ConversationMessage ToConversationMessage()
        {
            if (SourceMessage != null)
                return SourceMessage;

            return new ConversationMessage
            {
                Role = Role,
                Timestamp = Timestamp,
                Summary = Content,
                AnalysisRaw = ThinkingContent,
                AnalysisBlocks = ThinkingBlocks,
                Payload = RestorePayload()
            };
        }

        public MessagePayload? RestorePayload()
        {
            if (string.IsNullOrEmpty(PayloadJson) || PayloadType == PayloadType.None)
                return null;

            try
            {
                return PayloadType switch
                {
                    PayloadType.Plan => JsonSerializer.Deserialize<PlanPayload>(PayloadJson),
                    PayloadType.AgentExecution => JsonSerializer.Deserialize<AgentPayload>(PayloadJson),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIMessageItem] Payload 反序列化失败: {ex.Message}");
                return null;
            }
        }

        public bool HasPayload => PayloadType != PayloadType.None && !string.IsNullOrEmpty(PayloadJson);

        #endregion

        #region 工厂方法

        public static UIMessageItem FromChatMessageContent(ChatMessageContent message)
        {
            var item = new UIMessageItem
            {
                Role = message.Role,
                Content = message.Content ?? string.Empty,
                ModelName = message.ModelId
            };

            if (message.Metadata?.TryGetValue("Thinking", out var thinking) == true)
            {
                item.ThinkingContent = thinking?.ToString() ?? string.Empty;
            }

            if (message.Metadata?.TryGetValue("Usage", out var usage) == true)
            {
            }

            return item;
        }

        public static UIMessageItem CreateUserMessage(string content)
        {
            return new UIMessageItem
            {
                Role = AuthorRole.User,
                Content = content
            };
        }

        public static UIMessageItem CreateAssistantPlaceholder()
        {
            return new UIMessageItem
            {
                Role = AuthorRole.Assistant,
                Content = string.Empty,
                IsStreaming = true
            };
        }

        public static UIMessageItem CreateErrorMessage(string error)
        {
            return new UIMessageItem
            {
                Role = AuthorRole.Assistant,
                Content = error,
                IsError = true
            };
        }

        public static UIMessageItem CreateSystemMessage(string content)
        {
            return new UIMessageItem
            {
                Role = AuthorRole.System,
                Content = content
            };
        }

        #endregion

        #region 流式更新

        public void AppendContent(string chunk)
        {
            if (IsThinking && _contentBuilder.Length == 0)
            {
                if (HasThinking)
                {
                    var elapsed = Math.Max(0.1, (DateTime.Now - _thinkingStartTime).TotalSeconds);
                    AnalysisDurationSeconds = elapsed;
                    AnalysisSummary = $"Thought for {elapsed:F1} s";
                }
                IsThinking = false;
            }

            _contentBuilder.Append(chunk);

            if (_contentBuilder.Length - _lastTokenEstimateLength >= 200)
            {
                _lastTokenEstimateLength = _contentBuilder.Length;
                StreamingTokenEstimate = EstimateTokensFromLength(_contentBuilder.Length);
            }

            _charQueue ??= new Queue<char>();
            foreach (var c in chunk)
                _charQueue.Enqueue(c);
            EnsureSmoothTimerRunning();
        }

        private void EnsureSmoothTimerRunning()
        {
            if (_smoothTimer == null)
            {
                _smoothTimer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                _smoothTimer.Tick += OnSmoothTimerTick;
            }
            if (!_smoothTimer.IsEnabled)
                _smoothTimer.Start();
        }

        private void OnSmoothTimerTick(object? sender, EventArgs e)
        {
            if (_charQueue == null || _charQueue.Count == 0)
            {
                _smoothTimer?.Stop();
                return;
            }

            var count = Math.Max(1, _charQueue.Count / 5);
            for (int i = 0; i < count && _charQueue.Count > 0; i++)
                _displayedBuilder.Append(_charQueue.Dequeue());

            Content = _displayedBuilder.ToString();
        }

        private void FlushSmoothQueue()
        {
            _smoothTimer?.Stop();
            _smoothTimer = null;

            if (_charQueue != null && _charQueue.Count > 0)
            {
                Content = _contentBuilder.ToString();
            }
            _charQueue = null;
            _displayedBuilder.Clear();
        }

        public void AppendThinking(string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return;
            }

            _thinkingBuilder.Append(chunk);

            if (chunk.Contains("✓ 章节 ", StringComparison.Ordinal)
                && chunk.Contains(" 生成完成", StringComparison.Ordinal)
                && ++_thinkingChapterCount % 3 == 0)
                TruncateThinkingToLastLines(20);

            _thinkingContent = _thinkingBuilder.ToString();
            OnPropertyChanged(nameof(ThinkingContent));
            OnPropertyChanged(nameof(HasThinking));
            UpdateThinkingBlocks();
        }

        public void FinishStreaming()
        {
            FlushSmoothQueue();

            IsStreaming = false;
            IsThinking = false;

            if (!string.IsNullOrWhiteSpace(Content))
            {
                TokenCount = TM.Framework.Common.Helpers.TokenEstimator.CountTokens(Content);
            }
            else if (_streamingTokenEstimate > 0)
            {
                TokenCount = _streamingTokenEstimate;
            }

            if (!string.IsNullOrWhiteSpace(_thinkingContent))
                UpdateThinkingBlocks();

            _contentBuilder.Clear();
            _thinkingBuilder.Clear();
            _thinkingChapterCount = 0;
        }

        public string? OldChapterContent
        {
            get => _oldChapterContent;
            set { _oldChapterContent = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChapterDiff)); }
        }

        public string? NewChapterContent
        {
            get => _newChapterContent;
            set { _newChapterContent = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChapterDiff)); }
        }

        public bool HasChapterDiff =>
            !string.IsNullOrEmpty(_oldChapterContent) && !string.IsNullOrEmpty(_newChapterContent);

        #endregion

        private static int EstimateTokensFromLength(int charLength)
            => Math.Max(1, (int)(charLength * 0.7));

        private void UpdateThinkingBlocks()
        {
            if (string.IsNullOrWhiteSpace(_thinkingContent))
            {
                ThinkingBlocks = Array.Empty<ThinkingBlock>();
                return;
            }

            var blocks = new List<ThinkingBlock>();
            var currentLines = new List<string>();
            var normalized = _thinkingContent.Replace("\r\n", "\n");
            var lines = normalized.Split('\n');

            foreach (var raw in lines)
            {
                var line = raw;
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (currentLines.Count > 0)
                    {
                        AddBlockFromLines(currentLines, blocks);
                        currentLines.Clear();
                    }
                }
                else
                {
                    currentLines.Add(line);
                }
            }

            if (currentLines.Count > 0)
            {
                AddBlockFromLines(currentLines, blocks);
            }

            ThinkingBlocks = blocks;
        }

        private void UpdateChangesBlocks()
        {
            if (string.IsNullOrWhiteSpace(_changesJson))
            {
                ChangesBlocks = Array.Empty<ThinkingBlock>();
                return;
            }

            var pretty = TryFormatJson(_changesJson);
            ChangesBlocks = new[]
            {
                new ThinkingBlock
                {
                    Title = "CHANGES",
                    Body = pretty
                }
            };
        }

        private static string TryFormatJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(doc.RootElement, JsonHelper.Default);
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(TryFormatJson), ex);
                return json;
            }
        }

        private static void AddBlockFromLines(List<string> lines, List<ThinkingBlock> blocks)
        {
            if (lines.Count == 0) return;

            var first = lines[0].Trim();
            string title;
            string body;

            if (first.StartsWith("#"))
            {
                title = first.TrimStart('#', ' ').Trim();
                body = string.Join("\n", lines.GetRange(1, lines.Count - 1)).Trim();
            }
            else if (first.EndsWith(":"))
            {
                title = first.TrimEnd(':').Trim();
                body = string.Join("\n", lines.GetRange(1, lines.Count - 1)).Trim();
            }
            else
            {
                title = "Thinking";
                body = string.Join("\n", lines).Trim();
            }

            blocks.Add(new ThinkingBlock
            {
                Title = title,
                Body = body
            });
        }

        #region 三层序列化

        public SerializedMessageRecord ToSerializedRecord()
        {
            string? refsJson = null;
            if (HasReferences)
            {
                try { refsJson = JsonSerializer.Serialize(References, JsonHelper.Compact); }
                catch {}
            }

            return new SerializedMessageRecord
            {
                MessageId = MessageId,
                Role = Role.Label,
                Summary = Content,
                Analysis = string.IsNullOrWhiteSpace(ThinkingContent) ? null : ThinkingContent,
                DurationSeconds = AnalysisDurationSeconds,
                ChangesJson = string.IsNullOrWhiteSpace(ChangesJson) ? null : ChangesJson,
                ChangesDurationSeconds = ChangesDurationSeconds,
                PayloadType = (int)PayloadType,
                PayloadJson = PayloadJson,
                Timestamp = Timestamp,
                ReferencesJson = refsJson
            };
        }

        public static UIMessageItem FromSerializedRecord(SerializedMessageRecord record)
        {
            var role = record.Role.ToLower() switch
            {
                "system" => AuthorRole.System,
                "user" => AuthorRole.User,
                "assistant" => AuthorRole.Assistant,
                _ => AuthorRole.User
            };

            var item = new UIMessageItem
            {
                MessageId = record.MessageId,
                Role = role,
                Content = record.Summary,
                ThinkingContent = record.Analysis ?? string.Empty,
                ChangesJson = record.ChangesJson,
                Timestamp = record.Timestamp,
                PayloadType = (PayloadType)record.PayloadType,
                PayloadJson = record.PayloadJson
            };

            if (record.DurationSeconds.HasValue)
            {
                item.AnalysisDurationSeconds = record.DurationSeconds.Value;
                item.AnalysisSummary = $"Thought for {record.DurationSeconds.Value:F1} s";
            }
            else if (!string.IsNullOrEmpty(record.Analysis))
            {
                item.AnalysisSummary = "View analysis";
            }

            if (record.ChangesDurationSeconds.HasValue)
            {
                item.ChangesDurationSeconds = record.ChangesDurationSeconds.Value;
                item.ChangesSummary = $"CHANGES for {record.ChangesDurationSeconds.Value:F1} s";
            }
            else if (!string.IsNullOrWhiteSpace(record.ChangesJson))
            {
                item.ChangesSummary = "View CHANGES";
            }

            if (!string.IsNullOrWhiteSpace(record.ReferencesJson))
            {
                try
                {
                    var refs = JsonSerializer.Deserialize<List<SearchResult>>(record.ReferencesJson);
                    if (refs != null && refs.Count > 0)
                        item.References = refs;
                }
                catch {}
            }

            return item;
        }

        [Obfuscation(Exclude = true, ApplyToMembers = true)]
        public abstract class CollapsiblePanelBindings : INotifyPropertyChanged
        {
            protected UIMessageItem Owner { get; }

            protected CollapsiblePanelBindings(UIMessageItem owner)
            {
                Owner = owner;
                Owner.PropertyChanged += OnOwnerPropertyChanged;
            }

            public abstract string? Summary { get; }

            public abstract bool IsProcessing { get; }

            public abstract bool IsExpanded { get; set; }

            public abstract IReadOnlyList<ThinkingBlock> Blocks { get; }

            public abstract ICommand ToggleCommand { get; }

            public event PropertyChangedEventHandler? PropertyChanged;

            protected void RaisePropertyChanged([CallerMemberName] string? name = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }

            protected abstract void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e);
        }

        public sealed class ThinkingPanelBindingsAdapter : CollapsiblePanelBindings
        {
            public ThinkingPanelBindingsAdapter(UIMessageItem owner) : base(owner)
            {
            }

            public override string? Summary => Owner.AnalysisSummary;

            public override bool IsProcessing => Owner.IsThinking;

            public override bool IsExpanded
            {
                get => Owner.IsThinkingExpanded;
                set => Owner.IsThinkingExpanded = value;
            }

            public override IReadOnlyList<ThinkingBlock> Blocks => Owner.ThinkingBlocks;

            public override ICommand ToggleCommand => Owner.ToggleThinkingExpandedCommand;

            protected override void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(UIMessageItem.AnalysisSummary)) RaisePropertyChanged(nameof(Summary));
                if (e.PropertyName == nameof(UIMessageItem.IsThinking)) RaisePropertyChanged(nameof(IsProcessing));
                if (e.PropertyName == nameof(UIMessageItem.IsThinkingExpanded)) RaisePropertyChanged(nameof(IsExpanded));
                if (e.PropertyName == nameof(UIMessageItem.ThinkingBlocks)) RaisePropertyChanged(nameof(Blocks));
            }
        }

        public sealed class ChangesPanelBindingsAdapter : CollapsiblePanelBindings
        {
            public ChangesPanelBindingsAdapter(UIMessageItem owner) : base(owner)
            {
            }

            public override string? Summary => Owner.ChangesSummary;

            public override bool IsProcessing => Owner.IsChangesProcessing;

            public override bool IsExpanded
            {
                get => Owner.IsChangesExpanded;
                set => Owner.IsChangesExpanded = value;
            }

            public override IReadOnlyList<ThinkingBlock> Blocks => Owner.ChangesBlocks;

            public override ICommand ToggleCommand => Owner.ToggleChangesExpandedCommand;

            protected override void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(UIMessageItem.ChangesSummary)) RaisePropertyChanged(nameof(Summary));
                if (e.PropertyName == nameof(UIMessageItem.IsChangesProcessing)) RaisePropertyChanged(nameof(IsProcessing));
                if (e.PropertyName == nameof(UIMessageItem.IsChangesExpanded)) RaisePropertyChanged(nameof(IsExpanded));
                if (e.PropertyName == nameof(UIMessageItem.ChangesBlocks)) RaisePropertyChanged(nameof(Blocks));
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        #endregion
    }
}
