using Microsoft.VisualStudio.TestTools.UnitTesting;

// Many tests in this assembly drive real loopback sockets and background session receive
// pumps with timing-sensitive assertions (windowed sends, backpressure, dedup). Running them
// concurrently with each other multiplies thread-pool and socket-scheduling contention well
// beyond what any single test exercises in isolation, which manifests as multi-second stalls
// that have nothing to do with the protocol logic under test. Keep this assembly sequential.
[assembly: DoNotParallelize]

namespace DeltaZulu.Forward.Tests;

/// <summary>
/// Raises the thread pool's minimum thread count once, before any test runs, so a burst of
/// blocked awaits at the start of the run does not pay the default ramp-up penalty.
/// </summary>
[TestClass]
public static class TestAssemblySetup
{
    [AssemblyInitialize]
    public static void Initialize(TestContext context) =>
        ThreadPool.SetMinThreads(Math.Max(32, Environment.ProcessorCount * 8), Math.Max(32, Environment.ProcessorCount * 8));
}
