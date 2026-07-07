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

    [ObservableProperty]
    private string _newName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ConversationRowViewModel? _selectedRow;

    public ConversationsViewModel(ILibraryHost library)
    {
        _library = library;
        Rows = new ObservableCollection<ConversationRowViewModel>(
            library.Conversations.Select(c => new ConversationRowViewModel(c, library)));
        _selectedRow = Rows.FirstOrDefault();
    }

    /// <summary>One editable row per conversation.</summary>
    public ObservableCollection<ConversationRowViewModel> Rows { get; }

    /// <summary>True once a conversation is selected — the step editor panel shows only then.</summary>
    public bool HasSelection => SelectedRow is not null;

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
    }

    /// <summary>Persist a row's edited name.</summary>
    [RelayCommand]
    private void Rename(ConversationRowViewModel? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.Name))
            return;

        _library.RenameConversation(row.Id, row.Name);
    }

    [RelayCommand]
    private void Delete(ConversationRowViewModel? row)
    {
        if (row is null)
            return;

        if (_library.DeleteConversation(row.Id))
        {
            Rows.Remove(row);
            if (SelectedRow == row)
                SelectedRow = Rows.FirstOrDefault();
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

    public ConversationRowViewModel(Conversation conversation, ILibraryHost library)
    {
        _library = library;
        Id = conversation.Id;
        _name = conversation.Name;
        Members = new ObservableCollection<ConversationPhraseRowViewModel>(BuildMembers(conversation.PhraseIds));
    }

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
