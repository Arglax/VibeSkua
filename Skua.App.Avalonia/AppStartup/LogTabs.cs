using Microsoft.Extensions.DependencyInjection;
using Skua.App.Avalonia.ViewModels.AppLogs;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using System;
using System.Collections.Generic;

namespace Skua.App.Avalonia.AppStartup;

internal static class LogTabs
{
    internal static IEnumerable<LogTabViewModel> CreateViewModels(IServiceProvider s)
    {
        ILogService logService = s.GetRequiredService<ILogService>();
        IDispatcherService dispatcherService = s.GetRequiredService<IDispatcherService>();
        // The script's log first; the Debug and Flash tabs are developer tools.
        return AppFeatures.DeveloperTools
            ? new[] { Create("Script", LogType.Script), Create("Debug", LogType.Debug), Create("Flash", LogType.Flash) }
            : new[] { Create("Script", LogType.Script) };

        LogTabViewModel Create(string title, LogType logType) => new(title, logService, dispatcherService, logType);
    }
}