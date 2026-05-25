using System;

namespace TM.Services.Framework.AI.SemanticKernel.Embedding
{
    public interface ITextEmbedder : IDisposable
    {
        float[] Embed(string text);

        double Similarity(float[] a, float[] b);
    }
}
