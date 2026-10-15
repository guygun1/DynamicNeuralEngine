#pragma once
#include <optional>
#include <string>
#include <vector>

enum class ConceptRelation : int { IsA = 0, Has = 1, Needs = 2, In = 3 };
struct ConceptFact { int subject; ConceptRelation relation; int object; std::string source; };
struct ParsedConceptFact { std::string subject; ConceptRelation relation; std::string object; };
struct ConceptAnswer { bool recognized = false; bool proved = false; std::string text; };

// Explicit propositions differ from mere word co-occurrence. Inference derives
// conclusions from stored premises and returns the actual supporting fact path.
class ConceptMemory {
public:
    static std::optional<ParsedConceptFact> ParseStatement(const std::string& text);
    void Learn(const ConceptFact& fact);
    ConceptAnswer Answer(const std::string& query, const std::vector<std::string>& vocabulary) const;
    const std::vector<ConceptFact>& Facts() const { return facts_; }
    void Load(const std::vector<ConceptFact>& facts) { facts_ = facts; }
private:
    std::vector<ConceptFact> facts_;
    std::optional<std::vector<ConceptFact>> Prove(int subject, ConceptRelation relation, int object) const;
};
