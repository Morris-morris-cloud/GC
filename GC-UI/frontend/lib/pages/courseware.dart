import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_inappwebview/flutter_inappwebview.dart';

import '../theme.dart';

/// 等高线课件页：用 WebView 承载去除授权验证后的离线 HTML 课件。
///
/// 资源（index.html + assets/）作为 Flutter assets 打包。桌面 WebView 不能直接
/// 读 flutter assets，因此用一个仅监听 127.0.0.1 的静态服务器（端口 0 自动分配，
/// 不与后端/其他进程冲突）把 assets 暴露成 http://127.0.0.1:<port>/ 再交给
/// WebView 加载。图片、视频、Three.js 与全部交互都保持原课件原样。
class CoursewarePage extends StatefulWidget {
  const CoursewarePage({super.key});

  @override
  State<CoursewarePage> createState() => _CoursewarePageState();
}

class _CoursewarePageState extends State<CoursewarePage>
    with AutomaticKeepAliveClientMixin {
  @override
  bool get wantKeepAlive => true;

  HttpServer? _server;
  int? _port;
  InAppWebViewController? _controller;
  double _progress = 0;
  String? _error;
  Directory? _extractDir;

  /// 打包后的 app 工作目录不固定，不能直接用相对路径读 flutter assets。
  /// 首次进入时把课件资源解压到临时目录，服务器再从这里提供文件。
  Future<Directory> _extractAssets() async {
    final dir = await Directory.systemTemp.createTemp('gc-courseware-');
    final manifest = await AssetManifest.loadFromAssetBundle(rootBundle);
    const prefix = 'courseware/';
    for (final key in manifest.listAssets()) {
      if (!key.startsWith(prefix)) continue;
      final rel = key.substring(prefix.length);
      if (rel.isEmpty) continue;
      final data = await rootBundle.load(key);
      final out = File('${dir.path}/$rel');
      await out.parent.create(recursive: true);
      await out.writeAsBytes(data.buffer.asUint8List(
          data.offsetInBytes, data.lengthInBytes));
    }
    return dir;
  }

  @override
  void initState() {
    super.initState();
    _start();
  }

  Future<void> _start() async {
    try {
      _extractDir = await _extractAssets();
      final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      _server = server;
      _port = server.port;
      server.listen(_handleRequest);
      if (mounted) setState(() {});
    } catch (e) {
      if (mounted) setState(() => _error = e.toString());
    }
  }

  /// 只服务课件目录内的文件，路径做规范化防止越界。
  Future<void> _handleRequest(HttpRequest req) async {
    var path = Uri.decodeFull(req.uri.path);
    if (path == '/' || path.isEmpty) path = '/index.html';
    // 去掉开头斜杠，拼到资源根目录
    final rel = path.replaceFirst(RegExp(r'^/+'), '');
    final normalized = _normalize(rel);
    final file = File('${_extractDir?.path ?? "."}/$normalized');
    try {
      if (!await file.exists()) {
        req.response.statusCode = HttpStatus.notFound;
        req.response.write('404 $rel');
        await req.response.close();
        return;
      }
      final length = await file.length();
      // _mimeOf 返回完整 MIME（含主/子类型）；charset 由 HTML 的 meta 声明。
      req.response.headers.contentType = ContentType.parse(_mimeOf(rel));
      req.response.headers.set('Accept-Ranges', 'bytes');

      // 视频拖动依赖 HTTP Range；解析 "bytes=start-end" 返回 206。
      final range = req.headers.value('range');
      if (range != null && range.startsWith('bytes=')) {
        final span = range.substring(6).split('-');
        var start = int.tryParse(span.first) ?? 0;
        var end = span.length > 1 ? int.tryParse(span.last) : null;
        if (start < 0) start = 0;
        if (start >= length) {
          req.response.statusCode = HttpStatus.requestedRangeNotSatisfiable;
          req.response.headers.set('Content-Range', 'bytes */$length');
          await req.response.close();
          return;
        }
        end ??= length - 1;
        if (end >= length) end = length - 1;
        if (end < start) end = start;
        req.response.statusCode = HttpStatus.partialContent;
        req.response.headers.set('Content-Range', 'bytes $start-$end/$length');
        req.response.headers.set('Content-Length', '${end - start + 1}');
        await req.response.addStream(
            file.openRead(start, end + 1).cast<List<int>>());
        await req.response.close();
        return;
      }

      req.response.headers.set('Content-Length', '$length');
      await req.response.addStream(file.openRead().cast<List<int>>());
      await req.response.close();
    } catch (e) {
      req.response.statusCode = HttpStatus.internalServerError;
      await req.response.close();
    }
  }

  /// 折叠 .. 与 .，防止 ../ 越界读取其他文件。
  static String _normalize(String path) {
    final parts = <String>[];
    for (final seg in path.split('/')) {
      if (seg.isEmpty || seg == '.') continue;
      if (seg == '..') {
        if (parts.isNotEmpty) parts.removeLast();
        continue;
      }
      parts.add(seg);
    }
    return parts.join('/');
  }

  static String _mimeOf(String path) {
    final ext = path.split('.').last.toLowerCase();
    switch (ext) {
      case 'html':
      case 'htm':
        return 'text/html';
      case 'css':
        return 'text/css';
      case 'js':
        return 'text/javascript';
      case 'json':
        return 'application/json';
      case 'png':
        return 'image/png';
      case 'jpg':
      case 'jpeg':
        return 'image/jpeg';
      case 'gif':
        return 'image/gif';
      case 'svg':
        return 'image/svg+xml';
      case 'mp4':
        return 'video/mp4';
      case 'webm':
        return 'video/webm';
      case 'ico':
        return 'image/x-icon';
      default:
        return 'application/octet-stream';
    }
  }

  Future<void> _reload() async {
    if (_port == null) return;
    await _controller?.loadUrl(
      urlRequest: URLRequest(
        url: WebUri('http://127.0.0.1:$_port/index.html'),
      ),
    );
  }

  @override
  void dispose() {
    unawaited(() async {
      await _server?.close();
      try {
        await _extractDir?.delete(recursive: true);
      } catch (_) {
        // 临时目录清理失败无需影响页面销毁。
      }
    }());
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    super.build(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        _toolbar(),
        Expanded(child: _body()),
      ],
    );
  }

  Widget _toolbar() {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
      decoration: BoxDecoration(
        color: GcColors.surface,
        border: Border(bottom: BorderSide(color: GcColors.line)),
      ),
      child: Row(
        children: <Widget>[
          const Icon(Icons.terrain_rounded, size: 20),
          const SizedBox(width: 10),
          const Text(
            '等高线地形 · 五大典型地貌判读',
            style: TextStyle(fontSize: 15, fontWeight: FontWeight.w600),
          ),
          const Spacer(),
          IconButton(
            tooltip: '刷新',
            icon: const Icon(Icons.refresh_rounded, size: 18),
            onPressed: _reload,
          ),
          IconButton(
            tooltip: '全屏',
            icon: const Icon(Icons.fullscreen_rounded, size: 18),
            onPressed: () => _controller?.evaluateJavascript(
              source: 'document.documentElement.requestFullscreen?.();',
            ),
          ),
        ],
      ),
    );
  }

  Widget _body() {
    if (_error != null) {
      return Center(child: Text('课件加载失败：$_error'));
    }
    if (_port == null) {
      return const Center(child: CircularProgressIndicator(strokeWidth: 2));
    }
    return _webview();
  }

  Widget _webview() {
    return Stack(
      children: <Widget>[
        InAppWebView(
          initialUrlRequest: URLRequest(
            url: WebUri('http://127.0.0.1:$_port/index.html'),
          ),
          initialSettings: InAppWebViewSettings(
            javaScriptEnabled: true,
            mediaPlaybackRequiresUserGesture: false,
            allowsInlineMediaPlayback: true,
            supportZoom: false,
            clearCache: false,
            // 本地可信内容，允许混合加载。
            mixedContentMode: MixedContentMode.MIXED_CONTENT_ALWAYS_ALLOW,
          ),
          onWebViewCreated: (controller) => _controller = controller,
          onProgressChanged: (_, progress) {
            if (mounted && progress < 100) {
              setState(() => _progress = progress / 100);
            }
          },
          onLoadStop: (_, __) {
            if (mounted) setState(() => _progress = 1);
          },
          onReceivedError: (_, request, error) {
            if (mounted) {
              setState(() => _error = '${error.type} ${error.description}');
            }
          },
        ),
        if (_progress < 1)
          Positioned(
            top: 0,
            left: 0,
            right: 0,
            child: SizedBox(
              height: 2,
              child: LinearProgressIndicator(value: _progress),
            ),
          ),
      ],
    );
  }
}
