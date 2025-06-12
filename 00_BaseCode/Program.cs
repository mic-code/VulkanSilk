using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace VulkanSilk;

internal class HelloTriangleApplication
{
    public int Width = 800;
    public int Height = 600;

    IWindow window;

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

    }

    void MainLoop()
    {
        window.Run();
    }

    void Cleanup()
    {
        window.Dispose();
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