cmake_minimum_required(VERSION 3.15)
project(DynamicNeuralEngine VERSION 1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Add all modular library files for the shared core engine and modalities
add_library(DynamicNeuralEngine SHARED
        library.h
        include/core/Synapse.hpp
        include/core/Neuron.hpp
        include/core/EmotionalState.hpp
        include/core/NeuralGraph.hpp
        include/core/BrainPersistence.hpp
        include/core/BrainCodec.hpp
        include/modalities/IModalityAdapter.hpp
        include/modalities/TextLanguageModality.hpp
        include/modalities/DialogueMemory.hpp
        include/modalities/ArithmeticReasoner.hpp
        include/modalities/LanguageRequests.hpp
        include/modalities/ConceptMemory.hpp
        include/modalities/VisionVideoModality.hpp
        include/modalities/Spatial3DModality.hpp
        include/modalities/ComputerUseModality.hpp
        include/EngineState.hpp
        src/core/NeuralGraph.cpp
        src/core/BrainPersistence.cpp
        src/core/BrainCodec.cpp
        src/modalities/TextLanguageModality.cpp
        src/modalities/DialogueMemory.cpp
        src/modalities/ArithmeticReasoner.cpp
        src/modalities/LanguageRequests.cpp
        src/modalities/ConceptMemory.cpp
        src/EngineState.cpp
        src/library.cpp
)

# Ensure no "lib" prefix is added to the DLL name on Windows/MinGW
set_target_properties(DynamicNeuralEngine PROPERTIES PREFIX "")

# A single predictable native artifact; the C# build copies this DLL into its output.
# Target properties must be set explicitly after add_library has created the target.
set_target_properties(DynamicNeuralEngine PROPERTIES
        RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/native"
        RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_BINARY_DIR}/native"
        RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/native"
)

# Tell the compiler where to look for header files
target_include_directories(DynamicNeuralEngine PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}
        ${CMAKE_CURRENT_SOURCE_DIR}/include
)
