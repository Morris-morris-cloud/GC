/// 五大地形枚举（对应 C# TerrainKind）
enum TerrainKind {
  plain,     // 平原
  hill,      // 丘陵
  mountain,  // 山地
  plateau,   // 高原
  basin,     // 盆地
}

/// 五大典型地貌部位（对应 C# LandformKind）
enum LandformKind {
  peak,    // 山峰
  ridge,   // 山脊
  valley,  // 山谷
  saddle,  // 鞍部
  cliff,   // 陡崖
}

/// 等高线模型状态
class ContourState {
  TerrainKind terrain = TerrainKind.mountain;
  LandformKind? selectedLandform;
  double progress = 0;
  bool isPlaying = false;
  bool colored = false;
  bool isTopView = false;
}
