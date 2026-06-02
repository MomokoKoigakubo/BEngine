#pragma once
#include "Renderer/Renderer.h"
#include "Resources/ResourceManager.h"
#include "Scene/Scene.h"
#include <SDL3/SDL.h>

class Application
{
public:
	Application(SDL_Window* window);
	void run();

private:
	void processEvents();
	void update(float dt);
	void render();

	SDL_Window* window;
	Renderer renderer;
	ResourceManager resources;
	Scene scene;

	//playback texture | timing state
	TextureHandle texHandle;
	bool running = true;

	//for modal/window resize watcher
	static bool SDLCALL resizeWatcher(void* userdata, SDL_Event* event);
};