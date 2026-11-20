import 'dart:async';

import 'package:flutter/material.dart';

import '../api_client.dart';
import '../controller.dart';
import '../i18n.dart';
import '../theme.dart';

// ---------------------------------------------------------------------------
// 首页：模型库（从原 pages.dart 拆出，公开类名不变）
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// 首页：模型库
// ---------------------------------------------------------------------------

class HomePageView extends StatefulWidget {
  const HomePageView({super.key, required this.controller, this.initialModels});

  final ShellController controller;

  /// 预置模型列表（仅用于测试 / 截图，正常运行时为 null，由后端提供）。
  final List<ModelInfo>? initialModels;

  @override
  State<HomePageView> createState() => _HomePageViewState();
}

class _HomePageViewState extends State<HomePageView> {
  List<ModelInfo> _models = const <ModelInfo>[];
  bool _loading = true;
  String? _error;
  String? _notice;
  int _lastRevision = 0;

  @override
  void initState() {
    super.initState();
    widget.controller.addListener(_onControllerChanged);
    _lastRevision = widget.controller.modelsRevision;

    final preset = widget.initialModels;
    if (preset != null) {
      _models = preset;
      _loading = false;
    } else {
      _load();
    }
  }

  @override
  void dispose() {
    widget.controller.removeListener(_onControllerChanged);
    super.dispose();
  }

  /// 下载完成 / 删除模型后重新拉列表：页面被 Offstage 保活，不会自己重建。
  void _onControllerChanged() {
    if (!mounted) return;
    final revision = widget.controller.modelsRevision;
    if (revision == _lastRevision) return;
    _lastRevision = revision;
    if (widget.initialModels != null) return; // 截图 / 测试用的预置数据
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final models = await widget.controller.api.models();
      if (!mounted) return;
      setState(() {
        _models = models;
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

  Future<void> _launch(ModelInfo model) async {
    setState(() => _notice = null);
    try {
      final result = await widget.controller.api.launchModel(model.id);
      if (!mounted) return;
      setState(() => _notice = result.message);
    } catch (error) {
      if (!mounted) return;
      setState(() => _notice = error.toString());
    }
  }

  Future<void> _delete(ModelInfo model) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        backgroundColor: GcColors.surface,
        title: Text(T('common.confirmTitle')),
        content: Text(T('home.deleteConfirm', <Object?>[model.name])),
        actions: <Widget>[
          TextButton(
            onPressed: () => Navigator.of(context).pop(false),
            child: Text(T('common.cancel')),
          ),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(true),
            child: Text(T('common.delete')),
          ),
        ],
      ),
    );
    if (confirmed != true) return;

    try {
      await widget.controller.api.deleteModel(model.id);
      await _load();
    } catch (error) {
      if (!mounted) return;
      setState(() => _notice = error.toString());
    }
  }

  Future<void> _openFolder(String path) async {
    try {
      await widget.controller.api.openFolder(path);
    } catch (error) {
      if (!mounted) return;
      setState(() => _notice = error.toString());
    }
  }

  @override
  Widget build(BuildContext context) {
    final settings = widget.controller.settings;
    return RefreshIndicator(
      onRefresh: _load,
      color: GcColors.accent,
      backgroundColor: GcColors.surface,
      child: ListView(
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.fromLTRB(
            GcSpace.xxl, GcSpace.xxl, GcSpace.xxl, GcSpace.xxxl),
        children: <Widget>[
          _hero(settings),
          const SizedBox(height: GcSpace.xxl),
          _sectionHeader(),
          if (_notice != null) ...<Widget>[
            const SizedBox(height: GcSpace.md),
            GcMessage(
              title: _notice!,
              icon: Icons.info_outline_rounded,
              color: GcColors.warn,
            ),
          ],
          const SizedBox(height: GcSpace.lg),
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
              icon: Icons.cloud_off_rounded,
              color: GcColors.danger,
              action: OutlinedButton(
                onPressed: _load,
                child: Text(T('common.retry')),
              ),
            )
          else if (_models.isEmpty)
            _emptyState()
          else
            LayoutBuilder(
              builder: (context, constraints) {
                const spacing = GcSpace.lg;
                final columns =
                    (constraints.maxWidth / 320).floor().clamp(1, 4);
                final cardWidth =
                    (constraints.maxWidth - spacing * (columns - 1)) / columns;
                return Wrap(
                  spacing: spacing,
                  runSpacing: spacing,
                  children: <Widget>[
                    for (final model in _models)
                      SizedBox(
                        width: cardWidth,
                        child: _ModelCard(
                          model: model,
                          previewUrl: widget.controller.api.previewUrl(model),
                          onLaunch: () => _launch(model),
                          onOpenFolder: () => _openFolder(model.directory),
                          onDelete: () => _delete(model),
                        ),
                      ),
                  ],
                );
              },
            ),
        ],
      ),
    );
  }

  /// 顶部横幅：产品定位 + 主操作 + 关键指标。
  ///
  /// 不用渐变：深色主题下的低对比渐变会让整块区域发灰，看起来就是「糊」。
  Widget _hero(SettingsInfo? settings) {
    final enabledSources =
        settings?.sources.where((s) => s.enabled).length ?? 0;
    return GcCard(
      padding: const EdgeInsets.all(GcSpace.xxl + 2),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                Text(
                  'MODEL LIBRARY',
                  style: TextStyle(
                    fontSize: 11,
                    height: 1.2,
                    letterSpacing: 1.4,
                    fontWeight: FontWeight.w600,
                    color: GcColors.accent,
                  ),
                ),
                const SizedBox(height: GcSpace.md),
                Text(
                  T('home.title'),
                  style: TextStyle(
                    fontSize: 26,
                    height: 1.25,
                    fontWeight: FontWeight.w600,
                    letterSpacing: -0.2,
                    color: GcColors.ink,
                  ),
                ),
                const SizedBox(height: GcSpace.sm),
                ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 460),
                  child: Text(
                    T('home.subtitle'),
                    style: TextStyle(
                      fontSize: 13,
                      height: 1.65,
                      color: GcColors.muted,
                    ),
                  ),
                ),
                const SizedBox(height: GcSpace.xl),
                Row(
                  children: <Widget>[
                    FilledButton.icon(
                      onPressed:
                          _models.isEmpty ? null : () => _launch(_models.first),
                      icon: const Icon(Icons.play_arrow_rounded, size: 18),
                      label: Text(T('home.launch')),
                    ),
                    const SizedBox(width: GcSpace.md),
                    OutlinedButton(
                      onPressed: () => widget.controller.goTo(2),
                      child: Text(T('home.download')),
                    ),
                    const SizedBox(width: GcSpace.xs),
                    TextButton(
                      onPressed: () => widget.controller.goTo(3),
                      child: Text(T('home.settings')),
                    ),
                  ],
                ),
                const SizedBox(height: GcSpace.md),
                Text(
                  T('home.launchHint'),
                  style: TextStyle(
                    fontSize: 12,
                    height: 1.55,
                    color: GcColors.faint,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: GcSpace.xxl),
          Container(
            padding: const EdgeInsets.symmetric(
              horizontal: GcSpace.xl,
              vertical: GcSpace.lg,
            ),
            decoration: BoxDecoration(
              color: GcColors.surfaceAlt,
              borderRadius: BorderRadius.circular(GcRadius.control),
              border: Border.all(color: GcColors.line),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: <Widget>[
                GcMetric(
                  value: '${_models.length}',
                  label: T('home.stats.models'),
                ),
                _metricDivider(),
                GcMetric(
                  value: '$enabledSources',
                  label: T('home.stats.sources'),
                ),
                _metricDivider(),
                GcMetric(
                  value: '${settings?.memoryBudgetMb ?? 0}',
                  label: T('home.stats.memory'),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _metricDivider() {
    return Container(
      width: 1,
      height: 34,
      margin: const EdgeInsets.symmetric(horizontal: GcSpace.xl),
      color: GcColors.line,
    );
  }

  Widget _sectionHeader() {
    return Row(
      children: <Widget>[
        Text(
          T('home.installed'),
          style: TextStyle(
            fontSize: 16,
            height: 1.4,
            fontWeight: FontWeight.w600,
            color: GcColors.ink,
          ),
        ),
        const SizedBox(width: GcSpace.sm),
        GcPill(T('home.count', <Object?>[_models.length]), subtle: true),
        const Spacer(),
        IconButton(
          tooltip: T('common.refresh'),
          onPressed: _load,
          icon: const Icon(Icons.refresh_rounded, size: 18),
        ),
      ],
    );
  }

  Widget _emptyState() {
    return GcCard(
      padding: const EdgeInsets.symmetric(
        horizontal: GcSpace.xxxl,
        vertical: 40,
      ),
      child: Column(
        children: <Widget>[
          Container(
            width: 52,
            height: 52,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: GcColors.accentSoft,
              borderRadius: BorderRadius.circular(GcRadius.panel),
            ),
            child: Icon(
              Icons.travel_explore_rounded,
              size: 26,
              color: GcColors.accent,
            ),
          ),
          const SizedBox(height: GcSpace.lg),
          Text(
            T('home.empty'),
            style: TextStyle(
              fontSize: 16,
              height: 1.4,
              fontWeight: FontWeight.w600,
              color: GcColors.ink,
            ),
          ),
          const SizedBox(height: GcSpace.sm),
          ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 420),
            child: Text(
              T('home.emptyHint'),
              textAlign: TextAlign.center,
              style: TextStyle(
                fontSize: 12.5,
                height: 1.7,
                color: GcColors.muted,
              ),
            ),
          ),
          const SizedBox(height: GcSpace.xl),
          FilledButton(
            onPressed: () => widget.controller.goTo(2),
            child: Text(T('home.emptyAction')),
          ),
        ],
      ),
    );
  }
}

class _ModelCard extends StatefulWidget {
  const _ModelCard({
    required this.model,
    required this.previewUrl,
    required this.onLaunch,
    required this.onOpenFolder,
    required this.onDelete,
  });

  final ModelInfo model;
  final String previewUrl;
  final VoidCallback onLaunch;
  final VoidCallback onOpenFolder;
  final VoidCallback onDelete;

  @override
  State<_ModelCard> createState() => _ModelCardState();
}

class _ModelCardState extends State<_ModelCard> {
  bool _hover = false;

  @override
  Widget build(BuildContext context) {
    final model = widget.model;
    return MouseRegion(
      onEnter: (_) => setState(() => _hover = true),
      onExit: (_) => setState(() => _hover = false),
      cursor: SystemMouseCursors.click,
      child: GcCard(
        padding: EdgeInsets.zero,
        clip: true,
        borderColor: _hover ? GcColors.lineStrong : GcColors.line,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            _cover(model),
            Padding(
              padding: const EdgeInsets.fromLTRB(
                GcSpace.lg,
                GcSpace.md + 2,
                GcSpace.lg,
                GcSpace.md,
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: <Widget>[
                  Row(
                    children: <Widget>[
                      Expanded(
                        child: Text(
                          model.name,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(
                            fontSize: 14.5,
                            height: 1.35,
                            fontWeight: FontWeight.w600,
                            color: GcColors.ink,
                          ),
                        ),
                      ),
                      const SizedBox(width: GcSpace.sm),
                      Text(
                        model.sizeText,
                        style: TextStyle(
                          fontSize: 12,
                          height: 1.35,
                          color: GcColors.faint,
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: GcSpace.sm - 2),
                  SizedBox(
                    height: 36,
                    child: Text(
                      model.description.isEmpty
                          ? T('home.noDescription')
                          : model.description,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontSize: 12.5,
                        height: 1.45,
                        color: model.description.isEmpty
                            ? GcColors.faint
                            : GcColors.muted,
                      ),
                    ),
                  ),
                  if (model.tags.isNotEmpty) ...<Widget>[
                    const SizedBox(height: GcSpace.sm + 2),
                    SizedBox(
                      height: 20,
                      child: Wrap(
                        spacing: 6,
                        runSpacing: 6,
                        children: <Widget>[
                          for (final tag in model.tags.take(3))
                            GcPill(tag, subtle: true),
                        ],
                      ),
                    ),
                  ],
                  const SizedBox(height: GcSpace.md + 2),
                  Row(
                    children: <Widget>[
                      FilledButton.icon(
                        onPressed: widget.onLaunch,
                        icon: const Icon(Icons.play_arrow_rounded, size: 18),
                        label: Text(T('home.launch')),
                      ),
                      const Spacer(),
                      IconButton(
                        tooltip: T('home.open'),
                        onPressed: widget.onOpenFolder,
                        icon: const Icon(Icons.folder_open_rounded, size: 18),
                      ),
                      IconButton(
                        tooltip: T('home.delete'),
                        onPressed: widget.onDelete,
                        icon:
                            const Icon(Icons.delete_outline_rounded, size: 18),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  /// 封面：按卡片宽度和屏幕像素比解码，避免被放大后发虚。
  Widget _cover(ModelInfo model) {
    final dpr = MediaQuery.devicePixelRatioOf(context);
    final decodeWidth =
        (MediaQuery.sizeOf(context).width * dpr).clamp(320, 900).round();
    return SizedBox(
      height: 156,
      width: double.infinity,
      child: Stack(
        fit: StackFit.expand,
        children: <Widget>[
          _PreviewFallback(model: model),
          if (model.hasPreview)
            Image.network(
              widget.previewUrl,
              fit: BoxFit.cover,
              cacheWidth: decodeWidth,
              filterQuality: FilterQuality.medium,
              gaplessPlayback: true,
              errorBuilder: (context, error, stack) => const SizedBox.shrink(),
            ),
          DecoratedBox(
            decoration: BoxDecoration(
              // 只用「透明 → 深色」的遮罩，别用 alphaBlend：叠加不透明底色
              // 会把整张封面盖平，占位字母也跟着消失。
              gradient: LinearGradient(
                begin: Alignment.topCenter,
                end: Alignment.bottomCenter,
                colors: <Color>[
                  GcColors.appBg.withOpacity(0.0),
                  GcColors.appBg.withOpacity(0.32),
                  GcColors.appBg.withOpacity(0.88),
                ],
                stops: const <double>[0.42, 0.72, 1.0],
              ),
            ),
          ),
          Positioned(
            left: GcSpace.md,
            right: GcSpace.sm,
            bottom: GcSpace.sm,
            child: Row(
              children: <Widget>[
                if (model.version.isNotEmpty)
                  GcPill('v${model.version}', color: GcColors.accent),
                if (model.author.isNotEmpty) ...<Widget>[
                  const SizedBox(width: GcSpace.sm),
                  Flexible(
                    child: Text(
                      model.author,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontSize: 11.5,
                        height: 1.3,
                        color: GcColors.muted,
                      ),
                    ),
                  ),
                ],
                const Spacer(),
                AnimatedOpacity(
                  opacity: _hover ? 1 : 0,
                  duration: const Duration(milliseconds: 140),
                  child: Container(
                    width: 26,
                    height: 26,
                    decoration: BoxDecoration(
                      color: GcColors.accentFill,
                      shape: BoxShape.circle,
                    ),
                    child: Icon(
                      Icons.play_arrow_rounded,
                      size: 18,
                      color: GcColors.onAccent,
                    ),
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

/// 没有封面时的占位：型号首字 + 淡淡的网格感，比一个大「◎」更像封面。
class _PreviewFallback extends StatelessWidget {
  const _PreviewFallback({this.model});

  final ModelInfo? model;

  @override
  Widget build(BuildContext context) {
    final label = (model?.name ?? '').characters.isEmpty
        ? '◎'
        : model!.name.characters.first;
    return DecoratedBox(
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: <Color>[
            Color.alphaBlend(
              GcColors.accent.withOpacity(0.22),
              GcColors.surfaceAlt,
            ),
            GcColors.surfaceAlt,
          ],
        ),
      ),
      child: Center(
        child: Text(
          label,
          style: TextStyle(
            fontSize: 46,
            height: 1.0,
            fontWeight: FontWeight.w600,
            color: Color.alphaBlend(
              GcColors.accent.withOpacity(0.55),
              GcColors.surfaceAlt,
            ),
          ),
        ),
      ),
    );
  }
}
