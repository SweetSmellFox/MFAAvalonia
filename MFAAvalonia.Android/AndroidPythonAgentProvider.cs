using Android.App;
using Android.Content;
using Android.OS;
using MaaFramework.Binding;
using MFAAvalonia.Extensions.MaaFW;
using System;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace MFAAvalonia.Android;

internal sealed class AndroidPythonAgentProvider
{
    private const string ServiceClassMetadataKey = "MFA.Android.PythonServiceClass";
    private const string EntryPointMetadataKey = "MFA.Android.PythonAgentEntryPoint";
    private const string OutputLineExtra = "line";

    private readonly Activity _activity;
    private readonly Lock _sessionLock = new();
    private readonly string? _serviceClass;
    private readonly string? _entryPoint;
    private bool _sessionActive;

    public AndroidPythonAgentProvider(Activity activity)
    {
        _activity = activity;
        foreach (var attribute in typeof(AndroidPythonAgentProvider).Assembly
                     .GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(attribute.Key, ServiceClassMetadataKey, StringComparison.Ordinal))
                _serviceClass = attribute.Value;
            else if (string.Equals(attribute.Key, EntryPointMetadataKey, StringComparison.Ordinal))
                _entryPoint = attribute.Value;
        }
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_serviceClass);

    public bool LinkStart(MaaAgentClient client)
    {
        var result = NativeBridgeInterop.SafeAgentClientConnect(client.Handle);
        if (result < 0)
        {
            global::Android.Util.Log.Warn(
                "MFAAgent",
                $"MaaAgentClientConnect was interrupted or threw across its native ABI; result={result}. Retrying.");
        }
        return result > 0;
    }

    public IPlatformAgentSession? Start(PlatformAgentStartRequest request)
    {
        if (!IsAvailable)
            return null;

        lock (_sessionLock)
        {
            if (_sessionActive)
                throw new InvalidOperationException(
                    "MFA Android supports one python-for-android Agent service at a time.");
            _sessionActive = true;
        }

        try
        {
            var outputAction = $"{_activity.PackageName}.PYTHON_AGENT_OUTPUT";
            var outputReceiver = new AgentOutputReceiver(request.Output);
            var outputFilter = new IntentFilter(outputAction);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                _activity.RegisterReceiver(outputReceiver, outputFilter, ReceiverFlags.NotExported);
            else
                _activity.RegisterReceiver(outputReceiver, outputFilter);

            var serviceArgument = JsonSerializer.Serialize(new
            {
                client_id = request.Identifier,
                child_exec = request.Program,
                child_args = request.Arguments,
                data_root = request.DataRoot,
                entrypoint = _entryPoint,
                instance_id = request.InstanceId,
                instance_name = request.InstanceName,
                native_library_dir = _activity.ApplicationInfo?.NativeLibraryDir ?? string.Empty,
                output_action = outputAction,
                output_package = _activity.PackageName ?? string.Empty,
            });

            var serviceClass = _serviceClass!;
            try
            {
                AndroidPythonServiceInterop.Prepare(_activity, serviceClass);
                AndroidPythonServiceInterop.Start(_activity, serviceClass, serviceArgument);
                return new Session(this, serviceClass, outputReceiver);
            }
            catch
            {
                _activity.UnregisterReceiver(outputReceiver);
                outputReceiver.Dispose();
                throw;
            }
        }
        catch
        {
            ReleaseSession();
            throw;
        }
    }

    private void ReleaseSession()
    {
        lock (_sessionLock)
            _sessionActive = false;
    }

    private sealed class Session : IPlatformAgentSession
    {
        private readonly AndroidPythonAgentProvider _owner;
        private readonly string _serviceClass;
        private readonly AgentOutputReceiver _outputReceiver;
        private bool _disposed;

        public Session(
            AndroidPythonAgentProvider owner,
            string serviceClass,
            AgentOutputReceiver outputReceiver)
        {
            _owner = owner;
            _serviceClass = serviceClass;
            _outputReceiver = outputReceiver;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                AndroidPythonServiceInterop.Stop(_owner._activity, _serviceClass);
            }
            finally
            {
                try
                {
                    _owner._activity.UnregisterReceiver(_outputReceiver);
                }
                catch (ArgumentException)
                {
                    // The Activity may already have torn down its receiver table.
                }
                _outputReceiver.Dispose();
                _owner.ReleaseSession();
            }
        }
    }

    private sealed class AgentOutputReceiver(Action<string> output) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            var line = intent?.GetStringExtra(OutputLineExtra);
            if (!string.IsNullOrEmpty(line))
                output(line);
        }
    }
}
