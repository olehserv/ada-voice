using System.Collections.ObjectModel;
using AdaVoice.Core.Domain;
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdaVoice.App.ViewModels;

/// <summary>
/// Backs the "Manage conversations" dialog: add/rename/delete a conversation, and edit which phrases
/// it contains and in what order. Each change is written straight through the
/// <see cref="ILibraryHost"/>. Pure (no XAML), so it is unit-testable with a fake host — mirrors
/// <see cref="CategoriesViewModel"/>.
/// </summary>
public partial class ConversationsViewModel : ObservableObject
{
    private readonly ILibraryHost _library;
    private readonly Func<ConversationRowViewModel, Task<bool>> _confirmDelete;

    [ObservableProperty]
    private string _newName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ConversationRowViewModel? _selectedRow;

    public ConversationsViewModel(ILibraryHost library, Func<ConversationRowViewModel, Task<bool>>? confirmDelete = null)
    {
        _library = library;
        _confirmDelete = confirmDelete ?? (_ => Task.FromResult(true)); // default: confirm (unit tests)
        Rows = new ObservableCollection<ConversationRowViewModel>(
            library.Conversations.Select(c => new ConversationRowViewModel(c, library)));
        _selectedRow = Rows.FirstOrDefault();
    }

    /// <summary>One editable row per conversation.</summary>
    public ObservableCollection<ConversationRowViewModel> Rows { get; }

    /// <summary>True once a conversation is selected — the step editor panel shows only then.</summary>
    public bool HasSelection => SelectedRow is not null;

    /// <summary>True when there are no conversations yet — the list panel shows a hint instead of
    /// an empty box (finding 10).</summary>
    public bool HasNoConversations => Rows.Count == 0;

    [RelayCommand]
    private void Add()
    {
        if (string.IsNullOrWhiteSpace(NewName))
            return;

        var conversation = _library.AddConversation(NewName);
        var row = new ConversationRowViewModel(conversation, _library);
        Rows.Add(row);
        SelectedRow = row;
        NewName = "";
        OnPropertyChanged(nameof(HasNoConversations));
    }

    /// <summary>Persist a row's edited name. A blank name is refused (like the library layer itself
    /// requires) — revert the field to the persisted name instead of leaving it blank on screen while
    /// storage still has the old value (review finding 8).</summary>
    [RelayCommand]
    private void Rename(ConversationRowViewModel? row)
    {
        if (row is null)
            return;

        if (string.IsNullOrWhiteSpace(row.Name))
        {
            row.Name = _library.Conversations.FirstOrDefault(c => c.Id == row.Id)?.Name ?? row.Name;
            return;
        }

        _library.RenameConversation(row.Id, row.Name);
    }

    [RelayCommand]
    private async Task Delete(ConversationRowViewModel? row)
    {
        if (row is null)
            return;

        if (!await _confirmDelete(row))
            return;

        if (_library.DeleteConversation(row.Id))
        {
            Rows.Remove(row);
            if (SelectedRow == row)
                SelectedRow = Rows.FirstOrDefault();
            OnPropertyChanged(nameof(HasNoConversations));
        }
    }
}

/// <summary>One conversation in the manager: its editable name and its ordered phrase list. Every
/// membership/order change calls <see cref="ILibraryHost.SetConversationPhrases"/> immediately — like
/// <see cref="CategoriesViewModel"/>, there is no separate "Save" step for the step list.</summary>
public partial class ConversationRowViewModel : ObservableObject
{
    private readonly ILibraryHost _library;

    public string Id { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddablePhrases))]
    private PhraseEntry? _phraseToAdd;

    [ObservableProperty]
    private bool _useRandomVersion;

    public ConversationRowViewModel(Conversation conversation, ILibraryHost library)
    {
        _library = library;
        Id = conversation.Id;
        _name = conversation.Name;
        _useRandomVersion = conversation.UseRandomVersion;
        Members = new ObservableCollection<ConversationPhraseRowViewModel>(BuildMembers(conversation.PhraseIds));
    }

    /// <summary>Persist the random-version flag immediately, like every other edit in this row — no
    /// separate Save step.</summary>
    partial void OnUseRandomVersionChanged(bool value) => _library.SetConversationUseRandomVersion(Id, value);

    /// <summary>False while the library refuses writes (a transiently locked file) — the checkbox
    /// binds its <c>IsEnabled</c> to this, so a refused edit shows as "disabled" instead of throwing
    /// inside the binding engine, where WPF would swallow it silently (review finding 9).</summary>
    public bool IsWritable => _library.IsWritable;

    /// <summary>The conversation's phrases, in call order.</summary>
    public ObservableCollection<ConversationPhraseRowViewModel> Members { get; }

    /// <summary>Phrases not already in this conversation — the Add-phrase picker's source. Excluding
    /// current members keeps the picker from ever offering a phrase already added (no duplicate
    /// membership through this dialog).</summary>
    public IReadOnlyList<PhraseEntry> AddablePhrases =>
        _library.Phrases.Where(p => Members.All(m => m.PhraseId != p.Id)).ToList();

    [RelayCommand]
    private void AddPhrase()
    {
        if (PhraseToAdd is not { } phrase)
            return;

        Members.Add(new ConversationPhraseRowViewModel(phrase.Id, phrase.Title));
        Persist();
        PhraseToAdd = null;
        OnPropertyChanged(nameof(AddablePhrases));
    }

    [RelayCommand]
    private void RemovePhrase(ConversationPhraseRowViewModel? row)
    {
        if (row is null)
            return;

        Members.Remove(row);
        Persist();
        OnPropertyChanged(nameof(AddablePhrases));
    }

    [RelayCommand]
    private void MoveUp(ConversationPhraseRowViewModel? row)
    {
        if (row is null)
            return;

        var index = Members.IndexOf(row);
        if (index > 0)
        {
            Members.Move(index, index - 1);
            Persist();
        }
    }

    [RelayCommand]
    private void MoveDown(ConversationPhraseRowViewModel? row)
    {
        if (row is null)
            return;

        var index = Members.IndexOf(row);
        if (index >= 0 && index < Members.Count - 1)
        {
            Members.Move(index, index + 1);
            Persist();
        }
    }

    private IEnumerable<ConversationPhraseRowViewModel> BuildMembers(IReadOnlyList<string> phraseIds)
    {
        var titleById = _library.Phrases.ToDictionary(p => p.Id, p => p.Title);
        return phraseIds.Select(id =>
            new ConversationPhraseRowViewModel(id, titleById.TryGetValue(id, out var t) ? t : "(deleted)"));
    }

    private void Persist() => _library.SetConversationPhrases(Id, Members.Select(m => m.PhraseId).ToList());
}

/// <summary>One phrase's row in a conversation's step list.</summary>
public sealed record ConversationPhraseRowViewModel(string PhraseId, string Title);
