namespace AdaVoice.App.ViewModels;

/// <summary>Severity of a board notification — drives the toast colour in the view
/// (Info = neutral, Warning = amber, Error = red).</summary>
public enum NoticeSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>A user-facing message raised by <see cref="BoardViewModel.Notified"/>; the view shows
/// it as a bottom-right toast.</summary>
public sealed record BoardNotification(string Message, NoticeSeverity Severity);
