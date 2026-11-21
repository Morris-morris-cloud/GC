/// 模型注册表：所有 Flutter 实现的模型都在这里注册。
library;

export 'base.dart';
export 'contour_model.dart';
export 'contour_flutter.dart';
export 'geography_model.dart';
export 'geography_flutter.dart';
export 'rotation_flutter.dart';
export 'revolution_flutter.dart';
export 'scale_model.dart';
export 'scale_flutter.dart';

import 'base.dart';
import 'contour_flutter.dart';
import 'geography_flutter.dart';
import 'rotation_flutter.dart';
import 'revolution_flutter.dart';
import 'scale_flutter.dart';

/// 所有可用的 Flutter 模型实例。
final List<GcModel> allGcModels = [
  ContourModelFlutter(),
  GeographyModelFlutter(),
  RotationModelFlutter(),
  RevolutionModelFlutter(),
  ScaleCalculatorModelFlutter(),
];

/// 根据 id 查找模型。
GcModel? findModel(String id) {
  try {
    return allGcModels.firstWhere((m) => m.id == id);
  } catch (_) {
    return null;
  }
}
