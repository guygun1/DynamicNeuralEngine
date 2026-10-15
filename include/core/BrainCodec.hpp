#pragma once
#include <string>

// Bounded lossless LZ packets: no external compression runtime, no weight rounding.
class BrainCodec {
public:
    static std::string Encode(const std::string& input);
    static bool Decode(const std::string& input, std::string& output);
};
