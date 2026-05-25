using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TM.Framework.UI.Workspace
{
    public partial class WorkspaceLayout : UserControl
    {
        private static readonly object _debugLogLock = new();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new();

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

            Debug.WriteLine($"[WorkspaceLayout] {key}: {ex.Message}");
        }

        private void RestoreProportions()
        {
            try
            {
                if (MainGrid == null)
                {
                    return;
                }

                const double leftDefault = 300d;
                const double rightDefault = 300d;

                LeftWorkColumn.Width = new GridLength(leftDefault, GridUnitType.Pixel);
                RightWorkColumn.Width = new GridLength(rightDefault, GridUnitType.Pixel);

                CenterWorkColumn.Width = new GridLength(1, GridUnitType.Star);

                _isLeftWorkVisible = true;
                _isRightWorkVisible = true;
                _leftWorkOriginalWidth = LeftWorkColumn.Width;
                _rightWorkOriginalWidth = RightWorkColumn.Width;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(RestoreProportions), ex);
            }
        }

        public void ToggleLeftWorkspace()
        {
            _isLeftWorkVisible = !_isLeftWorkVisible;

            if (_isLeftWorkVisible)
            {
                LeftWorkColumn.Width = _leftWorkOriginalWidth;
                LeftSplitterColumn.Width = new GridLength(5);
            }
            else
            {
                _leftWorkOriginalWidth = LeftWorkColumn.Width;
                LeftWorkColumn.Width = new GridLength(0);
                LeftSplitterColumn.Width = new GridLength(0);
            }

            _settings.Set("workspace/left_visible", _isLeftWorkVisible);
        }

        public void ToggleRightWorkspace()
        {
            _isRightWorkVisible = !_isRightWorkVisible;

            if (_isRightWorkVisible)
            {
                RightWorkColumn.Width = _rightWorkOriginalWidth;
                RightSplitterColumn.Width = new GridLength(5);
            }
            else
            {
                _rightWorkOriginalWidth = RightWorkColumn.Width;
                RightWorkColumn.Width = new GridLength(0);
                RightSplitterColumn.Width = new GridLength(0);
            }

            _settings.Set("workspace/right_visible", _isRightWorkVisible);
        }

        public (bool left, bool right) GetWorkspaceVisibility()
        {
            return (_isLeftWorkVisible, _isRightWorkVisible);
        }

    }
}
