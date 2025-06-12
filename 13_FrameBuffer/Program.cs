using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

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
    PipelineLayout pipelineLayout;
    Pipeline graphicPipeline;

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
        CreateSurface();
        PickPhysicalDevice();
        CreateLogicalDevice();
        CreateSwapChain();
        CreateImageViews();
        CreateRenderPass();
        CreateGraphicsPipeline();
        CreateFramebuffers();
    }

    void MainLoop()
    {
        window.Run();
    }

    void Cleanup()
    {
        foreach (var frameBuffer in swapChainFramebuffers)
            vk.DestroyFramebuffer(device, frameBuffer, null);

        vk.DestroyPipeline(device, graphicPipeline, null);
        vk.DestroyPipelineLayout(device, pipelineLayout, null);
        vk.DestroyRenderPass(device, renderPass, null);

        foreach (var imageView in swapChainImageViews!)
            vk.DestroyImageView(device, imageView, null);

        khrSwapChain.DestroySwapchain(device, swapChain, null);
        vk.DestroyDevice(device, null);

        if (enableValidationLayers)
            debugUtils.DestroyDebugUtilsMessenger(instance, debugMessenger, null);

        khrSurface.DestroySurface(instance, surface, null);
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

        return indices.IsComplete() && extensionsSupported && swapChainAdequate;
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

        PhysicalDeviceFeatures deviceFeatures = new();

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
        {
            ImageViewCreateInfo createInfo = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = swapChainImages[i],
                ViewType = ImageViewType.Type2D,
                Format = swapChainImageFormat,
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

            if (vk.CreateImageView(device, in createInfo, null, out swapChainImageViews[i]) != Result.Success)
                throw new Exception("failed to create image views!");
        }
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

        RenderPassCreateInfo renderPassCreateInfo = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass
        };

        if (vk.CreateRenderPass(device, in renderPassCreateInfo, null, out renderPass) != Result.Success)
            throw new Exception("failed to create render pass!");
    }

    void CreateGraphicsPipeline()
    {
        var vertShaderCode = File.ReadAllBytes("../../../../09_ShaderModules/vert.spv");
        var fragShaderCode = File.ReadAllBytes("../../../../09_ShaderModules/frag.spv");

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

        PipelineVertexInputStateCreateInfo vertexInputInfo = new()
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 0,
            VertexAttributeDescriptionCount = 0
        };

        PipelineInputAssemblyStateCreateInfo inputAssembly = new()
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = PrimitiveTopology.TriangleList,
            PrimitiveRestartEnable = false
        };

        Viewport viewport = new()
        {
            X = 0,
            Y = 0,
            Width = swapChainExtent.Width,
            Height = swapChainExtent.Height,
            MinDepth = 0,
            MaxDepth = 1,
        };

        Rect2D scissor = new()
        {
            Offset = new Offset2D(0, 0),
            Extent = swapChainExtent
        };

        var dynamicState = stackalloc[]
        {
           DynamicState.Viewport, DynamicState.Scissor
        };

        PipelineDynamicStateCreateInfo dynamicStateInfo = new()
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = 2,
            PDynamicStates = dynamicState
        };

        PipelineViewportStateCreateInfo viewportState = new()
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1
        };

        PipelineRasterizationStateCreateInfo rasterizationState = new()
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            DepthClampEnable = false,
            RasterizerDiscardEnable = false,
            PolygonMode = PolygonMode.Fill,
            LineWidth = 1,
            CullMode = CullModeFlags.BackBit,
            FrontFace = FrontFace.Clockwise,
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

        PipelineLayoutCreateInfo pipelineLayoutInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
        };

        if (vk.CreatePipelineLayout(device, in pipelineLayoutInfo, null, out pipelineLayout) != Result.Success)
            throw new Exception("failed to create pipeline layout!");

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

internal class Program
{
    static void Main(string[] args)
    {
        var app = new HelloTriangleApplication();
        app.Run();
    }
}