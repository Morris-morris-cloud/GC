import 'package:flutter/material.dart';
import 'dart:math';
import '../controller.dart';
import '../theme.dart';
import '../i18n.dart';
import '../models/user.dart';

/// 用户页面：登录/注册、签到、主题颜色、二选一
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

  // 登录/注册表单
  final _usernameCtrl = TextEditingController();
  final _passwordCtrl = TextEditingController();
  bool _isLogin = true;

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
    _option1Ctrl.dispose();
    _option2Ctrl.dispose();
    super.dispose();
  }

  Future<void> _loadUser() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      await widget.controller.api.initUserSession();
      final resp = await widget.controller.api.me();
      if (!mounted) return;
      if (resp != null) {
        setState(() {
          _user = UserRecord.fromJson(resp);
          _loading = false;
        });
      } else {
        setState(() {
          _loading = false;
        });
      }
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = e.toString();
      });
    }
  }

  Future<void> _loginOrRegister() async {
    final username = _usernameCtrl.text.trim();
    final password = _passwordCtrl.text;
    if (username.isEmpty || password.isEmpty) {
      _showError(T('user.fillAll'));
      return;
    }

    try {
      Map<String, dynamic> resp;
      if (_isLogin) {
        resp = await widget.controller.api.login(username, password);
      } else {
        resp = await widget.controller.api.register(username, password);
        if (!mounted) return;
        // 注册成功后自动登录
        resp = await widget.controller.api.login(username, password);
      }
      if (!mounted) return;
      setState(() {
        _user = UserRecord.fromJson(resp);
        _usernameCtrl.clear();
        _passwordCtrl.clear();
      });
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(_isLogin ? T('user.loginSuccess') : T('user.registerSuccess'))),
      );
    } catch (e) {
      if (!mounted) return;
      _showError(e.toString());
    }
  }

  Future<void> _signin() async {
    try {
      final resp = await widget.controller.api.signin();
      if (!mounted) return;
      if (resp.containsKey('message') && resp['message'] == '今日已签到') {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(T('user.alreadySigned'))),
        );
        return;
      }
      setState(() {
        _user = UserRecord.fromJson({
          ...resp,
          'username': _user?.username ?? '',
        });
      });
      final reward = resp['reward'] as int? ?? 0;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('${T('user.signinSuccess')} +$reward ${T('user.points')}')),
      );
    } catch (e) {
      if (!mounted) return;
      _showError(e.toString());
    }
  }

  void _pickRandom() {
    final opt1 = _option1Ctrl.text.trim();
    final opt2 = _option2Ctrl.text.trim();
    if (opt1.isEmpty || opt2.isEmpty) {
      _showError(T('user.fillOptions'));
      return;
    }
    final result = Random().nextBool() ? opt1 : opt2;
    setState(() => _randomResult = result);
  }

  Future<void> _changeTheme(String hex, int cost) async {
    if (_user == null) {
      _showError(T('user.loginFirst'));
      return;
    }
    try {
      final resp = await widget.controller.api.changeTheme(hex, cost);
      if (!mounted) return;
      if (resp.containsKey('message')) {
        setState(() {
          _user = UserRecord(
            username: _user!.username,
            points: resp['points'] as int? ?? _user!.points,
            consecutiveDays: _user!.consecutiveDays,
            lastSignIn: _user!.lastSignIn,
            themeColor: '0x${resp['theme_color'] ?? hex}',
          );
        });
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('主题已更新')),
        );
      }
    } catch (e) {
      if (!mounted) return;
      _showError(e.toString());
    }
  }

  void _showError(String msg) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(msg)),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: GcColors.appBg,
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : SingleChildScrollView(
              padding: const EdgeInsets.all(GcSpace.xxl),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: <Widget>[
                  // 用户信息卡片
                  if (_user != null) ...[
                    GcCard(
                      padding: const EdgeInsets.all(GcSpace.lg),
                      child: Row(
                        children: <Widget>[
                          Icon(Icons.person_rounded, size: 40, color: GcColors.accent),
                          const SizedBox(width: GcSpace.md),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: <Widget>[
                                Text(
                                  _user!.username,
                                  style: const TextStyle(
                                    fontSize: 18,
                                    fontWeight: FontWeight.w700,
                                  ),
                                ),
                                const SizedBox(height: 4),
                                Text(
                                  '${T('user.points')}: ${_user!.points}',
                                  style: TextStyle(color: GcColors.muted),
                                ),
                                Text(
                                  '${T('user.days')}: ${_user!.consecutiveDays}',
                                  style: TextStyle(color: GcColors.muted),
                                ),
                              ],
                            ),
                          ),
                          FilledButton(
                            onPressed: _signin,
                            child: Text(T('user.signin')),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: GcSpace.lg),
                  ],

                  // 登录/注册表单
                  GcCard(
                    padding: const EdgeInsets.all(GcSpace.lg),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: <Widget>[
                        Text(
                          _isLogin ? T('user.login') : T('user.register'),
                          style: const TextStyle(
                            fontSize: 16,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                        const SizedBox(height: GcSpace.md),
                        TextField(
                          controller: _usernameCtrl,
                          decoration: InputDecoration(
                            labelText: T('user.username'),
                            border: const OutlineInputBorder(),
                          ),
                        ),
                        const SizedBox(height: GcSpace.md),
                        TextField(
                          controller: _passwordCtrl,
                          decoration: InputDecoration(
                            labelText: T('user.password'),
                            border: const OutlineInputBorder(),
                          ),
                          obscureText: true,
                        ),
                        const SizedBox(height: GcSpace.md),
                        FilledButton(
                          onPressed: _loginOrRegister,
                          child: Text(_isLogin ? T('user.login') : T('user.register')),
                        ),
                        TextButton(
                          onPressed: () => setState(() => _isLogin = !_isLogin),
                          child: Text(_isLogin ? T('user.switchToRegister') : T('user.switchToLogin')),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: GcSpace.lg),

                  // 二选一
                  GcCard(
                    padding: const EdgeInsets.all(GcSpace.lg),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: <Widget>[
                        Text(
                          T('user.pick'),
                          style: const TextStyle(
                            fontSize: 16,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                        const SizedBox(height: GcSpace.md),
                        TextField(
                          controller: _option1Ctrl,
                          decoration: InputDecoration(
                            labelText: T('user.option1'),
                            border: const OutlineInputBorder(),
                          ),
                        ),
                        const SizedBox(height: GcSpace.md),
                        TextField(
                          controller: _option2Ctrl,
                          decoration: InputDecoration(
                            labelText: T('user.option2'),
                            border: const OutlineInputBorder(),
                          ),
                        ),
                        const SizedBox(height: GcSpace.md),
                        FilledButton(
                          onPressed: _pickRandom,
                          child: Text(T('user.pickRandom')),
                        ),
                        if (_randomResult != null) ...[
                          const SizedBox(height: GcSpace.md),
                          GcCard(
                            child: Text(
                              _randomResult!,
                              textAlign: TextAlign.center,
                              style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
                            ),
                          ),
                        ],
                      ],
                    ),
                  ),
                  const SizedBox(height: GcSpace.lg),

                  // 主题颜色
                  GcCard(
                    padding: const EdgeInsets.all(GcSpace.lg),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: <Widget>[
                        Text(
                          T('user.theme'),
                          style: const TextStyle(
                            fontSize: 16,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                        const SizedBox(height: GcSpace.md),
                        Wrap(
                          spacing: GcSpace.md,
                          runSpacing: GcSpace.md,
                          children: ThemeColor.predefined.map((c) {
                            final canAfford = _user == null || _user!.points >= c.cost;
                            final isCurrent = _user?.themeColor == '0x$c.hex';
                            return InkWell(
                              onTap: canAfford ? () => _changeTheme(c.hex, c.cost) : null,
                              borderRadius: BorderRadius.circular(8),
                              child: Container(
                                width: 60,
                                height: 60,
                                decoration: BoxDecoration(
                                  color: Color(int.parse('0xFF${c.hex}')),
                                  borderRadius: BorderRadius.circular(8),
                                  border: Border.all(
                                    color: isCurrent ? GcColors.accent : GcColors.line,
                                    width: isCurrent ? 2 : 1,
                                  ),
                                ),
                                child: Stack(
                                  children: <Widget>[
                                    if (c.cost > 0 && !_canAfford(c.cost))
                                      Container(
                                        decoration: BoxDecoration(
                                          color: Colors.black.withOpacity(0.5),
                                          borderRadius: BorderRadius.circular(8),
                                        ),
                                        child: Center(
                                          child: Text(
                                            '${c.cost}',
                                            style: const TextStyle(color: Colors.white, fontSize: 12),
                                          ),
                                        ),
                                      ),
                                    if (isCurrent)
                                      const Positioned(
                                        bottom: 2,
                                        right: 2,
                                        child: Icon(Icons.check_rounded, size: 16, color: Colors.white),
                                      ),
                                  ],
                                ),
                              ),
                            );
                          }).toList(),
                        ),
                      ],
                    ),
                  ),
                  if (_error != null) ...[
                    const SizedBox(height: GcSpace.md),
                    Text(_error!, style: const TextStyle(color: Colors.red)),
                  ],
                ],
              ),
            ),
    );
  }

  bool _canAfford(int cost) {
    if (cost <= 0) return true;
    if (_user == null) return false;
    return _user!.points >= cost;
  }
}
