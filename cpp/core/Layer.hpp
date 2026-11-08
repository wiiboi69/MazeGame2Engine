#pragma once
#include "Common.hpp"
#include "TileDef.hpp"
namespace mg {

enum class LayerKind { Tiles, Image };

struct ImageLayerProps {
    std::string image;
    real x = 0, y = 0;
    real scale = 1;
    real parallax = 1;
    bool repeatX = false, repeatY = false;
    bool fitScreen = false;
};

struct Layer : ImageLayerProps {
    std::string name = "layer";
    LayerKind kind = LayerKind::Tiles;
    bool front = false;
    bool visible = true;
    std::vector<int> tiles;   // column-major like the main layer

    static Layer newTiles(const std::string& name, int width, int height, bool front) {
        Layer l; l.name = name; l.kind = LayerKind::Tiles; l.front = front;
        l.tiles.assign((size_t)width * height, TileInfo::Air);
        return l;
    }
    Layer clone() const { return *this; }   // value copy == MemberwiseClone + tiles clone
};

} // namespace mg
