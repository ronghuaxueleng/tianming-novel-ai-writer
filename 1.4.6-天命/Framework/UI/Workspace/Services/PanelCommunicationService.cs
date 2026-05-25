using System;
using System.Collections.Generic;

namespace TM.Framework.UI.Workspace.Services
{
    public class PanelCommunicationService
    {
        public PanelCommunicationService() { }

        public event Action<string, string, string>? ChapterSelected;

        public event Action<string>? ChapterDeleted;

        public event Action<string, string, string, bool>? NewChapterRequested;

        public event Action? RefreshChapterListRequested;

        public event Action<string, string, string>? ContentGenerated;

        public event Action<bool>? ShowPlanViewChanged;

        public event Action<string, string, string>? ShowDiffRequested;

        public event Action<Guid, Guid?>? HighlightExecutionRequested;

        public event Action<string>? SendMessageRequested;

        public event Action<IReadOnlyList<(int Index, string Title, string Detail)>>? StartPlanExecutionRequested;

        public event Action<string>? ChapterNavigationRequested;

        public event Action? ClearMessageSelectionRequested;

        public event Action<string>? ModuleNavigationRequested;

        public event Action<string, string, Type>? FunctionNavigationRequested;

        public event Action? NewChapterFromHomepageRequested;

        public event EventHandler? BusinessDataCleared;

        public void PublishChapterSelected(string id, string title, string content)
            => ChapterSelected?.Invoke(id, title, content);

        public void PublishChapterDeleted(string id)
            => ChapterDeleted?.Invoke(id);

        public void PublishNewChapterRequested(string chapterId, string chapterTitle, string initialContent, bool isNew)
            => NewChapterRequested?.Invoke(chapterId, chapterTitle, initialContent, isNew);

        public void PublishRefreshChapterList()
            => RefreshChapterListRequested?.Invoke();

        public void PublishContentGenerated(string id, string title, string content)
            => ContentGenerated?.Invoke(id, title, content);

        public void PublishShowPlanViewChanged(bool show)
            => ShowPlanViewChanged?.Invoke(show);

        public void PublishShowDiff(string id, string original, string modified)
            => ShowDiffRequested?.Invoke(id, original, modified);

        public void PublishHighlightExecution(Guid runId, Guid? eventId)
            => HighlightExecutionRequested?.Invoke(runId, eventId);

        public void PublishSendMessage(string message)
            => SendMessageRequested?.Invoke(message);

        public void PublishStartPlanExecution(IReadOnlyList<(int Index, string Title, string Detail)> steps)
            => StartPlanExecutionRequested?.Invoke(steps);

        public void RequestChapterNavigation(string chapterId)
            => ChapterNavigationRequested?.Invoke(chapterId);

        public void RequestClearMessageSelection()
            => ClearMessageSelectionRequested?.Invoke();

        public void PublishModuleNavigationRequested(string moduleName)
            => ModuleNavigationRequested?.Invoke(moduleName);

        public void PublishFunctionNavigationRequested(string moduleName, string subModuleName, Type viewType)
            => FunctionNavigationRequested?.Invoke(moduleName, subModuleName, viewType);

        public void PublishNewChapterFromHomepage()
            => NewChapterFromHomepageRequested?.Invoke();

        public void PublishBusinessDataCleared()
            => BusinessDataCleared?.Invoke(this, EventArgs.Empty);

        public void ClearAllSubscriptions()
        {
            ChapterSelected = null;
            ChapterDeleted = null;
            NewChapterRequested = null;
            RefreshChapterListRequested = null;
            ContentGenerated = null;
            ShowPlanViewChanged = null;
            ShowDiffRequested = null;
            HighlightExecutionRequested = null;
            SendMessageRequested = null;
            StartPlanExecutionRequested = null;
            ChapterNavigationRequested = null;
            ClearMessageSelectionRequested = null;
            ModuleNavigationRequested = null;
            FunctionNavigationRequested = null;
            NewChapterFromHomepageRequested = null;
            BusinessDataCleared = null;
        }
    }
}
