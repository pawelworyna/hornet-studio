using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using HornetStudio.Core.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HornetStudio.Tests.Logging;

public class FileLoggerTests : IDisposable
{
    private readonly string _testLogDir;

    public FileLoggerTests()
    {
        _testLogDir = Path.Combine(Path.GetTempPath(), $"hornet_logs_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testLogDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testLogDir))
        {
            try { Directory.Delete(_testLogDir, true); } catch { }
        }
    }

    [Fact]
    public void SensitiveDataSanitizer_RedactsPasswordsAndTokens()
    {
        var input1 = "Server=localhost;Database=mydb;User=sa;Password=SuperSecret123;Port=1433;";
        var sanitized1 = SensitiveDataSanitizer.Sanitize(input1);
        sanitized1.Should().NotContain("SuperSecret123");
        sanitized1.Should().Contain("Password=******");

        var input2 = "Connecting with pwd=MyDbPassword! and token=abc123xyz";
        var sanitized2 = SensitiveDataSanitizer.Sanitize(input2);
        sanitized2.Should().NotContain("MyDbPassword!");
        sanitized2.Should().NotContain("abc123xyz");

        var jsonInput = "{\"host\":\"10.0.0.1\",\"password\":\"plainPass123\",\"username\":\"root\"}";
        var sanitizedJson = SensitiveDataSanitizer.Sanitize(jsonInput);
        sanitizedJson.Should().NotContain("plainPass123");
        sanitizedJson.Should().Contain("\"password\":\"******\"");
    }

    [Fact]
    public async Task FileLogger_WritesFormattedEntriesToFile()
    {
        var options = new FileLoggerOptions
        {
            LogDirectory = _testLogDir,
            MinLogLevel = LogLevel.Debug,
            FilePrefix = "testlog_"
        };

        var provider = new HornetFileLoggerProvider(options);
        var logger = provider.CreateLogger("HornetStudio.Services.TestService");

        logger.LogInformation("Connecting to server with password=SuperSecret;host=localhost");
        logger.LogWarning("Warning condition occurred.");

        // Allow background channel writer to flush
        await provider.DisposeAsync();

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var logFile = Path.Combine(_testLogDir, $"testlog_{today}.log");

        File.Exists(logFile).Should().BeTrue();
        var content = await File.ReadAllTextAsync(logFile);

        content.Should().Contain("[INF] [TestService]");
        content.Should().Contain("password=******");
        content.Should().NotContain("SuperSecret");
        content.Should().Contain("[WRN] [TestService] Warning condition occurred.");
    }
}
