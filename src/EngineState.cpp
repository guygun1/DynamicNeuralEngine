#include "EngineState.hpp"
#include "core/BrainPersistence.hpp"
#include <cmath>
#include "modalities/LanguageRequests.hpp"

EngineState::EngineState(int initial_capacity)
    : graph_(initial_capacity) {
    text_modality_.Initialize(graph_);
}

void EngineState::Step(float input_signal, bool good_action, bool bad_action, bool novel_state) {
    char c = (input_signal >= 32 && input_signal <= 126) ? static_cast<char>(input_signal) : ' ';
    StepChar(c, novel_state);
    SimulateExperience(std::abs(input_signal), good_action, bad_action, 1);
}

void EngineState::StepChar(char c, bool novelState) {
    text_modality_.StepChar(graph_, c, novelState);
}

void EngineState::StepToken(const std::string& token, bool novelState) {
    text_modality_.StepToken(graph_, token, novelState);
}

void EngineState::TrainDialogue(const std::string& prompt, const std::string& response) {
    text_modality_.TrainDialogue(graph_, prompt, response);
}

void EngineState::TrainSentence(const std::string& sentence) {
    text_modality_.TrainSentence(graph_, sentence);
}

void EngineState::SimulateExperience(float signal, bool goodAction, bool badAction, int iterations) {
    Simulate(signal, goodAction, badAction, iterations);
}

void EngineState::Simulate(float signal, bool goodAction, bool badAction, int iterations) {
    graph_.Simulate(signal, goodAction, badAction, iterations);
    emotions_.Update(goodAction, badAction, false);
}

bool EngineState::ExecuteRealWorldAction(float input_signal, bool action_result_was_bad) {
    if (action_result_was_bad) {
        emotions_.real_world_chances = std::max(0, emotions_.real_world_chances - 1);
    } else {
        if (emotions_.real_world_chances < 4) emotions_.real_world_chances++;
    }
    return emotions_.real_world_chances > 0;
}

int EngineState::GetRealWorldChances() const {
    return emotions_.real_world_chances;
}

int EngineState::SpawnNeuron() {
    return graph_.SpawnNeuron();
}

int EngineState::GenerateCompletionBuffer(const std::string& prompt, char* buffer, int buffer_size, int max_chars) {
    return text_modality_.GenerateCompletion(graph_, prompt, buffer, buffer_size, max_chars);
}

int EngineState::Propose(const std::string& prompt, char* buffer, int buffer_size, int max_chars, unsigned int seed) const {
    return text_modality_.Propose(prompt, buffer, buffer_size, max_chars, seed);
}

int EngineState::ProcessLiveDialogue(const std::string& input, char* buffer, int buffer_size, int max_chars) {
    return text_modality_.ProcessLiveDialogue(graph_, input, buffer, buffer_size, max_chars);
}

void EngineState::AdaptIdentity(const std::string& new_name) {
    text_modality_.AdaptIdentity(graph_, new_name);
}

float EngineState::ActionValue(const std::string& state, const std::string& action) const {
    auto normalized_state = LanguageRequests::Join(text_modality_.Tokenize(state));
    auto normalized_action = LanguageRequests::Join(text_modality_.Tokenize(action));
    if (normalized_state.empty() || normalized_action.empty()) return 0.0f;
    auto state_label = "state " + normalized_state;
    auto action_label = "action " + normalized_action;
    return graph_.GetSynapseWeight(text_modality_.FindWordNeuron(state_label), text_modality_.FindWordNeuron(action_label));
}

void EngineState::LearnActionValue(const std::string& state, const std::string& action, float target, float learning_rate) {
    if (!std::isfinite(target) || !std::isfinite(learning_rate) || learning_rate <= 0 || learning_rate > 1 || state.empty() || action.empty() || state.size() > 256 || action.size() > 256) return;
    auto normalized_state = LanguageRequests::Join(text_modality_.Tokenize(state));
    auto normalized_action = LanguageRequests::Join(text_modality_.Tokenize(action));
    if (normalized_state.empty() || normalized_action.empty()) return;
    auto state_label = "state " + normalized_state;
    auto action_label = "action " + normalized_action;
    int state_id = text_modality_.GetOrCreateTokenNeuron(graph_, state_label);
    int action_id = text_modality_.GetOrCreateTokenNeuron(graph_, action_label);
    float value = graph_.GetSynapseWeight(state_id, action_id);
    graph_.SetSynapseWeight(state_id, action_id, value + learning_rate * (target - value));
}

bool EngineState::SaveFullBrainState(const std::string& filepath) {
    return BrainPersistence::SaveBrainState(
        filepath, graph_, text_modality_, emotions_, dynamic_learning_rate_, system_novelty_
    );
}

bool EngineState::LoadFullBrainState(const std::string& filepath) {
    return BrainPersistence::LoadBrainState(
        filepath, graph_, text_modality_, emotions_, dynamic_learning_rate_, system_novelty_
    );
}

bool EngineState::SaveToFile(const std::string& filepath) {
    return SaveFullBrainState(filepath);
}

bool EngineState::LoadFromFile(const std::string& filepath) {
    return LoadFullBrainState(filepath);
}

bool EngineState::ExportSubnet(const std::string& filepath, int start_neuron, int count) {
    return graph_.ExportSubnet(filepath, start_neuron, count);
}

bool EngineState::ImportSubnet(const std::string& filepath) {
    return graph_.ImportSubnet(filepath);
}

bool EngineState::ImportAndMergeSubnet(const std::string& filepath) {
    return graph_.ImportSubnet(filepath);
}

int EngineState::GetNeuronCount() const {
    return graph_.GetNeuronCount();
}

int EngineState::GetVocabularyCount() const {
    return text_modality_.GetVocabularyCount();
}
