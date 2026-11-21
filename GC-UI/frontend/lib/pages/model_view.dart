import 'package:flutter/material.dart';

import '../i18n.dart';
import '../models/base.dart';
import '../models/lookup.dart';
import '../theme.dart';

/// 模型视图：在壳内直接渲染 Flutter 模型。
///
/// 以前「启动模型」是让后端另开一个 Avalonia 窗口；现在模型全部是 Flutter 实现，
/// 直接换掉内容区即可 —— 不会多出一个窗口，也不用跨进程传状态。
class ModelViewPage extends StatelessWidget {
  const ModelViewPage({
    super.key,
    required this.modelId,
    required this.onClose,
  });

  final String modelId;
  final VoidCallback onClose;

  @override
  Widget build(BuildContext context) {
    final model = flutterModelFor(modelId);

    if (model == null) {
      return Center(
        child: GcCard(
          padding: const EdgeInsets.all(GcSpace.xxl),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              Text(
                T('model.missing'),
                style: TextStyle(
                  fontSize: 15,
                  height: 1.35,
                  fontWeight: FontWeight.w600,
                  color: GcColors.ink,
                ),
              ),
              const SizedBox(height: GcSpace.sm),
              GcHint(T('model.missingHint')),
              const SizedBox(height: GcSpace.lg),
              OutlinedButton(
                onPressed: onClose,
                child: Text(T('model.back')),
              ),
            ],
          ),
        ),
      );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        Padding(
          padding: const EdgeInsets.fromLTRB(
            GcSpace.xxl,
            GcSpace.lg,
            GcSpace.xxl,
            GcSpace.sm,
          ),
          child: Row(
            children: <Widget>[
              OutlinedButton.icon(
                onPressed: onClose,
                icon: const Icon(Icons.arrow_back_rounded, size: 16),
                label: Text(T('model.back')),
              ),
              const SizedBox(width: GcSpace.lg),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: <Widget>[
                    Text(
                      model.name(),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontSize: 16,
                        height: 1.3,
                        fontWeight: FontWeight.w600,
                        color: GcColors.ink,
                      ),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      model.description(),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontSize: 12,
                        height: 1.4,
                        color: GcColors.faint,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
        Expanded(child: ClipRect(child: _body(model))),
      ],
    );
  }

  Widget _body(GcModel model) {
    // 模型视图自己管状态（动画、视角），换 id 时必须重建，不能复用 State
    return KeyedSubtree(
      key: ValueKey<String>(model.id),
      child: Builder(builder: (context) => model.buildView(context)),
    );
  }
}
