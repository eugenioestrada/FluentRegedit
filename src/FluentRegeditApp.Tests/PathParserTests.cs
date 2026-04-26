using AwesomeAssertions;
using FluentRegeditApp.Models;
using FluentRegeditApp.Services;
using Xunit;

namespace FluentRegeditApp.Tests;

public class PathParserTests
{
    [Theory]
    [InlineData("HKCU", RegistryRoot.CurrentUser, "")]
    [InlineData("HKEY_CURRENT_USER", RegistryRoot.CurrentUser, "")]
    [InlineData("HKCR", RegistryRoot.ClassesRoot, "")]
    [InlineData("HKEY_CLASSES_ROOT", RegistryRoot.ClassesRoot, "")]
    [InlineData("HKLM", RegistryRoot.LocalMachine, "")]
    [InlineData("HKEY_LOCAL_MACHINE", RegistryRoot.LocalMachine, "")]
    [InlineData("HKU", RegistryRoot.Users, "")]
    [InlineData("HKEY_USERS", RegistryRoot.Users, "")]
    [InlineData("HKCC", RegistryRoot.CurrentConfig, "")]
    [InlineData("HKEY_CURRENT_CONFIG", RegistryRoot.CurrentConfig, "")]
    public void Parses_short_and_long_root_names(string input, RegistryRoot expectedRoot, string expectedSub)
    {
        PathParser.TryParse(input, out var root, out var sub).Should().BeTrue();
        root.Should().Be(expectedRoot);
        sub.Should().Be(expectedSub);
    }

    [Theory]
    [InlineData(@"HKCU\Software\Test", RegistryRoot.CurrentUser, @"Software\Test")]
    [InlineData(@"hkcu\Software\Test", RegistryRoot.CurrentUser, @"Software\Test")]
    [InlineData(@"HkCu\Software\Test", RegistryRoot.CurrentUser, @"Software\Test")]
    [InlineData(@"HKEY_CURRENT_USER\Software\Test\", RegistryRoot.CurrentUser, @"Software\Test")]
    [InlineData(@"HKCU/Software/Test", RegistryRoot.CurrentUser, @"Software\Test")]
    [InlineData(@"  HKCU\Software\Test  ", RegistryRoot.CurrentUser, @"Software\Test")]
    [InlineData("\"HKCU\\Software\\Test\"", RegistryRoot.CurrentUser, @"Software\Test")]
    public void Parses_paths_with_subkeys_and_normalises_separators(string input, RegistryRoot expectedRoot, string expectedSub)
    {
        PathParser.TryParse(input, out var root, out var sub).Should().BeTrue();
        root.Should().Be(expectedRoot);
        sub.Should().Be(expectedSub);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NOT_A_HIVE")]
    [InlineData(@"BOGUS\Software")]
    public void Rejects_invalid_input(string? input)
    {
        PathParser.TryParse(input, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Combine_produces_full_path()
    {
        PathParser.Combine(RegistryRoot.CurrentUser, "Software\\Test")
            .Should().Be(@"HKEY_CURRENT_USER\Software\Test");
        PathParser.Combine(RegistryRoot.CurrentUser, "")
            .Should().Be("HKEY_CURRENT_USER");
    }
}
