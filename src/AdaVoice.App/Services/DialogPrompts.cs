using AdaVoice.App.Resources;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace AdaVoice.App.Services;

/// <summary>Shared CTRL-008-correct option builders for a window's hosted <see cref="ContentDialogService"/>
/// (confirm/error/info) — one place for the phrasing/button-naming rules so each window that hosts a
/// dialog doesn't re-derive them. Host wiring itself (field, SetDialogHost, the XAML overlay) still lives
/// per-window, matching the existing board pattern in MainWindow.</summary>
internal static class DialogPrompts
{
    /// <summary>Confirm before a destructive/consequential action. <paramref name="confirmButtonText"/>
    /// must name the action ("Delete phrase", not "Yes") per CTRL-008. <paramref name="cancelButtonText"/>
    /// defaults to the localized "Cancel" — null, not a literal default, since a resource lookup isn't a
    /// compile-time constant.</summary>
    public static async Task<bool> ConfirmAsync(
        ContentDialogService dialogService, string title, string message, string confirmButtonText,
        string? cancelButtonText = null)
    {
        var result = await dialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmButtonText,
            CloseButtonText = cancelButtonText ?? Strings.DialogPrompts_Cancel,
        });
        return result == ContentDialogResult.Primary;
    }

    public static Task ShowErrorAsync(ContentDialogService dialogService, string message) =>
        dialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "AdaVoice",
            Content = message,
            CloseButtonText = Strings.DialogPrompts_Ok,
        });

    public static Task ShowInfoAsync(ContentDialogService dialogService, string message) =>
        dialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "AdaVoice",
            Content = message,
            CloseButtonText = Strings.DialogPrompts_Ok,
        });
}
