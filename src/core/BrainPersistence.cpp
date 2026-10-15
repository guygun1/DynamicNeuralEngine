#include "core/BrainPersistence.hpp"
#include "core/BrainCodec.hpp"
#include "core/NeuralGraph.hpp"
#include "core/EmotionalState.hpp"
#include "modalities/TextLanguageModality.hpp"
#include <fstream>
#include <iostream>
#include <sstream>
#include <iterator>
#include <cstdio>
#if defined(_WIN32)
#define NOMINMAX
#include <windows.h>
#endif

constexpr uint32_t BRAIN_MAGIC_V1 = 0x41474931; // "AGI1" (Legacy character-based)
constexpr uint32_t BRAIN_MAGIC_V2 = 0x41474932; // "AGI2" (Token & Context Structural Plasticity)
constexpr uint32_t BRAIN_MAGIC_CURRENT = 0x41474937; // "AGI7": source provenance; a file-schema label only.

bool BrainPersistence::SaveBrainState(
    const std::string& filepath,
    const NeuralGraph& graph,
    const TextLanguageModality& textModality,
    const EmotionalState& emotions,
    float dynamic_learning_rate,
    float system_novelty
) {
    std::ostringstream out(std::ios::out | std::ios::binary);

    out.write(reinterpret_cast<const char*>(&BRAIN_MAGIC_CURRENT), sizeof(BRAIN_MAGIC_CURRENT));

    // Vocabulary
    const auto& vocabulary = textModality.GetVocabulary();
    uint32_t vocab_size = static_cast<uint32_t>(vocabulary.size());
    out.write(reinterpret_cast<const char*>(&vocab_size), sizeof(vocab_size));
    for (const auto& token : vocabulary) {
        uint32_t len = static_cast<uint32_t>(token.length());
        out.write(reinterpret_cast<const char*>(&len), sizeof(len));
        out.write(token.data(), len);
    }

    // Transient Engine State
    int last_token_idx = textModality.GetLastTokenIdx();
    out.write(reinterpret_cast<const char*>(&last_token_idx), sizeof(last_token_idx));
    out.write(reinterpret_cast<const char*>(&dynamic_learning_rate), sizeof(dynamic_learning_rate));
    out.write(reinterpret_cast<const char*>(&system_novelty), sizeof(system_novelty));
    out.write(reinterpret_cast<const char*>(&emotions.real_world_chances), sizeof(emotions.real_world_chances));

    // Prompt to Starter Map
    const auto& prompt_to_starter = textModality.GetPromptToStarter();
    uint32_t starter_keys_count = static_cast<uint32_t>(prompt_to_starter.size());
    out.write(reinterpret_cast<const char*>(&starter_keys_count), sizeof(starter_keys_count));
    for (const auto& [pid, inner_map] : prompt_to_starter) {
        out.write(reinterpret_cast<const char*>(&pid), sizeof(pid));
        uint32_t inner_count = static_cast<uint32_t>(inner_map.size());
        out.write(reinterpret_cast<const char*>(&inner_count), sizeof(inner_count));
        for (const auto& [sid, score] : inner_map) {
            out.write(reinterpret_cast<const char*>(&sid), sizeof(sid));
            out.write(reinterpret_cast<const char*>(&score), sizeof(score));
        }
    }

    // Neurons & Synaptic Graph
    const auto& neurons = graph.GetAllNeurons();
    uint32_t total_neurons = static_cast<uint32_t>(neurons.size());
    out.write(reinterpret_cast<const char*>(&total_neurons), sizeof(total_neurons));

    for (const auto& neuron : neurons) {
        out.write(reinterpret_cast<const char*>(&neuron.id), sizeof(neuron.id));
        out.write(reinterpret_cast<const char*>(&neuron.bias), sizeof(neuron.bias));
        out.write(reinterpret_cast<const char*>(&neuron.activation), sizeof(neuron.activation));

        uint32_t total_synapses = static_cast<uint32_t>(neuron.outgoing_synapses.size());
        out.write(reinterpret_cast<const char*>(&total_synapses), sizeof(total_synapses));

        for (const auto& syn : neuron.outgoing_synapses) {
            out.write(reinterpret_cast<const char*>(&syn.target_neuron_id), sizeof(syn.target_neuron_id));
            out.write(reinterpret_cast<const char*>(&syn.weight), sizeof(syn.weight));
            out.write(reinterpret_cast<const char*>(&syn.age_ticks), sizeof(syn.age_ticks));
        }
    }

    const auto& examples = textModality.GetDialogueExamples();
    uint32_t example_count = static_cast<uint32_t>(examples.size());
    out.write(reinterpret_cast<const char*>(&example_count), sizeof(example_count));
    for (const auto& example : examples) {
        for (const auto* sequence : {&example.prompt, &example.response}) {
            uint32_t count = static_cast<uint32_t>(sequence->size());
            out.write(reinterpret_cast<const char*>(&count), sizeof(count));
            for (int id : *sequence) out.write(reinterpret_cast<const char*>(&id), sizeof(id));
        }
        uint32_t length = static_cast<uint32_t>(example.text.size());
        out.write(reinterpret_cast<const char*>(&length), sizeof(length));
        out.write(example.text.data(), length);
    }
    const auto& fingerprint = textModality.GetCurriculumFingerprint();
    uint32_t fingerprint_length = static_cast<uint32_t>(fingerprint.size());
    out.write(reinterpret_cast<const char*>(&fingerprint_length), sizeof(fingerprint_length));
    out.write(fingerprint.data(), fingerprint_length);
    const auto& topic = textModality.GetConversationTopic();
    uint32_t topic_length = static_cast<uint32_t>(topic.size());
    out.write(reinterpret_cast<const char*>(&topic_length), sizeof(topic_length));
    out.write(topic.data(), topic_length);
    const auto& facts = textModality.GetConceptFacts();
    uint32_t fact_count = static_cast<uint32_t>(facts.size());
    out.write(reinterpret_cast<const char*>(&fact_count), sizeof(fact_count));
    for (const auto& fact : facts) {
        int relationship = static_cast<int>(fact.relation);
        out.write(reinterpret_cast<const char*>(&fact.subject), sizeof(fact.subject));
        out.write(reinterpret_cast<const char*>(&relationship), sizeof(relationship));
        out.write(reinterpret_cast<const char*>(&fact.object), sizeof(fact.object));
        uint32_t source_length = static_cast<uint32_t>(fact.source.size());
        out.write(reinterpret_cast<const char*>(&source_length), sizeof(source_length));
        out.write(fact.source.data(), source_length);
    }
    if (!out) return false;
    std::string encoded = BrainCodec::Encode(out.str());
    std::string temporary = filepath + ".writing";
    std::ofstream file(temporary, std::ios::binary | std::ios::trunc);
    file.write(encoded.data(), static_cast<std::streamsize>(encoded.size()));
    file.close();
    if (!file) { std::remove(temporary.c_str()); return false; }
#if defined(_WIN32)
    bool replaced = MoveFileExA(temporary.c_str(), filepath.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != 0;
#else
    bool replaced = std::rename(temporary.c_str(), filepath.c_str()) == 0;
#endif
    if (!replaced) std::remove(temporary.c_str());
    return replaced;
}

bool BrainPersistence::LoadBrainState(
    const std::string& filepath,
    NeuralGraph& graph,
    TextLanguageModality& textModality,
    EmotionalState& emotions,
    float& dynamic_learning_rate,
    float& system_novelty
) {
    std::ifstream file(filepath, std::ios::binary);
    if (!file.is_open()) return false;
    std::string encoded((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
    std::string decoded;
    if (!BrainCodec::Decode(encoded, decoded)) return false;
    std::istringstream in(decoded, std::ios::in | std::ios::binary);

    uint32_t magic = 0;
    in.read(reinterpret_cast<char*>(&magic), sizeof(magic));

    int version = magic >= BRAIN_MAGIC_V2 && magic <= BRAIN_MAGIC_CURRENT ? static_cast<int>(magic - BRAIN_MAGIC_V1 + 1) : 0;
    if (version >= 2) {
        uint32_t vocab_size = 0;
        in.read(reinterpret_cast<char*>(&vocab_size), sizeof(vocab_size));
        std::vector<std::string> vocab;
        vocab.reserve(vocab_size);

        for (uint32_t v = 0; v < vocab_size; ++v) {
            uint32_t len = 0;
            in.read(reinterpret_cast<char*>(&len), sizeof(len));
            std::string tok(len, '\0');
            in.read(&tok[0], len);
            vocab.push_back(tok);
        }
        textModality.LoadVocabulary(vocab);

        int last_token_idx = -1;
        in.read(reinterpret_cast<char*>(&last_token_idx), sizeof(last_token_idx));
        textModality.SetLastTokenIdx(last_token_idx);

        in.read(reinterpret_cast<char*>(&dynamic_learning_rate), sizeof(dynamic_learning_rate));
        in.read(reinterpret_cast<char*>(&system_novelty), sizeof(system_novelty));
        in.read(reinterpret_cast<char*>(&emotions.real_world_chances), sizeof(emotions.real_world_chances));

        // Prompt to Starter Map
        std::unordered_map<int, std::unordered_map<int, float>> starter_map;
        uint32_t starter_keys_count = 0;
        in.read(reinterpret_cast<char*>(&starter_keys_count), sizeof(starter_keys_count));
        for (uint32_t k = 0; k < starter_keys_count; ++k) {
            int pid = 0;
            in.read(reinterpret_cast<char*>(&pid), sizeof(pid));
            uint32_t inner_count = 0;
            in.read(reinterpret_cast<char*>(&inner_count), sizeof(inner_count));
            for (uint32_t ik = 0; ik < inner_count; ++ik) {
                int sid = 0;
                float score = 0.0f;
                in.read(reinterpret_cast<char*>(&sid), sizeof(sid));
                in.read(reinterpret_cast<char*>(&score), sizeof(score));
                starter_map[pid][sid] = score;
            }
        }
        textModality.SetPromptToStarter(starter_map);

        uint32_t total_neurons = 0;
        in.read(reinterpret_cast<char*>(&total_neurons), sizeof(total_neurons));
        graph.Clear();
        graph.Resize(total_neurons);

        auto& neurons = graph.GetAllNeurons();
        for (uint32_t i = 0; i < total_neurons; ++i) {
            in.read(reinterpret_cast<char*>(&neurons[i].id), sizeof(neurons[i].id));
            in.read(reinterpret_cast<char*>(&neurons[i].bias), sizeof(neurons[i].bias));
            in.read(reinterpret_cast<char*>(&neurons[i].activation), sizeof(neurons[i].activation));

            uint32_t total_synapses = 0;
            in.read(reinterpret_cast<char*>(&total_synapses), sizeof(total_synapses));
            neurons[i].outgoing_synapses.resize(total_synapses);

            for (uint32_t j = 0; j < total_synapses; ++j) {
                in.read(reinterpret_cast<char*>(&neurons[i].outgoing_synapses[j].target_neuron_id), sizeof(int));
                in.read(reinterpret_cast<char*>(&neurons[i].outgoing_synapses[j].weight), sizeof(float));
                in.read(reinterpret_cast<char*>(&neurons[i].outgoing_synapses[j].age_ticks), sizeof(uint32_t));
            }
        }
        if (!in) return false;
        std::vector<DialogueExample> examples;
        if (version >= 3) {
            uint32_t example_count = 0;
            in.read(reinterpret_cast<char*>(&example_count), sizeof(example_count));
            if (!in || example_count > 1000000) return false;
            examples.reserve(example_count);
            for (uint32_t e = 0; e < example_count; ++e) {
                DialogueExample example;
                for (auto* sequence : {&example.prompt, &example.response}) {
                    uint32_t count = 0;
                    in.read(reinterpret_cast<char*>(&count), sizeof(count));
                    if (!in || count == 0 || count > 100000) return false;
                    sequence->resize(count);
                    for (int& id : *sequence) {
                        in.read(reinterpret_cast<char*>(&id), sizeof(id));
                        if (!in || id < 0 || id >= static_cast<int>(vocab.size())) return false;
                    }
                }
                if (version >= 4) {
                    uint32_t length = 0;
                    in.read(reinterpret_cast<char*>(&length), sizeof(length));
                    if (!in || length > 1048576) return false;
                    example.text.resize(length);
                    if (length) in.read(example.text.data(), length);
                    if (!in) return false;
                }
                examples.push_back(std::move(example));
            }
        }
        textModality.SetDialogueExamples(examples);
        std::string fingerprint;
        if (version >= 4) {
            uint32_t length = 0;
            in.read(reinterpret_cast<char*>(&length), sizeof(length));
            if (!in || length > 1024) return false;
            fingerprint.resize(length);
            if (length) in.read(fingerprint.data(), length);
            if (!in) return false;
        }
        textModality.SetCurriculumFingerprint(fingerprint);
        std::string topic;
        if (version >= 5) {
            uint32_t length = 0;
            in.read(reinterpret_cast<char*>(&length), sizeof(length));
            if (!in || length > 512) return false;
            topic.resize(length);
            if (length) in.read(topic.data(), length);
            if (!in) return false;
        }
        textModality.SetConversationTopic(topic);
        std::vector<ConceptFact> facts;
        if (version >= 6) {
            uint32_t count = 0;
            in.read(reinterpret_cast<char*>(&count), sizeof(count));
            if (!in || count > 1000000) return false;
            for (uint32_t i = 0; i < count; ++i) {
                int subject, relation, object;
                in.read(reinterpret_cast<char*>(&subject), sizeof(subject));
                in.read(reinterpret_cast<char*>(&relation), sizeof(relation));
                in.read(reinterpret_cast<char*>(&object), sizeof(object));
                if (!in || subject < 0 || object < 0 || subject >= static_cast<int>(vocab.size()) || object >= static_cast<int>(vocab.size()) ||
                    vocab[subject].empty() || vocab[object].empty() || relation < 0 || relation > 3) return false;
                std::string source;
                if (version >= 7) {
                    uint32_t length = 0;
                    in.read(reinterpret_cast<char*>(&length), sizeof(length));
                    if (!in || length > 4096) return false;
                    source.resize(length);
                    if (length) in.read(source.data(), length);
                    if (!in) return false;
                }
                facts.push_back({subject, static_cast<ConceptRelation>(relation), object, source});
            }
        }
        textModality.SetConceptFacts(facts);
        return true;
    } else if (magic == BRAIN_MAGIC_V1) {
        textModality.SetDialogueExamples({});
        textModality.SetCurriculumFingerprint("");
        textModality.SetConversationTopic("");
        textModality.SetConceptFacts({});
        uint32_t alphabet_len = 0;
        in.read(reinterpret_cast<char*>(&alphabet_len), sizeof(alphabet_len));
        std::string alphabet(alphabet_len, '\0');
        in.read(&alphabet[0], alphabet_len);

        int last_char_idx = -1;
        in.read(reinterpret_cast<char*>(&last_char_idx), sizeof(last_char_idx));
        in.read(reinterpret_cast<char*>(&dynamic_learning_rate), sizeof(dynamic_learning_rate));
        in.read(reinterpret_cast<char*>(&system_novelty), sizeof(system_novelty));
        in.read(reinterpret_cast<char*>(&emotions.real_world_chances), sizeof(emotions.real_world_chances));

        uint32_t total_neurons = 0;
        in.read(reinterpret_cast<char*>(&total_neurons), sizeof(total_neurons));
        graph.Clear();
        graph.Resize(total_neurons);

        auto& neurons = graph.GetAllNeurons();
        for (uint32_t i = 0; i < total_neurons; ++i) {
            in.read(reinterpret_cast<char*>(&neurons[i].id), sizeof(neurons[i].id));
            in.read(reinterpret_cast<char*>(&neurons[i].bias), sizeof(neurons[i].bias));
            in.read(reinterpret_cast<char*>(&neurons[i].activation), sizeof(neurons[i].activation));

            uint32_t total_synapses = 0;
            in.read(reinterpret_cast<char*>(&total_synapses), sizeof(total_synapses));
            neurons[i].outgoing_synapses.resize(total_synapses);

            for (uint32_t j = 0; j < total_synapses; ++j) {
                in.read(reinterpret_cast<char*>(&neurons[i].outgoing_synapses[j].target_neuron_id), sizeof(int));
                in.read(reinterpret_cast<char*>(&neurons[i].outgoing_synapses[j].weight), sizeof(float));
                in.read(reinterpret_cast<char*>(&neurons[i].outgoing_synapses[j].age_ticks), sizeof(uint32_t));
            }
        }
        return true;
    }

    std::cerr << "[BrainPersistence] Invalid brain state magic identifier.\n";
    return false;
}
