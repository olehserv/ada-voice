using AdaVoice.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>One checkable row in the Categories filter menu. <see cref="IsChecked"/> is observed by
/// <see cref="BoardViewModel"/> to re-run the filter and enforce mutual exclusivity with the
/// Conversation filter — see BoardViewModel.OnCategoryFilterItemChanged.</summary>
public sealed partial class CategoryFilterItemViewModel : ObservableObject
{
    public Category Category { get; }

    [ObservableProperty]
    private bool _isChecked;

    public CategoryFilterItemViewModel(Category category) => Category = category;
}
