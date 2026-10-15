#pragma once
#include <vector>
#include "Synapse.hpp"

struct Neuron {
    int id;
    float bias;
    float activation;
    std::vector<Synapse> outgoing_synapses;
};
