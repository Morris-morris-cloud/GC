import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:gc_ui/api_client.dart';

/// 前后端契约联调测试。
///
/// 需要 GC-UI 后端正在运行（`dotnet run --project GC-UI/backend/GC.UI.Server`）。
/// 后端没起时整组跳过，不会让 CI 变红。
///
/// 注意：flutter_test 默认把所有 HTTP 请求拦成 400，这里显式放开真实网络。
void main() {
  setUpAll(() {
    HttpOverrides.global = null;
  });

  final api = GcApi(
    baseUrl: const String.fromEnvironment(
      'GC_UI_BASE_URL',
      defaultValue: 'http://127.0.0.1:17890',
    ),
  );

  Future<bool> backendUp() async {
    try {
      await api.health();
      return true;
    } catch (_) {
      return false;
    }
  }

  test('health / settings / models / sources / jobs 都能解析', () async {
    if (!await backendUp()) {
      markTestSkipped('后端未运行，跳过联调测试');
      return;
    }

    final health = await api.health();
    expect(health.ok, isTrue);
    expect(health.version, isNotEmpty);
    expect(health.platform, isNotEmpty);

    final settings = await api.settings();
    expect(settings.modelRoot, isNotEmpty);
    expect(settings.dataRoot, isNotEmpty);
    expect(settings.renderFps, greaterThan(0));
    expect(settings.memoryMb, greaterThan(0));
    expect(settings.languages, isNotEmpty);
    expect(settings.sources, isNotEmpty);

    final models = await api.models();
    for (final model in models) {
      expect(model.id, isNotEmpty);
      expect(model.name, isNotEmpty);
    }

    final sources = await api.sources();
    expect(sources, isNotEmpty);
    expect(sources.first.id, isNotEmpty);

    final jobs = await api.jobs();
    expect(jobs, isA<List<JobInfo>>());
  });

  test('设置补丁能写回并读出一致', () async {
    if (!await backendUp()) {
      markTestSkipped('后端未运行，跳过联调测试');
      return;
    }

    final before = await api.settings();
    final updated = await api.saveSettings(
      SettingsPatch(renderFps: before.renderFps == 60 ? 90 : 60),
    );
    expect(updated.renderFps, before.renderFps == 60 ? 90 : 60);

    final restored = await api.saveSettings(
      SettingsPatch(renderFps: before.renderFps),
    );
    expect(restored.renderFps, before.renderFps);
  });

  test('越界路径被后端拒绝', () async {
    if (!await backendUp()) {
      markTestSkipped('后端未运行，跳过联调测试');
      return;
    }

    await expectLater(
      api.openFolder('/etc'),
      throwsA(isA<ApiException>()),
    );
  });
}
