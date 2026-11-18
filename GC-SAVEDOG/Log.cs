using System;
using GC.Services;

namespace GC.Savedog;

/// <summary>
/// 守护进程的日志入口，实际写入 <see cref="GcLog"/>：控制台 + <c>.GC/logs</c> 下的
/// <c>gc-YYYYMMDD.log</c>（中文）与 <c>gc-YYYYMMDD.en.log</c>（英文）。
///
/// 每条日志都必须同时给中英两份文案 —— 用户反馈问题时直接发对应语言的文件。
/// </summary>
internal sealed class Log
{
    public Log(string? dir)
    {
        if (!string.IsNullOrWhiteSpace(dir)) GcLog.Init(dir);
    }

    public void Info(string zh, string en) => GcLog.Info(zh, en);
    public void Warn(string zh, string en) => GcLog.Warn(zh, en);
    public void Error(string zh, string en) => GcLog.Error(zh, en);
}
