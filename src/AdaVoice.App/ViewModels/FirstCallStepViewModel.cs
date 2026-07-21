using System.Collections.ObjectModel;
using AdaVoice.App.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>Setup-wizard's final step: a 3-item checklist nudging a real test call before trusting
/// the app on a client call. The checked state is local UI feedback only — never persisted.</summary>
public sealed class FirstCallStepViewModel : ObservableObject, IWizardStep
{
    public ObservableCollection<ChecklistItem> Checklist { get; } =
    [
        new(Strings.FirstCall_Check1),
        new(Strings.FirstCall_Check2),
        new(Strings.FirstCall_Check3),
    ];

    public bool CanAdvance => true;
}

/// <summary>One line of the first-call checklist. Its checked state is local-only (not persisted)
/// — it exists to make the operator consciously confirm each step, not to gate anything.</summary>
public sealed partial class ChecklistItem : ObservableObject
{
    public ChecklistItem(string text) => Text = text;

    public string Text { get; }

    [ObservableProperty]
    private bool _isChecked;
}
