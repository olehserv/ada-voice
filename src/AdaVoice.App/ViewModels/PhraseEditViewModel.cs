using AdaVoice.Core.Domain;
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>
/// Backs the "Edit phrase" dialog: the editable title, the chosen category, and a comma-separated tags
/// box. <see cref="Save"/> writes all three through the <see cref="ILibraryHost"/> and returns the final
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
        TagsText = string.Join(", ", entry.Tags);
    }

    /// <summary>The categories the dialog's picker offers.</summary>
    public IReadOnlyList<Category> Categories { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidTitle))]
    private string _title;

    [ObservableProperty]
    private string _selectedCategoryId;

    /// <summary>Tags as the user types them, comma-separated. The service trims, drops blanks, and
    /// de-duplicates; multi-word tags survive because we split only on commas.</summary>
    [ObservableProperty]
    private string _tagsText;

    /// <summary>The Save button binds its enabled state to this — an empty title is rejected.</summary>
    public bool HasValidTitle => !string.IsNullOrWhiteSpace(Title);

    /// <summary>
    /// Apply the title, category, and tags. Returns the final updated entry, or null if the title is
    /// blank or the phrase no longer exists. Title is applied first; if that succeeds the category and
    /// tags follow, each returning the latest entry.
    /// </summary>
    public PhraseEntry? Save()
    {
        if (!HasValidTitle)
            return null;

        var updated = _library.SetPhraseTitle(_phraseId, Title);
        if (updated is null)
            return null; // phrase gone (e.g. deleted in another view) — nothing to edit

        updated = _library.SetPhraseCategory(_phraseId, SelectedCategoryId) ?? updated;
        updated = _library.SetPhraseTags(_phraseId, ParseTags(TagsText)) ?? updated;
        return updated;
    }

    private static IEnumerable<string> ParseTags(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
