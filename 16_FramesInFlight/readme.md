
Instead of creating a hardcoded constant MAX_FRAMES_IN_FLIGHT
Set up arrays of semaphore and fence with length equel to swapchain image count

<https://github.com/Overv/VulkanTutorial/issues/276>

This fix the validation message

```
[validation layer] vkQueueSubmit(): pSubmits[0].pSignalSemaphores[0] (VkSemaphore 0x140000000014) is being signaled by VkQueue 0x27210aff860, but it may still be in use by VkSwapchainKHR 0x30000000003.
Here are the most recently acquired image indices: [0], 1.
(brackets mark the last use of VkSemaphore 0x140000000014 in a presentation operation)
Swapchain image 0 was presented but was not re-acquired, so VkSemaphore 0x140000000014 may still be in use and cannot be safely reused with image index 1.
Vulkan insight: One solution is to assign each image its own semaphore. Here are some common methods to ensure that a semaphore passed to vkQueuePresentKHR is not in use and can be safely reused:
        a) Use a separate semaphore per swapchain image. Index these semaphores using the index of the acquired image.
        b) Consider the VK_EXT_swapchain_maintenance1 extension. It allows using a VkFence with the presentation operation.
The Vulkan spec states: Each binary semaphore element of the pSignalSemaphores member of any element of pSubmits must be unsignaled when the semaphore signal operation it defines is executed on the device (https://vulkan.lunarg.com/doc/view/1.4.313.2/windows/antora/spec/latest/chapters/cmdbuffers.html#VUID-vkQueueSubmit-pSignalSemaphores-00067)
```
