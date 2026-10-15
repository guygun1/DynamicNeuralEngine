#pragma once

#include <vector>
#include <string>
#include <cstddef>

// Context-specific teaching episodes, expressed as vocabulary neuron IDs.
// Responses are learned sequences; the memory contains no built-in English replies.
struct DialogueExample {
    std::vector<int> prompt;
    std::vector<int> response;
    std::string text;
};

class DialogueMemory {
public:
    void Learn(const std::vector<int>& prompt, const std::vector<int>& response, const std::string& text = "");
    const std::vector<int>* Recall(const std::vector<int>& prompt, int unknown_id = -1, int boundary_id = -1) const;
    const std::vector<int>* Uncertainty(int unknown_id) const;
    const std::string* Text(const std::vector<int>* response) const;
    bool IsUncertainty(const std::vector<int>* response, int unknown_id) const;
    void AttachSource(const std::vector<int>& prompt, const std::string& source);
    const DialogueExample* Sourced(const std::vector<int>& prompt) const;
    std::vector<const DialogueExample*> Related(const std::vector<int>& topic, int unknown_id, size_t limit) const;
    std::vector<int> Propose(const std::vector<int>& prompt, unsigned int seed, int max_tokens) const;
    bool Empty() const { return examples_.empty(); }
    const std::vector<DialogueExample>& Examples() const { return examples_; }
    void Load(const std::vector<DialogueExample>& examples) { examples_ = examples; }

private:
    std::vector<DialogueExample> examples_;
};
