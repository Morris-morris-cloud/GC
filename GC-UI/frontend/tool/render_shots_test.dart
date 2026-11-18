import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:gc_ui/api_client.dart';
import 'package:gc_ui/controller.dart';
import 'package:gc_ui/i18n.dart';
import 'package:gc_ui/pages.dart';
import 'package:gc_ui/shell.dart';
import 'package:gc_ui/theme.dart';

/// 视觉检查：把真实页面渲染成 PNG 写到 test/shots/。
///
/// 用 RepaintBoundary 直接取像素，不走 golden 比较器（避免网络图片阻塞）。
/// 测试环境默认用 Ahem 占位字体，这里显式加载系统中文字体，否则中文全是方块。
///
/// 运行（需要后端已在 127.0.0.1:17890 上跑起来）：
///   flutter test tool/render_shots_test.dart
void main() {
  setUpAll(() async {
    HttpOverrides.global = null;

    const candidates = <String>[
      '/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc',
      '/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc',
    ];
    for (final path in candidates) {
      final file = File(path);
      if (!file.existsSync()) continue;
      final bytes = file.readAsBytesSync();
      final loader = FontLoader('Noto Sans CJK SC')
        ..addFont(Future<ByteData>.value(ByteData.view(bytes.buffer)));
      await loader.load();
      break;
    }
  });

  SettingsInfo sampleSettings() => SettingsInfo.fromJson(<String, dynamic>{
        'language': 'zh-CN',
        'theme': 'dark',
        'renderFps': 120,
        'memoryMb': 2048,
        'memoryAuto': true,
        'autoUpdateOnStartup': true,
        'requireSignedManifest': true,
        'updateRepository': 'https://git.xhmcl.com/Morris/GC',
        'memoryBudgetMb': 2048,
        'memoryUsageText': '512 MB / 2048 MB',
        'modelRoot': '/Users/morris/.GC/model',
        'dataRoot': '/Users/morris/.GC/data',
        'settingsFile': '/Users/morris/.GC/settings.json',
        'version': '1.0.7',
        'platform': 'osx-arm64',
        'sources': <dynamic>[
          <String, dynamic>{
            'id': 'github',
            'name': 'GitHub',
            'url': 'https://github.com/Morris-GC/GC',
            'enabled': true,
            'builtIn': true,
            'host': 'github.com',
          },
          <String, dynamic>{
            'id': 'gitee',
            'name': 'Gitee',
            'url': 'https://gitee.com/morris/gc',
            'enabled': true,
            'builtIn': true,
            'host': 'gitee.com',
          },
        ],
        'languages': <dynamic>[
          <String, dynamic>{'code': 'zh-CN', 'label': '简体中文'},
          <String, dynamic>{'code': 'en', 'label': 'English'},
        ],
      });

  List<ModelInfo> sampleModels() => <ModelInfo>[
        ModelInfo.fromJson(<String, dynamic>{
          'id': 'contour-map',
          'name': '等高线地图',
          'description': '把三维地形与等高线对应起来，识别山峰、山脊、山谷、鞍部与陡崖。',
          'version': '1.0',
          'author': 'Morris GC',
          'sizeBytes': 147351,
          'sizeText': '143.9 KB',
          'tags': <dynamic>['地理', '地图', '等高线', '地形'],
          'hasPreview': false,
        }),
        ModelInfo.fromJson(<String, dynamic>{
          'id': 'solar-system',
          'name': '太阳系',
          'description': '观察八大行星的公转轨道与相对大小，理解日地月系统的尺度关系。',
          'version': '1.2',
          'author': 'Morris GC',
          'sizeBytes': 2411724,
          'sizeText': '2.3 MB',
          'tags': <dynamic>['天文', '太阳系', '公转'],
          'hasPreview': false,
        }),
        ModelInfo.fromJson(<String, dynamic>{
          'id': 'earth-rotation',
          'name': '地球自转',
          'description': '用可交互的三维地球演示自转方向、昼夜交替与地方时差。',
          'version': '1.1',
          'author': 'Morris GC',
          'sizeBytes': 1887436,
          'sizeText': '1.8 MB',
          'tags': <dynamic>['天文', '地球', '自转'],
          'hasPreview': false,
        }),
      ];

  ShellController sampleController() => ShellController(api: GcApi())
    ..settings = sampleSettings()
    ..health = HealthInfo.fromJson(<String, dynamic>{
      'ok': true,
      'version': '1.0.7',
      'platform': 'osx-arm64',
      'uiAvailable': true,
    });

  Future<void> shoot(WidgetTester tester, String name, Widget page,
      {Brightness brightness = Brightness.dark}) async {
    tester.view.physicalSize = const Size(1280, 820);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.reset);

    // GcColors 是静态取值，截图前先按目标亮度绑定，否则浅色截图仍是深色配色。
    GcColors.bind(brightness);

    final key = GlobalKey();
    await tester.pumpWidget(
      MaterialApp(
        debugShowCheckedModeBanner: false,
        theme: buildGcTheme(brightness).copyWith(
          textTheme: buildGcTheme(brightness).textTheme.apply(
                fontFamily: 'Noto Sans CJK SC',
              ),
        ),
        home: RepaintBoundary(
          key: key,
          child: Scaffold(backgroundColor: GcColors.appBg, body: page),
        ),
      ),
    );
    await tester.pump(const Duration(milliseconds: 200));
    // 页面里的数据来自真实后端（/api/about、/api/sources 等）。
    // flutter_test 默认用假时钟，真实 HTTP 永远不会完成，所以这里用 runAsync
    // 让出真实时间，等请求回来后再 pump 出最终画面。
    await tester.runAsync(() async {
      await Future<void>.delayed(const Duration(milliseconds: 1200));
    });
    await tester.pump(const Duration(milliseconds: 300));
    await tester.runAsync(() async {
      await Future<void>.delayed(const Duration(milliseconds: 600));
    });
    await tester.pump(const Duration(milliseconds: 300));

    final boundary =
        key.currentContext!.findRenderObject()! as RenderRepaintBoundary;
    final image = await boundary.toImage(pixelRatio: 1.0);
    final bytes = await image.toByteData(format: ui.ImageByteFormat.png);

    final dir = Directory('test/shots')..createSync(recursive: true);
    File('${dir.path}/$name.png').writeAsBytesSync(bytes!.buffer.asUint8List());
  }

  testWidgets('home', (tester) async {
    I18n.language.value = 'zh-CN';
    await shoot(
      tester,
      'home',
      HomePageView(controller: sampleController(), initialModels: sampleModels()),
    );
  });

  testWidgets('download', (tester) async {
    I18n.language.value = 'zh-CN';
    await shoot(
      tester,
      'download',
      DownloadPageView(
        controller: sampleController(),
        initialSources: <SourceInfo>[
          SourceInfo.fromJson(<String, dynamic>{
            'id': 'github',
            'name': 'GitHub',
            'url': 'https://github.com/Morris-GC/GC',
            'enabled': true,
            'builtIn': true,
            'host': 'github.com',
          }),
          SourceInfo.fromJson(<String, dynamic>{
            'id': 'gitee',
            'name': 'Gitee',
            'url': 'https://gitee.com/morris/gc',
            'enabled': true,
            'builtIn': true,
            'host': 'gitee.com',
          }),
        ],
        initialIndex: SourceIndexInfo.fromJson(<String, dynamic>{
          'sourceId': 'github',
          'root': 'https://github.com/Morris-GC/GC',
          'fromCache': false,
          'resolvedFrom': 'version.json',
          'models': <dynamic>[
            <String, dynamic>{
              'id': 'solar-system',
              'name': '太阳系',
              'description': '观察八大行星的公转轨道与相对大小。',
              'version': '1.2',
              'sizeBytes': 2411724,
              'sizeText': '2.3 MB',
              'sourceId': 'github',
              'installed': true,
              'installedVersion': '1.2',
            },
            <String, dynamic>{
              'id': 'earth-revolution',
              'name': '地球公转',
              'description': '演示公转轨道、四季成因与太阳直射点移动。',
              'version': '1.0',
              'sizeBytes': 2097152,
              'sizeText': '2.0 MB',
              'sourceId': 'github',
              'installed': false,
            },
            <String, dynamic>{
              'id': 'earth-geography',
              'name': '地球地理',
              'description': '经纬网、海陆分布与大洲大洋的交互式讲解。',
              'version': '1.1',
              'sizeBytes': 3145728,
              'sizeText': '3.0 MB',
              'sourceId': 'github',
              'installed': false,
            },
          ],
        }),
        initialJobs: <JobInfo>[
          JobInfo.fromJson(<String, dynamic>{
            'id': 'job-1',
            'modelId': 'earth-revolution',
            'name': '地球公转',
            'version': '1.0',
            'state': 'Downloading',
            'progress': 0.42,
            'statusText': '正在下载 42%',
            'detailText': '860 KB / 2.0 MB',
            'isActive': true,
          }),
        ],
      ),
    );
  });

  testWidgets('settings', (tester) async {
    I18n.language.value = 'zh-CN';
    await shoot(tester, 'settings', SettingsPageView(controller: sampleController()));
  });

  testWidgets('about', (tester) async {
    I18n.language.value = 'zh-CN';
    await shoot(
      tester,
      'about',
      AboutPageView(
        controller: sampleController(),
        initialAbout: AboutInfo.fromJson(<String, dynamic>{
          'appName': 'Morris GC',
          'version': '1.0.7',
          'platform': 'osx-arm64',
          'repository': 'https://git.xhmcl.com/Morris/GC',
          'sourceLanguage': 'C#',
          'frontend': 'Flutter',
          'contributors': <dynamic>[
            <String, dynamic>{
              'name': 'Morris',
              'contributions': 25,
              'avatarUrl': null,
            },
          ],
        }),
      ),
    );
  });

  testWidgets('shell', (tester) async {
    I18n.language.value = 'zh-CN';
    await shoot(tester, 'shell', ShellPage(controller: sampleController()));
  });

  testWidgets('home-light', (tester) async {
    I18n.language.value = 'zh-CN';
    await shoot(
      tester,
      'home-light',
      HomePageView(controller: sampleController(), initialModels: sampleModels()),
      brightness: Brightness.light,
    );
  });

  testWidgets('settings-light', (tester) async {
    I18n.language.value = 'zh-CN';
    await shoot(
      tester,
      'settings-light',
      SettingsPageView(controller: sampleController()),
      brightness: Brightness.light,
    );
  });
}
