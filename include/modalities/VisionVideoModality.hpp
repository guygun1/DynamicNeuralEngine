#pragma once

#include "IModalityAdapter.hpp"
#include <vector>
#include <cstdint>

/// <summary>
/// Architecture slot for Vision & Video generation and perception.
/// Connects visual temporal frame latent neurons to the unified NeuralGraph.
/// </summary>
class VisionVideoModality : public IModalityAdapter {
public:
    VisionVideoModality() = default;
    ~VisionVideoModality() override = default;

    const char* GetModalityName() const override { return "VisionVideo"; }
    ModalityType GetModalityType() const override { return ModalityType::VisionVideo; }

    void Initialize(NeuralGraph& graph) override {
        // Will allocate latent patch neurons and temporal frame transitions
    }

    // Future Vision/Video Pipeline interfaces:
    // virtual void IngestFrame(const uint8_t* rgba_data, int width, int height);
    // virtual int GenerateVideoSequence(const std::string& prompt, uint8_t* output_frames, int num_frames);
};
