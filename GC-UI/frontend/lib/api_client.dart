import 'dart:convert';
import 'dart:io';

import 'package:http/http.dart' as http;

/// 后端返回的错误（HTTP 非 2xx，或后端明确返回 ok=false）。
class ApiException implements Exception {
  ApiException(this.message, {this.statusCode});

  final String message;
  final int? statusCode;

  @override
  String toString() => message;
}

/// 已安装模型。
class ModelInfo {
  ModelInfo.fromJson(Map<String, dynamic> json)
      : id = json['id'] as String? ?? '',
        name = json['name'] as String? ?? '',
        description = json['description'] as String? ?? '',
        version = json['version'] as String? ?? '',
        author = json['author'] as String? ?? '',
        sizeBytes = (json['sizeBytes'] as num?)?.toInt() ?? 0,
        sizeText = json['sizeText'] as String? ?? '',
        tags = _stringList(json['tags']),
        hasPreview = json['hasPreview'] as bool? ?? false,
        previewUrl = json['previewUrl'] as String? ?? '',
        directory = json['directory'] as String? ?? '',
        dataDirectory = json['dataDirectory'] as String? ?? '';

  final String id;
  final String name;
  final String description;
  final String version;
  final String author;
  final int sizeBytes;
  final String sizeText;
  final List<String> tags;
  final bool hasPreview;
  final String previewUrl;
  final String directory;
  final String dataDirectory;

  String get metaText => 'v$version  ·  $sizeText';
}

/// 模型源。
class SourceInfo {
  SourceInfo.fromJson(Map<String, dynamic> json)
      : id = json['id'] as String? ?? '',
        name = json['name'] as String? ?? '',
        url = json['url'] as String? ?? '',
        enabled = json['enabled'] as bool? ?? false,
        builtIn = json['builtIn'] as bool? ?? false,
        host = json['host'] as String? ?? '';

  final String id;
  final String name;
  final String url;
  final bool enabled;
  final bool builtIn;
  final String host;
}

/// 远端可下载模型。
class RemoteModelInfo {
  RemoteModelInfo.fromJson(Map<String, dynamic> json)
      : id = json['id'] as String? ?? '',
        name = json['name'] as String? ?? '',
        description = json['description'] as String? ?? '',
        version = json['version'] as String? ?? '',
        sizeBytes = (json['sizeBytes'] as num?)?.toInt() ?? 0,
        sizeText = json['sizeText'] as String? ?? '',
        sourceId = json['sourceId'] as String? ?? '',
        installed = json['installed'] as bool? ?? false,
        installedVersion = json['installedVersion'] as String?;

  final String id;
  final String name;
  final String description;
  final String version;
  final int sizeBytes;
  final String sizeText;
  final String sourceId;
  final bool installed;
  final String? installedVersion;
}

/// 一次远端源索引结果。
class SourceIndexInfo {
  SourceIndexInfo.fromJson(Map<String, dynamic> json)
      : sourceId = json['sourceId'] as String? ?? '',
        root = json['root'] as String? ?? '',
        fromCache = json['fromCache'] as bool? ?? false,
        error = json['error'] as String?,
        resolvedFrom = json['resolvedFrom'] as String? ?? '',
        models = (json['models'] as List<dynamic>? ?? const [])
            .map((e) => RemoteModelInfo.fromJson(e as Map<String, dynamic>))
            .toList();

  final String sourceId;
  final String root;
  final bool fromCache;
  final String? error;
  final String resolvedFrom;
  final List<RemoteModelInfo> models;
}

/// 下载任务。
class JobInfo {
  JobInfo.fromJson(Map<String, dynamic> json)
      : id = json['id'] as String? ?? '',
        modelId = json['modelId'] as String? ?? '',
        name = json['name'] as String? ?? '',
        version = json['version'] as String? ?? '',
        state = json['state'] as String? ?? 'Queued',
        progress = (json['progress'] as num?)?.toDouble() ?? 0,
        statusText = json['statusText'] as String? ?? '',
        detailText = json['detailText'] as String? ?? '',
        error = json['error'] as String?,
        isActive = json['isActive'] as bool? ?? false,
        canRetry = json['canRetry'] as bool? ?? false,
        isDone = json['isDone'] as bool? ?? false;

  final String id;
  final String modelId;
  final String name;
  final String version;
  final String state;
  final double progress;
  final String statusText;
  final String detailText;
  final String? error;
  final bool isActive;
  final bool canRetry;
  final bool isDone;
}

/// 界面语言选项。
class LanguageInfo {
  LanguageInfo.fromJson(Map<String, dynamic> json)
      : code = json['code'] as String? ?? '',
        label = json['label'] as String? ?? '';

  final String code;
  final String label;
}

/// 设置。
class SettingsInfo {
  SettingsInfo.fromJson(Map<String, dynamic> json)
      : language = json['language'] as String? ?? 'auto',
        theme = json['theme'] as String? ?? 'light',
        accentColor = json['accentColor'] as String? ?? '#8FB6FF',
        renderFps = (json['renderFps'] as num?)?.toInt() ?? 120,
        memoryMb = (json['memoryMb'] as num?)?.toInt() ?? 2048,
        memoryAuto = json['memoryAuto'] as bool? ?? true,
        autoUpdateOnStartup = json['autoUpdateOnStartup'] as bool? ?? true,
        requireSignedManifest = json['requireSignedManifest'] as bool? ?? true,
        updateRepository = json['updateRepository'] as String? ?? '',
        updateLocalRoot = json['updateLocalRoot'] as String?,
        modelRootOverride = json['modelRootOverride'] as String?,
        memoryBudgetMb = (json['memoryBudgetMb'] as num?)?.toInt() ?? 0,
        memoryUsageText = json['memoryUsageText'] as String? ?? '',
        modelRoot = json['modelRoot'] as String? ?? '',
        dataRoot = json['dataRoot'] as String? ?? '',
        settingsFile = json['settingsFile'] as String? ?? '',
        version = json['version'] as String? ?? '',
        platform = json['platform'] as String? ?? '',
        sources = (json['sources'] as List<dynamic>? ?? const [])
            .map((e) => SourceInfo.fromJson(e as Map<String, dynamic>))
            .toList(),
        languages = (json['languages'] as List<dynamic>? ?? const [])
            .map((e) => LanguageInfo.fromJson(e as Map<String, dynamic>))
            .toList();

  final String language;
  final String theme;
  final String accentColor;
  final int renderFps;
  final int memoryMb;
  final bool memoryAuto;
  final bool autoUpdateOnStartup;
  final bool requireSignedManifest;
  final String updateRepository;
  final String? updateLocalRoot;
  final String? modelRootOverride;
  final int memoryBudgetMb;
  final String memoryUsageText;
  final String modelRoot;
  final String dataRoot;
  final String settingsFile;
  final String version;
  final String platform;
  final List<SourceInfo> sources;
  final List<LanguageInfo> languages;
}

/// 设置补丁：只提交要改的字段。
class SettingsPatch {
  const SettingsPatch({
    this.language,
    this.theme,
    this.accentColor,
    this.renderFps,
    this.memoryMb,
    this.memoryAuto,
    this.autoUpdateOnStartup,
    this.requireSignedManifest,
    this.updateRepository,
    this.updateLocalRoot,
    this.modelRootOverride,
  });

  final String? language;
  final String? theme;
  final String? accentColor;
  final int? renderFps;
  final int? memoryMb;
  final bool? memoryAuto;
  final bool? autoUpdateOnStartup;
  final bool? requireSignedManifest;
  final String? updateRepository;
  final String? updateLocalRoot;
  final String? modelRootOverride;

  Map<String, dynamic> toJson() {
    final map = <String, dynamic>{};
    if (language != null) map['language'] = language;
    if (theme != null) map['theme'] = theme;
    if (accentColor != null) map['accentColor'] = accentColor;
    if (renderFps != null) map['renderFps'] = renderFps;
    if (memoryMb != null) map['memoryMb'] = memoryMb;
    if (memoryAuto != null) map['memoryAuto'] = memoryAuto;
    if (autoUpdateOnStartup != null) map['autoUpdateOnStartup'] = autoUpdateOnStartup;
    if (requireSignedManifest != null) map['requireSignedManifest'] = requireSignedManifest;
    if (updateRepository != null) map['updateRepository'] = updateRepository;
    if (updateLocalRoot != null) map['updateLocalRoot'] = updateLocalRoot;
    if (modelRootOverride != null) map['modelRootOverride'] = modelRootOverride;
    return map;
  }
}

/// 健康检查。
class HealthInfo {
  HealthInfo.fromJson(Map<String, dynamic> json)
      : ok = json['ok'] as bool? ?? false,
        version = json['version'] as String? ?? '',
        platform = json['platform'] as String? ?? '',
        uiAvailable = json['uiAvailable'] as bool? ?? false,
        uiError = json['uiError'] as String?;

  final bool ok;
  final String version;
  final String platform;
  final bool uiAvailable;
  final String? uiError;
}

/// 一次更新检查的结果。
class UpdateCheckInfo {
  UpdateCheckInfo.fromJson(Map<String, dynamic> json)
      : outcome = json['outcome'] as String? ?? '',
        message = json['message'] as String? ?? '',
        updatedModels = (json['updatedModels'] as num?)?.toInt() ?? 0,
        modelNames = _stringList(json['modelNames']),
        launcherMessage = json['launcherMessage'] as String?,
        error = json['error'] as String?,
        launcherLatest = json['launcherLatest'] as String?,
        sourceName = json['sourceName'] as String? ?? '';

  final String outcome;
  final String message;
  final int updatedModels;
  final List<String> modelNames;
  final String? launcherMessage;
  final String? error;
  final String? launcherLatest;
  final String sourceName;
}

/// 贡献者。
class ContributorInfo {
  ContributorInfo.fromJson(Map<String, dynamic> json)
      : name = json['name'] as String? ?? '',
        contributions = (json['contributions'] as num?)?.toInt() ?? 0,
        avatarUrl = json['avatarUrl'] as String?;

  final String name;
  final int contributions;
  final String? avatarUrl;
}

/// 关于页数据。
class AboutInfo {
  AboutInfo.fromJson(Map<String, dynamic> json)
      : appName = json['appName'] as String? ?? '',
        version = json['version'] as String? ?? '',
        platform = json['platform'] as String? ?? '',
        repository = json['repository'] as String? ?? '',
        sourceLanguage = json['sourceLanguage'] as String? ?? 'C#',
        frontend = json['frontend'] as String? ?? 'Flutter',
        contributors = (json['contributors'] as List<dynamic>? ?? const [])
            .map((e) => ContributorInfo.fromJson(e as Map<String, dynamic>))
            .toList();

  final String appName;
  final String version;
  final String platform;
  final String repository;

  /// 源码语言（后端 / 模型层），由后端 `/api/about` 返回。
  final String sourceLanguage;

  /// 前端框架，由后端 `/api/about` 返回。
  final String frontend;

  final List<ContributorInfo> contributors;
}

/// 通用操作结果。
class ActionResult {
  ActionResult.fromJson(Map<String, dynamic> json)
      : ok = json['ok'] as bool? ?? false,
        message = json['message'] as String? ?? '';

  final bool ok;
  final String message;
}

List<String> _stringList(dynamic value) =>
    (value as List<dynamic>? ?? const []).map((e) => e.toString()).toList();

/// GC-UI 后端客户端。
///
/// 后端是本地伴随进程，端口由启动器（GC-BOOT）选定后通过 `GC_UI_PORT`
/// 环境变量传进来 —— 这样端口被占用时换端口不用重新编译前端。
/// 也可用 `--dart-define=GC_UI_BASE_URL=http://127.0.0.1:12345` 强制指定。
class GcApi {
  GcApi({String? baseUrl})
      : baseUrl = baseUrl ?? defaultBaseUrl;

  /// 复用同一个 HTTP 连接：轮询（下载任务每秒一次）时每次都新建连接
  /// 会不断握手，桌面端也能明显看到延迟抖动。
  static final http.Client _client = http.Client();

  /// 兜底默认端口，与后端 GC.Services.GcUiContract.DefaultPort 保持一致。
  static const int fallbackPort = 17890;

  static String get defaultBaseUrl {
    const fromDefine = String.fromEnvironment('GC_UI_BASE_URL');
    if (fromDefine.isNotEmpty) return fromDefine;

    final fromEnvUrl = Platform.environment['GC_UI_BASE_URL'];
    if (fromEnvUrl != null && fromEnvUrl.isNotEmpty) return fromEnvUrl;

    // 启动器通过 GC_UI_PORT 传端口（主路径）。
    final port = int.tryParse(Platform.environment['GC_UI_PORT'] ?? '');
    if (port != null) return 'http://127.0.0.1:$port';

    // 手动起后端 / 前端调试时没有环境变量：读后端写的 .GC/ui-backend.json。
    final discovered = _endpointFileUrl();
    if (discovered != null) return discovered;

    return 'http://127.0.0.1:$fallbackPort';
  }

  /// 从运行目录下的 `.GC/ui-backend.json` 发现后端地址。
  ///
  /// 后端每次启动都会写这个文件（port / pid / url）。只认 24 小时内的记录：
  /// 更早的多半是上次遗留的，连过去只会得到一个「无法连接」。
  static String? _endpointFileUrl() {
    try {
      final file = File('${Directory.current.path}/.GC/ui-backend.json');
      if (!file.existsSync()) return null;

      final age = DateTime.now().difference(file.lastModifiedSync());
      if (age.inHours >= 24) return null;

      final decoded = jsonDecode(file.readAsStringSync());
      if (decoded is! Map<String, dynamic>) return null;

      final url = decoded['url'];
      if (url is String && url.startsWith('http://127.0.0.1:')) return url;

      final port = (decoded['port'] as num?)?.toInt();
      if (port != null && port > 0 && port < 65536) {
        return 'http://127.0.0.1:$port';
      }
    } catch (_) {
      // 文件损坏 / 无权限：退回默认端口即可
    }
    return null;
  }

  final String baseUrl;

  Uri _uri(String path, [Map<String, String>? query]) =>
      Uri.parse('$baseUrl$path').replace(queryParameters: query);

  Future<dynamic> _send(String method, String path,
      {Map<String, String>? query, Object? body}) async {
    final uri = _uri(path, query);
    late http.Response response;
    try {
      switch (method) {
        case 'GET':
          response = await _client.get(uri).timeout(const Duration(seconds: 30));
          break;
        case 'POST':
          response = await _client
              .post(uri,
                  headers: const {'Content-Type': 'application/json'},
                  body: body == null ? null : jsonEncode(body))
              .timeout(const Duration(seconds: 120));
          break;
        case 'DELETE':
          response = await _client.delete(uri).timeout(const Duration(seconds: 30));
          break;
        default:
          throw ApiException('不支持的方法 $method');
      }
    } on ApiException {
      rethrow;
    } catch (error) {
      throw ApiException('无法连接后端（$baseUrl）：$error');
    }

    final text = utf8.decode(response.bodyBytes);
    dynamic decoded;
    if (text.isNotEmpty) {
      try {
        decoded = jsonDecode(text);
      } catch (_) {
        decoded = null;
      }
    }

    if (response.statusCode >= 400) {
      final message = decoded is Map<String, dynamic>
          ? (decoded['message'] as String? ?? 'HTTP ${response.statusCode}')
          : 'HTTP ${response.statusCode}';
      throw ApiException(message, statusCode: response.statusCode);
    }

    return decoded;
  }

  List<Map<String, dynamic>> _asList(dynamic value) =>
      (value as List<dynamic>? ?? const [])
          .map((e) => e as Map<String, dynamic>)
          .toList();

  Future<HealthInfo> health() async =>
      HealthInfo.fromJson(await _send('GET', '/api/health') as Map<String, dynamic>);

  Future<List<ModelInfo>> models() async =>
      _asList(await _send('GET', '/api/models')).map(ModelInfo.fromJson).toList();

  Future<ActionResult> launchModel(String id) async => ActionResult.fromJson(
      await _send('POST', '/api/models/$id/launch') as Map<String, dynamic>);

  Future<ActionResult> deleteModel(String id) async => ActionResult.fromJson(
      await _send('DELETE', '/api/models/$id') as Map<String, dynamic>);

  Future<SettingsInfo> settings() async =>
      SettingsInfo.fromJson(await _send('GET', '/api/settings') as Map<String, dynamic>);

  Future<SettingsInfo> saveSettings(SettingsPatch patch) async =>
      SettingsInfo.fromJson(await _send('POST', '/api/settings', body: patch.toJson())
          as Map<String, dynamic>);

  Future<List<SourceInfo>> sources() async =>
      _asList(await _send('GET', '/api/sources')).map(SourceInfo.fromJson).toList();

  Future<SourceIndexInfo> remoteModels({String? sourceId, bool force = false}) async =>
      SourceIndexInfo.fromJson(await _send('GET', '/api/remote/models', query: {
        if (sourceId != null) 'sourceId': sourceId,
        'force': force.toString(),
      }) as Map<String, dynamic>);

  Future<List<JobInfo>> jobs() async =>
      _asList(await _send('GET', '/api/download/jobs')).map(JobInfo.fromJson).toList();

  Future<JobInfo> startDownload(String sourceId, String modelId) async =>
      JobInfo.fromJson(await _send('POST', '/api/download',
          body: {'sourceId': sourceId, 'modelId': modelId}) as Map<String, dynamic>);

  Future<JobInfo> cancelJob(String id) async => JobInfo.fromJson(
      await _send('POST', '/api/download/$id/cancel') as Map<String, dynamic>);

  Future<JobInfo> retryJob(String id) async => JobInfo.fromJson(
      await _send('POST', '/api/download/$id/retry') as Map<String, dynamic>);

  Future<JobInfo> removeJob(String id) async => JobInfo.fromJson(
      await _send('POST', '/api/download/$id/remove') as Map<String, dynamic>);

  Future<ActionResult> clearFinished() async => ActionResult.fromJson(
      await _send('POST', '/api/download/clear-finished') as Map<String, dynamic>);

  Future<UpdateCheckInfo> checkUpdate({bool force = true}) async =>
      UpdateCheckInfo.fromJson(
          await _send('POST', '/api/update/check', body: {'force': force})
              as Map<String, dynamic>);

  Future<AboutInfo> about() async =>
      AboutInfo.fromJson(await _send('GET', '/api/about') as Map<String, dynamic>);

  Future<ActionResult> openFolder(String path) async => ActionResult.fromJson(
      await _send('POST', '/api/shell/open-folder', body: {'path': path})
          as Map<String, dynamic>);

  Future<ActionResult> openUrl(String url) async => ActionResult.fromJson(
      await _send('POST', '/api/shell/open-url', body: {'url': url})
          as Map<String, dynamic>);

  /// 模型封面图地址（直接给 Image.network 用）。
  String previewUrl(ModelInfo model) => '$baseUrl${model.previewUrl}';
}
