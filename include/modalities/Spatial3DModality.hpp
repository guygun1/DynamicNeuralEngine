#pragma once

#include "IModalityAdapter.hpp"
#include <vector>

/// <summary>
/// Architecture slot for 3D Model generation, meshes, and spatial perception.
/// Connects 3D spatial voxel / vertex latent neurons to the unified NeuralGraph.
/// </summary>
class Spatial3DModality : public IModalityAdapter {
public:
    Spatial3DModality() = default;
    ~Spatial3DModality() override = default;

    const char* GetModalityName() const override { return "Spatial3D"; }
    ModalityType GetModalityType() const override { return ModalityType::Spatial3D; }

    void Initialize(NeuralGraph& graph) override {
        // Will allocate 3D geometric coordinates & point cloud cluster neurons
    }

    // Future 3D Pipeline interfaces:
    // virtual void IngestMesh(const float* vertices, int vertex_count, const int* indices, int index_count);
    // virtual int Generate3DModel(const std::string& prompt, float* output_mesh_buffer);
};
