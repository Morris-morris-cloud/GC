#!/bin/sh
# buildsh/lib/core.sh —— 版本清单 / 签名 / 模型构建
#
# 这些步骤原来全用 dotnet run --project，每次都要走一遍 MSBuild（还不算输出噪音）。
# 现在统一走 dt_tool：首次构建工具程序集，之后直接跑 dll。

# 只在清单需要重算时生成；--no-sign 用于完整构建的中途（打包结束后统一签一次）
stamp_versions() {
  say "生成 version.json"
  run_logged "$GC_ROOT/build/logs/stamp.log" "生成 version.json " \
    dt_tool stamp "$STAMP_PROJ" VersionStamp "$GC_ROOT" "$VERSION" || return 1
  step_ok "version.json（${VERSION}）"
  case "${1:-}" in
    --no-sign) detail "跳过中途签名，打包结束后统一签一次" ;;
    *)         sign_manifest ;;
  esac
}

# 打完 zip 后只刷新启动器段（path / dir / name / sha256 / size）。
# 模型指纹涉及几百 MB 文件，没必要再算一遍。
stamp_launcher_only() {
  say "刷新包信息（sha256 / 体积）"
  run_logged "$GC_ROOT/build/logs/stamp-launcher.log" "刷新包信息 " \
    dt_tool stamp "$STAMP_PROJ" VersionStamp "$GC_ROOT" "$VERSION" --launcher-only || return 1
  sign_manifest
}

# 用 .signing/ 里的私钥签 version.json（私钥不入库）。没有私钥只警告：
# 清单不带 signature，客户端按「未签名」处理。
#
# keyId 必须与私钥真正对应，否则签出来的清单自己都验不过（客户端更会拒）：
# 本地私钥可能是上一代 gc-2026-11，而默认值写的是 gc-2026-12。
# 所以这里从 SignatureKeyring.cs 取出所有已注册 keyId 逐个试签，第一个通过验签的胜出。
sign_manifest() {
  _key="$GC_ROOT/.signing/gc-signing.pem"
  if [ ! -f "$_key" ]; then
    warn "未找到签名私钥 .signing/gc-signing.pem，version.json 未签名（客户端会拒绝更新）"
    note "生成密钥：dotnet run --project GC-SAVEDOG -- keygen"
    return 0
  fi

  _ids="${GC_SIGNING_KEY_ID:-}"
  if [ -z "$_ids" ]; then
    _kr="$GC_ROOT/GC/src/Security/SignatureKeyring.cs"
    [ -f "$_kr" ] && _ids=$(grep -oE '"gc-[0-9]{4}-[0-9]{2}"' "$_kr" | tr -d '"' | sort -u | tr '\n' ' ')
  fi
  [ -n "$_ids" ] || _ids="gc-2026-12"

  _tmp="$GC_ROOT/build/tmp/version.signed.json"
  mkdir -p "$(dirname "$_tmp")"
  for _id in $_ids; do
    if dt_tool savedog "$SAVEDOG_PROJ" GC-SAVEDOG sign \
         --manifest "$GC_ROOT/version.json" --key "$_key" \
         --key-id "$_id" --out "$_tmp" >"$GC_ROOT/build/logs/sign.log" 2>&1; then
      mv "$_tmp" "$GC_ROOT/version.json"
      # model/version.json 必须与根清单完全一致（同一签名），否则走 model 地址的源会验签失败
      cp "$GC_ROOT/version.json" "$GC_ROOT/model/version.json"
      step_ok "已签名（${_id}）"
      return 0
    fi
    detail "keyId ${_id} 与本地私钥不匹配，换下一个"
  done

  err "签名失败：本地私钥与内置公钥环里的 keyId 都对不上"
  note "可用 GC_SIGNING_KEY_ID=<id> 指定；或用 keygen 重新生成并登记公钥"
  return 1
}

# 模型已全部用 Flutter 实现（GC-UI/frontend/lib/models/），不再编译 C# 程序集，
# 也不再需要 Avalonia 渲染引擎生成封面。这里只刷新清单里的模型指纹。
build_models() {
  say "模型清单"
  note "模型渲染已迁到 Flutter，跳过 C# 程序集编译与封面生成"
  stamp_versions
}
