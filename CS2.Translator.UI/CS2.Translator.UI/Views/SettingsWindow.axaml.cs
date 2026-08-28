using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CS2.Translator.UI.ViewModels;

namespace CS2.Translator.UI.Views;

public partial class SettingsWindow : Window
{
    /// <summary>Parameterless constructor for the XAML loader and the previewer.</summary>
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsViewModel vm) : this()
    {
        DataContext = vm;
        vm.PickFolderAsync = PickFolderAsync;
        vm.CloseRequested += Close;

        Closed += (_, _) =>
        {
            vm.CloseRequested -= Close;
            vm.PickFolderAsync = null;
        };
    }

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the Counter-Strike Global Offensive folder",
            AllowMultiple = false
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
}
