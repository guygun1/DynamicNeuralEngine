#include "EngineState.hpp"

extern "C" {
__declspec(dllexport) EngineState* CreateEngine(int cap) {
    return new EngineState(cap);
}

__declspec(dllexport) void DestroyEngine(EngineState* ptr) {
    delete ptr;
}

__declspec(dllexport) void StepEngine(EngineState* ptr, float sig, bool good, bool bad, bool novel) {
    if (ptr) ptr->Step(sig, good, bad, novel);
}

__declspec(dllexport) void StepCharEngine(EngineState* ptr, char c, bool novel) {
    if (ptr) ptr->StepChar(c, novel);
}

__declspec(dllexport) void SimulateEngine(EngineState* ptr, float sig, bool good, bool bad, int iter) {
    if (ptr) ptr->Simulate(sig, good, bad, iter);
}

__declspec(dllexport) int SpawnNeuron(EngineState* ptr) {
    if (!ptr) return -1;
    return ptr->SpawnNeuron();
}

__declspec(dllexport) int GenerateCompletion(EngineState* ptr, const char* prompt, char* buffer, int buf_size, int max_chars) {
    if (!ptr) return 0;
    return ptr->GenerateCompletionBuffer(prompt, buffer, buf_size, max_chars);
}

__declspec(dllexport) bool ExportSubnetEngine(EngineState* ptr, const char* path, int start, int count) {
    if (!ptr) return false;
    return ptr->ExportSubnet(path, start, count);
}

__declspec(dllexport) bool ImportSubnetEngine(EngineState* ptr, const char* path) {
    if (!ptr) return false;
    return ptr->ImportSubnet(path);
}

__declspec(dllexport) bool SaveBrainFile(EngineState* ptr, const char* path) {
    if (!ptr) return false;
    return ptr->SaveFullBrainState(path);
}

__declspec(dllexport) bool LoadBrainFile(EngineState* ptr, const char* path) {
    if (!ptr) return false;
    return ptr->LoadFullBrainState(path);
}
}