# DeltaZulu.Forward zstd NDJSON examples

This folder contains a minimal DeltaZulu.Forward client/server pair for sending newline-delimited JSON (NDJSON/JSON Lines) payloads compressed with zstd, as `TypedBatch` frames.

The examples use [`ZstdSharp.Port`](https://www.nuget.org/packages/ZstdSharp.Port/) for zstd compression/decompression. The client uses the public `ForwardConnection` and `ForwardSession` APIs so the example stays close to normal library usage. The server demonstrates the collector/server role with `ForwardConnection.FromAcceptedClient` and `ForwardSession.AcceptAsync`.

## Run the server

```bash
dotnet run --project examples/DeltaZulu.Forward.Examples.Server -- 1601
```

The server listens on loopback by default. To bind all network interfaces for a non-local demo, pass `any` as the second argument:

```bash
dotnet run --project examples/DeltaZulu.Forward.Examples.Server -- 1601 any
```

The server accepts the DeltaZulu.Forward handshake, accepts `TypedBatch` frames, decompresses each with zstd, and prints each JSON line to stdout as:

```text
<ingestion timestamp> [TypedBatch <batch id>]: <log>
```

Press Ctrl+C to stop the server gracefully.

## Run the client

```bash
dotnet run --project examples/DeltaZulu.Forward.Examples.Client -- 127.0.0.1 1601 5
```

Arguments are:

1. server host (default: `127.0.0.1`)
2. server port (default: `1601`)
3. batch count (default: `5`)

The client generates basic NDJSON log objects, compresses each line with zstd, and sends each as a `TypedBatch` frame, waiting for its durable-commit acknowledgement before sending the next.
