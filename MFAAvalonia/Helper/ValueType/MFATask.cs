using CommunityToolkit.Mvvm.ComponentModel;
using MaaFramework.Binding;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.ViewModels.Pages;
using MFAAvalonia.Views.Windows;
using MFAAvalonia.Helper;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.Helper.ValueType;

public partial class MFATask : ObservableObject
{
    public enum MFATaskType
    {
        MFA,
        MAAFW
    }

    public enum MFATaskStatus
    {
        NOT_STARTED,
        STOPPING,
        STOPPED,
        SUCCEEDED,
        FAILED
    }

    [ObservableProperty] private string? _name = string.Empty;
    [ObservableProperty] private MFATaskType _type = MFATaskType.MFA;
    [ObservableProperty] private int _count = 1;
    [ObservableProperty] private Func<Task> _action;
    public Func<Task<MaaJobStatus>>? MaaAction { get; set; }
    // [ObservableProperty] private Dictionary<string, MaaNode> _tasks = new();
    [ObservableProperty] private bool _isUpdateRelated;

    public TaskQueueViewModel? OwnerViewModel { get; set; }
    public DragItemViewModel? SourceItem { get; set; }
    public long RunId { get; set; }
    public bool ContinueOnError { get; set; }

    public async Task<MFATaskStatus> Run(CancellationToken token)
    {
        var instanceId = OwnerViewModel?.Processor.InstanceId;
        if (instanceId != null)
            TelemetryService.StartTask(instanceId, this);

        MFATaskStatus Complete(MFATaskStatus status)
        {
            if (instanceId != null)
                TelemetryService.FinishTask(instanceId, this, status);
            return status;
        }

        try
        {
            if (Count < 0)
                Count = int.MaxValue;
            OwnerViewModel?.MarkTaskRunning(SourceItem, RunId);
            var hasFailed = false;
            string? failureMessage = null;
            for (int i = 0; i < Count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (Type == MFATaskType.MAAFW)
                {
                    OwnerViewModel?.AddLogByKey(LangKeys.TaskStart, (Avalonia.Media.IBrush?)null, true, true, LanguageHelper.GetLocalizedString(Name));
                    OwnerViewModel?.SetCurrentTaskName(LanguageHelper.GetLocalizedString(Name));
                }
                if (MaaAction != null)
                {
                    var jobStatus = await MaaAction();
                    if (jobStatus != MaaJobStatus.Succeeded)
                    {
                        hasFailed = true;
                        failureMessage = jobStatus.ToString();
                        if (!ContinueOnError)
                        {
                            OwnerViewModel?.MarkTaskFailed(SourceItem, RunId, failureMessage);
                            return Complete(MFATaskStatus.FAILED);
                        }
                    }
                }
                else
                {
                    await Action();
                }
                OwnerViewModel?.MarkTaskIterationCompleted(SourceItem, RunId);
            }
            if (hasFailed)
                OwnerViewModel?.MarkTaskFailed(SourceItem, RunId, failureMessage);
            else
                OwnerViewModel?.MarkTaskSucceeded(SourceItem, RunId);
            return Complete(MFATaskStatus.SUCCEEDED);
        }
        catch (MaaJobStatusException ex)
        {
            OwnerViewModel?.MarkTaskFailed(SourceItem, RunId, ex.Message);
            LoggerHelper.Error($"任务执行失败：{LanguageHelper.GetLocalizedString(Name)}");
            return Complete(MFATaskStatus.FAILED);
        }
        catch (OperationCanceledException)
        {
            OwnerViewModel?.MarkTaskStopped(SourceItem, RunId);
            return Complete(MFATaskStatus.STOPPED);
        }
        catch (Exception ex)
        {
            OwnerViewModel?.MarkTaskFailed(SourceItem, RunId, ex.Message);
            LoggerHelper.Error($"任务执行异常：任务={LanguageHelper.GetLocalizedString(Name)}，原因={ex.Message}", ex);
            return Complete(MFATaskStatus.FAILED);
        }
    }
}
