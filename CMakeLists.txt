cmake_minimum_required(VERSION 3.15)
project(DynamicNeuralEngine VERSION 1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Add all library files (headers and sources) for the shared core engine
add_library(DynamicNeuralEngine SHARED
        library.h
        include/Neuron.hpp
        include/Synapse.hpp
        include/EmotionalState.hpp
        include/EngineState.hpp
        src/EngineState.cpp
        src/library.cpp
)

# Ensure no "lib" prefix is added to the DLL name on Windows/MinGW
set_target_properties(DynamicNeuralEngine PROPERTIES PREFIX "")

# Force CMake to output the compiled DLL directly into the C# Trainer project folder
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/DynamicNeuralEngine.Trainer/bin/Debug/net8.0")
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/DynamicNeuralEngine.Trainer/bin/Debug/net8.0")



# Tell the compiler where to look for header files
target_include_directories(DynamicNeuralEngine PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}
        ${CMAKE_CURRENT_SOURCE_DIR}/include
)