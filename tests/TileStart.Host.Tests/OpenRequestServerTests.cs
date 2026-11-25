using System.IO;
using System.IO.Pipes;
using System.Windows.Threading;
using TileStart.Host;

namespace TileStart.Host.Tests;

[Collection("Host pipe")]
public sealed class OpenRequestServerTests
{
    [Fact]
    public async Task SecondaryInstanceWaitsForPrimaryPipeStartup()
    {
        var pipeName = $"TileStart.Host.Tests.{Guid.NewGuid():N}";
        var expected = new HostRequest(HostRequestKind.Open);
        var serverTask = Task.Run<HostRequest?>(async () =>
        {
            await Task.Delay(350);
            await using var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Message,
                PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync();
            await using var message = new MemoryStream();
            var buffer = new byte[256];
            do
            {
                var count = await pipe.ReadAsync(buffer);
                if (count == 0)
                {
                    break;
                }

                await message.WriteAsync(buffer.AsMemory(0, count));
            } while (!pipe.IsMessageComplete);

            return HostRequest.TryDecode(message.ToArray(), out var request) ? request : null;
        });

        var delivered = await Task.Run(() => SingleInstanceGuard.NotifyPrimaryInstance(
            expected,
            pipeName,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(25)));

        Assert.True(delivered);
        Assert.Equal(expected, await serverTask.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Theory]
    [InlineData("OPEN", 1)]
    [InlineData("EXIT", 1)]
    [InlineData("NOPE", 0)]
    public async Task CommandsAreAcknowledgedWithoutWaitingForUiWork(string command, byte expectedResponse)
    {
        HostRequest? queuedRequest = null;
        var pipeName = $"TileStart.Host.Tests.{Guid.NewGuid():N}";
        var started = new TaskCompletionSource<(Dispatcher Dispatcher, OpenRequestServer Server)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var unblock = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            _ = dispatcher.BeginInvoke(() =>
            {
                var server = new OpenRequestServer(request => queuedRequest = request, dispatcher, pipeName);
                server.Start();
                started.SetResult((dispatcher, server));
                unblock.Wait(TimeSpan.FromSeconds(5));
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var (dispatcher, server) = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(timeout.Token);
            await client.WriteAsync(System.Text.Encoding.ASCII.GetBytes(command), timeout.Token);
            await client.FlushAsync(timeout.Token);

            var response = new byte[1];
            Assert.Equal(1, await client.ReadAsync(response, timeout.Token));
            Assert.Equal(expectedResponse, response[0]);
            Assert.Null(queuedRequest);
        }
        finally
        {
            unblock.Set();
            try
            {
                await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShellOpenRequiresConfirmationBeforeItIsQueued(bool confirm)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var pipeName = $"TileStart.Host.Tests.{Guid.NewGuid():N}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var queued = 0;
        DispatcherHookEventHandler onPosted = (_, _) => Interlocked.Increment(ref queued);
        dispatcher.Hooks.OperationPosted += onPosted;
        var server = new OpenRequestServer(_ => { }, dispatcher, pipeName);
        server.Start();

        try
        {
            using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await client.ConnectAsync(timeout.Token);
                await client.WriteAsync("SHELL_OPEN"u8.ToArray(), timeout.Token);
                var response = new byte[1];
                Assert.Equal(1, await client.ReadAsync(response, timeout.Token));
                Assert.Equal(1, response[0]);
                Assert.Equal(0, Volatile.Read(ref queued));
                if (confirm)
                {
                    await client.WriteAsync(new byte[] { 1 }, timeout.Token);
                    Assert.Equal(0, await client.ReadAsync(response, timeout.Token));
                }
            }

            // The next connection is acknowledged after the previous client has been processed.
            using var next = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await next.ConnectAsync(timeout.Token);
            await next.WriteAsync("NOPE"u8.ToArray(), timeout.Token);
            var rejected = new byte[1];
            Assert.Equal(1, await next.ReadAsync(rejected, timeout.Token));
            Assert.Equal(0, rejected[0]);
            Assert.Equal(confirm ? 1 : 0, Volatile.Read(ref queued));
        }
        finally
        {
            await server.StopAsync();
            dispatcher.Hooks.OperationPosted -= onPosted;
        }
    }
}

[CollectionDefinition("Host pipe", DisableParallelization = true)]
public sealed class HostPipeCollection;
