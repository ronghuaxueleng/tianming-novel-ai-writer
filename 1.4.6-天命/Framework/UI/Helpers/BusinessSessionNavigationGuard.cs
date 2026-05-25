using System;
using TM.Framework.Common.Controls.Dialogs;
using TM.Services.Framework.AI.Core;

namespace TM.Framework.UI.Helpers
{
    public static class BusinessSessionNavigationGuard
    {
        public static bool TryConfirmAndEndDirtyBusinessSession(string? currentBusinessKey)
        {
            try
            {
                var ai = ServiceLocator.Get<AIService>();

                var key = currentBusinessKey ?? string.Empty;

                var hasDirty = false;
                if (!string.IsNullOrWhiteSpace(key))
                {
                    hasDirty = ai.HasDirtyBusinessSession(key);
                    if (!hasDirty && ai.TryGetDirtyBusinessSessionKey(out var dirtyKey))
                    {
                        key = dirtyKey;
                        hasDirty = true;
                    }
                }
                else
                {
                    hasDirty = ai.TryGetDirtyBusinessSessionKey(out key);
                }

                if (!hasDirty)
                {
                    return true;
                }

                var confirmed = StandardDialog.ShowConfirm(
                    "当前业务存在未保存的AI生成会话。切换业务将结束本次会话，可能导致后续生成的连贯性下降。\n\n是否仍要切换？",
                    "切换业务确认");
                if (confirmed != true)
                {
                    return false;
                }

                var underscoreIdx = key.IndexOf('_');
                var clearPrefix = underscoreIdx > 0 ? key[..underscoreIdx] : key;
                ai.EndBusinessSessionsByPrefix(clearPrefix);
                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BusinessSessionNavigationGuard] 拦截异常: {ex.Message}");
                return true;
            }
        }
    }
}
