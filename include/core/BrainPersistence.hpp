#pragma once

#include <string>

class NeuralGraph;
class TextLanguageModality;
struct EmotionalState;

class BrainPersistence {
public:
    static bool SaveBrainState(
        const std::string& filepath,
        const NeuralGraph& graph,
        const TextLanguageModality& textModality,
        const EmotionalState& emotions,
        float dynamic_learning_rate,
        float system_novelty
    );

    static bool LoadBrainState(
        const std::string& filepath,
        NeuralGraph& graph,
        TextLanguageModality& textModality,
        EmotionalState& emotions,
        float& dynamic_learning_rate,
        float& system_novelty
    );
};
