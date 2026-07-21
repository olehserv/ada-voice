using System.Collections.ObjectModel;
using AdaVoice.App.Resources;
using AdaVoice.Core.Domain;
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdaVoice.App.ViewModels;

/// <summary>
/// Backs the "Manage categories" dialog: add a category, rename/recolour an existing one, and delete
/// (the seeded "Uncategorized" is protected). Each change is written straight through the
/// <see cref="ILibraryHost"/>. Pure (no XAML), so it is unit-testable with a fake host.
/// </summary>
public partial class CategoriesViewModel : ObservableObject
{
    private readonly ILibraryHost _library;
    private readonly Func<CategoryRowViewModel, Task<bool>> _confirmDelete;

    [ObservableProperty]
    private string _newName = "";

    [ObservableProperty]
    private string _newColor = ColorPalette.Swatches[0];

    public CategoriesViewModel(ILibraryHost library, Func<CategoryRowViewModel, Task<bool>>? confirmDelete = null)
    {
        _library = library;
        _confirmDelete = confirmDelete ?? (_ => Task.FromResult(true)); // default: confirm (unit tests)
        Rows = new ObservableCollection<CategoryRowViewModel>(library.Categories.Select(c => new CategoryRowViewModel(c)));
    }

    /// <summary>One editable row per category.</summary>
    public ObservableCollection<CategoryRowViewModel> Rows { get; }

    /// <summary>The colours the add-row's dropdown offers. <see cref="NewColor"/> defaults to the first,
    /// so the dropdown always shows a real selection.</summary>
    public IReadOnlyList<string> Palette => ColorPalette.Swatches;

    [RelayCommand]
    private void Add()
    {
        if (string.IsNullOrWhiteSpace(NewName))
            return;

        var category = _library.AddCategory(NewName, NewColor);
        Rows.Add(new CategoryRowViewModel(category));
        NewName = "";
    }

    /// <summary>Persist a row's edited name/colour. A blank name is refused (like the library layer
    /// itself requires) — revert the field to the persisted name instead of leaving it blank on screen
    /// while storage still has the old value (review finding 8).</summary>
    [RelayCommand]
    private void Save(CategoryRowViewModel? row)
    {
        if (row is null)
            return;

        if (string.IsNullOrWhiteSpace(row.Name))
        {
            row.Name = _library.Categories.FirstOrDefault(c => c.Id == row.Id)?.Name ?? row.Name;
            return;
        }

        _library.UpdateCategory(row.Id, row.Name, row.Color);
    }

    /// <summary>Delete a category (its phrases fall back to Uncategorized). The default is protected.</summary>
    [RelayCommand]
    private async Task Delete(CategoryRowViewModel? row)
    {
        if (row is null || row.IsDefault)
            return;

        if (!await _confirmDelete(row))
            return;

        if (_library.DeleteCategory(row.Id))
            Rows.Remove(row);
    }
}

/// <summary>One row in the category manager: an editable name and colour for a single category. The
/// "Uncategorized" default is flagged so the UI can hide its delete button and disable its name
/// field — <see cref="DisplayName"/> shows a localized label for it, but that label must never be
/// typeable back into <see cref="Name"/> (the stored value is a live constraint: other phrases
/// reference it by id, and Stage 3 of the localization plan requires it stay unchanged on disk).</summary>
public partial class CategoryRowViewModel(Category category) : ObservableObject
{
    public string Id { get; } = category.Id;
    public bool IsDefault { get; } = category.Id == Category.DefaultId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _name = category.Name;

    /// <summary>What the name field shows: the localized "Uncategorized" label for the default row
    /// (read-only there — the field is disabled in XAML), or the real editable name otherwise.</summary>
    public string DisplayName
    {
        get => IsDefault ? Strings.Category_Uncategorized : Name;
        set
        {
            if (!IsDefault)
                Name = value;
        }
    }

    [ObservableProperty]
    private string _color = category.Color;

    /// <summary>The colours this row's dropdown offers: the curated palette, plus the row's own current
    /// colour if it isn't one of them. Including the current value guarantees the bound
    /// <see cref="Color"/> always matches an item, so the ComboBox never coerces the selection to null
    /// and writes that null back — which would silently wipe a legacy (off-palette) colour.</summary>
    public IReadOnlyList<string> ColorOptions { get; } =
        ColorPalette.Swatches.Contains(category.Color)
            ? ColorPalette.Swatches
            : [category.Color, .. ColorPalette.Swatches];
}
