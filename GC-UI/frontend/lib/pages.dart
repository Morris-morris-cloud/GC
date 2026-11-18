import 'dart:async';

import 'package:flutter/material.dart';

import 'api_client.dart';
import 'controller.dart';
import 'i18n.dart';
import 'theme.dart';

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
                      onPressed: () => widget.controller.goTo(1),
                      child: Text(T('home.download')),
                    ),
                    const SizedBox(width: GcSpace.xs),
                    TextButton(
                      onPressed: () => widget.controller.goTo(2),
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
            onPressed: () => widget.controller.goTo(1),
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

// ---------------------------------------------------------------------------
// 下载页
// ---------------------------------------------------------------------------

class DownloadPageView extends StatefulWidget {
  const DownloadPageView({
    super.key,
    required this.controller,
    this.initialSources,
    this.initialIndex,
    this.initialJobs,
  });

  final ShellController controller;

  /// 预置数据（仅用于测试 / 截图，正常运行时为 null，由后端提供）。
  final List<SourceInfo>? initialSources;
  final SourceIndexInfo? initialIndex;
  final List<JobInfo>? initialJobs;

  @override
  State<DownloadPageView> createState() => _DownloadPageViewState();
}

class _DownloadPageViewState extends State<DownloadPageView> {
  List<SourceInfo> _sources = const <SourceInfo>[];
  SourceIndexInfo? _index;
  List<JobInfo> _jobs = const <JobInfo>[];
  String? _selectedSourceId;
  bool _loading = true;
  String? _error;
  Timer? _poll;

  /// 页面是否可见（被切走后仍保活，但没必要继续请求后端）。
  bool _visible = true;

  /// 上一次轮询时是否有活跃任务，用来判断「刚刚全部结束」。
  bool _hadActive = false;

  @override
  void initState() {
    super.initState();
    final presetSources = widget.initialSources;
    if (presetSources != null) {
      _sources = presetSources;
      _index = widget.initialIndex;
      _jobs = widget.initialJobs ?? const <JobInfo>[];
      _selectedSourceId = presetSources.isEmpty ? null : presetSources.first.id;
      _loading = false;
      return;
    }
    _load();
    _schedulePoll(const Duration(seconds: 2));
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    // TickerMode 由外壳的 Offstage 控制：切到别的页时变 false。
    _visible = TickerMode.of(context);
  }

  @override
  void dispose() {
    _poll?.cancel();
    super.dispose();
  }

  /// 单次 Timer 链：有活跃任务时 1 秒一次，空闲时降到 5 秒，不可见时 10 秒。
  ///
  /// 以前是 `Timer.periodic(1s)` 且永不停止 —— 只要进过下载页，之后每秒都
  /// 在请求 `/api/download/jobs`，哪怕没有任何任务、页面也早就切走了。
  void _schedulePoll(Duration delay) {
    _poll?.cancel();
    _poll = Timer(delay, _pollOnce);
  }

  Future<void> _pollOnce() async {
    if (!mounted) return;

    if (!_visible) {
      _schedulePoll(const Duration(seconds: 10));
      return;
    }

    _hadActive = _jobs.any((job) => job.isActive);
    await _refreshJobs();
    if (!mounted) return;

    final active = _jobs.any((job) => job.isActive);
    // 活跃 → 空闲：这一轮里下载结束了，首页的模型列表已经过期。
    if (_hadActive && !active) widget.controller.modelsChanged();
    _hadActive = active;

    _schedulePoll(Duration(seconds: active ? 1 : 5));
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final sources = await widget.controller.api.sources();
      final preferred = sources.where((s) => s.enabled).toList();
      final selected = _selectedSourceId ??
          (preferred.isNotEmpty
              ? preferred.first.id
              : (sources.isNotEmpty ? sources.first.id : ''));

      final index = selected.isEmpty
          ? null
          : await widget.controller.api.remoteModels(sourceId: selected);

      if (!mounted) return;
      setState(() {
        _sources = sources;
        _selectedSourceId = selected;
        _index = index;
        _loading = false;
      });
      await _refreshJobs();
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _error = error.toString();
        _loading = false;
      });
    }
  }

  Future<void> _refreshJobs() async {
    try {
      final jobs = await widget.controller.api.jobs();
      if (!mounted) return;
      setState(() => _jobs = jobs);
    } catch (_) {
      // 轮询失败不打扰用户，下一次再试
    }
  }

  Future<void> _selectSource(String id) async {
    setState(() {
      _selectedSourceId = id;
      _loading = true;
      _error = null;
    });
    try {
      final index = await widget.controller.api.remoteModels(sourceId: id);
      if (!mounted) return;
      setState(() {
        _index = index;
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

  Future<void> _download(RemoteModelInfo model) async {
    final sourceId = _selectedSourceId;
    if (sourceId == null) return;
    try {
      await widget.controller.api.startDownload(sourceId, model.id);
      await _refreshJobs();
    } catch (error) {
      if (!mounted) return;
      setState(() => _error = error.toString());
    }
  }

  Future<void> _jobAction(
      Future<JobInfo> Function(String id) action, String id) async {
    try {
      await action(id);
      await _refreshJobs();
    } catch (error) {
      if (!mounted) return;
      setState(() => _error = error.toString());
    }
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(
          GcSpace.xxl, GcSpace.xxl, GcSpace.xxl, GcSpace.xl),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          SizedBox(width: 268, child: _sourcePanel()),
          const SizedBox(width: GcSpace.xl),
          Expanded(child: _modelPanel()),
        ],
      ),
    );
  }

  Widget _sourcePanel() {
    return GcCard(
      padding: const EdgeInsets.all(GcSpace.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: <Widget>[
          GcSectionTitle(T('dl.sources')),
          const SizedBox(height: GcSpace.xs + 2),
          GcHint(T('dl.subtitle')),
          const SizedBox(height: GcSpace.md),
          for (final source in _sources)
            Padding(
              padding: const EdgeInsets.only(bottom: GcSpace.xs + 2),
              child: _sourceTile(source),
            ),
          const SizedBox(height: GcSpace.sm),
          SizedBox(
            width: double.infinity,
            child: OutlinedButton.icon(
              onPressed: _load,
              icon: const Icon(Icons.refresh_rounded, size: 18),
              label: Text(T('dl.refresh')),
            ),
          ),
        ],
      ),
    );
  }

  Widget _sourceTile(SourceInfo source) {
    final selected = source.id == _selectedSourceId;
    return Material(
      color: selected ? GcColors.accentSoft : Colors.transparent,
      borderRadius: BorderRadius.circular(GcRadius.control),
      child: InkWell(
        borderRadius: BorderRadius.circular(GcRadius.control),
        hoverColor: GcColors.surfaceAlt,
        onTap: () => _selectSource(source.id),
        child: Container(
          padding: const EdgeInsets.symmetric(
            horizontal: GcSpace.md,
            vertical: GcSpace.md - 2,
          ),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(GcRadius.control),
            border: Border.all(
              color: selected
                  ? GcColors.accent.withOpacity(0.45)
                  : Colors.transparent,
            ),
          ),
          child: Row(
            children: <Widget>[
              Icon(
                selected
                    ? Icons.radio_button_checked_rounded
                    : Icons.radio_button_unchecked_rounded,
                size: 16,
                color: selected ? GcColors.accent : GcColors.ghost,
              ),
              const SizedBox(width: GcSpace.sm + 2),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: <Widget>[
                    Text(
                      source.name,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontSize: 13,
                        height: 1.35,
                        fontWeight: FontWeight.w600,
                        color: selected ? GcColors.accent : GcColors.ink,
                      ),
                    ),
                    const SizedBox(height: 1),
                    Text(
                      source.host,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontSize: 11.5,
                        height: 1.35,
                        color: GcColors.faint,
                      ),
                    ),
                  ],
                ),
              ),
              if (!source.enabled) const GcPill('off', subtle: true),
            ],
          ),
        ),
      ),
    );
  }

  Widget _modelPanel() {
    final models = _index?.models ?? const <RemoteModelInfo>[];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        GcPageHeader(
          title: T('dl.available'),
          subtitle: _index == null
              ? null
              : (_index!.fromCache
                  ? '${T('dl.ready', <Object?>[
                          models.length
                        ])} · ${T('dl.fromCache')}'
                  : T('dl.ready', <Object?>[models.length])),
          trailing: IconButton(
            tooltip: T('common.refresh'),
            onPressed: _load,
            icon: const Icon(Icons.refresh_rounded, size: 18),
          ),
        ),
        const SizedBox(height: GcSpace.lg),
        Expanded(
          child: ListView(
            padding: const EdgeInsets.only(bottom: GcSpace.sm),
            children: <Widget>[
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
              else if (models.isEmpty)
                GcMessage(
                  title: T('dl.empty'),
                  message: T('dl.emptyHint'),
                  icon: Icons.inbox_rounded,
                )
              else
                for (final model in models)
                  Padding(
                    padding: const EdgeInsets.only(bottom: GcSpace.md),
                    child: _remoteRow(model),
                  ),
              const SizedBox(height: GcSpace.sm),
              _jobsPanel(),
            ],
          ),
        ),
      ],
    );
  }

  Widget _remoteRow(RemoteModelInfo model) {
    return GcCard(
      padding: const EdgeInsets.fromLTRB(
        GcSpace.lg,
        GcSpace.md + 2,
        GcSpace.lg,
        GcSpace.md + 2,
      ),
      child: Row(
        children: <Widget>[
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                Row(
                  children: <Widget>[
                    Flexible(
                      child: Text(
                        model.name,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(
                          fontSize: 14,
                          height: 1.35,
                          fontWeight: FontWeight.w600,
                          color: GcColors.ink,
                        ),
                      ),
                    ),
                    const SizedBox(width: GcSpace.sm),
                    Text(
                      'v${model.version} · ${model.sizeText}',
                      style: TextStyle(
                        fontSize: 12,
                        height: 1.35,
                        color: GcColors.faint,
                      ),
                    ),
                    if (model.installed) ...<Widget>[
                      const SizedBox(width: GcSpace.sm),
                      GcPill(T('dl.installed'), color: GcColors.ok),
                    ],
                  ],
                ),
                const SizedBox(height: GcSpace.xs + 1),
                Text(
                  model.description,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    fontSize: 12.5,
                    height: 1.5,
                    color: GcColors.muted,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: GcSpace.lg),
          if (model.installed)
            OutlinedButton(
              onPressed: () => _download(model),
              child: Text(T('dl.reinstall')),
            )
          else
            FilledButton(
              onPressed: () => _download(model),
              child: Text(T('dl.download')),
            ),
        ],
      ),
    );
  }

  Widget _jobsPanel() {
    return GcCard(
      padding: const EdgeInsets.all(GcSpace.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          GcSectionTitle(
            T('dl.background'),
            trailing: TextButton(
              onPressed: () async {
                try {
                  await widget.controller.api.clearFinished();
                  await _refreshJobs();
                } catch (_) {
                  // 忽略
                }
              },
              child: Text(T('dl.clearDone')),
            ),
          ),
          const SizedBox(height: GcSpace.md),
          if (_jobs.isEmpty)
            GcHint(T('dl.bgEmpty'))
          else
            for (final job in _jobs)
              Padding(
                padding: const EdgeInsets.only(bottom: GcSpace.md),
                child: _jobRow(job),
              ),
        ],
      ),
    );
  }

  Widget _jobRow(JobInfo job) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        Row(
          children: <Widget>[
            Expanded(
              child: Text(
                '${job.name}  v${job.version}',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                  fontSize: 13,
                  height: 1.35,
                  fontWeight: FontWeight.w600,
                  color: GcColors.ink,
                ),
              ),
            ),
            GcPill(
              _stateLabel(job.state),
              color: job.state == 'Failed'
                  ? GcColors.danger
                  : job.state == 'Completed'
                      ? GcColors.ok
                      : GcColors.accent,
            ),
            const SizedBox(width: GcSpace.sm),
            if (job.isActive)
              TextButton(
                onPressed: () =>
                    _jobAction(widget.controller.api.cancelJob, job.id),
                child: Text(T('dl.cancel')),
              )
            else if (job.canRetry)
              TextButton(
                onPressed: () =>
                    _jobAction(widget.controller.api.retryJob, job.id),
                child: Text(T('dl.retry')),
              )
            else
              TextButton(
                onPressed: () =>
                    _jobAction(widget.controller.api.removeJob, job.id),
                child: Text(T('dl.removeTask')),
              ),
          ],
        ),
        const SizedBox(height: GcSpace.sm - 2),
        ClipRRect(
          borderRadius: BorderRadius.circular(2),
          child: LinearProgressIndicator(
            value: job.progress.clamp(0, 1).toDouble(),
            minHeight: 4,
            backgroundColor: GcColors.surfaceAlt,
          ),
        ),
        const SizedBox(height: GcSpace.xs + 1),
        Text(
          job.error ?? job.detailText,
          style: TextStyle(
            fontSize: 11.5,
            height: 1.4,
            color: GcColors.faint,
          ),
        ),
      ],
    );
  }

  String _stateLabel(String state) {
    switch (state) {
      case 'Queued':
        return T('dl.status.queued');
      case 'Running':
      case 'Downloading':
        return T('dl.status.downloading');
      case 'Completed':
        return T('dl.status.done');
      case 'Failed':
        return T('dl.status.failed');
      case 'Canceled':
        return T('dl.status.canceled');
      default:
        return state;
    }
  }
}

// ---------------------------------------------------------------------------
// 设置页
// ---------------------------------------------------------------------------

class SettingsPageView extends StatefulWidget {
  const SettingsPageView({super.key, required this.controller});

  final ShellController controller;

  @override
  State<SettingsPageView> createState() => _SettingsPageViewState();
}

class _SettingsPageViewState extends State<SettingsPageView> {
  double _memory = 2048;
  bool _memoryAuto = true;
  int _fps = 120;
  bool _autoUpdate = true;
  String _language = 'auto';
  ThemeMode _themeMode = ThemeMode.dark;

  /// 上一次同步所用的设置对象。以前用的是 `bool _synced` —— 只同步一次，
  /// 之后外部（更新状态条、其它页面）改了设置，本页仍显示旧值。
  SettingsInfo? _syncedFrom;
  String? _status;

  @override
  void initState() {
    super.initState();
    widget.controller.reloadSettings();
  }

  void _syncFrom(SettingsInfo settings) {
    if (identical(_syncedFrom, settings)) return;
    _syncedFrom = settings;
    _memory = settings.memoryMb.toDouble();
    _memoryAuto = settings.memoryAuto;
    _fps = settings.renderFps;
    _autoUpdate = settings.autoUpdateOnStartup;
    _language = settings.language;
    _themeMode = GcPalette.parseMode(settings.theme);
  }

  @override
  Widget build(BuildContext context) {
    final settings = widget.controller.settings;
    if (settings == null) {
      return const Center(
        child: SizedBox(
          width: 22,
          height: 22,
          child: CircularProgressIndicator(strokeWidth: 2),
        ),
      );
    }
    _syncFrom(settings);

    return LayoutBuilder(
      builder: (context, constraints) {
        final twoColumns = constraints.maxWidth >= 960;
        final cards = <Widget>[
          _generalCard(settings),
          _appearanceCard(settings),
          _memoryCard(settings),
          _sourcesCard(settings),
          _storageCard(settings),
          _updateCard(settings),
          _backendCard(settings),
        ];

        Widget column(List<Widget> items) => Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: <Widget>[
                for (var i = 0; i < items.length; i++) ...<Widget>[
                  if (i > 0) const SizedBox(height: GcSpace.lg),
                  items[i],
                ],
              ],
            );

        final left = <Widget>[];
        final right = <Widget>[];
        for (var i = 0; i < cards.length; i++) {
          if (twoColumns && i.isOdd) {
            right.add(cards[i]);
          } else {
            left.add(cards[i]);
          }
        }

        return SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(
            GcSpace.xxl,
            GcSpace.xxl,
            GcSpace.xxl,
            GcSpace.xxxl,
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: <Widget>[
              GcPageHeader(
                title: T('set.title'),
                subtitle: T('set.subtitle'),
                trailing: _status == null
                    ? null
                    : GcPill(
                        _status!,
                        color: GcColors.ok,
                        icon: Icons.check_rounded,
                      ),
              ),
              const SizedBox(height: GcSpace.xxl),
              if (twoColumns)
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    Expanded(child: column(left)),
                    const SizedBox(width: GcSpace.lg),
                    Expanded(child: column(right)),
                  ],
                )
              else
                column(cards),
            ],
          ),
        );
      },
    );
  }

  Widget _label(String text) {
    return Text(
      text,
      style: TextStyle(fontSize: 13, height: 1.5, color: GcColors.muted),
    );
  }

  Widget _cardRow(String label, Widget control) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: GcSpace.sm),
      child: Row(
        children: <Widget>[
          SizedBox(width: 124, child: _label(label)),
          Expanded(child: control),
        ],
      ),
    );
  }

  Widget _generalCard(SettingsInfo settings) {
    return GcCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          GcSectionTitle(T('set.general')),
          const SizedBox(height: GcSpace.lg),
          _cardRow(
            T('set.language'),
            Align(
              alignment: Alignment.centerLeft,
              child: DropdownButton<String>(
                value: _language,
                dropdownColor: GcColors.surfaceAlt,
                underline: const SizedBox.shrink(),
                borderRadius: BorderRadius.circular(GcRadius.control),
                // 从主题样式派生，才能继承字体族（自建 TextStyle 会丢掉
                // fontFamily，中文就回退成系统兜底字体）。
                style: Theme.of(context).textTheme.bodyLarge!.copyWith(
                      fontSize: 13,
                      height: 1.4,
                      fontWeight: FontWeight.w400,
                      color: GcColors.ink,
                    ),
                items: <DropdownMenuItem<String>>[
                  DropdownMenuItem<String>(
                    value: 'auto',
                    child: Text(T('set.language.auto')),
                  ),
                  for (final language in settings.languages)
                    DropdownMenuItem<String>(
                      value: language.code,
                      child: Text(language.label),
                    ),
                ],
                onChanged: (value) async {
                  if (value == null) return;
                  setState(() => _language = value);
                  await widget.controller
                      .saveSettings(SettingsPatch(language: value));
                },
              ),
            ),
          ),
          _cardRow(
            T('set.fps'),
            Row(
              children: <Widget>[
                Expanded(
                  child: Slider(
                    value: _fps.toDouble(),
                    min: 24,
                    max: 120,
                    divisions: 4,
                    label: '$_fps',
                    onChanged: (value) => setState(() => _fps = value.round()),
                    onChangeEnd: (value) async {
                      await widget.controller.saveSettings(
                          SettingsPatch(renderFps: value.round()));
                      if (mounted) {
                        setState(() => _status = T('common.applied'));
                      }
                    },
                  ),
                ),
                SizedBox(
                  width: 48,
                  child: Text(
                    '$_fps',
                    textAlign: TextAlign.end,
                    style: TextStyle(
                      fontSize: 13,
                      height: 1.4,
                      fontWeight: FontWeight.w600,
                      color: GcColors.ink,
                    ),
                  ),
                ),
              ],
            ),
          ),
          GcHint(T('set.fps.desc')),
        ],
      ),
    );
  }

  Widget _appearanceCard(SettingsInfo settings) {
    final current =
        GcPalette.parseHex(settings.accentColor) ?? GcPalette.defaultAccent;
    return GcCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          GcSectionTitle(T('set.appearance')),
          const SizedBox(height: GcSpace.md),
          GcHint(T('set.theme.desc')),
          const SizedBox(height: GcSpace.md),
          SegmentedButton<ThemeMode>(
            segments: <ButtonSegment<ThemeMode>>[
              ButtonSegment<ThemeMode>(
                value: ThemeMode.dark,
                label: Text(T('set.theme.dark')),
                icon: const Icon(Icons.dark_mode_outlined, size: 16),
              ),
              ButtonSegment<ThemeMode>(
                value: ThemeMode.light,
                label: Text(T('set.theme.light')),
                icon: const Icon(Icons.light_mode_outlined, size: 16),
              ),
              ButtonSegment<ThemeMode>(
                value: ThemeMode.system,
                label: Text(T('set.theme.system')),
                icon: const Icon(Icons.brightness_auto_outlined, size: 16),
              ),
            ],
            selected: <ThemeMode>{_themeMode},
            showSelectedIcon: false,
            onSelectionChanged: (selection) async {
              final mode = selection.first;
              setState(() => _themeMode = mode);
              GcPalette.mode.value = mode; // 立即预览
              await widget.controller.saveSettings(
                  SettingsPatch(theme: GcPalette.modeToName(mode)));
              if (mounted) setState(() => _status = T('common.applied'));
            },
          ),
          const SizedBox(height: GcSpace.xl),
          GcHint(T('set.accent.desc')),
          const SizedBox(height: GcSpace.md + 2),
          Wrap(
            spacing: GcSpace.sm + 2,
            runSpacing: GcSpace.sm + 2,
            children: <Widget>[
              for (final color in GcPalette.presets)
                _accentSwatch(color, current),
              _customAccentChip(),
            ],
          ),
          const SizedBox(height: GcSpace.md),
          Text(
            '${T('set.accent')}: ${GcPalette.toHex(current)}',
            style: TextStyle(
              fontSize: 11.5,
              height: 1.4,
              color: GcColors.faint,
            ),
          ),
        ],
      ),
    );
  }

  Widget _accentSwatch(Color color, Color current) {
    final selected = color.value == current.value;
    return Tooltip(
      message: GcPalette.toHex(color),
      child: GestureDetector(
        onTap: () => _applyAccent(GcPalette.toHex(color)),
        child: Container(
          width: 34,
          height: 34,
          decoration: BoxDecoration(
            color: color,
            borderRadius: BorderRadius.circular(GcRadius.control),
            border: Border.all(
              color: selected ? GcColors.ink : Colors.transparent,
              width: 2,
            ),
          ),
          child: selected
              ? Icon(
                  Icons.check_rounded,
                  size: 18,
                  color: color.computeLuminance() > 0.42
                      ? const Color(0xFF0A1226)
                      : Colors.white,
                )
              : null,
        ),
      ),
    );
  }

  Widget _customAccentChip() {
    return MouseRegion(
      cursor: SystemMouseCursors.click,
      child: GestureDetector(
        onTap: _pickCustomAccent,
        child: Container(
          height: 34,
          padding: const EdgeInsets.symmetric(horizontal: GcSpace.md),
          decoration: BoxDecoration(
            color: GcColors.surfaceAlt,
            borderRadius: BorderRadius.circular(GcRadius.control),
            border: Border.all(color: GcColors.line),
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: <Widget>[
              Icon(Icons.colorize_rounded, size: 16, color: GcColors.faint),
              const SizedBox(width: GcSpace.xs + 2),
              Text(
                T('set.accent.custom'),
                style: TextStyle(
                  fontSize: 12,
                  height: 1.3,
                  color: GcColors.muted,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Future<void> _pickCustomAccent() async {
    final controller = TextEditingController(
      text: GcPalette.toHex(GcPalette.accent.value),
    );
    final hex = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        backgroundColor: GcColors.surface,
        title: Text(T('set.accent.custom')),
        content: TextField(
          controller: controller,
          autofocus: true,
          style: Theme.of(context).textTheme.bodyLarge!.copyWith(fontSize: 13),
          decoration: InputDecoration(
            hintText: '#8FB6FF',
            helperText: T('set.accent.hexHint'),
          ),
          onSubmitted: (value) => Navigator.of(context).pop(value),
        ),
        actions: <Widget>[
          TextButton(
            onPressed: () => Navigator.of(context).pop(),
            child: Text(T('common.cancel')),
          ),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(controller.text),
            child: Text(T('common.confirm')),
          ),
        ],
      ),
    );
    controller.dispose();
    if (hex != null) await _applyAccent(hex);
  }

  Future<void> _applyAccent(String hex) async {
    final parsed = GcPalette.parseHex(hex);
    if (parsed == null) {
      if (mounted) setState(() => _status = T('set.accent.invalid'));
      return;
    }
    final normalized = GcPalette.toHex(parsed);
    GcPalette.apply(normalized); // 立即预览
    await widget.controller
        .saveSettings(SettingsPatch(accentColor: normalized));
    if (mounted) setState(() => _status = T('common.applied'));
  }

  Widget _memoryCard(SettingsInfo settings) {
    return GcCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          GcSectionTitle(T('set.memory')),
          const SizedBox(height: GcSpace.md),
          GcHint(T('set.memory.desc')),
          const SizedBox(height: GcSpace.lg),
          Row(
            children: <Widget>[
              Switch(
                value: _memoryAuto,
                onChanged: (value) async {
                  setState(() => _memoryAuto = value);
                  await widget.controller
                      .saveSettings(SettingsPatch(memoryAuto: value));
                },
              ),
              const SizedBox(width: GcSpace.md),
              Expanded(
                child: Text(
                  T('set.memory.auto'),
                  style: TextStyle(
                    fontSize: 13,
                    height: 1.5,
                    color: GcColors.muted,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: GcSpace.xs),
          Row(
            children: <Widget>[
              Expanded(
                child: Slider(
                  value: _memory,
                  min: 512,
                  max: 16384,
                  divisions: 62,
                  label: '${_memory.round()} MB',
                  onChanged: _memoryAuto
                      ? null
                      : (value) => setState(() => _memory = value),
                  onChangeEnd: (value) async {
                    await widget.controller
                        .saveSettings(SettingsPatch(memoryMb: value.round()));
                    if (mounted) setState(() => _status = T('common.applied'));
                  },
                ),
              ),
              SizedBox(
                width: 104,
                child: Text(
                  '${_memory.round()} MB',
                  textAlign: TextAlign.end,
                  style: TextStyle(
                    fontSize: 13,
                    height: 1.4,
                    fontWeight: FontWeight.w600,
                    color: GcColors.ink,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: GcSpace.xs),
          GcHint('${T('set.memory.current')}: ${settings.memoryUsageText}'),
          const SizedBox(height: GcSpace.xs),
          GcHint(T('set.memory.hint')),
        ],
      ),
    );
  }

  Widget _sourcesCard(SettingsInfo settings) {
    return GcCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          GcSectionTitle(T('set.sources')),
          const SizedBox(height: GcSpace.md),
          GcHint(T('set.sources.desc')),
          const SizedBox(height: GcSpace.md),
          for (final source in settings.sources)
            Padding(
              padding: const EdgeInsets.only(bottom: GcSpace.md),
              child: Row(
                children: <Widget>[
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      mainAxisSize: MainAxisSize.min,
                      children: <Widget>[
                        Text(
                          source.name,
                          style: TextStyle(
                            fontSize: 13,
                            height: 1.4,
                            fontWeight: FontWeight.w600,
                            color: GcColors.ink,
                          ),
                        ),
                        const SizedBox(height: 1),
                        Text(
                          source.url,
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
                  const SizedBox(width: GcSpace.sm),
                  if (source.enabled)
                    GcPill(T('set.sourceEnabled'), color: GcColors.ok)
                  else
                    const GcPill('—', subtle: true),
                ],
              ),
            ),
        ],
      ),
    );
  }

  Widget _storageCard(SettingsInfo settings) {
    return GcCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          GcSectionTitle(T('set.storage')),
          const SizedBox(height: GcSpace.md),
          GcFieldRow(
            label: T('set.modelDir'),
            value: settings.modelRoot,
            selectable: true,
            valueColor: GcColors.muted,
          ),
          GcFieldRow(
            label: T('set.dataDir'),
            value: settings.dataRoot,
            selectable: true,
            valueColor: GcColors.muted,
          ),
          const SizedBox(height: GcSpace.md),
          Wrap(
            spacing: GcSpace.sm + 2,
            runSpacing: GcSpace.sm + 2,
            children: <Widget>[
              OutlinedButton(
                onPressed: () async {
                  try {
                    await widget.controller.api.openFolder(settings.modelRoot);
                  } catch (error) {
                    if (mounted) setState(() => _status = error.toString());
                  }
                },
                child: Text(T('set.openFolder')),
              ),
              OutlinedButton(
                onPressed: () async {
                  await widget.controller.checkUpdate(force: true);
                  if (mounted) setState(() => _status = T('set.cacheCleared'));
                },
                child: Text(T('set.clearCache')),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _updateCard(SettingsInfo settings) {
    return GcCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          GcSectionTitle(T('update.title')),
          const SizedBox(height: GcSpace.md),
          GcHint(T('set.checkUpdate.desc')),
          const SizedBox(height: GcSpace.md),
          Row(
            children: <Widget>[
              Switch(
                value: _autoUpdate,
                onChanged: (value) async {
                  setState(() => _autoUpdate = value);
                  await widget.controller
                      .saveSettings(SettingsPatch(autoUpdateOnStartup: value));
                },
              ),
              const SizedBox(width: GcSpace.md),
              Expanded(
                child: Text(
                  T('update.auto'),
                  style: TextStyle(
                    fontSize: 13,
                    height: 1.5,
                    color: GcColors.muted,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: GcSpace.sm),
          GcFieldRow(
            label: T('update.source'),
            value: settings.updateRepository,
            selectable: true,
            valueColor: GcColors.muted,
          ),
          const SizedBox(height: GcSpace.md),
          FilledButton(
            onPressed: () => widget.controller.checkUpdate(force: true),
            child: Text(T('update.checkNow')),
          ),
        ],
      ),
    );
  }

  Widget _backendCard(SettingsInfo settings) {
    final health = widget.controller.health;
    return GcCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          GcSectionTitle(T('set.backend')),
          const SizedBox(height: GcSpace.md),
          GcHint(T('set.backend.desc')),
          const SizedBox(height: GcSpace.md),
          GcFieldRow(
            label: 'API',
            value: widget.controller.api.baseUrl,
            selectable: true,
            valueColor: GcColors.muted,
          ),
          GcFieldRow(
            label: T('set.version'),
            value: settings.version,
            valueColor: GcColors.muted,
          ),
          GcFieldRow(
            label: T('set.platform'),
            value: settings.platform,
            valueColor: GcColors.muted,
          ),
          GcFieldRow(
            label: T('set.backend.ui'),
            value: health == null
                ? '—'
                : health.uiAvailable
                    ? T('set.backend.uiOk')
                    : '${T('set.backend.uiBad')}${health.uiError == null ? '' : ' · ${health.uiError}'}',
            valueColor: health == null
                ? GcColors.muted
                : health.uiAvailable
                    ? GcColors.ok
                    : GcColors.danger,
          ),
        ],
      ),
    );
  }
}

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
}
