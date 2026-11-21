/// 用户记录（对应后端 UserPayload）
class UserRecord {
  const UserRecord({
    required this.username,
    required this.points,
    required this.consecutiveDays,
    required this.cycleDay,
    required this.cycleDays,
    required this.todaySigned,
    required this.nextReward,
    this.lastSignIn,
    this.themeColor,
    this.themePaid = 0,
    this.refundSeconds = 0,
  });

  final String username;
  final int points;

  /// 连续签到天数（断签会重置）
  final int consecutiveDays;

  /// 当前处在 30 天周期的第几天（1–30）
  final int cycleDay;

  /// 周期长度，固定 30
  final int cycleDays;

  /// 今天是否已签到
  final bool todaySigned;

  /// 下一次签到能拿多少分
  final int nextReward;

  final String? lastSignIn;

  /// 形如 `4A90D9`（不带 #）
  final String? themeColor;

  /// 当前这个颜色实际花了多少分
  final int themePaid;

  /// 退款窗口剩余秒数（0 表示没有可退的）
  final int refundSeconds;

  factory UserRecord.fromJson(Map<String, dynamic> json) {
    return UserRecord(
      username: json['username'] as String? ?? '',
      points: (json['points'] as num?)?.toInt() ?? 0,
      consecutiveDays: (json['consecutive_days'] as num?)?.toInt() ?? 0,
      cycleDay: (json['cycle_day'] as num?)?.toInt() ?? 1,
      cycleDays: (json['cycle_days'] as num?)?.toInt() ?? 30,
      todaySigned: json['today_signed'] as bool? ?? false,
      nextReward: (json['next_reward'] as num?)?.toInt() ?? 10,
      lastSignIn: json['last_sign_in'] as String?,
      themeColor: json['theme_color'] as String?,
      themePaid: (json['theme_paid'] as num?)?.toInt() ?? 0,
      refundSeconds: (json['refund_seconds'] as num?)?.toInt() ?? 0,
    );
  }
}

/// 签到奖励：30 天一循环，第 7/14/21/28 天给周奖励，第 30 天给满月奖励。
/// 与后端 `UserService.GetSignInReward` 保持一致。
class SignInRewards {
  static const int cycleDays = 30;

  static int getReward(int day) {
    var d = day;
    if (d < 1) d = 1;
    if (d > cycleDays) d = ((d - 1) % cycleDays) + 1;
    switch (d) {
      case 7:
        return 30;
      case 14:
        return 40;
      case 21:
        return 50;
      case 28:
        return 60;
      case 30:
        return 100;
    }
    if (d <= 6) return 10;
    if (d <= 13) return 15;
    if (d <= 20) return 20;
    if (d <= 27) return 25;
    return 40;
  }
}

/// 主题颜色：预置色 50 分一个，自定义色 150 分一个，默认蓝免费。
class ThemeColor {
  const ThemeColor({required this.name, required this.hex, required this.cost});

  final String name;
  final String hex;
  final int cost;

  /// 默认色（免费）
  static const String defaultHex = '4A90D9';

  /// 预置色单价
  static const int presetCost = 50;

  /// 自定义色单价
  static const int customCost = 150;

  /// 购买后 1 小时内换色可退 80%
  static const Duration refundWindow = Duration(hours: 1);
  static const double refundRate = 0.8;

  static const List<ThemeColor> predefined = <ThemeColor>[
    ThemeColor(name: '蓝色', hex: defaultHex, cost: 0),
    ThemeColor(name: '红色', hex: 'E74C3C', cost: presetCost),
    ThemeColor(name: '绿色', hex: '27AE60', cost: presetCost),
    ThemeColor(name: '紫色', hex: '9B59B6', cost: presetCost),
    ThemeColor(name: '橙色', hex: 'F39C12', cost: presetCost),
    ThemeColor(name: '青色', hex: '1ABC9C', cost: presetCost),
    ThemeColor(name: '粉色', hex: 'E91E63', cost: presetCost),
    ThemeColor(name: '深蓝', hex: '2C3E50', cost: presetCost),
  ];

  /// 某个十六进制颜色的价格；默认蓝 0，预置色 50，其它 150。
  static int costOf(String hex) {
    final h = hex.replaceAll('#', '').toUpperCase();
    if (h == defaultHex) return 0;
    for (final c in predefined) {
      if (c.hex.toUpperCase() == h) return presetCost;
    }
    return customCost;
  }
}

/// 人品值结果
class FortuneResult {
  const FortuneResult(this.value, this.comment);

  final int value;
  final String comment;

  /// 0–100，越高越「人品爆发」
  static String commentFor(int value) {
    if (value >= 96) return '欧皇附体，今天买彩票吧';
    if (value >= 90) return '运气爆棚，适合干大事';
    if (value >= 80) return '手气不错，稳';
    if (value >= 65) return '中上水平，稍微努一下';
    if (value >= 50) return '普普通通，够用';
    if (value >= 35) return '有点背，低调点';
    if (value >= 20) return '今天别赌任何东西';
    if (value >= 8) return '极差，建议躺平';
    return '非酋之王，明天再来';
  }
}
