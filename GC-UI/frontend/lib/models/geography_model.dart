import 'package:flutter/material.dart';

/// 地球地理模型数据
class GeoZone {
  final String id;
  final String nameZh;
  final String nameEn;
  final double latMin;
  final double latMax;
  final String descZh;
  final String descEn;

  GeoZone({
    required this.id,
    required this.nameZh,
    required this.nameEn,
    required this.latMin,
    required this.latMax,
    required this.descZh,
    required this.descEn,
  });
}

/// 大陆板块数据
class Continent {
  final String nameZh;
  final String nameEn;
  final List<List<double>> points; // [lon, lat]

  Continent({
    required this.nameZh,
    required this.nameEn,
    required this.points,
  });
}

/// 板块数据
class Plate {
  final String id;
  final String nameZh;
  final String nameEn;
  final List<List<double>> points;

  Plate({
    required this.id,
    required this.nameZh,
    required this.nameEn,
    required this.points,
  });
}

/// 洲界线
class Border {
  final String betweenZh;
  final String betweenEn;
  final String nameZh;
  final String nameEn;
  final List<List<double>> points;

  Border({
    required this.betweenZh,
    required this.betweenEn,
    required this.nameZh,
    required this.nameEn,
    required this.points,
  });
}
