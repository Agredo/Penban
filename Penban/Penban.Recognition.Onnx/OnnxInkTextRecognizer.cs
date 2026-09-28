using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Penban.Models;
using TensorElementType = Microsoft.ML.OnnxRuntime.Tensors.TensorElementType;

namespace Penban.Recognition.Onnx;

/// <summary>
/// Recognises handwriting with a kraken PP-OCRv6 line recognizer exported to ONNX.
/// <para>
/// The whole pipeline sits behind <see cref="IInkTextRecognizer"/>: segment the card into lines,
/// render each line at the agreed fill ratio, run the graph, collapse the CTC output. Everything the
/// graph cannot say for itself - the input name, the output names, the codec, the blank - comes from
/// the catalog that ships with the model, never from a constant in this class.
/// </para>
/// <para>
/// The session is created once and reused. ONNX Runtime sessions are documented as safe for
/// concurrent <c>Run</c> calls, and the recognizer holds no per-call state, so
/// <see cref="RecognizeAsync"/> can be called from several cards at once. On a phone that is a way to
/// oversubscribe the CPU rather than to go faster, which is why the caller - the recognition queue -
/// is the place that decides how much runs in parallel.
/// </para>
/// </summary>
public sealed class OnnxInkTextRecognizer : IInkTextRecognizer, IDisposable
{
    /// <summary>
    /// The fill ratio the model was measured under. Phase 0 scored the same model and ink at 0.0707
    /// CER at r81 and clearly worse at r50, so this is a contract rather than a tuning knob: changing
    /// it changes the text, which is why it is part of <see cref="RecognizerProfile"/>.
    /// </summary>
    public const string DefaultRenderConvention = "r81";

    /// <summary>
    /// The model this assembly ships with, as named in the catalog. Kept in step with
    /// <see cref="RecognizerProfile.Ppocrv6SmallFp16"/> by deriving from it.
    /// </summary>
    public static string DefaultModelId => RecognizerProfile.Ppocrv6SmallFp16.ModelId;

    /// <summary>The logical name of the embedded catalog, see the project file.</summary>
    public const string CatalogResourceName = ResourcePrefix + "ppocrv6-catalog.json";

    /// <summary>Prefix every embedded model resource name shares.</summary>
    public const string ResourcePrefix = "Penban.Recognition.Onnx.Assets.";

    /// <summary>Channels in the model input. The renderer produces one plane; the graph takes three.</summary>
    private const int Channels = 3;

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _labelsName;
    private readonly string _confidencesName;
    private readonly string?[] _labelTable;

    private bool _disposed;

    private OnnxInkTextRecognizer(
        InferenceSession session,
        Ppocrv6Catalog catalog,
        string modelId,
        string renderConvention,
        string unicodeForm)
    {
        if (catalog.Input!.Height != InkLineRenderer.ModelHeight)
        {
            throw new InvalidDataException(
                $"Der Katalog verlangt {catalog.Input.Height} px Zeilenhoehe, der Renderer liefert " +
                $"{InkLineRenderer.ModelHeight} px. Katalog und Renderer passen nicht zusammen.");
        }

        _session = session;
        _inputName = catalog.Input.Name!;
        _labelsName = catalog.Output!.Labels!;
        _confidencesName = catalog.Output.Confidences!;
        _labelTable = catalog.Codec!.LabelTable;

        Id = $"onnx:{modelId}";
        Profile = new RecognizerProfile(modelId, renderConvention, unicodeForm);
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public RecognizerProfile Profile { get; }

    /// <summary>
    /// Loads a model that has already been read into memory and checks it against the catalog.
    /// <para>
    /// The hash is verified here rather than trusted: a model file that arrives truncated, swapped or
    /// half-downloaded still loads into a valid graph, and the only symptom would be text that is
    /// subtly wrong on every card. Failing loudly at startup is far cheaper than that.
    /// </para>
    /// </summary>
    /// <param name="model">The ONNX file's bytes.</param>
    /// <param name="catalog">The catalog or per-model manifest describing it.</param>
    /// <param name="modelId">Which entry of the catalog this file is, e.g. <c>kraken-ppocrv6-small-fp16</c>.</param>
    /// <param name="renderConvention">Fill ratio for rendering, see <see cref="DefaultRenderConvention"/>.</param>
    /// <param name="intraOpThreads">
    /// Threads ONNX Runtime may use inside a single line. <c>null</c> leaves the runtime's default,
    /// which is one thread per core - more than a phone wants while the user is still drawing.
    /// </param>
    public static OnnxInkTextRecognizer Create(
        byte[] model,
        Ppocrv6Catalog catalog,
        string modelId,
        string? renderConvention = null,
        int? intraOpThreads = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        catalog.Validate();

        var entry = catalog.FindModel(modelId)
            ?? throw new ArgumentException(
                $"Der Modellkatalog enthält keinen Eintrag '{modelId}'.", nameof(modelId));

        Verify(model, entry);

        var convention = string.IsNullOrWhiteSpace(renderConvention)
            ? DefaultRenderConvention
            : renderConvention;

        // The renderer is the authority on what a convention means, so an unknown one is rejected here
        // rather than on the first line of the first card.
        if (!InkLineRenderer.TryGetFillRatio(convention, out _))
        {
            throw new ArgumentException(
                $"Unbekannte Render-Konvention '{convention}'.", nameof(renderConvention));
        }

        var unicodeForm = catalog.UnicodeForm?.ToLowerInvariant() ?? Ppocrv6Catalog.SupportedUnicodeForm;

        // ONNX Runtime copies the options into the native session while it is constructed, so the
        // managed handle can go as soon as the session exists.
        using var options = new SessionOptions
        {
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };

        if (intraOpThreads is > 0)
        {
            options.IntraOpNumThreads = intraOpThreads.Value;
        }

        var session = new InferenceSession(model, options);

        return new OnnxInkTextRecognizer(session, catalog, modelId, convention, unicodeForm);
    }

    /// <summary>Loads the model from disk. See <see cref="Create(byte[], Ppocrv6Catalog, string, string?, int?)"/>.</summary>
    public static OnnxInkTextRecognizer CreateFromFiles(
        string modelPath,
        string catalogPath,
        string modelId,
        string? renderConvention = null,
        int? intraOpThreads = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);

        using var catalogStream = File.OpenRead(catalogPath);
        var catalog = Ppocrv6Catalog.Load(catalogStream);

        return Create(File.ReadAllBytes(modelPath), catalog, modelId, renderConvention, intraOpThreads);
    }

    /// <summary>
    /// Loads the model and catalog that ship inside this assembly. This is the path the app takes;
    /// the file-based overloads exist for tests and the benchmark harness.
    /// </summary>
    /// <param name="modelId">Which catalog entry to load, see <see cref="DefaultModelId"/>.</param>
    /// <param name="renderConvention">Fill ratio for rendering, see <see cref="DefaultRenderConvention"/>.</param>
    /// <param name="intraOpThreads">Threads ONNX Runtime may use inside a single line.</param>
    public static OnnxInkTextRecognizer CreateFromEmbeddedResources(
        string? modelId = null,
        string? renderConvention = null,
        int? intraOpThreads = null)
    {
        var assembly = typeof(OnnxInkTextRecognizer).Assembly;
        var resolvedId = string.IsNullOrWhiteSpace(modelId) ? DefaultModelId : modelId;

        Ppocrv6Catalog catalog;
        using (var catalogStream = OpenResource(assembly, CatalogResourceName))
        {
            catalog = Ppocrv6Catalog.Load(catalogStream);
        }

        // The resource name comes from the catalog entry rather than from the id, so renaming an id
        // cannot desynchronise it from the file that is actually embedded.
        var entry = catalog.FindModel(resolvedId)
            ?? throw new ArgumentException(
                $"Der Modellkatalog enthält keinen Eintrag '{resolvedId}'.", nameof(modelId));

        var model = ReadResource(assembly, ResourcePrefix + entry.Model);

        return Create(model, catalog, resolvedId, renderConvention, intraOpThreads);
    }

    private static Stream OpenResource(Assembly assembly, string resourceName) =>
        assembly.GetManifestResourceStream(resourceName)
        ?? throw new InvalidDataException(
            $"Die Ressource '{resourceName}' fehlt in der Assembly '{assembly.GetName().Name}'.");

    private static byte[] ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = OpenResource(assembly, resourceName);

        // A resource inside a loaded assembly is a view onto the mapped image and reports its length;
        // the copy path is only for the case where a host has re-wrapped it as a non-seekable stream.
        if (stream.CanSeek && stream.Length is > 0 and <= int.MaxValue)
        {
            var buffer = new byte[(int)stream.Length];
            stream.ReadExactly(buffer);
            return buffer;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <inheritdoc />
    public Task<InkRecognitionResult> RecognizeAsync(
        IReadOnlyList<InkStroke> strokes,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(strokes);

        if (strokes.Count == 0)
        {
            return Task.FromResult(new InkRecognitionResult([], Profile));
        }

        // Off the caller's thread: this is CPU-bound work measured in hundreds of milliseconds per
        // line, and the caller is usually a UI-facing service.
        return Task.Run(() => RecognizeCore(strokes, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Dispose();
    }

    private static void Verify(byte[] model, Ppocrv6ModelEntry entry)
    {
        if (entry.SizeBytes > 0 && entry.SizeBytes != model.LongLength)
        {
            throw new InvalidDataException(
                $"Das Modell '{entry.Id}' ist {model.LongLength} Bytes groß, der Katalog erwartet " +
                $"{entry.SizeBytes}.");
        }

        if (string.IsNullOrWhiteSpace(entry.Sha256))
        {
            return;
        }

        var actual = Convert.ToHexString(SHA256.HashData(model)).ToLowerInvariant();

        if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Das Modell '{entry.Id}' hat den SHA-256 {actual}, der Katalog erwartet {entry.Sha256}.");
        }
    }

    private InkRecognitionResult RecognizeCore(
        IReadOnlyList<InkStroke> strokes,
        CancellationToken cancellationToken)
    {
        var lines = InkLineSegmenter.Segment(strokes);
        var results = new List<InkLineResult>(lines.Count);

        foreach (var line in lines)
        {
            // Cancellation is only honest between lines: ONNX Runtime has no way to abort a Run that
            // is already executing. A line is the shortest unit that is still cheap to abandon, and
            // the caller has not lost anything by then - no partial result is ever returned.
            cancellationToken.ThrowIfCancellationRequested();

            var image = InkLineRenderer.Render(line, Profile.RenderConvention);
            var decoded = RecognizeLine(image);

            results.Add(new InkLineResult(decoded.Text, decoded.MeanConfidence, line.Bounds));
        }

        return new InkRecognitionResult(results, Profile);
    }

    private CtcDecodeResult RecognizeLine(InkLineImage image)
    {
        var input = image.ToModelInput();

        // The OrtValue overload is the only one that does not route through System.Numerics.Tensors:
        // under net10.0 the framework ships its own Tensor<T> (experimental, without DenseTensor<T>),
        // which shadows the 9.0 assembly the managed ONNX Runtime package was built against.
        // RunOptions is not safe for concurrent use, so every call gets its own.
        using var runOptions = new RunOptions();
        using var inputValue = OrtValue.CreateTensorValueFromMemory(
            input,
            [1, Channels, image.Height, image.Width]);

        string[] inputNames = [_inputName];
        OrtValue[] inputValues = [inputValue];
        string[] outputNames = [_labelsName, _confidencesName];

        using var values = _session.Run(runOptions, inputNames, inputValues, outputNames);

        OrtValue? labelValue = null;
        OrtValue? confidenceValue = null;

        foreach (var value in values)
        {
            // The OrtValue result carries no names, so the outputs are told apart by element type:
            // the catalog declares labels as int64 and confidences as float32.
            switch (value.GetTensorTypeAndShape().ElementDataType)
            {
                case TensorElementType.Int64:
                    labelValue = value;
                    break;

                case TensorElementType.Float:
                    confidenceValue = value;
                    break;

                default:
                    break;
            }
        }

        if (labelValue is null || confidenceValue is null)
        {
            throw new InvalidDataException(
                $"Das Modell lieferte die Ausgaben '{_labelsName}' (int64) und '{_confidencesName}' " +
                "(float32) nicht.");
        }

        var decoded = CtcDecoder.Decode(
            labelValue.GetTensorDataAsSpan<long>(),
            confidenceValue.GetTensorDataAsSpan<float>(),
            _labelTable);

        return decoded with { Text = decoded.Text.Normalize(NormalizationForm.FormC) };
    }
}
