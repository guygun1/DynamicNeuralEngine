#ifndef DYNAMICNEURALENGINE_LIBRARY_H
#define DYNAMICNEURALENGINE_LIBRARY_H

#if defined(_MSC_VER)
    #define ENGINE_API __declspec(dllexport)
#else
    #define ENGINE_API __attribute__((visibility("default")))
#endif

extern "C" {
ENGINE_API void* CreateEngine(int initial_neurons);
ENGINE_API void DestroyEngine(void* engine_ptr);
ENGINE_API void StepEngine(void* engine_ptr, float input_signal, bool good_action, bool bad_action, bool novel_state);
ENGINE_API void StepEngineChar(void* engine_ptr, char c, bool novel_state); // Added P/Invoke export
ENGINE_API void SimulateEngineExperience(void* engine_ptr, float signal, bool good, bool bad, int iterations);
ENGINE_API bool ExecuteEngineRealWorldAction(void* engine_ptr, float input_signal, bool action_result_was_bad);
ENGINE_API int GetEngineRealWorldChances(void* engine_ptr);
ENGINE_API int SpawnNewNeuron(void* engine_ptr);
ENGINE_API bool SaveEngineState(void* engine_ptr, const char* filepath);
ENGINE_API bool LoadEngineState(void* engine_ptr, const char* filepath);

ENGINE_API bool ExportEngineSubnet(void* engine_ptr, const char* filepath, int start_id, int count);
ENGINE_API bool ImportEngineSubnet(void* engine_ptr, const char* filepath);
ENGINE_API int GenerateEngineCompletion(void* engine_ptr, const char* prompt, char* buffer, int buffer_size, int max_chars);
}

#endif //DYNAMICNEURALENGINE_LIBRARY_H