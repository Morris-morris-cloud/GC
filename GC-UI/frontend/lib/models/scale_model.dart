import 'package:flutter/material.dart';

/// 比例尺预设
class ScalePreset {
  final int n;
  final String nameKey;
  final String noteKey;

  ScalePreset({required this.n, required this.nameKey, required this.noteKey});
}

/// 比例尺光谱
class ScaleSpectrum {
  final int n;
  final String labelKey;
  final String scopeKey;

  ScaleSpectrum({required this.n, required this.labelKey, required this.scopeKey});
}

/// 真实地标
class Landmark {
  final String nameKey;
  final double meters;

  Landmark({required this.nameKey, required this.meters});
}
