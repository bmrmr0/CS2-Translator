using System.ComponentModel;
using System.Runtime.CompilerServices;
using CS2.Translator.Core.Enums;

namespace CS2.Translator.Core.Models;

public class Chat : Log, INotifyPropertyChanged
{
    public ChatType ChatType { get; }
    public string Name { get; }
    public string Message { get; }

    /// <summary>When the line was picked up, used for ordering and display.</summary>
    public DateTime ReceivedAt { get; } = DateTime.Now;

    /// <summary>True when the line was sent by the configured player, so it is never translated.</summary>
    public bool IsOwnMessage { get; set; }

    private Translation _translation;
    public Translation Translation
    {
        get => _translation;
        set
        {
            if (ReferenceEquals(_translation, value))
                return;

            _translation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TranslationText));
        }
    }

    private TranslationState _state = TranslationState.Pending;
    public TranslationState State
    {
        get => _state;
        set
        {
            if (_state == value)
                return;

            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPending));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(HasTranslation));
            OnPropertyChanged(nameof(TranslationText));
        }
    }

    public bool IsPending => State == TranslationState.Pending;
    public bool HasError => State == TranslationState.Failed;

    /// <summary>True once a real translation is showing, so the original is worth displaying under it.</summary>
    public bool HasTranslation => State == TranslationState.Translated
                                  && !string.IsNullOrEmpty(Translation.Text)
                                  && !string.Equals(Translation.Text, Message, StringComparison.Ordinal);

    /// <summary>Short badge for team/dead/spectator chat. Empty for ordinary all-chat.</summary>
    public string ChatTypeLabel => ChatType switch
    {
        ChatType.Team => "TEAM",
        ChatType.Dead => "DEAD",
        ChatType.Spectator => "SPEC",
        _ => string.Empty
    };

    public bool HasChatTypeLabel => ChatTypeLabel.Length > 0;

    // Per-type flags so the view can gate each badge on its own setting without a converter.
    public bool IsTeamChat => ChatType == ChatType.Team;
    public bool IsDeadChat => ChatType == ChatType.Dead;
    public bool IsSpectatorChat => ChatType == ChatType.Spectator;

    /// <summary>
    /// What the UI shows as the main line: the translation once it lands,
    /// the original while it is still pending or when translation was skipped.
    /// </summary>
    public string TranslationText => State switch
    {
        TranslationState.Pending => Message,
        TranslationState.Skipped => Message,
        _ => string.IsNullOrEmpty(Translation.Text) ? Message : Translation.Text
    };

    public Chat(string rawString, ChatType chatType, string name, string message)
        : base(rawString)
    {
        ChatType = chatType;
        Name = name;
        Message = message;
        _translation = new Translation(string.Empty, string.Empty);
    }

    /// <summary>Records a completed translation and flips the state in one notification pass.</summary>
    public void Complete(Translation translation, TranslationState state)
    {
        Translation = translation;
        State = state;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
