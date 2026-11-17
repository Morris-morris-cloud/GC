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

  @override
  void initState() {
    super.initState();
    final preset = widget.initialModels;
    if (preset != null) {
      _models = preset;
      _loading = false;
    } else {
      _load();
    }
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
      child: ListView(
        padding: const EdgeInsets.fromLTRB(26, 24, 26, 28),
        children: <Widget>[
          _hero(settings),
          const SizedBox(height: 18),
          Row(
            children: <Widget>[
              Expanded(
                child: Text(
                  T('home.installed'),
                  style: const TextStyle(
                    fontSize: 15,
                    fontWeight: FontWeight.w600,
                    color: GcColors.ink,
                  ),
                ),
              ),
              GcHint(T('home.count', <Object?>[_models.length])),
              const SizedBox(width: 10),
              IconButton(
                tooltip: T('common.refresh'),
                onPressed: _load,
                icon: const Icon(Icons.refresh_rounded, size: 18),
                color: GcColors.faint,
              ),
            ],
          ),
          if (_notice != null) ...<Widget>[
            const SizedBox(height: 8),
            GcCard(
              padding: const EdgeInsets.all(12),
              child: Text(
                _notice!,
                style: const TextStyle(fontSize: 12, color: GcColors.warn),
              ),
            ),
          ],
          const SizedBox(height: 12),
          if (_loading)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 40),
              child: Center(child: CircularProgressIndicator(strokeWidth: 2.4)),
            )
          else if (_error != null)
            GcCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: <Widget>[
                  Text(T('common.error'),
                      style: const TextStyle(color: GcColors.danger)),
                  const SizedBox(height: 8),
                  GcHint(_error!),
                ],
              ),
            )
          else if (_models.isEmpty)
            _emptyState()
          else
            Wrap(
              spacing: 16,
              runSpacing: 16,
              children: <Widget>[
                for (final model in _models)
                  _ModelCard(
                    model: model,
                    previewUrl: widget.controller.api.previewUrl(model),
                    onLaunch: () => _launch(model),
                    onOpenFolder: () => _openFolder(model.directory),
                    onDelete: () => _delete(model),
                  ),
              ],
            ),
        ],
      ),
    );
  }

  Widget _hero(SettingsInfo? settings) {
    return Container(
      padding: const EdgeInsets.all(26),
      decoration: BoxDecoration(
        gradient: const LinearGradient(
          colors: <Color>[Color(0xFF16233C), Color(0xFF0E1A31)],
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
        ),
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: GcColors.line),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                Text(
                  T('home.title'),
                  style: const TextStyle(
                    fontSize: 24,
                    fontWeight: FontWeight.w700,
                    color: Colors.white,
                  ),
                ),
                const SizedBox(height: 9),
                ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 500),
                  child: Text(
                    T('home.subtitle'),
                    style: const TextStyle(
                      fontSize: 12.5,
                      color: Color(0xFFA9BEDF),
                      height: 1.5,
                    ),
                  ),
                ),
                const SizedBox(height: 16),
                Wrap(
                  spacing: 10,
                  runSpacing: 8,
                  children: <Widget>[
                    FilledButton(
                      onPressed: _models.isEmpty ? null : () => _launch(_models.first),
                      child: Text(T('home.launch')),
                    ),
                    OutlinedButton(
                      onPressed: () => widget.controller.goTo(1),
                      child: Text(T('home.download')),
                    ),
                    OutlinedButton(
                      onPressed: () => widget.controller.goTo(2),
                      child: Text(T('home.settings')),
                    ),
                  ],
                ),
                const SizedBox(height: 10),
                Text(
                  T('home.launchHint'),
                  style: const TextStyle(fontSize: 11, color: Color(0xFF7E93B8)),
                ),
              ],
            ),
          ),
          const SizedBox(width: 16),
          Row(
            children: <Widget>[
              _stat('${_models.length}', T('home.stats.models'), 96),
              const SizedBox(width: 10),
              _stat('${settings?.sources.where((s) => s.enabled).length ?? 0}',
                  T('home.stats.sources'), 96),
              const SizedBox(width: 10),
              _stat('${settings?.memoryBudgetMb ?? 0} MB',
                  T('home.stats.memory'), 112),
            ],
          ),
        ],
      ),
    );
  }

  Widget _stat(String value, String label, double width) {
    return Container(
      width: width,
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      decoration: BoxDecoration(
        color: GcColors.surfaceAlt,
        borderRadius: BorderRadius.circular(14),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Text(
            value,
            style: const TextStyle(
              fontSize: 18,
              fontWeight: FontWeight.w600,
              color: Colors.white,
            ),
          ),
          const SizedBox(height: 2),
          Text(
            label,
            style: const TextStyle(fontSize: 10.5, color: Color(0xFF7E93B8)),
          ),
        ],
      ),
    );
  }

  Widget _emptyState() {
    return GcCard(
      padding: const EdgeInsets.symmetric(horizontal: 30, vertical: 34),
      child: Column(
        children: <Widget>[
          Text(
            T('home.empty'),
            style: const TextStyle(
              fontSize: 15,
              fontWeight: FontWeight.w600,
              color: GcColors.ink,
            ),
          ),
          const SizedBox(height: 10),
          ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 420),
            child: Text(
              T('home.emptyHint'),
              textAlign: TextAlign.center,
              style: const TextStyle(
                fontSize: 12,
                color: GcColors.faint,
                height: 1.5,
              ),
            ),
          ),
          const SizedBox(height: 16),
          FilledButton(
            onPressed: () => widget.controller.goTo(1),
            child: Text(T('home.emptyAction')),
          ),
        ],
      ),
    );
  }
}

class _ModelCard extends StatelessWidget {
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
  Widget build(BuildContext context) {
    return SizedBox(
      width: 336,
      child: GcCard(
        padding: EdgeInsets.zero,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            ClipRRect(
              borderRadius: const BorderRadius.vertical(top: Radius.circular(13)),
              child: SizedBox(
                height: 138,
                width: double.infinity,
                child: Container(
                  color: const Color(0xFF0B1226),
                  child: model.hasPreview
                      ? Image.network(
                          previewUrl,
                          fit: BoxFit.cover,
                          errorBuilder: (context, error, stack) =>
                              const _PreviewFallback(),
                        )
                      : const _PreviewFallback(),
                ),
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 13, 16, 15),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: <Widget>[
                  Row(
                    children: <Widget>[
                      Expanded(
                        child: Text(
                          model.name,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(
                            fontSize: 14,
                            fontWeight: FontWeight.w600,
                            color: GcColors.ink,
                          ),
                        ),
                      ),
                      const SizedBox(width: 8),
                      Text(
                        model.metaText,
                        style: const TextStyle(
                          fontSize: 10.5,
                          color: GcColors.faint,
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 7),
                  SizedBox(
                    height: 34,
                    child: Text(
                      model.description,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        fontSize: 12,
                        color: GcColors.muted,
                        height: 1.4,
                      ),
                    ),
                  ),
                  if (model.tags.isNotEmpty) ...<Widget>[
                    const SizedBox(height: 6),
                    Text(
                      model.tags.join('  ·  '),
                      style: const TextStyle(
                        fontSize: 10.5,
                        color: GcColors.faint,
                      ),
                    ),
                  ],
                  const SizedBox(height: 12),
                  Row(
                    children: <Widget>[
                      FilledButton(
                        onPressed: onLaunch,
                        child: Text(T('home.launch')),
                      ),
                      const Spacer(),
                      TextButton(
                        onPressed: onOpenFolder,
                        child: Text(T('home.open')),
                      ),
                      TextButton(
                        onPressed: onDelete,
                        style: TextButton.styleFrom(
                          foregroundColor: GcColors.danger,
                        ),
                        child: Text(T('home.delete')),
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
}

class _PreviewFallback extends StatelessWidget {
  const _PreviewFallback();

  @override
  Widget build(BuildContext context) => const Center(
        child: Text(
          '◎',
          style: TextStyle(fontSize: 34, color: Color(0xFF1B2A47)),
        ),
      );
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
    _poll = Timer.periodic(const Duration(seconds: 1), (_) => _refreshJobs());
  }

  @override
  void dispose() {
    _poll?.cancel();
    super.dispose();
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

  Future<void> _jobAction(Future<JobInfo> Function(String id) action, String id) async {
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
      padding: const EdgeInsets.fromLTRB(24, 22, 24, 24),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          SizedBox(width: 300, child: _sourcePanel()),
          const SizedBox(width: 16),
          Expanded(child: _modelPanel()),
        ],
      ),
    );
  }

  Widget _sourcePanel() {
    return GcCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          GcSectionTitle(T('dl.sources')),
          const SizedBox(height: 12),
          for (final source in _sources)
            Padding(
              padding: const EdgeInsets.only(bottom: 6),
              child: Material(
                color: source.id == _selectedSourceId
                    ? GcColors.surfaceAlt
                    : Colors.transparent,
                borderRadius: BorderRadius.circular(9),
                child: InkWell(
                  borderRadius: BorderRadius.circular(9),
                  onTap: () => _selectSource(source.id),
                  child: Padding(
                    padding: const EdgeInsets.symmetric(
                        horizontal: 12, vertical: 10),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: <Widget>[
                        Row(
                          children: <Widget>[
                            Expanded(
                              child: Text(
                                source.name,
                                style: const TextStyle(
                                  fontSize: 12.5,
                                  fontWeight: FontWeight.w600,
                                  color: GcColors.ink,
                                ),
                              ),
                            ),
                            if (!source.enabled)
                              const Text(
                                'off',
                                style: TextStyle(
                                    fontSize: 10, color: GcColors.faint),
                              ),
                          ],
                        ),
                        const SizedBox(height: 2),
                        Text(
                          source.host,
                          style: const TextStyle(
                              fontSize: 10.5, color: GcColors.faint),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
          const SizedBox(height: 10),
          GcHint(T('dl.subtitle')),
          const SizedBox(height: 12),
          OutlinedButton.icon(
            onPressed: _load,
            icon: const Icon(Icons.refresh_rounded, size: 16),
            label: Text(T('dl.refresh')),
          ),
        ],
      ),
    );
  }

  Widget _modelPanel() {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        Row(
          children: <Widget>[
            Expanded(
              child: Text(
                T('dl.available'),
                style: const TextStyle(
                  fontSize: 15,
                  fontWeight: FontWeight.w600,
                  color: GcColors.ink,
                ),
              ),
            ),
            if (_index != null)
              GcHint(_index!.fromCache
                  ? '${T('dl.ready', <Object?>[_index!.models.length])} · ${T('dl.fromCache')}'
                  : T('dl.ready', <Object?>[_index!.models.length])),
          ],
        ),
        const SizedBox(height: 12),
        Expanded(
          child: ListView(
            children: <Widget>[
              if (_loading)
                const Padding(
                  padding: EdgeInsets.symmetric(vertical: 40),
                  child: Center(
                      child: CircularProgressIndicator(strokeWidth: 2.4)),
                )
              else if (_error != null)
                GcCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: <Widget>[
                      Text(T('common.error'),
                          style: const TextStyle(color: GcColors.danger)),
                      const SizedBox(height: 8),
                      GcHint(_error!),
                    ],
                  ),
                )
              else if (_index == null || _index!.models.isEmpty)
                GcCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: <Widget>[
                      Text(T('dl.empty'),
                          style: const TextStyle(color: GcColors.ink)),
                      const SizedBox(height: 8),
                      GcHint(T('dl.emptyHint')),
                    ],
                  ),
                )
              else
                for (final model in _index!.models)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 10),
                    child: GcCard(
                      padding: const EdgeInsets.all(14),
                      child: Row(
                        children: <Widget>[
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: <Widget>[
                                Row(
                                  children: <Widget>[
                                    Text(
                                      model.name,
                                      style: const TextStyle(
                                        fontSize: 13,
                                        fontWeight: FontWeight.w600,
                                        color: GcColors.ink,
                                      ),
                                    ),
                                    const SizedBox(width: 8),
                                    Text(
                                      'v${model.version}  ·  ${model.sizeText}',
                                      style: const TextStyle(
                                          fontSize: 10.5,
                                          color: GcColors.faint),
                                    ),
                                    if (model.installed) ...<Widget>[
                                      const SizedBox(width: 8),
                                      Container(
                                        padding: const EdgeInsets.symmetric(
                                            horizontal: 6, vertical: 2),
                                        decoration: BoxDecoration(
                                          color: GcColors.ok
                                              .withOpacity(0.16),
                                          borderRadius:
                                              BorderRadius.circular(5),
                                        ),
                                        child: Text(
                                          T('dl.installed'),
                                          style: const TextStyle(
                                              fontSize: 10,
                                              color: GcColors.ok),
                                        ),
                                      ),
                                    ],
                                  ],
                                ),
                                const SizedBox(height: 4),
                                Text(
                                  model.description,
                                  maxLines: 2,
                                  overflow: TextOverflow.ellipsis,
                                  style: const TextStyle(
                                    fontSize: 11.5,
                                    color: GcColors.muted,
                                    height: 1.4,
                                  ),
                                ),
                              ],
                            ),
                          ),
                          const SizedBox(width: 12),
                          FilledButton(
                            onPressed: () => _download(model),
                            child: Text(model.installed
                                ? T('dl.reinstall')
                                : T('dl.download')),
                          ),
                        ],
                      ),
                    ),
                  ),
              const SizedBox(height: 8),
              _jobsPanel(),
            ],
          ),
        ),
      ],
    );
  }

  Widget _jobsPanel() {
    return GcCard(
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
          const SizedBox(height: 8),
          if (_jobs.isEmpty)
            GcHint(T('dl.bgEmpty'))
          else
            for (final job in _jobs)
              Padding(
                padding: const EdgeInsets.only(bottom: 10),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    Row(
                      children: <Widget>[
                        Expanded(
                          child: Text(
                            '${job.name}  v${job.version}',
                            style: const TextStyle(
                              fontSize: 12.5,
                              fontWeight: FontWeight.w600,
                              color: GcColors.ink,
                            ),
                          ),
                        ),
                        Text(
                          _stateLabel(job.state),
                          style: TextStyle(
                            fontSize: 11,
                            color: job.state == 'Failed'
                                ? GcColors.danger
                                : job.state == 'Completed'
                                    ? GcColors.ok
                                    : GcColors.faint,
                          ),
                        ),
                        const SizedBox(width: 8),
                        if (job.isActive)
                          TextButton(
                            onPressed: () => _jobAction(
                                widget.controller.api.cancelJob, job.id),
                            child: Text(T('dl.cancel')),
                          )
                        else if (job.canRetry)
                          TextButton(
                            onPressed: () => _jobAction(
                                widget.controller.api.retryJob, job.id),
                            child: Text(T('dl.retry')),
                          )
                        else
                          TextButton(
                            onPressed: () => _jobAction(
                                widget.controller.api.removeJob, job.id),
                            child: Text(T('dl.removeTask')),
                          ),
                      ],
                    ),
                    const SizedBox(height: 6),
                    LinearProgressIndicator(
                      value: job.progress.clamp(0, 1).toDouble(),
                      minHeight: 4,
                    ),
                    const SizedBox(height: 4),
                    Text(
                      job.error ?? job.detailText,
                      style: const TextStyle(
                          fontSize: 10.5, color: GcColors.faint),
                    ),
                  ],
                ),
              ),
        ],
      ),
    );
  }

  String _stateLabel(String state) {
    switch (state) {
      case 'Queued':
        return T('dl.status.queued');
      case 'Running':
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
  bool _synced = false;
  String? _status;

  @override
  void initState() {
    super.initState();
    widget.controller.reloadSettings();
  }

  void _syncFrom(SettingsInfo settings) {
    if (_synced) return;
    _memory = settings.memoryMb.toDouble();
    _memoryAuto = settings.memoryAuto;
    _fps = settings.renderFps;
    _autoUpdate = settings.autoUpdateOnStartup;
    _language = settings.language;
    _synced = true;
  }

  @override
  Widget build(BuildContext context) {
    final settings = widget.controller.settings;
    if (settings == null) {
      return const Center(child: CircularProgressIndicator(strokeWidth: 2.4));
    }
    _syncFrom(settings);

    return ListView(
      padding: const EdgeInsets.fromLTRB(26, 24, 26, 28),
      children: <Widget>[
        Text(
          T('set.title'),
          style: const TextStyle(
            fontSize: 20,
            fontWeight: FontWeight.w700,
            color: GcColors.ink,
          ),
        ),
        const SizedBox(height: 6),
        GcHint(T('set.subtitle')),
        const SizedBox(height: 18),
        _generalCard(settings),
        const SizedBox(height: 16),
        _memoryCard(settings),
        const SizedBox(height: 16),
        _sourcesCard(settings),
        const SizedBox(height: 16),
        _storageCard(settings),
        const SizedBox(height: 16),
        _updateCard(settings),
        const SizedBox(height: 16),
        _backendCard(settings),
        if (_status != null) ...<Widget>[
          const SizedBox(height: 14),
          GcHint(_status!),
        ],
      ],
    );
  }

  Widget _generalCard(SettingsInfo settings) {
    return GcCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          GcSectionTitle(T('set.general')),
          const SizedBox(height: 14),
          Row(
            children: <Widget>[
              SizedBox(width: 160, child: Text(T('set.language'),
                  style: const TextStyle(fontSize: 12.5, color: GcColors.muted))),
              DropdownButton<String>(
                value: _language,
                dropdownColor: GcColors.surfaceAlt,
                underline: const SizedBox.shrink(),
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
                  await widget.controller.saveSettings(
                      SettingsPatch(language: value));
                },
              ),
            ],
          ),
          const SizedBox(height: 10),
          Row(
            children: <Widget>[
              SizedBox(width: 160, child: Text(T('set.fps'),
                  style: const TextStyle(fontSize: 12.5, color: GcColors.muted))),
              Expanded(
                child: Slider(
                  value: _fps.toDouble(),
                  min: 24,
                  max: 120,
                  divisions: 4,
                  label: '$_fps',
                  onChanged: (value) => setState(() => _fps = value.round()),
                  onChangeEnd: (value) async {
                    await widget.controller
                        .saveSettings(SettingsPatch(renderFps: value.round()));
                    if (mounted) setState(() => _status = T('common.applied'));
                  },
                ),
              ),
              SizedBox(
                width: 46,
                child: Text('$_fps',
                    style: const TextStyle(
                        fontSize: 12.5, color: GcColors.ink)),
              ),
            ],
          ),
          GcHint(T('set.fps.desc')),
        ],
      ),
    );
  }

  Widget _memoryCard(SettingsInfo settings) {
    return GcCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          GcSectionTitle(T('set.memory')),
          const SizedBox(height: 8),
          GcHint(T('set.memory.desc')),
          const SizedBox(height: 14),
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
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  T('set.memory.auto'),
                  style: const TextStyle(fontSize: 12.5, color: GcColors.muted),
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
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
                    await widget.controller.saveSettings(
                        SettingsPatch(memoryMb: value.round()));
                    if (mounted) setState(() => _status = T('common.applied'));
                  },
                ),
              ),
              SizedBox(
                width: 96,
                child: Text(
                  '${_memory.round()} MB',
                  style: const TextStyle(fontSize: 12.5, color: GcColors.ink),
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Row(
            children: <Widget>[
              Text(
                '${T('set.memory.current')}: ${settings.memoryUsageText}',
                style: const TextStyle(fontSize: 11.5, color: GcColors.faint),
              ),
            ],
          ),
          const SizedBox(height: 6),
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
          const SizedBox(height: 8),
          GcHint(T('set.sources.desc')),
          const SizedBox(height: 12),
          for (final source in settings.sources)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: Row(
                children: <Widget>[
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: <Widget>[
                        Text(
                          source.name,
                          style: const TextStyle(
                            fontSize: 12.5,
                            fontWeight: FontWeight.w600,
                            color: GcColors.ink,
                          ),
                        ),
                        const SizedBox(height: 2),
                        Text(
                          source.url,
                          style: const TextStyle(
                              fontSize: 10.5, color: GcColors.faint),
                        ),
                      ],
                    ),
                  ),
                  Text(
                    source.enabled ? T('set.sourceEnabled') : '—',
                    style: const TextStyle(fontSize: 11, color: GcColors.faint),
                  ),
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
          const SizedBox(height: 12),
          _pathRow(T('set.modelDir'), settings.modelRoot),
          const SizedBox(height: 8),
          _pathRow(T('set.dataDir'), settings.dataRoot),
          const SizedBox(height: 12),
          Wrap(
            spacing: 10,
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

  Widget _pathRow(String label, String value) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        SizedBox(
          width: 160,
          child: Text(label,
              style: const TextStyle(fontSize: 12.5, color: GcColors.muted)),
        ),
        Expanded(
          child: SelectableText(
            value,
            style: const TextStyle(fontSize: 11.5, color: GcColors.faint),
          ),
        ),
      ],
    );
  }

  Widget _updateCard(SettingsInfo settings) {
    return GcCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          GcSectionTitle(T('update.title')),
          const SizedBox(height: 8),
          GcHint(T('set.checkUpdate.desc')),
          const SizedBox(height: 12),
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
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  T('update.auto'),
                  style: const TextStyle(fontSize: 12.5, color: GcColors.muted),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          _pathRow(T('update.source'), settings.updateRepository),
          const SizedBox(height: 12),
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
          const SizedBox(height: 8),
          GcHint(T('set.backend.desc')),
          const SizedBox(height: 12),
          _pathRow('API', widget.controller.api.baseUrl),
          const SizedBox(height: 8),
          _pathRow(T('set.version'), settings.version),
          const SizedBox(height: 8),
          _pathRow(T('set.platform'), settings.platform),
          const SizedBox(height: 8),
          _pathRow(
            T('set.backend.ui'),
            health == null
                ? '—'
                : health.uiAvailable
                    ? T('set.backend.uiOk')
                    : '${T('set.backend.uiBad')}${health.uiError == null ? '' : ' · ${health.uiError}'}',
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
      padding: const EdgeInsets.fromLTRB(26, 24, 26, 28),
      children: <Widget>[
        Text(
          T('nav.about'),
          style: const TextStyle(
            fontSize: 20,
            fontWeight: FontWeight.w700,
            color: GcColors.ink,
          ),
        ),
        const SizedBox(height: 18),
        if (_loading)
          const Padding(
            padding: EdgeInsets.symmetric(vertical: 40),
            child: Center(child: CircularProgressIndicator(strokeWidth: 2.4)),
          )
        else if (_error != null)
          GcCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                Text(T('common.error'),
                    style: const TextStyle(color: GcColors.danger)),
                const SizedBox(height: 8),
                GcHint(_error!),
                const SizedBox(height: 12),
                OutlinedButton(
                  onPressed: _load,
                  child: Text(T('common.retry')),
                ),
              ],
            ),
          )
        else ...<Widget>[
          GcCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                Text(
                  _about?.appName ?? 'Morris GC',
                  style: const TextStyle(
                    fontSize: 18,
                    fontWeight: FontWeight.w700,
                    color: GcColors.ink,
                  ),
                ),
                const SizedBox(height: 6),
                GcHint(T('app.tagline')),
                const SizedBox(height: 14),
                _row(T('set.version'), 'v${_about?.version ?? '—'}'),
                const SizedBox(height: 8),
                _row(T('set.platform'), _about?.platform ?? '—'),
                const SizedBox(height: 8),
                _row(T('about.architecture'), T('about.architectureValue')),
                const SizedBox(height: 8),
                _row(T('about.sourceLanguage'), _about?.sourceLanguage ?? 'C#'),
                const SizedBox(height: 8),
                _row(T('about.frontend'), _about?.frontend ?? 'Flutter'),
                const SizedBox(height: 8),
                _row(T('set.license'), T('set.licenseValue')),
                const SizedBox(height: 14),
                OutlinedButton(
                  onPressed: () async {
                    final url = _about?.repository;
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
            ),
          ),
          const SizedBox(height: 16),
          GcCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                GcSectionTitle(T('about.developers')),
                const SizedBox(height: 12),
                if ((_about?.contributors ?? const <ContributorInfo>[]).isEmpty)
                  GcHint(T('about.noContributors'))
                else
                  for (final contributor in _about!.contributors)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 10),
                      child: Row(
                        children: <Widget>[
                          CircleAvatar(
                            radius: 16,
                            backgroundColor: GcColors.surfaceAlt,
                            backgroundImage: contributor.avatarUrl == null
                                ? null
                                : NetworkImage(contributor.avatarUrl!),
                            child: contributor.avatarUrl == null
                                ? const Text('◎',
                                    style: TextStyle(
                                        fontSize: 12, color: GcColors.accent))
                                : null,
                          ),
                          const SizedBox(width: 12),
                          Expanded(
                            child: Text(
                              contributor.name,
                              style: const TextStyle(
                                fontSize: 12.5,
                                fontWeight: FontWeight.w600,
                                color: GcColors.ink,
                              ),
                            ),
                          ),
                          Text(
                            T('about.contributions',
                                <Object?>[contributor.contributions]),
                            style: const TextStyle(
                                fontSize: 11, color: GcColors.faint),
                          ),
                        ],
                      ),
                    ),
              ],
            ),
          ),
        ],
      ],
    );
  }

  Widget _row(String label, String value) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        SizedBox(
          width: 160,
          child: Text(label,
              style: const TextStyle(fontSize: 12.5, color: GcColors.muted)),
        ),
        Expanded(
          child: SelectableText(
            value,
            style: const TextStyle(fontSize: 12, color: GcColors.ink),
          ),
        ),
      ],
    );
  }
}
