import 'dart:math' as math;
import 'package:flutter/material.dart';
import 'base.dart';
import 'contour_model.dart';

/// 等高线模型 Flutter 实现
class ContourModelFlutter implements GcModel {
  @override
  final String id = 'contour-map-flutter';

  @override
  String name() => '等高线地形图 (Flutter)';

  @override
  String description() => '纯 Flutter 实现的等高线地形模型，支持五大地形切换';

  @override
  Widget buildView(BuildContext context) {
    return const _ContourModelView();
  }
}

class _ContourModelView extends StatefulWidget {
  const _ContourModelView();

  @override
  State<_ContourModelView> createState() => _ContourModelViewState();
}

class _ContourModelViewState extends State<_ContourModelView>
    with TickerProviderStateMixin {
  late AnimationController _animController;
  TerrainKind _selectedTerrain = TerrainKind.mountain;
  bool _isPlaying = false;
  bool _colored = false;
  bool _isTopView = false;
  double _progress = 0;

  final List<TerrainKind> _terrains = [
    TerrainKind.plain,
    TerrainKind.hill,
    TerrainKind.mountain,
    TerrainKind.plateau,
    TerrainKind.basin,
  ];

  @override
  void initState() {
    super.initState();
    _animController = AnimationController(
      vsync: this,
      duration: const Duration(seconds: 8),
    );
  }

  @override
  void dispose() {
    _animController.dispose();
    super.dispose();
  }

  void _togglePlay() {
    setState(() {
      _isPlaying = !_isPlaying;
    });
    if (_isPlaying) {
      _animController.repeat();
    } else {
      _animController.stop();
    }
  }

  void _reset() {
    setState(() {
      _isPlaying = false;
      _progress = 0;
      _colored = false;
      _isTopView = false;
    });
    _animController.stop();
  }

  void _selectTerrain(TerrainKind terrain) {
    setState(() {
      _selectedTerrain = terrain;
    });
  }

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final bgColor = isDark ? const Color(0xFF0B1524) : const Color(0xFFE8F0FE);
    final panelColor = isDark ? const Color(0xFF101E31) : const Color(0xFFF5F7FA);
    final inkColor = isDark ? const Color(0xFFEAF2FF) : const Color(0xFF111A2E);
    final mutedColor = isDark ? const Color(0xFF93A8C6) : const Color(0xFF5C6A80);

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
                  '等高线地形 · 五大地形',
                  style: TextStyle(
                    fontSize: 16,
                    fontWeight: FontWeight.w600,
                    color: inkColor,
                  ),
                ),
                const Spacer(),
                TextButton.icon(
                  onPressed: _reset,
                  icon: const Icon(Icons.refresh_rounded, size: 16),
                  label: const Text('重置'),
                ),
              ],
            ),
          ),
          // 五大地形切换
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            color: panelColor,
            child: Row(
              children: [
                Text(
                  '五大地形：',
                  style: TextStyle(fontSize: 12, color: mutedColor),
                ),
                const SizedBox(width: 8),
                ..._terrains.map((t) => _terrainChip(t, _selectedTerrain == t, () => _selectTerrain(t))),
              ],
            ),
          ),
          // 画布区域
          Expanded(
            child: _ContourCanvas(
              terrain: _selectedTerrain,
              isPlaying: _isPlaying,
              progress: _progress,
              colored: _colored,
              isTopView: _isTopView,
              animProgress: _animController.value,
            ),
          ),
          // 底部控制栏
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
            color: panelColor,
            child: Row(
              children: [
                _controlBtn(
                  icon: _isPlaying ? Icons.pause_rounded : Icons.play_arrow_rounded,
                  label: _isPlaying ? '暂停' : '播放',
                  color: const Color(0xFF4D8CFF),
                  onPressed: _togglePlay,
                ),
                const SizedBox(width: 8),
                _controlBtn(
                  icon: Icons.map_rounded,
                  label: _isTopView ? '立体图' : '俯视图',
                  color: panelColor,
                  onPressed: () => setState(() => _isTopView = !_isTopView),
                ),
                const SizedBox(width: 8),
                _controlBtn(
                  icon: Icons.palette_rounded,
                  label: _colored ? '取消上色' : '上色',
                  color: _colored ? const Color(0xFF4D8CFF) : panelColor,
                  onPressed: () => setState(() => _colored = !_colored),
                ),
                const Spacer(),
                Expanded(
                  child: LinearProgressIndicator(
                    value: _progress,
                    minHeight: 4,
                    backgroundColor: isDark ? const Color(0xFF1E3350) : const Color(0xFFE3E8F3),
                    valueColor: const AlwaysStoppedAnimation<Color>(Color(0xFF4D8CFF)),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _terrainChip(TerrainKind terrain, bool selected, VoidCallback onTap) {
    final names = {
      TerrainKind.plain: '平原',
      TerrainKind.hill: '丘陵',
      TerrainKind.mountain: '山地',
      TerrainKind.plateau: '高原',
      TerrainKind.basin: '盆地',
    };
    return Padding(
      padding: const EdgeInsets.only(right: 6),
      child: FilterChip(
        label: Text(names[terrain] ?? ''),
        selected: selected,
        onSelected: (_) => onTap(),
        selectedColor: const Color(0xFF4D8CFF).withOpacity(0.2),
        checkmarkColor: const Color(0xFF4D8CFF),
      ),
    );
  }

  Widget _controlBtn({
    required IconData icon,
    required String label,
    required Color color,
    required VoidCallback onPressed,
  }) {
    return TextButton.icon(
      onPressed: onPressed,
      icon: Icon(icon, size: 16, color: color),
      label: Text(label, style: TextStyle(fontSize: 12, color: color)),
    );
  }
}

/// 等高线画布
class _ContourCanvas extends StatelessWidget {
  final TerrainKind terrain;
  final bool isPlaying;
  final double progress;
  final bool colored;
  final bool isTopView;
  final double animProgress;

  const _ContourCanvas({
    required this.terrain,
    required this.isPlaying,
    required this.progress,
    required this.colored,
    required this.isTopView,
    required this.animProgress,
  });

  @override
  Widget build(BuildContext context) {
    return CustomPaint(
      size: Size.infinite,
      painter: _ContourPainter(
        terrain: terrain,
        isPlaying: isPlaying,
        progress: progress,
        colored: colored,
        isTopView: isTopView,
        animProgress: animProgress,
      ),
    );
  }
}

class _ContourPainter extends CustomPainter {
  final TerrainKind terrain;
  final bool isPlaying;
  final double progress;
  final bool colored;
  final bool isTopView;
  final double animProgress;

  _ContourPainter({
    required this.terrain,
    required this.isPlaying,
    required this.progress,
    required this.colored,
    required this.isTopView,
    required this.animProgress,
  });

  @override
  void paint(Canvas canvas, Size size) {
    final bgColor = const Color(0xFF08111E);
    final lineColor = const Color(0xFF4D8CFF);
    final fillColor = const Color(0xFF1C5FA8);

    canvas.drawColor(bgColor, BlendMode.src);

    // 绘制等高线
    final center = Offset(size.width / 2, size.height / 2);
    final radius = math.min(size.width, size.height) * 0.35;

    int contourCount;
    double maxHeight;

    switch (terrain) {
      case TerrainKind.plain:
        contourCount = 3;
        maxHeight = 60;
        break;
      case TerrainKind.hill:
        contourCount = 8;
        maxHeight = 180;
        break;
      case TerrainKind.mountain:
        contourCount = 12;
        maxHeight = 1200;
        break;
      case TerrainKind.plateau:
        contourCount = 10;
        maxHeight = 1250;
        break;
      case TerrainKind.basin:
        contourCount = 10;
        maxHeight = 1060;
        break;
    }

    final interval = maxHeight / contourCount;

    for (int i = 0; i < contourCount; i++) {
      final ratio = (i + 1) / contourCount;
      final progressRatio = isPlaying ? animProgress : progress;

      if (ratio > progressRatio) continue;

      final alpha = colored ? (1 - ratio).clamp(0.2, 1.0) : 1.0;
      final paint = Paint()
        ..color = colored
            ? fillColor.withOpacity(alpha * 0.3)
            : lineColor.withOpacity(alpha)
        ..style = colored ? PaintingStyle.fill : PaintingStyle.stroke
        ..strokeWidth = 1.5;

      Path path;
      if (terrain == TerrainKind.basin) {
        final r = radius * ratio;
        path = Path()..addOval(Rect.fromCircle(center: center, radius: r));
      } else if (terrain == TerrainKind.plateau) {
        final r = ratio < 0.5
            ? radius * 0.5
            : radius * (0.5 + (ratio - 0.5) * 0.8);
        path = Path()..addOval(Rect.fromCircle(center: center, radius: r));
      } else {
        final spacing = terrain == TerrainKind.plain ? 0.15 : (terrain == TerrainKind.hill ? 0.1 : 0.08);
        final r = radius * (1 - ratio * (1 - spacing));
        path = Path()..addOval(Rect.fromCircle(center: center, radius: r));
      }

      canvas.drawPath(path, paint);
    }

    // 绘制山峰标记
    if (!isTopView && terrain != TerrainKind.plain) {
      final peakPaint = Paint()
        ..color = const Color(0xFFFFD166)
        ..style = PaintingStyle.fill;
      canvas.drawCircle(center, radius * 0.08, peakPaint);

      final textPainter = TextPainter(
        text: TextSpan(
          text: '${maxHeight.toInt()}m',
          style: const TextStyle(fontSize: 11, color: Color(0xFFEAF2FF)),
        ),
        textDirection: TextDirection.ltr,
      );
      textPainter.layout();
      textPainter.paint(canvas, Offset(center.dx + 15, center.dy - 20));
    }
  }

  @override
  bool shouldRepaint(covariant _ContourPainter oldDelegate) {
    return oldDelegate.terrain != terrain ||
        oldDelegate.isPlaying != isPlaying ||
        oldDelegate.progress != progress ||
        oldDelegate.colored != colored ||
        oldDelegate.isTopView != isTopView ||
        oldDelegate.animProgress != animProgress;
  }
}
