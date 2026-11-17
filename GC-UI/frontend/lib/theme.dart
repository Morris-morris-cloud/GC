import 'package:flutter/material.dart';

/// 主题强调色（可在设置页自定义，并持久化到后端 settings.accentColor）。
///
/// 用 [ValueNotifier] 持有当前颜色：设置页改色后，`main.dart` 监听到变化
/// 会重建 `MaterialApp.theme`，所有引用 [GcColors.accent] 的组件随之换色。
class GcPalette {
  GcPalette._();

  /// 默认强调色，与 Avalonia 启动器一致。
  static const Color defaultAccent = Color(0xFF8FB6FF);

  static final ValueNotifier<Color> accent =
      ValueNotifier<Color>(defaultAccent);

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

/// 与 Avalonia 启动器一致的深色配色。
class GcColors {
  GcColors._();

  static const Color appBg = Color(0xFF0A1226);
  static const Color sideBg = Color(0xFF0C1529);
  static const Color surface = Color(0xFF111C33);
  static const Color surfaceAlt = Color(0xFF16233C);
  static const Color line = Color(0xFF1B2740);
  static const Color ink = Color(0xFFF2F6FF);
  static const Color muted = Color(0xFFA9B8D4);
  static const Color faint = Color(0xFF7C90B4);
  static const Color warn = Color(0xFFFFC46B);
  static const Color danger = Color(0xFFFF7A7A);
  static const Color ok = Color(0xFF6BE3A2);

  /// 当前强调色（动态，跟随设置）。
  static Color get accent => GcPalette.accent.value;

  /// 强调色的加深版本，用于主按钮 / 轨道。
  static Color get accentStrong => _strongen(accent);

  static Color _strongen(Color color) {
    final hsl = HSLColor.fromColor(color);
    return hsl
        .withSaturation((hsl.saturation + 0.12).clamp(0.0, 1.0))
        .withLightness((hsl.lightness - 0.10).clamp(0.0, 1.0))
        .toColor();
  }
}

ThemeData buildGcTheme() {
  final base = ThemeData.dark(useMaterial3: true);
  final accent = GcColors.accent;
  final accentStrong = GcColors.accentStrong;
  return base.copyWith(
    scaffoldBackgroundColor: GcColors.appBg,
    colorScheme: base.colorScheme.copyWith(
      primary: accentStrong,
      secondary: accent,
      surface: GcColors.surface,
      error: GcColors.danger,
    ),
    textTheme: base.textTheme.apply(
      bodyColor: GcColors.ink,
      displayColor: GcColors.ink,
    ),
    dividerColor: GcColors.line,
    cardTheme: const CardTheme(
      color: GcColors.surface,
      elevation: 0,
      margin: EdgeInsets.zero,
    ),
    sliderTheme: base.sliderTheme.copyWith(
      activeTrackColor: accentStrong,
      thumbColor: accent,
      inactiveTrackColor: GcColors.line,
    ),
    switchTheme: SwitchThemeData(
      thumbColor: WidgetStateProperty.resolveWith(
        (states) => states.contains(WidgetState.selected)
            ? accent
            : GcColors.faint,
      ),
      trackColor: WidgetStateProperty.resolveWith(
        (states) => states.contains(WidgetState.selected)
            ? accentStrong.withOpacity(0.45)
            : GcColors.line,
      ),
    ),
    progressIndicatorTheme: ProgressIndicatorThemeData(
      color: accent,
      linearTrackColor: GcColors.line,
    ),
    tooltipTheme: const TooltipThemeData(
      decoration: BoxDecoration(
        color: GcColors.surfaceAlt,
        borderRadius: BorderRadius.all(Radius.circular(6)),
      ),
      textStyle: TextStyle(color: GcColors.ink, fontSize: 12),
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
            style: const TextStyle(
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
        style: const TextStyle(fontSize: 12, color: GcColors.faint, height: 1.5),
      );
}
