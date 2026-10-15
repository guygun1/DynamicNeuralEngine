#pragma once
#include <algorithm>

struct EmotionalState {
    float happiness = 0.5f;
    float sadness   = 0.0f;
    float guilt     = 0.0f;
    float fear      = 0.0f; // New: Scaled caution that never reaches absolute paralysis
    float curiosity = 0.8f;
    float boredom   = 0.0f;


    void Update(bool success, bool violation, bool is_novel_state) {
        if (violation) {
            guilt = std::min(1.0f, guilt + 0.4f);
            fear  = std::min(1.0f, fear + 0.3f);     // Pain increases fear
            happiness = std::max(0.0f, happiness - 0.3f);
        } else if (success) {
            happiness = std::min(1.0f, happiness + 0.2f);
            sadness = std::max(0.0f, sadness - 0.2f);
            guilt = std::max(0.0f, guilt - 0.1f);
            fear  = std::max(0.0f, fear - 0.15f);    // Success heals fear gradually
        } else {
            sadness = std::min(1.0f, sadness + 0.05f);
            happiness = std::max(0.0f, happiness - 0.05f);
        }

        // Fear decay over time so it never permanently traps the agent
        fear = std::max(0.0f, fear - 0.02f);

        // Curiosity & Boredom Dynamics
        if (is_novel_state) {
            curiosity = std::min(1.0f, curiosity + 0.15f);
            boredom = std::max(0.0f, boredom - 0.25f);
        } else {
            boredom = std::min(1.0f, boredom + 0.08f);
            if (boredom > 0.5f) {
                curiosity = std::min(1.0f, curiosity + 0.1f);
            }
        }
    }
};