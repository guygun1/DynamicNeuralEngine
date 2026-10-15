#include "modalities/LanguageRequests.hpp"
#include <algorithm>
#include <cctype>
#include <numeric>
#include <unordered_set>
#include <cstdlib>

namespace {
int Distance(const std::string& a, const std::string& b) {
    std::vector<std::vector<int>> d(a.size() + 1, std::vector<int>(b.size() + 1));
    for (size_t i = 0; i <= a.size(); ++i) d[i][0] = static_cast<int>(i);
    for (size_t j = 0; j <= b.size(); ++j) d[0][j] = static_cast<int>(j);
    for (size_t i = 1; i <= a.size(); ++i) for (size_t j = 1; j <= b.size(); ++j) {
        d[i][j] = std::min({d[i - 1][j] + 1, d[i][j - 1] + 1, d[i - 1][j - 1] + (a[i - 1] != b[j - 1])});
        if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i][j] = std::min(d[i][j], d[i - 2][j - 2] + 1);
    }
    return d.back().back();
}
}

std::vector<std::string> LanguageRequests::RecoverSpelling(const std::vector<std::string>& tokens, const std::vector<std::string>& vocabulary,
    const std::function<float(const std::string&, const std::string&)>& connection) {
    std::unordered_set<std::string> known(vocabulary.begin(), vocabulary.end());
    auto result = tokens;
    const std::unordered_set<std::string> syntax = {"write", "compose", "draft", "explain", "elaborate", "continue", "please", "describe", "about", "essay", "article", "summary"};
    for (size_t index = 0; index < result.size(); ++index) {
        auto& token = result[index];
        if (known.count(token) || syntax.count(token) || token.size() < 5 || token.size() > 32 || !std::all_of(token.begin(), token.end(), [](unsigned char c) { return std::isalpha(c); })) continue;
        int threshold = token.size() > 7 ? 2 : 1;
        int best = threshold + 1;
        std::vector<std::string> matches;
        for (const auto& candidate : vocabulary) {
            if (candidate.size() < 4 || std::abs(static_cast<int>(candidate.size()) - static_cast<int>(token.size())) > threshold) continue;
            if (!std::all_of(candidate.begin(), candidate.end(), [](unsigned char c) { return std::isalpha(c); })) continue;
            int distance = Distance(token, candidate);
            if (distance < best) { best = distance; matches = {candidate}; }
            else if (distance == best) matches.push_back(candidate);
        }
        if (best > threshold || matches.empty()) continue;
        if (matches.size() == 1) { token = matches.front(); continue; }
        if (connection) {
            float strongest = 0, runner_up = 0;
            std::string chosen;
            for (const auto& candidate : matches) {
                float support = 0;
                if (index > 0) support += connection(result[index - 1], candidate) + 0.25f * connection(candidate, result[index - 1]);
                if (index + 1 < result.size()) support += connection(candidate, result[index + 1]) + 0.25f * connection(result[index + 1], candidate);
                if (support > strongest) { runner_up = strongest; strongest = support; chosen = candidate; }
                else runner_up = std::max(runner_up, support);
            }
            if (strongest > 0.5f && strongest - runner_up > 0.5f) token = chosen;
        }
    }
    return result;
}

std::vector<std::vector<std::string>> LanguageRequests::SplitTopics(const std::vector<std::string>& tokens) {
    auto conjunction = std::find(tokens.begin(), tokens.end(), "and");
    if (conjunction == tokens.end() || conjunction == tokens.begin() || conjunction + 1 == tokens.end()) return {tokens};
    std::vector<std::string> first(tokens.begin(), conjunction), second(conjunction + 1, tokens.end());
    auto stripArticle = [](std::vector<std::string>& words) {
        if (!words.empty() && (words.front() == "the" || words.front() == "a" || words.front() == "an")) words.erase(words.begin());
    };
    stripArticle(first); stripArticle(second);
    if (first.empty() || second.empty()) return {tokens};
    if (second.back() == "one") second.back() = first.back();
    return {first, second};
}

std::string LanguageRequests::Join(const std::vector<std::string>& tokens) {
    std::string text;
    for (const auto& token : tokens) { if (!text.empty()) text += ' '; text += token; }
    return text;
}

LanguageRequest LanguageRequests::Interpret(const std::vector<std::string>& input) {
    auto tokens = input;
    while (!tokens.empty() && (tokens.back() == "." || tokens.back() == "?" || tokens.back() == "!")) tokens.pop_back();
    LanguageRequest request{tokens};
    const std::vector<std::vector<std::string>> followups = {{"explain"}, {"elaborate"}, {"continue"}, {"tell","me","more"}, {"explain","more"}, {"go","on"}};
    if (std::find(followups.begin(), followups.end(), tokens) != followups.end()) { request.follow_up = true; request.compose = true; request.topic.clear(); return request; }
    if (tokens.empty()) return request;
    size_t begin = 0;
    if (tokens[0] == "please") begin = 1;
    if (begin < tokens.size() && (tokens[begin] == "write" || tokens[begin] == "compose" || tokens[begin] == "draft")) {
        auto about = std::find(tokens.begin() + begin, tokens.end(), "about");
        if (about == tokens.end()) about = std::find(tokens.begin() + begin, tokens.end(), "on");
        if (about != tokens.end()) { request.topic.assign(about + 1, tokens.end()); request.compose = true; return request; }
    }
    const std::vector<std::vector<std::string>> prefixes = {
        {"please","tell","me","about"}, {"tell","me","about"}, {"please","explain"}, {"explain"}, {"describe"},
        {"what","is","a"}, {"what","is","an"}, {"what","is","the"}, {"what","is"}, {"what","a"}, {"what","an"},
        {"what","are"}, {"what","was","the"}, {"what","was"}, {"who","was"}, {"who","is"}
    };
    for (const auto& prefix : prefixes) {
        if (tokens.size() > prefix.size() && std::equal(prefix.begin(), prefix.end(), tokens.begin())) {
            request.topic.assign(tokens.begin() + prefix.size(), tokens.end());
            if (prefix.back() == "explain" || prefix.back() == "describe") request.compose = true;
            return request;
        }
    }
    return request;
}
