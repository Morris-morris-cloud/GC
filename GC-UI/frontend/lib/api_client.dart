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

/// 「重新下载启动器」的结果。
///
/// State: Ready（已下载并通过 sha256 校验）/ UpToDate（已是最新，无需重下）/ Failed。
class LauncherRedownloadInfo {
  LauncherRedownloadInfo.fromJson(Map<String, dynamic> json)
      : state = json['state'] as String? ?? '',
        version = json['version'] as String? ?? '',
        message = json['message'] as String? ?? '',
        steps = ((json['steps'] as List<dynamic>?) ?? const <dynamic>[])
            .map((e) => e.toString())
            .toList();

  final String state;
  final String version;
  final String message;
  final List<String> steps;

  bool get ok => state == 'Ready' || state == 'UpToDate';
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
        uiError = json['uiError'] as String?,
        sessionProof = json['sessionProof'] as String?;

  final bool ok;
  final String version;
  final String platform;
  final bool uiAvailable;
  final String? uiError;
  /// 后端会话令牌的指纹；前端可用于诊断「是否连到了正确的后端」。
  final String? sessionProof;
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

/// 握手文件内容。
class _Endpoint {
  const _Endpoint(this.url, this.token);

  final String url;
  final String? token;
}

/// GC-UI 后端客户端。
///
/// 后端是本地伴随进程，端口由启动器（GC-BOOT）选定后通过 `GC_UI_PORT`
/// 环境变量传进来 —— 这样端口被占用时换端口不用重新编译前端。
/// 也可用 `--dart-define=GC_UI_BASE_URL=http://127.0.0.1:12345` 强制指定。
class GcApi {
  GcApi({String? baseUrl})
      : baseUrl = baseUrl ?? defaultBaseUrl;

  /// 后端地址。可被 [rediscover] 更新（后端换端口 / 首次靠握手文件发现）。
  String baseUrl;

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

    // 没有环境变量（例如直接从 Finder 打开 GC-UI.app）：读后端写的握手文件。
    final discovered = _readEndpointFile();
    if (discovered != null) return discovered.url;

    return 'http://127.0.0.1:$fallbackPort';
  }

  /// 后端启动时写的握手文件：地址 + 会话令牌。
  static _Endpoint? _endpoint;

  /// 依次尝试的运行目录。打包后 GC-UI.app 在 GC.app/Contents/MacOS/ 下，
  /// 直接 Finder 打开时 Directory.current 可能是 `/`，所以要把可执行文件周围都试一遍。
  static List<String> _candidateRoots() {
    final roots = <String>[];
    void add(String path) {
      if (path.isEmpty) return;
      final root = path.endsWith('/') ? path.substring(0, path.length - 1) : path;
      if (!roots.contains(root)) roots.add(root);
    }

    add(Platform.environment['GC_UI_HOME'] ?? '');
    try {
      add(Directory.current.path);
    } catch (_) {}

    try {
      var dir = File(Platform.resolvedExecutable).parent;
      // GC-UI.app/Contents/MacOS -> 逐级向上都可能是 .GC 所在目录
      for (var i = 0; i < 4; i++) {
        add(dir.path);
        if (dir.parent.path == dir.path) break;
        dir = dir.parent;
      }
    } catch (_) {}

    return roots;
  }

  static _Endpoint? _readEndpointFile({bool refresh = false}) {
    if (!refresh && _endpoint != null) return _endpoint;

    for (final root in _candidateRoots()) {
      try {
        final file = File('$root/.GC/ui-backend.json');
        try {
          final logFile = File('${Directory.systemTemp.path}/gc-api-debug.log');
          logFile.writeAsStringSync('[api] 检查: $root/.GC/ui-backend.json (exists=${file.existsSync()})\n', mode: FileMode.append);
        } catch (_) {}
        if (!file.existsSync()) continue;

        // 只认 24 小时内的记录：更早的多半是上次遗留，连过去只会得到「无法连接」
        final age = DateTime.now().difference(file.lastModifiedSync());
        if (age.inHours >= 24) continue;

        final decoded = jsonDecode(file.readAsStringSync());
        if (decoded is! Map<String, dynamic>) continue;

        String? url;
        final rawUrl = decoded['url'];
        if (rawUrl is String && rawUrl.startsWith('http://127.0.0.1:')) {
          url = rawUrl;
        } else {
          final port = (decoded['port'] as num?)?.toInt();
          if (port != null && port > 0 && port < 65536) {
            url = 'http://127.0.0.1:$port';
          }
        }
        if (url == null) continue;

        final token = decoded['token'];
        return _endpoint = _Endpoint(url, token is String && token.isNotEmpty ? token : null);
      } catch (_) {
        // 文件损坏 / 无权限：试下一个候选目录
      }
    }
    return _endpoint = null;
  }

  /// 重新解析后端地址与令牌（后端重启换端口、或环境变量缺失时用）。
  /// 返回 true 表示地址或令牌发生了变化，值得重试一次。
  bool rediscover() {
    final before = (_endpoint?.url, _sessionToken);
    _endpoint = null;
    final endpoint = _readEndpointFile(refresh: true);

    if (endpoint?.token != null && endpoint!.token != _sessionToken) {
      _sessionToken = endpoint.token;
    }

    final envUrl = Platform.environment['GC_UI_BASE_URL'];
    final envPort = int.tryParse(Platform.environment['GC_UI_PORT'] ?? '');
    final resolved = (envUrl != null && envUrl.isNotEmpty)
        ? envUrl
        : (envPort != null
            ? 'http://127.0.0.1:$envPort'
            : (endpoint?.url ?? 'http://127.0.0.1:$fallbackPort'));

    if (resolved != baseUrl) {
      baseUrl = resolved;
      return true;
    }
    return (before.$1 != _endpoint?.url) || (before.$2 != _sessionToken);
  }

  /// 启动器注入的一次性会话令牌：环境变量优先，其次握手文件。
  /// 后端在发布包模式下校验它，防止前端连到同机伪造的「后端」。
  static String? _sessionToken = _readSessionToken();

  static String? _readSessionToken() {
    const fromDefine = String.fromEnvironment('GC_UI_TOKEN');
    if (fromDefine.isNotEmpty) return fromDefine;
    final fromEnv = Platform.environment['GC_UI_TOKEN'];
    if (fromEnv != null && fromEnv.isNotEmpty) return fromEnv;
    // 环境变量没传进来（Finder 直接打开 GC-UI.app）：用握手文件里的令牌
    return _readEndpointFile()?.token;
  }

  /// 是否拿到了会话令牌（诊断用）。
  bool get hasSessionToken => _sessionToken != null;

  /// 用户 session token：登录时后端返回，只放内存。
  /// 持久化由后端负责（写 .GC/user/user.yaml），下次启动后端会用它自动登录，
  /// 前端不需要自己去猜 .GC 在哪。
  static String? _userToken;
  static String? get userToken => _userToken;

  /// 带会话令牌的请求头；没有令牌时返回 const 空 map（调试/独立后端模式）。
  Map<String, String> get _authHeaders => {
    if (_sessionToken != null) 'X-GC-UI-Token': _sessionToken!,
    if (_userToken != null) 'X-User-Token': _userToken!,
  };

  Uri _uri(String path, [Map<String, String>? query]) =>
      Uri.parse('$baseUrl$path').replace(queryParameters: query);

  Future<dynamic> _send(String method, String path,
      {Map<String, String>? query,
      Object? body,
      Duration? timeout}) async {
    final uri = _uri(path, query);
    final auth = _authHeaders;
    late http.Response response;
    try {
      switch (method) {
        case 'GET':
          response = await _client.get(uri, headers: auth)
              .timeout(timeout ?? const Duration(seconds: 30));
          break;
        case 'POST':
          response = await _client
              .post(uri,
                  headers: <String, String>{...auth, 'Content-Type': 'application/json'},
                  body: body == null ? null : jsonEncode(body))
              .timeout(timeout ?? const Duration(seconds: 120));
          break;
        case 'DELETE':
          response = await _client.delete(uri, headers: auth)
              .timeout(timeout ?? const Duration(seconds: 30));
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

  /// 重新下载本机启动器包，返回时后端已完成 sha256 完整性校验。
  Future<LauncherRedownloadInfo> redownloadLauncher() async =>
      LauncherRedownloadInfo.fromJson(await _send(
        'POST',
        '/api/launcher/redownload',
        timeout: const Duration(minutes: 10),
      ) as Map<String, dynamic>);

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

  /// 模型封面图地址（直接给 Image.network 用，带不上自定义头，令牌走 query 参数）。
  String previewUrl(ModelInfo model) {
    if (_sessionToken == null) return '$baseUrl${model.previewUrl}';
    final uri = Uri.parse('$baseUrl${model.previewUrl}');
    return uri.replace(queryParameters: {
      ...uri.queryParameters,
      'X-GC-UI-Token': _sessionToken!,
    }).toString();
  }

  // ==================== 用户系统 ====================

  /// 取一个人机验证题（算术题，5 分钟内有效、一次性）。
  Future<CaptchaInfo> captcha() async {
    final r = await _send('GET', '/api/user/captcha');
    return CaptchaInfo.fromJson(r as Map<String, dynamic>);
  }

  /// 当前登录用户；未登录返回 null（后端会用 .GC/user/user.yaml 自动登录）。
  Future<Map<String, dynamic>?> me() async {
    try {
      final r = await _send('GET', '/api/user/me');
      return r as Map<String, dynamic>?;
    } on ApiException catch (e) {
      if (e.statusCode == 401 || e.statusCode == 403) {
        _userToken = null;
        return null;
      }
      rethrow;
    }
  }

  Future<Map<String, dynamic>> register(String username, String password,
      {required String captchaId, required String captchaAnswer}) async {
    final r = await _send('POST', '/api/user/register', body: {
      'username': username,
      'password': password,
      'captchaId': captchaId,
      'captcha': captchaAnswer,
    });
    return r as Map<String, dynamic>;
  }

  Future<Map<String, dynamic>> login(String username, String password) async {
    final r = await _send('POST', '/api/user/login',
        body: {'username': username, 'password': password});
    final result = r as Map<String, dynamic>;
    _userToken = result['session_token'] as String?;
    return result;
  }

  /// 退出登录：清掉后端的 user.yaml 与内存 token。
  Future<void> logout() async {
    try {
      await _send('POST', '/api/user/logout');
    } catch (_) {
      // 后端连不上也要让界面退出
    }
    _userToken = null;
  }

  Future<Map<String, dynamic>> signin() async {
    final r = await _send('POST', '/api/user/signin');
    return r as Map<String, dynamic>;
  }

  /// 换主题色：只传颜色，价格由后端判定（预置 50 / 自定义 150）。
  Future<Map<String, dynamic>> changeTheme(String color) async {
    final r = await _send('POST', '/api/user/theme', body: {'color': color});
    return r as Map<String, dynamic>;
  }
}

/// 人机验证题
class CaptchaInfo {
  const CaptchaInfo({required this.id, required this.question});

  final String id;
  final String question;

  factory CaptchaInfo.fromJson(Map<String, dynamic> json) => CaptchaInfo(
        id: json['id'] as String? ?? '',
        question: json['question'] as String? ?? '',
      );
}
