import 'package:flutter/material.dart';
import 'base.dart';
import 'scale_model.dart';

class ScaleCalculatorModelFlutter implements GcModel {
  @override
  final String id = 'scale-calculator-flutter';
  @override
  String name() => '比例尺计算器 (Flutter)';
  @override
  String description() => '纯 Flutter 实现的比例尺计算器';
  @override
  Widget buildView(BuildContext context) => const _ScaleCalculatorView();

  List<ScalePreset> get presets => [
    ScalePreset(n: 10000, nameKey: 'preset.campus', noteKey: 'preset.campusNote'),
    ScalePreset(n: 50000, nameKey: 'preset.town', noteKey: 'preset.townNote'),
    ScalePreset(n: 250000, nameKey: 'preset.city', noteKey: 'preset.cityNote'),
    ScalePreset(n: 1000000, nameKey: 'preset.province', noteKey: 'preset.provinceNote'),
    ScalePreset(n: 10000000, nameKey: 'preset.china', noteKey: 'preset.chinaNote'),
    ScalePreset(n: 100000000, nameKey: 'preset.world', noteKey: 'preset.worldNote'),
  ];

  List<ScaleSpectrum> get spectrum => [
    ScaleSpectrum(n: 10000, labelKey: 'spec.campus', scopeKey: 'spec.campusScope'),
    ScaleSpectrum(n: 250000, labelKey: 'spec.city', scopeKey: 'spec.cityScope'),
    ScaleSpectrum(n: 10000000, labelKey: 'spec.china', scopeKey: 'spec.chinaScope'),
    ScaleSpectrum(n: 100000000, labelKey: 'spec.world', scopeKey: 'spec.worldScope'),
  ];

  List<Landmark> get landmarks => [
    Landmark(nameKey: 'lm.classroom', meters: 10),
    Landmark(nameKey: 'lm.track', meters: 400),
    Landmark(nameKey: 'lm.marathon', meters: 42195),
    Landmark(nameKey: 'lm.beijingShanghai', meters: 1080000),
    Landmark(nameKey: 'lm.equator', meters: 40000000),
  ];
}

class _ScaleCalculatorView extends StatefulWidget {
  const _ScaleCalculatorView();
  @override
  State<_ScaleCalculatorView> createState() => _ScaleCalculatorViewState();
}

class _ScaleCalculatorViewState extends State<_ScaleCalculatorView> {
  String _mode = 'real';
  double _mapDist = 10;
  double _realDist = 100000;
  double _scaleN = 10000.0;
  String _unit = 'cm';

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
                Text('比例尺计算器', style: TextStyle(fontSize: 16, fontWeight: FontWeight.w600, color: inkColor)),
                const Spacer(),
                _modeBtn('求实地距离', _mode == 'real'),
                _modeBtn('求图上距离', _mode == 'map'),
                _modeBtn('求比例尺', _mode == 'scale'),
              ],
            ),
          ),
          Expanded(
            child: _ScaleCanvas(
              mode: _mode,
              mapDist: _mapDist,
              realDist: _realDist,
              scaleN: _scaleN,
              unit: _unit,
            ),
          ),
          Container(
            padding: const EdgeInsets.all(16),
            color: panelColor,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('已知量:', style: TextStyle(fontSize: 12, color: isDark ? const Color(0xFF93A8C6) : const Color(0xFF5C6A80))),
                const SizedBox(height: 8),
                Row(
                  children: [
                    Expanded(
                      child: TextField(
                        decoration: const InputDecoration(labelText: '图上距离 (cm)'),
                        keyboardType: TextInputType.number,
                        onChanged: (v) => setState(() => _mapDist = double.tryParse(v) ?? 0),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: TextField(
                        decoration: const InputDecoration(labelText: '比例尺 N'),
                        keyboardType: TextInputType.number,
                        onChanged: (v) => setState(() => _scaleN = double.tryParse(v) ?? 10000.0),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 8),
                Row(
                  children: [
                    Text('结果:', style: TextStyle(fontSize: 12, color: isDark ? const Color(0xFF93A8C6) : const Color(0xFF5C6A80))),
                    const SizedBox(width: 8),
                    Text('实地距离 = ${(_mapDist * _scaleN / 100).toStringAsFixed(2)} m', style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w600, color: Color(0xFF4D8CFF))),
                  ],
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _modeBtn(String label, bool active) {
    return Padding(
      padding: const EdgeInsets.only(left: 6),
      child: FilterChip(
        label: Text(label, style: const TextStyle(fontSize: 12)),
        selected: active,
        onSelected: (_) => setState(() => _mode = active ? 'real' : 'real'),
        selectedColor: const Color(0xFF4D8CFF).withOpacity(0.2),
      ),
    );
  }
}

class _ScaleCanvas extends StatelessWidget {
  final String mode;
  final double mapDist;
  final double realDist;
  final double scaleN;
  final String unit;

  const _ScaleCanvas({required this.mode, required this.mapDist, required this.realDist, required this.scaleN, required this.unit});

  @override
  Widget build(BuildContext context) {
    return CustomPaint(
      size: Size.infinite,
      painter: _ScalePainter(mode: mode, mapDist: mapDist, realDist: realDist, scaleN: scaleN, unit: unit),
    );
  }
}

class _ScalePainter extends CustomPainter {
  final String mode;
  final double mapDist;
  final double realDist;
  final double scaleN;
  final String unit;

  _ScalePainter({required this.mode, required this.mapDist, required this.realDist, required this.scaleN, required this.unit});

  @override
  void paint(Canvas canvas, Size size) {
    final isDark = true;
    final bgColor = const Color(0xFF08111E);
    final barColor = const Color(0xFF4D8CFF);
    final inkColor = const Color(0xFFEAF2FF);

    canvas.drawColor(bgColor, BlendMode.src);

    final centerY = size.height / 2;
    final double barHeight = 40.0;
    final barY = centerY - barHeight / 2;
    final maxBarWidth = size.width * 0.8;
    final startX = (size.width - maxBarWidth) / 2;

    // 绘制标尺
    canvas.drawRRect(
      RRect.fromRectAndRadius(Rect.fromLTWH(startX, barY, maxBarWidth, barHeight), Radius.circular(4.0)),
      Paint()..color = barColor.withOpacity(0.2),
    );

    // 刻度
    final double tickCount = 20.0;
    for (int i = 0; i <= tickCount; i++) {
      final x = startX + (maxBarWidth * i / tickCount);
      final double tickHeight = i % 5 == 0 ? 15.0 : 8.0;
      canvas.drawLine(
        Offset(x, barY + (barHeight - tickHeight) / 2),
        Offset(x, barY + (barHeight + tickHeight) / 2),
        Paint()
          ..color = inkColor
          ..strokeWidth = 1,
      );
    }

    // 数值标注
    final textPainter = TextPainter(
      text: TextSpan(text: '1 : $scaleN', style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w600, color: Color(0xFF4D8CFF))),
      textDirection: TextDirection.ltr,
    );
    textPainter.layout();
    textPainter.paint(canvas, Offset(size.width / 2 - 40, barY + barHeight + 16));
  }

  @override
  bool shouldRepaint(covariant _ScalePainter oldDelegate) {
    return oldDelegate.mode != mode || oldDelegate.scaleN != scaleN;
  }
}
