/* The custom-op entry point of onnxruntime-extensions, which Penban does not use.
 *
 * The managed ONNX Runtime assembly declares this symbol as [DllImport("__Internal")] in
 * OrtExtensionsNativeMethods. On iOS that makes it a hard requirement of the link - "__Internal"
 * means "comes from the app itself" - even though nothing in Penban ever calls it, because the
 * extensions are a separate, optional package for custom operators. Penban runs the stock
 * PP-OCRv6 model with no custom ops.
 *
 * Linking the ONNX Runtime as the static framework it ships for iOS therefore fails with
 * "Undefined symbols for architecture arm64: _RegisterCustomOps" unless the app answers the
 * symbol. This file is that answer. It is compiled and handed to the link by the
 * LinkOnnxRuntimeExtensionsStub target in Penban.Maui.csproj.
 *
 * Returning NULL means "no error, nothing registered", which is what an app without custom ops
 * answers. Nothing in Penban sets SessionOptions.RegisterOrtExtensions, so the function is never
 * called.
 *
 * The three types are spelled out instead of including onnxruntime_extensions.h: the file then
 * needs no include path into the NuGet package, and the pointers are opaque here anyway. The
 * signatures mirror the declarations in onnxruntime_c_api.h.
 */

typedef struct OrtStatus OrtStatus;
typedef struct OrtSessionOptions OrtSessionOptions;
typedef struct OrtApiBase OrtApiBase;

OrtStatus* RegisterCustomOps(OrtSessionOptions* options, const OrtApiBase* api)
{
    (void)options;
    (void)api;
    return 0;
}
