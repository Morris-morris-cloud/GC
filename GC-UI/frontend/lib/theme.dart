import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

// ---------------------------------------------------------------------------
// 设计令牌
// ---------------------------------------------------------------------------

/// 间距 / 圆角：全应用统一取值，避免各处 13.5、17 之类的散数。
class GcSpace {
  GcSpace._();

  static const double xs = 4;
  static const double sm = 8;
  static const double md = 12;
  static const double lg = 16;
  static const double xl = 20;
  static const double xxl = 24;
  static const double xxxl = 32;
}

/// 圆角。
class GcRadius {
  GcRadius._();

  static const double control = 8;
  static const double card = 12;
  static const double panel = 16;
  static const double pill = 999;
}

/// 尺寸：侧栏 / 控件高度。
class GcSize {
  GcSize._();

  static const double sidebar = 248;
  static const double control = 40;
  static const double controlSmall = 32;
}

/// 字体。
///
/// 中文界面「发糊」的一大来源是字体回退到了很细的旧字体：Windows 上
/// Segoe UI 没有中文字形，缺字时由系统回退，字号一小就糊成一片。
/// 这里显式指定「拉丁用系统 UI 字体 + 中文用雅黑 / 苹方」，并把行高
/// 钉死，避免不同平台的默认行高把中文挤在一起。
class GcFonts {
  GcFonts._();

  /// 拉丁字形字体。macOS 留空，走系统默认（苹方），不要写死字体名。
  static String? get family {
    if (kIsWeb) return null;
    switch (defaultTargetPlatform) {
      case TargetPlatform.windows:
        return 'Segoe UI';
      case TargetPlatform.linux:
        return 'Noto Sans';
      default:
        return null;
    }
  }

  /// 中文字形回退顺序：先细体、再常规体，最后才是旧版回退。
  static List<String> get fallback {
    if (kIsWeb) return const <String>['Noto Sans CJK SC', 'sans-serif'];
    switch (defaultTargetPlatform) {
      case TargetPlatform.windows:
        return const <String>[
          'Microsoft YaHei UI',
          'Microsoft YaHei',
          'SimHei'
        ];
      case TargetPlatform.linux:
        return const <String>[
          'Noto Sans CJK SC',
          'Noto Sans SC',
          'WenQuanYi Micro Hei'
        ];
      default:
        return const <String>[
          'PingFang SC',
          'Hiragino Sans GB',
          'Noto Sans CJK SC'
        ];
    }
  }
}

/// 主题偏好（可在设置页切换，并持久化到后端 settings.theme）。
class GcPalette {
  GcPalette._();

  /// 默认强调色，与 Avalonia 启动器一致。
  static const Color defaultAccent = Color(0xFF8FB6FF);

  static final ValueNotifier<Color> accent =
      ValueNotifier<Color>(defaultAccent);

  /// 当前主题模式（dark / light / system）。
  static final ValueNotifier<ThemeMode> mode =
      ValueNotifier<ThemeMode>(ThemeMode.dark);

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
/// 深色不再用「深蓝底 + 半透明白」，而是中性石墨底 + 高对比正文：
/// 半透明叠加会让中文笔画边缘出现灰边，看着就是「糊」。这里所有文字
/// 颜色都是不透明的实色。
class GcColors {
  GcColors._();

  static bool _dark = true;

  static bool get isDark => _dark;

  /// 由 `MaterialApp.builder` 在每次构建前按实际亮度绑定。
  static void bind(Brightness brightness) =>
      _dark = brightness == Brightness.dark;

  static Color _pick(int dark, int light) => Color(_dark ? dark : light);

  // ---- 底与面 ----
  static Color get appBg => _pick(0xFF080B12, 0xFFF3F6FB);
  static Color get sideBg => _pick(0xFF0C1119, 0xFFFFFFFF);
  static Color get surface => _pick(0xFF131B28, 0xFFFFFFFF);
  static Color get surfaceAlt => _pick(0xFF1A2434, 0xFFEDF2F9);
  static Color get surfaceHover => _pick(0xFF202B3D, 0xFFE2EAF5);

  // ---- 描边 ----
  static Color get line => _pick(0xFF243044, 0xFFDEE6F1);
  static Color get lineStrong => _pick(0xFF33425A, 0xFFC4D1E3);

  // ---- 文字 ----
  static Color get ink => _pick(0xFFEAF0FA, 0xFF0E1522);
  static Color get muted => _pick(0xFFA7B4C8, 0xFF48566B);
  static Color get faint => _pick(0xFF7E8CA3, 0xFF69788E);
  static Color get ghost => _pick(0xFF5C6A80, 0xFF94A2B6);

  // ---- 状态 ----
  static Color get warn => _pick(0xFFF2C57C, 0xFF9A5B00);
  static Color get danger => _pick(0xFFF08A8A, 0xFFB3261E);
  static Color get ok => _pick(0xFF74DFA8, 0xFF12714A);

  /// 当前强调色（动态，跟随设置）。
  static Color get accent => GcPalette.accent.value;

  /// 主按钮填充色：深色主题直接用强调色（浅底深字最清晰），
  /// 浅色主题把强调色压深，避免白底上「浅蓝底 + 白字」看不清。
  static Color get accentFill => _dark ? accent : _darken(accent, 0.18);

  /// 强调色上的文字 / 图标颜色，按对比度自动取深色或白色。
  static Color get onAccent {
    final fill = accentFill;
    // 阈值 0.42：粉紫色（低亮度）走白字，天蓝 / 薄荷（高亮度）走深字。
    return fill.computeLuminance() > 0.42
        ? const Color(0xFF0A1226)
        : Colors.white;
  }

  /// 强调色的淡底（选中项 / 标签背景）。用不透明混色，不用 withOpacity：
  /// 半透明色叠在卡片上会让下层的圆角描边透出来，边缘发脏。
  static Color get accentSoft =>
      Color.lerp(surface, accent, _dark ? 0.16 : 0.12)!;

  static Color _darken(Color color, double amount) {
    final hsl = HSLColor.fromColor(color);
    return hsl
        .withSaturation((hsl.saturation + amount * 0.35).clamp(0.0, 1.0))
        .withLightness((hsl.lightness - amount).clamp(0.0, 1.0))
        .toColor();
  }
}

/// 按指定亮度构建主题。
///
/// [fontFamily] / [fontFamilyFallback] 默认取 [GcFonts] 的平台字体栈；
/// 截图 / 自动化测试需要换成自己加载的字体时，从这里传，才能连按钮、
/// 输入框这些「主题内嵌样式」一起换掉。
///
/// 注意：不要在这里改 [GcColors] 的全局状态 —— MaterialApp 会同时构建
/// theme 与 darkTheme，后构建的那个会把状态写脏。亮度绑定由 `main.dart`
/// 的 builder 统一完成。
ThemeData buildGcTheme(
  Brightness brightness, {
  String? fontFamily,
  List<String>? fontFamilyFallback,
}) {
  final family = fontFamily ?? GcFonts.family;
  final fallback = fontFamilyFallback ?? GcFonts.fallback;
  final isDark = brightness == Brightness.dark;
  final base = isDark
      ? ThemeData.dark(useMaterial3: true)
      : ThemeData.light(useMaterial3: true);

  Color pick(int dark, int light) => Color(isDark ? dark : light);

  final appBg = pick(0xFF080B12, 0xFFF3F6FB);
  final surface = pick(0xFF131B28, 0xFFFFFFFF);
  final surfaceAlt = pick(0xFF1A2434, 0xFFEDF2F9);
  final surfaceHover = pick(0xFF202B3D, 0xFFE2EAF5);
  final line = pick(0xFF243044, 0xFFDEE6F1);
  final lineStrong = pick(0xFF33425A, 0xFFC4D1E3);
  final ink = pick(0xFFEAF0FA, 0xFF0E1522);
  final muted = pick(0xFFA7B4C8, 0xFF48566B);
  final faint = pick(0xFF7E8CA3, 0xFF69788E);
  final danger = pick(0xFFF08A8A, 0xFFB3261E);
  final accent = GcPalette.accent.value;
  final accentFill = isDark ? accent : _darkenTheme(accent, 0.18);
  final accentSoft = Color.lerp(surface, accent, isDark ? 0.16 : 0.12)!;
  final onAccent = accentFill.computeLuminance() > 0.42
      ? const Color(0xFF0A1226)
      : Colors.white;

  // 字号 / 行高在这里定义；字体族必须最后统一 apply ——
  // 下面每个 TextStyle 都是新建对象，不会继承前面的 fontFamily，
  // 漏掉就会回退到系统默认字体（中文尤其糊）。
  final textTheme = base.textTheme
      .copyWith(
        // 页面主标题
        displaySmall: TextStyle(
          fontSize: 28,
          height: 1.24,
          fontWeight: FontWeight.w600,
          letterSpacing: -0.2,
          color: ink,
        ),
        headlineMedium: TextStyle(
          fontSize: 24,
          height: 1.28,
          fontWeight: FontWeight.w600,
          letterSpacing: -0.2,
          color: ink,
        ),
        headlineSmall: TextStyle(
          fontSize: 20,
          height: 1.32,
          fontWeight: FontWeight.w600,
          color: ink,
        ),
        // 分区标题
        titleLarge: TextStyle(
          fontSize: 16,
          height: 1.4,
          fontWeight: FontWeight.w600,
          color: ink,
        ),
        titleMedium: TextStyle(
          fontSize: 14,
          height: 1.45,
          fontWeight: FontWeight.w600,
          color: ink,
        ),
        titleSmall: TextStyle(
          fontSize: 13,
          height: 1.45,
          fontWeight: FontWeight.w600,
          color: ink,
        ),
        // 正文
        bodyLarge: TextStyle(fontSize: 14, height: 1.6, color: ink),
        bodyMedium: TextStyle(fontSize: 13, height: 1.6, color: muted),
        bodySmall: TextStyle(fontSize: 12, height: 1.55, color: faint),
        // 按钮 / 标签
        labelLarge: const TextStyle(
          fontSize: 13,
          height: 1.2,
          fontWeight: FontWeight.w600,
          letterSpacing: 0,
        ),
        labelMedium: TextStyle(
          fontSize: 12,
          height: 1.2,
          fontWeight: FontWeight.w600,
          color: muted,
        ),
        labelSmall: TextStyle(
          fontSize: 11,
          height: 1.2,
          fontWeight: FontWeight.w600,
          letterSpacing: 0.6,
          color: faint,
        ),
      )
      // 只统一字体族，不动上面各自设置的字号与颜色。
      .apply(
        fontFamily: family,
        fontFamilyFallback: fallback,
      );

  final buttonShape = RoundedRectangleBorder(
    borderRadius: BorderRadius.circular(GcRadius.control),
  );

  ButtonStyle buttonStyle({
    required Color background,
    required Color foreground,
    Color? border,
    bool transparent = false,
  }) {
    Color blend(Color base, double v) =>
        Color.lerp(base, isDark ? Colors.white : Colors.black, v)!;
    final Color normal = transparent ? Colors.transparent : background;
    final Color hover = transparent ? surfaceAlt : blend(background, 0.07);
    final Color pressed = transparent ? surfaceHover : blend(background, 0.14);
    return ButtonStyle(
      minimumSize: const WidgetStatePropertyAll<Size>(Size(0, GcSize.control)),
      padding: const WidgetStatePropertyAll<EdgeInsetsGeometry>(
        EdgeInsets.symmetric(horizontal: 18),
      ),
      shape: WidgetStatePropertyAll<OutlinedBorder>(buttonShape),
      textStyle: WidgetStatePropertyAll<TextStyle>(textTheme.labelLarge!),
      elevation: const WidgetStatePropertyAll<double>(0),
      backgroundColor: WidgetStateProperty.resolveWith((states) {
        if (states.contains(WidgetState.disabled)) {
          return transparent ? Colors.transparent : blend(background, 0.04);
        }
        if (states.contains(WidgetState.pressed)) return pressed;
        if (states.contains(WidgetState.hovered) ||
            states.contains(WidgetState.focused)) {
          return hover;
        }
        return normal;
      }),
      foregroundColor: WidgetStateProperty.resolveWith((states) {
        if (states.contains(WidgetState.disabled)) return faint;
        return foreground;
      }),
      overlayColor: const WidgetStatePropertyAll<Color>(Colors.transparent),
      side: border == null
          ? null
          : WidgetStateProperty.resolveWith((states) {
              if (states.contains(WidgetState.disabled)) {
                return BorderSide(color: line);
              }
              if (states.contains(WidgetState.hovered)) {
                return BorderSide(color: lineStrong);
              }
              return BorderSide(color: border);
            }),
    );
  }

  return base.copyWith(
    scaffoldBackgroundColor: appBg,
    colorScheme: base.colorScheme.copyWith(
      brightness: brightness,
      primary: accentFill,
      onPrimary: onAccent,
      secondary: accent,
      onSecondary: onAccent,
      surface: surface,
      onSurface: ink,
      error: danger,
      outline: lineStrong,
      outlineVariant: line,
      surfaceContainerHighest: surfaceAlt,
    ),
    textTheme: textTheme,
    dividerColor: line,
    dividerTheme: DividerThemeData(color: line, thickness: 1, space: 1),
    cardTheme: CardTheme(
      color: surface,
      elevation: 0,
      margin: EdgeInsets.zero,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(GcRadius.card),
        side: BorderSide(color: line),
      ),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: buttonStyle(
        background: accentFill,
        foreground: onAccent,
        border: accentFill,
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: buttonStyle(
        background: Colors.transparent,
        foreground: ink,
        border: lineStrong,
        transparent: true,
      ),
    ),
    textButtonTheme: TextButtonThemeData(
      style: TextButton.styleFrom(
        minimumSize: const Size(0, GcSize.controlSmall),
        padding: const EdgeInsets.symmetric(horizontal: 12),
        foregroundColor: muted,
        textStyle: textTheme.labelLarge,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(GcRadius.control),
        ),
      ).copyWith(
        foregroundColor: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.disabled)) return faint;
          if (states.contains(WidgetState.hovered)) return ink;
          return muted;
        }),
        backgroundColor: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.hovered)) return surfaceAlt;
          return Colors.transparent;
        }),
        overlayColor: const WidgetStatePropertyAll<Color>(Colors.transparent),
      ),
    ),
    iconButtonTheme: IconButtonThemeData(
      style: IconButton.styleFrom(
        minimumSize: const Size(32, 32),
        foregroundColor: muted,
        highlightColor: Colors.transparent,
        hoverColor: surfaceAlt,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(GcRadius.control),
        ),
      ),
    ),
    sliderTheme: base.sliderTheme.copyWith(
      activeTrackColor: accent,
      thumbColor: accent,
      inactiveTrackColor: line,
      trackHeight: 4,
      overlayShape: const RoundSliderOverlayShape(overlayRadius: 16),
      thumbShape: const RoundSliderThumbShape(enabledThumbRadius: 7),
    ),
    switchTheme: SwitchThemeData(
      thumbColor: WidgetStateProperty.resolveWith(
        (states) => states.contains(WidgetState.selected) ? onAccent : faint,
      ),
      trackColor: WidgetStateProperty.resolveWith((states) {
        if (states.contains(WidgetState.selected)) return accentFill;
        return isDark ? surfaceAlt : line;
      }),
      trackOutlineColor: WidgetStateProperty.resolveWith(
        (states) =>
            states.contains(WidgetState.selected) ? accentFill : lineStrong,
      ),
    ),
    progressIndicatorTheme: ProgressIndicatorThemeData(
      color: accent,
      linearTrackColor: isDark ? surfaceAlt : line,
      linearMinHeight: 4,
      circularTrackColor: Colors.transparent,
    ),
    tooltipTheme: TooltipThemeData(
      waitDuration: const Duration(milliseconds: 500),
      decoration: BoxDecoration(
        color: isDark ? const Color(0xFF1D2739) : const Color(0xFF0E1522),
        borderRadius: BorderRadius.circular(6),
      ),
      textStyle: TextStyle(
        color: isDark ? ink : Colors.white,
        fontSize: 12,
        height: 1.4,
      ),
    ),
    dialogTheme: DialogTheme(
      backgroundColor: surface,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(GcRadius.panel),
        side: BorderSide(color: line),
      ),
      titleTextStyle: textTheme.titleLarge,
      contentTextStyle: textTheme.bodyMedium?.copyWith(color: muted),
    ),
    snackBarTheme: SnackBarThemeData(
      backgroundColor:
          isDark ? const Color(0xFF1D2739) : const Color(0xFF0E1522),
      contentTextStyle:
          TextStyle(color: isDark ? ink : Colors.white, fontSize: 13),
      behavior: SnackBarBehavior.floating,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
      elevation: 0,
    ),
    dropdownMenuTheme: DropdownMenuThemeData(
      menuStyle: MenuStyle(
        backgroundColor: WidgetStatePropertyAll<Color>(surfaceAlt),
        surfaceTintColor:
            const WidgetStatePropertyAll<Color>(Colors.transparent),
        shape: WidgetStatePropertyAll<OutlinedBorder>(
          RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(GcRadius.control),
            side: BorderSide(color: line),
          ),
        ),
      ),
      textStyle: textTheme.bodyLarge,
    ),
    popupMenuTheme: PopupMenuThemeData(
      color: surfaceAlt,
      surfaceTintColor: Colors.transparent,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(GcRadius.control),
        side: BorderSide(color: line),
      ),
      textStyle: textTheme.bodyMedium?.copyWith(color: ink),
    ),
    segmentedButtonTheme: SegmentedButtonThemeData(
      style: ButtonStyle(
        minimumSize: const WidgetStatePropertyAll<Size>(Size(0, 36)),
        padding: const WidgetStatePropertyAll<EdgeInsetsGeometry>(
          EdgeInsets.symmetric(horizontal: 14),
        ),
        textStyle: WidgetStatePropertyAll<TextStyle>(textTheme.labelLarge!),
        backgroundColor: WidgetStateProperty.resolveWith((states) {
          return states.contains(WidgetState.selected)
              ? accentSoft
              : Colors.transparent;
        }),
        foregroundColor: WidgetStateProperty.resolveWith((states) {
          return states.contains(WidgetState.selected) ? ink : muted;
        }),
        side: WidgetStatePropertyAll<BorderSide>(BorderSide(color: lineStrong)),
        shape: WidgetStatePropertyAll<OutlinedBorder>(
          RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(GcRadius.control)),
        ),
      ),
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: surfaceAlt,
      isDense: true,
      contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
      hintStyle: TextStyle(color: faint, fontSize: 13),
      helperStyle: TextStyle(color: faint, fontSize: 12),
      border: OutlineInputBorder(
        borderRadius: BorderRadius.circular(GcRadius.control),
        borderSide: BorderSide(color: line),
      ),
      enabledBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(GcRadius.control),
        borderSide: BorderSide(color: line),
      ),
      focusedBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(GcRadius.control),
        borderSide: BorderSide(color: accent, width: 1.4),
      ),
    ),
    scrollbarTheme: ScrollbarThemeData(
      thickness: const WidgetStatePropertyAll<double>(8),
      radius: const Radius.circular(4),
      thumbColor: WidgetStateProperty.resolveWith((states) {
        if (states.contains(WidgetState.hovered) ||
            states.contains(WidgetState.dragged)) {
          return faint;
        }
        return lineStrong;
      }),
      crossAxisMargin: 2,
      mainAxisMargin: 2,
    ),
    splashFactory: NoSplash.splashFactory,
    highlightColor: Colors.transparent,
  );
}

Color _darkenTheme(Color color, double amount) {
  final hsl = HSLColor.fromColor(color);
  return hsl
      .withSaturation((hsl.saturation + amount * 0.4).clamp(0.0, 1.0))
      .withLightness((hsl.lightness - amount).clamp(0.0, 1.0))
      .toColor();
}

// ---------------------------------------------------------------------------
// 通用组件
// ---------------------------------------------------------------------------

/// 通用卡片容器：一层描边 + 极浅投影，不用半透明叠色。
class GcCard extends StatelessWidget {
  const GcCard({
    super.key,
    required this.child,
    this.padding,
    this.width,
    this.radius,
    this.color,
    this.borderColor,
    this.clip = false,
  });

  final Widget child;
  final EdgeInsetsGeometry? padding;
  final double? width;
  final double? radius;
  final Color? color;
  final Color? borderColor;
  final bool clip;

  @override
  Widget build(BuildContext context) {
    final r = radius ?? GcRadius.card;
    return Container(
      width: width,
      padding: padding ?? const EdgeInsets.all(GcSpace.xl),
      clipBehavior: clip ? Clip.antiAlias : Clip.none,
      decoration: BoxDecoration(
        color: color ?? GcColors.surface,
        borderRadius: BorderRadius.circular(r),
        border: Border.all(color: borderColor ?? GcColors.line),
      ),
      child: child,
    );
  }
}

/// 分区标题：左侧标题 + 右侧操作。
class GcSectionTitle extends StatelessWidget {
  const GcSectionTitle(this.text, {super.key, this.trailing, this.subtitle});

  final String text;
  final Widget? trailing;
  final String? subtitle;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.center,
      children: <Widget>[
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: <Widget>[
              Text(
                text,
                style: TextStyle(
                  fontSize: 15,
                  height: 1.4,
                  fontWeight: FontWeight.w600,
                  color: GcColors.ink,
                ),
              ),
              if (subtitle != null) ...<Widget>[
                const SizedBox(height: GcSpace.xs),
                Text(
                  subtitle!,
                  style: TextStyle(
                    fontSize: 12,
                    height: 1.5,
                    color: GcColors.faint,
                  ),
                ),
              ],
            ],
          ),
        ),
        if (trailing != null) trailing!,
      ],
    );
  }
}

/// 说明文字。
class GcHint extends StatelessWidget {
  const GcHint(this.text, {super.key, this.maxLines, this.align});

  final String text;
  final int? maxLines;
  final TextAlign? align;

  @override
  Widget build(BuildContext context) => Text(
        text,
        maxLines: maxLines,
        textAlign: align,
        overflow: maxLines == null ? null : TextOverflow.ellipsis,
        style: TextStyle(
          fontSize: 12,
          height: 1.6,
          color: GcColors.faint,
        ),
      );
}

/// 页面标题区：大标题 + 一句说明 + 右侧操作。
class GcPageHeader extends StatelessWidget {
  const GcPageHeader({
    super.key,
    required this.title,
    this.subtitle,
    this.trailing,
  });

  final String title;
  final String? subtitle;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: <Widget>[
              Text(
                title,
                style: TextStyle(
                  fontSize: 22,
                  height: 1.3,
                  fontWeight: FontWeight.w600,
                  letterSpacing: -0.1,
                  color: GcColors.ink,
                ),
              ),
              if (subtitle != null && subtitle!.isNotEmpty) ...<Widget>[
                const SizedBox(height: GcSpace.xs + 2),
                Text(
                  subtitle!,
                  style: TextStyle(
                    fontSize: 13,
                    height: 1.6,
                    color: GcColors.muted,
                  ),
                ),
              ],
            ],
          ),
        ),
        if (trailing != null) ...<Widget>[
          const SizedBox(width: GcSpace.lg),
          Padding(
            padding: const EdgeInsets.only(top: 2),
            child: trailing!,
          ),
        ],
      ],
    );
  }
}

/// 小胶囊标签（版本 / 标签 / 状态）。
class GcPill extends StatelessWidget {
  const GcPill(
    this.text, {
    super.key,
    this.color,
    this.subtle = false,
    this.icon,
  });

  final String text;
  final Color? color;
  final bool subtle;
  final IconData? icon;

  @override
  Widget build(BuildContext context) {
    final tint = color ?? GcColors.muted;
    final background = subtle
        ? GcColors.surfaceAlt
        : Color.alphaBlend(tint.withOpacity(0.16), GcColors.surface);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
      decoration: BoxDecoration(
        color: background,
        borderRadius: BorderRadius.circular(GcRadius.pill),
        border: Border.all(
          color: subtle
              ? GcColors.line
              : Color.alphaBlend(tint.withOpacity(0.34), GcColors.surface),
        ),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: <Widget>[
          if (icon != null) ...<Widget>[
            Icon(icon, size: 11, color: subtle ? GcColors.faint : tint),
            const SizedBox(width: 4),
          ],
          Text(
            text,
            style: TextStyle(
              fontSize: 11,
              height: 1.25,
              fontWeight: FontWeight.w600,
              color: subtle ? GcColors.faint : tint,
            ),
          ),
        ],
      ),
    );
  }
}

/// 数字指标块（首页 / 关于页用）。
class GcMetric extends StatelessWidget {
  const GcMetric({
    super.key,
    required this.value,
    required this.label,
    this.valueColor,
  });

  final String value;
  final String label;
  final Color? valueColor;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: <Widget>[
        Text(
          value,
          style: TextStyle(
            fontSize: 20,
            height: 1.2,
            fontWeight: FontWeight.w600,
            letterSpacing: -0.2,
            color: valueColor ?? GcColors.ink,
          ),
        ),
        const SizedBox(height: GcSpace.xs),
        Text(
          label,
          style: TextStyle(
            fontSize: 12,
            height: 1.4,
            color: GcColors.faint,
          ),
        ),
      ],
    );
  }
}

/// 统一的空状态 / 错误提示块。
class GcMessage extends StatelessWidget {
  const GcMessage({
    super.key,
    required this.title,
    this.message,
    this.icon,
    this.color,
    this.action,
  });

  final String title;
  final String? message;
  final IconData? icon;
  final Color? color;
  final Widget? action;

  @override
  Widget build(BuildContext context) {
    final tint = color ?? GcColors.faint;
    return GcCard(
      padding: const EdgeInsets.symmetric(
        horizontal: GcSpace.xxl,
        vertical: GcSpace.xl,
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          if (icon != null) ...<Widget>[
            Container(
              width: 34,
              height: 34,
              decoration: BoxDecoration(
                color: GcColors.surfaceAlt,
                borderRadius: BorderRadius.circular(GcRadius.control),
              ),
              alignment: Alignment.center,
              child: Icon(icon, size: 18, color: tint),
            ),
            const SizedBox(width: GcSpace.md),
          ],
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: <Widget>[
                Text(
                  title,
                  style: TextStyle(
                    fontSize: 14,
                    height: 1.45,
                    fontWeight: FontWeight.w600,
                    color: GcColors.ink,
                  ),
                ),
                if (message != null && message!.isNotEmpty) ...<Widget>[
                  const SizedBox(height: GcSpace.xs + 2),
                  Text(
                    message!,
                    style: TextStyle(
                      fontSize: 12,
                      height: 1.6,
                      color: GcColors.faint,
                    ),
                  ),
                ],
                if (action != null) ...<Widget>[
                  const SizedBox(height: GcSpace.md),
                  action!,
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// 设置页 / 关于页的「标签 + 值」行。
class GcFieldRow extends StatelessWidget {
  const GcFieldRow({
    super.key,
    required this.label,
    required this.value,
    this.selectable = false,
    this.valueColor,
    this.trailing,
  });

  final String label;
  final String value;
  final bool selectable;
  final Color? valueColor;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    final style = TextStyle(
      fontSize: 13,
      height: 1.55,
      color: valueColor ?? GcColors.ink,
    );
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 6),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          SizedBox(
            width: 132,
            child: Text(
              label,
              style: TextStyle(
                fontSize: 13,
                height: 1.55,
                color: GcColors.muted,
              ),
            ),
          ),
          Expanded(
            child: selectable
                ? SelectableText(value, style: style)
                : Text(value, style: style),
          ),
          if (trailing != null) ...<Widget>[
            const SizedBox(width: GcSpace.sm),
            trailing!,
          ],
        ],
      ),
    );
  }
}

/// 细分割线。
class GcDivider extends StatelessWidget {
  const GcDivider({super.key, this.verticalPadding = GcSpace.md});

  final double verticalPadding;

  @override
  Widget build(BuildContext context) => Padding(
        padding: EdgeInsets.symmetric(vertical: verticalPadding),
        child: Container(height: 1, color: GcColors.line),
      );
}
