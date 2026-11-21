import 'package:flutter/material.dart';

import '../controller.dart';
import '../api_client.dart';
import '../i18n.dart';
import '../theme.dart';
import '../models/user.dart';

/// 用户页面：账号（注册/登录/人机验证）、签到、人品值、二选一、主题色
class UserPage extends StatefulWidget {
  const UserPage({super.key, required this.controller});

  final ShellController controller;

  @override
  State<UserPage> createState() => _UserPageState();
}

class _UserPageState extends State<UserPage> {
  UserRecord? _user;
  bool _loading = true;
  String? _error;

  // 登录 / 注册
  final _usernameCtrl = TextEditingController();
  final _passwordCtrl = TextEditingController();
  final _captchaCtrl = TextEditingController();
  bool _isLogin = true;
  CaptchaInfo? _captcha;
  bool _captchaLoading = false;
  bool _submitting = false;

  // 人品值
  FortuneResult? _fortune;
  bool _rolling = false;

  // 二选一
  final _option1Ctrl = TextEditingController();
  final _option2Ctrl = TextEditingController();
  String? _randomResult;

  @override
  void initState() {
    super.initState();
    _loadUser();
  }

  @override
  void dispose() {
    _usernameCtrl.dispose();
    _passwordCtrl.dispose();
    _captchaCtrl.dispose();
    _option1Ctrl.dispose();
    _option2Ctrl.dispose();
    super.dispose();
  }

  // ---------------------------------------------------------------- 账号

  Future<void> _loadUser() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final resp = await widget.controller.api.me();
      if (!mounted) return;
      setState(() {
        _user = resp == null ? null : UserRecord.fromJson(resp);
        _loading = false;
      });
      _applyTheme();
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = e.toString();
      });
    }
  }

  /// 用户买的主题色覆盖全局强调色
  void _applyTheme() {
    final hex = _user?.themeColor;
    if (hex == null || hex.isEmpty) return;
    GcPalette.apply('#$hex');
  }

  Future<void> _refreshCaptcha() async {
    setState(() => _captchaLoading = true);
    try {
      final c = await widget.controller.api.captcha();
      if (!mounted) return;
      setState(() {
        _captcha = c;
        _captchaLoading = false;
        _captchaCtrl.clear();
      });
    } catch (e) {
      if (!mounted) return;
      setState(() => _captchaLoading = false);
      _toast(e.toString());
    }
  }

  Future<void> _submit() async {
    final username = _usernameCtrl.text.trim();
    final password = _passwordCtrl.text;

    if (username.isEmpty || password.isEmpty) {
      _toast(T('user.fillAll'));
      return;
    }

    if (!_isLogin) {
      if (_captcha == null) {
        _toast(T('user.captchaMissing'));
        return;
      }
      if (_captchaCtrl.text.trim().isEmpty) {
        _toast(T('user.fillCaptcha'));
        return;
      }
    }

    setState(() => _submitting = true);
    try {
      if (_isLogin) {
        await widget.controller.api.login(username, password);
      } else {
        await widget.controller.api.register(
          username,
          password,
          captchaId: _captcha!.id,
          captchaAnswer: _captchaCtrl.text.trim(),
        );
        await widget.controller.api.login(username, password);
      }

      final resp = await widget.controller.api.me();
      if (!mounted) return;
      setState(() {
        _user = resp == null ? null : UserRecord.fromJson(resp);
        _usernameCtrl.clear();
        _passwordCtrl.clear();
        _captchaCtrl.clear();
        _captcha = null;
        _submitting = false;
      });
      _applyTheme();
      _toast(_isLogin ? T('user.loginSuccess') : T('user.registerSuccess'));
    } catch (e) {
      if (!mounted) return;
      setState(() => _submitting = false);
      // 注册失败时换一题：验证码是一次性的
      if (!_isLogin) _refreshCaptcha();
      _toast(e.toString());
    }
  }

  Future<void> _logout() async {
    await widget.controller.api.logout();
    if (!mounted) return;
    setState(() {
      _user = null;
      _fortune = null;
    });
    GcPalette.accent.value = GcPalette.defaultAccent;
    _toast(T('user.logoutDone'));
  }

  Future<void> _signin() async {
    try {
      final resp = await widget.controller.api.signin();
      if (!mounted) return;

      if (resp['message'] == '今日已签到') {
        _toast(T('user.alreadySigned'));
        return;
      }

      final payload = resp['user'] as Map<String, dynamic>?;
      final reward = (resp['reward'] as num?)?.toInt() ?? 0;
      setState(() {
        if (payload != null) _user = UserRecord.fromJson(payload);
      });
      _toast('${T('user.signinSuccess')} +$reward ${T('user.points')}');
    } catch (e) {
      if (!mounted) return;
      _toast(e.toString());
    }
  }

  // ---------------------------------------------------------------- 人品值

  Future<void> _rollFortune() async {
    if (_user == null) {
      _toast(T('user.loginFirst'));
      return;
    }
    setState(() => _rolling = true);
    try {
      // 服务端算随机数和价格：当天第一次免费，之后按当日次数阶梯计费
      final resp = await widget.controller.api.rollFortune();
      if (!mounted) return;
      final value = (resp['value'] as num?)?.toInt() ?? 0;
      final cost = (resp['cost'] as num?)?.toDouble() ?? 0;
      setState(() {
        _fortune = FortuneResult(value, resp['comment'] as String? ?? FortuneResult.commentFor(value));
        final payload = resp['user'] as Map<String, dynamic>?;
        if (payload != null) _user = UserRecord.fromJson(payload);
        _rolling = false;
      });
      _toast(cost > 0 ? '本次消耗 ${formatPoints(cost)} 积分' : '今日首次免费');
    } catch (e) {
      if (!mounted) return;
      setState(() => _rolling = false);
      _toast(e.toString());
    }
  }

  // ---------------------------------------------------------------- 二选一

  Future<void> _pickRandom() async {
    final a = _option1Ctrl.text.trim();
    final b = _option2Ctrl.text.trim();
    if (a.isEmpty || b.isEmpty) {
      _toast(T('user.fillOptions'));
      return;
    }
    if (_user == null) {
      _toast(T('user.loginFirst'));
      return;
    }
    try {
      // 服务端随机 + 计费：当天第一次免费，之后每次 0.5 积分
      final resp = await widget.controller.api.pickOne(a, b);
      if (!mounted) return;
      final cost = (resp['cost'] as num?)?.toDouble() ?? 0;
      setState(() {
        _randomResult = resp['choice'] as String?;
        final payload = resp['user'] as Map<String, dynamic>?;
        if (payload != null) _user = UserRecord.fromJson(payload);
      });
      _toast(cost > 0 ? '本次消耗 ${formatPoints(cost)} 积分' : '今日首次免费');
    } catch (e) {
      if (!mounted) return;
      _toast(e.toString());
    }
  }

  // ---------------------------------------------------------------- 主题色

  Future<void> _changeTheme(String hex) async {
    if (_user == null) {
      _toast(T('user.loginFirst'));
      return;
    }
    try {
      final resp = await widget.controller.api.changeTheme(hex);
      if (!mounted) return;

      final payload = resp['user'] as Map<String, dynamic>?;
      setState(() {
        if (payload != null) _user = UserRecord.fromJson(payload);
      });
      _applyTheme();

      final refunded = (resp['refunded'] as num?)?.toInt() ?? 0;
      _toast(refunded > 0
          ? '${T('user.themeUpdated')}，${T('user.refunded')} +$refunded'
          : T('user.themeUpdated'));
    } catch (e) {
      if (!mounted) return;
      _toast(e.toString());
    }
  }

  Future<void> _pickCustomColor() async {
    if (_user == null) {
      _toast(T('user.loginFirst'));
      return;
    }

    final controller = TextEditingController(text: '#7C6CFF');
    final hex = await showDialog<String>(
      context: context,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setLocal) {
          final raw = controller.text.replaceAll('#', '').trim();
          final valid = raw.length == 6 && int.tryParse(raw, radix: 16) != null;
          final preview = valid
              ? Color(0xFF000000 | int.parse(raw, radix: 16))
              : GcColors.surface;

          return AlertDialog(
            title: Text(T('user.customColor')),
            content: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                Text(
                  T('user.customColorCost', <Object?>['${ThemeColor.customCost}']),
                  style: TextStyle(color: GcColors.muted, fontSize: 13),
                ),
                const SizedBox(height: GcSpace.md),
                TextField(
                  controller: controller,
                  autofocus: true,
                  decoration: InputDecoration(
                    labelText: '#RRGGBB',
                    border: const OutlineInputBorder(),
                    errorText: valid || raw.isEmpty ? null : T('user.hexInvalid'),
                  ),
                  onChanged: (_) => setLocal(() {}),
                ),
                const SizedBox(height: GcSpace.md),
                Row(
                  children: <Widget>[
                    Container(
                      width: 44,
                      height: 44,
                      decoration: BoxDecoration(
                        color: preview,
                        borderRadius: BorderRadius.circular(GcRadius.control),
                        border: Border.all(color: GcColors.line),
                      ),
                    ),
                    const SizedBox(width: GcSpace.md),
                    Expanded(
                      child: Text(
                        valid ? '#${raw.toUpperCase()}' : '—',
                        style: TextStyle(color: GcColors.muted),
                      ),
                    ),
                  ],
                ),
              ],
            ),
            actions: <Widget>[
              TextButton(
                onPressed: () => Navigator.of(ctx).pop(),
                child: Text(T('common.cancel')),
              ),
              FilledButton(
                onPressed:
                    valid ? () => Navigator.of(ctx).pop(raw.toUpperCase()) : null,
                child: Text(T('user.confirmBuy')),
              ),
            ],
          );
        },
      ),
    );

    controller.dispose();
    if (hex != null) await _changeTheme(hex);
  }

  // ---------------------------------------------------------------- 工具

  void _toast(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(message)));
  }

  String _formatRefund(int seconds) {
    if (seconds >= 60) return '${seconds ~/ 60} ${T('user.minutes')}';
    return '$seconds ${T('user.seconds')}';
  }

  // ---------------------------------------------------------------- UI

  @override
  Widget build(BuildContext context) {
    if (_loading) {
      return Scaffold(
        backgroundColor: GcColors.appBg,
        body: const Center(child: CircularProgressIndicator()),
      );
    }

    return Scaffold(
      backgroundColor: GcColors.appBg,
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(GcSpace.xxl),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: <Widget>[
            _header(),
            const SizedBox(height: GcSpace.lg),
            if (_user == null) ...[
              _authCard(),
              const SizedBox(height: GcSpace.lg),
            ],
            _fortuneCard(),
            const SizedBox(height: GcSpace.lg),
            _pickCard(),
            const SizedBox(height: GcSpace.lg),
            _themeCard(),
            if (_error != null) ...[
              const SizedBox(height: GcSpace.md),
              Text(_error!, style: TextStyle(color: GcColors.danger)),
            ],
          ],
        ),
      ),
    );
  }

  Widget _header() {
    final user = _user;
    return GcCard(
      padding: const EdgeInsets.all(GcSpace.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Row(
            children: <Widget>[
              CircleAvatar(
                radius: 22,
                backgroundColor: GcColors.accentSoft,
                child: Icon(Icons.person_rounded, color: GcColors.accent),
              ),
              const SizedBox(width: GcSpace.md),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    Text(
                      user?.username ?? T('user.notLoggedIn'),
                      style: const TextStyle(
                          fontSize: 18, fontWeight: FontWeight.w700),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      user == null
                          ? T('user.loginHint')
                          : '${T('user.points')} ${formatPoints(user.points)} · '
                              '${T('user.days')} ${user.consecutiveDays}',
                      style: TextStyle(color: GcColors.muted, fontSize: 13),
                    ),
                  ],
                ),
              ),
              if (user != null)
                OutlinedButton(
                  onPressed: _logout,
                  child: Text(T('user.logout')),
                ),
            ],
          ),
          if (user != null) ...[
            const SizedBox(height: GcSpace.lg),
            Row(
              children: <Widget>[
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: <Widget>[
                      Text(
                        T('user.cycleDay',
                            <Object?>['${user.cycleDay}', '${user.cycleDays}']),
                        style: TextStyle(color: GcColors.muted, fontSize: 13),
                      ),
                      const SizedBox(height: 6),
                      ClipRRect(
                        borderRadius: BorderRadius.circular(GcRadius.pill),
                        child: LinearProgressIndicator(
                          value: user.cycleDay / user.cycleDays,
                          minHeight: 6,
                          backgroundColor: GcColors.surfaceAlt,
                        ),
                      ),
                      const SizedBox(height: 6),
                      Text(
                        user.todaySigned
                            ? T('user.signedToday')
                            : T('user.nextReward', <Object?>['${user.nextReward}']),
                        style: TextStyle(color: GcColors.faint, fontSize: 12),
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: GcSpace.lg),
                FilledButton(
                  onPressed: user.todaySigned ? null : _signin,
                  child: Text(user.todaySigned
                      ? T('user.signedTodayShort')
                      : T('user.signin')),
                ),
              ],
            ),
          ],
        ],
      ),
    );
  }

  Widget _authCard() {
    return GcCard(
      padding: const EdgeInsets.all(GcSpace.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Row(
            children: <Widget>[
              Text(
                _isLogin ? T('user.login') : T('user.register'),
                style:
                    const TextStyle(fontSize: 16, fontWeight: FontWeight.w600),
              ),
              const Spacer(),
              TextButton(
                onPressed: () {
                  setState(() {
                    _isLogin = !_isLogin;
                    _captcha = null;
                    _captchaCtrl.clear();
                  });
                  if (!_isLogin) _refreshCaptcha();
                },
                child: Text(_isLogin
                    ? T('user.switchToRegister')
                    : T('user.switchToLogin')),
              ),
            ],
          ),
          const SizedBox(height: GcSpace.md),
          TextField(
            controller: _usernameCtrl,
            decoration: InputDecoration(
              labelText: T('user.username'),
              helperText: _isLogin ? null : T('user.usernameRule'),
              border: const OutlineInputBorder(),
            ),
          ),
          const SizedBox(height: GcSpace.md),
          TextField(
            controller: _passwordCtrl,
            obscureText: true,
            decoration: InputDecoration(
              labelText: T('user.password'),
              border: const OutlineInputBorder(),
            ),
            onSubmitted: (_) => _submit(),
          ),

          // 注册才需要人机验证
          if (!_isLogin) ...[
            const SizedBox(height: GcSpace.lg),
            Row(
              children: <Widget>[
                Expanded(
                  child: Container(
                    padding: const EdgeInsets.symmetric(
                        horizontal: GcSpace.md, vertical: GcSpace.md),
                    decoration: BoxDecoration(
                      color: GcColors.surfaceAlt,
                      borderRadius: BorderRadius.circular(GcRadius.control),
                      border: Border.all(color: GcColors.line),
                    ),
                    child: Row(
                      children: <Widget>[
                        Icon(Icons.shield_outlined,
                            size: 18, color: GcColors.muted),
                        const SizedBox(width: GcSpace.sm),
                        Text(
                          _captchaLoading
                              ? T('common.loading')
                              : (_captcha?.question ?? T('user.captcha')),
                          style: const TextStyle(
                              fontSize: 15,
                              fontWeight: FontWeight.w600,
                              letterSpacing: 1),
                        ),
                      ],
                    ),
                  ),
                ),
                IconButton(
                  tooltip: T('user.refreshCaptcha'),
                  onPressed: _captchaLoading ? null : _refreshCaptcha,
                  icon: const Icon(Icons.refresh_rounded),
                ),
              ],
            ),
            const SizedBox(height: GcSpace.md),
            TextField(
              controller: _captchaCtrl,
              keyboardType: TextInputType.number,
              decoration: InputDecoration(
                labelText: T('user.captchaAnswer'),
                border: const OutlineInputBorder(),
              ),
              onSubmitted: (_) => _submit(),
            ),
          ],

          const SizedBox(height: GcSpace.lg),
          SizedBox(
            height: GcSize.control,
            child: FilledButton(
              onPressed: _submitting ? null : _submit,
              child: _submitting
                  ? const SizedBox(
                      width: 18,
                      height: 18,
                      child: CircularProgressIndicator(strokeWidth: 2))
                  : Text(_isLogin ? T('user.login') : T('user.register')),
            ),
          ),
          const SizedBox(height: GcSpace.sm),
          Text(
            T('user.noRecharge'),
            style: TextStyle(color: GcColors.faint, fontSize: 12),
          ),
        ],
      ),
    );
  }

  Widget _fortuneCard() {
    return GcCard(
      padding: const EdgeInsets.all(GcSpace.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Text(T('user.fortune'),
              style:
                  const TextStyle(fontSize: 16, fontWeight: FontWeight.w600)),
          const SizedBox(height: 4),
          Text(T('user.fortuneDesc'),
              style: TextStyle(color: GcColors.muted, fontSize: 13)),
          const SizedBox(height: GcSpace.lg),
          Row(
            children: <Widget>[
              Container(
                width: 96,
                height: 96,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: GcColors.accentSoft,
                  shape: BoxShape.circle,
                  border: Border.all(color: GcColors.accent, width: 2),
                ),
                child: Text(
                  _rolling ? '…' : (_fortune?.value.toString() ?? '?'),
                  style: const TextStyle(
                      fontSize: 34, fontWeight: FontWeight.w700, height: 1.0),
                ),
              ),
              const SizedBox(width: GcSpace.lg),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    Text(
                      _fortune?.comment ?? T('user.fortuneIdle'),
                      style: const TextStyle(
                          fontSize: 14, fontWeight: FontWeight.w600),
                    ),
                    const SizedBox(height: GcSpace.md),
                    FilledButton(
                      onPressed: _rolling ? null : _rollFortune,
                      child: Text(_fortune == null
                          ? T('user.fortuneRoll')
                          : T('user.fortuneAgain')),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _pickCard() {
    return GcCard(
      padding: const EdgeInsets.all(GcSpace.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Text(T('user.pick'),
              style:
                  const TextStyle(fontSize: 16, fontWeight: FontWeight.w600)),
          const SizedBox(height: 4),
          Text(T('user.pickDesc'),
              style: TextStyle(color: GcColors.muted, fontSize: 13)),
          const SizedBox(height: GcSpace.md),
          Row(
            children: <Widget>[
              Expanded(
                child: TextField(
                  controller: _option1Ctrl,
                  decoration: InputDecoration(
                    labelText: T('user.option1'),
                    border: const OutlineInputBorder(),
                  ),
                ),
              ),
              const SizedBox(width: GcSpace.md),
              Expanded(
                child: TextField(
                  controller: _option2Ctrl,
                  decoration: InputDecoration(
                    labelText: T('user.option2'),
                    border: const OutlineInputBorder(),
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: GcSpace.md),
          Row(
            children: <Widget>[
              FilledButton(
                onPressed: _pickRandom,
                child: Text(T('user.pickRandom')),
              ),
              if (_randomResult != null) ...[
                const SizedBox(width: GcSpace.lg),
                Expanded(
                  child: Container(
                    padding: const EdgeInsets.symmetric(
                        horizontal: GcSpace.md, vertical: GcSpace.sm),
                    decoration: BoxDecoration(
                      color: GcColors.accentSoft,
                      borderRadius: BorderRadius.circular(GcRadius.control),
                    ),
                    child: Text(
                      '${T('user.pickResult')} ${_randomResult!}',
                      style: const TextStyle(
                          fontSize: 15, fontWeight: FontWeight.w600),
                    ),
                  ),
                ),
              ],
            ],
          ),
        ],
      ),
    );
  }

  Widget _themeCard() {
    final user = _user;
    final current = user?.themeColor?.toUpperCase();
    final canRefund = (user?.refundSeconds ?? 0) > 0;

    return GcCard(
      padding: const EdgeInsets.all(GcSpace.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Text(T('user.themeColor'),
              style:
                  const TextStyle(fontSize: 16, fontWeight: FontWeight.w600)),
          const SizedBox(height: 4),
          Text(T('user.themeDesc'),
              style: TextStyle(color: GcColors.muted, fontSize: 13)),
          if (canRefund) ...[
            const SizedBox(height: 6),
            Text(
              T('user.refundHint', <Object?>[_formatRefund(user!.refundSeconds)]),
              style: TextStyle(color: GcColors.ok, fontSize: 12),
            ),
          ],
          const SizedBox(height: GcSpace.lg),
          Wrap(
            spacing: GcSpace.md,
            runSpacing: GcSpace.md,
            children: <Widget>[
              ...ThemeColor.predefined.map((c) => _swatch(
                    name: c.name,
                    hex: c.hex,
                    cost: c.cost,
                    selected: current == c.hex.toUpperCase(),
                  )),
              _customSwatch(
                selected: current != null &&
                    ThemeColor.costOf(current) == ThemeColor.customCost,
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _swatch({
    required String name,
    required String hex,
    required int cost,
    required bool selected,
  }) {
    final points = _user?.points ?? 0;
    final affordable = cost == 0 || points >= cost;

    return InkWell(
      onTap: () => _changeTheme(hex),
      borderRadius: BorderRadius.circular(GcRadius.control),
      child: Column(
        children: <Widget>[
          Stack(
            children: <Widget>[
              Container(
                width: 56,
                height: 56,
                decoration: BoxDecoration(
                  color: Color(0xFF000000 | int.parse(hex, radix: 16)),
                  borderRadius: BorderRadius.circular(GcRadius.control),
                  border: Border.all(
                    color: selected ? GcColors.accent : GcColors.line,
                    width: selected ? 2.5 : 1,
                  ),
                ),
              ),
              if (selected)
                const Positioned(
                  right: 3,
                  bottom: 3,
                  child: Icon(Icons.check_circle_rounded,
                      size: 16, color: Colors.white),
                ),
              if (!affordable)
                Positioned.fill(
                  child: Container(
                    decoration: BoxDecoration(
                      // ignore: deprecated_member_use
                      color: Colors.black.withOpacity(0.45),
                      borderRadius: BorderRadius.circular(GcRadius.control),
                    ),
                    child: const Icon(Icons.lock_outline,
                        size: 18, color: Colors.white),
                  ),
                ),
            ],
          ),
          const SizedBox(height: 4),
          Text(name, style: TextStyle(fontSize: 11, color: GcColors.muted)),
          Text(
            cost == 0 ? T('user.free') : '$cost',
            style: TextStyle(
              fontSize: 11,
              color: affordable ? GcColors.faint : GcColors.danger,
            ),
          ),
        ],
      ),
    );
  }

  Widget _customSwatch({required bool selected}) {
    final canAfford = (_user?.points ?? 0) >= ThemeColor.customCost;
    return InkWell(
      onTap: _pickCustomColor,
      borderRadius: BorderRadius.circular(GcRadius.control),
      child: Column(
        children: <Widget>[
          Container(
            width: 56,
            height: 56,
            decoration: BoxDecoration(
              gradient: const SweepGradient(
                colors: <Color>[
                  Color(0xFFFF7A7A),
                  Color(0xFFFFC46B),
                  Color(0xFF6BE3A2),
                  Color(0xFF4FD1C5),
                  Color(0xFF7C6CFF),
                  Color(0xFFFF8FB1),
                  Color(0xFFFF7A7A),
                ],
              ),
              borderRadius: BorderRadius.circular(GcRadius.control),
              border: Border.all(
                color: selected ? GcColors.accent : GcColors.line,
                width: selected ? 2.5 : 1,
              ),
            ),
            child: Icon(Icons.colorize_rounded,
                size: 20, color: canAfford ? Colors.white : Colors.white70),
          ),
          const SizedBox(height: 4),
          Text(T('user.custom'),
              style: TextStyle(fontSize: 11, color: GcColors.muted)),
          Text(
            '${ThemeColor.customCost}',
            style: TextStyle(
              fontSize: 11,
              color: canAfford ? GcColors.faint : GcColors.danger,
            ),
          ),
        ],
      ),
    );
  }
}
