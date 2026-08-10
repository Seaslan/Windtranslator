using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Tokenizers.DotNet;

namespace Windtranslator.Services;

public sealed class LocalT5TranslationService : IDisposable
{
    private const int DecoderStartTokenId = 0;
    private const int EndOfSentenceTokenId = 1;
    private const int VocabularySize = 65100;
    private const int MaxOutputTokens = 1024;
    private const float RepetitionPenalty = 5.0f;

    private static readonly string[] RequiredModelFiles =
    {
        "config.json",
        "encoder_model.onnx",
        "decoder_model.onnx",
        "tokenizer.json",
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private InferenceSession? _encoderSession;
    private InferenceSession? _decoderSession;
    private Tokenizer? _tokenizer;
    private string? _modelPath;

    public async Task<string> TranslateAsync(
        string modelPath,
        string targetLanguage,
        string sourceText,
        CancellationToken cancellationToken)
    {
        ValidateModel(modelPath);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureModel(modelPath);
            return await Task.Run(
                () => TranslateCore(targetLanguage, sourceText, cancellationToken),
                CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        DisposeModel();
        _gate.Dispose();
    }

    private void EnsureModel(string modelPath)
    {
        if (!Environment.Is64BitProcess)
        {
            throw new InvalidOperationException("本地 ONNX 翻译仅支持 x64 或 ARM64 发布版本。请使用 x64 平台生成应用。");
        }

        var normalizedPath = Path.GetFullPath(modelPath);
        if (_encoderSession is not null
            && _decoderSession is not null
            && _tokenizer is not null
            && string.Equals(_modelPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DisposeModel();
        try
        {
            _tokenizer = new Tokenizer(vocabPath: Path.Combine(normalizedPath, "tokenizer.json"));
            _encoderSession = new InferenceSession(Path.Combine(normalizedPath, "encoder_model.onnx"));
            _decoderSession = new InferenceSession(Path.Combine(normalizedPath, "decoder_model.onnx"));
            _modelPath = normalizedPath;
        }
        catch (Exception ex)
        {
            DisposeModel();
            throw new InvalidOperationException(
                "无法加载本地 ONNX 翻译模型：" + ex.GetBaseException().Message,
                ex);
        }
    }

    private string TranslateCore(string targetLanguage, string sourceText, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var prompt = $"translate to {targetLanguage}: {sourceText}";
        var encoderTokens = _tokenizer!
            .Encode(prompt)
            .Select(id => (long)id)
            .ToArray();
        if (encoderTokens.Length == 0)
        {
            throw new InvalidOperationException("本地模型无法处理空输入。");
        }

        var encoderInputIds = CreateLongTensor(encoderTokens);
        var encoderAttentionMask = CreateLongTensor(Enumerable.Repeat(1L, encoderTokens.Length).ToArray());
        using var encoderResults = _encoderSession!.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("input_ids", encoderInputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", encoderAttentionMask),
        });
        var encoderHiddenStates = encoderResults
            .First(result => result.Name == "last_hidden_state")
            .AsTensor<float>();

        var decoderTokens = new List<long> { DecoderStartTokenId };
        var emittedTokens = new HashSet<int>();
        for (var generatedCount = 0; generatedCount < MaxOutputTokens; generatedCount++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var decoderInputIds = CreateLongTensor(decoderTokens.ToArray());
            using var decoderResults = _decoderSession!.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", encoderAttentionMask),
                NamedOnnxValue.CreateFromTensor("input_ids", decoderInputIds),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderHiddenStates),
            });
            var logits = decoderResults
                .First(result => result.Name == "logits")
                .AsTensor<float>();
            var nextToken = FindNextToken(logits, decoderTokens.Count - 1, emittedTokens);

            if (nextToken == EndOfSentenceTokenId || nextToken == DecoderStartTokenId)
            {
                break;
            }

            decoderTokens.Add(nextToken);
            emittedTokens.Add(nextToken);
        }

        var translatedTokens = decoderTokens
            .Skip(1)
            .Where(id => id != EndOfSentenceTokenId && id != DecoderStartTokenId)
            .Select(id => (uint)id)
            .ToArray();
        var translatedText = _tokenizer.Decode(translatedTokens).Trim();
        if (string.IsNullOrWhiteSpace(translatedText))
        {
            throw new InvalidOperationException("本地模型没有生成可用的译文。");
        }

        return translatedText;
    }

    private static DenseTensor<long> CreateLongTensor(long[] values)
    {
        var tensor = new DenseTensor<long>(new[] { 1, values.Length });
        for (var index = 0; index < values.Length; index++)
        {
            tensor[0, index] = values[index];
        }

        return tensor;
    }

    private static int FindNextToken(Tensor<float> logits, int position, HashSet<int> emittedTokens)
    {
        var selectedToken = EndOfSentenceTokenId;
        var highestScore = float.NegativeInfinity;
        for (var tokenId = 0; tokenId < VocabularySize; tokenId++)
        {
            var score = logits[0, position, tokenId];
            if (emittedTokens.Contains(tokenId))
            {
                score = score < 0 ? score * RepetitionPenalty : score / RepetitionPenalty;
            }

            if (score > highestScore)
            {
                highestScore = score;
                selectedToken = tokenId;
            }
        }

        return selectedToken;
    }

    private static void ValidateModel(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !Directory.Exists(modelPath))
        {
            throw new InvalidOperationException("本地模型文件夹不存在。请选择导出的 ONNX 模型文件夹。");
        }

        foreach (var file in RequiredModelFiles)
        {
            if (!File.Exists(Path.Combine(modelPath, file)))
            {
                throw new InvalidOperationException("所选文件夹不是支持的 ONNX T5 翻译模型，缺少 " + file + "。");
            }
        }
    }

    private void DisposeModel()
    {
        _encoderSession?.Dispose();
        _decoderSession?.Dispose();
        _tokenizer?.Dispose();
        _encoderSession = null;
        _decoderSession = null;
        _tokenizer = null;
        _modelPath = null;
    }
}
