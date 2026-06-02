#define VOLK_IMPLEMENTATION
#include "App/Application.h"
#include <SDL3/SDL.h>
#include <SDL3/SDL_vulkan.h>
#include <iostream>

int main(int argc, char** argv)
{
    try {
        if (!SDL_Init(SDL_INIT_VIDEO))
        {
            std::cerr << "SDL_Init Error: " << SDL_GetError() << std::endl;
            return 1;
        }

        SDL_Window* window = SDL_CreateWindow("BEngine", 1280, 720,
            SDL_WINDOW_VULKAN | SDL_WINDOW_RESIZABLE);
        if (!window)
        {
            std::cerr << "SDL_CreateWindow Error: " << SDL_GetError() << std::endl;
            return 1;
        }
        SDL_SetWindowMinimumSize(window, 650, 360);

        {
            Application app(window);   // owns renderer/resources/scene; ctor loads
            app.run();
        }   // app destructs here > renderer torn down BEFORE window destroyed

        SDL_DestroyWindow(window);
        SDL_Quit();
        return 0;
    }
    catch (const std::exception& e)
    {
        std::cerr << "Fatal: " << e.what() << std::endl;
    }
}