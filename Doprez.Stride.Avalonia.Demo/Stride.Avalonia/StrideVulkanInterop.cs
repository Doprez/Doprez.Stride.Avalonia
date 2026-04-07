using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using SkiaSharp;
using Stride.Graphics;
using Vortice.Vulkan;

using static Vortice.Vulkan.Vulkan;

namespace Stride.Avalonia;

/// <summary>
/// Creates a shared SkiaSharp <see cref="GRContext"/> from Stride's Vulkan
/// device handles, enabling zero-copy GPU texture sharing between Avalonia's
/// Skia compositor and Stride's renderer.
/// <para>
/// All Vulkan handles are extracted via reflection because Stride 4.3's
/// Vulkan backend exposes them as <c>internal</c> properties only.
/// </para>
/// </summary>
internal static unsafe class StrideVulkanInterop
{
    private const uint VkImageTilingOptimal = 0;
    private const uint VkSharingModeExclusive = 0;
    private const uint VkQueueFamilyIgnored = uint.MaxValue;
    private const uint VkImageUsageSampledBit = 0x00000004;
    private const uint VkImageUsageColorAttachmentBit = 0x00000010;

    // Cached reflection lookups — GraphicsDevice
    private static PropertyInfo? _nativeInstanceProp;
    private static PropertyInfo? _nativePhysicalDeviceProp;
    private static PropertyInfo? _nativeDeviceProp;
    private static FieldInfo? _nativeCommandQueueField; // field, not property
    private static FieldInfo? _queueLockField;
    private static FieldInfo? _queueFamilyField;
    private static bool _reflectionCached;

    // Cached reflection lookups — Texture
    private static FieldInfo? _nativeImageField;  // field, not property
    private static FieldInfo? _nativeFormatField;  // field, not property
    private static FieldInfo? _nativeLayoutField;  // field, not property
    private static FieldInfo? _nativeAccessMaskField;  // field, not property
    private static FieldInfo? _nativePipelineStageMaskField;  // field, not property
    private static FieldInfo? _nativeImageAspectField;  // field, not property
    private static bool _textureReflectionCached;

    // Vulkan function pointer resolution
    private static IntPtr _vulkanLib;
    private static delegate* unmanaged<IntPtr, byte*, IntPtr> _vkGetInstanceProcAddr;
    private static delegate* unmanaged<IntPtr, byte*, IntPtr> _vkGetDeviceProcAddr;

    // VkImage interception for Skia-managed surfaces
    private static IntPtr _realVkCreateImagePtr;
    private static volatile bool _captureMode;
    // Per-capture-session slot.  Each BeginVkImageCapture gets a unique token.
    // The interceptor stores the VkImage handle for the active capture dimensions.
    // EndVkImageCapture claims the handle only if the token matches.
    private static long _captureToken;
    private static long _activeCaptureToken;
    private static uint _captureWidth;
    private static uint _captureHeight;
    private static ulong _capturedImageHandle;
    private static long _interceptCallCount;
    private static bool _loggedInterception;
    private static bool _loggedInterceptDiag;

    /// <summary>
    /// The graphics queue family index discovered during GRContext creation.
    /// Used when building <see cref="GRVkImageInfo"/> for render targets.
    /// </summary>
    internal static uint GraphicsQueueFamilyIndex { get; private set; }

    internal readonly struct VulkanImageState
    {
        public VulkanImageState(
            VkImageLayout layout,
            VkAccessFlags accessMask,
            VkPipelineStageFlags pipelineStageMask,
            uint queueFamilyIndex,
            VkImageAspectFlags imageAspect)
        {
            Layout = layout;
            AccessMask = accessMask;
            PipelineStageMask = pipelineStageMask;
            QueueFamilyIndex = queueFamilyIndex;
            ImageAspect = imageAspect;
        }

        public VkImageLayout Layout { get; }

        public VkAccessFlags AccessMask { get; }

        public VkPipelineStageFlags PipelineStageMask { get; }

        public uint QueueFamilyIndex { get; }

        public VkImageAspectFlags ImageAspect { get; }
    }

    internal sealed class SharedImageSyncScope : IDisposable
    {
        private readonly GraphicsDevice _device;
        private readonly Texture _texture;
        private readonly object _queueLock;
        private int _disposed;

        internal SharedImageSyncScope(GraphicsDevice device, Texture texture, VulkanImageState currentState)
        {
            _device = device;
            _texture = texture;
            _queueLock = GetQueueLock(device);

            Monitor.Enter(_queueLock);

            try
            {
                CurrentState = TransitionImage(
                    device,
                    texture,
                    currentState,
                    CreateColorAttachmentState(currentState.ImageAspect));
            }
            catch
            {
                Monitor.Exit(_queueLock);
                throw;
            }
        }

        public VulkanImageState CurrentState { get; private set; }

        public void Complete()
        {
            CurrentState = TransitionImage(
                _device,
                _texture,
                CurrentState,
                CreateShaderReadState(CurrentState.ImageAspect));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Monitor.Exit(_queueLock);
        }
    }

    /// <summary>
    /// Creates a <see cref="GRContext"/> from Stride's Vulkan device.
    /// Throws on any failure — no silent fallback.
    /// </summary>
    public static GRContext CreateGRContext(GraphicsDevice device)
    {
        EnsureReflectionCache(device);
        LoadVulkanLibrary();

        var vkInstance = GetHandleProperty(_nativeInstanceProp!, device, "NativeInstance");
        var vkPhysicalDevice = GetHandleProperty(_nativePhysicalDeviceProp!, device, "NativePhysicalDevice");
        var vkDevice = GetHandleProperty(_nativeDeviceProp!, device, "NativeDevice");
        var vkQueue = GetHandleField(_nativeCommandQueueField!, device, "NativeCommandQueue");

        GraphicsQueueFamilyIndex = GetQueueFamilyIndex(device);

        // Skia needs the extensions list to initialise its format/capability
        // tables.  The static Create method auto-discovers available extensions
        // from the instance and physical device via vkEnumerate*ExtensionProperties.
        var extensions = GRVkExtensions.Create(
            GetProcedureAddress,
            vkInstance,
            vkPhysicalDevice,
            instanceExtensions: null,
            deviceExtensions: null);

        var backendContext = new GRVkBackendContext
        {
            VkInstance = vkInstance,
            VkPhysicalDevice = vkPhysicalDevice,
            VkDevice = vkDevice,
            VkQueue = vkQueue,
            GraphicsQueueIndex = GraphicsQueueFamilyIndex,
            GetProcedureAddress = GetProcedureAddress,
            Extensions = extensions,
        };

        var grContext = GRContext.CreateVulkan(backendContext)
            ?? throw new InvalidOperationException(
                "SkiaSharp failed to create a Vulkan GRContext. " +
                "Verify GPU driver Vulkan support and that the SkiaSharp native library (libSkiaSharp.so) " +
                "was built with Vulkan backend enabled.");

        return grContext;
    }

    /// <summary>
    /// Returns <c>true</c> when the given <see cref="GraphicsDevice"/> exposes
    /// the Vulkan handles required for shared Skia interop.
    /// </summary>
    public static bool IsSupported(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var type = device.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        return FindPropertyInHierarchy(type, "NativeInstance", flags) != null
            && FindPropertyInHierarchy(type, "NativePhysicalDevice", flags) != null
            && FindPropertyInHierarchy(type, "NativeDevice", flags) != null
            && FindFieldInHierarchy(type, "NativeCommandQueue", flags) != null;
    }

    /// <summary>
    /// Wraps a Stride-owned Vulkan render target as a Skia backend render target.
    /// </summary>
    internal static GRBackendRenderTarget CreateBackendRenderTarget(Texture texture, VulkanImageState imageState)
    {
        ArgumentNullException.ThrowIfNull(texture);

        var imageInfo = new GRVkImageInfo
        {
            Image = unchecked((ulong)GetNativeImage(texture).ToInt64()),
            Format = GetNativeFormat(texture),
            ImageLayout = (uint)imageState.Layout,
            ImageTiling = VkImageTilingOptimal,
            ImageUsageFlags = VkImageUsageSampledBit | VkImageUsageColorAttachmentBit,
            SampleCount = 1,
            LevelCount = 1,
            CurrentQueueFamily = imageState.QueueFamilyIndex,
            SharingMode = VkSharingModeExclusive,
        };

        return new GRBackendRenderTarget(texture.Width, texture.Height, imageInfo);
    }

    internal static VulkanImageState CaptureImageState(GraphicsDevice device, Texture texture)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(texture);

        EnsureReflectionCache(device);
        EnsureTextureReflection(texture);

        return new VulkanImageState(
            GetEnumFieldValue<VkImageLayout>(_nativeLayoutField, texture, "NativeLayout"),
            GetEnumFieldValue<VkAccessFlags>(_nativeAccessMaskField, texture, "NativeAccessMask"),
            GetEnumFieldValue<VkPipelineStageFlags>(_nativePipelineStageMaskField, texture, "NativePipelineStageMask"),
            GraphicsQueueFamilyIndex,
            GetEnumFieldValue<VkImageAspectFlags>(_nativeImageAspectField, texture, "NativeImageAspect"));
    }

    internal static SharedImageSyncScope BeginSharedImageScope(GraphicsDevice device, Texture texture, VulkanImageState currentState)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(texture);

        EnsureReflectionCache(device);
        EnsureTextureReflection(texture);
        LoadVulkanLibrary();

        return new SharedImageSyncScope(device, texture, currentState);
    }

    /// <summary>
    /// Extracts the <c>VkImage</c> handle from a Stride <see cref="Texture"/>.
    /// </summary>
    internal static IntPtr GetNativeImage(Texture texture)
    {
        EnsureTextureReflection(texture);
        return GetHandleField(_nativeImageField!, texture, "NativeImage");
    }

    /// <summary>
    /// Extracts the <c>VkFormat</c> (as uint) from a Stride <see cref="Texture"/>.
    /// </summary>
    internal static uint GetNativeFormat(Texture texture)
    {
        EnsureTextureReflection(texture);
        var val = _nativeFormatField!.GetValue(texture)
            ?? throw new InvalidOperationException(
                $"Texture.NativeFormat returned null on {texture.GetType().FullName}. " +
                "Stride Vulkan backend may have changed.");
        // VkFormat is an enum — convert to uint
        return Convert.ToUInt32(val);
    }

    /// <summary>
    /// Extracts the current <c>VkImageLayout</c> (as uint) from a Stride <see cref="Texture"/>.
    /// </summary>
    internal static uint GetNativeLayout(Texture texture)
    {
        EnsureTextureReflection(texture);

        if (_nativeLayoutField == null)
        {
            throw new InvalidOperationException(
                $"Could not find 'NativeLayout' field on {texture.GetType().FullName}. " +
                "Stride Vulkan backend may have changed.");
        }

        var val = _nativeLayoutField!.GetValue(texture)
            ?? throw new InvalidOperationException(
                $"Texture.NativeLayout returned null on {texture.GetType().FullName}. " +
                "Stride Vulkan backend may have changed.");
        return Convert.ToUInt32(val);
    }

    // ── Reflection helpers ──────────────────────────────────────────

    /// <summary>
    /// Walk the type hierarchy to find a non-public field (GetField does NOT
    /// search base classes for non-public members).
    /// </summary>
    private static FieldInfo? FindFieldInHierarchy(Type type, string name, BindingFlags flags)
    {
        var current = type;
        while (current != null)
        {
            var field = current.GetField(name, flags | BindingFlags.DeclaredOnly);
            if (field != null) return field;
            current = current.BaseType;
        }
        return null;
    }

    /// <summary>
    /// Walk the type hierarchy to find a non-public property.
    /// </summary>
    private static PropertyInfo? FindPropertyInHierarchy(Type type, string name, BindingFlags flags)
    {
        var current = type;
        while (current != null)
        {
            var prop = current.GetProperty(name, flags | BindingFlags.DeclaredOnly);
            if (prop != null) return prop;
            current = current.BaseType;
        }
        return null;
    }

    private static void EnsureReflectionCache(GraphicsDevice device)
    {
        if (_reflectionCached) return;

        var type = device.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var nativeInstanceProp = FindPropertyInHierarchy(type, "NativeInstance", flags)
            ?? throw new InvalidOperationException(
                $"Could not find 'NativeInstance' property on {type.FullName}. " +
                "Is this a Vulkan-backend GraphicsDevice? (Stride 4.3 expected)");

        var nativePhysicalDeviceProp = FindPropertyInHierarchy(type, "NativePhysicalDevice", flags)
            ?? throw new InvalidOperationException(
                $"Could not find 'NativePhysicalDevice' property on {type.FullName}. " +
                "Is this a Vulkan-backend GraphicsDevice? (Stride 4.3 expected)");

        var nativeDeviceProp = FindPropertyInHierarchy(type, "NativeDevice", flags)
            ?? throw new InvalidOperationException(
                $"Could not find 'NativeDevice' property on {type.FullName}. " +
                "Is this a Vulkan-backend GraphicsDevice? (Stride 4.3 expected)");

        var nativeCommandQueueField = FindFieldInHierarchy(type, "NativeCommandQueue", flags)
            ?? throw new InvalidOperationException(
                $"Could not find 'NativeCommandQueue' field on {type.FullName}. " +
                "Is this a Vulkan-backend GraphicsDevice? (Stride 4.3 expected)");

        var queueLockField = FindFieldInHierarchy(type, "QueueLock", flags)
            ?? throw new InvalidOperationException(
                $"Could not find 'QueueLock' field on {type.FullName}. " +
                "Stride Vulkan queue submissions can no longer be synchronized safely.");

        // Queue family index: try common field/property names
        var queueFamilyField = FindFieldInHierarchy(type, "_graphicsQueueFamilyIndex", flags)
            ?? FindFieldInHierarchy(type, "graphicsQueueFamilyIndex", flags);
        // If not found we'll try a property fallback later

        _nativeInstanceProp = nativeInstanceProp;
        _nativePhysicalDeviceProp = nativePhysicalDeviceProp;
        _nativeDeviceProp = nativeDeviceProp;
        _nativeCommandQueueField = nativeCommandQueueField;
        _queueLockField = queueLockField;
        _queueFamilyField = queueFamilyField;
        _reflectionCached = true;
    }

    private static void EnsureTextureReflection(Texture texture)
    {
        if (_textureReflectionCached) return;

        var type = texture.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var nativeImageField = FindFieldInHierarchy(type, "NativeImage", flags)
            ?? throw new InvalidOperationException(
                $"Could not find 'NativeImage' field on {type.FullName}. " +
                "Stride Vulkan backend may have changed.");

        var nativeFormatField = FindFieldInHierarchy(type, "NativeFormat", flags)
            ?? throw new InvalidOperationException(
                $"Could not find 'NativeFormat' field on {type.FullName}. " +
                "Stride Vulkan backend may have changed.");

        var nativeLayoutField = FindFieldInHierarchy(type, "NativeLayout", flags);
        var nativeAccessMaskField = FindFieldInHierarchy(type, "NativeAccessMask", flags);
        var nativePipelineStageMaskField = FindFieldInHierarchy(type, "NativePipelineStageMask", flags);
        var nativeImageAspectField = FindFieldInHierarchy(type, "NativeImageAspect", flags);

        _nativeImageField = nativeImageField;
        _nativeFormatField = nativeFormatField;
        _nativeLayoutField = nativeLayoutField;
        _nativeAccessMaskField = nativeAccessMaskField;
        _nativePipelineStageMaskField = nativePipelineStageMaskField;
        _nativeImageAspectField = nativeImageAspectField;
        _textureReflectionCached = true;
    }

    private static TEnum GetEnumFieldValue<TEnum>(FieldInfo? field, object target, string name)
        where TEnum : struct, Enum
    {
        if (field == null)
        {
            throw new InvalidOperationException(
                $"Could not find '{name}' field on {target.GetType().FullName}. " +
                "Stride Vulkan backend may have changed.");
        }

        var value = field.GetValue(target)
            ?? throw new InvalidOperationException(
                $"{name} returned null on {target.GetType().FullName}. " +
                "Stride Vulkan backend may have changed.");

        return value is TEnum typedValue
            ? typedValue
            : (TEnum)Enum.ToObject(typeof(TEnum), value);
    }

    private static object GetQueueLock(GraphicsDevice device)
    {
        EnsureReflectionCache(device);

        var queueLock = _queueLockField!.GetValue(device);
        return queueLock
            ?? throw new InvalidOperationException(
                $"QueueLock returned null on {device.GetType().FullName}. " +
                "Stride Vulkan queue submissions can no longer be synchronized safely.");
    }

    private static VulkanImageState CreateColorAttachmentState(VkImageAspectFlags imageAspect)
        => new(
            VkImageLayout.ColorAttachmentOptimal,
            VkAccessFlags.ColorAttachmentWrite,
            VkPipelineStageFlags.ColorAttachmentOutput,
            GraphicsQueueFamilyIndex,
            imageAspect);

    private static VulkanImageState CreateShaderReadState(VkImageAspectFlags imageAspect)
        => new(
            VkImageLayout.ShaderReadOnlyOptimal,
            VkAccessFlags.InputAttachmentRead | VkAccessFlags.ShaderRead,
            VkPipelineStageFlags.VertexInput | VkPipelineStageFlags.FragmentShader,
            GraphicsQueueFamilyIndex,
            imageAspect);

    private static VulkanImageState TransitionImage(
        GraphicsDevice device,
        Texture texture,
        VulkanImageState currentState,
        VulkanImageState targetState)
    {
        ValidateQueueOwnership(currentState.QueueFamilyIndex, targetState.QueueFamilyIndex);

        if (currentState.Layout == targetState.Layout
            && currentState.AccessMask == targetState.AccessMask
            && currentState.PipelineStageMask == targetState.PipelineStageMask
            && currentState.QueueFamilyIndex == targetState.QueueFamilyIndex)
        {
            ApplyImageState(texture, targetState);
            return targetState;
        }

        var nativeDevice = GetHandleProperty(_nativeDeviceProp!, device, "NativeDevice");
        var nativeQueue = GetHandleField(_nativeCommandQueueField!, device, "NativeCommandQueue");
        var deviceHandle = new VkDevice(nativeDevice);
        var commandPool = VkCommandPool.Null;
        var commandBuffer = VkCommandBuffer.Null;
        var fence = VkFence.Null;

        try
        {
            commandPool = CreateCommandPool(deviceHandle, GraphicsQueueFamilyIndex);
            commandBuffer = AllocateCommandBuffer(deviceHandle, commandPool);
            BeginCommandBuffer(commandBuffer);

            var barrier = new VkImageMemoryBarrier(
                (VkImage)(ulong)GetNativeImage(texture).ToInt64(),
                new VkImageSubresourceRange(currentState.ImageAspect),
                currentState.AccessMask,
                targetState.AccessMask,
                currentState.Layout,
                targetState.Layout,
                NeedsQueueFamilyTransfer(currentState.QueueFamilyIndex, targetState.QueueFamilyIndex)
                    ? currentState.QueueFamilyIndex
                    : VkQueueFamilyIgnored,
                NeedsQueueFamilyTransfer(currentState.QueueFamilyIndex, targetState.QueueFamilyIndex)
                    ? targetState.QueueFamilyIndex
                    : VkQueueFamilyIgnored,
                null);

            vkCmdPipelineBarrier(
                commandBuffer,
                NormalizeSourceStage(currentState),
                NormalizeDestinationStage(targetState),
                VkDependencyFlags.None,
                0,
                null,
                0,
                null,
                1,
                &barrier);

            ThrowOnError(vkEndCommandBuffer(commandBuffer), "vkEndCommandBuffer");

            fence = CreateFence(deviceHandle);
            SubmitAndWait(deviceHandle, new VkQueue(nativeQueue), commandBuffer, fence);
            ApplyImageState(texture, targetState);
            return targetState;
        }
        finally
        {
            if (fence != VkFence.Null)
                vkDestroyFence(deviceHandle, fence, null);

            if (commandBuffer != VkCommandBuffer.Null)
                vkFreeCommandBuffers(deviceHandle, commandPool, 1, &commandBuffer);

            if (commandPool != VkCommandPool.Null)
                vkDestroyCommandPool(deviceHandle, commandPool, null);
        }
    }

    private static void ValidateQueueOwnership(uint sourceQueueFamily, uint targetQueueFamily)
    {
        if (sourceQueueFamily != targetQueueFamily && sourceQueueFamily != VkQueueFamilyIgnored && targetQueueFamily != VkQueueFamilyIgnored)
        {
            throw new NotSupportedException(
                "The shared Avalonia/Stride Vulkan path only supports textures owned by the shared graphics queue family. " +
                "Cross-queue ownership transfer would require source-queue release hooks that Stride does not expose in this integration layer.");
        }
    }

    private static bool NeedsQueueFamilyTransfer(uint sourceQueueFamily, uint targetQueueFamily)
        => sourceQueueFamily != targetQueueFamily
            && sourceQueueFamily != VkQueueFamilyIgnored
            && targetQueueFamily != VkQueueFamilyIgnored;

    private static VkPipelineStageFlags NormalizeSourceStage(VulkanImageState state)
        => state.Layout == VkImageLayout.Undefined || state.PipelineStageMask == VkPipelineStageFlags.None
            ? VkPipelineStageFlags.TopOfPipe
            : state.PipelineStageMask;

    private static VkPipelineStageFlags NormalizeDestinationStage(VulkanImageState state)
        => state.PipelineStageMask == VkPipelineStageFlags.None
            ? VkPipelineStageFlags.AllCommands
            : state.PipelineStageMask;

    internal static void ApplyImageState(Texture texture, VulkanImageState state)
    {
        EnsureTextureReflection(texture);

        _nativeLayoutField!.SetValue(texture, state.Layout);
        _nativeAccessMaskField!.SetValue(texture, state.AccessMask);
        _nativePipelineStageMaskField!.SetValue(texture, state.PipelineStageMask);
    }

    private static VkCommandPool CreateCommandPool(VkDevice deviceHandle, uint queueFamilyIndex)
    {
        var commandPoolInfo = new VkCommandPoolCreateInfo
        {
            sType = VkStructureType.CommandPoolCreateInfo,
            queueFamilyIndex = queueFamilyIndex,
            flags = VkCommandPoolCreateFlags.ResetCommandBuffer,
        };

        ThrowOnError(vkCreateCommandPool(deviceHandle, &commandPoolInfo, null, out var commandPool), "vkCreateCommandPool");
        return commandPool;
    }

    private static VkCommandBuffer AllocateCommandBuffer(VkDevice deviceHandle, VkCommandPool commandPool)
    {
        var allocateInfo = new VkCommandBufferAllocateInfo
        {
            sType = VkStructureType.CommandBufferAllocateInfo,
            level = VkCommandBufferLevel.Primary,
            commandPool = commandPool,
            commandBufferCount = 1,
        };

        VkCommandBuffer commandBuffer = default;
        ThrowOnError(vkAllocateCommandBuffers(deviceHandle, &allocateInfo, &commandBuffer), "vkAllocateCommandBuffers");
        return commandBuffer;
    }

    private static void BeginCommandBuffer(VkCommandBuffer commandBuffer)
    {
        var beginInfo = new VkCommandBufferBeginInfo
        {
            sType = VkStructureType.CommandBufferBeginInfo,
            flags = VkCommandBufferUsageFlags.OneTimeSubmit,
        };

        ThrowOnError(vkBeginCommandBuffer(commandBuffer, &beginInfo), "vkBeginCommandBuffer");
    }

    private static VkFence CreateFence(VkDevice deviceHandle)
    {
        var fenceInfo = new VkFenceCreateInfo
        {
            sType = VkStructureType.FenceCreateInfo,
        };

        ThrowOnError(vkCreateFence(deviceHandle, &fenceInfo, null, out var fence), "vkCreateFence");
        return fence;
    }

    private static void SubmitAndWait(VkDevice deviceHandle, VkQueue queueHandle, VkCommandBuffer commandBuffer, VkFence fence)
    {
        var submitInfo = new VkSubmitInfo
        {
            sType = VkStructureType.SubmitInfo,
            commandBufferCount = 1,
            pCommandBuffers = &commandBuffer,
        };

        ThrowOnError(vkQueueSubmit(queueHandle, 1, &submitInfo, fence), "vkQueueSubmit");
        ThrowOnError(vkWaitForFences(deviceHandle, 1, &fence, true, ulong.MaxValue), "vkWaitForFences");
    }

    private static void ThrowOnError(VkResult result, string operation)
    {
        if (result != VkResult.Success)
        {
            throw new InvalidOperationException($"{operation} failed with Vulkan result {result}.");
        }
    }

    private static IntPtr GetHandleProperty(PropertyInfo prop, object target, string name)
    {
        var value = prop.GetValue(target);
        if (value == null)
            throw new InvalidOperationException(
                $"{name} returned null on {target.GetType().FullName}.");

        return ExtractHandle(value, target, name);
    }

    private static IntPtr GetHandleField(FieldInfo field, object target, string name)
    {
        var value = field.GetValue(target);
        if (value == null)
            throw new InvalidOperationException(
                $"{name} returned null on {target.GetType().FullName}.");

        return ExtractHandle(value, target, name);
    }

    private static IntPtr ExtractHandle(object value, object target, string name)
    {
        // Vortice Vulkan handle wrappers expose a Handle property, but on Linux
        // that property may be an unsigned integer rather than IntPtr.
        var handleProp = value.GetType().GetProperty("Handle",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (handleProp != null)
        {
            var handleValue = handleProp.GetValue(value)
                ?? throw new InvalidOperationException(
                    $"{name}.Handle returned null on {target.GetType().FullName}.");
            return ConvertHandleToIntPtr(handleValue, target, name);
        }

        return ConvertHandleToIntPtr(value, target, name);
    }

    private static IntPtr ConvertHandleToIntPtr(object value, object target, string name)
    {
        if (value.GetType().IsEnum)
        {
            value = Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()))!;
        }

        return value switch
        {
            IntPtr ptr => ptr,
            UIntPtr uptr => new IntPtr(unchecked((long)uptr.ToUInt64())),
            long longValue => new IntPtr(longValue),
            ulong ulongValue => new IntPtr(unchecked((long)ulongValue)),
            int intValue => new IntPtr(intValue),
            uint uintValue => new IntPtr(unchecked((int)uintValue)),
            short shortValue => new IntPtr(shortValue),
            ushort ushortValue => new IntPtr(ushortValue),
            byte byteValue => new IntPtr(byteValue),
            sbyte sbyteValue => new IntPtr(sbyteValue),
            _ => throw new InvalidOperationException(
                $"{name} on {target.GetType().FullName} exposed unsupported Vulkan handle type '{value.GetType().FullName}'.")
        };
    }

    private static uint GetQueueFamilyIndex(GraphicsDevice device)
    {
        // Try field first
        if (_queueFamilyField != null)
        {
            var val = _queueFamilyField.GetValue(device);
            if (val != null) return Convert.ToUInt32(val);
        }

        // Try property (with hierarchy walk)
        var type = device.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var prop = FindPropertyInHierarchy(type, "GraphicsQueueFamilyIndex", flags)
            ?? FindPropertyInHierarchy(type, "CommandQueueFamilyIndex", flags);
        if (prop != null)
        {
            var val = prop.GetValue(device);
            if (val != null) return Convert.ToUInt32(val);
        }

        // Default to 0 — the most common graphics queue family index
        return 0;
    }

    // ── Vulkan library loading ──────────────────────────────────────

    private static unsafe void LoadVulkanLibrary()
    {
        if (_vulkanLib != IntPtr.Zero) return;

        // Try platform-specific library names
        if (!NativeLibrary.TryLoad("libvulkan.so.1", out _vulkanLib) &&
            !NativeLibrary.TryLoad("libvulkan.so", out _vulkanLib) &&
            !NativeLibrary.TryLoad("vulkan-1", out _vulkanLib))
        {
            throw new DllNotFoundException(
                "Could not load Vulkan library (tried libvulkan.so.1, libvulkan.so, vulkan-1). " +
                "Ensure Vulkan runtime is installed.");
        }

        if (!NativeLibrary.TryGetExport(_vulkanLib, "vkGetInstanceProcAddr", out var gipaPtr))
            throw new DllNotFoundException(
                "vkGetInstanceProcAddr not found in Vulkan library.");

        _vkGetInstanceProcAddr = (delegate* unmanaged<IntPtr, byte*, IntPtr>)gipaPtr;

        // vkGetDeviceProcAddr is optional — resolve via instance proc
        if (!NativeLibrary.TryGetExport(_vulkanLib, "vkGetDeviceProcAddr", out var gdpaPtr))
        {
            // Get it through vkGetInstanceProcAddr
            var name = "vkGetDeviceProcAddr"u8;
            fixed (byte* pName = name)
            {
                // We need an instance, but we don't have one yet at load time.
                // We'll resolve it lazily in GetProcedureAddress.
                gdpaPtr = IntPtr.Zero;
            }
        }

        if (gdpaPtr != IntPtr.Zero)
            _vkGetDeviceProcAddr = (delegate* unmanaged<IntPtr, byte*, IntPtr>)gdpaPtr;
    }

    /// <summary>
    /// SkiaSharp's Vulkan procedure address resolver.
    /// Intercepts vkCreateImage to capture VkImage handles created by Skia.
    /// </summary>
    private static unsafe IntPtr GetProcedureAddress(string name, IntPtr instance, IntPtr device)
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes(name + '\0');
        IntPtr addr = IntPtr.Zero;
        fixed (byte* pName = utf8)
        {
            // Try device-level first (more specific)
            if (device != IntPtr.Zero && _vkGetDeviceProcAddr != null)
            {
                addr = _vkGetDeviceProcAddr(device, pName);
                if (addr != IntPtr.Zero)
                {
                    if (name == "vkCreateImage")
                    {
                        _realVkCreateImagePtr = addr;
                        var interceptor = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, VkImageCreateInfo*, VkAllocationCallbacks*, ulong*, VkResult>)&InterceptVkCreateImage;
                        if (!_loggedInterception)
                        {
                            Console.Error.WriteLine("[Stride.Avalonia] vkCreateImage interceptor installed for VkImage capture");
                            _loggedInterception = true;
                        }
                        return interceptor;
                    }
                    return addr;
                }
            }

            // Fall back to instance-level
            if (instance != IntPtr.Zero && _vkGetInstanceProcAddr != null)
            {
                addr = _vkGetInstanceProcAddr(instance, pName);
                if (addr != IntPtr.Zero)
                {
                    if (name == "vkCreateImage")
                    {
                        _realVkCreateImagePtr = addr;
                        return (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, VkImageCreateInfo*, VkAllocationCallbacks*, ulong*, VkResult>)&InterceptVkCreateImage;
                    }
                    return addr;
                }
            }

            // Global-level (pre-instance functions like vkCreateInstance)
            if (_vkGetInstanceProcAddr != null)
            {
                var result = _vkGetInstanceProcAddr(IntPtr.Zero, pName);
                if (result != IntPtr.Zero) return result;
            }
        }

        return IntPtr.Zero;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe VkResult InterceptVkCreateImage(IntPtr device, VkImageCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator, ulong* pImage)
    {
        Interlocked.Increment(ref _interceptCallCount);
        var realFn = (delegate* unmanaged[Cdecl]<IntPtr, VkImageCreateInfo*, VkAllocationCallbacks*, ulong*, VkResult>)_realVkCreateImagePtr;
        var result = realFn(device, pCreateInfo, pAllocator, pImage);
        if (result == VkResult.Success && _captureMode)
        {
            var w = pCreateInfo->extent.width;
            var h = pCreateInfo->extent.height;
            var usage = pCreateInfo->usage;

            // Check if this VkImage matches the active capture dimensions.
            // Only capture images with ColorAttachment usage — this is the
            // render target Skia draws into.  Skia may also create a
            // depth/stencil buffer with the same dimensions; capturing that
            // would cause the blit to read from the wrong image.
            if (w == _captureWidth && h == _captureHeight
                && (usage & VkImageUsageFlags.ColorAttachment) != 0)
            {
                _capturedImageHandle = *pImage;
            }
        }
        return result;
    }

    /// <summary>
    /// Begins VkImage capture for a single surface.  Returns a token that
    /// must be passed to <see cref="EndVkImageCapture"/> to claim the result.
    /// Only one capture may be active at a time (compositor renders sequentially).
    /// </summary>
    internal static long BeginVkImageCapture(uint width, uint height)
    {
        var token = Interlocked.Increment(ref _captureToken);
        _activeCaptureToken = token;
        _captureWidth = width;
        _captureHeight = height;
        _capturedImageHandle = 0;
        _captureMode = true;
        return token;
    }

    /// <summary>
    /// Ends VkImage capture and returns the captured handle (0 if none).
    /// The token must match the one from <see cref="BeginVkImageCapture"/>.
    /// </summary>
    internal static ulong EndVkImageCapture(long token)
    {
        if (_activeCaptureToken != token)
            return 0; // stale/mismatched — another capture superseded this one

        _captureMode = false;
        var handle = _capturedImageHandle;
        _capturedImageHandle = 0;
        return handle;
    }

    /// <summary>
    /// Performs a GPU-to-GPU copy from a Skia-managed VkImage to a Stride-owned VkImage.
    /// </summary>
    internal static void GpuCopyImage(GraphicsDevice device, ulong srcImage, ulong dstImage, int width, int height)
    {
        EnsureReflectionCache(device);
        LoadVulkanLibrary();

        var queueLock = GetQueueLock(device);
        Monitor.Enter(queueLock);
        try
        {
            var nativeDevice = GetHandleProperty(_nativeDeviceProp!, device, "NativeDevice");
            var nativeQueue = GetHandleField(_nativeCommandQueueField!, device, "NativeCommandQueue");
            var deviceHandle = new VkDevice(nativeDevice);
            var commandPool = VkCommandPool.Null;
            var commandBuffer = VkCommandBuffer.Null;
            var fence = VkFence.Null;

            try
            {
                commandPool = CreateCommandPool(deviceHandle, GraphicsQueueFamilyIndex);
                commandBuffer = AllocateCommandBuffer(deviceHandle, commandPool);
                BeginCommandBuffer(commandBuffer);

                var srcImageHandle = new VkImage(srcImage);
                var dstImageHandle = new VkImage(dstImage);

                // Barriers: src COLOR_ATTACHMENT_OPTIMAL → TRANSFER_SRC_OPTIMAL,
                //           dst UNDEFINED → TRANSFER_DST_OPTIMAL
                var barriers = stackalloc VkImageMemoryBarrier[2];
                barriers[0] = new VkImageMemoryBarrier(
                    srcImageHandle,
                    new VkImageSubresourceRange(VkImageAspectFlags.Color),
                    VkAccessFlags.ColorAttachmentWrite,
                    VkAccessFlags.TransferRead,
                    VkImageLayout.ColorAttachmentOptimal,
                    VkImageLayout.TransferSrcOptimal,
                    VkQueueFamilyIgnored,
                    VkQueueFamilyIgnored,
                    null);
                barriers[1] = new VkImageMemoryBarrier(
                    dstImageHandle,
                    new VkImageSubresourceRange(VkImageAspectFlags.Color),
                    VkAccessFlags.None,
                    VkAccessFlags.TransferWrite,
                    VkImageLayout.Undefined,
                    VkImageLayout.TransferDstOptimal,
                    VkQueueFamilyIgnored,
                    VkQueueFamilyIgnored,
                    null);

                vkCmdPipelineBarrier(
                    commandBuffer,
                    VkPipelineStageFlags.ColorAttachmentOutput,
                    VkPipelineStageFlags.Transfer,
                    VkDependencyFlags.None,
                    0, null,
                    0, null,
                    2, barriers);

                // vkCmdBlitImage — copies with Y-flip because Skia's
                // managed Vulkan surfaces store pixels bottom-up.
                var blit = new VkImageBlit
                {
                    srcSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
                    dstSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
                };
                blit.srcOffsets[0] = new VkOffset3D(0, height, 0);
                blit.srcOffsets[1] = new VkOffset3D(width, 0, 1);
                blit.dstOffsets[0] = new VkOffset3D(0, 0, 0);
                blit.dstOffsets[1] = new VkOffset3D(width, height, 1);
                vkCmdBlitImage(
                    commandBuffer,
                    srcImageHandle, VkImageLayout.TransferSrcOptimal,
                    dstImageHandle, VkImageLayout.TransferDstOptimal,
                    1, &blit,
                    VkFilter.Nearest);

                // Barriers: src TRANSFER_SRC_OPTIMAL → COLOR_ATTACHMENT_OPTIMAL,
                //           dst TRANSFER_DST_OPTIMAL → SHADER_READ_ONLY_OPTIMAL
                barriers[0] = new VkImageMemoryBarrier(
                    srcImageHandle,
                    new VkImageSubresourceRange(VkImageAspectFlags.Color),
                    VkAccessFlags.TransferRead,
                    VkAccessFlags.ColorAttachmentWrite,
                    VkImageLayout.TransferSrcOptimal,
                    VkImageLayout.ColorAttachmentOptimal,
                    VkQueueFamilyIgnored,
                    VkQueueFamilyIgnored,
                    null);
                barriers[1] = new VkImageMemoryBarrier(
                    dstImageHandle,
                    new VkImageSubresourceRange(VkImageAspectFlags.Color),
                    VkAccessFlags.TransferWrite,
                    VkAccessFlags.InputAttachmentRead | VkAccessFlags.ShaderRead,
                    VkImageLayout.TransferDstOptimal,
                    VkImageLayout.ShaderReadOnlyOptimal,
                    VkQueueFamilyIgnored,
                    VkQueueFamilyIgnored,
                    null);

                vkCmdPipelineBarrier(
                    commandBuffer,
                    VkPipelineStageFlags.Transfer,
                    VkPipelineStageFlags.ColorAttachmentOutput | VkPipelineStageFlags.FragmentShader,
                    VkDependencyFlags.None,
                    0, null,
                    0, null,
                    2, barriers);

                ThrowOnError(vkEndCommandBuffer(commandBuffer), "vkEndCommandBuffer");

                fence = CreateFence(deviceHandle);
                SubmitAndWait(deviceHandle, new VkQueue(nativeQueue), commandBuffer, fence);
            }
            finally
            {
                if (fence != VkFence.Null)
                    vkDestroyFence(deviceHandle, fence, null);
                if (commandBuffer != VkCommandBuffer.Null)
                    vkFreeCommandBuffers(deviceHandle, commandPool, 1, &commandBuffer);
                if (commandPool != VkCommandPool.Null)
                    vkDestroyCommandPool(deviceHandle, commandPool, null);
            }
        }
        finally
        {
            Monitor.Exit(queueLock);
        }
    }

}
