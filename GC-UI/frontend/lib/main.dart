import 'package:flutter/material.dart';

import 'controller.dart';
import 'i18n.dart';
import 'shell.dart';
import 'theme.dart';

void main() {
  // 桌面端无需异步初始化外部插件：尽早绑定并直接渲染首帧，
  // 让窗口在引擎就绪后立刻出现，后端探测在外壳里异步进行。
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const GcUiApp());
}

/// GC-UI 前端：Flutter 桌面应用（Windows / macOS / Linux）。
///
/// 界面全部在这里；模型扫描、下载、更新、设置、模型窗口宿主都在后端
/// （GC-UI/backend/GC.UI.Server，本地 HTTP 服务）。
class GcUiApp extends StatefulWidget {
  const GcUiApp({super.key});

  @override
  State<GcUiApp> createState() => _GcUiAppState();
}

class _GcUiAppState extends State<GcUiApp> {
  final ShellController _controller = ShellController();

  @override
  Widget build(BuildContext context) {
    // 语言、强调色、主题模式都会触发重建：只重建 MaterialApp 的主题层，
    // 不触碰页面状态（页面由 ShellPage 自己持有）。
    return ValueListenableBuilder<String>(
      valueListenable: I18n.language,
      builder: (context, language, _) {
        return ValueListenableBuilder<Color>(
          valueListenable: GcPalette.accent,
          builder: (context, accent, _) {
            return ValueListenableBuilder<ThemeMode>(
              valueListenable: GcPalette.mode,
              builder: (context, mode, _) {
                return MaterialApp(
                  title: 'Morris GC',
                  debugShowCheckedModeBanner: false,
                  theme: buildGcTheme(Brightness.light),
                  darkTheme: buildGcTheme(Brightness.dark),
                  themeMode: mode,
                  // GcColors 是静态取值，必须按当前实际亮度（含跟随系统）绑定，
                  // 再用 key 强制子树重建，否则切主题后颜色仍是上一套。
                  builder: (context, child) {
                    final brightness = Theme.of(context).brightness;
                    GcColors.bind(brightness);
                    return KeyedSubtree(
                      key: ValueKey<bool>(brightness == Brightness.dark),
                      child: child ?? const SizedBox.shrink(),
                    );
                  },
                  home: ShellPage(controller: _controller),
                );
              },
            );
          },
        );
      },
    );
  }
}
