using System;

namespace TM.Framework.UI.Workspace.CenterPanel.ChapterEditor
{
    public class ContentModifiedEventArgs : EventArgs
    {
        public string Id { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}
