using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Plugin.Cosmetics.Services;
using Skua.Plugin.Cosmetics.Views;
using Skua.Shared.Avalonia.Services;

namespace Skua.Plugin.Cosmetics;

public class CosmeticsPlugin : ISkuaPlugin
{
    public string Name => "Cosmetics";
    public string Author => "NinjaXz & Lord Exelot";
    public string Description => "Visually loads any cosmetic item without owning it and manages saved outfit loadouts.";
    public List<IOption>? Options => [];

    private IServiceProvider? _provider;
    private IPluginHelper? _helper;
    private CosmeticsService? _service;
    private PluginWindow? _window;

    public void Load(IServiceProvider provider, IPluginHelper helper)
    {
        _provider = provider;
        _helper = helper;

        var bot = _provider.GetRequiredService<IScriptInterface>();
        _service = new CosmeticsService(bot);

        _helper.AddMenuButton("Cosmetics", ShowWindow);
    }

    public void Unload()
    {
        _helper?.RemoveMenuButton("Cosmetics");

        _service?.Dispose();

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
            var bot = _provider!.GetRequiredService<IScriptInterface>();
            var view = new CosmeticsWindow(bot, _service!);
            _window = PluginWindow.Show(view, "Cosmetics & Outfits", 1020, 730, 880, 580, ownTitleBar: true);
            _window.Closed += (s, e) =>
            {
                view.OnClosed();
                _window = null;
            };
        });
    }
}
