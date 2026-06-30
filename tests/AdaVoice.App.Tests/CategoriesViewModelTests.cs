using AdaVoice.App.ViewModels;
using AdaVoice.Core.Domain;

namespace AdaVoice.App.Tests;

public class CategoriesViewModelTests
{
    private static FakePlaybackHost HostWithDefault() =>
        new() { Categories = [new Category { Id = Category.DefaultId, Name = "Uncategorized", Color = "#808080" }] };

    [Fact]
    public void Rows_come_from_the_host()
    {
        var vm = new CategoriesViewModel(HostWithDefault());

        Assert.Equal("Uncategorized", Assert.Single(vm.Rows).Name);
    }

    [Fact]
    public void Add_creates_a_row_and_persists()
    {
        var host = HostWithDefault();
        var vm = new CategoriesViewModel(host) { NewName = "Greetings", NewColor = "#4F8EF7" };

        vm.AddCommand.Execute(null);

        Assert.Contains(vm.Rows, r => r.Name == "Greetings");
        Assert.Contains(host.Categories, c => c.Name == "Greetings"); // through the seam
        Assert.Equal("", vm.NewName);                                 // input cleared
    }

    [Fact]
    public void Add_ignores_a_blank_name()
    {
        var host = HostWithDefault();
        var vm = new CategoriesViewModel(host) { NewName = "   " };

        vm.AddCommand.Execute(null);

        Assert.Single(vm.Rows); // only the default
    }

    [Fact]
    public void Save_renames_through_the_seam()
    {
        var host = HostWithDefault();
        var vm = new CategoriesViewModel(host);
        vm.NewName = "Old";
        vm.AddCommand.Execute(null);
        var row = vm.Rows.First(r => r.Name == "Old");

        row.Name = "New";
        vm.SaveCommand.Execute(row);

        Assert.Contains(host.Categories, c => c.Name == "New");
    }

    [Fact]
    public void Delete_removes_a_category_but_protects_the_default()
    {
        var host = HostWithDefault();
        var vm = new CategoriesViewModel(host);
        vm.NewName = "Temp";
        vm.AddCommand.Execute(null);
        var temp = vm.Rows.First(r => r.Name == "Temp");
        var def = vm.Rows.First(r => r.IsDefault);

        vm.DeleteCommand.Execute(def);   // protected — no-op
        vm.DeleteCommand.Execute(temp);  // removed

        Assert.DoesNotContain(vm.Rows, r => r.Name == "Temp");
        Assert.Contains(vm.Rows, r => r.IsDefault);
        Assert.Contains(host.Categories, c => c.Id == Category.DefaultId);
    }
}
