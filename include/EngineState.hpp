#pragma once

#include <string>
#include <vector>
#include <cstdint>

struct Synapse {
    int target_neuron_id;
    float weight;
    uint32_t age_ticks;
};

struct Neuron {
    int id;
    float bias;
    float activation;
    std::vector<Synapse> outgoing_synapses;
};

class EngineState {
public:
    EngineState(int initial_capacity = 10);
    ~EngineState() = default;

    // Core Neural & Survival Operations (Matched to library.cpp)
    void Step(float input_signal, bool good_action, bool bad_action, bool novel_state);
    void StepChar(char c, bool novelState);
    void SimulateExperience(float signal, bool goodAction, bool badAction, int iterations);
    void Simulate(float signal, bool goodAction, bool badAction, int iterations);
    bool ExecuteRealWorldAction(float input_signal, bool action_result_was_bad);
    int GetRealWorldChances();
    int SpawnNeuron();

    int GenerateCompletionBuffer(const std::string& prompt, char* buffer, int buffer_size, int max_chars);

    // Subnet & File Persistence Aliases
    bool SaveToFile(const std::string& filepath);
    bool LoadFromFile(const std::string& filepath);
    bool ImportAndMergeSubnet(const std::string& filepath);

    bool ExportSubnet(const std::string& filepath, int start_neuron, int count);
    bool ImportSubnet(const std::string& filepath);

    // Unified Single-File State Persistence
    bool SaveFullBrainState(const std::string& filepath);
    bool LoadFullBrainState(const std::string& filepath);

private:
    int GetCharNeuronIndex(char c);

    std::string alphabet = "abcdefghijklmnopqrstuvwxyz ?.";
    std::vector<Neuron> neurons;
    int last_char_neuron_idx = -1;
    float dynamic_learning_rate = 0.05f;
    float system_novelty = 0.0f;
    int real_world_chances = 4;
};