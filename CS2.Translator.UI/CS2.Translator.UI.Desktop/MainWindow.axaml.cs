using System;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CS2.Translator.Core.Helper;
using CS2.Translator.Core.Services;
using CS2.Translator.UI.ViewModels;

namespace CS2.Translator.UI.Views;

public partial class MainWindow : Window
{
    private readonly ConfigService? _configService;
    private readonly TranslationSession? _session;
    private readonly MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        try
        {
            _configService = new ConfigService();
            _configService.Load();

            DebugLogger.Log(
                $"Config loaded: language='{_configService.Config.Language}', " +
                $"player='{_configService.Config.PlayerName}', path='{_configService.Config.InstallationPath}'",
                "MainWindow");

            // Everything the session raises is marshalled onto the UI thread here,
            // so neither the log reader nor the translation queue touches it directly.
            _session = new TranslationSession(_configService, action => Dispatcher.UIThread.Post(action));

            _viewModel = new MainViewModel(_session, _configService);
            _viewModel.SettingsRequested += OpenSettings;

            Content = new MainView(_viewModel);
        }
        catch (Exception ex)
        {
            // Previously this was swallowed, leaving null service fields behind and
            // an empty window that threw as soon as Settings was clicked.
            DebugLogger.LogException(ex, "MainWindow constructor");
            Content = BuildFailureView(ex);
        }

        Closed += OnClosed;
    }

    private static Control BuildFailureView(Exception ex) => new Border
    {
        Padding = new Avalonia.Thickness(24),
        Background = new SolidColorBrush(Color.Parse("#0E0E12")),
        Child = new StackPanel
        {
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "CS2 Translator could not start",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.White
                },
                new SelectableTextBlock
                {
                    Text = ex.Message,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.Parse("#FF7B72"))
                },
                new TextBlock
                {
                    Text = "Delete config.json in the data folder to reset the settings, "
                           + "or start with -debug for a full log.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7,
                    Foreground = Brushes.White
                }
            }
        }
    };

    private void OpenSettings()
    {
        if (_configService is null)
            return;

        try
        {
            var vm = new SettingsViewModel(_configService);
            var window = new SettingsWindow(vm);

            window.ShowDialog(this);
        }
        catch (Exception ex)
        {
            DebugLogger.LogException(ex, "OpenSettings");
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.SettingsRequested -= OpenSettings;

        _viewModel?.Dispose();

        // Disposing the session stops the watcher and poll timer and flushes the
        // translation cache to disk.
        _session?.Dispose();

        DebugLogger.Log("Main window closed", "MainWindow");
    }
}
