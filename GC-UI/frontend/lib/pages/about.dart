import 'dart:async';

import 'package:flutter/material.dart';

import '../api_client.dart';
import '../controller.dart';
import '../i18n.dart';
import '../theme.dart';

// ---------------------------------------------------------------------------
// 关于页（从原 pages.dart 拆出，公开类名不变）
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// 关于页
// ---------------------------------------------------------------------------

class AboutPageView extends StatefulWidget {
  const AboutPageView({super.key, required this.controller, this.initialAbout});

  final ShellController controller;

  /// 预置数据（仅用于测试 / 截图，正常运行时为 null，由后端提供）。
  final AboutInfo? initialAbout;

  @override
  State<AboutPageView> createState() => _AboutPageViewState();
}

class _AboutPageViewState extends State<AboutPageView> {
  AboutInfo? _about;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    final preset = widget.initialAbout;
    if (preset != null) {
      _about = preset;
      _loading = false;
      return;
    }
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final about = await widget.controller.api.about();
      if (!mounted) return;
      setState(() {
        _about = about;
        _loading = false;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _error = error.toString();
        _loading = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return ListView(
      padding: const EdgeInsets.fromLTRB(
        GcSpace.xxl,
        GcSpace.xxl,
        GcSpace.xxl,
        GcSpace.xxxl,
      ),
      children: <Widget>[
        GcPageHeader(
          title: T('nav.about'),
          subtitle: T('app.tagline'),
        ),
        const SizedBox(height: GcSpace.xxl),
        if (_loading)
          const Padding(
            padding: EdgeInsets.symmetric(vertical: 48),
            child: Center(
              child: SizedBox(
                width: 22,
                height: 22,
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
            ),
          )
        else if (_error != null)
          GcMessage(
            title: T('common.error'),
            message: _error,
            icon: Icons.error_outline_rounded,
            color: GcColors.danger,
            action: OutlinedButton(
              onPressed: _load,
              child: Text(T('common.retry')),
            ),
          )
        else ...<Widget>[
          _versionCard(),
          const SizedBox(height: GcSpace.lg),
          _contributorsCard(),
          const SizedBox(height: GcSpace.lg),
          _thanksCard(),
        ],
      ],
    );
  }

  Widget _versionCard() {
    final about = _about;

    // 左边身份卡、右边信息表：宽屏两栏，窄屏自动堆叠。
    final identity = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: <Widget>[
        Row(
          children: <Widget>[
            Container(
              width: 44,
              height: 44,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: GcColors.accentSoft,
                borderRadius: BorderRadius.circular(GcRadius.card),
              ),
              child: Text(
                '◎',
                style: TextStyle(
                    fontSize: 22, height: 1.1, color: GcColors.accent),
              ),
            ),
            const SizedBox(width: GcSpace.lg),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: <Widget>[
                  Text(
                    about?.appName ?? 'Morris GC',
                    style: TextStyle(
                      fontSize: 20,
                      height: 1.3,
                      fontWeight: FontWeight.w600,
                      color: GcColors.ink,
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    T('app.tagline'),
                    style: TextStyle(
                      fontSize: 12.5,
                      height: 1.5,
                      color: GcColors.faint,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
        const SizedBox(height: GcSpace.lg),
        Wrap(
          spacing: GcSpace.sm,
          runSpacing: GcSpace.sm,
          children: <Widget>[
            GcPill('v${about?.version ?? '—'}', color: GcColors.accent),
          ],
        ),
        const SizedBox(height: GcSpace.lg),
        OutlinedButton(
          onPressed: () async {
            final url = about?.repository;
            if (url == null || url.isEmpty) return;
            try {
              await widget.controller.api.openUrl(url);
            } catch (_) {
              // 忽略
            }
          },
          child: Text(T('about.openRepository')),
        ),
      ],
    );

    final details = Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      mainAxisSize: MainAxisSize.min,
      children: <Widget>[
        GcFieldRow(
          label: T('set.platform'),
          value: about?.platform ?? '—',
        ),
        GcFieldRow(
          label: T('about.architecture'),
          value: T('about.architectureValue'),
        ),
        GcFieldRow(
          label: T('about.sourceLanguage'),
          value: about?.sourceLanguage ?? 'C#',
        ),
        GcFieldRow(
          label: T('about.frontend'),
          value: about?.frontend ?? 'Flutter',
        ),
        GcFieldRow(
          label: T('about.gitService'),
          value: 'itxiaohui',
        ),
        GcFieldRow(
          label: T('set.license'),
          value: T('set.licenseValue'),
        ),
      ],
    );

    return GcCard(
      padding: const EdgeInsets.all(GcSpace.xxl),
      child: LayoutBuilder(
        builder: (context, constraints) {
          if (constraints.maxWidth < 720) {
            return Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                identity,
                const GcDivider(verticalPadding: GcSpace.lg),
                details,
              ],
            );
          }
          return Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              Expanded(flex: 4, child: identity),
              const SizedBox(width: GcSpace.xxxl),
              Expanded(flex: 5, child: details),
            ],
          );
        },
      ),
    );
  }

  Widget _contributorsCard() {
    final contributors = _about?.contributors ?? const <ContributorInfo>[];
    return GcCard(
      padding: const EdgeInsets.all(GcSpace.xxl),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          GcSectionTitle(
            T('about.developers'),
            subtitle: T('about.contributorsHint'),
          ),
          const SizedBox(height: GcSpace.lg),
          if (contributors.isEmpty)
            GcHint(T('about.noContributors'))
          else
            Wrap(
              spacing: GcSpace.xl,
              runSpacing: GcSpace.md,
              children: <Widget>[
                for (final contributor in contributors)
                  SizedBox(
                    width: 220,
                    child: Row(
                      children: <Widget>[
                        CircleAvatar(
                          radius: 18,
                          backgroundColor: GcColors.surfaceAlt,
                          backgroundImage: contributor.avatarUrl == null
                              ? null
                              : NetworkImage(contributor.avatarUrl!),
                          child: contributor.avatarUrl == null
                              ? Text(
                                  '◎',
                                  style: TextStyle(
                                    fontSize: 14,
                                    color: GcColors.accent,
                                  ),
                                )
                              : null,
                        ),
                        const SizedBox(width: GcSpace.md),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            mainAxisSize: MainAxisSize.min,
                            children: <Widget>[
                              Text(
                                contributor.name,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: TextStyle(
                                  fontSize: 13,
                                  height: 1.4,
                                  fontWeight: FontWeight.w600,
                                  color: GcColors.ink,
                                ),
                              ),
                              Text(
                                T(
                                  'about.contributions',
                                  <Object?>[contributor.contributions],
                                ),
                                style: TextStyle(
                                  fontSize: 11.5,
                                  height: 1.4,
                                  color: GcColors.faint,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                  ),
              ],
            ),
        ],
      ),
    );
  }

  /// 特别感谢：与「开发者」分开列 —— 提供 Git 服务不等于往仓库提交代码，
  /// 混进贡献者列表会让提交数失去意义。
  Widget _thanksCard() {
    return GcCard(
      padding: const EdgeInsets.all(GcSpace.xxl),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          GcSectionTitle(
            T('about.thanks'),
            subtitle: T('about.thanksHint'),
          ),
          const SizedBox(height: GcSpace.lg),
          Wrap(
            spacing: GcSpace.xl,
            runSpacing: GcSpace.md,
            children: <Widget>[
              _creditTile('地理洪老师', 'about.thanksGeo'),
              _creditTile('itxiaohui', 'about.thanksGit'),
            ],
          ),
        ],
      ),
    );
  }

  Widget _creditTile(String name, String roleKey) {
    return SizedBox(
      width: 220,
      child: Row(
        children: <Widget>[
          CircleAvatar(
            radius: 18,
            backgroundColor: GcColors.surfaceAlt,
            child: Text(
              '◎',
              style: TextStyle(fontSize: 14, color: GcColors.accent),
            ),
          ),
          const SizedBox(width: GcSpace.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: <Widget>[
                Text(
                  name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    fontSize: 13,
                    height: 1.4,
                    fontWeight: FontWeight.w600,
                    color: GcColors.ink,
                  ),
                ),
                Text(
                  T(roleKey),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    fontSize: 11.5,
                    height: 1.4,
                    color: GcColors.faint,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
