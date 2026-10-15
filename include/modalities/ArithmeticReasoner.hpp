#pragma once
#include <string>

struct ArithmeticResult {
    bool recognized = false;
    bool valid = false;
    std::string response;
};

// A deterministic arithmetic skill. It computes new expressions, rather than
// choosing a nearby memorized arithmetic dialogue. It does not execute input code.
class ArithmeticReasoner {
public:
    static ArithmeticResult Evaluate(const std::string& prompt);
};
