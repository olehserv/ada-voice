using System.Collections.ObjectModel;
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

    [ObservableProperty]
    private string _newName = "";

    [ObservableProperty]
    private string _newColor = "#808080";

    public CategoriesViewModel(ILibraryHost library)
    {
        _library = library;
        Rows = new ObservableCollection<CategoryRowViewModel>(library.Categories.Select(c => new CategoryRowViewModel(c)));
    }

    /// <summary>One editable row per category.</summary>
    public ObservableCollection<CategoryRowViewModel> Rows { get; }

    [RelayCommand]
    private void Add()
    {
        if (string.IsNullOrWhiteSpace(NewName))
            return;

        var category = _library.AddCategory(NewName, NewColor);
        Rows.Add(new CategoryRowViewModel(category));
        NewName = "";
    }

    /// <summary>Persist a row's edited name/colour.</summary>
    [RelayCommand]
    private void Save(CategoryRowViewModel? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.Name))
            return;

        _library.UpdateCategory(row.Id, row.Name, row.Color);
    }

    /// <summary>Delete a category (its phrases fall back to Uncategorized). The default is protected.</summary>
    [RelayCommand]
    private void Delete(CategoryRowViewModel? row)
    {
        if (row is null || row.IsDefault)
            return;

        if (_library.DeleteCategory(row.Id))
            Rows.Remove(row);
    }
}

/// <summary>One row in the category manager: an editable name and colour for a single category. The
/// "Uncategorized" default is flagged so the UI can hide its delete button.</summary>
public partial class CategoryRowViewModel(Category category) : ObservableObject
{
    public string Id { get; } = category.Id;
    public bool IsDefault { get; } = category.Id == Category.DefaultId;

    [ObservableProperty]
    private string _name = category.Name;

    [ObservableProperty]
    private string _color = category.Color;
}
