using System;
using System.Reflection;
using System.Runtime.InteropServices;
using SkiaSharp;
using Stride.Graphics;

namespace Stride.Avalonia;

internal static unsafe class StrideDirect3D11Interop
{
    private const uint DxgiFormatR8G8B8A8Unorm = 28;
    private const uint DxgiFormatR8G8B8A8UnormSrgb = 29;

    private static readonly Guid Id3D11Texture2DIid = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid DxgiDeviceIid = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    private static PropertyInfo? _nativeDeviceProperty;
    private static PropertyInfo? _nativeDeviceContextProperty;
    private static MethodInfo? _getNativeResourceMethod;
    private static bool _reflectionCached;

    internal sealed class SharedContextResources : IDisposable
    {
        private readonly IntPtr _devicePointer;
        private readonly IntPtr _deviceContextPointer;
        private readonly IntPtr _adapterPointer;
        private bool _disposed;

        internal SharedContextResources(GRContext context, IntPtr devicePointer, IntPtr deviceContextPointer, IntPtr adapterPointer)
        {
            Context = context;
            _devicePointer = devicePointer;
            _deviceContextPointer = deviceContextPointer;
            _adapterPointer = adapterPointer;
        }

        internal GRContext Context { get; }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Release(_adapterPointer);
            Release(_deviceContextPointer);
            Release(_devicePointer);
        }
    }

    internal sealed class SharedTextureRenderScope : IDisposable
    {
        private readonly IntPtr _texturePointer;
        private bool _disposed;

        internal SharedTextureRenderScope(IntPtr texturePointer)
        {
            _texturePointer = texturePointer;
        }

        internal void Complete()
        {
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Release(_texturePointer);
        }
    }

    internal readonly struct RenderTargetScope
    {
        internal RenderTargetScope(GRBackendRenderTarget backendRenderTarget, SharedTextureRenderScope scope)
        {
            BackendRenderTarget = backendRenderTarget;
            Scope = scope;
        }

        internal GRBackendRenderTarget BackendRenderTarget { get; }

        internal SharedTextureRenderScope Scope { get; }
    }

    public static bool IsSupported(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var deviceType = device.GetType();
        var interopType = deviceType.Assembly.GetType("Stride.Graphics.SharpDXInterop");

        return FindPropertyInHierarchy(deviceType, "NativeDevice", flags) != null
            && FindPropertyInHierarchy(deviceType, "NativeDeviceContext", flags) != null
            && interopType?.GetMethod(
                "GetNativeResource",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(GraphicsResource) },
                modifiers: null) != null;
    }

    public static SharedContextResources CreateSharedContext(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        EnsureReflectionCache(device);

        var nativeDevice = _nativeDeviceProperty!.GetValue(device)
            ?? throw new InvalidOperationException("GraphicsDevice.NativeDevice returned null for the active Direct3D11 backend.");
        var nativeDeviceContext = _nativeDeviceContextProperty!.GetValue(device)
            ?? throw new InvalidOperationException("GraphicsDevice.NativeDeviceContext returned null for the active Direct3D11 backend.");

        var devicePointer = AddRefPointer(GetNativePointer(nativeDevice, "GraphicsDevice.NativeDevice"));
        var deviceContextPointer = AddRefPointer(GetNativePointer(nativeDeviceContext, "GraphicsDevice.NativeDeviceContext"));
        IntPtr adapterPointer = IntPtr.Zero;

        try
        {
            adapterPointer = GetAdapterPointer(devicePointer);

            var backendContext = new GRD3DBackendContext
            {
                Adapter = adapterPointer,
                Device = devicePointer,
                Queue = deviceContextPointer,
                ProtectedContext = false,
            };

            var grContext = GRContext.CreateDirect3D(backendContext)
                ?? throw new InvalidOperationException(
                    "SkiaSharp failed to create a Direct3D11 GRContext from the active Stride device.");

            return new SharedContextResources(grContext, devicePointer, deviceContextPointer, adapterPointer);
        }
        catch
        {
            Release(adapterPointer);
            Release(deviceContextPointer);
            Release(devicePointer);
            throw;
        }
    }

    public static RenderTargetScope CreateBackendRenderTarget(Texture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);

        EnsureNativeResourceMethod(texture.GetType().Assembly);

        var nativeResource = _getNativeResourceMethod!.Invoke(null, new object[] { texture })
            ?? throw new InvalidOperationException("SharpDXInterop.GetNativeResource returned null for the active Stride texture.");
        var resourcePointer = GetNativePointer(nativeResource, "SharpDXInterop.GetNativeResource");
        var texturePointer = QueryInterface(resourcePointer, Id3D11Texture2DIid, "ID3D11Texture2D");

        try
        {
            var textureInfo = new GRD3DTextureResourceInfo
            {
                Resource = texturePointer,
                ResourceState = 0,
                Format = GetDxgiFormat(texture),
                SampleCount = 1,
                LevelCount = (uint)Math.Max(texture.MipLevels, 1),
                SampleQualityPattern = 0,
                Protected = false,
            };

            var backendRenderTarget = new GRBackendRenderTarget(texture.Width, texture.Height, textureInfo);
            if (!backendRenderTarget.IsValid)
            {
                backendRenderTarget.Dispose();
                throw new InvalidOperationException(
                    "SkiaSharp created an invalid Direct3D11 backend render target for the active Stride texture.");
            }

            return new RenderTargetScope(backendRenderTarget, new SharedTextureRenderScope(texturePointer));
        }
        catch
        {
            Release(texturePointer);
            throw;
        }
    }

    private static FieldInfo? FindFieldInHierarchy(Type type, string name, BindingFlags flags)
    {
        var current = type;
        while (current != null)
        {
            var field = current.GetField(name, flags | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;

            current = current.BaseType;
        }

        return null;
    }

    private static PropertyInfo? FindPropertyInHierarchy(Type type, string name, BindingFlags flags)
    {
        var current = type;
        while (current != null)
        {
            var property = current.GetProperty(name, flags | BindingFlags.DeclaredOnly);
            if (property != null)
                return property;

            current = current.BaseType;
        }

        return null;
    }

    private static void EnsureReflectionCache(GraphicsDevice device)
    {
        if (_reflectionCached)
            return;

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var deviceType = device.GetType();

        _nativeDeviceProperty = FindPropertyInHierarchy(deviceType, "NativeDevice", flags)
            ?? throw new InvalidOperationException(
                $"Could not find 'NativeDevice' on {deviceType.FullName}. The active backend is not exposing Direct3D11 interop.");
        _nativeDeviceContextProperty = FindPropertyInHierarchy(deviceType, "NativeDeviceContext", flags)
            ?? throw new InvalidOperationException(
                $"Could not find 'NativeDeviceContext' on {deviceType.FullName}. The active backend is not exposing Direct3D11 interop.");

        EnsureNativeResourceMethod(deviceType.Assembly);
        _reflectionCached = true;
    }

    private static void EnsureNativeResourceMethod(Assembly assembly)
    {
        if (_getNativeResourceMethod != null)
            return;

        var interopType = assembly.GetType("Stride.Graphics.SharpDXInterop")
            ?? throw new InvalidOperationException(
                "Stride.Graphics.SharpDXInterop was not found in the active Direct3D11 build. A native resource wrapper is required for Skia render-target creation.");

        _getNativeResourceMethod = interopType.GetMethod(
            "GetNativeResource",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(GraphicsResource) },
            modifiers: null)
            ?? throw new InvalidOperationException(
                "Stride.Graphics.SharpDXInterop.GetNativeResource(GraphicsResource) is missing from the active Direct3D11 build.");
    }

    private static IntPtr GetNativePointer(object nativeObject, string memberName)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var nativePointerProperty = FindPropertyInHierarchy(nativeObject.GetType(), "NativePointer", flags)
            ?? throw new InvalidOperationException(
                $"{memberName} returned '{nativeObject.GetType().FullName}', which does not expose a NativePointer property.");

        var value = nativePointerProperty.GetValue(nativeObject)
            ?? throw new InvalidOperationException($"{memberName}.NativePointer returned null.");

        if (value is not IntPtr pointer || pointer == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"{memberName}.NativePointer did not expose a valid COM pointer.");
        }

        return pointer;
    }

    private static IntPtr AddRefPointer(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
            throw new InvalidOperationException("A Direct3D11 COM pointer was unexpectedly null.");

        Marshal.AddRef(pointer);
        return pointer;
    }

    private static IntPtr GetAdapterPointer(IntPtr devicePointer)
    {
        var dxgiDevicePointer = QueryInterface(devicePointer, DxgiDeviceIid, "IDXGIDevice");

        try
        {
            var vtable = *(IntPtr**)dxgiDevicePointer;
            var getAdapter = (delegate* unmanaged<IntPtr, IntPtr*, int>)vtable[7];
            IntPtr adapterPointer;
            var hr = getAdapter(dxgiDevicePointer, &adapterPointer);
            ThrowIfFailed(hr, "IDXGIDevice.GetAdapter");

            if (adapterPointer == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "IDXGIDevice.GetAdapter returned a null adapter pointer. SkiaSharp requires the DXGI adapter to create a Direct3D GRContext.");
            }

            return adapterPointer;
        }
        finally
        {
            Release(dxgiDevicePointer);
        }
    }

    private static IntPtr QueryInterface(IntPtr instance, Guid iid, string interfaceName)
    {
        var vtable = *(IntPtr**)instance;
        var queryInterface = (delegate* unmanaged<IntPtr, Guid*, IntPtr*, int>)vtable[0];
        IntPtr result;
        var iidCopy = iid;

        var hr = queryInterface(instance, &iidCopy, &result);
        ThrowIfFailed(hr, $"IUnknown.QueryInterface({interfaceName})");

        if (result == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"IUnknown.QueryInterface({interfaceName}) succeeded but returned a null COM pointer.");
        }

        return result;
    }

    private static void Release(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
            return;

        var vtable = *(IntPtr**)pointer;
        var release = (delegate* unmanaged<IntPtr, uint>)vtable[2];
        _ = release(pointer);
    }

    private static void ThrowIfFailed(int hr, string operation)
    {
        if (hr < 0)
        {
            throw new InvalidOperationException(
                $"{operation} failed with HRESULT 0x{hr:X8}.");
        }
    }

    private static uint GetDxgiFormat(Texture texture)
        => texture.Format switch
        {
            PixelFormat.R8G8B8A8_UNorm => DxgiFormatR8G8B8A8Unorm,
            PixelFormat.R8G8B8A8_UNorm_SRgb => DxgiFormatR8G8B8A8UnormSrgb,
            _ => throw new NotSupportedException(
                $"The Direct3D11 shared-texture path only supports R8G8B8A8 render targets today. Received '{texture.Format}'.")
        };
}