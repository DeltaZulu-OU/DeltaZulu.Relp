namespace DeltaZulu.Relp;

/// <summary>RELP protocol command names.</summary>
public enum RelpCommand
{
    /// <summary>Opens a RELP session.</summary>
    Open,

    /// <summary>Closes a RELP session.</summary>
    Close,

    /// <summary>Aborts a RELP session.</summary>
    Abort,

    /// <summary>Indicates that the server is closing the RELP session.</summary>
    ServerClose,

    /// <summary>Transports a syslog message.</summary>
    Syslog,

    /// <summary>Contains a RELP response.</summary>
    Response
}

/// <summary>Provides protocol string conversions for <see cref="RelpCommand" /> values.</summary>
public static class RelpCommandExtensions
{
    /// <summary>Converts a RELP command to its wire protocol name.</summary>
    /// <param name="command">The command to convert.</param>
    /// <returns>The RELP wire protocol command name.</returns>
    public static string ToProtocolString(this RelpCommand command) => command switch {
        RelpCommand.Open => "open",
        RelpCommand.Close => "close",
        RelpCommand.Abort => "abort",
        RelpCommand.ServerClose => "serverclose",
        RelpCommand.Syslog => "syslog",
        RelpCommand.Response => "rsp",
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
    };

    /// <summary>Attempts to parse a RELP wire protocol command name from UTF-8 bytes.</summary>
    /// <param name="value">The protocol command name to parse.</param>
    /// <param name="command">When this method returns, contains the parsed command if parsing succeeded.</param>
    /// <returns><see langword="true" /> if <paramref name="value" /> was parsed; otherwise, <see langword="false" />.</returns>
    public static bool TryParseProtocolSpan(ReadOnlySpan<byte> value, out RelpCommand command)
    {
        command = value switch {
            [(byte)'o', (byte)'p', (byte)'e', (byte)'n'] => RelpCommand.Open,
            [(byte)'c', (byte)'l', (byte)'o', (byte)'s', (byte)'e'] => RelpCommand.Close,
            [(byte)'a', (byte)'b', (byte)'o', (byte)'r', (byte)'t'] => RelpCommand.Abort,
            [(byte)'s', (byte)'e', (byte)'r', (byte)'v', (byte)'e', (byte)'r', (byte)'c', (byte)'l', (byte)'o', (byte)'s', (byte)'e'] => RelpCommand.ServerClose,
            [(byte)'s', (byte)'y', (byte)'s', (byte)'l', (byte)'o', (byte)'g'] => RelpCommand.Syslog,
            [(byte)'r', (byte)'s', (byte)'p'] => RelpCommand.Response,
            _ => default
        };
        return value is
            [(byte)'o', (byte)'p', (byte)'e', (byte)'n'] or
            [(byte)'c', (byte)'l', (byte)'o', (byte)'s', (byte)'e'] or
            [(byte)'a', (byte)'b', (byte)'o', (byte)'r', (byte)'t'] or
            [(byte)'s', (byte)'e', (byte)'r', (byte)'v', (byte)'e', (byte)'r', (byte)'c', (byte)'l', (byte)'o', (byte)'s', (byte)'e'] or
            [(byte)'s', (byte)'y', (byte)'s', (byte)'l', (byte)'o', (byte)'g'] or
            [(byte)'r', (byte)'s', (byte)'p'];
    }

    /// <summary>Attempts to parse a RELP wire protocol command name.</summary>
    /// <param name="value">The protocol command name to parse.</param>
    /// <param name="command">When this method returns, contains the parsed command if parsing succeeded.</param>
    /// <returns><see langword="true" /> if <paramref name="value" /> was parsed; otherwise, <see langword="false" />.</returns>
    public static bool TryParseProtocolString(string value, out RelpCommand command)
    {
        command = value switch {
            "open" => RelpCommand.Open,
            "close" => RelpCommand.Close,
            "abort" => RelpCommand.Abort,
            "serverclose" => RelpCommand.ServerClose,
            "syslog" => RelpCommand.Syslog,
            "rsp" => RelpCommand.Response,
            _ => default
        };
        return value is "open" or "close" or "abort" or "serverclose" or "syslog" or "rsp";
    }
}
