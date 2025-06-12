using Silk.NET.Core;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Vulkan;
using Silk.NET.Core.Native;

namespace VulkanSilk;

unsafe class HelloTriangleApplication
{
    public int Width = 800;
    public int Height = 600;

    IWindow window;
    Vk vk;
    Instance instance;

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
    }

    void MainLoop()
    {
        window.Run();
    }

    void Cleanup()
    {
        vk.DestroyInstance(instance, null);
        vk.Dispose();
        window.Dispose();
    }

    void CreateInstance()
    {
        vk = Vk.GetApi();

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

        var glfwExtensions = window!.VkSurface!.GetRequiredExtensions(out var glfwExtensionCount);

        createInfo.EnabledExtensionCount = glfwExtensionCount;
        createInfo.PpEnabledExtensionNames = glfwExtensions;
        createInfo.EnabledLayerCount = 0;

        if (vk.CreateInstance(in createInfo, null, out instance) != Result.Success)
        {
            throw new Exception("failed to create instance!");
        }

        SilkMarshal.FreeString((IntPtr)appInfo.PApplicationName);
        SilkMarshal.FreeString((IntPtr)appInfo.PEngineName);
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