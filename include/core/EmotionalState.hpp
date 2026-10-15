#pragma once
#include <algorithm>

struct EmotionalState {
    float happiness = 0.5f;
    float sadness   = 0.0f;
    float guilt     = 0.0f;
    float fear      = 0.0f;
    float curiosity = 0.8f;
    float boredom   = 0.0f;
    int real_world_chances = 4;

    void Update(bool success, bool violation, bool is_novel_state) {
        if (violation) {
            guilt = std::min(1.0f, guilt + 0.4f);
            fear  = std::min(1.0f, fear + 0.3f);
            happiness = std::max(0.0f, happiness - 0.3f);
            real_world_chances = std::max(0, real_world_chances - 1);
        } else if (success) {
            happiness = std::min(1.0f, happiness + 0.2f);
            sadness   = std::max(0.0f, sadness - 0.2f);
            guilt     = std::max(0.0f, guilt - 0.1f);
            fear      = std::max(0.0f, fear - 0.15f);
            if (real_world_chances < 4) real_world_chances++;
        } else {
            sadness   = std::min(1.0f, sadness + 0.05f);
            happiness = std::max(0.0f, happiness - 0.05f);
        }

        fear = std::max(0.0f, fear - 0.02f);

        if (is_novel_state) {
            curiosity = std::min(1.0f, curiosity + 0.15f);
            boredom   = std::max(0.0f, boredom - 0.25f);
        } else {
            boredom   = std::min(1.0f, boredom + 0.08f);
            if (boredom > 0.5f) {
                curiosity = std::min(1.0f, curiosity + 0.1f);
            }
        }
    }
};
