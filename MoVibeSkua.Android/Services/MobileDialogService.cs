using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using CommunityToolkit.Mvvm.DependencyInjection;
using MoVibeSkua.Android.Platform;
using Skua.App.Avalonia.ViewModels.AdvancedSkills;
using Skua.App.Avalonia.ViewModels.Dialogs;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.ViewModels;
using Skua.Core.Models;
using Skua.Shared.Avalonia.Controls;
using Skua.Shared.Avalonia.Controls.Options;
using Skua.Shared.Avalonia.ViewModels.Dialogs;
using Skua.Shared.Avalonia.ViewModels.Options;

namespace MoVibeSkua.Android.Services;

/// <summary>
/// <see cref="IDialogService"/> that draws dialogs as cards over the shell instead of windows.
/// Mirrors the desktop DialogService dialog by dialog.
/// </summary>
public class MobileDialogService : IDialogService
{
    public bool? ShowDialog<TViewModel>(TViewModel viewModel) where TViewModel : class
        => ShowDialogInternal(viewModel, DefaultTitle(viewModel));

    public bool? ShowDialog<TViewModel>(TViewModel viewModel, string title) where TViewModel : class
        => ShowDialogInternal(viewModel, title);

    public bool? ShowDialog<TViewModel>(TViewModel viewModel, Action<TViewModel> callback) where TViewModel : class
    {
        bool? result = ShowDialogInternal(viewModel, DefaultTitle(viewModel));
        if (result == true)
            callback?.Invoke(viewModel);
        return result;
    }

    public void ShowDialog(IOptionContainer optionContainer, Action<IOptionContainerViewModel> callback)
    {
        OptionContainerViewModel? vm = null;
        bool? result = Modal.Run<bool?>(close =>
        {
            vm = new OptionContainerViewModel(optionContainer);
            return BuildOptionContainer(vm, "Script Options", close);
        }, null);

        if (result == true && vm is not null)
            callback?.Invoke(vm);
    }

    public void ShowMessageBox(string message, string caption)
    {
        // Informational only; don't block a script thread on it.
        System.Diagnostics.Trace.WriteLine($"[Dialog] {caption}: {message}");
        Modal.Show(close => BuildButtons(caption, message, ["OK"], close));
    }

    public bool? ShowMessageBox(string message, string caption, bool yesAndNo)
    {
        string[] buttons = yesAndNo ? ["No", "Yes"] : ["OK"];
        string clicked = Modal.Run<string>(close => BuildButtons(caption, message, buttons, close), string.Empty);
        return clicked == "Yes" || clicked == "OK";
    }

    public DialogResult ShowMessageBox(string message, string caption, params string[] buttons)
    {
        string[] actual = buttons.Length == 0 ? ["OK"] : buttons;
        string clicked = Modal.Run<string>(close => BuildButtons(caption, message, actual, close), string.Empty);
        int index = Array.IndexOf(actual, clicked);
        return index < 0 ? DialogResult.Cancelled : new DialogResult(clicked, index);
    }

    public IInputDialogViewModel CreateInputDialog(string title, string dialogHint)
    {
        return new InputDialogViewModel(title, dialogHint);
    }

    private static string DefaultTitle(object viewModel)
    {
        return viewModel is DialogViewModelBase { Title: { Length: > 0 } title } ? title : viewModel.GetType().Name;
    }

    private static bool? ShowDialogInternal<TViewModel>(TViewModel viewModel, string title) where TViewModel : class
    {
        switch (viewModel)
        {
            case OptionContainerViewModel options:
                return Modal.Run<bool?>(close => BuildOptionContainer(options, string.IsNullOrWhiteSpace(title) || title == nameof(OptionContainerViewModel) ? options.Title : title, close), null);

            case IOptionContainerViewModel optionsVm:
                OptionContainerViewModel wrapped = optionsVm as OptionContainerViewModel ?? new OptionContainerViewModel(optionsVm.Container);
                return Modal.Run<bool?>(close => BuildOptionContainer(wrapped, title, close), null);

            case InputDialogViewModel input:
                return Modal.Run<bool?>(close => BuildInput(input, title, close), null);

            case AssignHotKeyDialogViewModel:
                Ioc.Default.GetRequiredService<IDialogService>().ShowMessageBox("Hotkeys need a hardware keyboard. Connect one and use the HotKeys page to assign keys.", "HotKeys");
                return false;

            case SkillRuleEditorDialogViewModel skillRules:
                return Modal.Run<bool?>(close => BuildSkillRules(skillRules, title, close), null);

            case MessageBoxDialogViewModel messageBox:
                return Ioc.Default.GetRequiredService<IDialogService>().ShowMessageBox(messageBox.Message, messageBox.Title, messageBox.YesAndNo);

            case CustomDialogViewModel custom:
                string clicked = Modal.Run<string>(close => BuildButtons(title, custom.Message, custom.Buttons.ToArray(), close), string.Empty);
                int index = custom.Buttons.IndexOf(clicked);
                custom.Result = index < 0 ? DialogResult.Cancelled : new DialogResult(clicked, index);
                return index >= 0;

            default:
                // Anything with a DataTemplate renders as-is with an OK button.
                return Modal.Run<bool?>(close => BuildCard(title,
                    new ContentControl { Content = viewModel, HorizontalContentAlignment = HorizontalAlignment.Stretch },
                    Footer(Primary("OK", () => close(true)))), null);
        }
    }

    #region Builders

    private static Control BuildOptionContainer(OptionContainerViewModel vm, string title, Action<bool?> close)
    {
        OptionContainerUserControl content = new() { DataContext = vm, MinHeight = 240 };
        Button reset = Plain("Reset Defaults", vm.ResetAllToDefaults);
        Grid footer = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        footer.Children.Add(reset);
        StackPanel actions = Footer(Plain("Cancel", () => close(false)), Primary("Save", () => close(true)));
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        return BuildCard(string.IsNullOrWhiteSpace(title) ? "Script Options" : title, content, footer, stretch: true);
    }

    private static Control BuildInput(InputDialogViewModel vm, string title, Action<bool?> close)
    {
        TextBox input = new() { Text = vm.DialogTextInput, Watermark = vm.TextBoxHint };
        if (vm.NumberOnly)
        {
            input.TextChanged += (_, _) =>
            {
                string digits = new(input.Text?.Where(char.IsDigit).ToArray() ?? Array.Empty<char>());
                if (input.Text != digits)
                    input.Text = digits;
            };
        }

        StackPanel body = new() { Spacing = 8 };
        if (!string.IsNullOrWhiteSpace(vm.DialogHint) && vm.DialogHint != title)
            body.Children.Add(new TextBlock { Text = vm.DialogHint, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(input);

        return BuildCard(string.IsNullOrWhiteSpace(title) ? vm.Title : title, body, Footer(
            Plain("Cancel", () => close(false)),
            Primary("OK", () =>
            {
                vm.DialogTextInput = input.Text ?? string.Empty;
                close(true);
            })));
    }

    private static Control BuildSkillRules(SkillRuleEditorDialogViewModel vm, string title, Action<bool?> close)
    {
        SkillRulesViewModel scratch = new(vm.UseRules);
        PropertyGridControl grid = new() { SelectedObject = scratch, MinHeight = 300 };
        return BuildCard(string.IsNullOrWhiteSpace(title) ? "Edit Rules" : title, grid, Footer(
            Plain("Cancel", () => close(false)),
            Primary("Confirm", () =>
            {
                CopySkillRules(scratch, vm.UseRules);
                close(true);
            })), stretch: true);
    }

    private static Control BuildButtons(string title, string message, string[] buttons, Action<string> close)
    {
        SelectableTextBlock label = new() { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 20 };
        ScrollViewer scroll = new() { Content = label, MaxHeight = 420 };

        Button copy = Plain("Copy", () => Ioc.Default.GetService<IClipboardService>()?.SetText(message));
        StackPanel actions = Footer();
        foreach (string text in buttons)
        {
            string captured = text;
            Button b = text is "OK" or "Yes" ? Primary(text, () => close(captured)) : Plain(text, () => close(captured));
            actions.Children.Add(b);
        }

        Grid footer = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        footer.Children.Add(copy);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        return BuildCard(title, scroll, footer);
    }

    private static Border BuildCard(string title, Control content, Control footer, bool stretch = false)
    {
        TextBlock header = new() { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        Grid layout = new() { RowDefinitions = new RowDefinitions(stretch ? "Auto,*,Auto" : "Auto,Auto,Auto"), RowSpacing = 12, Margin = new Thickness(16) };
        Grid.SetRow(header, 0);
        Grid.SetRow(content, 1);
        Grid.SetRow(footer, 2);
        layout.Children.Add(header);
        layout.Children.Add(content);
        layout.Children.Add(footer);

        Border card = new() { Child = layout };
        card.Classes.Add("mobile-dialog");
        if (stretch)
        {
            card.VerticalAlignment = VerticalAlignment.Stretch;
            card.Margin = new Thickness(8, 24);
        }
        return card;
    }

    private static StackPanel Footer(params Button[] buttons)
    {
        StackPanel panel = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        foreach (Button b in buttons)
            panel.Children.Add(b);
        return panel;
    }

    private static Button Primary(string text, Action onClick)
    {
        Button b = Plain(text, onClick);
        b.Classes.Add("skua-primary");
        return b;
    }

    private static Button Plain(string text, Action onClick)
    {
        Button b = new() { Content = text, MinWidth = 80, Classes = { "skua-button" } };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static void CopySkillRules(SkillRulesViewModel source, SkillRulesViewModel target)
    {
        target.UseRuleBool = source.UseRuleBool;
        target.MultiAuraBool = source.MultiAuraBool;
        target.HealthGreaterThanBool = source.HealthGreaterThanBool;
        target.HealthUseValue = source.HealthUseValue;
        target.HealthIsPercentage = source.HealthIsPercentage;
        target.ManaGreaterThanBool = source.ManaGreaterThanBool;
        target.ManaUseValue = source.ManaUseValue;
        target.ManaIsPercentage = source.ManaIsPercentage;
        target.WaitUseValue = source.WaitUseValue;
        target.SkipUseBool = source.SkipUseBool;
        target.AuraGreaterThanBool = source.AuraGreaterThanBool;
        target.AuraUseValue = source.AuraUseValue;
        target.AuraTargetIndex = source.AuraTargetIndex;
        target.AuraName = source.AuraName;
        target.PartyMemberHealthGreaterThanBool = source.PartyMemberHealthGreaterThanBool;
        target.PartyMemberHealthUseValue = source.PartyMemberHealthUseValue;
        target.PartyMemberHealthIsPercentage = source.PartyMemberHealthIsPercentage;
        target.MultiAuraOperatorIndex = source.MultiAuraOperatorIndex;

        target.MultiAuraChecks.Clear();
        foreach (AuraCheckViewModel check in source.MultiAuraChecks)
            target.MultiAuraChecks.Add(new AuraCheckViewModel(check));
    }

    #endregion
}
