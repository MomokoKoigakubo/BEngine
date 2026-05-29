// IdleL.cpp : Vulkan engine entry point.

#define VOLK_IMPLEMENTATION
#include "IdleL.h"
#include "VulkanContext/Context.h"
#include <Volk/volk.h>
#include <SDL3/SDL.h>
#include <SDL3/SDL_vulkan.h>
#include <iostream>

// Aborts with file:line and the failing call if a Vulkan call doesn't return VK_SUCCESS.
#define chk(x)                                                     \
    do {                                                           \
        VkResult err = x;                                          \
        if (err != VK_SUCCESS) {                                   \
            std::cerr << "Vulkan error: " << err                   \
                      << " at " << __FILE__ << ":" << __LINE__     \
                      << "  (" << #x << ")" << std::endl;          \
            std::abort();                                          \
        }                                                          \
    } while (0)

int main(int argc, char** argv)
{
    try {
        if (!SDL_Init(SDL_INIT_VIDEO))
        {
            std::cerr << "SDL_Init Error: " << SDL_GetError() << std::endl;
            return 1;
        }

        Context context;

        SDL_Window* window = SDL_CreateWindow("BEngine", 1280, 720,
            SDL_WINDOW_VULKAN | SDL_WINDOW_RESIZABLE);
        if (!window)
        {
            std::cerr << "SDL_CreateWindow Error: " << SDL_GetError() << std::endl;
            return 1;
        }

        bool running = true;
        while (running)
        {
            SDL_Event event;
            while (SDL_PollEvent(&event))
            {
                if (event.type == SDL_EVENT_QUIT)
                {
                    running = false;
                }
            }
        }

        SDL_DestroyWindow(window);
        SDL_Quit();
        return 0;
    } 
    catch (const std::exception& e)
    {
        std::cerr << "Fatal: " << e.what() << std::endl;
    }
}
