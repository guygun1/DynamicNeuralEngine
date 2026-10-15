#include "core/BrainCodec.hpp"
#include <algorithm>
#include <array>
#include <cstdint>
#include <cstring>
#include <limits>
#include <vector>

namespace {
constexpr uint32_t Magic = 0x31434e44; // DNC1: compact envelope around a versioned brain.
uint32_t Checksum(const std::string& input) {
    uint32_t result = 2166136261u;
    for (unsigned char c : input) { result ^= c; result *= 16777619u; }
    return result;
}
void Write32(std::string& output, uint32_t value) {
    for (int shift = 0; shift < 32; shift += 8) output += static_cast<char>(value >> shift);
}
uint32_t Read32(const std::string& input, size_t offset) {
    uint32_t value = 0;
    for (int i = 0; i < 4; ++i) value |= static_cast<uint32_t>(static_cast<unsigned char>(input[offset + i])) << (i * 8);
    return value;
}
}

std::string BrainCodec::Encode(const std::string& input) {
    if (input.size() > std::numeric_limits<uint32_t>::max()) return input;
    std::vector<int64_t> last(65536, -1);
    std::string packets;
    size_t literal_start = 0, literal_count = 0;
    auto hash = [&](size_t index) {
        uint32_t value = static_cast<unsigned char>(input[index]) * 251u + static_cast<unsigned char>(input[index + 1]);
        return (value * 251u + static_cast<unsigned char>(input[index + 2])) & 65535u;
    };
    auto literals = [&]() {
        if (!literal_count) return;
        packets += static_cast<char>(literal_count - 1);
        packets.append(input, literal_start, literal_count);
        literal_count = 0;
    };
    size_t index = 0;
    while (index < input.size()) {
        size_t length = 0, distance = 0;
        if (index + 3 < input.size()) {
            auto slot = hash(index);
            int64_t previous = last[slot];
            last[slot] = static_cast<int64_t>(index);
            if (previous >= 0 && index - static_cast<size_t>(previous) <= 65535) {
                distance = index - static_cast<size_t>(previous);
                while (length < 130 && index + length < input.size() && input[previous + length] == input[index + length]) length++;
            }
        }
        if (length >= 4) {
            literals();
            packets += static_cast<char>(0x80u | (length - 3));
            packets += static_cast<char>(distance);
            packets += static_cast<char>(distance >> 8);
            for (size_t i = 1; i < length && index + i + 2 < input.size(); ++i) last[hash(index + i)] = static_cast<int64_t>(index + i);
            index += length;
        } else {
            if (!literal_count) literal_start = index;
            literal_count++; index++;
            if (literal_count == 128) literals();
        }
    }
    literals();
    std::string encoded;
    Write32(encoded, Magic);
    Write32(encoded, static_cast<uint32_t>(input.size()));
    Write32(encoded, Checksum(input));
    encoded += packets;
    return encoded.size() < input.size() ? encoded : input;
}

bool BrainCodec::Decode(const std::string& input, std::string& output) {
    if (input.size() < 4 || Read32(input, 0) != Magic) { output = input; return true; }
    if (input.size() < 12) return false;
    uint32_t size = Read32(input, 4);
    if (size > 512u * 1024u * 1024u) return false;
    output.clear(); output.reserve(size);
    size_t cursor = 12;
    while (cursor < input.size()) {
        unsigned int flag = static_cast<unsigned char>(input[cursor++]);
        if (flag < 128) {
            size_t length = flag + 1;
            if (cursor + length > input.size() || output.size() + length > size) return false;
            output.append(input, cursor, length); cursor += length;
        } else {
            if (cursor + 2 > input.size()) return false;
            size_t length = (flag & 127u) + 3;
            size_t distance = static_cast<unsigned char>(input[cursor]) | (static_cast<unsigned char>(input[cursor + 1]) << 8);
            cursor += 2;
            if (!distance || distance > output.size() || output.size() + length > size) return false;
            for (size_t i = 0; i < length; ++i) output += output[output.size() - distance];
        }
    }
    return output.size() == size && Checksum(output) == Read32(input, 8);
}
