import 'package:flutter/material.dart';

/// 主题偏好（可在设置页切换，并持久化到后端 settings.theme）。
///
/// 支持深色 / 浅色 / 跟随系统三种；[mode] 变化会让 `main.dart` 重建
/// `MaterialApp`，`themeMode` 交给 Flutter 决定实际亮度。
class GcPalette {
  GcPalette._();

  /// 默认强调色，与 Avalonia 启动器一致。
  static const Color defaultAccent = Color(0xFF8FB6FF);

  static final ValueNotifier<Color> accent =
      ValueNotifier<Color>(defaultAccent);

  /// 当前主题模式（dark / light / system）。
  static final ValueNotifier<ThemeMode> mode = ValueNotifier<ThemeMode>(ThemeMode.dark);

  /// 设置页可选的预设色板。
  static const List<Color> presets = <Color>[
    Color(0xFF8FB6FF), // 极光蓝（默认）
    Color(0xFF7C6CFF), // 星云紫
    Color(0xFF4FD1C5), // 湖水青
    Color(0xFF6BE3A2), // 极光绿
    Color(0xFFFFC46B), // 暖阳橙
    Color(0xFFFF8FB1), // 樱粉
    Color(0xFFFF7A7A), // 珊瑚红
    Color(0xFFB0BEC5), // 石墨灰
  ];

  /// 把后端设置里的 `theme` 字符串转成 [ThemeMode]（未知值按深色处理）。
  static ThemeMode parseMode(String? value) {
    switch ((value ?? '').trim().toLowerCase()) {
      case 'light':
        return ThemeMode.light;
      case 'system':
        return ThemeMode.system;
      case 'dark':
      default:
        return ThemeMode.dark;
    }
  }

  /// 把 [ThemeMode] 转回后端设置的字符串。
  static String modeToName(ThemeMode value) {
    switch (value) {
      case ThemeMode.light:
        return 'light';
      case ThemeMode.system:
        return 'system';
      case ThemeMode.dark:
        return 'dark';
    }
  }

  /// 应用主题模式（来自后端设置）。
  static void applyTheme(String? value) {
    final next = parseMode(value);
    if (mode.value != next) mode.value = next;
  }

  /// 把 `#RRGGBB` / `RRGGBB` 解析为颜色；非法返回 null。
  static Color? parseHex(String? value) {
    if (value == null) return null;
    var hex = value.trim();
    if (hex.startsWith('#')) hex = hex.substring(1);
    if (hex.length == 8) hex = hex.substring(2); // 忽略 alpha
    if (hex.length != 6) return null;
    final parsed = int.tryParse(hex, radix: 16);
    if (parsed == null) return null;
    return Color(0xFF000000 | parsed);
  }

  /// 颜色转 `#RRGGBB`。
  static String toHex(Color color) {
    // ignore: deprecated_member_use
    final rgb = color.value & 0xFFFFFF;
    return '#${rgb.toRadixString(16).padLeft(6, '0').toUpperCase()}';
  }

  /// 应用颜色（来自后端设置或用户点选）。
  static void apply(String? hex) {
    final color = parseHex(hex);
    if (color == null) return;
    // ignore: deprecated_member_use
    if (accent.value.value != color.value) accent.value = color;
  }
}

/// 配色：浅色 / 深色两套，按当前 [Brightness] 取值。
///
/// 以前这里是 const 常量，主题只能是深色 —— 设置里的 `theme` 字段写了也没人读。
/// 改成 getter 后，所有引用点自动跟随主题；代价是不能再放进 const 上下文。
class GcColors {
  GcColors._();

  static bool _dark = true;

  static bool get isDark => _dark;

  /// 由 `MaterialApp.builder` 在每次构建前按实际亮度绑定。
  static void bind(Brightness brightness) => _dark = brightness == Brightness.dark;

  static Color _pick(int dark, int light) => Color(_dark ? dark : light);

  static Color get appBg => _pick(0xFF0A1226, 0xFFF3F6FB);
  static Color get sideBg => _pick(0xFF0C1529, 0xFFFFFFFF);
  static Color get surface => _pick(0xFF111C33, 0xFFFFFFFF);
  static Color get surfaceAlt => _pick(0xFF16233C, 0xFFE9EFFA);
  static Color get line => _pick(0xFF1B2740, 0xFFD8E2F2);
  static Color get ink => _pick(0xFFF2F6FF, 0xFF0F1B2D);
  static Color get muted => _pick(0xFFA9B8D4, 0xFF46587A);
  static Color get faint => _pick(0xFF7C90B4, 0xFF7286A6);
  static Color get warn => _pick(0xFFFFC46B, 0xFFB26A00);
  static Color get danger => _pick(0xFFFF7A7A, 0xFFC0392B);
  static Color get ok => _pick(0xFF6BE3A2, 0xFF1E8E5A);

  /// 侧栏「MENU」一类最弱的说明文字。
  static Color get ghost => _pick(0xFF4C5F80, 0xFF97A8C4);

  /// 当前强调色（动态，跟随设置）。
  static Color get accent => GcPalette.accent.value;

  /// 强调色的加深版本，用于主按钮 / 轨道。浅色主题下需要反向调整，
  /// 否则强调色在浅背景上对比度不足。
  static Color get accentStrong => _strongen(accent);

  static Color _strongen(Color color) {
    final hsl = HSLColor.fromColor(color);
    final lightness = _dark ? hsl.lightness - 0.10 : hsl.lightness - 0.22;
    return hsl
        .withSaturation((hsl.saturation + 0.12).clamp(0.0, 1.0))
        .withLightness(lightness.clamp(0.0, 1.0))
        .toColor();
  }
}

/// 按指定亮度构建主题。
///
/// 注意：不要在这里改 [GcColors] 的全局状态 —— MaterialApp 会同时构建
/// theme 与 darkTheme，后构建的那个会把状态写脏。亮度绑定由 `main.dart`
/// 的 builder 统一完成。
ThemeData buildGcTheme(Brightness brightness) {
  final isDark = brightness == Brightness.dark;
  final base = isDark
      ? ThemeData.dark(useMaterial3: true)
      : ThemeData.light(useMaterial3: true);

  Color pick(int dark, int light) => Color(isDark ? dark : light);

  final appBg = pick(0xFF0A1226, 0xFFF3F6FB);
  final surface = pick(0xFF111C33, 0xFFFFFFFF);
  final surfaceAlt = pick(0xFF16233C, 0xFFE9EFFA);
  final line = pick(0xFF1B2740, 0xFFD8E2F2);
  final ink = pick(0xFFF2F6FF, 0xFF0F1B2D);
  final danger = pick(0xFFFF7A7A, 0xFFC0392B);
  final accent = GcPalette.accent.value;
  final hsl = HSLColor.fromColor(accent);
  final accentStrong = hsl
      .withSaturation((hsl.saturation + 0.12).clamp(0.0, 1.0))
      .withLightness((hsl.lightness - (isDark ? 0.10 : 0.22)).clamp(0.0, 1.0))
      .toColor();

  return base.copyWith(
    scaffoldBackgroundColor: appBg,
    colorScheme: base.colorScheme.copyWith(
      brightness: brightness,
      primary: accentStrong,
      secondary: accent,
      surface: surface,
      error: danger,
    ),
    textTheme: base.textTheme.apply(
      bodyColor: ink,
      displayColor: ink,
    ),
    dividerColor: line,
    cardTheme: CardTheme(
      color: surface,
      elevation: 0,
      margin: EdgeInsets.zero,
    ),
    sliderTheme: base.sliderTheme.copyWith(
      activeTrackColor: accentStrong,
      thumbColor: accent,
      inactiveTrackColor: line,
    ),
    switchTheme: SwitchThemeData(
      thumbColor: WidgetStateProperty.resolveWith(
        (states) => states.contains(WidgetState.selected) ? accent : GcColors.faint,
      ),
      trackColor: WidgetStateProperty.resolveWith(
        (states) => states.contains(WidgetState.selected)
            ? accentStrong.withOpacity(0.45)
            : line,
      ),
    ),
    progressIndicatorTheme: ProgressIndicatorThemeData(
      color: accent,
      linearTrackColor: line,
    ),
    tooltipTheme: TooltipThemeData(
      decoration: BoxDecoration(
        color: surfaceAlt,
        borderRadius: const BorderRadius.all(Radius.circular(6)),
      ),
      textStyle: TextStyle(color: ink, fontSize: 12),
    ),
  );
}

/// 通用卡片容器。
class GcCard extends StatelessWidget {
  const GcCard({super.key, required this.child, this.padding, this.width});

  final Widget child;
  final EdgeInsetsGeometry? padding;
  final double? width;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: width,
      padding: padding ?? const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: GcColors.surface,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: GcColors.line),
      ),
      child: child,
    );
  }
}

/// 小标题。
class GcSectionTitle extends StatelessWidget {
  const GcSectionTitle(this.text, {super.key, this.trailing});

  final String text;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: <Widget>[
        Expanded(
          child: Text(
            text,
            style: TextStyle(
              fontSize: 15,
              fontWeight: FontWeight.w600,
              color: GcColors.ink,
            ),
          ),
        ),
        if (trailing != null) trailing!,
      ],
    );
  }
}

/// 说明文字。
class GcHint extends StatelessWidget {
  const GcHint(this.text, {super.key});

  final String text;

  @override
  Widget build(BuildContext context) => Text(
        text,
        style: TextStyle(fontSize: 12, color: GcColors.faint, height: 1.5),
      );
}
