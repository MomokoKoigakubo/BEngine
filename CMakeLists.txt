# CMakeList.txt : CMake project for IdleL, include source and define
# project specific logic here.
#
cmake_minimum_required (VERSION 3.8)

# Enable Hot Reload for MSVC compilers if supported.
if (POLICY CMP0141)
  cmake_policy(SET CMP0141 NEW)
  set(CMAKE_MSVC_DEBUG_INFORMATION_FORMAT "$<IF:$<AND:$<C_COMPILER_ID:MSVC>,$<CXX_COMPILER_ID:MSVC>>,$<$<CONFIG:Debug,RelWithDebInfo>:EditAndContinue>,$<$<CONFIG:Debug,RelWithDebInfo>:ProgramDatabase>>")
endif()

project ("IdleL")

# Add source to this project's executable.
add_executable (IdleL "main.cpp"
# Renderer (Vulkan)
"Renderer/Renderer.h" "Renderer/Renderer.cpp" "Renderer/VkCheck.h"
"Renderer/Instance.h" "Renderer/Instance.cpp" "Renderer/Surface.h" "Renderer/Surface.cpp"
"Renderer/Device.h" "Renderer/Device.cpp" "Renderer/Swapchain.h" "Renderer/Swapchain.cpp"
"Renderer/Buffer.h" "Renderer/Buffer.cpp" "Renderer/Vertex.h"
# Resources
"Resources/Mesh.h" "Resources/Mesh.cpp" "Resources/Texture.h" "Resources/Texture.cpp"
# Scene
"Scene/Camera.h" "Scene/OrbitCamera.h" "Scene/OrbitCamera.cpp" "Scene/Scene.h"
# Assets
"Assets/bbmodel.h" "Assets/bbloader.h" "Assets/bbloader.cpp"
"Assets/cubeBuilder.h" "Assets/cubeBuilder.cpp"
# util (foundation) / third_party
"util/math/math.h"
"util/json/jparser.h" "util/json/jparser.cpp"
"util/fileIO.h" "util/fileIO.cpp" "util/vmaImpl.cpp" "util/stbImpl.cpp"
"util/third_party/stb_image.h"
"Resources/Handles.h" "Resources/ResourceManager.h" "Resources/ResourceManager.cpp" "App/Application.h" "App/Application.cpp")
target_include_directories(IdleL PRIVATE ${CMAKE_SOURCE_DIR})
set(SLANGC "$ENV{VULKAN_SDK}/bin/slangc.exe")

add_custom_command(
    OUTPUT "${CMAKE_SOURCE_DIR}/shaders/triangle.spv"
    COMMAND "${SLANGC}" "${CMAKE_SOURCE_DIR}/shaders/triangle.slang"
    -target spirv -profile spirv_1_5
    -o "${CMAKE_SOURCE_DIR}/shaders/triangle.spv"
    DEPENDS "${CMAKE_SOURCE_DIR}/shaders/triangle.slang"
    COMMENT "Compiling triangle.slang -> triangle.spv"
)

add_custom_target(shaders DEPENDS "${CMAKE_SOURCE_DIR}/shaders/triangle.spv")
add_dependencies(IdleL shaders)

find_package(Vulkan REQUIRED)

if (CMAKE_VERSION VERSION_GREATER 3.12)
  set_property(TARGET IdleL PROPERTY CXX_STANDARD 20)
endif()

# TODO: Add tests and install targets if needed.

include_directories(SYSTEM $ENV{VULKAN_SDK}/include)
find_library(SDL_LIBRARY NAMES SDL3 HINTS "$ENV{VULKAN_SDK}/lib" REQUIRED)
find_library(SLANG_LIBRARY NAMES slang HINTS "$ENV{VULKAN_SDK}/lib" REQUIRED)
  target_compile_features(IdleL PRIVATE cxx_std_20)

  # Volk loads Vulkan at runtime, so we tell the headers not to declare prototypes
  target_compile_definitions(IdleL PRIVATE
      VK_NO_PROTOTYPES
      VK_USE_PLATFORM_WIN32_KHR
      NOMINMAX
      _USE_MATH_DEFINES
      _CRT_SECURE_NO_WARNINGS
      SHADER_DIR="${CMAKE_SOURCE_DIR}/shaders"
      MODEL_DIR="${CMAKE_SOURCE_DIR}/ModelsForTest")

  target_link_libraries(IdleL PRIVATE ${SDL_LIBRARY} ${SLANG_LIBRARY})

  if(MSVC)
      target_compile_options(IdleL PRIVATE /EHsc)
  endif()