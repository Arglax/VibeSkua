using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Shared.Avalonia.Services;

namespace Skua.Plugin.DailyTracker;

public class DailyTrackerPlugin : ISkuaPlugin
{
    public string Name => "Daily & Weekly Tracker";
    public string Author => "NinjaXz";
    public string Description => "Tracks the status of Daily and Weekly quests.";
    public List<IOption>? Options => [];

    private IServiceProvider? _provider;
    private IPluginHelper? _helper;
    private PluginWindow? _window;

    public void Load(IServiceProvider provider, IPluginHelper helper)
    {
        _provider = provider;
        _helper = helper;

        _helper.AddMenuButton("Daily Tracker", ShowWindow);
    }

    public void Unload()
    {
        _helper?.RemoveMenuButton("Daily Tracker");
        Dispatcher.UIThread.Post(() =>
        {
            _window?.Close();
            _window = null;
        });
    }

    private void ShowWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_window != null)
            {
                _window.Activate();
                return;
            }
            IScriptInterface bot = _provider!.GetRequiredService<IScriptInterface>();
            DailyTrackerWindow view = new(bot);
            _window = PluginWindow.Show(view, "Daily & Weekly Tracker", 520, 720, 440, 500, ownTitleBar: true);
            _window.Closed += (s, e) =>
            {
                view.OnClosed();
                _window = null;
            };
        });
    }
}
