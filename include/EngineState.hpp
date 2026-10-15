#pragma once

#include "core/NeuralGraph.hpp"
#include "core/EmotionalState.hpp"
#include "modalities/TextLanguageModality.hpp"
#include <memory>
#include <string>
#include <vector>

/// <summary>
/// Central AGI Engine Orchestrator.
/// Combines the modality-agnostic NeuralGraph, Emotional State, and active Modality Adapters
/// (Text/Language, and future Vision/Video, Spatial 3D, and Computer Use).
/// </summary>
class EngineState {
public:
    EngineState(int initial_capacity = 10);
    ~EngineState() = default;

    // Core Neural Operations
    void Step(float input_signal, bool good_action, bool bad_action, bool novel_state);
    void StepChar(char c, bool novelState);
    void StepToken(const std::string& token, bool novelState);
    void TrainSentence(const std::string& sentence);
    void TrainDialogue(const std::string& prompt, const std::string& response);

    void SimulateExperience(float signal, bool goodAction, bool badAction, int iterations);
    void Simulate(float signal, bool goodAction, bool badAction, int iterations);
    bool ExecuteRealWorldAction(float input_signal, bool action_result_was_bad);
    int GetRealWorldChances() const;
    int SpawnNeuron();

    // Natural Language Modality Forwarding
    int GenerateCompletionBuffer(const std::string& prompt, char* buffer, int buffer_size, int max_chars);
    int Propose(const std::string& prompt, char* buffer, int buffer_size, int max_chars, unsigned int seed) const;
    int ProcessLiveDialogue(const std::string& input, char* buffer, int buffer_size, int max_chars = 8192);
    void AdaptIdentity(const std::string& new_name);
    float ActionValue(const std::string& state, const std::string& action) const;
    void LearnActionValue(const std::string& state, const std::string& action, float target, float learning_rate);

    // Persistence API
    bool SaveFullBrainState(const std::string& filepath);
    bool LoadFullBrainState(const std::string& filepath);
    bool SaveToFile(const std::string& filepath);
    bool LoadFromFile(const std::string& filepath);
    bool ExportSubnet(const std::string& filepath, int start_neuron, int count);
    bool ImportSubnet(const std::string& filepath);
    bool ImportAndMergeSubnet(const std::string& filepath);

    // Inspection
    int GetNeuronCount() const;
    int GetVocabularyCount() const;

    // Component Accessors
    NeuralGraph& GetGraph() { return graph_; }
    const NeuralGraph& GetGraph() const { return graph_; }
    TextLanguageModality& GetTextModality() { return text_modality_; }
    const TextLanguageModality& GetTextModality() const { return text_modality_; }
    EmotionalState& GetEmotions() { return emotions_; }
    const EmotionalState& GetEmotions() const { return emotions_; }

private:
    NeuralGraph graph_;
    EmotionalState emotions_;
    TextLanguageModality text_modality_;

    float dynamic_learning_rate_ = 0.20f;
    float system_novelty_ = 0.0f;
};
