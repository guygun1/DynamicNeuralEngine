#pragma once

#include "Neuron.hpp"
#include <vector>
#include <string>

class NeuralGraph {
public:
    NeuralGraph(int initial_capacity = 10);
    ~NeuralGraph() = default;

    int SpawnNeuron();
    void EnsureCapacity(size_t capacity);

    void AddSynapse(int from_id, int to_id, float delta_weight);
    void DepressSynapse(int from_id, int to_id, float amount);
    float GetSynapseWeight(int from_id, int to_id) const;
    void SetSynapseWeight(int from_id, int to_id, float weight);

    void Simulate(float signal, bool goodAction, bool badAction, int iterations);
    void ClearActivations(float decay_factor = 0.1f);

    bool ExportSubnet(const std::string& filepath, int start_neuron, int count) const;
    bool ImportSubnet(const std::string& filepath);

    int GetNeuronCount() const { return static_cast<int>(neurons_.size()); }
    Neuron& GetNeuron(int id) { return neurons_[id]; }
    const Neuron& GetNeuron(int id) const { return neurons_[id]; }
    std::vector<Neuron>& GetAllNeurons() { return neurons_; }
    const std::vector<Neuron>& GetAllNeurons() const { return neurons_; }

    void Clear() { neurons_.clear(); }
    void Resize(size_t size);

private:
    std::vector<Neuron> neurons_;
};
