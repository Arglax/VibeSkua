using System;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;

namespace Skua.App.Avalonia.ViewModels.Plugins;

public partial class PluginItemViewModel : ObservableObject
{
    private readonly MethodInfo? _openWindowMethod;
    private readonly MethodInfo? _openSettingsMethod;

    public PluginItemViewModel(IPluginContainer container, bool hasOptions)
    {
        Container = container;
        _openWindowMethod = container.Plugin.GetType().GetMethod(
            "OpenWindow",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            Type.EmptyTypes);
        _openSettingsMethod = container.Plugin.GetType().GetMethod(
            "OpenSettings",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            Type.EmptyTypes);
        HasOptions = hasOptions || _openSettingsMethod is not null;
    }

    public IPluginContainer Container { get; }
    public bool HasOptions { get; }
    public bool CanOpen => _openWindowMethod is not null;
    public string Author => Container.Plugin.Author;
    public string Description => Container.Plugin.Description;

    [RelayCommand]
    private void OpenPlugin()
    {
        _openWindowMethod?.Invoke(Container.Plugin, null);
    }

    [RelayCommand]
    private void EditPluginOptions()
    {
        if (_openSettingsMethod is not null)
        {
            _openSettingsMethod.Invoke(Container.Plugin, null);
            return;
        }
        Container.OptionContainer.Configure();
    }

    [RelayCommand]
    private void Unload()
    {
        Ioc.Default.GetRequiredService<IPluginManager>().Unload(Container.Plugin);
    }
}
