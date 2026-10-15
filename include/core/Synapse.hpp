#pragma once
#include <cstdint>

struct Synapse {
    int target_neuron_id;
    float weight;
    uint32_t age_ticks;
};
