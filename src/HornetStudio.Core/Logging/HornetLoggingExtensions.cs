using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace HornetStudio.Core.Logging;

public static class HornetLoggingExtensions
{
    public static ILoggingBuilder AddHornetFileLogger(
        this ILoggingBuilder builder,
        Action<FileLoggerOptions>? configure = null)
    {
        var options = new FileLoggerOptions();
        configure?.Invoke(options);

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider>(new HornetFileLoggerProvider(options)));

        return builder;
    }
}
