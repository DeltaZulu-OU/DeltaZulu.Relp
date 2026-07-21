using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeltaZulu.Forward;

/// <summary>Registers DeltaZulu.Forward as a Microsoft.Extensions.Logging provider.</summary>
public static class ForwardLoggingBuilderExtensions
{
    /// <summary>Adds a DeltaZulu.Forward logging sink to the logging builder.</summary>
    public static ILoggingBuilder AddForward(this ILoggingBuilder builder, Action<ForwardLoggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddSingleton<ILoggerProvider>(_ => {
            var options = new ForwardLoggerOptions();
            configure(options);
            return new ForwardLoggerProvider(options);
        });
        return builder;
    }
}
