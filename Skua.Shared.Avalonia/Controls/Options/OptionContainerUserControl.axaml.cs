using Avalonia.Controls;
using Avalonia.Input;
using Skua.Shared.Avalonia.ViewModels.Options;

namespace Skua.Shared.Avalonia.Controls.Options;

public partial class OptionContainerUserControl : UserControl
{
    public OptionContainerUserControl()
    {
        InitializeComponent();
        // MVS: the phone layout (see the styles in the .axaml).
        if (System.OperatingSystem.IsAndroid())
            Classes.Add("mvs");
    }

    private void OnOptionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is OptionContainerItemViewModel item)
        {
            if (DataContext is OptionContainerViewModel vm)
            {
                vm.SelectedOption = item;
            }
        }
    }
}
