using System.IO;
using System.IO.Pipes;
using System.Windows.Threading;

namespace TileStart.Host.Shell;

public sealed class OpenRequestServer
{
    private const string DefaultPipeName = "TileStart.Host";
    private readonly Action<HostRequest> _handleRequest;
    private readonly Dispatcher _dispatcher;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listenTask;

    public OpenRequestServer(Action<HostRequest> handleRequest, Dispatcher dispatcher)
        : this(handleRequest, dispatcher, DefaultPipeName)
    {
    }

    internal OpenRequestServer(Action<HostRequest> handleRequest, Dispatcher dispatcher, string pipeName)
    {
        _handleRequest = handleRequest;
        _dispatcher = dispatcher;
        _pipeName = pipeName;
    }

    public void Start()
    {
        _listenTask = ListenAsync(_cancellation.Token);
    }

    public async Task StopAsync()
    {
        _cancellation.Cancel();
        if (_listenTask is not null)
        {
            try
            {
                await _listenTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cancellation.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(_pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Message,
                PipeOptions.Asynchronous);
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var message = new MemoryStream();
                var buffer = new byte[4096];
                do
                {
                    var count = await pipe.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (count == 0)
                    {
                        break;
                    }

                    message.Write(buffer, 0, count);
                } while (!pipe.IsMessageComplete);

                var command = message.GetBuffer().AsMemory(0, (int)message.Length);
                if (command.Span.SequenceEqual("SHELL_OPEN"u8))
                {
                    // The shell may have let the original input through by the time it sees our reply.
                    // Only open after it confirms that it will suppress the original input.
                    await pipe.WriteAsync(new byte[] { 1 }, cancellationToken).ConfigureAwait(false);
                    var count = await pipe.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (count == 1 && buffer[0] == 1)
                    {
                        QueueRequest(new HostRequest(HostRequestKind.Open));
                    }

                    continue;
                }

                var accepted = HostRequest.TryDecode(command.Span, out var request);
                if (accepted)
                {
                    QueueRequest(request);
                }

                await pipe.WriteAsync(new[] { accepted ? (byte)1 : (byte)0 }, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
            }
        }
    }

    private void QueueRequest(HostRequest request) =>
        _dispatcher.BeginInvoke(() => _handleRequest(request));
}
