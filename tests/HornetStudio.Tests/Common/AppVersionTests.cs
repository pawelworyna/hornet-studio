using System.Text.RegularExpressions;
using FluentAssertions;
using HornetStudio.Core.Common;

namespace HornetStudio.Tests.Common;

public class AppVersionTests
{
    [Fact]
    public void AppVersion_ProvidesValidSemVer()
    {
        AppVersion.SemVer.Should().NotBeNullOrWhiteSpace();
        AppVersion.SemVer.Should().MatchRegex(@"^\d+\.\d+\.\d+");
    }

    [Fact]
    public void AppVersion_ProvidesFormattedDisplayVersion()
    {
        AppVersion.DisplayVersion.Should().StartWith("v");
        AppVersion.DisplayVersion.Should().Be($"v{AppVersion.SemVer}");
    }

    [Fact]
    public void AppVersion_FullVersionIsPopulated()
    {
        AppVersion.FullVersion.Should().NotBeNullOrWhiteSpace();
        AppVersion.FullVersion.Should().Contain(AppVersion.SemVer);
    }
}
