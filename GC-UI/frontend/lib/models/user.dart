/// 用户记录
class UserRecord {
  const UserRecord({
    required this.username,
    required this.points,
    required this.consecutiveDays,
    this.lastSignIn,
    this.themeColor,
  });

  final String username;
  final int points;
  final int consecutiveDays;
  final String? lastSignIn;
  final String? themeColor;

  factory UserRecord.fromJson(Map<String, dynamic> json) {
    return UserRecord(
      username: json['username'] as String? ?? '',
      points: json['points'] as int? ?? 0,
      consecutiveDays: json['consecutive_days'] as int? ?? 0,
      lastSignIn: json['last_sign_in'] as String?,
      themeColor: json['theme_color'] as String?,
    );
  }
}

/// 签到奖励配置（30天周期）
class SignInRewards {
  static const List<int> rewards = [
    10, 10, 10, 10, 10, 10, 10,  // 第1-7天：每天+10
    20, 20, 20, 20, 20, 20, 20,  // 第8-15天：每天+20
    30, 30, 30, 30, 30, 30, 30,  // 第16-23天：每天+30
    30, 30, 30, 30, 30, 30, 30,  // 第24-30天：每天+30
  ];

  static int getReward(int day) {
    if (day < 1) day = 1;
    if (day > 30) day = ((day - 1) % 30) + 1;
    return rewards[day - 1];
  }
}

/// 主题颜色
class ThemeColor {
  const ThemeColor({required this.name, required this.hex, required this.cost});

  final String name;
  final String hex;
  final int cost;

  static const List<ThemeColor> predefined = [
    ThemeColor(name: '蓝色', hex: '4A90D9', cost: 0),
    ThemeColor(name: '红色', hex: 'E74C3C', cost: 50),
    ThemeColor(name: '绿色', hex: '27AE60', cost: 50),
    ThemeColor(name: '紫色', hex: '9B59B6', cost: 50),
    ThemeColor(name: '橙色', hex: 'F39C12', cost: 50),
    ThemeColor(name: '青色', hex: '1ABC9C', cost: 50),
    ThemeColor(name: '粉色', hex: 'E91E63', cost: 50),
    ThemeColor(name: '深蓝', hex: '2C3E50', cost: 50),
  ];
}
