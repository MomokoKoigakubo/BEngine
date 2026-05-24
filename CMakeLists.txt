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
add_executable (IdleL "IdleL.cpp" "IdleL.h" )

find_package(Vulkan REQUIRED)

if (CMAKE_VERSION VERSION_GREATER 3.12)
  set_property(TARGET IdleL PROPERTY CXX_STANDARD 20)
endif()

# TODO: Add tests and install targets if needed.

include_directories(SYSTEM $ENV{VULKAN_SDK}/include)
find_library(SDL_LIBRARY NAMES SDL3 HINTS "$ENV{VULKAN_SDK}/lib" REQUIRED)
  target_compile_features(IdleL PRIVATE cxx_std_20)

  # Volk loads Vulkan at runtime, so we tell the headers not to declare prototypes
  target_compile_definitions(IdleL PRIVATE
      VK_NO_PROTOTYPES
      VK_USE_PLATFORM_WIN32_KHR
      NOMINMAX
      _USE_MATH_DEFINES
      _CRT_SECURE_NO_WARNINGS)

  target_link_libraries(IdleL PRIVATE ${SDL_LIBRARY})

  if(MSVC)
      target_compile_options(IdleL PRIVATE /EHsc)
  endif()