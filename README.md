# DeltaZulu.Forward

DeltaZulu.Forward is a .NET implementation of the client (forwarder) and
collector (server) sides of **DeltaZulu.Forward**, DeltaZulu's proprietary
reliable log-forwarding transport. The naming follows the fluentd Forward
protocol convention: a product-scoped name for a product-scoped protocol.
DeltaZulu.Forward is **derived from RELP's design but is not wire-compatible
with it** — it replaces RELP's text command verbs and space-separated ASCII
header grammar with a fixed binary header, and adds a typed handshake,
first-class frame types, explicit backpressure signaling, and batch
deduplication that RELP does not have. Lineage is credited here in
documentation, not in naming or wire compatibility. See
[ADR-7 in the type-fidelity design record](#design-background) for the full
rationale.

## Why not RELP itself

A prior iteration of this library implemented literal RELP framing
(`txnr command datalen data\n`, `rsp` response frames, the `open`/`close`/
`syslog` command set). That design was superseded: RELP's text framing and
`librelp` wire compatibility exist to interoperate with the rsyslog
ecosystem, but DeltaZulu.Forward's payload is already Avro-encoded catalog
data with no syslog assumptions, so that interop guarantee buys nothing here
while still costing a weaker framing model (ASCII header parsing, no
integrity check, no typed handshake). Interop with rsyslog-world or fluentd
peers is a non-goal on this channel; raw ingestion from such sources is a
separate input adapter feeding the parser, not this transport.

## What DeltaZulu.Forward harvests from RELP, and what it changes

**Harvested:**

- Application-layer acknowledgements bound to durable commit — the core
  insight that TCP delivery is not processing.
- Per-frame transaction numbers (`TxNr`, wrapping over the header's `uint32`
  range).
- Negotiated windowing — multiple batches may be in flight at once, up to a
  credit window agreed during the handshake.
- An offer/capability handshake (`Hello` / `HelloAck`).
- Octet-counted, binary-safe framing (the payload length is a binary field
  the reader trusts, not a delimiter to scan for).
- Session-resumption semantics underpinning an at-least-once delivery
  contract.

**Dropped:** text command verbs, syslog payload assumptions, `librelp`
compatibility and its TLS layering, the space-separated header grammar.

**Replaced:** the ASCII header is now a fixed 16-byte binary header (frame
type, flags, protocol version, transaction number, payload length, CRC-32
payload checksum) — an explicit frame-integrity decision rather than an
inherited assumption. TLS, when enabled, is layered as a plain stream
transport beneath the framing, not woven into the protocol's own handshake.

**Added beyond RELP:**

- A typed handshake negotiating catalog version, known schema fingerprints,
  compression, dedup-window size, and in-flight window size
  (`ForwardHandshakeOffer` / `ForwardHandshakeAck`).
- First-class frame types: `TypedBatch`, `RawEnvelope`, `SchemaRequest` /
  `SchemaResponse`, `DeadLetterForward`, `Control`, in addition to the
  handshake and lifecycle frames.
- Explicit backpressure signaling via `Control` window-adjustment or
  throttle frames.
- A bounded, session-spanning dedup window keyed on a per-batch UUID, so
  at-least-once redelivery does not double-process a batch.

## Frame types

| Frame type | Direction | Purpose |
| --- | --- | --- |
| `Hello` / `HelloAck` | forwarder → collector / reply | Typed handshake: protocol version, catalog version, schema fingerprints, compression, window sizing, session resumption. |
| `TypedBatch` | forwarder → collector | One Avro-encoded typed batch, identified by a batch UUID. |
| `RawEnvelope` | forwarder → collector | One raw batch (bytes plus source metadata) for a source parsed at the collector tier. |
| `SchemaRequest` / `SchemaResponse` | either direction | Resolves schema bytes for a fingerprint the receiver hasn't seen. |
| `DeadLetterForward` | either direction | Forwards a batch that failed parsing or validation, with its original bytes and an error reason. |
| `Ack` | collector → forwarder | Acknowledgement bound to durable commit of the batch named by the frame header's transaction number. |
| `Control` | either direction | Window-adjustment or throttle (backpressure) signaling. |
| `Close` / `CloseAck` | either direction / reply | Orderly session shutdown. |

One Avro batch per frame: a batch is never split across frames, and a frame
never carries more than one independently committable batch.

## Projects

| Project | Purpose |
| --- | --- |
| `src/DeltaZulu.Forward/DeltaZulu.Forward.csproj` | Core DeltaZulu.Forward library: binary framing, handshake, session (forwarder and collector roles), credit window, dedup window, and a Microsoft.Extensions.Logging sink. |
| `src/DeltaZulu.Forward.Tests/DeltaZulu.Forward.Tests.csproj` | MSTest coverage for framing, handshake, windowed sends, backpressure, dedup, and session lifecycle. |
| `examples/DeltaZulu.Forward.Examples.Server/DeltaZulu.Forward.Examples.Server.csproj` | Minimal collector that accepts zstd-compressed NDJSON payloads as `TypedBatch` frames. |
| `examples/DeltaZulu.Forward.Examples.Client/DeltaZulu.Forward.Examples.Client.csproj` | Minimal forwarder that sends zstd-compressed NDJSON payloads as `TypedBatch` frames. |

The solution file is `DeltaZulu.Forward.slnx` at the repository root and
includes the library, tests, and example projects.

## Requirements

- .NET SDK that supports `net10.0`. The package targets `net10.0` while the
  project is experimental; consider multi-targeting a long-term support
  framework before operational adoption.
- Network access to restore NuGet packages for tests and examples.

The example projects use
[`ZstdSharp.Port`](https://www.nuget.org/packages/ZstdSharp.Port/) for
payload compression. The core `DeltaZulu.Forward` library does not depend
on zstd.

## Build and test

From the repository root:

```bash
dotnet restore DeltaZulu.Forward.slnx
dotnet build DeltaZulu.Forward.slnx
dotnet test DeltaZulu.Forward.slnx --no-build
```

The same restore/build/test sequence runs in GitHub Actions for pushes to
the default branches and for pull requests.

## Basic library usage (forwarder role)

```csharp
using DeltaZulu.Forward;

await using var connection = new ForwardConnection("127.0.0.1", 1601);
await connection.ConnectAsync();

var session = new ForwardSession(connection, new ForwardSessionOptions {
    CatalogVersion = "1",
    RequestedWindowSize = 64,
    DedupWindowSize = 4096
});
await session.OpenAsync();
await session.SendTypedBatchAsync(avroEncodedBatchBytes);
await session.CloseAsync();
```

`SendTypedBatchAsync` and `SendRawEnvelopeAsync` return once the batch's
`Ack` frame reports durable commit; up to `RequestedWindowSize` batches may
be in flight at once, and a `Control` frame from the peer can grow, shrink,
or pause that window mid-session. The lower-level `ForwardFrameReader` can
parse a `ReadOnlySequence<byte>` directly, and `ForwardConnection` uses a
`PipeReader`/`PipeWriter` pair so buffering is owned by the transport layer.

## Collector (server) role

`ForwardConnection.FromAcceptedClient` wraps an already-accepted
`TcpClient`, and `ForwardSession.AcceptAsync` performs the inbound
handshake and starts the same background receive pump used by the
forwarder role:

```csharp
await using var connection = ForwardConnection.FromAcceptedClient(acceptedClient);
var session = await ForwardSession.AcceptAsync(
    connection,
    offer => new ForwardHandshakeAck(
        Accepted: true,
        ProtocolVersion: offer.ProtocolVersion,
        SessionId: Guid.NewGuid(),
        GrantedWindowSize: offer.RequestedWindowSize,
        DedupWindowSize: offer.DedupWindowSize,
        CompressionSelected: offer.CompressionOffered,
        UnknownSchemaFingerprints: [],
        RejectReason: string.Empty),
    new ForwardSessionOptions {
        BatchHandler = (frameType, batchId, payload, ct) => {
            // Decode and durably commit the batch, then...
            return Task.FromResult(new ForwardAckOutcome(0, null));
        }
    });
```

Inbound batches are deduplicated by UUID against a bounded,
session-spanning window before `BatchHandler` runs, so at-least-once
redelivery after a lost acknowledgement does not double-process a batch.

## Microsoft.Extensions.Logging sink

The core package can also be registered as a `Microsoft.Extensions.Logging`
provider. Configure the DeltaZulu.Forward endpoint when building your
DI-backed logging pipeline:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DeltaZulu.Forward;

var services = new ServiceCollection();
services.AddLogging(builder => {
    builder.AddForward(options => {
        options.Host = "127.0.0.1";
        options.Port = 1601;
        options.MinimumLevel = LogLevel.Information;
        options.IncludeScopes = true;
    });
});
```

`ForwardLoggerProvider` queues log events on a bounded background channel,
opens a DeltaZulu.Forward session on first use, sends each accepted log
record as a `RawEnvelope` batch (log entries are not catalog-typed values,
so they travel as raw envelopes rather than typed batches), and closes the
session when the provider is disposed. Override
`ForwardLoggerOptions.Formatter` when your receiver expects a specific
payload format.

## Examples

The examples demonstrate zstd-compressed newline-delimited JSON
(NDJSON/JSON Lines) sent as `TypedBatch` frames. Frame payload length is a
binary field the reader trusts (checked against a configurable maximum and
verified with a CRC-32 checksum), not a delimiter to scan for.

Start the server:

```bash
dotnet run --project examples/DeltaZulu.Forward.Examples.Server -- 1601
```

In another terminal, run the client:

```bash
dotnet run --project examples/DeltaZulu.Forward.Examples.Client -- 127.0.0.1 1601 5
```

The client arguments are:

1. server host, default `127.0.0.1`
2. server port, default `1601`
3. batch count, default `5.000.000`

The server binds to loopback by default for local demonstration. Pass a
second argument of `any` to bind to all interfaces:

```bash
dotnet run --project examples/DeltaZulu.Forward.Examples.Server -- 1601 any
```

## Design background

This library implements the transport decisions recorded in the DeltaZulu
type-fidelity design record's ADR-7 ("Transport: DeltaZulu.Forward, a
RELP-derived proprietary protocol"): parse-once type fidelity is enforced
upstream by the catalog and parser; this library's job is to move
already-typed (or explicitly raw) batches between agents without
reintroducing a second, independent reconstruction of their types, and
without silently dropping batches when a session drops.

## Notes for contributors

- Keep optional payload encodings, such as zstd, out of the core library
  unless they become explicit library features.
- Keep examples buildable with the solution so API changes are caught
  early.
- Prefer adding tests in `src/DeltaZulu.Forward.Tests` for protocol
  behavior changes.
- Do not add a degraded fallback wire format (for example, plain NDJSON)
  for outage scenarios: a fallback is a second contract every consumer
  would have to implement forever, and it resurrects exactly the type
  reconstruction divergence this protocol exists to avoid. Handle outages
  with spooling and replay at the caller's transport-adapter layer instead.
