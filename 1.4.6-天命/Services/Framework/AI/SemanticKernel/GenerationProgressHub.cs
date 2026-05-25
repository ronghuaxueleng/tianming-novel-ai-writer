using System;
using System.Collections.Concurrent;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public static class GenerationProgressHub
    {
        public static event Action<string>? ProgressReported;

        public static void Report(string message)
        {
            ProgressReported?.Invoke(message);
        }
    }
}
