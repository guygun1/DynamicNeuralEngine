#include "modalities/ArithmeticReasoner.hpp"
#include <algorithm>
#include <cctype>
#include <cmath>
#include <cstdlib>
#include <iomanip>
#include <locale>
#include <map>
#include <sstream>
#include <stdexcept>
#include <vector>

namespace {
std::string Number(long double value) {
    if (value == 0) value = 0; // Normalize negative zero.
    std::ostringstream out;
    out.imbue(std::locale::classic());
    out << std::setprecision(15) << value;
    return out.str();
}

void Replace(std::string& text, const std::string& from, const std::string& to) {
    size_t pos = 0;
    while ((pos = text.find(from, pos)) != std::string::npos) {
        text.replace(pos, from.size(), to);
        pos += to.size();
    }
}

std::string Normalize(std::string text) {
    std::transform(text.begin(), text.end(), text.begin(), [](unsigned char c) { return std::tolower(c); });
    while (!text.empty() && (std::isspace(static_cast<unsigned char>(text.back())) || text.back() == '?' || text.back() == '=')) text.pop_back();
    size_t first = text.find_first_not_of(" \t\r\n");
    text = first == std::string::npos ? "" : text.substr(first);
    if (text.size() > 1 && text.back() == '.' && text.find_last_of("0123456789") != text.size() - 2) text.pop_back();
    const std::vector<std::string> prefixes = {"can you calculate ", "can you compute ", "please calculate ", "please compute ", "please tell me ", "what is ", "what's ", "calculate ", "compute ", "evaluate ", "solve ", "tell me ", "explain "};
    for (const auto& prefix : prefixes)
        if (text.rfind(prefix, 0) == 0) { text.erase(0, prefix.size()); break; }
    for (const auto& suffix : {" step by step", " show your work", " in detail", " equal", " equals"}) {
        std::string ending(suffix);
        if (text.size() >= ending.size() && text.compare(text.size() - ending.size(), ending.size(), ending) == 0)
            text.erase(text.size() - ending.size());
    }
    for (size_t i = 1; i + 1 < text.size(); ++i)
        if (text[i] == '-' && std::isalpha(static_cast<unsigned char>(text[i - 1])) && std::isalpha(static_cast<unsigned char>(text[i + 1]))) text[i] = ' ';
    text = " " + text + " ";
    for (const auto& [from, to] : std::vector<std::pair<std::string, std::string>>{
        {" multiplied by ", " * "}, {" divided by ", " / "}, {" to the power of ", " ^ "},
        {" plus ", " + "}, {" minus ", " - "}, {" times ", " * "}, {" x ", " * "},
        {" negative ", " - "}, {" squared ", " ^ 2 "}, {" cubed ", " ^ 3 "},
        {" percent of ", " / 100 * "}}) Replace(text, from, to);

    const std::map<std::string, int> values = {
        {"zero",0},{"one",1},{"two",2},{"three",3},{"four",4},{"five",5},{"six",6},{"seven",7},{"eight",8},{"nine",9},
        {"ten",10},{"eleven",11},{"twelve",12},{"thirteen",13},{"fourteen",14},{"fifteen",15},{"sixteen",16},
        {"seventeen",17},{"eighteen",18},{"nineteen",19},{"twenty",20},{"thirty",30},{"forty",40},{"fifty",50},
        {"sixty",60},{"seventy",70},{"eighty",80},{"ninety",90}
    };
    // Group spelled number phrases while preserving symbolic operators.
    std::istringstream words(text);
    std::string word, result;
    long double group = 0, total = 0;
    bool active = false;
    auto flush = [&]() {
        if (active) result += Number(total + group) + " ";
        active = false; group = total = 0;
    };
    while (words >> word) {
        auto value = values.find(word);
        if (value != values.end()) { group += value->second; active = true; }
        else if (word == "hundred") { group = (active ? group : 1) * 100; active = true; }
        else if (word == "thousand" || word == "million") {
            total += (active ? group : 1) * (word == "thousand" ? 1000 : 1000000);
            group = 0; active = true;
        } else if (word == "and" && active) { continue; }
        else { flush(); result += word + " "; }
    }
    flush();
    return result;
}

class Parser {
    const std::string& text_;
    size_t position_ = 0;
    int depth_ = 0;
    int operations_ = 0;
    void Space() { while (position_ < text_.size() && std::isspace(static_cast<unsigned char>(text_[position_]))) position_++; }
    bool Eat(char c) { Space(); if (position_ < text_.size() && text_[position_] == c) { position_++; return true; } return false; }
    long double Apply(long double a, long double b, char operation) {
        if (++operations_ > 256) throw std::runtime_error("limit");
        if ((operation == '/' || operation == '%') && b == 0) throw std::domain_error("zero");
        long double value = operation == '+' ? a + b : operation == '-' ? a - b : operation == '*' ? a * b :
            operation == '/' ? a / b : operation == '%' ? std::fmod(a, b) : std::pow(a, b);
        if (!std::isfinite(value)) throw std::domain_error("domain");
        steps.push_back(Number(a) + " " + operation + " " + Number(b) + " = " + Number(value));
        return value;
    }
    long double Primary() {
        if (++depth_ > 64) throw std::runtime_error("depth");
        long double value;
        if (Eat('(')) { value = Expression(); if (!Eat(')')) throw std::runtime_error("parenthesis"); }
        else {
            Space();
            if (position_ >= text_.size() || (!std::isdigit(static_cast<unsigned char>(text_[position_])) && text_[position_] != '.')) throw std::runtime_error("number");
            char* end = nullptr;
            value = std::strtold(text_.c_str() + position_, &end);
            if (end == text_.c_str() + position_ || !std::isfinite(value)) throw std::runtime_error("number");
            position_ = static_cast<size_t>(end - text_.c_str());
        }
        depth_--;
        return value;
    }
    long double Power() { auto value = Primary(); if (Eat('^')) value = Apply(value, Unary(), '^'); return value; }
    long double Unary() {
        if (++depth_ > 64) throw std::runtime_error("depth");
        long double value = Eat('+') ? Unary() : Eat('-') ? -Unary() : Power();
        depth_--; return value;
    }
    long double Term() {
        auto value = Unary();
        while (true) {
            if (Eat('*')) value = Apply(value, Unary(), '*');
            else if (Eat('/')) value = Apply(value, Unary(), '/');
            else if (Eat('%')) value = Apply(value, Unary(), '%');
            else break;
        }
        return value;
    }
    long double Expression() {
        auto value = Term();
        while (true) {
            if (Eat('+')) value = Apply(value, Term(), '+');
            else if (Eat('-')) value = Apply(value, Term(), '-');
            else break;
        }
        return value;
    }
public:
    std::vector<std::string> steps;
    explicit Parser(const std::string& text) : text_(text) {}
    long double Run() { auto value = Expression(); Space(); if (position_ != text_.size()) throw std::runtime_error("trailing input"); return value; }
};
}

ArithmeticResult ArithmeticReasoner::Evaluate(const std::string& prompt) {
    if (prompt.size() > 8192) return {};
    std::string expression = Normalize(prompt);
    bool numeric = std::any_of(expression.begin(), expression.end(), [](unsigned char c) { return std::isdigit(c); });
    if (!numeric) return {};
    bool operation = expression.find_first_of("+-*/^%") != std::string::npos;
    bool letters = std::any_of(expression.begin(), expression.end(), [](unsigned char c) { return std::isalpha(c) && c != 'e'; });
    if (letters) return {operation, false, ""};
    try {
        Parser parser(expression);
        auto value = parser.Run();
        std::string canonical = expression;
        while (!canonical.empty() && canonical.back() == ' ') canonical.pop_back();
        std::string output = canonical + " = " + Number(value) + ".";
        if (parser.steps.size() > 1) {
            output += "\n\n";
            for (const auto& step : parser.steps) output += step + ".\n";
            if (output.back() == '\n') output.pop_back();
        }
        return {true, true, output};
    } catch (const std::domain_error&) {
        return {true, true, "undefined (" + expression + ")"};
    } catch (const std::exception&) {
        return {operation, false, ""};
    }
}
