#include "core/NeuralGraph.hpp"
#include <algorithm>
#include <cmath>
#include <fstream>

NeuralGraph::NeuralGraph(int initial_capacity) {
    neurons_.reserve(initial_capacity);
}

int NeuralGraph::SpawnNeuron() {
    int id = static_cast<int>(neurons_.size());
    Neuron n;
    n.id = id;
    n.bias = 0.1f;
    n.activation = 0.0f;
    neurons_.push_back(n);
    return id;
}

void NeuralGraph::EnsureCapacity(size_t capacity) {
    while (neurons_.size() < capacity) {
        SpawnNeuron();
    }
}

void NeuralGraph::Resize(size_t size) {
    neurons_.resize(size);
}

void NeuralGraph::AddSynapse(int from_id, int to_id, float delta_weight) {
    if (from_id < 0 || from_id >= static_cast<int>(neurons_.size())) return;
    if (to_id < 0 || to_id >= static_cast<int>(neurons_.size())) return;
    if (from_id == to_id) return; // Prevent immediate self-loops

    auto& from_neuron = neurons_[from_id];
    for (auto& syn : from_neuron.outgoing_synapses) {
        if (syn.target_neuron_id == to_id) {
            syn.weight += delta_weight;
            if (syn.weight > 6.0f) syn.weight = 6.0f;
            syn.age_ticks = 0;
            return;
        }
    }

    // Structural Plasticity: Sprout new synapse
    Synapse new_syn;
    new_syn.target_neuron_id = to_id;
    new_syn.weight = std::clamp(delta_weight, 0.2f, 6.0f);
    new_syn.age_ticks = 0;
    from_neuron.outgoing_synapses.push_back(new_syn);
}

void NeuralGraph::DepressSynapse(int from_id, int to_id, float amount) {
    if (from_id < 0 || from_id >= static_cast<int>(neurons_.size())) return;
    auto& n = neurons_[from_id];
    for (auto& syn : n.outgoing_synapses) {
        if (syn.target_neuron_id == to_id) {
            syn.weight = std::max(0.01f, syn.weight - amount);
            return;
        }
    }
}

float NeuralGraph::GetSynapseWeight(int from_id, int to_id) const {
    if (from_id < 0 || from_id >= static_cast<int>(neurons_.size())) return 0.0f;
    const auto& n = neurons_[from_id];
    for (const auto& syn : n.outgoing_synapses) {
        if (syn.target_neuron_id == to_id) return syn.weight;
    }
    return 0.0f;
}

void NeuralGraph::SetSynapseWeight(int from_id, int to_id, float weight) {
    if (from_id < 0 || to_id < 0 || from_id >= GetNeuronCount() || to_id >= GetNeuronCount() || from_id == to_id || !std::isfinite(weight)) return;
    for (auto& synapse : neurons_[from_id].outgoing_synapses) {
        if (synapse.target_neuron_id != to_id) continue;
        synapse.weight = std::clamp(weight, -1.0f, 6.0f); synapse.age_ticks = 0; return;
    }
    neurons_[from_id].outgoing_synapses.push_back({to_id, std::clamp(weight, -1.0f, 6.0f), 0});
}

void NeuralGraph::Simulate(float signal, bool goodAction, bool badAction, int iterations) {
    for (int iter = 0; iter < iterations; ++iter) {
        for (auto& neuron : neurons_) {
            neuron.activation *= 0.95f;
            for (auto& syn : neuron.outgoing_synapses) {
                if (badAction) syn.weight -= 0.002f * signal;
                syn.weight = std::clamp(syn.weight, -1.0f, 6.0f);
            }
        }
    }
}

void NeuralGraph::ClearActivations(float decay_factor) {
    for (auto& n : neurons_) {
        n.activation *= decay_factor;
    }
}

bool NeuralGraph::ExportSubnet(const std::string& filepath, int start_neuron, int count) const {
    std::ofstream out(filepath, std::ios::binary);
    if (!out.is_open()) return false;

    int actual_count = std::min(count, static_cast<int>(neurons_.size()) - start_neuron);
    if (actual_count <= 0) return false;

    out.write(reinterpret_cast<const char*>(&actual_count), sizeof(actual_count));
    for (int i = start_neuron; i < start_neuron + actual_count; ++i) {
        const auto& n = neurons_[i];
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

bool NeuralGraph::ImportSubnet(const std::string& filepath) {
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

        EnsureCapacity(static_cast<size_t>(id + 1));
        neurons_[id].bias = bias;

        for (int s = 0; s < syn_count; ++s) {
            int target_id;
            float weight;
            in.read(reinterpret_cast<char*>(&target_id), sizeof(target_id));
            in.read(reinterpret_cast<char*>(&weight), sizeof(weight));

            auto& out_syns = neurons_[id].outgoing_synapses;
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
