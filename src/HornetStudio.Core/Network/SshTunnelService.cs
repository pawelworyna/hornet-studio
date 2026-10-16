using System.Net;
using System.Net.Sockets;
using Renci.SshNet;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Network;

public interface ISshTunnelService : IAsyncDisposable
{
    bool IsConnected { get; }
    uint BoundPort { get; }
    Task<uint> StartTunnelAsync(SshTunnelConfig config, string targetHost, int targetPort, CancellationToken ct = default);
    Task StopTunnelAsync();
}

public class SshTunnelService : ISshTunnelService
{
    private SshClient? _sshClient;
    private SshClient? _bastionClient;
    private ForwardedPortLocal? _forwardedPort;

    public bool IsConnected => _sshClient?.IsConnected == true;
    public uint BoundPort => _forwardedPort?.Port ?? 0;

    public async Task<uint> StartTunnelAsync(
        SshTunnelConfig config,
        string targetHost,
        int targetPort,
        CancellationToken ct = default)
    {
        if (!config.Enabled)
            throw new InvalidOperationException("Tunel SSH nie jest włączony w konfiguracji.");

        await StopTunnelAsync();

        var localPort = (uint)GetAvailablePort();

        // 1. Uwierzytelnianie SSH
        var authMethods = new List<AuthenticationMethod>();
        if (config.Mode == SshMode.Password && !string.IsNullOrEmpty(config.Password))
        {
            authMethods.Add(new PasswordAuthenticationMethod(config.Username, config.Password));
        }
        else if (config.Mode == SshMode.KeyFile && !string.IsNullOrEmpty(config.PrivateKeyPath) && File.Exists(config.PrivateKeyPath))
        {
            var keyFile = string.IsNullOrEmpty(config.Passphrase)
                ? new PrivateKeyFile(config.PrivateKeyPath)
                : new PrivateKeyFile(config.PrivateKeyPath, config.Passphrase);

            authMethods.Add(new PrivateKeyAuthenticationMethod(config.Username, keyFile));
        }
        else if (!string.IsNullOrEmpty(config.Password))
        {
            authMethods.Add(new PasswordAuthenticationMethod(config.Username, config.Password));
        }

        var connInfo = new ConnectionInfo(config.Host, config.Port, config.Username, authMethods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        // 2. Obsługa Bastion / Jump Host
        if (config.UseBastion && !string.IsNullOrEmpty(config.BastionHost))
        {
            var bastionAuth = new List<AuthenticationMethod>();
            if (!string.IsNullOrEmpty(config.BastionPassword))
            {
                bastionAuth.Add(new PasswordAuthenticationMethod(config.BastionUsername ?? config.Username, config.BastionPassword));
            }
            if (!string.IsNullOrEmpty(config.BastionKeyPath) && File.Exists(config.BastionKeyPath))
            {
                bastionAuth.Add(new PrivateKeyAuthenticationMethod(config.BastionUsername ?? config.Username, new PrivateKeyFile(config.BastionKeyPath)));
            }

            var bastionConnInfo = new ConnectionInfo(
                config.BastionHost,
                config.BastionPort,
                config.BastionUsername ?? config.Username,
                bastionAuth.ToArray());

            _bastionClient = new SshClient(bastionConnInfo);
            await _bastionClient.ConnectAsync(ct);
        }

        _sshClient = new SshClient(connInfo);
        await _sshClient.ConnectAsync(ct);

        // 3. Lokalne przekierowanie portu
        _forwardedPort = new ForwardedPortLocal("127.0.0.1", localPort, targetHost, (uint)targetPort);
        _sshClient.AddForwardedPort(_forwardedPort);
        _forwardedPort.Start();

        return localPort;
    }

    public Task StopTunnelAsync()
    {
        if (_forwardedPort != null)
        {
            if (_forwardedPort.IsStarted) _forwardedPort.Stop();
            _forwardedPort.Dispose();
            _forwardedPort = null;
        }

        if (_sshClient != null)
        {
            if (_sshClient.IsConnected) _sshClient.Disconnect();
            _sshClient.Dispose();
            _sshClient = null;
        }

        if (_bastionClient != null)
        {
            if (_bastionClient.IsConnected) _bastionClient.Disconnect();
            _bastionClient.Dispose();
            _bastionClient = null;
        }

        return Task.CompletedTask;
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await StopTunnelAsync();
        GC.SuppressFinalize(this);
    }
}
