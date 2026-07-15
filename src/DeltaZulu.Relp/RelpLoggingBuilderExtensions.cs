using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeltaZulu.Relp;

/// <summary>Registers RELP as a Microsoft.Extensions.Logging provider.</summary>
public static class RelpLoggingBuilderExtensions
{
    /// <summary>Adds a RELP logging sink to the logging builder.</summary>
    public static ILoggingBuilder AddRelp(this ILoggingBuilder builder, Action<RelpLoggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddSingleton<ILoggerProvider>(_ => {
            var options = new RelpLoggerOptions();
            configure(options);
            return new RelpLoggerProvider(options);
        });
        return builder;
    }
}
