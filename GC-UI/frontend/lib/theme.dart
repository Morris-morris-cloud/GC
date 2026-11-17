import 'package:flutter/material.dart';

/// 与 Avalonia 启动器一致的深色配色。
class GcColors {
  GcColors._();

  static const Color appBg = Color(0xFF0A1226);
  static const Color sideBg = Color(0xFF0C1529);
  static const Color surface = Color(0xFF111C33);
  static const Color surfaceAlt = Color(0xFF16233C);
  static const Color line = Color(0xFF1B2740);
  static const Color accent = Color(0xFF8FB6FF);
  static const Color accentStrong = Color(0xFF4C7DFF);
  static const Color ink = Color(0xFFF2F6FF);
  static const Color muted = Color(0xFFA9B8D4);
  static const Color faint = Color(0xFF7C90B4);
  static const Color warn = Color(0xFFFFC46B);
  static const Color danger = Color(0xFFFF7A7A);
  static const Color ok = Color(0xFF6BE3A2);
}

ThemeData buildGcTheme() {
  final base = ThemeData.dark(useMaterial3: true);
  return base.copyWith(
    scaffoldBackgroundColor: GcColors.appBg,
    colorScheme: base.colorScheme.copyWith(
      primary: GcColors.accentStrong,
      secondary: GcColors.accent,
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
      activeTrackColor: GcColors.accentStrong,
      thumbColor: GcColors.accent,
      inactiveTrackColor: GcColors.line,
    ),
    switchTheme: SwitchThemeData(
      thumbColor: WidgetStateProperty.resolveWith(
        (states) => states.contains(WidgetState.selected)
            ? GcColors.accent
            : GcColors.faint,
      ),
      trackColor: WidgetStateProperty.resolveWith(
        (states) => states.contains(WidgetState.selected)
            ? GcColors.accentStrong.withOpacity(0.45)
            : GcColors.line,
      ),
    ),
    progressIndicatorTheme: const ProgressIndicatorThemeData(
      color: GcColors.accent,
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
