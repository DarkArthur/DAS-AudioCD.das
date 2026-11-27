using DAS.Design;
using DiscCheck.App.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiscCheck.App.Tests;

public class AboutViewModelTests
{
    [Fact]
    public void Shows_app_and_design_versions()
    {
        var vm = new AboutViewModel("1.2.3", null);

        vm.AppVersionText.Should().Contain("1.2.3");
        vm.DesignVersionText.Should().Contain(DasDesignInfo.Version);
    }

    [Fact]
    public void Attribution_is_hidden_when_null_and_shown_when_set()
    {
        new AboutViewModel("1.0.0", null).HasAttribution.Should().BeFalse();
        new AboutViewModel("1.0.0", "  ").HasAttribution.Should().BeFalse();

        var vm = new AboutViewModel("1.0.0", "Logo e icone CC-BY 4.0");
        vm.HasAttribution.Should().BeTrue();
        vm.Attribution.Should().Be("Logo e icone CC-BY 4.0");
    }

    [Fact]
    public void Default_attribution_states_cc_by_4_0_with_link()
    {
        var vm = new AboutViewModel();

        vm.HasAttribution.Should().BeTrue();
        vm.Attribution.Should().Contain("CC-BY 4.0").And.Contain("https://creativecommons.org/licenses/by/4.0/");
    }
}

