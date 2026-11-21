import 'base.dart';
import 'contour_flutter.dart';
import 'geography_flutter.dart';
import 'revolution_flutter.dart';
import 'rotation_flutter.dart';
import 'scale_flutter.dart';

/// 后端模型 id → Flutter 实现。
///
/// 后端（`/api/models`）用的是模型包目录名（contour-map、earth-rotation…），
/// Flutter 实现自己的 id 带 `-flutter` 后缀，这里做一次映射。
/// Avalonia 版模型窗口已移除，所有模型统一在客户端内用 Flutter 渲染。
final Map<String, GcModel Function()> _factories = <String, GcModel Function()>{
  'contour-map': () => ContourModelFlutter(),
  'earth-geography': () => GeographyModelFlutter(),
  'earth-rotation': () => RotationModelFlutter(),
  'earth-revolution': () => RevolutionModelFlutter(),
  'scale-calculator': () => ScaleCalculatorModelFlutter(),
};

/// 按模型 id 取 Flutter 实现；没有对应实现时返回 null。
GcModel? flutterModelFor(String id) {
  final factory = _factories[id] ?? _factories[id.replaceAll('-flutter', '')];
  return factory?.call();
}
