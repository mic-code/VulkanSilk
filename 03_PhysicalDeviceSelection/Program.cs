using Silk.NET.Core;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Vulkan;
using Silk.NET.Core.Native;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan.Extensions.EXT;

namespace VulkanSilk;


unsafe class HelloTriangleApplication
{
#if DEBUG
    const bool enableValidationLayers = true;
#else
    const bool enableValidationLayers = false;
#endif

    public int Width = 800;
    public int Height = 600;

    string[] validationLayers = ["VK_LAYER_KHRONOS_validation"];

    Vk vk;
    IWindow window;
    Instance instance;
    PhysicalDevice physicalDevice;

    ExtDebugUtils debugUtils;
    DebugUtilsMessengerEXT debugMessenger;


    public void Run()
    {
        InitWindow();
        InitVulkan();
        MainLoop();
        Cleanup();
    }

    void InitWindow()
    {
        WindowOptions options = WindowOptions.DefaultVulkan with
        {
            Size = new Vector2D<int>(Width, Height),
            WindowBorder = WindowBorder.Resizable,
        };

        window = Window.Create(options);
        window.Initialize();
    }

    void InitVulkan()
    {
        CreateInstance();
        SetupDebugMessenger();
        PickPhysicalDevice();
    }

    void MainLoop()
    {
        window.Run();
    }

    void Cleanup()
    {
        if (enableValidationLayers)
            debugUtils.DestroyDebugUtilsMessenger(instance, debugMessenger, null);
        vk.DestroyInstance(instance, null);
        vk.Dispose();
        window.Dispose();
    }

    void CreateInstance()
    {
        vk = Vk.GetApi();

        if (enableValidationLayers && !CheckValidationLayerSupport())
            throw new Exception("Validation layer not available");


        var appInfo = new ApplicationInfo()
        {
            SType = StructureType.ApplicationInfo,

            PApplicationName = (byte*)SilkMarshal.StringToPtr("Hello Triangle"),
            ApplicationVersion = new Version32(1, 0, 0),
            PEngineName = (byte*)SilkMarshal.StringToPtr("No Engine"),
            EngineVersion = new Version32(1, 0, 0),
            ApiVersion = Vk.Version12
        };

        var createInfo = new InstanceCreateInfo()
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo
        };

        var extensions = GetRequiredExtensions();
        createInfo.EnabledExtensionCount = (uint)extensions.Length;
        createInfo.PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(extensions);

        if (enableValidationLayers)
        {
            createInfo.EnabledLayerCount = (uint)validationLayers.Length;
            createInfo.PpEnabledLayerNames = (byte**)SilkMarshal.StringArrayToPtr(validationLayers);

            var debugCreateInfo = new DebugUtilsMessengerCreateInfoEXT();
            PopulateDebugMessengerCreateInfo(ref debugCreateInfo);
            createInfo.PNext = &debugCreateInfo;
        }
        else
        {
            createInfo.EnabledLayerCount = 0;
            createInfo.PNext = null;
        }

        if (vk.CreateInstance(in createInfo, null, out instance) != Result.Success)
            throw new Exception("failed to create instance!");

        SilkMarshal.FreeString((IntPtr)appInfo.PApplicationName);
        SilkMarshal.FreeString((IntPtr)appInfo.PEngineName);
        SilkMarshal.Free((nint)createInfo.PpEnabledExtensionNames);

        if (enableValidationLayers)
            SilkMarshal.Free((nint)createInfo.PpEnabledLayerNames);
    }

    void PopulateDebugMessengerCreateInfo(ref DebugUtilsMessengerCreateInfoEXT createInfo)
    {
        createInfo.SType = StructureType.DebugUtilsMessengerCreateInfoExt;

        createInfo.MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt |
                                     DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                                     DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt;

        createInfo.MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                                 DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt |
                                 DebugUtilsMessageTypeFlagsEXT.ValidationBitExt;

        createInfo.PfnUserCallback = (DebugUtilsMessengerCallbackFunctionEXT)DebugCallback;
    }

    void SetupDebugMessenger()
    {
        if (!enableValidationLayers) return;
        if (!vk.TryGetInstanceExtension(instance, out debugUtils)) return;

        DebugUtilsMessengerCreateInfoEXT createInfo = new();
        PopulateDebugMessengerCreateInfo(ref createInfo);

        if (debugUtils.CreateDebugUtilsMessenger(instance, in createInfo, null, out debugMessenger) != Result.Success)
            throw new Exception("failed to set up debug messenger!");
    }

    void PickPhysicalDevice()
    {
        var devices = vk.GetPhysicalDevices(instance);

        foreach (var device in devices)
            if (IsDeviceSuitable(device))
            {
                physicalDevice = device;
                break;
            }

        if (physicalDevice.Handle == 0)
            throw new Exception("failed to find a suitable GPU!");
    }

    bool IsDeviceSuitable(PhysicalDevice device)
    {
        var indices = FindQueueFamilies(device);

        //get device name
        PhysicalDeviceProperties properties;
        vk.GetPhysicalDeviceProperties(device, &properties);
        Console.Write(SilkMarshal.PtrToString((IntPtr)properties.DeviceName));

        return indices.IsComplete();
    }

    QueueFamilyIndices FindQueueFamilies(PhysicalDevice device)
    {
        var indices = new QueueFamilyIndices();

        uint queueFamilityCount = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(device, ref queueFamilityCount, null);

        var queueFamilies = new QueueFamilyProperties[queueFamilityCount];
        fixed (QueueFamilyProperties* queueFamiliesPtr = queueFamilies)
            vk.GetPhysicalDeviceQueueFamilyProperties(device, ref queueFamilityCount, queueFamiliesPtr);


        uint i = 0;
        foreach (var queueFamily in queueFamilies)
        {
            if (queueFamily.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
                indices.GraphicsFamily = i;

            if (indices.IsComplete())
                break;

            i++;
        }

        return indices;
    }

    string[] GetRequiredExtensions()
    {
        var glfwExtensions = window.VkSurface.GetRequiredExtensions(out var glfwExtensionCount);
        var extensions = SilkMarshal.PtrToStringArray((nint)glfwExtensions, (int)glfwExtensionCount);

        if (enableValidationLayers)
            return extensions.Append(ExtDebugUtils.ExtensionName).ToArray();

        return extensions;
    }

    bool CheckValidationLayerSupport()
    {
        uint layerCount = 0;
        vk.EnumerateInstanceLayerProperties(ref layerCount, null);

        var availableLayers = new LayerProperties[layerCount];
        fixed (LayerProperties* availableLayersPtr = availableLayers)
            vk.EnumerateInstanceLayerProperties(ref layerCount, availableLayersPtr);

        var availableLayerNames = availableLayers.Select(layer => SilkMarshal.PtrToString((IntPtr)layer.LayerName)).ToHashSet();

        return validationLayers.All(availableLayerNames.Contains);
    }

    uint DebugCallback(DebugUtilsMessageSeverityFlagsEXT messageSeverity, DebugUtilsMessageTypeFlagsEXT messageTypes, DebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
    {
        Console.WriteLine($"[validation layer] " + Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage));

        return Vk.False;
    }
}

struct QueueFamilyIndices
{
    public uint? GraphicsFamily { get; set; }
    public bool IsComplete()
    {
        return GraphicsFamily.HasValue;
    }
}

internal class Program
{
    static void Main(string[] args)
    {
        var app = new HelloTriangleApplication();
        app.Run();
    }
}