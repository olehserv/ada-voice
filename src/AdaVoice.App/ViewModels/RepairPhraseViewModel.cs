using AdaVoice.Core.Domain;

namespace AdaVoice.App.ViewModels;

/// <summary>What the operator chose in the repair-phrase dialog.</summary>
public enum RepairChoice
{
    ReRecord,
    Remove,
}

/// <summary>Backs the repair-phrase dialog for a broken (audio-missing) phrase. Plain state and
/// two setters the dialog's buttons call directly — no commands needed, since the dialog's
/// code-behind records the choice and closes with <c>DialogResult = true</c> in the same click
/// handler (mirrors <see cref="PhraseEditViewModel"/>'s "caller reads state after ShowDialog"
/// shape, simplified since there's no form data to edit here).</summary>
public sealed class RepairPhraseViewModel(PhraseEntry entry)
{
    /// <summary>The broken phrase's title, shown in the dialog.</summary>
    public string Title => entry.Title;

    /// <summary>What the operator chose, or null if the dialog is still open / was cancelled.</summary>
    public RepairChoice? Choice { get; private set; }

    public void ChooseReRecord() => Choice = RepairChoice.ReRecord;
    public void ChooseRemove() => Choice = RepairChoice.Remove;
}
