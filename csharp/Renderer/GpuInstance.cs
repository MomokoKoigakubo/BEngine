using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;

namespace IdleL.Rendering;

// Owns the shared Vk api object, the VkInstance, the debug messenger, and the KHR_surface extension.
unsafe class GpuInstance : IDisposable
{
    public Vk Vk { get; }
    public Instance Instance;
    public KhrSurface KhrSurface = null!;

    ExtDebugUtils? debugUtils;
    DebugUtilsMessengerEXT debugMessenger;
    DebugUtilsMessengerCallbackFunctionEXT? keepAlive;   // pin the delegate so the GC doesn't collect it

    static readonly string[] ValidationLayers = { "VK_LAYER_KHRONOS_validation" };
#if DEBUG
    bool enableValidation = true;
#else
    bool enableValidation = false;
#endif

    public GpuInstance(IWindow window)
    {
        Vk = Vk.GetApi();
        if (enableValidation && !CheckValidationLayerSupport()) enableValidation = false;
        CreateInstance(window);
        SetupDebugMessenger();
        if (!Vk.TryGetInstanceExtension(Instance, out KhrSurface))
            throw new Exception("VK_KHR_surface not available");
    }

    void CreateInstance(IWindow window)
    {
        var appInfo = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*)SilkMarshal.StringToPtr("IdleL"),
            ApplicationVersion = new Version32(0, 0, 1),
            PEngineName = (byte*)SilkMarshal.StringToPtr("BEngine"),
            EngineVersion = new Version32(0, 0, 1),
            ApiVersion = Vk.Version13
        };

        string[] extensions = GetRequiredExtensions(window);
        var createInfo = new InstanceCreateInfo
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo,
            EnabledExtensionCount = (uint)extensions.Length,
            PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(extensions),
            EnabledLayerCount = 0
        };
        if (enableValidation)
        {
            createInfo.EnabledLayerCount = (uint)ValidationLayers.Length;
            createInfo.PpEnabledLayerNames = (byte**)SilkMarshal.StringArrayToPtr(ValidationLayers);
        }

        VkCheck.Check(Vk.CreateInstance(in createInfo, null, out Instance), "vkCreateInstance");

        SilkMarshal.Free((nint)appInfo.PApplicationName);
        SilkMarshal.Free((nint)appInfo.PEngineName);
        SilkMarshal.Free((nint)createInfo.PpEnabledExtensionNames);
        if (enableValidation) SilkMarshal.Free((nint)createInfo.PpEnabledLayerNames);
    }

    string[] GetRequiredExtensions(IWindow window)
    {
        byte** windowExt = window.VkSurface!.GetRequiredExtensions(out uint count);
        var exts = SilkMarshal.PtrToStringArray((nint)windowExt, (int)count);
        return enableValidation ? exts.Append(ExtDebugUtils.ExtensionName).ToArray() : exts;
    }

    bool CheckValidationLayerSupport()
    {
        uint count = 0;
        Vk.EnumerateInstanceLayerProperties(ref count, null);
        var available = new LayerProperties[count];
        fixed (LayerProperties* p = available) Vk.EnumerateInstanceLayerProperties(ref count, p);

        var names = new HashSet<string>();
        for (int i = 0; i < available.Length; i++)
            fixed (byte* n = available[i].LayerName)
            {
                string? s = SilkMarshal.PtrToString((nint)n);
                if (s != null) names.Add(s);
            }
        foreach (var v in ValidationLayers) if (!names.Contains(v)) return false;
        return true;
    }

    void SetupDebugMessenger()
    {
        if (!enableValidation) return;
        if (!Vk.TryGetInstanceExtension(Instance, out debugUtils)) return;

        keepAlive = DebugCallback;
        var info = new DebugUtilsMessengerCreateInfoEXT
        {
            SType = StructureType.DebugUtilsMessengerCreateInfoExt,
            MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt
                            | DebugUtilsMessageSeverityFlagsEXT.WarningBitExt
                            | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
            MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt
                        | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt
                        | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
            PfnUserCallback = new PfnDebugUtilsMessengerCallbackEXT(keepAlive)
        };
        debugUtils!.CreateDebugUtilsMessenger(Instance, in info, null, out debugMessenger);
    }

    static uint DebugCallback(DebugUtilsMessageSeverityFlagsEXT severity, DebugUtilsMessageTypeFlagsEXT type,
        DebugUtilsMessengerCallbackDataEXT* data, void* userData)
    {
        Console.Error.WriteLine("validation: " + SilkMarshal.PtrToString((nint)data->PMessage));
        return Vk.False;
    }

    public void Dispose()
    {
        if (debugUtils != null) debugUtils.DestroyDebugUtilsMessenger(Instance, debugMessenger, null);
        KhrSurface.Dispose();
        Vk.DestroyInstance(Instance, null);
        Vk.Dispose();
    }
}
