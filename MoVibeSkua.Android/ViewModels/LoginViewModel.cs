using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Core.Models.Servers;

namespace MoVibeSkua.Android.ViewModels;

/// <summary>
/// Native login form. Typing into the game's own login box works too, but this skips the soft
/// keyboard fight with Flash text fields and arms Skua's auto relogin.
/// </summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private const string PrefsName = "movibeskua.login";
    private const string ReloginTag = "[Relogin]";
    private readonly IScriptInterface _bot;
    private readonly ISharedPreferences? _prefs;
    private CancellationTokenSource? _loginCts;

    public LoginViewModel(IScriptInterface bot)
    {
        _bot = bot;
        _prefs = global::Android.App.Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
        _username = _prefs?.GetString("user", string.Empty) ?? string.Empty;
        _password = _prefs?.GetString("pass", string.Empty) ?? string.Empty;
        _server = _prefs?.GetString("server", "Twilly") ?? "Twilly";
        _remember = _prefs?.GetBoolean("remember", false) ?? false;
        _ = LoadServers();
    }

    public ObservableCollection<string> Servers { get; } = new();

    [ObservableProperty]
    private string _username;

    [ObservableProperty]
    private string _password;

    [ObservableProperty]
    private string _server;

    [ObservableProperty]
    private bool _remember;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    private async Task LoadServers()
    {
        try
        {
            List<Server> servers = await _bot.Servers.GetServers(true);
            foreach (Server s in servers.Where(s => s.Online).OrderBy(s => s.Name))
                Servers.Add(s.Name);
        }
        catch (Exception ex)
        {
            Status = $"Could not load servers: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task Login()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            Status = "Enter a username and password.";
            return;
        }

        SaveCredentials();
        using CancellationTokenSource cts = new();
        _loginCts = cts;
        IsBusy = true;
        Status = $"Logging in to {Server}...";
        // Relogin reports each attempt to the script log; mirror it here so a slow login doesn't look frozen.
        WeakReferenceMessenger.Default.Register<LoginViewModel, AddLogMessage>(this, static (r, m) => r.OnLog(m));
        try
        {
            string user = Username.Trim();
            string pass = Password;
            string server = string.IsNullOrWhiteSpace(Server) ? "Twilly" : Server;
            bool ok = await Task.Run(() =>
            {
                _bot.Servers.SetLoginInfo(user, pass);
                _bot.Servers.IsIntentionalLogout = false;
                return _bot.Servers.EnsureRelogin(server, cts.Token);
            });
            Status = ok ? "Logged in."
                : cts.IsCancellationRequested ? "Login cancelled."
                : "Login did not complete. Check your details or try another server.";
        }
        catch (OperationCanceledException)
        {
            Status = "Login cancelled.";
        }
        catch (Exception ex)
        {
            Status = $"Login failed: {ex.Message}";
        }
        finally
        {
            WeakReferenceMessenger.Default.Unregister<AddLogMessage>(this);
            _loginCts = null;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelLogin()
    {
        _loginCts?.Cancel();
        Status = "Cancelling...";
    }

    private void OnLog(AddLogMessage message)
    {
        int tag = message.Text.IndexOf(ReloginTag, StringComparison.Ordinal);
        if (message.LogType != LogType.Script || tag < 0)
            return;
        string text = message.Text[(tag + ReloginTag.Length)..].Trim();
        System.Diagnostics.Trace.WriteLine($"[Login] {text}");
        Dispatcher.UIThread.Post(() =>
        {
            if (IsBusy)
                Status = text;
        });
    }

    [RelayCommand]
    private void Logout()
    {
        Task.Run(() =>
        {
            _bot.Servers.IsIntentionalLogout = true;
            _bot.Servers.Logout();
        });
        Status = "Logged out.";
    }

    private void SaveCredentials()
    {
        if (_prefs is null)
            return;
        ISharedPreferencesEditor? editor = _prefs.Edit();
        if (editor is null)
            return;
        editor.PutBoolean("remember", Remember);
        editor.PutString("server", Server);
        editor.PutString("user", Remember ? Username : string.Empty);
        editor.PutString("pass", Remember ? Password : string.Empty);
        editor.Apply();
    }
}
