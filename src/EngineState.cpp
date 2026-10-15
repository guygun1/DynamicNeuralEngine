#include "EngineState.hpp"
#include <fstream>
#include <iostream>
#include <algorithm>
#include <cmath>

constexpr uint32_t BRAIN_MAGIC = 0x41474931; // "AGI1"

EngineState::EngineState(int initial_capacity) {
    neurons.reserve(initial_capacity);
    for (size_t i = 0; i < alphabet.length(); ++i) {
        Neuron n;
        n.id = static_cast<int>(i);
        n.bias = 0.1f;
        n.activation = 0.0f;
        neurons.push_back(n);
    }
}
void EngineState::Simulate(float signal, bool goodAction, bool badAction, int iterations) {
    for (int iter = 0; iter < iterations; ++iter) {
        for (auto& neuron : neurons) {
            neuron.activation *= 0.9f;
            for (auto& syn : neuron.outgoing_synapses) {
                if (goodAction) syn.weight += 0.001f * signal;
                if (badAction) syn.weight -= 0.002f * signal;
                syn.weight = std::clamp(syn.weight, -1.0f, 5.0f);
            }
        }
    }
}

int EngineState::GetCharNeuronIndex(char c) {
    char lower = static_cast<char>(tolower(c));
    size_t pos = alphabet.find(lower);
    if (pos != std::string::npos) {
        return static_cast<int>(pos);
    }
    size_t space_pos = alphabet.find(' ');
    return space_pos != std::string::npos ? static_cast<int>(space_pos) : 0;
}

void EngineState::Step(float input_signal, bool good_action, bool bad_action, bool novel_state) {
    // Map numerical signal / character code to engine step
    char c = (input_signal >= 32 && input_signal <= 126) ? static_cast<char>(input_signal) : ' ';
    StepChar(c, novel_state);
    SimulateExperience(std::abs(input_signal), good_action, bad_action, 1);
}

void EngineState::StepChar(char c, bool novelState) {
    int current_idx = GetCharNeuronIndex(c);

    if (current_idx >= static_cast<int>(neurons.size())) {
        while (neurons.size() <= static_cast<size_t>(current_idx)) {
            Neuron n;
            n.id = static_cast<int>(neurons.size());
            n.bias = 0.1f;
            n.activation = 0.0f;
            neurons.push_back(n);
        }
    }

    if (last_char_neuron_idx >= 0 && last_char_neuron_idx < static_cast<int>(neurons.size())) {
        auto& prev_neuron = neurons[last_char_neuron_idx];
        auto it = std::find_if(prev_neuron.outgoing_synapses.begin(), prev_neuron.outgoing_synapses.end(),
            [current_idx](const Synapse& s) { return s.target_neuron_id == current_idx; });

        if (it != prev_neuron.outgoing_synapses.end()) {
            it->weight += dynamic_learning_rate;
            if (it->weight > 5.0f) it->weight = 5.0f;
            it->age_ticks = 0;
        } else {
            Synapse new_syn;
            new_syn.target_neuron_id = current_idx;
            new_syn.weight = 0.5f;
            new_syn.age_ticks = 0;
            prev_neuron.outgoing_synapses.push_back(new_syn);
        }
    }

    neurons[current_idx].activation = 1.0f;
    last_char_neuron_idx = current_idx;
}

void EngineState::SimulateExperience(float signal, bool goodAction, bool badAction, int iterations) {
    for (int iter = 0; iter < iterations; ++iter) {
        for (auto& neuron : neurons) {
            neuron.activation *= 0.9f;
            for (auto& syn : neuron.outgoing_synapses) {
                if (goodAction) syn.weight += 0.001f * signal;
                if (badAction) syn.weight -= 0.002f * signal;
                syn.weight = std::clamp(syn.weight, -1.0f, 5.0f);
            }
        }
    }
}

bool EngineState::ExecuteRealWorldAction(float input_signal, bool action_result_was_bad) {
    if (action_result_was_bad) {
        real_world_chances--;
    } else {
        if (real_world_chances < 4) real_world_chances++;
    }
    return real_world_chances > 0;
}

int EngineState::GetRealWorldChances() {
    return real_world_chances;
}

int EngineState::SpawnNeuron() {
    Neuron n;
    n.id = static_cast<int>(neurons.size());
    n.bias = 0.1f;
    n.activation = 0.0f;
    neurons.push_back(n);
    return n.id;
}

int EngineState::GenerateCompletionBuffer(const std::string& prompt, char* buffer, int buffer_size, int max_chars) {
    for (char c : prompt) {
        StepChar(c, false);
    }

    std::string result = prompt;

    for (int i = 0; i < max_chars && result.length() < buffer_size - 1; ++i) {
        char current_char = result.back();
        int current_neuron_idx = GetCharNeuronIndex(current_char);

        char best_char = ' ';
        float max_weight = -1e9f;

        if (current_neuron_idx < static_cast<int>(neurons.size())) {
            const auto& outgoing = neurons[current_neuron_idx].outgoing_synapses;
            for (const auto& syn : outgoing) {
                if (syn.target_neuron_id >= 0 && syn.target_neuron_id < static_cast<int>(alphabet.length())) {
                    char target_char = alphabet[syn.target_neuron_id];
                    float weight = syn.weight;

                    if (target_char == current_char) {
                        weight -= 0.8f;
                    }

                    if (result.length() >= 5) {
                        std::string recent_window = result.substr(result.length() - 5);
                        std::string prospective_string = recent_window + target_char;
                        size_t first_occurrence = result.find(prospective_string);
                        if (first_occurrence != std::string::npos && first_occurrence < result.length() - 5) {
                            weight -= 1.5f;
                        }
                    }

                    if (weight > max_weight) {
                        max_weight = weight;
                        best_char = target_char;
                    }
                }
            }
        }

        if (max_weight <= -1e8f) {
            best_char = ' ';
        }

        result += best_char;
        StepChar(best_char, false);

        if (best_char == '.' && i > 2) {
            break;
        }
    }

#if defined(_MSC_VER)
    strcpy_s(buffer, buffer_size, result.c_str());
#else
    strncpy(buffer, result.c_str(), buffer_size);
    buffer[buffer_size - 1] = '\0';
#endif

    return static_cast<int>(result.length());
}

bool EngineState::SaveToFile(const std::string& filepath) {
    return SaveFullBrainState(filepath);
}

bool EngineState::LoadFromFile(const std::string& filepath) {
    return LoadFullBrainState(filepath);
}

bool EngineState::ImportAndMergeSubnet(const std::string& filepath) {
    return ImportSubnet(filepath);
}

bool EngineState::ExportSubnet(const std::string& filepath, int start_neuron, int count) {
    std::ofstream out(filepath, std::ios::binary);
    if (!out.is_open()) return false;

    int actual_count = std::min(count, static_cast<int>(neurons.size()) - start_neuron);
    if (actual_count <= 0) return false;

    out.write(reinterpret_cast<const char*>(&actual_count), sizeof(actual_count));
    for (int i = start_neuron; i < start_neuron + actual_count; ++i) {
        const auto& n = neurons[i];
        out.write(reinterpret_cast<const char*>(&n.id), sizeof(n.id));
        out.write(reinterpret_cast<const char*>(&n.bias), sizeof(n.bias));

        int syn_count = static_cast<int>(n.outgoing_synapses.size());
        out.write(reinterpret_cast<const char*>(&syn_count), sizeof(syn_count));
        for (const auto& s : n.outgoing_synapses) {
            out.write(reinterpret_cast<const char*>(&s.target_neuron_id), sizeof(s.target_neuron_id));
            out.write(reinterpret_cast<const char*>(&s.weight), sizeof(s.weight));
        }
    }
    out.close();
    return true;
}

bool EngineState::ImportSubnet(const std::string& filepath) {
    std::ifstream in(filepath, std::ios::binary);
    if (!in.is_open()) return false;

    int count = 0;
    in.read(reinterpret_cast<char*>(&count), sizeof(count));

    for (int i = 0; i < count; ++i) {
        int id;
        float bias;
        in.read(reinterpret_cast<char*>(&id), sizeof(id));
        in.read(reinterpret_cast<char*>(&bias), sizeof(bias));

        int syn_count;
        in.read(reinterpret_cast<char*>(&syn_count), sizeof(syn_count));

        while (neurons.size() <= static_cast<size_t>(id)) {
            Neuron n;
            n.id = static_cast<int>(neurons.size());
            n.bias = 0.1f;
            n.activation = 0.0f;
            neurons.push_back(n);
        }

        neurons[id].bias = bias;
        for (int s = 0; s < syn_count; ++s) {
            int target_id;
            float weight;
            in.read(reinterpret_cast<char*>(&target_id), sizeof(target_id));
            in.read(reinterpret_cast<char*>(&weight), sizeof(weight));

            auto& out_syns = neurons[id].outgoing_synapses;
            auto it = std::find_if(out_syns.begin(), out_syns.end(),
                [target_id](const Synapse& syn) { return syn.target_neuron_id == target_id; });

            if (it != out_syns.end()) {
                it->weight = std::max(it->weight, weight);
            } else {
                Synapse new_s{ target_id, weight, 0 };
                out_syns.push_back(new_s);
            }
        }
    }
    in.close();
    return true;
}

bool EngineState::SaveFullBrainState(const std::string& filepath) {
    std::ofstream out(filepath, std::ios::binary);
    if (!out.is_open()) return false;

    out.write(reinterpret_cast<const char*>(&BRAIN_MAGIC), sizeof(BRAIN_MAGIC));

    uint32_t alphabet_len = static_cast<uint32_t>(alphabet.length());
    out.write(reinterpret_cast<const char*>(&alphabet_len), sizeof(alphabet_len));
    out.write(alphabet.data(), alphabet_len);

    out.write(reinterpret_cast<const char*>(&last_char_neuron_idx), sizeof(last_char_neuron_idx));
    out.write(reinterpret_cast<const char*>(&dynamic_learning_rate), sizeof(dynamic_learning_rate));
    out.write(reinterpret_cast<const char*>(&system_novelty), sizeof(system_novelty));
    out.write(reinterpret_cast<const char*>(&real_world_chances), sizeof(real_world_chances));

    uint32_t total_neurons = static_cast<uint32_t>(neurons.size());
    out.write(reinterpret_cast<const char*>(&total_neurons), sizeof(total_neurons));

    for (const auto& neuron : neurons) {
        out.write(reinterpret_cast<const char*>(&neuron.id), sizeof(neuron.id));
        out.write(reinterpret_cast<const char*>(&neuron.bias), sizeof(neuron.bias));
        out.write(reinterpret_cast<const char*>(&neuron.activation), sizeof(neuron.activation));

        uint32_t total_synapses = static_cast<uint32_t>(neuron.outgoing_synapses.size());
        out.write(reinterpret_cast<const char*>(&total_synapses), sizeof(total_synapses));

        for (const auto& syn : neuron.outgoing_synapses) {
            out.write(reinterpret_cast<const char*>(&syn.target_neuron_id), sizeof(syn.target_neuron_id));
            out.write(reinterpret_cast<const char*>(&syn.weight), sizeof(syn.weight));
            out.write(reinterpret_cast<const char*>(&syn.age_ticks), sizeof(syn.age_ticks));
        }
    }

    out.close();
    return true;
}

bool EngineState::LoadFullBrainState(const std::string& filepath) {
    std::ifstream in(filepath, std::ios::binary);
    if (!in.is_open()) return false;

    uint32_t magic = 0;
    in.read(reinterpret_cast<char*>(&magic), sizeof(magic));
    if (magic != BRAIN_MAGIC) return false;

    uint32_t alphabet_len = 0;
    in.read(reinterpret_cast<char*>(&alphabet_len), sizeof(alphabet_len));
    alphabet.resize(alphabet_len);
    in.read(&alphabet[0], alphabet_len);

    in.read(reinterpret_cast<char*>(&last_char_neuron_idx), sizeof(last_char_neuron_idx));
    in.read(reinterpret_cast<char*>(&dynamic_learning_rate), sizeof(dynamic_learning_rate));
    in.read(reinterpret_cast<char*>(&system_novelty), sizeof(system_novelty));
    in.read(reinterpret_cast<char*>(&real_world_chances), sizeof(real_world_chances));

    uint32_t total_neurons = 0;
    in.read(reinterpret_cast<char*>(&total_neurons), sizeof(total_neurons));
    neurons.clear();
    neurons.resize(total_neurons);

    for (uint32_t i = 0; i < total_neurons; ++i) {
        in.read(reinterpret_cast<char*>(&neurons[i].id), sizeof(neurons[i].id));
        in.read(reinterpret_cast<char*>(&neurons[i].bias), sizeof(neurons[i].bias));
        in.read(reinterpret_cast<char*>(&neurons[i].activation), sizeof(neurons[i].activation));

        uint32_t total_synapses = 0;
        in.read(reinterpret_cast<char*>(&total_synapses), sizeof(total_synapses));
        neurons[i].outgoing_synapses.resize(total_synapses);

        for (uint32_t j = 0; j < total_synapses; ++j) {
            in.read(reinterpret_cast<char*>(&neurons[i].outgoing_synapses[j].target_neuron_id), sizeof(int));
            in.read(reinterpret_cast<char*>(&neurons[i].outgoing_synapses[j].weight), sizeof(float));
            in.read(reinterpret_cast<char*>(&neurons[i].outgoing_synapses[j].age_ticks), sizeof(uint32_t));
        }
    }

    in.close();
    return true;
}