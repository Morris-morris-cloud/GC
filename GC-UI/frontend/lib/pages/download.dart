import 'dart:async';

import 'package:flutter/material.dart';

import '../api_client.dart';
import '../controller.dart';
import '../i18n.dart';
import '../theme.dart';

// ---------------------------------------------------------------------------
// 下载页（从原 pages.dart 拆出，公开类名不变）
// ---------------------------------------------------------------------------

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
