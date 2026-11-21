import 'dart:async';
import 'dart:math' as math;
import 'package:flutter/material.dart';

/// 模型通用接口，所有 Flutter 模型实现此接口。
abstract class GcModel {
  String get id;
  String name();
  String description();
  Widget buildView(BuildContext context);
}
