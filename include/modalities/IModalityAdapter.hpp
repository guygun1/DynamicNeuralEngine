#pragma once
#include <string>

class NeuralGraph;

enum class ModalityType {
    TextLanguage,
    VisionVideo,
    Spatial3D,
    ComputerUse,
    Custom
};

class IModalityAdapter {
public:
    virtual ~IModalityAdapter() = default;
    virtual const char* GetModalityName() const = 0;
    virtual ModalityType GetModalityType() const = 0;
    virtual void Initialize(NeuralGraph& graph) = 0;
};
