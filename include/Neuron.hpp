#pragma once
#include <vector>
#include "Synapse.hpp"

struct Neuron {
    int id;
    float activation;
    bool is_active;
    std::vector<Synapse> outgoing_synapses;
};