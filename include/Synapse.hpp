#pragma once

struct Synapse {
    int target_neuron_id;
    float weight;
    bool is_active;

    // Adaptive Curiosity & Backoff Tracking
    int failure_count = 0;
    int steps_since_last_check = 0;
    int exploration_cooldown = 1; // Starts at 1, grows exponentially upon repeated failure
};