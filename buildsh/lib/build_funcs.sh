#!/bin/sh
# buildsh/lib/build_funcs.sh —— 兼容入口
#
# 原先是单文件函数库，现按职责拆成：
#   log.sh      输出层（say / step_ok / note / detail / warn / err / run_logged）
#   progress.sh 进度显示（终端进度条 / 非终端逐行）
#   publish.sh  环境预检、工具程序集缓存、dotnet 发布、阶段执行
#   core.sh     版本清单、签名、模型构建
#   flutter.sh  Flutter 构建与落位
#   package.sh  组装 .app、生成发布 zip
#
# 保留本文件是为了老的 `source buildsh/lib/build_funcs.sh` 用法继续可用。
_bf_root="${GC_ROOT:-${ROOT_DIR:?build_funcs.sh 需要先设置 ROOT_DIR 或 GC_ROOT}}"
for _bf_m in log progress publish core flutter package; do
  . "$_bf_root/buildsh/lib/$_bf_m.sh"
done
unset _bf_m _bf_root
