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
    // 不要设置 HttpOverrides.global = null：那会打开真实网络，
    // flutter_test 的假时钟下 HTTP 定时器永远悬着，用例结束时
    // 会撞上「!timersPending」断言。保持 flutter_test 默认的 FakeHttpClient
    // （所有请求立即返回 400），页面本身都用预置数据渲染，不需要后端。
    // 测试环境默认用 Ahem 占位字体（所有字都是方块），必须显式加载一套真字体：
    // Linux CI 用 Noto CJK，Windows 本机用 Noto Sans SC / 微软雅黑，macOS 用苹方。
    const candidates = <String>[
      r'C:\Windows\Fonts\Deng.ttf',
      r'C:\Windows\Fonts\NotoSansSC-VF.ttf',
      r'C:\Windows\Fonts\msyh.ttc',
      r'C:\Windows\Fonts\simhei.ttf',
      '/System/Library/Fonts/PingFang.ttc',
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

    // Material 图标字体：flutter_test 不会自动加载，不加载的话所有 Icon
    // 都是方框，截图就没法检查图标。SDK 缓存里那份由 flutter test 通过
    // FLUTTER_ROOT 指过来。
    final flutterRoot = Platform.environment['FLUTTER_ROOT'];
    if (flutterRoot != null) {
      final icons = File(
        '$flutterRoot/bin/cache/artifacts/material_fonts/materialicons-regular.otf',
      );
      if (icons.existsSync()) {
        final bytes = icons.readAsBytesSync();
        final loader = FontLoader('MaterialIcons')
          ..addFont(Future<ByteData>.value(ByteData.view(bytes.buffer)));
        await loader.load();
      }
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

  /// 外壳用的 controller：bootstrap 直接返回，不去连后端。
  ///
  /// 真 ShellPage 会在 initState 里 bootstrap，后端不在时会挂重试定时器，
  /// 用例结束时报「!timersPending」。这里只把 bootstrap 换成空实现，
  /// 页面里的数据请求仍会走 flutter_test 的假 HTTP（立即 400）。
  ShellController shellController() => _StubShellController()
    ..settings = sampleSettings()
    ..health = HealthInfo.fromJson(<String, dynamic>{
      'ok': true,
      'version': '1.0.7',
      'platform': 'osx-arm64',
      'uiAvailable': true,
    });

  Future<void> shoot(WidgetTester tester, String name, Widget page,
      {Brightness brightness = Brightness.dark,
      Size size = const Size(1280, 860)}) async {
    // 1280×860：与发布包窗口默认尺寸（1280×720）接近，再多留一点高度看得到
    // 底部状态条。像素比 1.0，方便逐像素检查描边和文字。
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.reset);

    // GcColors 是静态取值，截图前先按目标亮度绑定，否则浅色截图仍是深色配色。
    GcColors.bind(brightness);

    final key = GlobalKey();
    await tester.pumpWidget(
      MaterialApp(
        debugShowCheckedModeBanner: false,
        // 四个页面都用预置数据渲染，不需要后端；字体统一换成测试里加载的
        // 那一套，并且清空 fallback —— 否则主题里的按钮 / 输入框样式会去
        // 找系统字体（测试环境没有），缺字处就画成方块。
        theme: buildGcTheme(
          brightness,
          fontFamily: 'Noto Sans CJK SC',
          fontFamilyFallback: const <String>[],
        ),
        home: RepaintBoundary(
          key: key,
          child: Scaffold(backgroundColor: GcColors.appBg, body: page),
        ),
      ),
    );
    await tester.pump(const Duration(milliseconds: 200));
    await tester.pump(const Duration(milliseconds: 300));

    final boundary =
        key.currentContext!.findRenderObject()! as RenderRepaintBoundary;
    // toImage / toByteData 要走真实引擎的异步管线，必须放在 runAsync 里，
    // 否则 fake async 时钟不会推进，Future 一直不完成（表现为每个用例卡
    // 满 10 分钟超时才返回）。
    final stopwatch = Stopwatch()..start();
    final image = await tester.runAsync(
      () => boundary.toImage(pixelRatio: 1.0),
    );
    final bytes = await tester.runAsync(
      () => image!.toByteData(format: ui.ImageByteFormat.png),
    );

    final dir = Directory('test/shots')..createSync(recursive: true);
    File('${dir.path}/$name.png').writeAsBytesSync(bytes!.buffer.asUint8List());
    stopwatch.stop();
    // ignore: avoid_print
    print(
        '[shots] $name (${brightness.name}) 耗时 ${stopwatch.elapsedMilliseconds} ms');
  }

  testWidgets('home', (tester) async {
    I18n.language.value = 'zh-CN';
    await shoot(
      tester,
      'home',
      HomePageView(
          controller: sampleController(), initialModels: sampleModels()),
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
    await shoot(
        tester, 'settings', SettingsPageView(controller: sampleController()));
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
    final controller = shellController()
      ..setUpdateStatus(
        visible: true,
        busy: false,
        progress: 1,
        status: '已更新 2 个模型，立即生效',
        detail: '模型与启动器均为最新版',
        notice: '',
      );
    await shoot(tester, 'shell', ShellPage(controller: controller));
  });

  /// 小窗口回归：1024×680 下不应出现溢出条纹或卡片挤压。
  testWidgets('home-small', (tester) async {
    I18n.language.value = 'zh-CN';
    await shoot(
      tester,
      'home-small',
      HomePageView(
          controller: sampleController(), initialModels: sampleModels()),
      size: const Size(1024, 680),
    );
  });

  testWidgets('download-small', (tester) async {
    I18n.language.value = 'zh-CN';
    await shoot(
      tester,
      'download-small',
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
        ],
        initialIndex: SourceIndexInfo.fromJson(<String, dynamic>{
          'sourceId': 'github',
          'root': 'https://github.com/Morris-GC/GC',
          'fromCache': false,
          'resolvedFrom': 'version.json',
          'models': <dynamic>[
            <String, dynamic>{
              'id': 'sun',
              'name': '太阳系',
              'description': '观察八大行星的公转轨道与相对大小。',
              'version': '1.2',
              'sizeText': '2.3 MB',
              'installed': true,
            },
          ],
        }),
        initialJobs: const <JobInfo>[],
      ),
      size: const Size(1024, 680),
    );
  });

  testWidgets('settings-small', (tester) async {
    I18n.language.value = 'zh-CN';
    await shoot(
      tester,
      'settings-small',
      SettingsPageView(controller: sampleController()),
      size: const Size(1024, 680),
    );
  });

  testWidgets('home-light', (tester) async {
    I18n.language.value = 'zh-CN';
    await shoot(
      tester,
      'home-light',
      HomePageView(
          controller: sampleController(), initialModels: sampleModels()),
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

/// 只用于截图的 controller：不连后端，避免挂上重试定时器。
class _StubShellController extends ShellController {
  @override
  Future<void> bootstrap() async {}
}
