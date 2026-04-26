using AwesomeAssertions;
using FluentRegeditApp.Services;
using Xunit;

namespace FluentRegeditApp.Tests;

public class NavigationHistoryTests
{
    [Fact]
    public void Empty_history_has_no_current_and_cannot_move()
    {
        var h = new NavigationHistory<string>();
        h.Current.Should().BeNull();
        h.CanGoBack.Should().BeFalse();
        h.CanGoForward.Should().BeFalse();
        h.Back().Should().BeNull();
        h.Forward().Should().BeNull();
    }

    [Fact]
    public void Visit_sets_current_and_disables_forward()
    {
        var h = new NavigationHistory<string>();
        h.Visit("a");
        h.Current.Should().Be("a");
        h.CanGoBack.Should().BeFalse();
        h.CanGoForward.Should().BeFalse();
    }

    [Fact]
    public void Back_and_forward_traverse_history()
    {
        var h = new NavigationHistory<string>();
        h.Visit("a");
        h.Visit("b");
        h.Visit("c");

        h.Current.Should().Be("c");
        h.CanGoBack.Should().BeTrue();
        h.CanGoForward.Should().BeFalse();

        h.Back().Should().Be("b");
        h.Back().Should().Be("a");
        h.CanGoBack.Should().BeFalse();
        h.Back().Should().BeNull();

        h.Forward().Should().Be("b");
        h.Forward().Should().Be("c");
        h.CanGoForward.Should().BeFalse();
        h.Forward().Should().BeNull();
    }

    [Fact]
    public void New_visit_truncates_forward_history()
    {
        var h = new NavigationHistory<string>();
        h.Visit("a");
        h.Visit("b");
        h.Visit("c");
        h.Back();
        h.Back();
        h.Visit("d");

        h.Current.Should().Be("d");
        h.CanGoForward.Should().BeFalse();
        h.Back().Should().Be("a");
        h.Forward().Should().Be("d");
    }
}
