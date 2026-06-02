#include "Application.h"
#include "Assets/bbloader.h"
#include "Assets/cubeBuilder.h"
#include "util/json/jparser.h"
#include <cmath>

Application::Application(SDL_Window* window)
	:window(window)
	, renderer(window)
	, resources(renderer)
{
	//load model
	JsonParser parser;
	std::string text;
	parser.ReadFile(MODEL_DIR "/momoko.bbmodel", text);
	JsonValue root = parser.parse(text);

	BBModelLoader loader;
	BBModelParts model = loader.load(root);
	std::vector<Vertex> verts;
	std::vector<uint32_t> indices;
	buildModel(model, verts, indices);

	MeshHandle meshHandle = resources.createMesh(verts, indices);
	texHandle = resources.loadTexture(MODEL_DIR "/momoko.png");
	scene.add(meshHandle);

	if (!model.textures.empty())
	{
		const TextureMeta& tm = model.textures[0];
		int imgW = resources.textureWidth(texHandle);
		int imgH = resources.textureHeight(texHandle);
		float frameCount = (float)((imgH * tm.uvWidth) / (imgW * tm.uvHeight));
		resources.registerFlipbook(texHandle, frameCount, 10.0f);
	}

	scene.camera.target = { 0.0f, 0.6f, -0.75f };
	scene.camera.distance = 8.0f;

	SDL_AddEventWatch(resizeWatcher, this);
}

void Application::run()
{
	Uint64 lastTime = SDL_GetTicks(); //window fps
	Uint64 lastFrame = SDL_GetTicks(); //perframe dt
	int frameCount = 0;

	while (running)
	{
		processEvents();
		
		Uint64 now = SDL_GetTicks();
		float dt = (now - lastFrame) / 1000.0f;
		lastFrame = now;

		update(dt);
		render();

		frameCount++;
		if (now - lastTime >= 1000)
		{
			float fps = frameCount * 1000.0f / (now - lastTime);
			char title[64];
			SDL_snprintf(title, sizeof(title), "BEngine - %.1f FPS", fps);
			SDL_SetWindowTitle(window, title);
			frameCount = 0;
			lastTime = now;
		}
	}
	SDL_RemoveEventWatch(resizeWatcher, this);
	renderer.waitIdle(); //gpu idle pre member tear down
}

void Application::processEvents()
{
	SDL_Event event;
	while (SDL_PollEvent(&event))
	{
		if (event.type == SDL_EVENT_QUIT)
			running = false;

		if (event.type == SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED)
			renderer.setFrameBufferResized();

		if (event.type == SDL_EVENT_MOUSE_MOTION &&
			(event.motion.state & SDL_BUTTON_LMASK))
		{
			const float sens = 0.005f;
			scene.camera.orbit(event.motion.xrel * sens, -event.motion.yrel * sens);
		}
	}
}

void Application::update(float dt)
{
	scene.update(dt);
}

void Application::render()
{
	renderer.drawFrame(scene, resources);
}

bool SDLCALL Application::resizeWatcher(void* userdata, SDL_Event* event)
{
	if (event->type == SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED)
	{
		Application* app = static_cast<Application*>(userdata);
		app->renderer.setFrameBufferResized();
		app->render();
	}
	return true;
}