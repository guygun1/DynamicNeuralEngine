#include "modalities/TextLanguageModality.hpp"
#include "modalities/ArithmeticReasoner.hpp"
#include "modalities/LanguageRequests.hpp"
#include <algorithm>
#include <cctype>
#include <cstring>

TextLanguageModality::TextLanguageModality() {
    std::string base_tokens[] = {
        ".", "?", "!", ",", "hello", "hi", "i", "am", "you", "are", 
        "what", "is", "your", "name", "agi", "core", "yes", "no"
    };
    for (const auto& token : base_tokens) {
        vocabulary_.push_back(token);
        token_to_id_[token] = static_cast<int>(vocabulary_.size() - 1);
    }
}

void TextLanguageModality::Initialize(NeuralGraph& graph) {
    graph.EnsureCapacity(vocabulary_.size());
}

int TextLanguageModality::GetOrCreateTokenNeuron(NeuralGraph& graph, const std::string& token) {
    auto it = token_to_id_.find(token);
    if (it != token_to_id_.end()) {
        return it->second;
    }

    int new_id = graph.SpawnNeuron();
    // Explicit core/sensory neurons can precede new language neurons. Keep the
    // vocabulary indexed by real graph IDs rather than assuming identical counts.
    if (vocabulary_.size() <= static_cast<size_t>(new_id)) vocabulary_.resize(new_id + 1);
    vocabulary_[new_id] = token;
    token_to_id_[token] = new_id;
    return new_id;
}

std::vector<std::string> TextLanguageModality::Tokenize(const std::string& text) const {
    std::vector<std::string> tokens;
    std::string current;

    for (size_t i = 0; i < text.length(); ++i) {
        // A conventional out-of-vocabulary feature can be taught in lesson prompts.
        // It carries no built-in response or semantic label.
        if (text.compare(i, 5, "<unk>") == 0) {
            if (!current.empty()) {
                tokens.push_back(current);
                current.clear();
            }
            tokens.push_back("<unk>");
            i += 4;
            continue;
        }
        char c = static_cast<char>(tolower(static_cast<unsigned char>(text[i])));
        if (c == '?' || c == '.' || c == '!' || c == ',' || c == ':' || c == ';') {
            if (!current.empty()) {
                tokens.push_back(current);
                current.clear();
            }
            tokens.push_back(std::string(1, c));
        } else if (isspace(static_cast<unsigned char>(c)) || c == '-' || c == '/') {
            if (!current.empty()) {
                tokens.push_back(current);
                current.clear();
            }
        } else if (isalnum(static_cast<unsigned char>(c)) || c == '\'' || (c == '#' && current == "c")) {
            current += c;
        } else if (!current.empty()) {
            tokens.push_back(current);
            current.clear();
        }
    }
    if (!current.empty()) {
        tokens.push_back(current);
    }
    return tokens;
}

void TextLanguageModality::StepToken(NeuralGraph& graph, const std::string& token, bool novelState) {
    // Empty/noise strings have no representation. Existing words reuse their IDs;
    // a novelty flag never forces duplicate word neurons.
    for (const auto& lower : Tokenize(token)) {
        int current_idx = GetOrCreateTokenNeuron(graph, lower);
        if (last_token_neuron_idx_ >= 0 && last_token_neuron_idx_ < graph.GetNeuronCount()) {
            graph.AddSynapse(last_token_neuron_idx_, current_idx, dynamic_learning_rate_);
        }
        graph.GetNeuron(current_idx).activation = 1.0f;
        last_token_neuron_idx_ = current_idx;
    }
}

int TextLanguageModality::FindWordNeuron(const std::string& word) const {
    auto tokens = Tokenize(word);
    auto it = token_to_id_.find(LanguageRequests::Join(tokens));
    return it == token_to_id_.end() ? -1 : it->second;
}

void TextLanguageModality::StepChar(NeuralGraph& graph, char c, bool novelState) {
    if (c == ' ' || c == '?' || c == '.' || c == '!' || c == '\n') {
        if (!char_accumulator_.empty()) {
            StepToken(graph, char_accumulator_, novelState);
            char_accumulator_.clear();
        }
        if (c != ' ' && c != '\n') {
            StepToken(graph, std::string(1, c), novelState);
        }
    } else {
        char_accumulator_ += c;
    }
}

void TextLanguageModality::TrainDialogue(NeuralGraph& graph, const std::string& prompt, const std::string& response) {
    auto p_tokens = Tokenize(prompt);
    auto r_tokens = Tokenize(response);
    if (p_tokens.empty() || r_tokens.empty()) return;

    std::vector<int> p_ids;
    for (const auto& t : p_tokens) p_ids.push_back(GetOrCreateTokenNeuron(graph, t));

    std::vector<int> r_ids;
    for (const auto& t : r_tokens) r_ids.push_back(GetOrCreateTokenNeuron(graph, t));

    dialogue_memory_.Learn(PromptIds(p_tokens), r_ids, response);

    int r_front = r_ids.front();

    // 1. Reinforced sequential transitions within response chain
    for (size_t i = 0; i + 1 < r_ids.size(); ++i) {
        graph.AddSynapse(r_ids[i], r_ids[i + 1], dynamic_learning_rate_ * 4.5f);
    }

    // 2. Map prompt tokens to the response initiator and context
    for (int pid : p_ids) {
        if (pid < 0 || pid >= static_cast<int>(vocabulary_.size())) continue;
        const std::string& word = vocabulary_[pid];
        if (word == "?" || word == "." || word == "!" || word == ",") continue;

        float salience = 1.0f;
        if (word == "is" || word == "are" || word == "you" || word == "the" || word == "what") {
            salience = 0.6f;
        } else {
            salience = 3.5f; // Highly salient topic words
        }

        // Direct response starter mapping
        prompt_to_starter_[pid][r_front] += 2.5f * salience;

        // General associative co-occurrence to response concepts (skip punctuation)
        for (size_t ri = 0; ri + 1 < r_ids.size(); ++ri) {
            const std::string& r_word = vocabulary_[r_ids[ri]];
            if (r_word == "." || r_word == "!" || r_word == "?" || r_word == ",") continue;
            graph.AddSynapse(pid, r_ids[ri], dynamic_learning_rate_ * 1.5f * salience);
        }
    }
}

void TextLanguageModality::TrainSentence(NeuralGraph& graph, const std::string& sentence) {
    ObserveConcept(graph, sentence);
    size_t qpos = sentence.find('?');
    if (qpos != std::string::npos && qpos + 1 < sentence.length()) {
        std::string p = sentence.substr(0, qpos + 1);
        std::string r = sentence.substr(qpos + 1);
        TrainDialogue(graph, p, r);
        return;
    }

    auto tokens = Tokenize(sentence);
    if (tokens.size() < 2) return;

    std::vector<int> ids;
    for (const auto& t : tokens) ids.push_back(GetOrCreateTokenNeuron(graph, t));

    for (size_t i = 0; i + 1 < ids.size(); ++i) {
        graph.AddSynapse(ids[i], ids[i + 1], dynamic_learning_rate_ * 2.0f);
    }
}

void TextLanguageModality::TrainFromSource(NeuralGraph& graph, const std::string& prompt, const std::string& passage, const std::string& source) {
    // Learn content words before normalizing the query. Citation URLs are display
    // metadata only, so each URL does not sprout arbitrary vocabulary neurons.
    for (const auto& token : Tokenize(passage)) GetOrCreateTokenNeuron(graph, token);
    auto normalized = RecoverInput(Tokenize(prompt), &graph);
    TrainDialogue(graph, LanguageRequests::Join(normalized), passage);
    dialogue_memory_.AttachSource(PromptIds(normalized), source);
    // Only the controlled grammar becomes typed premises. General prose remains
    // attributed reference material rather than an invented logical assertion.
    size_t begin = 0;
    for (size_t i = 0; i <= passage.size(); ++i) {
        if (i != passage.size() && !(passage[i] == '.' && (i + 1 == passage.size() || std::isspace(static_cast<unsigned char>(passage[i + 1]))))) continue;
        ObserveConcept(graph, passage.substr(begin, i - begin), source.substr(0, 2048));
        begin = i + 1;
    }
}

bool TextLanguageModality::ObserveConcept(NeuralGraph& graph, const std::string& text, const std::string& source) {
    auto fact = ConceptMemory::ParseStatement(text);
    if (!fact) return false;
    for (const auto& token : Tokenize(text)) GetOrCreateTokenNeuron(graph, token);
    int subject = GetOrCreateTokenNeuron(graph, fact->subject);
    int object = GetOrCreateTokenNeuron(graph, fact->object);
    concept_memory_.Learn({subject, fact->relation, object, source});
    graph.AddSynapse(subject, object, dynamic_learning_rate_);
    return true;
}

std::vector<int> TextLanguageModality::PromptIds(const std::vector<std::string>& tokens) const {
    std::vector<int> ids;
    auto unknown = token_to_id_.find("<unk>");
    for (size_t index = 0; index < tokens.size(); ++index) {
        const auto& token = tokens[index];
        bool sentence_boundary = token == "." && index + 1 < tokens.size();
        if (!sentence_boundary && token.size() == 1 && std::ispunct(static_cast<unsigned char>(token[0]))) continue;
        auto it = token_to_id_.find(token);
        ids.push_back(it != token_to_id_.end() ? it->second :
            (unknown != token_to_id_.end() ? unknown->second : -1));
    }
    return ids;
}

int TextLanguageModality::WriteTokens(const std::vector<int>& tokens, char* buffer, int buffer_size, int max_chars) const {
    std::string output;
    const size_t limit = static_cast<size_t>(std::min(max_chars, buffer_size - 1));
    for (int id : tokens) {
        if (id < 0 || id >= static_cast<int>(vocabulary_.size())) continue;
        const auto& token = vocabulary_[id];
        bool punctuation = token.size() == 1 && std::ispunct(static_cast<unsigned char>(token[0]));
        size_t spacing = !output.empty() && !punctuation ? 1 : 0;
        if (output.size() + spacing + token.size() > limit) break;
        if (spacing) output += ' ';
        output += token;
    }
    if (!output.empty()) output[0] = static_cast<char>(std::toupper(static_cast<unsigned char>(output[0])));
    std::memcpy(buffer, output.c_str(), output.size() + 1);
    return static_cast<int>(output.size());
}

int TextLanguageModality::WriteText(const std::string& text, char* buffer, int buffer_size, int max_chars) const {
    if (!buffer || buffer_size <= 0) return 0;
    size_t limit = static_cast<size_t>(std::max(0, std::min(max_chars, buffer_size - 1)));
    std::string output = text.substr(0, limit);
    if (text.size() > limit && !output.empty() && !std::isspace(static_cast<unsigned char>(text[limit]))) {
        size_t boundary = output.find_last_of(" \n\t");
        if (boundary != std::string::npos) output.resize(boundary);
    }
    while (!output.empty() && std::isspace(static_cast<unsigned char>(output.back()))) output.pop_back();
    bool sentence = true;
    bool verbatim = output.rfind("```", 0) == 0;
    for (size_t i = 0; !verbatim && i < output.size(); ++i) {
        if (output.compare(i, 8, "https://") == 0 || output.compare(i, 7, "http://") == 0) {
            while (i + 1 < output.size() && !std::isspace(static_cast<unsigned char>(output[i + 1])) && output[i + 1] != ']') ++i;
            sentence = false;
            continue;
        }
        unsigned char c = static_cast<unsigned char>(output[i]);
        if (sentence && std::isalpha(c)) { output[i] = static_cast<char>(std::toupper(c)); sentence = false; }
        if ((output[i] == '.' || output[i] == '?' || output[i] == '!') &&
            (i + 1 == output.size() || std::isspace(static_cast<unsigned char>(output[i + 1])))) sentence = true;
        if (output[i] == 'i' && (i == 0 || !std::isalnum(static_cast<unsigned char>(output[i - 1]))) &&
            (i + 1 == output.size() || !std::isalnum(static_cast<unsigned char>(output[i + 1])))) output[i] = 'I';
    }
    std::memcpy(buffer, output.c_str(), output.size() + 1);
    return static_cast<int>(output.size());
}

int TextLanguageModality::Propose(const std::string& prompt, char* buffer, int buffer_size, int max_chars, unsigned int seed) const {
    if (!buffer || buffer_size <= 0) return 0;
    buffer[0] = '\0';
    if (max_chars <= 0) return 0;
    const auto calculation = ArithmeticReasoner::Evaluate(prompt);
    if (calculation.recognized && calculation.valid)
        return WriteText(calculation.response, buffer, buffer_size, max_chars);
    auto tokens = dialogue_memory_.Propose(PromptIds(Tokenize(prompt)), seed, 40);
    return WriteTokens(tokens, buffer, buffer_size, max_chars);
}

int TextLanguageModality::GenerateCompletion(NeuralGraph& graph, const std::string& prompt, char* buffer, int buffer_size, int max_chars) {
    answer_state_ = 0;
    if (!buffer || buffer_size <= 0) return 0;
    buffer[0] = '\0';
    if (max_chars <= 0) return 0;
    const auto calculation = ArithmeticReasoner::Evaluate(prompt);
    if (calculation.recognized && calculation.valid) {
        answer_state_ = 1;
        return WriteText(calculation.response, buffer, buffer_size, max_chars);
    }
    if (!char_accumulator_.empty()) {
        StepToken(graph, char_accumulator_, false);
        char_accumulator_.clear();
    }

    auto p_tokens = RecoverInput(Tokenize(prompt), &graph);
    if (p_tokens.empty()) {
        if (buffer && buffer_size > 0) buffer[0] = '\0';
        return 0;
    }
    answer_state_ = calculation.recognized ? 3 : 2;
    auto concept_answer = concept_memory_.Answer(LanguageRequests::Join(p_tokens), vocabulary_);
    if (concept_answer.proved) { answer_state_ = 1; return WriteText(concept_answer.text, buffer, buffer_size, max_chars); }

    if (!dialogue_memory_.Empty()) {
        if (concept_answer.recognized && !calculation.recognized) {
            if (const auto* source = dialogue_memory_.Sourced(PromptIds(p_tokens))) {
                answer_state_ = 1;
                return WriteText(source->text, buffer, buffer_size, max_chars);
            }
        }
        auto unknown = token_to_id_.find("<unk>");
        int unknown_id = unknown == token_to_id_.end() ? -1 : unknown->second;
        auto request = LanguageRequests::Interpret(p_tokens);
        auto topics = LanguageRequests::SplitTopics(request.topic);
        if (!calculation.recognized && topics.size() > 1) {
            std::string combined;
            bool supported = false;
            for (const auto& topic : topics) {
                if (!combined.empty()) combined += "\n\n";
                std::string heading = LanguageRequests::Join(topic);
                if (!heading.empty()) heading[0] = static_cast<char>(std::toupper(static_cast<unsigned char>(heading[0])));
                combined += heading + "\n\n";
                auto passages = dialogue_memory_.Related(PromptIds(topic), unknown_id, 1);
                if (!passages.empty()) { combined += passages.front()->text; supported = true; }
                else if (const auto* uncertain = dialogue_memory_.Uncertainty(unknown_id)) {
                    if (const auto* text = dialogue_memory_.Text(uncertain)) combined += *text;
                }
            }
            if (supported) { answer_state_ = 1; return WriteText(combined, buffer, buffer_size, max_chars); }
        }
        if (!calculation.recognized && (request.compose || p_tokens.size() == 1)) {
            auto passages = dialogue_memory_.Related(PromptIds(request.topic), unknown_id, request.compose ? 4 : 2);
            if (!passages.empty()) {
                answer_state_ = 1;
                std::string composition;
                for (const auto* passage : passages) { if (!composition.empty()) composition += "\n\n"; composition += passage->text; }
                return WriteText(composition, buffer, buffer_size, max_chars);
            }
        }
        const auto* response = calculation.recognized || concept_answer.recognized ? dialogue_memory_.Uncertainty(unknown_id) : dialogue_memory_.Recall(PromptIds(p_tokens), unknown_id, token_to_id_.at("."));
        if (!response) return 0;
        answer_state_ = calculation.recognized || request.follow_up ? 3 : dialogue_memory_.IsUncertainty(response, unknown_id) ? 2 : 1;
        const auto* text = dialogue_memory_.Text(response);
        return text ? WriteText(*text, buffer, buffer_size, max_chars) : WriteTokens(*response, buffer, buffer_size, max_chars);
    }

    // 1. Calculate prompt context weights with salience
    std::unordered_map<int, float> context_weight;
    for (const auto& token : p_tokens) {
        auto it = token_to_id_.find(token);
        if (it != token_to_id_.end()) {
            int pid = it->second;
            float salience = 1.0f;
            if (token == "?" || token == "." || token == "!" || token == ",") continue;
            if (token == "is" || token == "are" || token == "you" || token == "the" || token == "what") {
                salience = 0.6f;
            } else {
                salience = 3.5f;
            }
            context_weight[pid] = salience;
            graph.GetNeuron(pid).activation = 1.0f;
        }
    }

    // 2. Select initial response token via prompt_to_starter and associative synapses
    int best_start_id = -1;
    float max_start_score = -1e9f;
    std::unordered_map<int, float> initiator_scores;

    for (const auto& [pid, p_wt] : context_weight) {
        auto sit = prompt_to_starter_.find(pid);
        if (sit != prompt_to_starter_.end()) {
            for (const auto& [starter_id, score] : sit->second) {
                initiator_scores[starter_id] += score * p_wt;
            }
        }

        if (pid < graph.GetNeuronCount()) {
            for (const auto& syn : graph.GetNeuron(pid).outgoing_synapses) {
                int tid = syn.target_neuron_id;
                if (tid >= 0 && tid < static_cast<int>(vocabulary_.size())) {
                    const std::string& word = vocabulary_[tid];
                    if (word != "?" && word != "." && word != "!" && word != ",") {
                        initiator_scores[tid] += syn.weight * 0.5f * p_wt;
                    }
                }
            }
        }
    }

    for (const auto& [cid, score] : initiator_scores) {
        if (score > max_start_score) {
            max_start_score = score;
            best_start_id = cid;
        }
    }

    // Legacy brains without context episodes can still traverse their old graph.
    // With no learned evidence, produce no text rather than a built-in reply.
    if (best_start_id == -1 || max_start_score < 0.2f) {
        return 0;
    }

    // 3. Step forward from best_start_id through sequential synaptic pathways
    std::vector<int> response_ids;
    response_ids.push_back(best_start_id);
    std::unordered_map<int, int> token_usage_count;
    token_usage_count[best_start_id] = 1;

    int max_tokens = std::min(max_chars / 3, 512);
    int current_id = best_start_id;

    for (int step = 1; step < max_tokens; ++step) {
        if (current_id >= 0 && current_id < static_cast<int>(vocabulary_.size())) {
            const std::string& curr_word = vocabulary_[current_id];
            if ((curr_word == "." || curr_word == "!" || curr_word == "?") && step > 1) {
                break;
            }
        }

        int next_best_id = -1;
        float max_next_score = -1e9f;

        if (current_id >= 0 && current_id < graph.GetNeuronCount()) {
            for (const auto& syn : graph.GetNeuron(current_id).outgoing_synapses) {
                int tid = syn.target_neuron_id;
                if (tid < 0 || tid >= static_cast<int>(vocabulary_.size())) continue;

                // Sequential forward connection weight
                float score = syn.weight * 3.5f;

                const std::string& target_word = vocabulary_[tid];
                bool is_punct = (target_word == "." || target_word == "!" || target_word == "?" || target_word == ",");

                if (!is_punct) {
                    for (const auto& [pid, p_wt] : context_weight) {
                        if (pid == tid) {
                            score += 3.0f * p_wt;
                        }
                        if (pid < graph.GetNeuronCount()) {
                            for (const auto& psyn : graph.GetNeuron(pid).outgoing_synapses) {
                                if (psyn.target_neuron_id == tid) {
                                    score += psyn.weight * 1.5f * p_wt;
                                }
                            }
                        }
                    }
                } else {
                    if (step < 2) {
                        score -= 25.0f;
                    } else if (step >= 3) {
                        score += 20.0f;
                    }
                }

                // Loop & repetition penalties
                if (tid == current_id) {
                    score -= 50.0f;
                }
                int usage = token_usage_count[tid];
                if (usage > 0 && target_word != "two") {
                    score -= (usage * 35.0f);
                }

                if (score > max_next_score) {
                    max_next_score = score;
                    next_best_id = tid;
                }
            }
        }

        if (next_best_id == -1 || max_next_score <= -1e8f) {
            break;
        }

        response_ids.push_back(next_best_id);
        token_usage_count[next_best_id]++;
        current_id = next_best_id;

        const std::string& next_word = vocabulary_[next_best_id];
        if (next_word == "." || next_word == "!" || next_word == "?") {
            break;
        }
    }

    // 4. Detokenize response into formatted English
    std::string output;
    for (size_t i = 0; i < response_ids.size(); ++i) {
        const std::string& token = vocabulary_[response_ids[i]];
        if (token == "." || token == "!" || token == "?" || token == "," || token == ";" || token == ":") {
            output += token;
        } else {
            if (!output.empty() && output.back() != ' ') {
                output += " ";
            }
            output += token;
        }
    }

    if (!output.empty() && output.back() != '.' && output.back() != '!' && output.back() != '?') {
        output += ".";
    }

    if (!output.empty()) {
        output[0] = static_cast<char>(toupper(static_cast<unsigned char>(output[0])));
    }

    if (buffer && buffer_size > 0) {
#if defined(_MSC_VER)
        strncpy_s(buffer, buffer_size, output.c_str(), _TRUNCATE);
#else
        strncpy(buffer, output.c_str(), buffer_size - 1);
        buffer[buffer_size - 1] = '\0';
#endif
    }

    return static_cast<int>(output.length());
}

void TextLanguageModality::AdaptIdentity(NeuralGraph& graph, const std::string& new_name) {
    std::string lower_name = new_name;
    std::transform(lower_name.begin(), lower_name.end(), lower_name.begin(), [](unsigned char c){ return std::tolower(c); });
    // Correct only the identity contexts. Suppressing every outgoing "is" or
    // "am" synapse destroys unrelated knowledge (including arithmetic).
    TrainDialogue(graph, "what is your name?", "my name is " + lower_name + ".");
    TrainDialogue(graph, "who are you?", "i am " + lower_name + ".");
    TrainDialogue(graph, "what should i call you?", "my name is " + lower_name + ".");
    TrainDialogue(graph, "tell me your name", "my name is " + lower_name + ".");
}

int TextLanguageModality::ProcessLiveDialogue(NeuralGraph& graph, const std::string& input, char* buffer, int buffer_size, int max_chars) {
    answer_state_ = 0;
    if (!buffer || buffer_size <= 0) return 0;

    auto tokens = Tokenize(input);
    if (tokens.empty()) {
        buffer[0] = '\0';
        return 0;
    }

    // Check for identity update: "known as <NAME>", "your name is <NAME>", "call you <NAME>", "you are <NAME>"
    std::string detected_new_name;

    for (size_t i = 0; i < tokens.size(); ++i) {
        if (tokens[i] == "known" && i + 2 < tokens.size() && tokens[i + 1] == "as") {
            detected_new_name = tokens[i + 2];
            break;
        }
        if (tokens[i] == "name" && i + 2 < tokens.size() && tokens[i + 1] == "is") {
            if (i > 0 && (tokens[i - 1] == "your" || tokens[i - 1] == "new")) {
                detected_new_name = tokens[i + 2];
                break;
            }
        }
        if (tokens[i] == "call" && i + 2 < tokens.size() && (tokens[i + 1] == "you" || tokens[i + 1] == "yourself")) {
            detected_new_name = tokens[i + 2];
            break;
        }
    }

    if (!detected_new_name.empty() && detected_new_name != "." && detected_new_name != "!") {
        answer_state_ = 1;
        AdaptIdentity(graph, detected_new_name);

        std::string cap_name = detected_new_name;
        cap_name[0] = static_cast<char>(toupper(static_cast<unsigned char>(cap_name[0])));

        std::string reply = "Understood. My neural pathways have adapted: I will now be known as " + cap_name + ".";
#if defined(_MSC_VER)
        strncpy_s(buffer, buffer_size, reply.c_str(), _TRUNCATE);
#else
        strncpy(buffer, reply.c_str(), buffer_size - 1);
        buffer[buffer_size - 1] = '\0';
#endif
        return static_cast<int>(reply.length());
    }

    // Check for user identity declaration: "my name is <NAME>", "i am <NAME>"
    std::string user_name;
    for (size_t i = 0; i < tokens.size(); ++i) {
        if (tokens[i] == "my" && i + 2 < tokens.size() && tokens[i + 1] == "name" && tokens[i + 2] == "is" && i + 3 < tokens.size()) {
            user_name = tokens[i + 3];
            break;
        }
    }

    if (!user_name.empty() && user_name != "." && user_name != "!") {
        answer_state_ = 1;
        std::string lower_user = user_name;
        std::transform(lower_user.begin(), lower_user.end(), lower_user.begin(), [](unsigned char c){ return std::tolower(c); });

        TrainDialogue(graph, "what is my name?", "your name is " + lower_user + ".");
        TrainDialogue(graph, "who am i?", "you are " + lower_user + ".");
        TrainDialogue(graph, "tell me my name", "your name is " + lower_user + ".");

        std::string cap_user = user_name;
        cap_user[0] = static_cast<char>(toupper(static_cast<unsigned char>(cap_user[0])));

        std::string reply = "Nice to meet you, " + cap_user + "! I have wired your identity into my neural pathways.";
#if defined(_MSC_VER)
        strncpy_s(buffer, buffer_size, reply.c_str(), _TRUNCATE);
#else
        strncpy(buffer, reply.c_str(), buffer_size - 1);
        buffer[buffer_size - 1] = '\0';
#endif
        return static_cast<int>(reply.length());
    }

    if (ConceptMemory::ParseStatement(input)) {
        answer_state_ = 1;
        TrainSentence(graph, input);
        return WriteText("Learned relation: " + input, buffer, buffer_size, max_chars);
    }
    auto request = LanguageRequests::Interpret(RecoverInput(tokens, &graph));
    if (request.follow_up && !conversation_topic_.empty())
        return GenerateCompletion(graph, "explain " + conversation_topic_, buffer, buffer_size, max_chars);
    if (!request.topic.empty()) conversation_topic_ = LanguageRequests::Join(request.topic).substr(0, 512);
    return GenerateCompletion(graph, input, buffer, buffer_size, max_chars);
}

std::vector<std::string> TextLanguageModality::RecoverInput(const std::vector<std::string>& tokens, const NeuralGraph* graph) const {
    if (!graph) return LanguageRequests::RecoverSpelling(tokens, vocabulary_);
    return LanguageRequests::RecoverSpelling(tokens, vocabulary_, [&](const std::string& from, const std::string& to) {
        return graph->GetSynapseWeight(FindWordNeuron(from), FindWordNeuron(to));
    });
}

void TextLanguageModality::SetConversationTopic(const std::string& value, const NeuralGraph* graph) {
    auto request = LanguageRequests::Interpret(RecoverInput(Tokenize(value), graph));
    conversation_topic_ = LanguageRequests::Join(request.topic).substr(0, 512);
}

void TextLanguageModality::LoadVocabulary(const std::vector<std::string>& vocab) {
    vocabulary_ = vocab;
    token_to_id_.clear();
    for (size_t i = 0; i < vocabulary_.size(); ++i) {
        if (!vocabulary_[i].empty()) token_to_id_[vocabulary_[i]] = static_cast<int>(i);
    }
}
