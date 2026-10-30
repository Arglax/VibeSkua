using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using MoVibeSkua.Android.ViewModels;
using Skua.Core.Interfaces;

namespace MoVibeSkua.Android.Services;

/// <summary>
/// Desktop "windows" become pages in the shell's page stack.
/// </summary>
public class MobileWindowService : IWindowService
{
    private readonly Dictionary<string, Func<IManagedWindow>> _factories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IManagedWindow> _instances = new(StringComparer.OrdinalIgnoreCase);

    public void ShowWindow<TViewModel>(int width, int height) where TViewModel : class => ShowWindow<TViewModel>();

    public void ShowWindow<TViewModel>() where TViewModel : class
    {
        Dispatcher.UIThread.Post(() =>
        {
            TViewModel vm = Ioc.Default.GetRequiredService<TViewModel>();
            Show(typeof(TViewModel).FullName!, TitleOf(vm), vm);
        });
    }

    public void ShowWindow<TViewModel>(TViewModel viewModel) where TViewModel : class
    {
        Dispatcher.UIThread.Post(() => Show(typeof(TViewModel).FullName + "#" + viewModel.GetHashCode(), TitleOf(viewModel), viewModel));
    }

    public void ShowManagedWindow(string key)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_instances.TryGetValue(key, out IManagedWindow? vm))
            {
                if (!_factories.TryGetValue(key, out Func<IManagedWindow>? factory))
                    return;
                vm = factory();
                _instances[key] = vm;
            }
            Show(key, string.IsNullOrWhiteSpace(vm.Title) ? key : vm.Title, vm);
        });
    }

    public void RegisterManagedWindow<TViewModel>(string key, TViewModel viewModel) where TViewModel : class, IManagedWindow
    {
        _instances[key] = viewModel;
    }

    public void RegisterManagedWindow<TViewModel>(string key) where TViewModel : class, IManagedWindow
    {
        _factories[key] = () => Ioc.Default.GetRequiredService<TViewModel>();
    }

    public void RegisterManagedWindow(string key, Func<IManagedWindow> factory)
    {
        _factories[key] = factory;
    }

    private static void Show(string key, string title, object viewModel)
    {
        Ioc.Default.GetRequiredService<ShellViewModel>().ShowPage(key, title, viewModel);
    }

    private static string TitleOf(object viewModel)
    {
        if (viewModel is IManagedWindow managed && !string.IsNullOrWhiteSpace(managed.Title))
            return managed.Title;
        if (viewModel is Skua.Shared.Avalonia.ViewModels.BotControlViewModelBase control && !string.IsNullOrWhiteSpace(control.Title))
            return control.Title;

        string name = viewModel.GetType().Name;
        if (name.EndsWith("ViewModel", StringComparison.Ordinal))
            name = name[..^"ViewModel".Length];
        return Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
    }
}
