#pragma once

#include "IModalityAdapter.hpp"
#include "core/NeuralGraph.hpp"
#include "DialogueMemory.hpp"
#include "ConceptMemory.hpp"
#include <vector>
#include <string>
#include <unordered_map>

/// <summary>
/// Natural Language Modality Adapter.
/// Maps English tokens, sentence semantics, and dialogue associations to the generic NeuralGraph.
/// </summary>
class TextLanguageModality : public IModalityAdapter {
public:
    TextLanguageModality();
    ~TextLanguageModality() override = default;

    const char* GetModalityName() const override { return "TextLanguage"; }
    ModalityType GetModalityType() const override { return ModalityType::TextLanguage; }

    void Initialize(NeuralGraph& graph) override;

    // Token & Vocabulary Management
    int GetOrCreateTokenNeuron(NeuralGraph& graph, const std::string& token);
    std::vector<std::string> Tokenize(const std::string& text) const;
    int GetVocabularyCount() const { return static_cast<int>(token_to_id_.size()); }
    int FindWordNeuron(const std::string& word) const;
    const std::vector<std::string>& GetVocabulary() const { return vocabulary_; }

    // Core Language Training
    void TrainDialogue(NeuralGraph& graph, const std::string& prompt, const std::string& response);
    void TrainFromSource(NeuralGraph& graph, const std::string& prompt, const std::string& passage, const std::string& source);
    void TrainSentence(NeuralGraph& graph, const std::string& sentence);
    void StepToken(NeuralGraph& graph, const std::string& token, bool novelState);
    void StepChar(NeuralGraph& graph, char c, bool novelState);

    // Response Generation & Conversational Plasticity
    int GenerateCompletion(NeuralGraph& graph, const std::string& prompt, char* buffer, int buffer_size, int max_chars);
    int Propose(const std::string& prompt, char* buffer, int buffer_size, int max_chars, unsigned int seed) const;
    int ProcessLiveDialogue(NeuralGraph& graph, const std::string& input, char* buffer, int buffer_size, int max_chars = 8192);
    void AdaptIdentity(NeuralGraph& graph, const std::string& new_name);

    // State Serialization Helpers
    const std::unordered_map<int, std::unordered_map<int, float>>& GetPromptToStarter() const { return prompt_to_starter_; }
    void SetPromptToStarter(const std::unordered_map<int, std::unordered_map<int, float>>& map) { prompt_to_starter_ = map; }
    void LoadVocabulary(const std::vector<std::string>& vocab);
    int GetLastTokenIdx() const { return last_token_neuron_idx_; }
    void SetLastTokenIdx(int idx) { last_token_neuron_idx_ = idx; }
    float GetLearningRate() const { return dynamic_learning_rate_; }
    void SetLearningRate(float lr) { dynamic_learning_rate_ = lr; }
    const std::vector<DialogueExample>& GetDialogueExamples() const { return dialogue_memory_.Examples(); }
    void SetDialogueExamples(const std::vector<DialogueExample>& examples) { dialogue_memory_.Load(examples); }
    const std::string& GetCurriculumFingerprint() const { return curriculum_fingerprint_; }
    void SetCurriculumFingerprint(const std::string& value) { curriculum_fingerprint_ = value; }
    const std::string& GetConversationTopic() const { return conversation_topic_; }
    void SetConversationTopic(const std::string& value, const NeuralGraph* graph = nullptr);
    const std::vector<ConceptFact>& GetConceptFacts() const { return concept_memory_.Facts(); }
    void SetConceptFacts(const std::vector<ConceptFact>& facts) { concept_memory_.Load(facts); }
    int GetAnswerState() const { return answer_state_; } // 1=supported, 2=needs knowledge, 3=invalid/missing context.

private:
    std::vector<std::string> vocabulary_;
    std::unordered_map<std::string, int> token_to_id_;
    std::unordered_map<int, std::unordered_map<int, float>> prompt_to_starter_;
    DialogueMemory dialogue_memory_;
    std::string curriculum_fingerprint_;
    std::string conversation_topic_;
    ConceptMemory concept_memory_;
    int answer_state_ = 0;
    bool ObserveConcept(NeuralGraph& graph, const std::string& text, const std::string& source = "");
    std::vector<std::string> RecoverInput(const std::vector<std::string>& tokens, const NeuralGraph* graph = nullptr) const;

    std::vector<int> PromptIds(const std::vector<std::string>& tokens) const;
    int WriteTokens(const std::vector<int>& tokens, char* buffer, int buffer_size, int max_chars) const;
    int WriteText(const std::string& text, char* buffer, int buffer_size, int max_chars) const;

    std::string char_accumulator_;
    int last_token_neuron_idx_ = -1;
    float dynamic_learning_rate_ = 0.20f;
};
