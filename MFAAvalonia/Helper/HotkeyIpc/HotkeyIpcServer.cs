using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.Helper.HotkeyIpc;

/// <summary>
/// 热键 IPC 服务端 - 主进程使用 (基于 TCP Socket)
/// </summary>
public class HotkeyIpcServer : IDisposable
{
    private const int DefaultPort = 52718;
    private const int PortRange = 10;
    private const string PortFileName = "hotkey_ipc_port.txt";

    private TcpListener? _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, ClientConnection> _clients = new();
    private readonly ConcurrentDictionary<HotkeyIdentifier, HashSet<string>> _hotkeySubscriptions = new();
    private readonly object _subscriptionLock = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _isRunning;
    private bool _disposed;
    private int _actualPort;

    public event Action<HotkeyMessage>? MessageReceived;
    public event Action<string>? ClientConnected;
    public event Action<string>? ClientDisconnected;

    public bool IsRunning => _isRunning;
    public int ClientCount => _clients.Count;
    public int Port => _actualPort;

    public Task StartAsync()
    {
        if (_isRunning) return Task.CompletedTask;

        for (int i = 0; i < PortRange; i++)
        {
            int port = DefaultPort + i;
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
                _actualPort = port;
                _isRunning = true;
                SavePortToFile(port);
                LoggerHelper.Info($"HotkeyIpcServer: 启动 TCP 服务端，端口 {port}");
                break;
            }
            catch (SocketException)
            {
                _listener?.Stop();
                _listener = null;
            }
        }

        if (!_isRunning)
        {
            LoggerHelper.Error("HotkeyIpcServer: 无法找到可用端口");
            return Task.CompletedTask;
        }
        _ = Task.Run(AcceptClientsAsync);
        return Task.CompletedTask;
    }

    private void SavePortToFile(int port)
    {
        try
        {
            var portPath = GetPortFilePath();
            File.WriteAllText(portPath, port.ToString());LoggerHelper.Info($"HotkeyIpcServer: 端口文件已保存到 {portPath}");
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"HotkeyIpcServer: 保存端口文件失败 - {ex.Message}");
        }
    }

    public static int? LoadPortFromFile()
    {
        try
        {
            var portPath = GetPortFilePath();
            if (File.Exists(portPath))
            {
                var content = File.ReadAllText(portPath).Trim();
                if (int.TryParse(content, out int port))
                    return port;
            }
        }
        catch { }
        return null;
    }

    private static string GetPortFilePath()
    {
        // 使用用户临时目录，确保所有进程都能访问同一个文件
        var tempDir = Path.Combine(Path.GetTempPath(), "MFAAvalonia");
        Directory.CreateDirectory(tempDir);
        var portPath = Path.Combine(tempDir, PortFileName);
        return portPath;
    }

    private async Task AcceptClientsAsync()
    {
        while (!_cts.Token.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = HandleClientAsync(client);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                LoggerHelper.Warning($"HotkeyIpcServer: 接受连接失败 - {ex.Message}");
                await Task.Delay(100);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient)
    {
        string? clientId = null;
        NetworkStream? stream = null;
        StreamReader? reader = null;
        StreamWriter? writer = null;

        try
        {
            tcpClient.NoDelay = true;
            stream = tcpClient.GetStream();
            reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true)
            {
                AutoFlush = true
            };

            while (tcpClient.Connected && !_cts.Token.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(_cts.Token);
                }
                catch (OperationCanceledException) { break; }
                catch (IOException) { break; }

                if (string.IsNullOrEmpty(line)) break;

                LoggerHelper.Debug($"HotkeyIpcServer: 收到消息 - {line}");
                var msg = HotkeyMessage.Deserialize(line);
                if (msg == null) continue;

                switch (msg.Type)
                {
                    case HotkeyMessageType.Connect when msg.ClientId != null:
                        clientId = msg.ClientId;
                        _clients[clientId] = new ClientConnection(tcpClient, writer);
                        var ack = HotkeyMessage.Create(HotkeyMessageType.ConnectAck);
                        await WriteLineAsync(writer, ack.SerializeToJson());
                        ClientConnected?.Invoke(clientId);
                        LoggerHelper.Info($"HotkeyIpcServer: 客户端连接 - {clientId}");
                        break;

                    case HotkeyMessageType.RegisterHotkey when msg.Hotkey != null && clientId != null:
                        RegisterHotkey(clientId, msg.Hotkey);
                        break;

                    case HotkeyMessageType.UnregisterHotkey when msg.Hotkey != null && clientId != null:
                        UnregisterHotkey(clientId, msg.Hotkey);
                        break;

                    case HotkeyMessageType.Heartbeat when clientId != null:
                        var heartbeatAck = HotkeyMessage.Create(HotkeyMessageType.HeartbeatAck);
                        await WriteLineAsync(writer, heartbeatAck.SerializeToJson());
                        break;

                    case HotkeyMessageType.Disconnect:
                        return;
                }

                MessageReceived?.Invoke(msg);
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Debug($"HotkeyIpcServer: 客户端处理异常 - {ex.Message}");
        }
        finally
        {
            if (clientId != null)
            {
                RemoveClient(clientId);
                ClientDisconnected?.Invoke(clientId);
            }
            reader?.Dispose();
            writer?.Dispose();
            stream?.Dispose();
            tcpClient.Dispose();
        }
    }

    private async Task WriteLineAsync(StreamWriter writer, string message)
    {
        await _writeLock.WaitAsync();
        try
        {
            await writer.WriteLineAsync(message);
            await writer.FlushAsync(); // 确保消息被立即发送
        }
        finally { _writeLock.Release(); }
    }

    private void RegisterHotkey(string clientId, HotkeyIdentifier hotkey)
    {
        lock (_subscriptionLock)
        {
            if (!_hotkeySubscriptions.TryGetValue(hotkey, out var clients))
            {
                clients = new HashSet<string>();
                _hotkeySubscriptions[hotkey] = clients;
            }
            clients.Add(clientId);
        }
        LoggerHelper.Info($"HotkeyIpcServer: 注册热键 {hotkey} <- {clientId}");
    }

    private void UnregisterHotkey(string clientId, HotkeyIdentifier hotkey)
    {
        lock (_subscriptionLock)
        {
            if (_hotkeySubscriptions.TryGetValue(hotkey, out var clients))
            {
                clients.Remove(clientId);
                if (clients.Count == 0)
                    _hotkeySubscriptions.TryRemove(hotkey, out _);
            }
        }
    }

    private void RemoveClient(string clientId)
    {
        if (_clients.TryRemove(clientId, out var conn))
        {
            try { conn.TcpClient.Close(); }
            catch { }
        }

        lock (_subscriptionLock)
        {
            foreach (var kvp in _hotkeySubscriptions)
                kvp.Value.Remove(clientId);

            var emptyKeys = _hotkeySubscriptions.Where(k => k.Value.Count == 0).Select(k => k.Key).ToList();
            foreach (var key in emptyKeys)
                _hotkeySubscriptions.TryRemove(key, out _);
        }
    }

    public async Task BroadcastHotkeyTriggeredAsync(HotkeyIdentifier hotkey)
    {
        List<string> targetClients;
        lock (_subscriptionLock)
        {
            if (!_hotkeySubscriptions.TryGetValue(hotkey, out var clients)) return;
            targetClients = new List<string>(clients);
        }

        LoggerHelper.Info($"HotkeyIpcServer: 广播热键 {hotkey} 给 {targetClients.Count} 个客户端");
        var msg = HotkeyMessage.CreateTriggered(hotkey.KeyCode, hotkey.Modifiers);
        var json = msg.SerializeToJson();
        var failedClients = new List<string>();

        foreach (var clientId in targetClients)
        {
            if (_clients.TryGetValue(clientId, out var conn))
            {
                try { await WriteLineAsync(conn.Writer, json); }
                catch { failedClients.Add(clientId); }
            }
        }

        foreach (var clientId in failedClients)
            RemoveClient(clientId);
    }

    public bool HasSubscription(HotkeyIdentifier hotkey)
    {
        lock (_subscriptionLock)
        {
            return _hotkeySubscriptions.TryGetValue(hotkey, out var clients) && clients.Count > 0;
        }
    }

    public IEnumerable<HotkeyIdentifier> GetAllRegisteredHotkeys()
    {
        lock (_subscriptionLock)
        {
            return new List<HotkeyIdentifier>(_hotkeySubscriptions.Keys);
        }
    }

    public void Stop()
    {
        _cts.Cancel();
        _isRunning = false;
        try { _listener?.Stop(); }
        catch { }

        foreach (var conn in _clients.Values)
        {
            try { conn.TcpClient.Close(); }
            catch { }
        }
        _clients.Clear();

        try
        {
            var portPath = GetPortFilePath();
            if (File.Exists(portPath)) File.Delete(portPath);
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts.Dispose();
        _writeLock.Dispose();
    }

    private class ClientConnection
    {
        public TcpClient TcpClient { get; }
        public StreamWriter Writer { get; }
        public ClientConnection(TcpClient tcpClient, StreamWriter writer)
        {
            TcpClient = tcpClient;
            Writer = writer;
        }
    }
}
