import 'dart:math' as math;
import 'package:flutter/material.dart';
import 'base.dart';
import 'revolution_model.dart';

/// 地球公转模型 Flutter 实现
class RevolutionModelFlutter implements GcModel {
  @override
  final String id = 'earth-revolution-flutter';

  @override
  String name() => '地球公转 (Flutter)';

  @override
  String description() => '纯 Flutter 实现的地球公转模型，观察四季成因';

  @override
  Widget buildView(BuildContext context) {
    return const _RevolutionModelView();
  }

  List<Season> get defaultSeasons => [
    Season(key: 'spring', date: '3/21', declinationDeg: 0, angleDeg: 0,
      teachingZh: '太阳直射赤道，全球昼夜平分。', teachingEn: 'Sun overhead at equator.'),
    Season(key: 'summer', date: '6/22', declinationDeg: 23.5, angleDeg: 90,
      teachingZh: '太阳直射北回归线，北半球昼最长。', teachingEn: 'Sun at Tropic of Cancer.'),
    Season(key: 'autumn', date: '9/23', declinationDeg: 0, angleDeg: 180,
      teachingZh: '太阳再次直射赤道，昼夜平分。', teachingEn: 'Sun back at equator.'),
    Season(key: 'winter', date: '12/22', declinationDeg: -23.5, angleDeg: 270,
      teachingZh: '太阳直射南回归线，北半球昼最短。', teachingEn: 'Sun at Tropic of Capricorn.'),
  ];
}

class _RevolutionModelView extends StatefulWidget {
  const _RevolutionModelView();

  @override
  State<_RevolutionModelView> createState() => _RevolutionModelViewState();
}

class _RevolutionModelViewState extends State<_RevolutionModelView>
    with TickerProviderStateMixin {
  late AnimationController _orbitController;
  bool _isPlaying = true;
  double _speed = 1.0;
  int _selectedSeason = 0;

  @override
  void initState() {
    super.initState();
    _orbitController = AnimationController(
      vsync: this,
      duration: const Duration(seconds: 12),
    )..repeat();
  }

  @override
  void dispose() {
    _orbitController.dispose();
    super.dispose();
  }

  void _togglePlay() {
    setState(() => _isPlaying = !_isPlaying);
    if (_isPlaying) _orbitController.repeat();
    else _orbitController.stop();
  }

  void _reset() {
    setState(() {
      _isPlaying = true;
      _speed = 1.0;
      _selectedSeason = 0;
    });
    _orbitController.repeat();
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
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 12),
            color: panelColor,
            child: Row(
              children: [
                Text('地球公转 · 四季成因', style: TextStyle(fontSize: 16, fontWeight: FontWeight.w600, color: inkColor)),
                const Spacer(),
                ...['春分', '夏至', '秋分', '冬至'].asMap().entries.map((e) => 
                  Padding(padding: const EdgeInsets.only(left: 6),
                    child: FilterChip(
                      label: Text(e.value, style: const TextStyle(fontSize: 11)),
                      selected: _selectedSeason == e.key,
                      onSelected: (_) => setState(() => _selectedSeason = e.key),
                      selectedColor: const Color(0xFF4D8CFF).withOpacity(0.2),
                    ),
                  ),
                ),
              ],
            ),
          ),
          Expanded(
            child: _RevolutionOrbit(
              progress: _orbitController.value,
              selectedSeason: _selectedSeason,
            ),
          ),
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
            color: panelColor,
            child: Row(
              children: [
                IconButton(onPressed: _togglePlay, icon: Icon(_isPlaying ? Icons.pause_rounded : Icons.play_arrow_rounded), color: const Color(0xFF4D8CFF)),
                const SizedBox(width: 16),
                Text('速度:', style: TextStyle(fontSize: 12, color: isDark ? const Color(0xFF93A8C6) : const Color(0xFF5C6A80))),
                const SizedBox(width: 8),
                Expanded(child: Slider(value: _speed, min: 0.1, max: 3.0, divisions: 29, onChanged: (v) => setState(() => _speed = v))),
                Text('${_speed.toStringAsFixed(1)}x'),
                const Spacer(),
                TextButton.icon(onPressed: _reset, icon: const Icon(Icons.refresh_rounded, size: 16), label: const Text('重置')),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _RevolutionOrbit extends StatelessWidget {
  final double progress;
  final int selectedSeason;

  const _RevolutionOrbit({required this.progress, required this.selectedSeason});

  @override
  Widget build(BuildContext context) {
    return CustomPaint(
      size: Size.infinite,
      painter: _RevolutionPainter(progress: progress, selectedSeason: selectedSeason),
    );
  }
}

class _RevolutionPainter extends CustomPainter {
  final double progress;
  final int selectedSeason;

  _RevolutionPainter({required this.progress, required this.selectedSeason});

  @override
  void paint(Canvas canvas, Size size) {
    final bgColor = const Color(0xFF08111E);
    final sunColor = const Color(0xFFFFC24B);
    final orbitColor = const Color(0xFF4D8CFF);
    final earthColor = const Color(0xFF1C5FA8);

    canvas.drawColor(bgColor, BlendMode.src);

    final center = Offset(size.width / 2, size.height / 2);
    final orbitRadius = math.min(size.width, size.height) * 0.35;

    // 绘制轨道
    canvas.drawCircle(center, orbitRadius, Paint()
      ..color = orbitColor.withOpacity(0.3)
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.5
      
          ..style = PaintingStyle.stroke);

    // 绘制太阳
    canvas.drawCircle(center, 30, Paint()
      ..color = sunColor
      ..style = PaintingStyle.fill);
    
    // 太阳发光效果
    for (int i = 4; i > 0; i--) {
      canvas.drawCircle(center, 30 + i * 12, Paint()
        ..color = sunColor.withOpacity(0.1 * (5 - i))
        ..style = PaintingStyle.fill);
    }

    // 绘制地球位置（根据进度）
    final angle = progress * 2 * math.pi;
    final earthX = center.dx + math.cos(angle) * orbitRadius;
    final earthY = center.dy + math.sin(angle) * orbitRadius;

    // 地球
    canvas.drawCircle(Offset(earthX, earthY), 15, Paint()
      ..color = earthColor
      ..style = PaintingStyle.fill);

    // 地轴（倾斜23.5度）
    final tiltAngle = 23.5 * math.pi / 180;
    canvas.drawLine(
      Offset(earthX - math.sin(tiltAngle) * 25, earthY - math.cos(tiltAngle) * 25),
      Offset(earthX + math.sin(tiltAngle) * 25, earthY + math.cos(tiltAngle) * 25),
      Paint()
        ..color = const Color(0xFFFF6B35)
        ..strokeWidth = 2
        ..style = PaintingStyle.stroke);

    // 标注四个关键位置
    final seasonLabels = ['春分', '夏至', '秋分', '冬至'];
    final seasonColors = [const Color(0xFF6BCB77), const Color(0xFFFFD93D), const Color(0xFF4D8CFF), const Color(0xFF8B5CF6)];
    
    for (int i = 0; i < 4; i++) {
      final seasonAngle = i * math.pi / 2;
      final sx = center.dx + math.cos(seasonAngle) * orbitRadius;
      final sy = center.dy + math.sin(seasonAngle) * orbitRadius;

      // 小圆点标记
      canvas.drawCircle(Offset(sx, sy), 5, Paint()
        ..color = seasonColors[i]
        ..style = PaintingStyle.fill);

      // 标签
      final labelX = sx + math.cos(seasonAngle) * 25;
      final labelY = sy + math.sin(seasonAngle) * 25;
      final textPainter = TextPainter(
        text: TextSpan(text: seasonLabels[i], style: const TextStyle(fontSize: 10, color: Color(0xFFEAF2FF))),
        textDirection: TextDirection.ltr,
      );
      textPainter.layout();
      textPainter.paint(canvas, Offset(labelX - 15, labelY - 8));
    }

    // 当前选中季节高亮
    if (selectedSeason >= 0 && selectedSeason < 4) {
      final selAngle = selectedSeason * math.pi / 2;
      final selX = center.dx + math.cos(selAngle) * orbitRadius;
      final selY = center.dy + math.sin(selAngle) * orbitRadius;
      
      canvas.drawCircle(Offset(selX, selY), 8, Paint()
        ..color = seasonColors[selectedSeason].withOpacity(0.5)
        ..style = PaintingStyle.stroke
        ..strokeWidth = 2);
    }
  }

  @override
  bool shouldRepaint(covariant _RevolutionPainter oldDelegate) {
    return oldDelegate.progress != progress || oldDelegate.selectedSeason != selectedSeason;
  }
}
