using System.Collections.ObjectModel;
using AdaVoice.Core.Domain;
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdaVoice.App.ViewModels;

/// <summary>
/// Backs the "Edit phrase" dialog: the editable title, the chosen category, and the phrase's tags as a
/// chip editor (add a new tag, remove one, or add an existing tag from the registry suggestions).
/// <see cref="Save"/> writes all three through the <see cref="ILibraryHost"/> and returns the final
/// stored entry so the caller can refresh the board item. Pure (no XAML), so it is unit-testable.
/// </summary>
public partial class PhraseEditViewModel : ObservableObject
{
    private readonly ILibraryHost _library;
    private readonly string _phraseId;

    public PhraseEditViewModel(ILibraryHost library, PhraseEntry entry)
    {
        _library = library;
        _phraseId = entry.Id;
        Categories = library.Categories;
        Title = entry.Title;
        SelectedCategoryId = entry.CategoryId;

        Tags = new ObservableCollection<string>(entry.Tags);
        Tags.CollectionChanged += (_, _) => RefreshSuggestions();
        RefreshSuggestions();
    }

    /// <summary>The categories the dialog's picker offers.</summary>
    public IReadOnlyList<Category> Categories { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidTitle))]
    private string _title;

    [ObservableProperty]
    private string _selectedCategoryId;

    /// <summary>The tag being typed in the "add" box.</summary>
    [ObservableProperty]
    private string _newTag = "";

    /// <summary>The phrase's current tags, shown as removable chips.</summary>
    public ObservableCollection<string> Tags { get; }

    /// <summary>Registry tag names not already on this phrase — offered as clickable suggestions so the
    /// operator can reuse an existing tag instead of retyping it.</summary>
    public ObservableCollection<string> Suggestions { get; } = [];

    /// <summary>The Save button binds its enabled state to this — an empty title is rejected.</summary>
    public bool HasValidTitle => !string.IsNullOrWhiteSpace(Title);

    /// <summary>Add the tag typed in <see cref="NewTag"/> to the phrase.</summary>
    [RelayCommand]
    private void AddTag()
    {
        AddTagNamed(NewTag);
        NewTag = "";
    }

    /// <summary>Add an existing tag (clicked from the suggestions) to the phrase.</summary>
    [RelayCommand]
    private void AddSuggestion(string? name) => AddTagNamed(name);

    /// <summary>Remove a tag chip from the phrase.</summary>
    [RelayCommand]
    private void RemoveTag(string? name)
    {
        if (name is null)
            return;

        var existing = Tags.FirstOrDefault(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            Tags.Remove(existing);
    }

    private void AddTagNamed(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
            return;
        if (Tags.Any(t => string.Equals(t, trimmed, StringComparison.OrdinalIgnoreCase)))
            return; // already on the phrase (case-insensitive) — no duplicate chip

        Tags.Add(trimmed);
    }

    private void RefreshSuggestions()
    {
        Suggestions.Clear();
        foreach (var info in _library.Tags)
            if (!Tags.Any(t => string.Equals(t, info.Name, StringComparison.OrdinalIgnoreCase)))
                Suggestions.Add(info.Name);
    }

    /// <summary>
    /// Apply the title, category, and tags. Returns the final updated entry, or null if the title is
    /// blank or the phrase no longer exists. Title is applied first; if that succeeds the category and
    /// tags follow, each returning the latest entry. New tag names are registered by the service.
    /// </summary>
    public PhraseEntry? Save()
    {
        if (!HasValidTitle)
            return null;

        var updated = _library.SetPhraseTitle(_phraseId, Title);
        if (updated is null)
            return null; // phrase gone (e.g. deleted in another view) — nothing to edit

        updated = _library.SetPhraseCategory(_phraseId, SelectedCategoryId) ?? updated;
        updated = _library.SetPhraseTags(_phraseId, Tags) ?? updated;
        return updated;
    }
}
