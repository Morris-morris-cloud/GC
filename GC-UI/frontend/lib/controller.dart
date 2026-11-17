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

  bool get backendReady => settings != null;

  Future<void> bootstrap() async {
    try {
      // 健康检查与设置并行请求：两者互不依赖，串行会白白多等一次网络往返。
      final healthFuture = api.health();
      final settingsFuture = api.settings();
      health = await healthFuture;
      settings = await settingsFuture;
      loadError = null;
      _applySettings(settings!);
    } catch (error) {
      loadError = error.toString();
    }
    notifyListeners();
  }

  void _applySettings(SettingsInfo value) {
    I18n.setLanguage(value.language);
    GcPalette.apply(value.accentColor);
  }

  void goTo(int value) {
    if (index == value) return;
    index = value;
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
    } catch (error) {
      setUpdateStatus(
        busy: false,
        status: T('update.offline'),
        detail: error.toString(),
      );
    }
  }
}
