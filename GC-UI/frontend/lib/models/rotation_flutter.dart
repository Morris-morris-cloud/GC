import 'dart:math' as math;
import 'package:flutter/material.dart';
import 'base.dart';

/// 地球自转模型 Flutter 实现
class RotationModelFlutter implements GcModel {
  @override
  final String id = 'earth-rotation-flutter';

  @override
  String name() => '地球自转 (Flutter)';

  @override
  String description() => '纯 Flutter 实现的地球自转模型，观察昼夜交替、晨昏线';

  @override
  Widget buildView(BuildContext context) {
    return const _RotationModelView();
  }
}

class _RotationModelView extends StatefulWidget {
  const _RotationModelView();

  @override
  State<_RotationModelView> createState() => _RotationModelViewState();
}

class _RotationModelViewState extends State<_RotationModelView>
    with TickerProviderStateMixin {
  late AnimationController _spinController;
  late AnimationController _tiltController;
  bool _isPlaying = true;
  double _speed = 1.0;
  bool _showLabels = true;
  bool _showTilt = false;

  @override
  void initState() {
    super.initState();
    _spinController = AnimationController(
      vsync: this,
      duration: const Duration(seconds: 10),
    )..repeat();
    _tiltController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 500),
    );
  }

  @override
  void dispose() {
    _spinController.dispose();
    _tiltController.dispose();
    super.dispose();
  }

  void _togglePlay() {
    setState(() {
      _isPlaying = !_isPlaying;
    });
    if (_isPlaying) {
      _spinController.repeat();
    } else {
      _spinController.stop();
    }
  }

  void _reset() {
    setState(() {
      _isPlaying = true;
      _speed = 1.0;
      _showLabels = true;
      _showTilt = false;
    });
    _spinController.repeat();
    _tiltController.reset();
  }

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final bgColor = isDark ? const Color(0xFF0B1524) : const Color(0xFFE8F0FE);
    final panelColor = isDark ? const Color(0xFF101E31) : const Color(0xFFF5F7FA);
    final inkColor = isDark ? const Color(0xFFEAF2FF) : const Color(0xFF111A2E);

    return Scaffold(
      backgroundColor: bgColor,
      body: Column(
        children: [
          // 标题栏
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 12),
            color: panelColor,
            child: Row(
              children: [
                Text(
                  '地球自转 · 昼夜交替',
                  style: TextStyle(
                    fontSize: 16,
                    fontWeight: FontWeight.w600,
                    color: inkColor,
                  ),
                ),
                const Spacer(),
                _toggleButton('标注', _showLabels, () => setState(() => _showLabels = !_showLabels)),
                _toggleButton('倾角', _showTilt, () => setState(() => _showTilt = !_showTilt)),
                const SizedBox(width: 8),
                TextButton.icon(
                  onPressed: _reset,
                  icon: const Icon(Icons.refresh_rounded, size: 16),
                  label: const Text('重置'),
                ),
              ],
            ),
          ),
          // 地球动画
          Expanded(
            child: _RotationEarth(
              animProgress: _spinController.value,
              showLabels: _showLabels,
              showTilt: _showTilt,
              isPlaying: _isPlaying,
            ),
          ),
          // 底部控制栏
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
            color: panelColor,
            child: Row(
              children: [
                IconButton(
                  onPressed: _togglePlay,
                  icon: Icon(_isPlaying ? Icons.pause_rounded : Icons.play_arrow_rounded),
                  color: const Color(0xFF4D8CFF),
                ),
                const SizedBox(width: 16),
                Text('速度:', style: TextStyle(fontSize: 12, color: isDark ? const Color(0xFF93A8C6) : const Color(0xFF5C6A80))),
                const SizedBox(width: 8),
                Expanded(
                  child: Slider(
                    value: _speed,
                    min: 0.1,
                    max: 3.0,
                    divisions: 29,
                    onChanged: (v) => setState(() => _speed = v),
                  ),
                ),
                Text('${_speed.toStringAsFixed(1)}x', style: const TextStyle(fontSize: 12)),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _toggleButton(String label, bool active, VoidCallback onTap) {
    return Padding(
      padding: const EdgeInsets.only(left: 8),
      child: FilterChip(
        label: Text(label, style: const TextStyle(fontSize: 12)),
        selected: active,
        onSelected: (_) => onTap(),
        selectedColor: const Color(0xFF4D8CFF).withOpacity(0.2),
        checkmarkColor: const Color(0xFF4D8CFF),
      ),
    );
  }
}

class _RotationEarth extends StatelessWidget {
  final double animProgress;
  final bool showLabels;
  final bool showTilt;
  final bool isPlaying;

  const _RotationEarth({
    required this.animProgress,
    required this.showLabels,
    required this.showTilt,
    required this.isPlaying,
  });

  @override
  Widget build(BuildContext context) {
    return CustomPaint(
      size: Size.infinite,
      painter: _RotationPainter(
        animProgress: animProgress,
        showLabels: showLabels,
        showTilt: showTilt,
        isPlaying: isPlaying,
      ),
    );
  }
}

class _RotationPainter extends CustomPainter {
  final double animProgress;
  final bool showLabels;
  final bool showTilt;
  final bool isPlaying;

  _RotationPainter({
    required this.animProgress,
    required this.showLabels,
    required this.showTilt,
    required this.isPlaying,
  });

  @override
  void paint(Canvas canvas, Size size) {
    final bgColor = const Color(0xFF08111E);
    final earthColor = const Color(0xFF1C5FA8);
    final landColor = const Color(0xFF4E8C4C);
    final iceColor = const Color(0xFFE9EFF6);
    final axisColor = const Color(0xFFFF6B35);
    final labelColor = const Color(0xFFEAF2FF);

    canvas.drawColor(bgColor, BlendMode.src);

    final center = Offset(size.width / 2, size.height / 2);
    final radius = math.min(size.width, size.height) * 0.3;

    // 绘制地球球体
    final earthPaint = Paint()
      ..color = earthColor
      ..style = PaintingStyle.fill;
    canvas.drawCircle(center, radius, earthPaint);

    // 绘制大陆（简化版，用椭圆模拟）
    final continents = [
      (center.dx - radius * 0.3, center.dy - radius * 0.2, radius * 0.25, radius * 0.35), // 亚洲
      (center.dx + radius * 0.2, center.dy - radius * 0.1, radius * 0.2, radius * 0.25), // 欧洲
      (center.dx - radius * 0.1, center.dy + radius * 0.2, radius * 0.15, radius * 0.3), // 非洲
      (center.dx + radius * 0.35, center.dy + radius * 0.1, radius * 0.2, radius * 0.25), // 北美
      (center.dx + radius * 0.3, center.dy + radius * 0.4, radius * 0.15, radius * 0.2), // 南美
    ];

    for (final cont in continents) {
      canvas.drawOval(
        Rect.fromLTWH(cont.$1 - cont.$3 / 2, cont.$2 - cont.$4 / 2, cont.$3, cont.$4),
        Paint()..color = landColor,
      );
    }

    // 绘制极冰
    canvas.drawOval(
      Rect.fromLTWH(center.dx - radius * 0.15, center.dy - radius * 0.95, radius * 0.3, radius * 0.15),
      Paint()..color = iceColor,
    );
    canvas.drawOval(
      Rect.fromLTWH(center.dx - radius * 0.15, center.dy + radius * 0.8, radius * 0.3, radius * 0.15),
      Paint()..color = iceColor,
    );

    // 绘制地轴
    if (showTilt) {
      final tiltAngle = 23.5 * math.pi / 180;
      final axisLength = radius * 1.4;
      final topY = center.dy - axisLength / 2;
      final bottomY = center.dy + axisLength / 2;
      final offsetX = math.sin(tiltAngle) * axisLength / 2;

      canvas.drawLine(
        Offset(center.dx - offsetX, topY),
        Offset(center.dx + offsetX, bottomY),
        Paint()
          ..color = axisColor
          ..strokeWidth = 2
          ..style = PaintingStyle.stroke,
      );

      // 北极标注
      if (showLabels) {
        final textPainter = TextPainter(
          text: const TextSpan(text: '北极', style: TextStyle(fontSize: 11, color: Color(0xFFFF6B35))),
          textDirection: TextDirection.ltr,
        );
        textPainter.layout();
        textPainter.paint(canvas, Offset(center.dx + offsetX + 10, topY - 5));
      }
    }

    // 绘制晨昏线（昼夜分界）
    final sunAngle = animProgress * 2 * math.pi;
    final terminatorX = center.dx + math.cos(sunAngle) * radius * 0.3;
    
    // 昼半球高光
    final dayPaint = Paint()
      ..color = Colors.yellow.withOpacity(0.15)
      ..style = PaintingStyle.fill;
    canvas.drawCircle(
      Offset(terminatorX + math.cos(sunAngle) * radius * 0.3, center.dy),
      radius * 0.9,
      dayPaint,
    );

    // 标注直射点
    if (showLabels) {
      final subsolarX = center.dx + math.cos(sunAngle) * radius;
      final subsolarY = center.dy + math.sin(sunAngle * 0.3) * radius * 0.5;
      
      // 太阳光线
      canvas.drawLine(
        Offset(subsolarX + math.cos(sunAngle) * 20, subsolarY + math.sin(sunAngle) * 20),
        Offset(subsolarX + math.cos(sunAngle) * 50, subsolarY + math.sin(sunAngle) * 50),
        Paint()
          ..color = const Color(0xFFFFC24B)
          ..strokeWidth = 2
          ..style = PaintingStyle.stroke,
      );

      final textPainter = TextPainter(
        text: const TextSpan(text: '直射点', style: TextStyle(fontSize: 10, color: Color(0xFFFFC24B))),
        textDirection: TextDirection.ltr,
      );
      textPainter.layout();
      textPainter.paint(canvas, Offset(subsolarX + 15, subsolarY - 10));
    }
  }

  @override
  bool shouldRepaint(covariant _RotationPainter oldDelegate) {
    return oldDelegate.animProgress != animProgress ||
        oldDelegate.showLabels != showLabels ||
        oldDelegate.showTilt != showTilt;
  }
}
