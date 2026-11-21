import 'package:flutter/material.dart';

import '../api_client.dart';
import '../controller.dart';
import '../i18n.dart';
import '../theme.dart';

// ---------------------------------------------------------------------------
// 设置页（从原 pages.dart 拆出，公开类名不变）
// ---------------------------------------------------------------------------

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
