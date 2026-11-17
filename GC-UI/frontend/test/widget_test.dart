import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:gc_ui/api_client.dart';
import 'package:gc_ui/i18n.dart';
import 'package:gc_ui/theme.dart';

void main() {
  group('I18n', () {
    test('resolves auto to a supported language', () {
      expect(I18n.supported.contains(I18n.resolve('auto')), isTrue);
      expect(I18n.resolve('en-US'), 'en');
      expect(I18n.resolve('zh-CN'), 'zh-CN');
    });

    test('translates keys and fills placeholders', () {
      I18n.language.value = 'zh-CN';
      expect(T('nav.home'), '主页');
      expect(T('home.count', <Object?>[3]), '3 个模型');

      I18n.language.value = 'en';
      expect(T('nav.home'), 'Home');
      expect(T('home.count', <Object?>[3]), '3 models');
    });

    test('falls back to the key for unknown entries', () {
      expect(T('does.not.exist'), 'does.not.exist');
    });
  });

  group('SettingsPatch', () {
    test('only serialises the fields that were set', () {
      const patch = SettingsPatch(renderFps: 60, memoryAuto: false);
      expect(patch.toJson(), <String, dynamic>{
        'renderFps': 60,
        'memoryAuto': false,
      });
    });

    test('empty patch serialises to an empty map', () {
      expect(const SettingsPatch().toJson(), isEmpty);
    });
  });

  group('GcApi', () {
    test('builds model preview urls from the base url', () {
      final api = GcApi(baseUrl: 'http://127.0.0.1:17890');
      final model = ModelInfo.fromJson(<String, dynamic>{
        'id': 'solar-system',
        'name': '太阳系',
        'previewUrl': '/api/models/solar-system/preview',
      });
      expect(api.previewUrl(model),
          'http://127.0.0.1:17890/api/models/solar-system/preview');
    });

    test('parses a model payload', () {
      final model = ModelInfo.fromJson(<String, dynamic>{
        'id': 'earth',
        'name': '地球',
        'version': '1.2.0',
        'sizeBytes': 1048576,
        'sizeText': '1.0 MB',
        'tags': <dynamic>['天文', '地球'],
        'hasPreview': true,
      });
      expect(model.id, 'earth');
      expect(model.tags, <String>['天文', '地球']);
      expect(model.metaText, 'v1.2.0  ·  1.0 MB');
    });
  });

  group('theme', () {
    testWidgets('renders a card with its child', (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          theme: buildGcTheme(),
          home: const Scaffold(
            body: GcCard(child: Text('hello')),
          ),
        ),
      );
      expect(find.text('hello'), findsOneWidget);
    });
  });
}
