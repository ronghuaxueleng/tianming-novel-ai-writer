using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TM.Services.Framework.AI.SemanticKernel.Embedding
{
    public class OnnxTextEmbedder : ITextEmbedder
    {
        private readonly InferenceSession _session;
        private readonly Dictionary<string, int> _vocab;
        private readonly int _maxSequenceLength;
        private bool _disposed;

        private const string ClsToken = "[CLS]";
        private const string SepToken = "[SEP]";
        private const string UnkToken = "[UNK]";
        private const string PadToken = "[PAD]";

        private readonly int _clsId;
        private readonly int _sepId;
        private readonly int _unkId;
        private readonly int _padId;

        public OnnxTextEmbedder(string modelPath, string vocabPath, int maxSequenceLength = 512)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"ONNX 模型文件未找到: {modelPath}");
            if (!File.Exists(vocabPath))
                throw new FileNotFoundException($"词汇表文件未找到: {vocabPath}");

            _maxSequenceLength = maxSequenceLength;

            _vocab = LoadVocab(vocabPath);
            _clsId = _vocab.GetValueOrDefault(ClsToken, 101);
            _sepId = _vocab.GetValueOrDefault(SepToken, 102);
            _unkId = _vocab.GetValueOrDefault(UnkToken, 100);
            _padId = _vocab.GetValueOrDefault(PadToken, 0);

            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                InterOpNumThreads = 1,
                IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2)
            };
            _session = new InferenceSession(modelPath, options);
        }

        public float[] Embed(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<float>();

            var tokenIds = Tokenize(text);

            var inputLength = tokenIds.Count;
            var inputIds = new DenseTensor<long>(new[] { 1, inputLength });
            var attentionMask = new DenseTensor<long>(new[] { 1, inputLength });
            var tokenTypeIds = new DenseTensor<long>(new[] { 1, inputLength });

            for (int i = 0; i < inputLength; i++)
            {
                inputIds[0, i] = tokenIds[i];
                attentionMask[0, i] = 1;
                tokenTypeIds[0, i] = 0;
            }

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
                NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds)
            };

            using var results = _session.Run(inputs);

            var output = results.First();
            var outputTensor = output.AsTensor<float>();
            var shape = outputTensor.Dimensions.ToArray();
            var seqLen = shape[1];
            var hiddenSize = shape[2];

            var embedding = new float[hiddenSize];
            int validTokens = inputLength;

            for (int i = 0; i < seqLen && i < inputLength; i++)
            {
                for (int j = 0; j < hiddenSize; j++)
                {
                    embedding[j] += outputTensor[0, i, j];
                }
            }

            for (int j = 0; j < hiddenSize; j++)
            {
                embedding[j] /= validTokens;
            }

            Normalize(embedding);

            return embedding;
        }

        public double Similarity(float[] a, float[] b)
        {
            return CosineSimilarity(a, b);
        }

        public static double CosineSimilarity(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length == 0 || b.Length == 0 || a.Length != b.Length)
                return 0.0;

            double dot = 0, normA = 0, normB = 0;

            int i = 0;
            int simdLength = Vector<float>.Count;
            int alignedLength = a.Length - (a.Length % simdLength);

            var vDot = Vector<float>.Zero;
            var vNormA = Vector<float>.Zero;
            var vNormB = Vector<float>.Zero;

            for (; i < alignedLength; i += simdLength)
            {
                var va = new Vector<float>(a, i);
                var vb = new Vector<float>(b, i);
                vDot += va * vb;
                vNormA += va * va;
                vNormB += vb * vb;
            }

            for (int k = 0; k < simdLength; k++)
            {
                dot += vDot[k];
                normA += vNormA[k];
                normB += vNormB[k];
            }

            for (; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }

            var denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
            return denominator < 1e-10 ? 0.0 : dot / denominator;
        }

        #region BERT WordPiece 分词

        private List<int> Tokenize(string text)
        {
            var tokens = new List<int> { _clsId };

            text = text.ToLowerInvariant().Trim();

            var subTokens = PreTokenize(text);

            foreach (var token in subTokens)
            {
                var wordPieceIds = WordPieceTokenize(token);
                tokens.AddRange(wordPieceIds);

                if (tokens.Count >= _maxSequenceLength - 1)
                {
                    tokens = tokens.Take(_maxSequenceLength - 1).ToList();
                    break;
                }
            }

            tokens.Add(_sepId);
            return tokens;
        }

        private static List<string> PreTokenize(string text)
        {
            var result = new List<string>();
            var current = new System.Text.StringBuilder();

            foreach (var ch in text)
            {
                if (IsChineseChar(ch))
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                    result.Add(ch.ToString());
                }
                else if (char.IsWhiteSpace(ch) || IsPunctuation(ch))
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                    if (IsPunctuation(ch))
                        result.Add(ch.ToString());
                }
                else
                {
                    current.Append(ch);
                }
            }

            if (current.Length > 0)
                result.Add(current.ToString());

            return result;
        }

        private List<int> WordPieceTokenize(string token)
        {
            var ids = new List<int>();

            if (_vocab.TryGetValue(token, out var directId))
            {
                ids.Add(directId);
                return ids;
            }

            int start = 0;
            while (start < token.Length)
            {
                int end = token.Length;
                bool found = false;

                while (start < end)
                {
                    var subStr = token.Substring(start, end - start);
                    if (start > 0)
                        subStr = "##" + subStr;

                    if (_vocab.TryGetValue(subStr, out var subId))
                    {
                        ids.Add(subId);
                        found = true;
                        start = end;
                        break;
                    }

                    end--;
                }

                if (!found)
                {
                    ids.Add(_unkId);
                    start++;
                }
            }

            return ids;
        }

        private static bool IsChineseChar(char ch)
        {
            return (ch >= 0x4E00 && ch <= 0x9FFF) ||
                   (ch >= 0x3400 && ch <= 0x4DBF) ||
                   (ch >= 0x20000 && ch <= 0x2A6DF) ||
                   (ch >= 0xF900 && ch <= 0xFAFF) ||
                   (ch >= 0x2F800 && ch <= 0x2FA1F);
        }

        private static bool IsPunctuation(char ch)
        {
            return char.IsPunctuation(ch) || char.IsSymbol(ch);
        }

        #endregion

        #region 辅助方法

        private static Dictionary<string, int> LoadVocab(string vocabPath)
        {
            var vocab = new Dictionary<string, int>();
            var lines = File.ReadAllLines(vocabPath);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (!string.IsNullOrEmpty(line))
                    vocab[line] = i;
            }
            return vocab;
        }

        private static void Normalize(float[] vector)
        {
            double norm = 0;
            for (int i = 0; i < vector.Length; i++)
                norm += vector[i] * (double)vector[i];
            norm = Math.Sqrt(norm);

            if (norm < 1e-10) return;

            for (int i = 0; i < vector.Length; i++)
                vector[i] = (float)(vector[i] / norm);
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session?.Dispose();
        }
    }
}
