import 'package:flutter/material.dart';

import 'controller.dart';
import 'i18n.dart';
import 'pages.dart';
import 'theme.dart';
import 'window_chrome.dart';

/// 侧边栏导航项。
class _NavItem {
  const _NavItem(this.icon, this.labelKey);

  final IconData icon;
  final String labelKey;
}

const List<_NavItem> _navItems = <_NavItem>[
  _NavItem(Icons.dashboard_rounded, 'nav.home'),
  _NavItem(Icons.terrain_rounded, 'nav.courseware'),
  _NavItem(Icons.download_rounded, 'nav.download'),
  _NavItem(Icons.tune_rounded, 'nav.settings'),
  _NavItem(Icons.info_outline_rounded, 'nav.about'),
  _NavItem(Icons.auto_awesome_rounded, 'nav.treasure'),
  _NavItem(Icons.person_rounded, 'nav.user'),
];

/// 桌面端滚动行为：去掉移动端的拉伸回弹和边缘光晕，滚轮更跟手。
class GcScrollBehavior extends MaterialScrollBehavior {
  const GcScrollBehavior();

  @override
  Widget buildOverscrollIndicator(
    BuildContext context,
    Widget child,
    ScrollableDetails details,
  ) {
    return child;
  }

  @override
  ScrollPhysics getScrollPhysics(BuildContext context) {
    return const ClampingScrollPhysics(parent: AlwaysScrollableScrollPhysics());
  }
}

/// 应用外壳：左侧导航 + 右侧内容 + 底部更新状态条。
class ShellPage extends StatefulWidget {
  const ShellPage({super.key, required this.controller});

  final ShellController controller;

  @override
  State<ShellPage> createState() => _ShellPageState();
}

class _ShellPageState extends State<ShellPage> {
  /// 页面缓存：键为「索引:语言」。同一个页面只构建一次，切走时用
  /// [Offstage] 保活（不参与布局/绘制），切回来无需重建、状态也不丢。
  final Map<String, Widget> _pages = <String, Widget>{};

  @override
  void initState() {
    super.initState();
    widget.controller.bootstrap();
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: widget.controller,
      builder: (context, _) {
        final controller = widget.controller;
        return Scaffold(
          backgroundColor: GcColors.appBg,
          body: Column(
            children: <Widget>[
              // 自绘标题栏（macOS）：系统标题栏已隐藏，最小化/最大化/关闭在这里画
              const GcTitleBar(),
              Expanded(
                child: Row(
                  children: <Widget>[
                    _Sidebar(controller: controller),
                    Container(width: 1, color: GcColors.line),
                    Expanded(
                      child: Column(
                        children: <Widget>[
                          Expanded(child: _content(controller)),
                          if (controller.updateBarVisible)
                            _UpdateBar(controller: controller),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        );
      },
    );
  }

  Widget _content(ShellController controller) {
    // 课件页是纯前端资源，不依赖后端：即使后端未就绪也可打开。
    // 注意 _buildPage 内部对 index 1 返回课件页，这里单独放行它。
    final courseware = controller.index == 1;
    if (!courseware && !controller.backendReady) {
      if (controller.loadError != null) {
        return _BackendDown(controller: controller);
      }
      return const Center(
        child: SizedBox(
          width: 22,
          height: 22,
          child: CircularProgressIndicator(strokeWidth: 2),
        ),
      );
    }

    // 只构建当前页；其余已访问页面保留在树上但处于 Offstage。
    final key = '${controller.index}:${I18n.language.value}';
    _pages.putIfAbsent(key, () => _buildPage(controller, controller.index));

    final visibleKey = key;
    return Stack(
      children: <Widget>[
        for (final entry in _pages.entries)
          Offstage(
            offstage: entry.key != visibleKey,
            child: TickerMode(
              enabled: entry.key == visibleKey,
              child: entry.value,
            ),
          ),
      ],
    );
  }

  Widget _buildPage(ShellController controller, int index) {
    switch (index) {
      case 1:
        // 课件页自持有 WebView 状态，不需要 controller。
        return const CoursewarePage();
      case 2:
        return DownloadPageView(controller: controller);
      case 3:
        return SettingsPageView(controller: controller);
      case 4:
        return AboutPageView(controller: controller);
      case 5:
        return TreasurePage(controller: controller);
      case 6:
        return UserPage(controller: controller);
      default:
        return HomePageView(controller: controller);
    }
  }
}

class _Sidebar extends StatelessWidget {
  const _Sidebar({required this.controller});

  final ShellController controller;

  @override
  Widget build(BuildContext context) {
    final settings = controller.settings;
    return Container(
      width: GcSize.sidebar,
      color: GcColors.sideBg,
      padding: const EdgeInsets.fromLTRB(
          GcSpace.xl, GcSpace.xxl, GcSpace.lg, GcSpace.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          _brand(),
          const SizedBox(height: GcSpace.xxxl),
          const Padding(
            padding: EdgeInsets.only(left: 10, bottom: GcSpace.sm),
            child: Text(
              'MENU',
              style: TextStyle(
                fontSize: 11,
                height: 1.2,
                letterSpacing: 1.4,
                fontWeight: FontWeight.w600,
                color: Color(0xFF5C6A80),
              ),
            ),
          ),
          for (var i = 0; i < _navItems.length; i++)
            _NavButton(
              item: _navItems[i],
              active: controller.index == i,
              onTap: () => controller.goTo(i),
            ),
          const Spacer(),
          Container(height: 1, color: GcColors.line),
          const SizedBox(height: GcSpace.md),
          Padding(
            padding: const EdgeInsets.only(left: 10),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                Text(
                  'v${settings?.version ?? '—'}',
                  style: TextStyle(
                    fontSize: 12,
                    height: 1.4,
                    fontWeight: FontWeight.w600,
                    color: GcColors.muted,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  settings?.platform ?? '—',
                  style: TextStyle(
                    fontSize: 11.5,
                    height: 1.4,
                    color: GcColors.ghost,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  /// 品牌区：一个渐变圆角标 + 名称 + 一句定位。
  Widget _brand() {
    return Row(
      children: <Widget>[
        Container(
          width: 32,
          height: 32,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: GcColors.accentSoft,
            borderRadius: BorderRadius.circular(9),
            border: Border.all(
              color: Color.alphaBlend(
                  GcColors.accent.withOpacity(0.35), GcColors.sideBg),
            ),
          ),
          child: Text(
            '◎',
            style: TextStyle(fontSize: 16, height: 1.1, color: GcColors.accent),
          ),
        ),
        const SizedBox(width: GcSpace.md),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: <Widget>[
              Text(
                'Morris GC',
                style: TextStyle(
                  fontSize: 14,
                  height: 1.3,
                  fontWeight: FontWeight.w600,
                  color: GcColors.ink,
                ),
              ),
              const SizedBox(height: 1),
              Text(
                'SCIENCE · CLASSROOM',
                style: TextStyle(
                  fontSize: 10,
                  height: 1.3,
                  letterSpacing: 0.9,
                  fontWeight: FontWeight.w600,
                  color: GcColors.ghost,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _NavButton extends StatelessWidget {
  const _NavButton({
    required this.item,
    required this.active,
    required this.onTap,
  });

  final _NavItem item;
  final bool active;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: GcSpace.xs),
      child: Material(
        color: active ? GcColors.accentSoft : Colors.transparent,
        borderRadius: BorderRadius.circular(GcRadius.control),
        child: InkWell(
          borderRadius: BorderRadius.circular(GcRadius.control),
          onTap: onTap,
          hoverColor: GcColors.surfaceHover,
          child: Padding(
            padding: const EdgeInsets.symmetric(
              horizontal: GcSpace.md,
              vertical: 11,
            ),
            child: Row(
              children: <Widget>[
                Icon(
                  item.icon,
                  size: 17,
                  color: active ? GcColors.accent : GcColors.faint,
                ),
                const SizedBox(width: GcSpace.md),
                Text(
                  T(item.labelKey),
                  style: TextStyle(
                    fontSize: 13.5,
                    height: 1.2,
                    fontWeight: active ? FontWeight.w600 : FontWeight.w400,
                    color: active ? GcColors.accent : GcColors.muted,
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _UpdateBar extends StatelessWidget {
  const _UpdateBar({required this.controller});

  final ShellController controller;

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(
        color: GcColors.surface,
        border: Border(top: BorderSide(color: GcColors.line)),
      ),
      padding: const EdgeInsets.symmetric(
        horizontal: GcSpace.xxl,
        vertical: GcSpace.md,
      ),
      child: Row(
        children: <Widget>[
          Icon(
            Icons.sync_rounded,
            size: 18,
            color: GcColors.accent,
          ),
          const SizedBox(width: GcSpace.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: <Widget>[
                Text(
                  controller.updateStatus,
                  style: TextStyle(
                    fontSize: 13,
                    height: 1.35,
                    fontWeight: FontWeight.w600,
                    color: GcColors.ink,
                  ),
                  overflow: TextOverflow.ellipsis,
                ),
                if (controller.updateDetail.isNotEmpty)
                  Text(
                    controller.updateDetail,
                    style: TextStyle(
                      fontSize: 12,
                      height: 1.4,
                      color: GcColors.faint,
                    ),
                    overflow: TextOverflow.ellipsis,
                  ),
                if (controller.launcherNotice.isNotEmpty)
                  Text(
                    controller.launcherNotice,
                    style: TextStyle(
                      fontSize: 12,
                      height: 1.4,
                      color: GcColors.warn,
                    ),
                  ),
              ],
            ),
          ),
          if (controller.updateBusy) ...<Widget>[
            const SizedBox(width: GcSpace.lg),
            SizedBox(
              width: 140,
              child: ClipRRect(
                borderRadius: BorderRadius.circular(3),
                child: LinearProgressIndicator(
                  value: controller.updateProgress > 0
                      ? controller.updateProgress.clamp(0, 1).toDouble()
                      : null,
                  minHeight: 4,
                ),
              ),
            ),
          ],
          const SizedBox(width: GcSpace.lg),
          TextButton(
            onPressed: controller.updateBusy
                ? null
                : () => controller.checkUpdate(force: true),
            child: Text(T('update.checkNow')),
          ),
        ],
      ),
    );
  }
}

class _BackendDown extends StatelessWidget {
  const _BackendDown({required this.controller});

  final ShellController controller;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 520),
        child: GcCard(
          padding: const EdgeInsets.all(GcSpace.xxl),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              Row(
                children: <Widget>[
                  Icon(Icons.error_outline_rounded,
                      size: 20, color: GcColors.danger),
                  const SizedBox(width: GcSpace.sm),
                  Text(
                    T('common.error'),
                    style: TextStyle(
                      fontSize: 16,
                      height: 1.35,
                      fontWeight: FontWeight.w600,
                      color: GcColors.ink,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: GcSpace.md),
              GcHint(controller.loadError ?? ''),
              const SizedBox(height: GcSpace.xs),
              GcHint('${T('set.backend')}: ${controller.api.baseUrl}'
                  ' · ${controller.api.hasSessionToken ? '令牌已就绪' : '缺少会话令牌'}'),
              const SizedBox(height: GcSpace.lg),
              FilledButton(
                onPressed: controller.bootstrap,
                child: Text(T('common.retry')),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
