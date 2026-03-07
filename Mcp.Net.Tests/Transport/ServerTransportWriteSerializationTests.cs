using FluentAssertions;
using Mcp.Net.Core.Interfaces;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Server.Transport.Sse;
using Mcp.Net.Server.Transport.Stdio;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Net.Tests.Transport;

public class ServerTransportWriteSerializationTests
{
    [Fact]
    public async Task SseTransport_SendAsync_ShouldSerializeConcurrentOutboundWrites()
    {
        var writer = new BlockingResponseWriter();
        var transport = new SseTransport(writer, NullLogger<SseTransport>.Instance);

        var responseTask = transport.SendAsync(
            new JsonRpcResponseMessage("2.0", "1", new { ok = true }, null)
        );
        await writer.WaitForFirstWriteAsync();

        var notificationTask = transport.SendNotificationAsync(
            new JsonRpcNotificationMessage("2.0", "notifications/tools/list_changed", null)
        );

        (await writer.ConcurrentWriteObservedWithinAsync(TimeSpan.FromMilliseconds(250))).Should().BeFalse();

        writer.ReleaseWrites();

        await Task.WhenAll(responseTask, notificationTask);
        await transport.CloseAsync();
    }

    [Fact]
    public async Task StdioTransport_SendAsync_ShouldSerializeConcurrentOutboundWrites()
    {
        var transport = new BlockingStdioTransport();

        var responseTask = transport.SendAsync(
            new JsonRpcResponseMessage("2.0", "1", new { ok = true }, null)
        );
        await transport.WaitForFirstWriteAsync();

        var notificationTask = transport.SendNotificationAsync(
            new JsonRpcNotificationMessage("2.0", "notifications/tools/list_changed", null)
        );

        (await transport.ConcurrentWriteObservedWithinAsync(TimeSpan.FromMilliseconds(250))).Should().BeFalse();

        transport.ReleaseWrites();

        await Task.WhenAll(responseTask, notificationTask);
        await transport.CloseAsync();
    }

    private sealed class BlockingResponseWriter : IResponseWriter
    {
        private readonly TaskCompletionSource _firstWriteEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _concurrentWriteObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseWrites = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _writeCount;
        private int _activeWrites;

        public bool IsCompleted { get; private set; }

        public string Id { get; } = Guid.NewGuid().ToString();

        public string? RemoteIpAddress => null;

        public Task WriteAsync(string content, CancellationToken cancellationToken = default)
        {
            var writeNumber = Interlocked.Increment(ref _writeCount);
            if (writeNumber == 1)
            {
                _firstWriteEntered.TrySetResult();
            }

            if (Interlocked.Increment(ref _activeWrites) > 1)
            {
                _concurrentWriteObserved.TrySetResult();
            }

            return WaitForReleaseAsync(cancellationToken);
        }

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void SetHeader(string name, string value) { }

        public IEnumerable<KeyValuePair<string, string>> GetRequestHeaders() =>
            Array.Empty<KeyValuePair<string, string>>();

        public Task CompleteAsync()
        {
            IsCompleted = true;
            return Task.CompletedTask;
        }

        public Task WaitForFirstWriteAsync() => _firstWriteEntered.Task;

        public void ReleaseWrites() => _releaseWrites.TrySetResult();

        public async Task<bool> ConcurrentWriteObservedWithinAsync(TimeSpan timeout)
        {
            var completed = await Task.WhenAny(_concurrentWriteObserved.Task, Task.Delay(timeout));
            return completed == _concurrentWriteObserved.Task;
        }

        private async Task WaitForReleaseAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _releaseWrites.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _activeWrites);
            }
        }
    }

    private sealed class BlockingStdioTransport : StdioTransport
    {
        private readonly TaskCompletionSource _firstWriteEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _concurrentWriteObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseWrites = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _writeCount;
        private int _activeWrites;

        public BlockingStdioTransport()
            : base(
                "test-stdio-serialization",
                Stream.Null,
                new MemoryStream(),
                NullLogger<StdioTransport>.Instance
            ) { }

        public Task WaitForFirstWriteAsync() => _firstWriteEntered.Task;

        public void ReleaseWrites() => _releaseWrites.TrySetResult();

        public async Task<bool> ConcurrentWriteObservedWithinAsync(TimeSpan timeout)
        {
            var completed = await Task.WhenAny(_concurrentWriteObserved.Task, Task.Delay(timeout));
            return completed == _concurrentWriteObserved.Task;
        }

        protected override async Task WriteRawAsync(byte[] data)
        {
            var writeNumber = Interlocked.Increment(ref _writeCount);
            if (writeNumber == 1)
            {
                _firstWriteEntered.TrySetResult();
            }

            if (Interlocked.Increment(ref _activeWrites) > 1)
            {
                _concurrentWriteObserved.TrySetResult();
            }

            try
            {
                await _releaseWrites.Task;
            }
            finally
            {
                Interlocked.Decrement(ref _activeWrites);
            }
        }
    }
}
