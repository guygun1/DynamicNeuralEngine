#pragma once
#include <string>
#include <vector>
#include <functional>

struct LanguageRequest {
    std::vector<std::string> topic;
    bool compose = false;
    bool follow_up = false;
};

class LanguageRequests {
public:
    static std::vector<std::string> RecoverSpelling(const std::vector<std::string>& tokens,
        const std::vector<std::string>& vocabulary,
        const std::function<float(const std::string&, const std::string&)>& connection = {});
    static LanguageRequest Interpret(const std::vector<std::string>& tokens);
    static std::vector<std::vector<std::string>> SplitTopics(const std::vector<std::string>& tokens);
    static std::string Join(const std::vector<std::string>& tokens);
};
