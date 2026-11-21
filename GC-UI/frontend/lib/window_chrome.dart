import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'theme.dart';

/// 自绘窗口按钮的通道（macOS 侧在 macos/Runner/MainFlutterWindow.swift 实现）。
///
/// 系统标题栏已去掉（titleVisibility = .hidden + fullSizeContentView），
/// 最小化 / 最大化 / 关闭 全部由 Flutter 画，外观跟界面统一。
class GcWindow {
  GcWindow._();

  static const MethodChannel _channel = MethodChannel('gc/window');

  /// 只有 macOS 走了自绘标题栏；其它平台保留系统标题栏，不显示自绘按钮。
  static bool get supported => !kIsWeb && Platform.isMacOS;

  static Future<void> minimize() => _invoke('minimize');
  static Future<void> close() => _invoke('close');
  static Future<void> toggleMaximize() => _invoke('toggleMaximize');

  /// 开始拖拽窗口。用原生 performDrag，比在 Flutter 里模拟位置更跟手。
  static Future<void> beginDrag() => _invoke('beginDrag');

  static Future<bool> isMaximized() async {
    if (!supported) return false;
    try {
      return await _channel.invokeMethod<bool>('isMaximized') ?? false;
    } catch (_) {
      return false;
    }
  }

  static Future<void> _invoke(String method) async {
    if (!supported) return;
    try {
      await _channel.invokeMethod<void>(method);
    } catch (_) {
      // 通道不可用（例如截图测试环境）时静默忽略
    }
  }
}

/// 顶部标题栏：左侧应用名 + 右侧自绘的最小化 / 最大化 / 关闭。
///
/// 只在 macOS 显示；其它平台系统标题栏还在，这里返回零高度占位。
class GcTitleBar extends StatelessWidget {
  const GcTitleBar({super.key, this.title = 'Morris GC'});

  final String title;

  static const double height = 38;

  @override
  Widget build(BuildContext context) {
    if (!GcWindow.supported) return const SizedBox.shrink();

    return GestureDetector(
      behavior: HitTestBehavior.opaque,
      onPanStart: (_) => GcWindow.beginDrag(),
      child: Container(
        height: height,
        // 与主题底色保持一致：浅色主题是纯白，深色主题是深色面板色。
        // 不叠强调色 —— 标题栏属于窗口底色，染色会显得和内容区不是一套。
        decoration: BoxDecoration(
          color: GcColors.sideBg,
          border: Border(bottom: BorderSide(color: GcColors.line)),
        ),
        padding: const EdgeInsets.only(left: GcSpace.xl, right: GcSpace.sm),
        child: Row(
          children: <Widget>[
            Text(
              title,
              style: TextStyle(
                fontSize: 12.5,
                height: 1.2,
                fontWeight: FontWeight.w600,
                letterSpacing: 0.2,
                color: GcColors.faint,
              ),
            ),
            const Spacer(),
            _WindowButton(
              icon: Icons.remove_rounded,
              tooltip: '最小化',
              onTap: GcWindow.minimize,
            ),
            _WindowButton(
              icon: Icons.crop_square_rounded,
              tooltip: '最大化',
              onTap: GcWindow.toggleMaximize,
            ),
            _WindowButton(
              icon: Icons.close_rounded,
              tooltip: '关闭',
              danger: true,
              onTap: GcWindow.close,
            ),
          ],
        ),
      ),
    );
  }
}

class _WindowButton extends StatefulWidget {
  const _WindowButton({
    required this.icon,
    required this.tooltip,
    required this.onTap,
    this.danger = false,
  });

  final IconData icon;
  final String tooltip;
  final Future<void> Function() onTap;
  final bool danger;

  @override
  State<_WindowButton> createState() => _WindowButtonState();
}

class _WindowButtonState extends State<_WindowButton> {
  bool _hover = false;

  @override
  Widget build(BuildContext context) {
    final Color bg = _hover
        ? (widget.danger
            ? GcColors.danger.withOpacity(0.16)
            : GcColors.surfaceHover)
        : Colors.transparent;

    return Tooltip(
      message: widget.tooltip,
      waitDuration: const Duration(milliseconds: 500),
      child: MouseRegion(
        cursor: SystemMouseCursors.basic,
        onEnter: (_) => setState(() => _hover = true),
        onExit: (_) => setState(() => _hover = false),
        child: GestureDetector(
          onTap: () => widget.onTap(),
          child: AnimatedContainer(
            duration: const Duration(milliseconds: 110),
            width: 40,
            height: GcTitleBar.height,
            alignment: Alignment.center,
            color: bg,
            child: Icon(
              widget.icon,
              size: 15,
              color: _hover
                  ? (widget.danger ? GcColors.danger : GcColors.ink)
                  : GcColors.faint,
            ),
          ),
        ),
      ),
    );
  }
}
