import 'package:flutter/material.dart';

import 'controller.dart';
import 'i18n.dart';
import 'shell.dart';
import 'theme.dart';

void main() {
  runApp(const GcUiApp());
}

/// GC-UI 前端：Flutter 桌面应用（Windows / macOS）。
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
    return ValueListenableBuilder<String>(
      valueListenable: I18n.language,
      builder: (context, language, _) {
        return MaterialApp(
          title: 'Morris GC',
          debugShowCheckedModeBanner: false,
          theme: buildGcTheme(),
          home: ShellPage(controller: _controller),
        );
      },
    );
  }
}
