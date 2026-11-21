import 'package:flutter/material.dart';

/// 节气数据
class Season {
  final String key;
  final String date;
  final double declinationDeg;
  final double angleDeg;
  final String teachingZh;
  final String teachingEn;

  Season({
    required this.key,
    required this.date,
    required this.declinationDeg,
    required this.angleDeg,
    required this.teachingZh,
    required this.teachingEn,
  });
}

/// 教学要点
class Lesson {
  final String titleZh;
  final String titleEn;
  final String bodyZh;
  final String bodyEn;
  final String view;
  final double? angleDeg;

  Lesson({
    required this.titleZh,
    required this.titleEn,
    required this.bodyZh,
    required this.bodyEn,
    required this.view,
    this.angleDeg,
  });
}
