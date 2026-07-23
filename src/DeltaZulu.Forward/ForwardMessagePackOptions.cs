using MessagePack;
using MessagePack.Resolvers;

namespace DeltaZulu.Forward;

/// <summary>
/// Provides the <see cref="MessagePackSerializerOptions" /> used to encode and decode
/// <see cref="ForwardLogBatch" /> values: <see cref="ForwardObjectFormatter" /> handles
/// every <see cref="object" />-typed field value, and <see cref="ContractlessStandardResolver" />
/// reflects over the plain <see cref="ForwardLogRecord" />/<see cref="ForwardLogBatch" />
/// POCOs (which carry no MessagePack attributes by design, per <c>FWD-CONTRACT-v1</c>).
/// </summary>
internal static class ForwardMessagePackOptions
{
    /// <summary>Gets the shared serializer options for the <see cref="ForwardLogBatchCodec" />.</summary>
    public static MessagePackSerializerOptions Instance { get; } = BuildOptions();

    private static MessagePackSerializerOptions BuildOptions()
    {
        var resolver = CompositeResolver.Create(
            [ForwardObjectFormatter.Instance],
            [ContractlessStandardResolver.Instance]);

        return MessagePackSerializerOptions.Standard
            .WithResolver(resolver)
            .WithSecurity(MessagePackSecurity.UntrustedData);
    }
}
