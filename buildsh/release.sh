#!/bin/sh
# buildsh/release.sh —— 发布一致性核对
#
#   ./build.sh release-verify           # 核对当前版本号对应的 tag
#   ./build.sh release-verify 1.1.0     # 核对指定 tag
#   ./build.sh release-verify 1.1.0 --skip-download   # 只看体积，不下载
#
# 检查的是「tag 指向的清单 / Release 上的资产 / 清单里的 sha256」三者是否互相对得上。
# 客户端的升级判断完全依赖这三者：任何一处错位，用户侧就是验签失败或安全回滚。

bs_verify_release() {
  ROOT_DIR="$1"; shift
  TAG="${1:-$VERSION}"
  if [ "$#" -gt 0 ]; then shift; fi

  WORK="$ROOT_DIR/build/release-verify"
  mkdir -p "$WORK"

  echo "==> 核对 Release ${TAG}"
  rc=0
  python3 "$ROOT_DIR/buildsh/lib/verify_release.py" \
    --tag "$TAG" \
    --manifest "$ROOT_DIR/version.json" \
    --dump-manifest "$WORK/$TAG.json" \
    "$@" || rc=$?

  if [ "$rc" -ne 0 ]; then
    return "$rc"
  fi

  # 资产对得上还不够：清单本身必须是我签的，否则客户端一样会拒
  echo ""
  echo "==> 校验 tag 清单签名"
  if dotnet run --project "$ROOT_DIR/GC-SAVEDOG/GC-SAVEDOG.csproj" -c Release -- \
       verify --manifest "$WORK/$TAG.json"; then
    echo "结论: 签名可信，可以发布 / 已经发布正确"
    return 0
  fi

  echo "!! 签名校验未通过，客户端会拒绝这次更新"
  return 1
}
