import 'dart:async';

import 'package:flutter/foundation.dart';

import 'api_client.dart';
import 'i18n.dart';
import 'theme.dart';

/// 外壳状态：后端客户端 + 设置 + 健康状态 + 更新状态条。
///
/// 各页面只读这里的数据、通过这里的方法改后端，避免每页各持一份状态。
class ShellController extends ChangeNotifier {
  ShellController({GcApi? api}) : api = api ?? GcApi();

  final GcApi api;

  int index = 0;
  SettingsInfo? settings;
  HealthInfo? health;
  String? loadError;

  bool updateBarVisible = false;
  bool updateBusy = false;
  double updateProgress = 0;
  String updateStatus = '';
  String updateDetail = '';
  String launcherNotice = '';

  /// 模型库版本号：安装 / 删除 / 下载完成后自增，首页据此重新拉列表。
  ///
  /// 首页被 `Offstage` 保活，切走再切回不会重建；没有这个信号时，
  /// 下载完模型回到首页仍然显示旧的空列表。
  int modelsRevision = 0;

  Timer? _retryTimer;
  int _retryAttempt = 0;

  bool get backendReady => settings != null;

  Future<void> bootstrap() async {
    _retryTimer?.cancel();

    HealthInfo? healthResult;
    SettingsInfo? settingsResult;
    Object? firstError;

    // 两个请求互不依赖，并行发；各自捕获异常，避免一方失败时
    // 另一个的 Future 抛成未处理异常（Flutter 会在控制台打出恶性警告）。
    await Future.wait<void>(<Future<void>>[
      () async {
        try {
          healthResult = await api.health();
        } catch (error) {
          firstError ??= error;
        }
      }(),
      () async {
        try {
          settingsResult = await api.settings();
        } catch (error) {
          firstError ??= error;
        }
      }(),
    ]);

    if (healthResult != null) health = healthResult;
    if (settingsResult != null) {
      settings = settingsResult;
      _applySettings(settingsResult!);
      // 主题色现在归用户账号所有（设置页不再提供），启动时按当前用户恢复
      unawaited(_applyUserTheme());
    }

    if (firstError != null && settingsResult == null) {
      loadError = firstError.toString();
      _scheduleRetry();
    } else {
      loadError = null;
      _retryAttempt = 0;
    }

    notifyListeners();
  }

  /// 后端没起来时自动重试（2s、4s、8s…最多 5 次），不必让用户手动点。
  void _scheduleRetry() {
    if (_retryAttempt >= 5) return;
    final seconds = 1 << _retryAttempt; // 1,2,4,8,16
    _retryAttempt++;
    _retryTimer?.cancel();
    _retryTimer = Timer(Duration(seconds: seconds), () {
      if (settings == null) {
        // 后端可能刚换端口（端口占用）或刚写好握手文件：重试前重新解析一次
        api.rediscover();
        bootstrap();
      }
    });
  }

  void _applySettings(SettingsInfo value) {
    I18n.setLanguage(value.language);
    // 强调色不再来自设置：由用户账号的 theme_color 决定（见 _applyUserTheme）
    GcPalette.applyTheme(value.theme);
  }

  /// 按当前登录用户的 theme_color 应用强调色；未登录时保持默认。
  Future<void> applyUserTheme() => _applyUserTheme();

  Future<void> _applyUserTheme() async {
    try {
      final resp = await api.me();
      final hex = resp?['theme_color'] as String?;
      if (hex != null && hex.isNotEmpty) GcPalette.apply('#$hex');
    } catch (_) {
      // 后端没起来或未登录：保持当前颜色即可
    }
  }

  void goTo(int value) {
    if (index == value) return;
    index = value;
    notifyListeners();
  }

  // 模型视图已回退为后端开 Avalonia 独立窗口（见 AvaloniaHost），
  // Flutter 内嵌的 activeModelId / openModel / ModelViewPage 一套已移除。

  /// 模型库有变化（安装 / 删除 / 下载完成）：通知首页刷新。
  void modelsChanged() {
    modelsRevision++;
    notifyListeners();
  }

  Future<void> reloadSettings() async {
    try {
      settings = await api.settings();
      loadError = null;
      _applySettings(settings!);
    } catch (error) {
      loadError = error.toString();
    }
    notifyListeners();
  }

  Future<void> saveSettings(SettingsPatch patch) async {
    try {
      settings = await api.saveSettings(patch);
      loadError = null;
      _applySettings(settings!);
    } catch (error) {
      loadError = error.toString();
    }
    notifyListeners();
  }

  void setUpdateStatus({
    bool? visible,
    bool? busy,
    double? progress,
    String? status,
    String? detail,
    String? notice,
  }) {
    if (visible != null) updateBarVisible = visible;
    if (busy != null) updateBusy = busy;
    if (progress != null) updateProgress = progress;
    if (status != null) updateStatus = status;
    if (detail != null) updateDetail = detail;
    if (notice != null) launcherNotice = notice;
    notifyListeners();
  }

  /// 检查模型 / 启动器更新（设置页与状态条共用）。
  Future<void> checkUpdate({bool force = true}) async {
    if (updateBusy) return;
    setUpdateStatus(
      visible: true,
      busy: true,
      progress: 0,
      status: T('update.checking'),
      detail: '',
      notice: '',
    );

    try {
      final result = await api.checkUpdate(force: force);
      setUpdateStatus(
        busy: false,
        progress: 1,
        status: result.message.isEmpty ? T('update.done') : result.message,
        detail: result.error ?? '',
        notice: result.launcherMessage ?? '',
      );
      if (result.updatedModels > 0) modelsChanged();
    } catch (error) {
      setUpdateStatus(
        busy: false,
        status: T('update.offline'),
        detail: error.toString(),
      );
    }
  }

  @override
  void dispose() {
    _retryTimer?.cancel();
    super.dispose();
  }
}
