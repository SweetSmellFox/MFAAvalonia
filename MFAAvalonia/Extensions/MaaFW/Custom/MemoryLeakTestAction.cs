using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using MFAAvalonia.Views.Windows;
using System;
using System.Linq;
using System.Threading;
using MFAAvalonia.ViewModels.Pages;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 内存泄漏测试 Action - 高频点击和高频截图，用于检测内存泄漏
/// 使用方法：在 pipeline 中添加一个任务，action 设置为 "MemoryLeakTestAction"
/// </summary>
public class MemoryLeakTestAction : IMaaCustomAction
{
    public string Name { get; set; } = "MemoryLeakTestAction";
    // 测试配置
    private const long DefaultIterations = 1000; // 默认迭代次数
    private const int ActionInterval = 100; // 操作间隔（毫秒）- 0.2秒
    private const int MemoryLogInterval = 50; // 每多少次迭代记录一次内存

    private readonly Random _random = new();

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            return Execute(context, args);
        }
        catch (OperationCanceledException)
        {
            LoggerHelper.Info("[内存测试]测试被取消");
            return false;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"[内存测试]测试异常: {ex.Message}");
            return false;
        }
    }

    private bool Execute(IMaaContext context, RunArgs args)
    {
        // 解析迭代次数（可以通过 pipeline 参数传入）
        var iterations = DefaultIterations;
        if (!string.IsNullOrEmpty(args.ActionParam))
        {
            if (long.TryParse(args.ActionParam, out var customIterations))
            {
                iterations = customIterations;
            }
        }

        var vm = GetViewModel(context);
        vm?.AddLog($"[内存测试]开始测试，迭代次数: {iterations}", "Orange");

        var startMemory = GC.GetTotalMemory(false);
        var startTime = DateTime.Now;

        LoggerHelper.Info($"[内存测试]初始内存: {startMemory / (1024 * 1024)} MB");
        vm?.AddLog($"[内存测试]初始内存: {startMemory / (1024 * 1024)} MB", "Cyan");

        for (int i = 0; i < iterations; i++)
        {
            // 检查是否被取消
            // if (MaaProcessor.Instance.CancellationTokenSource?.IsCancellationRequested == true)
            // {
            //     throw new OperationCanceledException();
            // }

            // 高频截图测试
            TestScreenshot(context, i);
            Thread.Sleep(ActionInterval);

            // 高频点击测试
            TestClick(context, i);
            Thread.Sleep(ActionInterval);

            // 定期记录内存使用情况
            if ((i + 1) % MemoryLogInterval == 0)
            {
                var currentMemory = GC.GetTotalMemory(false);
                var memoryGrowth = currentMemory - startMemory;
                var elapsed = DateTime.Now - startTime;

                var message = $"[内存测试]迭代 {i + 1}/{iterations}, " + $"当前内存: {currentMemory / (1024 * 1024)} MB, " + $"增长: {memoryGrowth / (1024 * 1024)} MB, " + $"耗时: {elapsed.TotalSeconds:F1}s";

                LoggerHelper.Info(message);
                vm?.AddLog(message, memoryGrowth > 100 * 1024 * 1024 ? "OrangeRed" : "LightGreen");
            }
        }

        // 测试完成，输出统计信息
        var endMemory = GC.GetTotalMemory(false);
        var totalGrowth = endMemory - startMemory;
        var totalTime = DateTime.Now - startTime;

        vm?.AddLog($"[内存测试]测试完成!", "Gold");
        vm?.AddLog($"[内存测试]总迭代: {iterations}", "Cyan");
        vm?.AddLog($"[内存测试]初始内存: {startMemory / (1024 * 1024)} MB", "Cyan");
        vm?.AddLog($"[内存测试]结束内存: {endMemory / (1024 * 1024)} MB", "Cyan");
        vm?.AddLog($"[内存测试]内存增长: {totalGrowth / (1024 * 1024)} MB", totalGrowth > 50 * 1024 * 1024 ? "OrangeRed" : "LightGreen");
        vm?.AddLog($"[内存测试]总耗时: {totalTime.TotalSeconds:F1}s", "Cyan");
        vm?.AddLog($"[内存测试]平均每次迭代内存增长: {(double)totalGrowth / iterations / 1024:F2} KB", "Cyan");

        // 强制 GC 后再次检查
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);

        var afterGcMemory = GC.GetTotalMemory(true);
        var leakedMemory = afterGcMemory - startMemory;

        vm?.AddLog($"[内存测试]GC后内存: {afterGcMemory / (1024 * 1024)} MB", "Cyan");
        vm?.AddLog($"[内存测试]疑似泄漏: {leakedMemory / (1024 * 1024)} MB",
            leakedMemory > 20 * 1024 * 1024 ? "OrangeRed" : "LightGreen");

        return true;
    }

    private TaskQueueViewModel? GetViewModel(IMaaContext context) => MaaProcessor.Processors.FirstOrDefault(p => p.MaaTasker == context.Tasker)?.ViewModel;

    /// <summary>
    /// 高频截图测试
    /// </summary>
    private void TestScreenshot(IMaaContext context, int iteration)
    {
        // 使用 using 确保 MaaImageBuffer 被正确释放
        using var imageBuffer = new MaaImageBuffer();

        // 截图 - 使用 GetCachedImage 而不是 GetImage，避免创建额外的 buffer
        context.Screencap();
        if (!context.GetCachedImage(imageBuffer))
        {

            LoggerHelper.Warning($"[内存测试]高频截图 #{iteration}:获取图像失败");
            return;
        }

        // 模拟一些图像处理操作
        var width = imageBuffer.Width;
        var height = imageBuffer.Height;


        var vm = GetViewModel(context);
        vm?.AddLog($"[内存测试]高频截图 #{iteration}: {width}x{height}", (Avalonia.Media.IBrush?)null);

        // imageBuffer 会在 using 块结束时自动释放
    }

    /// <summary>
    /// 高频点击测试
    /// </summary>
    private void TestClick(IMaaContext context, int iteration)
    {
        // 生成随机点击坐标
        var x = 200;
        var y = 500;

        // 执行点击
        context.Click(x, y);


        var vm = GetViewModel(context);
        vm?.AddLog($"[内存测试]高频点击 #{iteration}: ({x},{y})");

    }
}
