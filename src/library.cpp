#include "EngineState.hpp"
#include <algorithm>

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

__declspec(dllexport) void StepTokenEngine(EngineState* ptr, const char* token, bool novel) {
    if (ptr && token) ptr->StepToken(token, novel);
}

__declspec(dllexport) void TrainTextEngine(EngineState* ptr, const char* text) {
    if (ptr && text) ptr->TrainSentence(text);
}

__declspec(dllexport) void TrainDialogueEngine(EngineState* ptr, const char* prompt, const char* response) {
    if (ptr && prompt && response) ptr->TrainDialogue(prompt, response);
}

__declspec(dllexport) void TrainFromSourceEngine(EngineState* ptr, const char* prompt, const char* passage, const char* source) {
    if (ptr && prompt && passage && source) ptr->GetTextModality().TrainFromSource(ptr->GetGraph(), prompt, passage, source);
}

__declspec(dllexport) int GetAnswerStateEngine(EngineState* ptr) {
    return ptr ? ptr->GetTextModality().GetAnswerState() : 0;
}

__declspec(dllexport) int GetConversationTopicEngine(EngineState* ptr, char* buffer, int capacity) {
    if (!ptr || !buffer || capacity <= 0) return 0;
    const auto& topic = ptr->GetTextModality().GetConversationTopic();
    int count = std::min(static_cast<int>(topic.size()), capacity - 1);
    std::copy_n(topic.c_str(), count, buffer); buffer[count] = '\0'; return count;
}

__declspec(dllexport) void SimulateEngine(EngineState* ptr, float sig, bool good, bool bad, int iter) {
    if (ptr) ptr->Simulate(sig, good, bad, iter);
}

__declspec(dllexport) int SpawnNeuron(EngineState* ptr) {
    if (!ptr) return -1;
    return ptr->SpawnNeuron();
}

__declspec(dllexport) int SpawnNeuronEngine(EngineState* ptr) {
    if (!ptr) return -1;
    return ptr->SpawnNeuron();
}

__declspec(dllexport) int GenerateCompletion(EngineState* ptr, const char* prompt, char* buffer, int buf_size, int max_chars) {
    if (!ptr) return 0;
    return ptr->GenerateCompletionBuffer(prompt, buffer, buf_size, max_chars);
}

__declspec(dllexport) int ProposeEngine(EngineState* ptr, const char* prompt, char* buffer, int buf_size, int max_chars, unsigned int seed) {
    if (!ptr || !prompt) return 0;
    return ptr->Propose(prompt, buffer, buf_size, max_chars, seed);
}

__declspec(dllexport) int ProcessLiveDialogueEngine(EngineState* ptr, const char* input, char* buffer, int buf_size) {
    if (!ptr || !input || !buffer || buf_size <= 0) return 0;
    return ptr->ProcessLiveDialogue(input, buffer, buf_size);
}

__declspec(dllexport) int ProcessLiveDialogueWithLimitEngine(EngineState* ptr, const char* input, char* buffer, int buf_size, int max_chars) {
    if (!ptr || !input) return 0;
    return ptr->ProcessLiveDialogue(input, buffer, buf_size, max_chars);
}

__declspec(dllexport) int GetCurriculumFingerprintEngine(EngineState* ptr, char* buffer, int capacity) {
    if (!ptr || !buffer || capacity <= 0) return 0;
    const auto& value = ptr->GetTextModality().GetCurriculumFingerprint();
    int length = static_cast<int>(value.size());
    int count = std::min(length, capacity - 1);
    std::copy_n(value.c_str(), count, buffer);
    buffer[count] = '\0';
    return count;
}

__declspec(dllexport) void SetCurriculumFingerprintEngine(EngineState* ptr, const char* value) {
    if (ptr && value) ptr->GetTextModality().SetCurriculumFingerprint(value);
}

__declspec(dllexport) void SetConversationTopicEngine(EngineState* ptr, const char* value) {
    if (ptr && value) ptr->GetTextModality().SetConversationTopic(value, &ptr->GetGraph());
}

__declspec(dllexport) void AdaptIdentityEngine(EngineState* ptr, const char* name) {
    if (ptr && name) ptr->AdaptIdentity(name);
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

__declspec(dllexport) int GetNeuronCount(EngineState* ptr) {
    if (!ptr) return 0;
    return ptr->GetNeuronCount();
}

__declspec(dllexport) int GetVocabularyCount(EngineState* ptr) {
    if (!ptr) return 0;
    return ptr->GetVocabularyCount();
}

__declspec(dllexport) int FindWordNeuronEngine(EngineState* ptr, const char* word) {
    return ptr && word ? ptr->GetTextModality().FindWordNeuron(word) : -1;
}

__declspec(dllexport) float WordConnectionWeightEngine(EngineState* ptr, const char* from, const char* to) {
    if (!ptr || !from || !to) return 0.0f;
    return ptr->GetGraph().GetSynapseWeight(ptr->GetTextModality().FindWordNeuron(from), ptr->GetTextModality().FindWordNeuron(to));
}

__declspec(dllexport) float GetActionValueEngine(EngineState* ptr, const char* state, const char* action) {
    return ptr && state && action ? ptr->ActionValue(state, action) : 0.0f;
}

__declspec(dllexport) void LearnActionValueEngine(EngineState* ptr, const char* state, const char* action, float target, float learning_rate) {
    if (ptr && state && action) ptr->LearnActionValue(state, action, target, learning_rate);
}
}
