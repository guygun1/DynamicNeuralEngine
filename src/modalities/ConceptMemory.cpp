#include "modalities/ConceptMemory.hpp"
#include <algorithm>
#include <cctype>
#include <queue>
#include <regex>
#include <unordered_set>

namespace {
std::string Clean(std::string text) {
    std::transform(text.begin(), text.end(), text.begin(), [](unsigned char c) { return std::tolower(c); });
    text = std::regex_replace(text, std::regex("\\s+"), " ");
    while (!text.empty() && (text.back() == ' ' || text.back() == '.' || text.back() == '?' || text.back() == '!')) text.pop_back();
    while (!text.empty() && text.front() == ' ') text.erase(0, 1);
    return text;
}
std::string Concept(std::string text) {
    text = Clean(text);
    for (const auto& article : {"a ", "an ", "the "}) if (text.rfind(article, 0) == 0) { text.erase(0, std::string(article).size()); break; }
    return text;
}
bool Suitable(const std::string& text) {
    return !text.empty() && text.size() <= 80 && std::count(text.begin(), text.end(), ' ') < 6 &&
        text.find_first_of(".?!,;:") == std::string::npos &&
        text != "it" && text != "this" && text != "that" && text != "he" && text != "she" && text != "you" && text != "i";
}
std::string Relation(ConceptRelation relation) {
    switch (relation) {
        case ConceptRelation::IsA: return "is a";
        case ConceptRelation::Has: return "has";
        case ConceptRelation::Needs: return "needs";
        case ConceptRelation::In: return "is in";
    }
    return "";
}
std::string Sentence(const ConceptFact& fact, const std::vector<std::string>& vocabulary) {
    std::string relationship = Relation(fact.relation);
    if (fact.relation == ConceptRelation::IsA && !vocabulary[fact.object].empty() && std::string("aeiou").find(vocabulary[fact.object][0]) != std::string::npos) relationship = "is an";
    auto text = vocabulary[fact.subject] + " " + relationship + " " + vocabulary[fact.object] + ".";
    if (!text.empty()) text[0] = static_cast<char>(std::toupper(static_cast<unsigned char>(text[0])));
    return text;
}
}

std::optional<ParsedConceptFact> ConceptMemory::ParseStatement(const std::string& input) {
    std::string text = Clean(input);
    if (text.size() > 200 || input.find('?') != std::string::npos) return std::nullopt;
    if (std::regex_search(text, std::regex("^(what|who|where|why|how|is|does|can|which|when)\\b"))) return std::nullopt;
    std::smatch match;
    ParsedConceptFact fact;
    if (std::regex_match(text, match, std::regex("^all (.+) are (.+)$"))) {
        auto singular = [](std::string value) { value = Concept(value); if (value.size() > 2 && value.back() == 's' && value[value.size() - 2] != 's') value.pop_back(); return value; };
        fact = {singular(match[1]), ConceptRelation::IsA, singular(match[2])};
    } else if (std::regex_match(text, match, std::regex("^(.+) is (?:a|an) (.+)$")))
        fact = {Concept(match[1]), ConceptRelation::IsA, Concept(match[2])};
    else if (std::regex_match(text, match, std::regex("^(.+) (has|needs|is in) (.+)$"))) {
        auto relation = match[2] == "has" ? ConceptRelation::Has : match[2] == "needs" ? ConceptRelation::Needs : ConceptRelation::In;
        fact = {Concept(match[1]), relation, Concept(match[3])};
    } else return std::nullopt;
    if (!Suitable(fact.subject) || !Suitable(fact.object) || fact.subject == fact.object) return std::nullopt;
    return fact;
}

void ConceptMemory::Learn(const ConceptFact& fact) {
    for (auto& old : facts_)
        if (old.subject == fact.subject && old.relation == fact.relation && old.object == fact.object) {
            if (old.source.empty() && !fact.source.empty()) old.source = fact.source;
            return;
        }
    facts_.push_back(fact);
}

std::optional<std::vector<ConceptFact>> ConceptMemory::Prove(int subject, ConceptRelation relation, int object) const {
    struct Step { int node; std::vector<ConceptFact> evidence; };
    std::queue<Step> frontier;
    std::unordered_set<int> visited;
    frontier.push({subject, {}});
    visited.insert(subject);
    while (!frontier.empty()) {
        auto current = frontier.front(); frontier.pop();
        for (const auto& fact : facts_) {
            if (fact.subject != current.node) continue;
            if (fact.relation == relation && fact.object == object) {
                auto evidence = current.evidence; evidence.push_back(fact); return evidence;
            }
            bool traverse = relation == ConceptRelation::In ? fact.relation == ConceptRelation::In : fact.relation == ConceptRelation::IsA;
            if (!traverse || !visited.insert(fact.object).second) continue;
            auto evidence = current.evidence; evidence.push_back(fact);
            frontier.push({fact.object, std::move(evidence)});
        }
    }
    return std::nullopt;
}

ConceptAnswer ConceptMemory::Answer(const std::string& input, const std::vector<std::string>& vocabulary) const {
    std::string text = Clean(input);
    if (text.rfind("why ", 0) == 0) text.erase(0, 4);
    std::smatch match;
    ParsedConceptFact query;
    if (std::regex_match(text, match, std::regex("^is (.+) in (.+)$")))
        query = {Concept(match[1]), ConceptRelation::In, Concept(match[2])};
    else if (std::regex_match(text, match, std::regex("^is (.+) (?:a|an) (.+)$")))
        query = {Concept(match[1]), ConceptRelation::IsA, Concept(match[2])};
    else if (std::regex_match(text, match, std::regex("^does (.+) (have|need) (.+)$")))
        query = {Concept(match[1]), match[2] == "have" ? ConceptRelation::Has : ConceptRelation::Needs, Concept(match[3])};
    else return {};
    if (!Suitable(query.subject) || !Suitable(query.object)) return {};
    auto subject = std::find(vocabulary.begin(), vocabulary.end(), query.subject);
    auto object = std::find(vocabulary.begin(), vocabulary.end(), query.object);
    if (subject == vocabulary.end() || object == vocabulary.end()) return {true, false, ""};
    int sid = static_cast<int>(subject - vocabulary.begin()), oid = static_cast<int>(object - vocabulary.begin());
    auto proof = Prove(sid, query.relation, oid);
    if (!proof) return {true, false, ""};
    bool sourced = std::any_of(proof->begin(), proof->end(), [](const auto& fact) { return !fact.source.empty(); });
    std::string answer = std::string(sourced ? "From the learned source premises: " : "Yes. ") + Sentence({sid, query.relation, oid, ""}, vocabulary) + "\n\nSupporting learned facts:";
    for (size_t i = 0; i < proof->size(); ++i) {
        answer += "\n" + std::to_string(i + 1) + ". " + Sentence((*proof)[i], vocabulary);
        if (!(*proof)[i].source.empty()) answer += " [" + (*proof)[i].source + "]";
    }
    return {true, true, answer};
}
