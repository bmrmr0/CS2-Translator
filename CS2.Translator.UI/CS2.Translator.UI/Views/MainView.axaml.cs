using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CS2.Translator.Core.Helper;
using CS2.Translator.UI.ViewModels;

namespace CS2.Translator.UI.Views;

public partial class MainView : UserControl
{
    private MainViewModel? _subscribed;

    /// <summary>Parameterless constructor for the XAML loader and the previewer.</summary>
    public MainView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    public MainView(MainViewModel vm) : this()
    {
        DataContext = vm;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => Subscribe();

    private void OnUnloaded(object? sender, RoutedEventArgs e) => Unsubscribe();

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Unsubscribe();

        if (IsLoaded)
            Subscribe();
    }

    private void Subscribe()
    {
        if (DataContext is not MainViewModel vm || ReferenceEquals(vm, _subscribed))
            return;

        Unsubscribe();

        vm.Chats.CollectionChanged += OnChatsChanged;
        _subscribed = vm;
    }

    private void Unsubscribe()
    {
        if (_subscribed is null)
            return;

        _subscribed.Chats.CollectionChanged -= OnChatsChanged;
        _subscribed = null;
    }

    private void OnChatsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add &&
            e.Action != NotifyCollectionChangedAction.Reset)
        {
            return;
        }

        // Newest messages are inserted at the top, so keep the view pinned there.
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                ChatScrollViewer.ScrollToHome();
            }
            catch (Exception ex)
            {
                DebugLogger.LogException(ex, "ScrollToHome");
            }
        }, DispatcherPriority.Background);
    }
}
