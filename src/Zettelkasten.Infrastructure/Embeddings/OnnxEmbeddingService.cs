using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Search;

namespace Zettelkasten.Infrastructure.Embeddings;

/// <summary>all-MiniLM-L6-v2 via ONNX Runtime on CPU: mean pooling + L2 normalization.</summary>
public sealed class OnnxEmbeddingService(AppPaths paths, ILogger<OnnxEmbeddingService> logger)
    : IEmbeddingService, IDisposable
{
    private InferenceSession? _session;
    private WordPieceTokenizer? _tokenizer;
    private volatile bool _isAvailable;

    public bool IsAvailable => _isAvailable;
    public event EventHandler? AvailabilityChanged;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_isAvailable) return;
        try
        {
            await Task.Run(() =>
            {
                if (!File.Exists(paths.ModelPath) || !File.Exists(paths.VocabPath))
                    throw new FileNotFoundException($"Embedding model not found in {paths.ModelDirectory}");
                _tokenizer = WordPieceTokenizer.FromFile(paths.VocabPath);
                _session = new InferenceSession(paths.ModelPath);
                var probe = EmbedCore("warm up");
                if (probe.Length != SemanticSettings.Dimensions)
                    throw new InvalidDataException($"Model returned {probe.Length} dimensions, expected {SemanticSettings.Dimensions}.");
            }, ct);
            _isAvailable = true;
            logger.LogInformation("Embedding model loaded from {Path}", paths.ModelPath);
            AvailabilityChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _session?.Dispose();
            _session = null;
            logger.LogWarning(ex, "Semantic search unavailable");
        }
    }

    public float[] Embed(string text)
    {
        if (!_isAvailable) throw new InvalidOperationException("Embedding model is not available.");
        return EmbedCore(text);
    }

    private float[] EmbedCore(string text)
    {
        var ids = _tokenizer!.Encode(text);
        var n = ids.Length;
        int[] shape = [1, n];
        var mask = new long[n];
        Array.Fill(mask, 1L);

        var inputs = new List<NamedOnnxValue>();
        foreach (var name in _session!.InputMetadata.Keys)
        {
            inputs.Add(name switch
            {
                "input_ids" => NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(ids, shape)),
                "attention_mask" => NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(mask, shape)),
                "token_type_ids" => NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(new long[n], shape)),
                _ => throw new InvalidDataException($"Unexpected model input '{name}'."),
            });
        }

        using var results = _session.Run(inputs);
        var output = results.FirstOrDefault(r => r.Name == "last_hidden_state") ?? results.First();
        var hidden = output.AsTensor<float>(); // [1, tokens, dims]
        var dims = hidden.Dimensions[2];

        var v = new float[dims];
        for (var t = 0; t < n; t++)
            for (var d = 0; d < dims; d++)
                v[d] += hidden[0, t, d];
        for (var d = 0; d < dims; d++) v[d] /= n;
        VectorMath.Normalize(v);
        return v;
    }

    public void Dispose() => _session?.Dispose();
}
