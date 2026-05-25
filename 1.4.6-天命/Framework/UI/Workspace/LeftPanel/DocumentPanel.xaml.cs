using System;
using System.Reflection;
using System.Collections.Generic;
using System.Windows.Controls;
using TM.Framework.Common.Services;
using TM.Framework.UI.Workspace.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Framework.UI.Workspace.LeftPanel
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class DocumentPanel : UserControl
    {
        private PanelCommunicationService? _comm;
        private PanelCommunicationService Comm => _comm ??= ServiceLocator.Get<PanelCommunicationService>();
        private GenerationGate? _generationGate;
        private GenerationGate GenerationGate => _generationGate ??= ServiceLocator.Get<GenerationGate>();
        private IGeneratedContentService? _contentService;
        private IGeneratedContentService ContentService => _contentService ??= ServiceLocator.Get<IGeneratedContentService>();

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
                if (!_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[DocumentPanel] {key}: {ex.Message}");
        }

        public DocumentPanel()
        {
            InitializeComponent();

            Comm.RefreshChapterListRequested += async () =>
            {
                await ChapterList.LoadChaptersAsync();
            };

            ChapterList.ChapterSelected += async (s, chapter) =>
            {
                var content = await LoadChapterContentAsync(chapter.Id);
                var raw = content ?? "";
                var protocol = GenerationGate.ValidateChangesProtocol(raw);
                var displayContent = protocol.ContentWithoutChanges ?? raw;
                Comm.PublishChapterSelected(chapter.Id, chapter.Title, displayContent);
            };

            ChapterList.ChapterDeleted += (s, chapterId) =>
            {
                Comm.PublishChapterDeleted(chapterId);
            };

            ChapterList.NewChapterRequested += (s, args) =>
            {
                Comm.PublishNewChapterRequested(args.ChapterId, args.ChapterTitle, args.InitialContent, true);
            };
        }

        private async System.Threading.Tasks.Task<string?> LoadChapterContentAsync(string chapterId)
        {
            try
            {
                return await ContentService.GetChapterAsync(chapterId);
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(LoadChapterContentAsync), ex);
                return null;
            }
        }

        public async System.Threading.Tasks.Task RefreshAsync()
        {
            await ChapterList.LoadChaptersAsync();
        }
    }
}
