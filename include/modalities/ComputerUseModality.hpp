#pragma once

#include "IModalityAdapter.hpp"
#include <string>

/// <summary>
/// Architecture slot for PC Use, OS interaction, and UI automation.
/// Connects screen perception and mouse/keyboard action policies to the NeuralGraph.
/// </summary>
class ComputerUseModality : public IModalityAdapter {
public:
    ComputerUseModality() = default;
    ~ComputerUseModality() override = default;

    const char* GetModalityName() const override { return "ComputerUse"; }
    ModalityType GetModalityType() const override { return ModalityType::ComputerUse; }

    void Initialize(NeuralGraph& graph) override {
        // Will allocate OS action neurons (click, type, keypress, drag, inspect)
    }

    // Future Computer Use interfaces:
    // virtual void IngestScreenState(const void* screenshot_buffer, int width, int height);
    // virtual void ExecuteAction(int action_id, int x, int y, const char* text);
};
