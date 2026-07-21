namespace DeltaZulu.Forward;

/// <summary>
/// The <see cref="ForwardFrameType.Hello" /> payload: a typed handshake offer negotiating
/// catalog version, known schema fingerprints, compression, dedup-window size, and window
/// sizing. This is added beyond RELP's plain offer/capability handshake (which only
/// negotiated a protocol version and command set).
/// </summary>
public sealed record ForwardHandshakeOffer(
    ushort ProtocolVersion,
    Guid SessionResumeToken,
    string CatalogVersion,
    uint RequestedWindowSize,
    uint DedupWindowSize,
    ForwardCompression CompressionOffered,
    IReadOnlyList<ulong> KnownSchemaFingerprints)
{
    /// <summary>Encodes the offer to its wire representation.</summary>
    public byte[] Encode()
    {
        var writer = new ForwardPayloadWriter();
        writer.WriteUInt16(ProtocolVersion);
        writer.WriteGuid(SessionResumeToken);
        writer.WriteString(CatalogVersion);
        writer.WriteUInt32(RequestedWindowSize);
        writer.WriteUInt32(DedupWindowSize);
        writer.WriteByte((byte)CompressionOffered);
        writer.WriteUInt16((ushort)KnownSchemaFingerprints.Count);
        foreach (var fingerprint in KnownSchemaFingerprints)
        {
            writer.WriteUInt64(fingerprint);
        }

        return writer.ToArray();
    }

    /// <summary>Decodes an offer from its wire representation.</summary>
    public static ForwardHandshakeOffer Decode(ReadOnlySpan<byte> payload)
    {
        var reader = new ForwardPayloadReader(payload);
        var protocolVersion = reader.ReadUInt16();
        var sessionResumeToken = reader.ReadGuid();
        var catalogVersion = reader.ReadString();
        var requestedWindowSize = reader.ReadUInt32();
        var dedupWindowSize = reader.ReadUInt32();
        var compressionOffered = (ForwardCompression)reader.ReadByte();
        var fingerprintCount = reader.ReadUInt16();
        var fingerprints = new ulong[fingerprintCount];
        for (var i = 0; i < fingerprintCount; i++)
        {
            fingerprints[i] = reader.ReadUInt64();
        }

        return new ForwardHandshakeOffer(
            protocolVersion,
            sessionResumeToken,
            catalogVersion,
            requestedWindowSize,
            dedupWindowSize,
            compressionOffered,
            fingerprints);
    }
}

/// <summary>
/// The <see cref="ForwardFrameType.HelloAck" /> payload: the server's negotiated response,
/// including any schema fingerprints it does not recognize (each followed up with a
/// <see cref="ForwardFrameType.SchemaRequest" />) and a reject reason when
/// <see cref="Accepted" /> is <see langword="false" />.
/// </summary>
public sealed record ForwardHandshakeAck(
    bool Accepted,
    ushort ProtocolVersion,
    Guid SessionId,
    uint GrantedWindowSize,
    uint DedupWindowSize,
    ForwardCompression CompressionSelected,
    IReadOnlyList<ulong> UnknownSchemaFingerprints,
    string RejectReason)
{
    /// <summary>Encodes the handshake acknowledgement to its wire representation.</summary>
    public byte[] Encode()
    {
        var writer = new ForwardPayloadWriter();
        writer.WriteBool(Accepted);
        writer.WriteUInt16(ProtocolVersion);
        writer.WriteGuid(SessionId);
        writer.WriteUInt32(GrantedWindowSize);
        writer.WriteUInt32(DedupWindowSize);
        writer.WriteByte((byte)CompressionSelected);
        writer.WriteUInt16((ushort)UnknownSchemaFingerprints.Count);
        foreach (var fingerprint in UnknownSchemaFingerprints)
        {
            writer.WriteUInt64(fingerprint);
        }

        writer.WriteString(RejectReason);
        return writer.ToArray();
    }

    /// <summary>Decodes a handshake acknowledgement from its wire representation.</summary>
    public static ForwardHandshakeAck Decode(ReadOnlySpan<byte> payload)
    {
        var reader = new ForwardPayloadReader(payload);
        var accepted = reader.ReadBool();
        var protocolVersion = reader.ReadUInt16();
        var sessionId = reader.ReadGuid();
        var grantedWindowSize = reader.ReadUInt32();
        var dedupWindowSize = reader.ReadUInt32();
        var compressionSelected = (ForwardCompression)reader.ReadByte();
        var fingerprintCount = reader.ReadUInt16();
        var fingerprints = new ulong[fingerprintCount];
        for (var i = 0; i < fingerprintCount; i++)
        {
            fingerprints[i] = reader.ReadUInt64();
        }

        var rejectReason = reader.ReadString();

        return new ForwardHandshakeAck(
            accepted,
            protocolVersion,
            sessionId,
            grantedWindowSize,
            dedupWindowSize,
            compressionSelected,
            fingerprints,
            rejectReason);
    }
}
