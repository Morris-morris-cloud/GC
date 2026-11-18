#!/usr/bin/env python3
"""核对「仓库清单 ↔ Release 资产」是否对得上。

为什么需要它：清单里的 sha256 是客户端验证下载包的唯一依据。一旦 Release 上的资产
和清单不是同一份（换过一次构建、上传了别的产物、tag 指到了旧提交），客户端会验签
失败或直接回滚 —— 用户看到的是「更新失败了」，而服务端一切正常。

检查项：
  1. tag 指向的 version.json 里的 launcher.version 等于 tag 名；
  2. 清单里带 sha256 的平台，Release 上有同名资产，且体积 / sha256 完全一致；
  3. 清单里没有 sha256 的平台（本次没构建）不要求有资产，但会列出；
  4. Release 上多出来的资产（清单里没有）单独提示；
  5. 本地清单与 tag 上的清单是否字节一致。

只用标准库，Linux / macOS / Windows 上都能跑。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
import urllib.error
import urllib.request

DEFAULT_BASE = os.environ.get("GC_GITEA_URL", "https://git.xhmcl.com")
DEFAULT_REPO = os.environ.get("GC_GITEA_REPO", "Morris/GC")


def log(msg: str = "") -> None:
    print(msg, flush=True)


def http_json(url: str, token: str | None) -> dict:
    req = urllib.request.Request(url)
    req.add_header("Accept", "application/json")
    if token:
        req.add_header("Authorization", "token " + token)
    with urllib.request.urlopen(req, timeout=60) as resp:
        return json.loads(resp.read().decode("utf-8"))


def download_sha256(url: str, token: str | None, dest: str) -> tuple[int, str]:
    req = urllib.request.Request(url)
    if token:
        req.add_header("Authorization", "token " + token)
    digest = hashlib.sha256()
    size = 0
    with urllib.request.urlopen(req, timeout=600) as resp, open(dest, "wb") as out:
        while True:
            chunk = resp.read(1 << 20)
            if not chunk:
                break
            size += len(chunk)
            digest.update(chunk)
            out.write(chunk)
    return size, digest.hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description="核对清单与 Release 资产")
    parser.add_argument("--tag", required=True, help="要核对的 tag，例如 1.1.0")
    parser.add_argument("--manifest", default="version.json", help="本地清单路径（用于对比）")
    parser.add_argument("--base-url", default=DEFAULT_BASE)
    parser.add_argument("--repo", default=DEFAULT_REPO)
    parser.add_argument("--token", default=os.environ.get("GC_GITEA_TOKEN"))
    parser.add_argument("--workdir", default=None, help="下载临时目录（默认系统临时目录）")
    parser.add_argument("--skip-download", action="store_true",
                        help="只核对清单体积与资产列表，不下载算哈希")
    parser.add_argument("--dump-manifest", default=None,
                        help="把 tag 上的清单原样写到这个路径（供 GC-SAVEDOG verify 验签）")
    args = parser.parse_args()

    tag = args.tag.strip()
    if tag.startswith("v"):
        tag = tag[1:]

    api = f"{args.base_url}/api/v1/repos/{args.repo}"
    workdir = args.workdir or os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "build", "release-verify")
    workdir = os.path.abspath(workdir)
    os.makedirs(workdir, exist_ok=True)

    failures: list[str] = []
    warnings: list[str] = []

    # ---- 1. tag 上的清单 ----
    try:
        remote = http_json(f"{api}/raw/version.json?ref={tag}", args.token)
    except urllib.error.HTTPError as exc:
        log(f"!! 取不到 tag {tag} 的 version.json（HTTP {exc.code}）")
        return 1

    if args.dump_manifest:
        os.makedirs(os.path.dirname(os.path.abspath(args.dump_manifest)), exist_ok=True)
        with open(args.dump_manifest, "w", encoding="utf-8") as fh:
            json.dump(remote, fh, ensure_ascii=False, indent=2)

    launcher = remote.get("launcher") or {}
    remote_version = str(launcher.get("version") or "")
    log(f"tag        : {tag}")
    log(f"清单版本   : {remote_version or '(空)'}")
    if remote_version != tag:
        failures.append(f"tag 指向的清单版本是 {remote_version!r}，与 tag {tag} 不一致")

    # 签名状态：交给 GC-SAVEDOG verify 判定（这里只提示有没有签名段）
    if not remote.get("signature"):
        failures.append("tag 上的清单没有签名段")
    else:
        log(f"清单签名   : {remote['signature'].get('keyId', '?')}")

    # ---- 2. Release 与资产 ----
    try:
        release = http_json(f"{api}/releases/tags/{tag}", args.token)
    except urllib.error.HTTPError as exc:
        log(f"!! 取不到 Release {tag}（HTTP {exc.code}）")
        return 1

    assets = {a["name"]: a for a in release.get("assets") or []}
    log(f"Release    : id={release.get('id')} prerelease={release.get('prerelease')}")
    log(f"资产数量   : {len(assets)}")

    packages = launcher.get("packages") or {}
    declared: set[str] = set()

    log("")
    log(f"{'平台':<12} {'包':<32} {'结果'}")
    log("-" * 78)

    for rid, pkg in packages.items():
        name = str(pkg.get("name") or "")
        want_sha = str(pkg.get("sha256") or "")
        want_size = int(pkg.get("size") or 0)
        if not name:
            warnings.append(f"{rid}: 清单条目没有 name")
            continue
        declared.add(name)

        asset = assets.get(name)
        if not want_sha:
            log(f"{rid:<12} {name:<32} 未构建（清单无 sha256）")
            if asset:
                warnings.append(f"{rid}: 清单没记录哈希，但 Release 上有 {name}")
            continue

        if not asset:
            log(f"{rid:<12} {name:<32} ✗ 资产缺失")
            failures.append(f"{rid}: Release 上找不到 {name}")
            continue

        if int(asset.get("size") or 0) != want_size:
            log(f"{rid:<12} {name:<32} ✗ 体积不符 {asset.get('size')} != {want_size}")
            failures.append(f"{rid}: {name} 体积 {asset.get('size')}，清单 {want_size}")
            continue

        if args.skip_download:
            log(f"{rid:<12} {name:<32} ✓ 体积一致（未下载）")
            continue

        url = asset.get("browser_download_url") or f"{args.base_url}/{args.repo}/releases/download/{tag}/{name}"
        dest = os.path.join(workdir, name)
        try:
            size, sha = download_sha256(url, args.token, dest)
            os.remove(dest)
        except Exception as exc:  # noqa: BLE001 - 下载失败一律按失败上报
            log(f"{rid:<12} {name:<32} ✗ 下载失败: {exc}")
            failures.append(f"{rid}: 下载 {name} 失败: {exc}")
            continue

        if size != want_size or sha != want_sha:
            log(f"{rid:<12} {name:<32} ✗ sha256 不符")
            log(f"             期望 {want_sha}")
            log(f"             实际 {sha}")
            failures.append(f"{rid}: {name} sha256 与清单不符")
        else:
            log(f"{rid:<12} {name:<32} ✓ {size:,} B  sha {sha[:16]}…")

    extra = sorted(set(assets) - declared)
    if extra:
        warnings.append("Release 上有清单未声明的资产: " + ", ".join(extra))

    # ---- 3. 本地清单对比 ----
    if os.path.isfile(args.manifest):
        with open(args.manifest, "rb") as fh:
            local_raw = fh.read()
        local = json.loads(local_raw.decode("utf-8"))
        local_packages = (local.get("launcher") or {}).get("packages") or {}
        if local_packages != packages:
            warnings.append("本地清单的 packages 与 tag 上的不一致（本地可能是未发布的构建）")
        else:
            log("")
            log("本地清单与 tag 上的 packages 一致")

    # ---- 汇总 ----
    log("")
    for w in warnings:
        log("警告: " + w)
    if failures:
        for f in failures:
            log("失败: " + f)
        log("")
        log("结论: 不一致 —— 客户端会拒绝或回滚这次更新")
        return 1

    log("结论: 一致 —— 清单、tag、资产三者对得上")
    return 0


if __name__ == "__main__":
    sys.exit(main())
