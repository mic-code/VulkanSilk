using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using StbImageSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml.Schema;
using Buffer = Silk.NET.Vulkan.Buffer;

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

    Vertex[] vertices =
    [
        new Vertex { pos = new Vector2D<float>(-0.5f,-0.5f), color = new Vector3D<float>(1.0f, 0.0f, 0.0f),texCoord = new Vector2D<float>(1.0f,0.0f) },
        new Vertex { pos = new Vector2D<float>(0.5f,-0.5f), color = new Vector3D<float>(0.0f, 1.0f, 0.0f),texCoord = new Vector2D<float>(0.0f,0.0f) },
        new Vertex { pos = new Vector2D<float>(0.5f,0.5f), color = new Vector3D<float>(0.0f, 0.0f, 1.0f) ,texCoord = new Vector2D<float>(0.0f,1.0f) },
        new Vertex { pos = new Vector2D<float>(-0.5f,0.5f), color = new Vector3D<float>(1.0f, 1.0f, 1.0f),texCoord = new Vector2D<float>(1.0f,1.0f) },
    ];

    ushort[] indices = [0, 1, 2, 2, 3, 0];

    string[] validationLayers = ["VK_LAYER_KHRONOS_validation"];
    readonly string[] deviceExtensions = [KhrSwapchain.ExtensionName];
    IWindow window;

    Vk vk;
    Instance instance;

    ExtDebugUtils debugUtils;
    DebugUtilsMessengerEXT debugMessenger;

    PhysicalDevice physicalDevice;
    Device device;
    Queue graphicsQueue;
    Queue presentQueue;

    KhrSurface khrSurface;
    SurfaceKHR surface;

    KhrSwapchain? khrSwapChain;
    SwapchainKHR swapChain;
    Image[] swapChainImages;
    Format swapChainImageFormat;
    Extent2D swapChainExtent;
    ImageView[] swapChainImageViews;
    Framebuffer[] swapChainFramebuffers;
    RenderPass renderPass;
    DescriptorSetLayout descriptorSetLayout;
    PipelineLayout pipelineLayout;
    Pipeline graphicPipeline;
    CommandPool commandPool;
    CommandBuffer[] commandBuffers;
    DescriptorPool descriptorPool;
    DescriptorSet[] descriptorSets;

    Semaphore[] imageAvailableSemaphores;
    Semaphore[] renderFinishedSemaphores;
    Fence[] inFlightFences;
    Buffer vertexBuffer;
    DeviceMemory vertexBufferMemory;
    Buffer indexBuffer;
    DeviceMemory indexBufferMemory;
    Buffer[] uniformBuffers;
    DeviceMemory[] uniformBuffersMemory;
    void*[] uniformBuffersMapped;
    Image textureIamge;
    DeviceMemory textureImageMemory;
    ImageView textureImageView;
    Sampler textureSampler;

    bool framebufferResized = false;
    uint currentFrame = 0;

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
        window.FramebufferResize += x => framebufferResized = true;
    }

    void InitVulkan()
    {
        CreateInstance();
        SetupDebugMessenger();
        CreateSurface();
        PickPhysicalDevice();
        CreateLogicalDevice();
        CreateSwapChain();
        CreateImageViews();
        CreateRenderPass();
        CreateDescriptorSetLayout();
        CreateGraphicsPipeline();
        CreateFramebuffers();
        CreateCommandPool();
        CreateTextureImage();
        CreateTextureImageView();
        CreateTextureSampler();
        CreateVertexBuffer();
        CreateIndexBuffer();
        CreateUniformBuffer();
        CreateDescriptorPool();
        CreateDescriptorSets();
        CreateCommandBuffers();
        CreateSyncObjects();
    }

    void MainLoop()
    {
        while (!window.IsClosing)
        {
            window.DoEvents();
            window.DoUpdate();
            window.DoRender();
            DrawFrame();
        }

        vk.DeviceWaitIdle(device);
    }

    void RecreateSwapChain()
    {
        while (window.FramebufferSize.X == 0 || window.FramebufferSize.Y == 0)
            window.DoEvents();

        vk.DeviceWaitIdle(device);
        CleanupSwapChain();

        CreateSwapChain();
        CreateImageViews();
        CreateFramebuffers();
    }

    void CleanupSwapChain()
    {
        foreach (var frameBuffer in swapChainFramebuffers)
            vk.DestroyFramebuffer(device, frameBuffer, null);

        foreach (var imageView in swapChainImageViews!)
            vk.DestroyImageView(device, imageView, null);

        khrSwapChain.DestroySwapchain(device, swapChain, null);
    }

    void Cleanup()
    {
        CleanupSwapChain();

        vk.DestroySampler(device, textureSampler, null);
        vk.DestroyImageView(device, textureImageView, null);
        vk.DestroyImage(device, textureIamge, null);
        vk.FreeMemory(device, textureImageMemory, null);

        for (int i = 0; i < uniformBuffers.Length; i++)
        {
            vk.DestroyBuffer(device, uniformBuffers[i], null);
            vk.FreeMemory(device, uniformBuffersMemory[i], null);
        }
        vk.DestroyDescriptorPool(device, descriptorPool, null);
        vk.DestroyDescriptorSetLayout(device, descriptorSetLayout, null);
        vk.DestroyBuffer(device, indexBuffer, null);
        vk.FreeMemory(device, indexBufferMemory, null);

        vk.DestroyBuffer(device, vertexBuffer, null);
        vk.FreeMemory(device, vertexBufferMemory, null);

        for (int i = 0; i < swapChainImages.Length; i++)
        {
            vk.DestroySemaphore(device, imageAvailableSemaphores[i], null);
            vk.DestroySemaphore(device, renderFinishedSemaphores[i], null);
            vk.DestroyFence(device, inFlightFences[i], null);
        }

        vk.DestroyCommandPool(device, commandPool, null);
        vk.DestroyPipeline(device, graphicPipeline, null);
        vk.DestroyPipelineLayout(device, pipelineLayout, null);
        vk.DestroyRenderPass(device, renderPass, null);
        vk.DestroyDevice(device, null);

        if (enableValidationLayers)
            debugUtils.DestroyDebugUtilsMessenger(instance, debugMessenger, null);

        khrSurface.DestroySurface(instance, surface, null);
        vk.DestroyInstance(instance, null);
        vk.Dispose();
        window.Dispose();
    }

    void DrawFrame()
    {
        vk.WaitForFences(device, 1, in inFlightFences[currentFrame], true, ulong.MaxValue);
        vk.ResetFences(device, 1, in inFlightFences[currentFrame]);

        uint imageIndex = 0;
        var acquireImageResult = khrSwapChain.AcquireNextImage(device, swapChain, ulong.MaxValue, imageAvailableSemaphores[currentFrame], default, ref imageIndex);

        if (acquireImageResult == Result.ErrorOutOfDateKhr)
        {
            RecreateSwapChain();
            return;
        }
        else if (acquireImageResult != Result.Success && acquireImageResult != Result.SuboptimalKhr)
        {
            throw new Exception("failed to acquire swap chain image!");
        }

        vk.ResetCommandBuffer(commandBuffers[currentFrame], CommandBufferResetFlags.None);
        RecordCommandBuffer(commandBuffers[currentFrame], imageIndex);

        var waitSemaphores = stackalloc[] { imageAvailableSemaphores[currentFrame] };
        var waitStages = stackalloc[] { PipelineStageFlags.ColorAttachmentOutputBit };
        var signalSemaphores = stackalloc[] { renderFinishedSemaphores[currentFrame] };

        updateUniformBuffer(currentFrame);

        var buffer = commandBuffers[currentFrame];
        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = waitSemaphores,
            PWaitDstStageMask = waitStages,
            CommandBufferCount = 1,
            PCommandBuffers = &buffer,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = signalSemaphores
        };

        if (vk.QueueSubmit(graphicsQueue, 1, in submitInfo, inFlightFences[currentFrame]) != Result.Success)
            throw new Exception("failed to submit draw command buffer!");

        var swapChains = stackalloc[] { swapChain };

        PresentInfoKHR presentInfoKHR = new()
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = signalSemaphores,
            PSwapchains = swapChains,
            PImageIndices = &imageIndex,
            SwapchainCount = 1
        };

        var queuePresetResult = khrSwapChain.QueuePresent(presentQueue, in presentInfoKHR);
        if (queuePresetResult == Result.ErrorOutOfDateKhr || queuePresetResult == Result.SuboptimalKhr || framebufferResized)
        {
            framebufferResized = false;
            RecreateSwapChain();
        }
        else if (queuePresetResult != Result.Success)
        {
            throw new Exception("failed to present swap chain image!");
        }

        currentFrame = (currentFrame + 1) % (uint)swapChainImages.Length;
    }

    void updateUniformBuffer(uint currentFrame)
    {
        var time = (float)window!.Time;
        static float Radians(float angle) => angle * MathF.PI / 180f;

        UniformBufferObject ubo = new()
        {
            model = Matrix4X4<float>.Identity * Matrix4X4.CreateFromAxisAngle(new Vector3D<float>(0, 0, 1), time * Radians(90.0f)),
            view = Matrix4X4.CreateLookAt(new Vector3D<float>(2, 2, 2), new Vector3D<float>(0, 0, 0), new Vector3D<float>(0, 0, 1)),
            proj = Matrix4X4.CreatePerspectiveFieldOfView(Radians(45.0f), (float)swapChainExtent.Width / swapChainExtent.Height, 0.1f, 10.0f),
        };
        ubo.proj.M22 *= -1;

        var data = uniformBuffersMapped[currentFrame];
        new Span<UniformBufferObject>(data, 1)[0] = ubo;
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

    void CreateSurface()
    {
        if (!vk.TryGetInstanceExtension(instance, out khrSurface))
            throw new NotSupportedException("KHR_surface extension not found.");

        surface = window.VkSurface.Create<AllocationCallbacks>(instance.ToHandle(), null).ToSurface();
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
        var extensionsSupported = CheckDeviceExtensionsSupport(device);

        bool swapChainAdequate = false;
        if (extensionsSupported)
        {
            var swapChainSupport = QuerySwapChainSupport(device);
            swapChainAdequate = swapChainSupport.Formats.Any() && swapChainSupport.PresentModes.Any();
        }

        //get device name
        //PhysicalDeviceProperties properties;
        //vk.GetPhysicalDeviceProperties(device, &properties);
        //Console.Write(SilkMarshal.PtrToString((IntPtr)properties.DeviceName));

        var features = vk.GetPhysicalDeviceFeature(device);
        return indices.IsComplete() && extensionsSupported && swapChainAdequate && features.SamplerAnisotropy;
    }

    bool CheckDeviceExtensionsSupport(PhysicalDevice device)
    {
        uint extentionsCount = 0;
        vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref extentionsCount, null);

        var availableExtensions = new ExtensionProperties[extentionsCount];
        fixed (ExtensionProperties* availableExtensionsPtr = availableExtensions)
            vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref extentionsCount, availableExtensionsPtr);

        var availableExtensionNames = availableExtensions.Select(extension => SilkMarshal.PtrToString((IntPtr)extension.ExtensionName)).ToHashSet();

        return deviceExtensions.All(availableExtensionNames.Contains);

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

            khrSurface.GetPhysicalDeviceSurfaceSupport(device, i, surface, out var presentSupport);

            if (presentSupport)
                indices.PresentFamily = i;

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

    void CreateLogicalDevice()
    {
        var indices = FindQueueFamilies(physicalDevice);

        var uniqueQueueFamilies = new[] { indices.GraphicsFamily!.Value, indices.PresentFamily!.Value };
        uniqueQueueFamilies = uniqueQueueFamilies.Distinct().ToArray();

        using var mem = GlobalMemory.Allocate(uniqueQueueFamilies.Length * sizeof(DeviceQueueCreateInfo));
        var queueCreateInfos = (DeviceQueueCreateInfo*)Unsafe.AsPointer(ref mem.GetPinnableReference());

        float queuePriority = 1.0f;
        for (int i = 0; i < uniqueQueueFamilies.Length; i++)
        {
            queueCreateInfos[i] = new()
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = uniqueQueueFamilies[i],
                QueueCount = 1,
                PQueuePriorities = &queuePriority
            };
        }

        PhysicalDeviceFeatures deviceFeatures = new()
        {
            SamplerAnisotropy = true
        };

        DeviceCreateInfo createInfo = new()
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = (uint)uniqueQueueFamilies.Length,
            PQueueCreateInfos = queueCreateInfos,
            PEnabledFeatures = &deviceFeatures,
            EnabledExtensionCount = (uint)deviceExtensions.Length,
            PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(deviceExtensions)
        };

        if (enableValidationLayers)
        {
            createInfo.EnabledLayerCount = (uint)validationLayers.Length;
            createInfo.PpEnabledLayerNames = (byte**)SilkMarshal.StringArrayToPtr(validationLayers);
        }
        else
        {
            createInfo.EnabledLayerCount = 0;
        }

        if (vk.CreateDevice(physicalDevice, in createInfo, null, out device) != Result.Success)
            throw new Exception("failed to create logical device!");

        vk.GetDeviceQueue(device, indices.GraphicsFamily!.Value, 0, out graphicsQueue);
        vk.GetDeviceQueue(device, indices.PresentFamily!.Value, 0, out presentQueue);

        if (enableValidationLayers)
            SilkMarshal.Free((nint)createInfo.PpEnabledLayerNames);
    }


    void CreateSwapChain()
    {
        var swapChainSupport = QuerySwapChainSupport(physicalDevice);

        var surfaceFormat = ChooseSwapSurfaceFormat(swapChainSupport.Formats);
        var presentMode = ChoosePresentMode(swapChainSupport.PresentModes);
        var extent = ChooseSwapExtent(swapChainSupport.Capabilities);

        var imageCount = swapChainSupport.Capabilities.MinImageCount + 1;
        if (swapChainSupport.Capabilities.MaxImageCount > 0 && imageCount > swapChainSupport.Capabilities.MaxImageCount)
            imageCount = swapChainSupport.Capabilities.MaxImageCount;

        SwapchainCreateInfoKHR creatInfo = new()
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = surface,

            MinImageCount = imageCount,
            ImageFormat = surfaceFormat.Format,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit,
        };

        var indices = FindQueueFamilies(physicalDevice);
        var queueFamilyIndices = stackalloc[] { indices.GraphicsFamily!.Value, indices.PresentFamily!.Value };

        if (indices.GraphicsFamily != indices.PresentFamily)
        {
            creatInfo = creatInfo with
            {
                ImageSharingMode = SharingMode.Concurrent,
                QueueFamilyIndexCount = 2,
                PQueueFamilyIndices = queueFamilyIndices,
            };
        }
        else
        {
            creatInfo.ImageSharingMode = SharingMode.Exclusive;
        }

        creatInfo = creatInfo with
        {
            PreTransform = swapChainSupport.Capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = presentMode,
            Clipped = true,

            OldSwapchain = default
        };

        if (!vk.TryGetDeviceExtension(instance, device, out khrSwapChain))
            throw new NotSupportedException("VK_KHR_swapchain extension not found.");

        if (khrSwapChain!.CreateSwapchain(device, in creatInfo, null, out swapChain) != Result.Success)
            throw new Exception("failed to create swap chain!");

        khrSwapChain.GetSwapchainImages(device, swapChain, ref imageCount, null);
        swapChainImages = new Image[imageCount];
        fixed (Image* swapChainImagesPtr = swapChainImages)
            khrSwapChain.GetSwapchainImages(device, swapChain, ref imageCount, swapChainImagesPtr);

        swapChainImageFormat = surfaceFormat.Format;
        swapChainExtent = extent;
    }

    SurfaceFormatKHR ChooseSwapSurfaceFormat(IReadOnlyList<SurfaceFormatKHR> availableFormats)
    {
        foreach (var availableFormat in availableFormats)
            if (availableFormat.Format == Format.B8G8R8A8Srgb && availableFormat.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                return availableFormat;

        return availableFormats[0];
    }

    PresentModeKHR ChoosePresentMode(IReadOnlyList<PresentModeKHR> availablePresentModes)
    {
        foreach (var availablePresentMode in availablePresentModes)
            if (availablePresentMode == PresentModeKHR.MailboxKhr)
                return availablePresentMode;

        return PresentModeKHR.FifoKhr;
    }

    Extent2D ChooseSwapExtent(SurfaceCapabilitiesKHR capabilities)
    {
        if (capabilities.CurrentExtent.Width != uint.MaxValue)
        {
            return capabilities.CurrentExtent;
        }
        else
        {
            var framebufferSize = window.FramebufferSize;

            Extent2D actualExtent = new()
            {
                Width = (uint)framebufferSize.X,
                Height = (uint)framebufferSize.Y
            };

            actualExtent.Width = Math.Clamp(actualExtent.Width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width);
            actualExtent.Height = Math.Clamp(actualExtent.Height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height);

            return actualExtent;
        }
    }

    SwapChainSupportDetails QuerySwapChainSupport(PhysicalDevice physicalDevice)
    {
        var details = new SwapChainSupportDetails();

        khrSurface.GetPhysicalDeviceSurfaceCapabilities(physicalDevice, surface, out details.Capabilities);

        uint formatCount = 0;
        khrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, surface, ref formatCount, null);

        if (formatCount > 0)
        {
            details.Formats = new SurfaceFormatKHR[formatCount];
            fixed (SurfaceFormatKHR* formatsPtr = details.Formats)
                khrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, surface, ref formatCount, formatsPtr);
        }
        else
        {
            details.Formats = [];
        }

        uint presentModeCount = 0;
        khrSurface.GetPhysicalDeviceSurfacePresentModes(physicalDevice, surface, ref presentModeCount, null);

        if (presentModeCount > 0)
        {
            details.PresentModes = new PresentModeKHR[presentModeCount];
            fixed (PresentModeKHR* formatsPtr = details.PresentModes)
                khrSurface.GetPhysicalDeviceSurfacePresentModes(physicalDevice, surface, ref presentModeCount, formatsPtr);
        }
        else
        {
            details.PresentModes = Array.Empty<PresentModeKHR>();
        }

        return details;
    }

    void CreateImageViews()
    {
        swapChainImageViews = new ImageView[swapChainImages.Length];
        for (int i = 0; i < swapChainImages.Length; i++)
            swapChainImageViews[i] = CreateImageView(swapChainImages[i], swapChainImageFormat);
    }

    void CreateRenderPass()
    {
        AttachmentDescription colorAttachment = new()
        {
            Format = swapChainImageFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr
        };

        AttachmentReference attachmentReference = new()
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal
        };

        SubpassDescription subpass = new()
        {
            ColorAttachmentCount = 1,
            PColorAttachments = &attachmentReference
        };

        SubpassDependency subpassDependency = new()
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit,
        };

        RenderPassCreateInfo renderPassCreateInfo = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &subpassDependency
        };


        if (vk.CreateRenderPass(device, in renderPassCreateInfo, null, out renderPass) != Result.Success)
            throw new Exception("failed to create render pass!");
    }

    void CreateDescriptorSetLayout()
    {
        DescriptorSetLayoutBinding binding = new()
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.VertexBit
        };

        DescriptorSetLayoutBinding binding2 = new()
        {
            Binding = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImmutableSamplers = null,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        var bindings = stackalloc[] { binding, binding2 };

        DescriptorSetLayoutCreateInfo layoutInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 2,
            PBindings = bindings
        };

        if (vk.CreateDescriptorSetLayout(device, &layoutInfo, null, out descriptorSetLayout) != Result.Success)
            throw new Exception("failed to create descriptor set layout!");

    }

    void CreateGraphicsPipeline()
    {
        var vertShaderCode = File.ReadAllBytes("../../../../26_TextureMapping/vert.spv");
        var fragShaderCode = File.ReadAllBytes("../../../../26_TextureMapping/frag.spv");

        var vertShaderModule = CreateShaderModule(vertShaderCode);
        var fragShaderModule = CreateShaderModule(fragShaderCode);

        PipelineShaderStageCreateInfo vertShaderStageInfo = new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.VertexBit,
            Module = vertShaderModule,
            PName = (byte*)SilkMarshal.StringToPtr("main")
        };

        PipelineShaderStageCreateInfo fragShaderStageInfo = new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.FragmentBit,
            Module = fragShaderModule,
            PName = (byte*)SilkMarshal.StringToPtr("main")
        };

        var shaderStages = stackalloc[]
        {
            vertShaderStageInfo,
            fragShaderStageInfo
        };


        PipelineInputAssemblyStateCreateInfo inputAssembly = new()
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = PrimitiveTopology.TriangleList,
            PrimitiveRestartEnable = false
        };

        var dynamicState = stackalloc[]
        {
           DynamicState.Viewport, DynamicState.Scissor
        };

        PipelineDynamicStateCreateInfo dynamicStateInfo = new()
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = 2,
            PDynamicStates = dynamicState,
        };

        PipelineViewportStateCreateInfo viewportState = new()
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1,
        };

        PipelineRasterizationStateCreateInfo rasterizationState = new()
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            DepthClampEnable = false,
            RasterizerDiscardEnable = false,
            PolygonMode = PolygonMode.Fill,
            LineWidth = 1,
            CullMode = CullModeFlags.BackBit,
            FrontFace = FrontFace.CounterClockwise,
            DepthBiasEnable = false
        };

        PipelineMultisampleStateCreateInfo multisampleState = new()
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            SampleShadingEnable = false,
            RasterizationSamples = SampleCountFlags.Count1Bit,
        };

        PipelineColorBlendAttachmentState colorBlendAttachmentState = new()
        {
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
            BlendEnable = false
        };

        PipelineColorBlendStateCreateInfo colorBlendState = new()
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            LogicOpEnable = false,
            LogicOp = LogicOp.Copy,
            AttachmentCount = 1,
            PAttachments = &colorBlendAttachmentState
        };

        colorBlendState.BlendConstants[0] = 0;
        colorBlendState.BlendConstants[1] = 0;
        colorBlendState.BlendConstants[2] = 0;
        colorBlendState.BlendConstants[3] = 0;

        var setLayout = descriptorSetLayout;
        PipelineLayoutCreateInfo pipelineLayoutInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout
        };

        if (vk.CreatePipelineLayout(device, in pipelineLayoutInfo, null, out pipelineLayout) != Result.Success)
            throw new Exception("failed to create pipeline layout!");


        var bindingDescription = Vertex.GetBindingDescription();
        var attributeDescription = Vertex.GetAttributeDescriptions();

        fixed (VertexInputAttributeDescription* attributeDescriptionPtr = attributeDescription)
        {
            PipelineVertexInputStateCreateInfo vertexInputInfo = new()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                VertexAttributeDescriptionCount = (uint)attributeDescription.Length,
                PVertexBindingDescriptions = &bindingDescription,
                PVertexAttributeDescriptions = attributeDescriptionPtr
            };

            GraphicsPipelineCreateInfo graphicsPipelineInfo = new()
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = shaderStages,
                PVertexInputState = &vertexInputInfo,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterizationState,
                PMultisampleState = &multisampleState,
                PDepthStencilState = null,
                PColorBlendState = &colorBlendState,
                PDynamicState = &dynamicStateInfo,
                Layout = pipelineLayout,
                RenderPass = renderPass,
                Subpass = 0,
            };

            if (vk.CreateGraphicsPipelines(device, default, 1, in graphicsPipelineInfo, null, out graphicPipeline) != Result.Success)
                throw new Exception("failed to create graphics pipeline!");
        }

        vk.DestroyShaderModule(device, fragShaderModule, null);
        vk.DestroyShaderModule(device, vertShaderModule, null);

        SilkMarshal.Free((nint)vertShaderStageInfo.PName);
        SilkMarshal.Free((nint)fragShaderStageInfo.PName);
    }

    ShaderModule CreateShaderModule(byte[] code)
    {
        ShaderModuleCreateInfo createInfo = new()
        {
            SType = StructureType.ShaderModuleCreateInfo,
            CodeSize = (nuint)code.Length,
        };

        ShaderModule shaderModule;

        fixed (byte* codePtr = code)
        {
            createInfo.PCode = (uint*)codePtr;

            if (vk.CreateShaderModule(device, in createInfo, null, out shaderModule) != Result.Success)
                throw new Exception();
        }

        return shaderModule;
    }

    void CreateFramebuffers()
    {
        swapChainFramebuffers = new Framebuffer[swapChainImageViews.Length];
        for (int i = 0; i < swapChainImageViews.Length; i++)
        {
            var attachments = stackalloc[] { swapChainImageViews[i] };
            FramebufferCreateInfo framebufferInfo = new()
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = renderPass,
                AttachmentCount = 1,
                PAttachments = attachments,
                Width = swapChainExtent.Width,
                Height = swapChainExtent.Height,
                Layers = 1
            };

            if (vk.CreateFramebuffer(device, in framebufferInfo, null, out swapChainFramebuffers[i]) != Result.Success)
                throw new Exception("failed to create framebuffer!");
        }
    }

    void CreateCommandPool()
    {
        var indices = FindQueueFamilies(physicalDevice);

        CommandPoolCreateInfo commandPoolInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = indices.GraphicsFamily.Value
        };

        if (vk.CreateCommandPool(device, in commandPoolInfo, null, out commandPool) != Result.Success)
            throw new Exception("failed to create command pool!");
    }

    void CreateTextureImage()
    {
        using (var stream = File.OpenRead("../../../../24_TextureImage/texture.jpg"))
        {
            ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            var size = (uint)image.Data.Length;

            Buffer stagingBuffer = default;
            DeviceMemory stagingBufferMemory = default;

            var usage = BufferUsageFlags.TransferSrcBit;
            var properties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
            CreateBuffer(size, usage, properties, ref stagingBuffer, ref stagingBufferMemory);

            void* data;
            vk.MapMemory(device, stagingBufferMemory, 0, size, 0, &data);
            fixed (byte* imgDataPtr = image.Data)
                System.Buffer.MemoryCopy(imgDataPtr, data, size, size);
            vk.UnmapMemory(device, stagingBufferMemory);

            var width = (uint)image.Width;
            var height = (uint)image.Height;

            CreateImage(
                width,
                height,
                Format.R8G8B8A8Srgb,
                ImageTiling.Optimal,
                ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
                MemoryPropertyFlags.DeviceLocalBit,
                ref textureIamge,
                ref textureImageMemory);

            TransitionImageLayout(textureIamge, Format.R8G8B8A8Srgb, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
            CopyBufferToImage(stagingBuffer, textureIamge, width, height);
            TransitionImageLayout(textureIamge, Format.R8G8B8A8Srgb, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);
            vk.DestroyBuffer(device, stagingBuffer, null);
            vk.FreeMemory(device, stagingBufferMemory, null);
        }
    }

    void CreateImage(uint width, uint height, Format format, ImageTiling tiling, ImageUsageFlags usage, MemoryPropertyFlags properties, ref Image image, ref DeviceMemory imageMemory)
    {
        Extent3D extent = new()
        {
            Width = width,
            Height = height,
            Depth = 1
        };

        ImageCreateInfo imageCreateInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = extent,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = format,
            Tiling = tiling,
            InitialLayout = ImageLayout.Undefined,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            Samples = SampleCountFlags.Count1Bit,
        };

        if (vk.CreateImage(device, in imageCreateInfo, null, out image) != Result.Success)
            throw new Exception("failed to create image!");

        MemoryRequirements memRequirements = new();
        vk.GetImageMemoryRequirements(device, image, out memRequirements);

        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = (uint)memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, properties)
        };

        if (vk.AllocateMemory(device, in allocInfo, null, out imageMemory) != Result.Success)
            throw new Exception("failed to allocate image memory!");

        vk.BindImageMemory(device, image, imageMemory, 0);
    }

    void TransitionImageLayout(Image image, Format format, ImageLayout oldLayout, ImageLayout newLayout)
    {
        var commandBuffer = BeginSingleTimeCommands();

        ImageSubresourceRange range = new()
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = 1,
            BaseArrayLayer = 0,
            LayerCount = 1
        };

        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = range,
            SrcAccessMask = 0,
            DstAccessMask = 0,
        };

        PipelineStageFlags sourceStage = default;
        PipelineStageFlags destinationStage = default;

        if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;

            sourceStage = PipelineStageFlags.TopOfPipeBit;
            destinationStage = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;

            sourceStage = PipelineStageFlags.TransferBit;
            destinationStage = PipelineStageFlags.FragmentShaderBit;
        }
        else
        {
            throw new NotImplementedException();
        }


        vk.CmdPipelineBarrier(
                commandBuffer,
                sourceStage, destinationStage,
                0,
                0, null,
                0, null,
                1, &barrier);

        EndSingleTimeCommands(commandBuffer);
    }

    void CopyBufferToImage(Buffer buffer, Image image, uint width, uint height)
    {
        var commandBuffer = BeginSingleTimeCommands();

        ImageSubresourceLayers sub = new()
        {
            AspectMask = ImageAspectFlags.ColorBit,
            MipLevel = 0,
            BaseArrayLayer = 0,
            LayerCount = 1
        };

        Extent3D extent = new()
        {
            Width = width,
            Height = height,
            Depth = 1
        };

        BufferImageCopy region = new()
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = sub,
            ImageOffset = new Offset3D(),
            ImageExtent = extent
        };

        vk.CmdCopyBufferToImage(commandBuffer, buffer, image, ImageLayout.TransferDstOptimal, 1, in region);

        EndSingleTimeCommands(commandBuffer);
    }

    void CreateTextureImageView()
    {
        textureImageView = CreateImageView(textureIamge, Format.R8G8B8A8Srgb);
    }

    ImageView CreateImageView(Image image, Format format)
    {
        ImageViewCreateInfo createInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            Components =
                {
                    R = ComponentSwizzle.Identity,
                    G = ComponentSwizzle.Identity,
                    B = ComponentSwizzle.Identity,
                    A = ComponentSwizzle.Identity,
                },
            SubresourceRange =
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                }
        };

        if (vk.CreateImageView(device, in createInfo, null, out var imageView) != Result.Success)
            throw new Exception("failed to create image views!");
        return imageView;
    }

    void CreateTextureSampler()
    {
        vk.GetPhysicalDeviceProperties(physicalDevice, out var properties);

        SamplerCreateInfo samplerCreateInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.Repeat,
            AnisotropyEnable = true,
            MaxAnisotropy = properties.Limits.MaxSamplerAnisotropy,
            BorderColor = BorderColor.IntOpaqueBlack,
            UnnormalizedCoordinates = false,
            CompareEnable = false,
            CompareOp = CompareOp.Always,
            MipmapMode = SamplerMipmapMode.Linear,
            MipLodBias = 0,
            MinLod = 0,
            MaxLod = 0,
        };

        if (vk.CreateSampler(device, in samplerCreateInfo, null, out textureSampler) != Result.Success)
            throw new Exception("failed to create texture sampler!");
    }

    void CreateVertexBuffer()
    {
        var size = (uint)(sizeof(Vertex) * vertices.Length);

        Buffer stagingBuffer = default;
        DeviceMemory stagingBufferMemory = default;

        var properties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
        CreateBuffer(size, BufferUsageFlags.TransferSrcBit, properties, ref stagingBuffer, ref stagingBufferMemory);

        void* data;
        vk.MapMemory(device, stagingBufferMemory, 0, size, 0, &data);
        vertices.AsSpan().CopyTo(new Span<Vertex>(data, vertices.Length));
        vk.UnmapMemory(device, stagingBufferMemory);

        var usage = BufferUsageFlags.TransferDstBit | BufferUsageFlags.VertexBufferBit;
        CreateBuffer(size, usage, MemoryPropertyFlags.DeviceLocalBit, ref vertexBuffer, ref vertexBufferMemory);

        CopyBuffer(stagingBuffer, vertexBuffer, size);
        vk.DestroyBuffer(device, stagingBuffer, null);
        vk.FreeMemory(device, stagingBufferMemory, null);
    }

    void CreateIndexBuffer()
    {
        var size = (uint)(sizeof(ushort) * indices.Length);
        Buffer stagingBuffer = default;
        DeviceMemory stagingBufferMemory = default;

        var properties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
        CreateBuffer(size, BufferUsageFlags.TransferSrcBit, properties, ref stagingBuffer, ref stagingBufferMemory);

        void* data;
        vk.MapMemory(device, stagingBufferMemory, 0, size, 0, &data);
        indices.AsSpan().CopyTo(new Span<ushort>(data, indices.Length));
        vk.UnmapMemory(device, stagingBufferMemory);

        var usage = BufferUsageFlags.TransferDstBit | BufferUsageFlags.IndexBufferBit;
        CreateBuffer(size, usage, MemoryPropertyFlags.DeviceLocalBit, ref indexBuffer, ref indexBufferMemory);

        CopyBuffer(stagingBuffer, indexBuffer, size);
        vk.DestroyBuffer(device, stagingBuffer, null);
        vk.FreeMemory(device, stagingBufferMemory, null);
    }

    void CreateUniformBuffer()
    {
        var size = (uint)sizeof(UniformBufferObject);
        uniformBuffers = new Buffer[swapChainImages.Length];
        uniformBuffersMemory = new DeviceMemory[swapChainImages.Length];
        uniformBuffersMapped = new void*[swapChainImages.Length];

        var properties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
        for (int i = 0; i < swapChainImages.Length; i++)
        {
            CreateBuffer(size, BufferUsageFlags.UniformBufferBit, properties, ref uniformBuffers[i], ref uniformBuffersMemory[i]);
            void* data;
            vk.MapMemory(device, uniformBuffersMemory[i], 0, size, 0, &data);
            uniformBuffersMapped[i] = data;
        }
    }

    void CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties, ref Buffer buffer, ref DeviceMemory deviceMemory)
    {
        BufferCreateInfo bufferInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };

        if (vk.CreateBuffer(device, in bufferInfo, default, out buffer) != Result.Success)
            throw new Exception("failed to create vertex buffer!");

        MemoryRequirements memRequirements = new();
        vk.GetBufferMemoryRequirements(device, buffer, out memRequirements);

        MemoryAllocateInfo memoryAllocateInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = (uint)memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, properties)
        };

        if (vk.AllocateMemory(device, in memoryAllocateInfo, default, out deviceMemory) != Result.Success)
            throw new Exception("failed to allocate vertex buffer memory!");

        vk.BindBufferMemory(device, buffer, deviceMemory, 0);
    }

    void CopyBuffer(Buffer srcBuffer, Buffer dstBuffer, ulong size)
    {
        var commandBuffer = BeginSingleTimeCommands();

        BufferCopy bufferCopy = new()
        {
            SrcOffset = 0,
            DstOffset = 0,
            Size = size
        };

        vk.CmdCopyBuffer(commandBuffer, srcBuffer, dstBuffer, 1, &bufferCopy);
        EndSingleTimeCommands(commandBuffer);
    }

    CommandBuffer BeginSingleTimeCommands()
    {
        CommandBufferAllocateInfo allocateInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            Level = CommandBufferLevel.Primary,
            CommandPool = commandPool,
            CommandBufferCount = 1
        };
        vk.AllocateCommandBuffers(device, in allocateInfo, out var commandBuffer);

        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        vk.BeginCommandBuffer(commandBuffer, in beginInfo);

        return commandBuffer;
    }

    void EndSingleTimeCommands(CommandBuffer commandBuffer)
    {
        vk.EndCommandBuffer(commandBuffer);

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };

        vk.QueueSubmit(graphicsQueue, 1, in submitInfo, default);
        vk.QueueWaitIdle(graphicsQueue);

        vk.FreeCommandBuffers(device, commandPool, 1, in commandBuffer);
    }

    uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        vk.GetPhysicalDeviceMemoryProperties(physicalDevice, out var memProperties);

        for (int i = 0; i < memProperties.MemoryTypeCount; i++)
            if ((typeFilter & (1 << i)) != 0 && (memProperties.MemoryTypes[i].PropertyFlags & properties) == properties)
                return (uint)i;

        throw new Exception("failed to find suitable memory type!");
    }

    void CreateDescriptorPool()
    {
        DescriptorPoolSize poolSize = new()
        {
            Type = DescriptorType.UniformBuffer,
            DescriptorCount = (uint)swapChainImages.Length
        };

        DescriptorPoolSize poolSize2 = new()
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = (uint)swapChainImages.Length
        };

        var poolSizes = stackalloc[] { poolSize, poolSize2 };

        DescriptorPoolCreateInfo poolCreateInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 2,
            PPoolSizes = poolSizes,
            MaxSets = (uint)swapChainImages.Length
        };

        fixed (DescriptorPool* descriptorPoolPtr = &descriptorPool)
            if (vk.CreateDescriptorPool(device, in poolCreateInfo, null, descriptorPoolPtr) != Result.Success)
                throw new Exception("failed to create descriptor pool!");
    }

    void CreateDescriptorSets()
    {
        var layouts = new DescriptorSetLayout[swapChainImages.Length];
        Array.Fill(layouts, descriptorSetLayout);

        fixed (DescriptorSetLayout* layoutsPtr = layouts)
        {
            DescriptorSetAllocateInfo allocInfo = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = descriptorPool,
                DescriptorSetCount = (uint)swapChainImages.Length,
                PSetLayouts = layoutsPtr
            };

            descriptorSets = new DescriptorSet[swapChainImages.Length];
            fixed (DescriptorSet* descriptorSetsPtr = descriptorSets)
                if (vk.AllocateDescriptorSets(device, in allocInfo, descriptorSetsPtr) != Result.Success)
                    throw new Exception("failed to allocate descriptor sets!");
        }

        for (int i = 0; i < swapChainImages.Length; i++)
        {
            DescriptorBufferInfo bufferInfo = new()
            {
                Buffer = uniformBuffers[i],
                Offset = 0,
                Range = (uint)sizeof(UniformBufferObject)
            };


            WriteDescriptorSet descriptorWrite = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSets[i],
                DstBinding = 0,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                PBufferInfo = &bufferInfo,
            };

            DescriptorImageInfo imageInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = textureImageView,
                Sampler = textureSampler
            };

            WriteDescriptorSet descriptorWrite2 = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSets[i],
                DstBinding = 1,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &imageInfo,
            };

            var writeSets = stackalloc[] { descriptorWrite, descriptorWrite2 };

            vk.UpdateDescriptorSets(device, 2, writeSets, 0, null);
        }
    }

    void CreateCommandBuffers()
    {
        commandBuffers = new CommandBuffer[swapChainImages.Length];
        CommandBufferAllocateInfo commandBufferAllocateInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = (uint)swapChainImages.Length
        };

        fixed (CommandBuffer* commandBuffersPtr = commandBuffers)
            if (vk.AllocateCommandBuffers(device, in commandBufferAllocateInfo, commandBuffersPtr) != Result.Success)
                throw new Exception("failed to allocate command buffers!");
    }

    void RecordCommandBuffer(CommandBuffer commandBuffer, uint imageIndex)
    {
        CommandBufferBeginInfo commandBufferBeginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
        };

        if (vk.BeginCommandBuffer(commandBuffer, in commandBufferBeginInfo) != Result.Success)
            throw new Exception("failed to begin recording command buffer!");


        Rect2D renderArea = new()
        {
            Offset = new Offset2D(),
            Extent = swapChainExtent
        };

        ClearValue clearValue = new()
        {
            Color = new ClearColorValue(0, 0, 0, 1),
        };

        RenderPassBeginInfo renderPassBeginInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = renderPass,
            Framebuffer = swapChainFramebuffers[imageIndex],
            RenderArea = renderArea,
            ClearValueCount = 1,
            PClearValues = &clearValue
        };

        vk.CmdBeginRenderPass(commandBuffer, in renderPassBeginInfo, SubpassContents.Inline);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, graphicPipeline);

        Viewport viewport = new()
        {
            X = 0,
            Y = 0,
            Width = swapChainExtent.Width,
            Height = swapChainExtent.Height,
            MinDepth = 0,
            MaxDepth = 1,
        };
        vk.CmdSetViewport(commandBuffer, 0, 1, in viewport);

        Rect2D scissor = new()
        {
            Offset = new Offset2D(0, 0),
            Extent = swapChainExtent
        };
        vk.CmdSetScissor(commandBuffer, 0, 1, in scissor);

        var vertexBuffers = new Buffer[] { vertexBuffer };
        var offsets = new ulong[] { 0 };
        vk.CmdBindVertexBuffers(commandBuffer, 0, 1, vertexBuffers, offsets);

        vk.CmdBindIndexBuffer(commandBuffer, indexBuffer, 0, IndexType.Uint16);
        vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, pipelineLayout, 0, 1, in descriptorSets[currentFrame], 0, null);
        vk.CmdDrawIndexed(commandBuffer, (uint)indices.Length, 1, 0, 0, 0);
        vk.CmdEndRenderPass(commandBuffer);

        if (vk.EndCommandBuffer(commandBuffer) != Result.Success)
            throw new Exception("failed to record command buffer!");
    }

    void CreateSyncObjects()
    {
        imageAvailableSemaphores = new Semaphore[swapChainImages.Length];
        renderFinishedSemaphores = new Semaphore[swapChainImages.Length];
        inFlightFences = new Fence[swapChainImages.Length];

        SemaphoreCreateInfo semaphoreInfo = new()
        {
            SType = StructureType.SemaphoreCreateInfo
        };

        FenceCreateInfo fenceInfo = new()
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit
        };

        for (int i = 0; i < swapChainImages.Length; i++)
        {
            if (vk.CreateSemaphore(device, in semaphoreInfo, null, out imageAvailableSemaphores[i]) != Result.Success)
                throw new Exception("failed to create imageAvailableSemaphores!");
            if (vk.CreateSemaphore(device, in semaphoreInfo, null, out renderFinishedSemaphores[i]) != Result.Success)
                throw new Exception("failed to create renderFinishedSemaphores!");
            if (vk.CreateFence(device, in fenceInfo, null, out inFlightFences[i]) != Result.Success)
                throw new Exception("failed to create inFlightFences!");
        }
    }
}

struct QueueFamilyIndices
{
    public uint? GraphicsFamily { get; set; }
    public uint? PresentFamily { get; set; }

    public bool IsComplete()
    {
        return GraphicsFamily.HasValue && PresentFamily.HasValue;
    }
}

struct SwapChainSupportDetails
{
    public SurfaceCapabilitiesKHR Capabilities;
    public SurfaceFormatKHR[] Formats;
    public PresentModeKHR[] PresentModes;
}

struct Vertex
{
    public Vector2D<float> pos;
    public Vector3D<float> color;
    public Vector2D<float> texCoord;

    public static VertexInputBindingDescription GetBindingDescription()
    {
        VertexInputBindingDescription description = new()
        {
            Binding = 0,
            Stride = (uint)Unsafe.SizeOf<Vertex>(),
            InputRate = VertexInputRate.Vertex
        };
        return description;
    }

    public static VertexInputAttributeDescription[] GetAttributeDescriptions()
    {
        VertexInputAttributeDescription description = new()
        {
            Binding = 0,
            Location = 0,
            Format = Format.R32G32Sfloat,
            Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(pos)),
        };

        VertexInputAttributeDescription description2 = new()
        {
            Binding = 0,
            Location = 1,
            Format = Format.R32G32B32Sfloat,
            Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(color)),
        };

        VertexInputAttributeDescription description3 = new()
        {
            Binding = 0,
            Location = 2,
            Format = Format.R32G32Sfloat,
            Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(texCoord)),
        };

        return [description, description2, description3];
    }
}

struct UniformBufferObject
{
    public Matrix4X4<float> model;
    public Matrix4X4<float> view;
    public Matrix4X4<float> proj;
}

internal class Program
{
    static void Main(string[] args)
    {
        var app = new HelloTriangleApplication();
        app.Run();
    }
}