import 'dart:math';

import 'package:flutter/material.dart';

import '../controller.dart';
import '../theme.dart';
import '../i18n.dart';

/// 百宝箱页：今日人品、随机运势等小玩意。
class TreasurePage extends StatefulWidget {
  const TreasurePage({super.key, required this.controller});

  final ShellController controller;

  @override
  State<TreasurePage> createState() => _TreasurePageState();
}

class _TreasurePageState extends State<TreasurePage> {
  String _fortune = '';
  String _luckyColor = '';
  String _luckyNumber = '';
  bool _loading = false;
  final Random _rand = Random();

  @override
  void initState() {
    super.initState();
    _refresh();
  }

  void _refresh() {
    setState(() => _loading = true);
    // 模拟计算延迟
    Future.delayed(const Duration(milliseconds: 400), () {
      if (!mounted) return;
      final fortunes = <String>[
        '今日宜：尝试新事物，突破舒适区。',
        '运气不错，适合做重要的决定。',
        '财运旺盛，可能会有意外收获。',
        '人际和谐，适合合作与沟通。',
        '专注当下，好运自然来。',
        '保持乐观，今天会有好消息。',
        '灵感涌现，适合创作和思考。',
        '脚踏实地，一步一个脚印。',
        '贵人运旺，多与他人交流。',
        '健康第一，注意休息和锻炼。',
      ];
      final colors = <String>[
        '红色', '蓝色', '绿色', '金色', '紫色', '橙色', '粉色', '青色',
      ];
      setState(() {
        _fortune = fortunes[_rand.nextInt(fortunes.length)];
        _luckyColor = colors[_rand.nextInt(colors.length)];
        _luckyNumber = '${(_rand.nextInt(99)) + 1}';
        _loading = false;
      });
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: GcColors.appBg,
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : Center(
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 480),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: <Widget>[
                    // 今日人品卡片
                    GcCard(
                      padding: const EdgeInsets.all(GcSpace.xxl),
                      child: Column(
                        children: <Widget>[
                          Icon(
                            Icons.star_rounded,
                            size: 48,
                            color: GcColors.accent,
                          ),
                          const SizedBox(height: GcSpace.lg),
                          Text(
                            T('treasure.fortuneTitle'),
                            style: const TextStyle(
                              fontSize: 20,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                          const SizedBox(height: GcSpace.md),
                          Text(
                            _fortune,
                            textAlign: TextAlign.center,
                            style: TextStyle(
                              fontSize: 15,
                              height: 1.5,
                              color: GcColors.muted,
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: GcSpace.lg),
                    // 幸运信息
                    Wrap(
                      spacing: GcSpace.md,
                      runSpacing: GcSpace.md,
                      children: <Widget>[
                        _buildStatCard(
                          icon: Icons.check_circle_outline_rounded,
                          label: T('treasure.luckyColor'),
                          value: _luckyColor,
                        ),
                        _buildStatCard(
                          icon: Icons.numbers_rounded,
                          label: T('treasure.luckyNumber'),
                          value: _luckyNumber,
                        ),
                      ],
                    ),
                    const SizedBox(height: GcSpace.xl),
                    // 刷新按钮
                    FilledButton.icon(
                      onPressed: _refresh,
                      icon: const Icon(Icons.refresh_rounded, size: 18),
                      label: Text(T('treasure.refresh')),
                    ),
                  ],
                ),
              ),
            ),
    );
  }

  Widget _buildStatCard({
    required IconData icon,
    required String label,
    required String value,
  }) {
    return GcCard(
      padding: const EdgeInsets.all(GcSpace.lg),
      child: Column(
        children: <Widget>[
          Icon(icon, size: 28, color: GcColors.accent),
          const SizedBox(height: GcSpace.sm),
          Text(
            label,
            style: TextStyle(fontSize: 12, color: GcColors.muted),
          ),
          const SizedBox(height: GcSpace.xs),
          Text(
            value,
            style: const TextStyle(
              fontSize: 20,
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
      ),
    );
  }
}
