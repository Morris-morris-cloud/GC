import 'dart:math' as math;
import 'package:flutter/material.dart';
import 'base.dart';
import 'geography_model.dart';

/// 地球地理模型 Flutter 实现
class GeographyModelFlutter implements GcModel {
  @override
  final String id = 'earth-geography-flutter';

  @override
  String name() => '地球地理 (Flutter)';

  @override
  String description() => '纯 Flutter 实现的地球地理模型，支持五带划分、七大洲四大洋';

  @override
  Widget buildView(BuildContext context) {
    return const _GeographyModelView();
  }

  /// 示例数据：五带
  List<GeoZone> get defaultZones => [
    GeoZone(
      id: 'torrid',
      nameZh: '热带',
      nameEn: 'Torrid Zone',
      latMin: -23.5,
      latMax: 23.5,
      descZh: '南北回归线之间，有太阳直射，终年高温。',
      descEn: 'Between the tropics; the Sun is overhead at some time each year.',
    ),
    GeoZone(
      id: 'north-temperate',
      nameZh: '北温带',
      nameEn: 'North Temperate',
      latMin: 23.5,
      latMax: 66.5,
      descZh: '北回归线与北极圈之间，四季分明。',
      descEn: 'Between Tropic of Cancer and Arctic Circle; four distinct seasons.',
    ),
    GeoZone(
      id: 'south-temperate',
      nameZh: '南温带',
      nameEn: 'South Temperate',
      latMin: -66.5,
      latMax: -23.5,
      descZh: '南回归线与南极圈之间，四季分明。',
      descEn: 'Between Tropic of Capricorn and Antarctic Circle; four seasons.',
    ),
    GeoZone(
      id: 'north-frigid',
      nameZh: '北寒带',
      nameEn: 'North Frigid',
      latMin: 66.5,
      latMax: 90,
      descZh: '北极圈以北，终年寒冷，有极昼极夜。',
      descEn: 'Beyond the Arctic Circle; perpetually cold with polar day/night.',
    ),
    GeoZone(
      id: 'south-frigid',
      nameZh: '南寒带',
      nameEn: 'South Frigid',
      latMin: -90,
      latMax: -66.5,
      descZh: '南极圈以南，终年寒冷，有极昼极夜。',
      descEn: 'Beyond the Antarctic Circle; perpetually cold with polar day/night.',
    ),
  ];

  /// 示例数据：五大洲简化轮廓
  List<Continent> get defaultContinents => [
    Continent(
      nameZh: '亚洲',
      nameEn: 'Asia',
      points: [
        [60, 70], [100, 70], [140, 65], [180, 60],
        [180, 40], [140, 20], [120, 10], [100, 5],
        [80, 10], [60, 20], [50, 30], [40, 40],
      ],
    ),
    Continent(
      nameZh: '非洲',
      nameEn: 'Africa',
      points: [
        [35, 35], [50, 35], [50, 15], [40, 5],
        [30, -5], [20, -20], [15, -30], [20, -35],
        [30, -35], [35, -25], [40, -10], [35, 5],
      ],
    ),
    Continent(
      nameZh: '欧洲',
      nameEn: 'Europe',
      points: [
        [10, 70], [40, 70], [50, 60], [40, 50],
        [30, 45], [20, 45], [10, 50], [0, 55],
      ],
    ),
    Continent(
      nameZh: '北美洲',
      nameEn: 'North America',
      points: [
        [-170, 70], [-130, 70], [-80, 70], [-60, 50],
        [-60, 30], [-80, 20], [-100, 20], [-120, 30],
        [-160, 50], [-170, 60],
      ],
    ),
    Continent(
      nameZh: '南美洲',
      nameEn: 'South America',
      points: [
        [-80, 10], [-60, 10], [-35, -5], [-35, -20],
        [-50, -30], [-65, -50], [-75, -55], [-75, -40],
        [-70, -20], [-80, 0],
      ],
    ),
    Continent(
      nameZh: '大洋洲',
      nameEn: 'Oceania',
      points: [
        [115, -15], [150, -15], [160, -25], [150, -35],
        [130, -38], [115, -35], [113, -25],
      ],
    ),
    Continent(
      nameZh: '南极洲',
      nameEn: 'Antarctica',
      points: [
        [-180, -65], [-90, -65], [0, -65], [90, -65],
        [180, -65], [180, -90], [-180, -90],
      ],
    ),
  ];
}

class _GeographyModelView extends StatefulWidget {
  const _GeographyModelView();

  @override
  State<_GeographyModelView> createState() => _GeographyModelViewState();
}

class _GeographyModelViewState extends State<_GeographyModelView> {
  String _activeView = 'zones'; // zones, coast, plates
  bool _showZones = true;
  bool _showCoast = true;
  bool _showPlates = false;

  final GeographyModelFlutter _model = GeographyModelFlutter();

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
                  '地球地理 · 五带与七大洲',
                  style: TextStyle(
                    fontSize: 16,
                    fontWeight: FontWeight.w600,
                    color: inkColor,
                  ),
                ),
                const Spacer(),
                _viewButton('五带', _activeView == 'zones', () => setState(() => _activeView = 'zones')),
                _viewButton('大洲', _activeView == 'coast', () => setState(() => _activeView = 'coast')),
                TextButton.icon(
                  onPressed: () => setState(() {
                    _showZones = !_showZones;
                    _showCoast = !_showCoast;
                  }),
                  icon: const Icon(Icons.tune_rounded, size: 16),
                  label: const Text('图层'),
                ),
              ],
            ),
          ),
          // 地图区域
          Expanded(
            child: _GeographyMap(
              zones: _model.defaultZones,
              continents: _model.defaultContinents,
              showZones: _showZones,
              showCoast: _showCoast,
              activeView: _activeView,
            ),
          ),
          // 底部说明
          if (_activeView == 'zones' && _showZones)
            Container(
              padding: const EdgeInsets.all(12),
              color: panelColor,
              child: Text(
                '热带：南北回归线之间 | 温带：回归线与极圈之间 | 寒带：极圈以内',
                style: TextStyle(fontSize: 12, color: isDark ? const Color(0xFF93A8C6) : const Color(0xFF5C6A80)),
                textAlign: TextAlign.center,
              ),
            ),
        ],
      ),
    );
  }

  Widget _viewButton(String label, bool active, VoidCallback onTap) {
    return TextButton(
      onPressed: onTap,
      style: TextButton.styleFrom(
        backgroundColor: active ? const Color(0xFF4D8CFF) : null,
        foregroundColor: active ? Colors.white : null,
      ),
      child: Text(label, style: const TextStyle(fontSize: 12)),
    );
  }
}

class _GeographyMap extends StatelessWidget {
  final List<GeoZone> zones;
  final List<Continent> continents;
  final bool showZones;
  final bool showCoast;
  final String activeView;

  const _GeographyMap({
    required this.zones,
    required this.continents,
    required this.showZones,
    required this.showCoast,
    required this.activeView,
  });

  @override
  Widget build(BuildContext context) {
    return CustomPaint(
      size: Size.infinite,
      painter: _GeographyPainter(
        zones: zones,
        continents: continents,
        showZones: showZones,
        showCoast: showCoast,
        activeView: activeView,
      ),
    );
  }
}

class _GeographyPainter extends CustomPainter {
  final List<GeoZone> zones;
  final List<Continent> continents;
  final bool showZones;
  final bool showCoast;
  final String activeView;

  _GeographyPainter({
    required this.zones,
    required this.continents,
    required this.showZones,
    required this.showCoast,
    required this.activeView,
  });

  /// 经纬度转画布坐标
  Offset _latLonToOffset(double lon, double lat, Size size) {
    final x = (lon + 180) / 360 * size.width;
    final y = (90 - lat) / 180 * size.height;
    return Offset(x, y);
  }

  @override
  void paint(Canvas canvas, Size size) {
    final bgColor = const Color(0xFF0E2A4A);
    final oceanColor = const Color(0xFF0E2A4A);
    final zoneColors = {
      'torrid': const Color(0xFFFF6B35),
      'north-temperate': const Color(0xFFFFD93D),
      'south-temperate': const Color(0xFF6BCB77),
      'north-frigid': const Color(0xFF4D8CFF),
      'south-frigid': const Color(0xFF8B5CF6),
    };

    // 海洋背景
    canvas.drawColor(oceanColor, BlendMode.src);

    // 绘制五带
    if (showZones && activeView == 'zones') {
      final bandHeight = size.height / 9; // 180度纬度分9份
      final bandColors = [
        zoneColors['north-frigid']!,
        zoneColors['north-temperate']!,
        zoneColors['torrid']!,
        zoneColors['south-temperate']!,
        zoneColors['south-frigid']!,
      ];
      final bandLatRanges = [
        (66.5, 90),
        (23.5, 66.5),
        (-23.5, 23.5),
        (-66.5, -23.5),
        (-90, -66.5),
      ];

      for (int i = 0; i < 5; i++) {
        final (latMin, latMax) = bandLatRanges[i];
        final y1 = (90 - latMax) / 180 * size.height;
        final y2 = (90 - latMin) / 180 * size.height;
        canvas.drawRect(
          Rect.fromLTWH(0, y1, size.width, y2 - y1),
          Paint()..color = bandColors[i].withOpacity(0.3),
        );
      }

      // 绘制回归线和极圈
      for (final lat in [23.5, -23.5, 66.5, -66.5]) {
        final y = (90 - lat) / 180 * size.height;
        canvas.drawLine(
          Offset(0, y),
          Offset(size.width, y),
          Paint()
            ..color = Colors.white.withOpacity(0.4)
            ..strokeWidth = 1
            ..style = PaintingStyle.stroke
            ..style = PaintingStyle.stroke,
        );
      }
    }

    // 绘制大陆轮廓
    if (showCoast && activeView == 'coast') {
      for (final continent in continents) {
        if (continent.points.length < 2) continue;

        final path = Path();
        final start = _latLonToOffset(continent.points[0][0], continent.points[0][1], size);
        path.moveTo(start.dx, start.dy);

        for (int i = 1; i < continent.points.length; i++) {
          final p = _latLonToOffset(continent.points[i][0], continent.points[i][1], size);
          path.lineTo(p.dx, p.dy);
        }
        path.close();

        canvas.drawPath(
          path,
          Paint()
            ..color = const Color(0xFF4E8C4C)
            ..style = PaintingStyle.fill,
        );
        canvas.drawPath(
          path,
          Paint()
            ..color = Colors.white.withOpacity(0.6)
            ..style = PaintingStyle.stroke
            ..strokeWidth = 1,
        );
      }
    }
  }

  @override
  bool shouldRepaint(covariant _GeographyPainter oldDelegate) {
    return oldDelegate.showZones != showZones ||
        oldDelegate.showCoast != showCoast ||
        oldDelegate.activeView != activeView;
  }
}
