#include "modalities/DialogueMemory.hpp"
#include <algorithm>
#include <cmath>
#include <unordered_map>
#include <unordered_set>
#include <random>

void DialogueMemory::Learn(const std::vector<int>& prompt, const std::vector<int>& response, const std::string& text) {
    if (prompt.empty() || response.empty()) return;
    for (auto& example : examples_) {
        if (example.prompt == prompt) {
            // An explicit correction replaces this context, not every use of a word.
            example.response = response;
            example.text = text;
            return;
        }
    }
    examples_.push_back({prompt, response, text});
}

const std::string* DialogueMemory::Text(const std::vector<int>* response) const {
    for (const auto& example : examples_)
        if (&example.response == response && !example.text.empty()) return &example.text;
    return nullptr;
}

bool DialogueMemory::IsUncertainty(const std::vector<int>* response, int unknown_id) const {
    for (const auto& example : examples_)
        if (&example.response == response) return unknown_id >= 0 && std::find(example.prompt.begin(), example.prompt.end(), unknown_id) != example.prompt.end();
    return false;
}

void DialogueMemory::AttachSource(const std::vector<int>& prompt, const std::string& source) {
    for (auto& example : examples_) if (example.prompt == prompt) {
        example.text = "Learned source material:\n\n" + example.text + "\n\nSource: " + source.substr(0, 2048);
        return;
    }
}

const DialogueExample* DialogueMemory::Sourced(const std::vector<int>& prompt) const {
    for (const auto& example : examples_)
        if (example.prompt == prompt && example.text.find("\n\nSource: ") != std::string::npos) return &example;
    return nullptr;
}

const std::vector<int>* DialogueMemory::Uncertainty(int unknown_id) const {
    if (unknown_id < 0) return nullptr;
    for (const auto& example : examples_)
        if (example.prompt == std::vector<int>{unknown_id}) return &example.response;
    for (const auto& example : examples_)
        if (std::find(example.prompt.begin(), example.prompt.end(), unknown_id) != example.prompt.end()) return &example.response;
    return nullptr;
}

const std::vector<int>* DialogueMemory::Recall(const std::vector<int>& prompt, int unknown_id, int boundary_id) const {
    if (prompt.empty()) return nullptr;
    std::unordered_map<int, int> document_frequency;
    std::unordered_set<int> scaffolding;
    for (const auto& example : examples_) {
        if (example.prompt == prompt) return &example.response;
        std::unordered_set<int> unique(example.prompt.begin(), example.prompt.end());
        for (int token : unique) document_frequency[token]++;
        if (unknown_id >= 0 && unique.count(unknown_id))
            for (int token : unique) if (token != unknown_id) scaffolding.insert(token);
    }

    // Corpus-derived inverse document frequency downweights common question words.
    auto weight = [&](int token) {
        return 1.0 + std::log((examples_.size() + 1.0) / (document_frequency[token] + 1.0));
    };
    auto features = [&](const std::vector<int>& tokens) {
        std::unordered_map<int, int> counts;
        for (int token : tokens) counts[token]++;
        return counts;
    };
    const auto query = features(prompt);
    bool novel = std::any_of(prompt.begin(), prompt.end(), [&](int id) { return id < 0 || id == unknown_id; });
    bool hasWildcard = unknown_id >= 0 && Uncertainty(unknown_id);
    // The language of abstention is learned. This gate concerns evidence, not wording.
    // Unfamiliar features must not be silently dropped while matching a known fact.
    if (novel && !hasWildcard) return nullptr;
    if (novel) return Uncertainty(unknown_id);
    for (const auto& [token, frequency] : document_frequency)
        if (frequency > static_cast<double>(examples_.size()) * 0.12) scaffolding.insert(token);
    double query_weight = 0.0;
    for (const auto& [token, count] : query) query_weight += weight(token) * count;

    const std::vector<int>* response = nullptr;
    double best_score = 0.0;
    double runner_up = 0.0;
    for (const auto& example : examples_) {
        bool wildcard = unknown_id >= 0 && std::find(example.prompt.begin(), example.prompt.end(), unknown_id) != example.prompt.end();
        if (wildcard != novel) continue;
        const auto candidate = features(example.prompt);
        if (!novel) {
            double topical = 0.0, supported = 0.0;
            for (const auto& [token, count] : query) {
                if (scaffolding.count(token)) continue;
                topical += weight(token);
                if (candidate.count(token)) supported += weight(token);
            }
            if (topical > 0.0 && supported / topical < 0.8) continue;
        }
        double candidate_weight = 0.0;
        double common_weight = 0.0;
        for (const auto& [token, count] : candidate) {
            candidate_weight += weight(token) * count;
            auto match = query.find(token);
            if (match != query.end()) common_weight += weight(token) * std::min(count, match->second);
        }
        // Include adjacent pairs so reversing subject/object or arithmetic operands
        // is not treated as an identical bag of words.
        double query_pairs = 0.0;
        double candidate_pairs = 0.0;
        double common_pairs = 0.0;
        std::vector<bool> used(example.prompt.size(), false);
        for (size_t i = 1; i < example.prompt.size(); ++i)
            candidate_pairs += (weight(example.prompt[i - 1]) + weight(example.prompt[i])) / 2.0;
        for (size_t i = 1; i < prompt.size(); ++i) {
            double pair_weight = (weight(prompt[i - 1]) + weight(prompt[i])) / 2.0;
            query_pairs += pair_weight;
            for (size_t j = 1; j < example.prompt.size(); ++j) {
                if (!used[j] && prompt[i - 1] == example.prompt[j - 1] && prompt[i] == example.prompt[j]) {
                    common_pairs += pair_weight;
                    used[j] = true;
                    break;
                }
            }
        }
        double score = 2.0 * (common_weight + common_pairs) /
            (query_weight + candidate_weight + query_pairs + candidate_pairs);
        // In a passage followed by a question, common passage words must not drown
        // out what is being asked (where versus what color, subject versus object).
        auto boundary = std::find(prompt.rbegin(), prompt.rend(), boundary_id);
        if (boundary_id >= 0 && boundary != prompt.rend()) {
            auto candidate_boundary = std::find(example.prompt.rbegin(), example.prompt.rend(), boundary_id);
            auto query_focus = features(std::vector<int>(boundary.base(), prompt.end()));
            auto candidate_focus = features(std::vector<int>(candidate_boundary == example.prompt.rend() ? example.prompt.begin() : candidate_boundary.base(), example.prompt.end()));
            double focus_total = 0.0, focus_common = 0.0;
            for (const auto& [token, count] : query_focus) focus_total += weight(token) * count;
            for (const auto& [token, count] : candidate_focus) {
                focus_total += weight(token) * count;
                auto match = query_focus.find(token);
                if (match != query_focus.end()) focus_common += weight(token) * std::min(count, match->second);
            }
            if (focus_total > 0.0) score = score * 0.4 + 0.6 * (2.0 * focus_common / focus_total);
        }
        if (score > best_score) {
            if (!response || *response != example.response) runner_up = best_score;
            best_score = score;
            response = &example.response;
        } else if (response && *response != example.response) {
            runner_up = std::max(runner_up, score);
        }
    }
    if (novel) return response ? response : Uncertainty(unknown_id);
    bool hasTopic = std::any_of(prompt.begin(), prompt.end(), [&](int token) { return !scaffolding.count(token); });
    if (best_score < (hasTopic ? 0.32 : 0.75) || (best_score < 0.95 && best_score - runner_up < 0.025))
        return Uncertainty(unknown_id);
    return response ? response : Uncertainty(unknown_id);
}

std::vector<const DialogueExample*> DialogueMemory::Related(const std::vector<int>& topic, int unknown_id, size_t limit) const {
    if (topic.empty() || std::any_of(topic.begin(), topic.end(), [&](int id) { return id < 0 || id == unknown_id; })) return {};
    std::vector<std::pair<double, const DialogueExample*>> ranked;
    for (const auto& example : examples_) {
        if (example.text.size() < 80 || std::find(example.prompt.begin(), example.prompt.end(), unknown_id) != example.prompt.end()) continue;
        bool supported = std::all_of(topic.begin(), topic.end(), [&](int id) { return std::find(example.prompt.begin(), example.prompt.end(), id) != example.prompt.end(); });
        if (!supported) continue;
        double score = 10.0 / (example.prompt.size() + 1.0) + std::min<size_t>(example.text.size(), 1000) / 10000.0;
        ranked.push_back({score, &example});
    }
    std::stable_sort(ranked.begin(), ranked.end(), [](const auto& a, const auto& b) { return a.first > b.first; });
    std::unordered_set<std::string> seen;
    std::vector<const DialogueExample*> result;
    for (const auto& [score, example] : ranked) {
        if (!seen.insert(example->text).second) continue;
        result.push_back(example);
        if (result.size() == limit) break;
    }
    return result;
}

std::vector<int> DialogueMemory::Propose(const std::vector<int>& prompt, unsigned int seed, int max_tokens) const {
    const auto* recalled = Recall(prompt);
    if (!recalled || recalled->empty() || max_tokens <= 0) return {};
    std::mt19937 random(seed);
    std::vector<int> proposal{recalled->front()};
    for (int step = 1; step < max_tokens; ++step) {
        // Recombine observed response transitions. Unlike ordinary recall, a proposal
        // can form a sequence that was never taught as a complete answer.
        std::vector<int> continuations;
        for (const auto& example : examples_) {
            for (size_t i = 0; i < example.response.size(); ++i) {
                if (example.response[i] != proposal.back()) continue;
                int next = i + 1 < example.response.size() ? example.response[i + 1] : -1;
                continuations.push_back(next);
                if (&example.response == recalled) {
                    continuations.push_back(next);
                    continuations.push_back(next);
                }
            }
        }
        if (continuations.empty()) break;
        // Modulo keeps seeded experiments reproducible across standard libraries.
        int next = continuations[random() % continuations.size()];
        if (next < 0) break;
        proposal.push_back(next);
    }
    return proposal;
}
