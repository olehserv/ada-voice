using AdaVoice.App.ViewModels;
using AdaVoice.Core.Domain;

namespace AdaVoice.App.Tests;

public class RepairPhraseViewModelTests
{
    [Fact]
    public void Exposes_the_entrys_title()
    {
        var vm = new RepairPhraseViewModel(new PhraseEntry { Id = "p-1", Title = "Hi" });

        Assert.Equal("Hi", vm.Title);
    }

    [Fact]
    public void Starts_with_no_choice_made()
    {
        var vm = new RepairPhraseViewModel(new PhraseEntry { Id = "p-1" });

        Assert.Null(vm.Choice);
    }

    [Fact]
    public void Choose_re_record_records_the_choice()
    {
        var vm = new RepairPhraseViewModel(new PhraseEntry { Id = "p-1" });

        vm.ChooseReRecord();

        Assert.Equal(RepairChoice.ReRecord, vm.Choice);
    }

    [Fact]
    public void Choose_remove_records_the_choice()
    {
        var vm = new RepairPhraseViewModel(new PhraseEntry { Id = "p-1" });

        vm.ChooseRemove();

        Assert.Equal(RepairChoice.Remove, vm.Choice);
    }
}
