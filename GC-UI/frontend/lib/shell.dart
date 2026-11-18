import 'package:flutter/material.dart';

import 'controller.dart';
import 'i18n.dart';
import 'pages.dart';
import 'theme.dart';

/// 侧边栏导航项。
class _NavItem {
  const _NavItem(this.icon, this.labelKey);

  final IconData icon;
  final String labelKey;
}

const List<_NavItem> _navItems = <_NavItem>[
  _NavItem(Icons.grid_view_rounded, 'nav.home'),
  _NavItem(Icons.download_rounded, 'nav.download'),
  _NavItem(Icons.tune_rounded, 'nav.settings'),
  _NavItem(Icons.info_outline_rounded, 'nav.about'),
];

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
          body: Row(
            children: <Widget>[
              _Sidebar(controller: controller),
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
        );
      },
    );
  }

  Widget _content(ShellController controller) {
    if (!controller.backendReady) {
      if (controller.loadError != null) {
        return _BackendDown(controller: controller);
      }
      return const Center(
        child: SizedBox(
          width: 26,
          height: 26,
          child: CircularProgressIndicator(strokeWidth: 2.4),
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
        return DownloadPageView(controller: controller);
      case 2:
        return SettingsPageView(controller: controller);
      case 3:
        return AboutPageView(controller: controller);
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
      width: 236,
      color: GcColors.sideBg,
      padding: const EdgeInsets.fromLTRB(16, 20, 16, 18),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Row(
            children: <Widget>[
              Container(
                width: 26,
                height: 26,
                decoration: BoxDecoration(
                  color: GcColors.surfaceAlt,
                  borderRadius: BorderRadius.circular(8),
                ),
                alignment: Alignment.center,
                child: Text(
                  '◎',
                  style: TextStyle(fontSize: 14, color: GcColors.accent),
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  'Morris GC',
                  style: TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.w600,
                    color: GcColors.ink,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          GcHint(T('app.tagline')),
          const SizedBox(height: 26),
          const Padding(
            padding: EdgeInsets.only(left: 14, bottom: 6),
            child: Text(
              'MENU',
              style: TextStyle(
                fontSize: 9.5,
                letterSpacing: 1.2,
                fontWeight: FontWeight.w600,
                color: Color(0xFF4C5F80),
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
          const SizedBox(height: 10),
          Padding(
            padding: const EdgeInsets.only(left: 14),
            child: Text(
              'v${settings?.version ?? '—'}',
              style: TextStyle(fontSize: 10.5, color: GcColors.faint),
            ),
          ),
          Padding(
            padding: const EdgeInsets.only(left: 14, top: 2),
            child: Text(
              settings?.platform ?? '—',
              style: const TextStyle(fontSize: 10, color: Color(0xFF4F6080)),
            ),
          ),
        ],
      ),
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
      padding: const EdgeInsets.only(bottom: 4),
      child: Material(
        color: active ? GcColors.surfaceAlt : Colors.transparent,
        borderRadius: BorderRadius.circular(9),
        child: InkWell(
          borderRadius: BorderRadius.circular(9),
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
            child: Row(
              children: <Widget>[
                Icon(
                  item.icon,
                  size: 16,
                  color: active ? GcColors.accent : GcColors.faint,
                ),
                const SizedBox(width: 10),
                Text(
                  T(item.labelKey),
                  style: TextStyle(
                    fontSize: 12.5,
                    fontWeight: active ? FontWeight.w600 : FontWeight.w400,
                    color: active ? GcColors.ink : GcColors.muted,
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
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 9),
      child: Row(
        children: <Widget>[
          Text('⟳',
              style: TextStyle(fontSize: 16, color: GcColors.accent)),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                Text(
                  controller.updateStatus,
                  style: TextStyle(
                    fontSize: 12.5,
                    fontWeight: FontWeight.w600,
                    color: GcColors.ink,
                  ),
                  overflow: TextOverflow.ellipsis,
                ),
                if (controller.updateDetail.isNotEmpty)
                  Text(
                    controller.updateDetail,
                    style: TextStyle(fontSize: 11, color: GcColors.faint),
                    overflow: TextOverflow.ellipsis,
                  ),
                if (controller.launcherNotice.isNotEmpty)
                  Text(
                    controller.launcherNotice,
                    style: TextStyle(fontSize: 11, color: GcColors.warn),
                  ),
              ],
            ),
          ),
          if (controller.updateBusy)
            const SizedBox(
              width: 130,
              child: LinearProgressIndicator(minHeight: 4),
            ),
          const SizedBox(width: 14),
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
          padding: const EdgeInsets.all(28),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              Text(
                T('common.error'),
                style: TextStyle(
                  fontSize: 17,
                  fontWeight: FontWeight.w600,
                  color: GcColors.ink,
                ),
              ),
              const SizedBox(height: 10),
              GcHint(controller.loadError ?? ''),
              const SizedBox(height: 6),
              GcHint('后端地址：${controller.api.baseUrl}'),
              const SizedBox(height: 16),
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
