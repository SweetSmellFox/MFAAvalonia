﻿using Avalonia.Platform.Storage;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFAAvalonia.Helper;

public static class FileLogExporter
{
    public const int MAX_LINES = 42000;

    public async static Task CompressRecentLogs(IStorageProvider storageProvider)
    {
        if (Instances.RootViewModel.IsRunning)
        {
            ToastHelper.Warn(
                "Warning".ToLocalization(),
                "StopTaskBeforeExportLog".ToLocalization());
            return;
        }
        MaaProcessor.Instance.SetTasker();

        if (storageProvider == null)
            throw new ArgumentNullException(nameof(storageProvider));

        try
        {
            // 获取用户选择的保存路径
            var saveFile = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "ExportLog".ToLocalization(),
                DefaultExtension = "zip",
                SuggestedFileName = $"log_{DateTime.Now:yyyyMMdd_HHmmss}"
            });

            if (saveFile == null)
                return; // 用户取消了操作

            // 获取应用程序基目录
            string baseDirectory = AppContext.BaseDirectory;

            // 获取符合条件的日志文件
            var logFiles = GetEligibleLogFiles(baseDirectory);

            if (!logFiles.Any())
            {
                LoggerHelper.Warning("未找到符合条件的日志文件。");
                return;
            }

            // 创建临时目录用于压缩
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // 处理每个日志文件
                foreach (var file in logFiles)
                {
                    var destDir = Path.Combine(tempDir, file.RelativePath ?? string.Empty);
                    Directory.CreateDirectory(destDir);
                    var destPath = Path.Combine(destDir, Path.GetFileName(file.FullName ?? string.Empty));

                    // 如果文件行数超过限制，提取最近的MAX_LINES行
                    if (file.LineCount > MAX_LINES)
                    {
                        ExtractLastLines(file.FullName, destPath, MAX_LINES);
                    }
                    else
                    {
                        // 行数未超过限制，直接复制
                        File.Copy(file.FullName ?? string.Empty, destPath);
                    }
                }

                await using (var stream = await saveFile.OpenWriteAsync())
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                    {
                        var entryName = Path.GetFileName(file);
                        archive.CreateEntryFromFile(file, entryName);
                    }
                }

                LoggerHelper.Info($"日志文件已成功压缩到：\n{saveFile.Name}");
            }
            catch (Exception ex)
            {
                LoggerHelper.Error($"压缩过程中发生错误：\n{ex}");
            }
            finally
            {
                // 清理临时目录
                try { Directory.Delete(tempDir, true); }
                catch
                {
                    /* 忽略清理错误 */
                }
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"发生错误：\n{ex}");
        }
    }

    // 获取符合条件的日志文件
    private static List<LogFileInfo> GetEligibleLogFiles(string baseDirectory)
    {
        var eligibleFiles = new List<LogFileInfo>();

        var logFiles = Directory.Exists(Path.Combine(baseDirectory, "debug")) ? Directory.GetFiles(Path.Combine(baseDirectory, "debug"), "*.log", SearchOption.AllDirectories) : Array.Empty<string>();
        var txtFiles = Directory.Exists(Path.Combine(baseDirectory, "logs")) ? Directory.GetFiles(Path.Combine(baseDirectory, "logs"), "*.txt", SearchOption.AllDirectories) : Array.Empty<string>();

        // 计算两天前的日期
        var twoDaysAgo = DateTime.Now.AddDays(-2);

        var allFiles = logFiles.Concat(txtFiles).ToArray();

        foreach (var file in allFiles)
        {
            try
            {
                var fileInfo = new FileInfo(file);

                // 检查文件修改日期
                if (fileInfo.LastWriteTime < twoDaysAgo)
                {
                    continue;
                }

                // 计算文件行数
                var lineCount = CountLines(file);

                // 计算相对路径
                var relativePath = (Path.GetDirectoryName(file) ?? string.Empty)
                    .Replace(baseDirectory, "")
                    .TrimStart(Path.DirectorySeparatorChar);

                eligibleFiles.Add(new LogFileInfo
                {
                    FullName = file,
                    RelativePath = relativePath,
                    LineCount = lineCount
                });
            }
            catch (Exception ex)
            {
                LoggerHelper.Error($"处理文件 {file} 时出错: {ex}");
                // 继续处理其他文件
            }
        }

        return eligibleFiles;
    }

    // 计算文件行数
    private static int CountLines(string filePath)
    {
        try
        {
            // 使用更底层的FileStream并设置FileShare.ReadWrite，允许其他进程同时读写
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            using var reader = new StreamReader(stream);
            int count = 0;

            // 逐行读取但限制最大行数，避免超大文件导致内存溢出
            while (reader.ReadLine() != null && count <= MAX_LINES + 1)
            {
                count++;
            }

            return count;
        }
        catch (FileNotFoundException)
        {
            LoggerHelper.Warning($"文件不存在: {filePath}");
            return int.MaxValue;
        }
        catch (UnauthorizedAccessException)
        {
            LoggerHelper.Warning($"无权访问文件: {filePath}");
            return int.MaxValue;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"读取文件失败: {filePath}", ex);
            return int.MaxValue;
        }
    }

    // 从文件末尾提取指定行数
    private static void ExtractLastLines(string sourcePath, string destPath, int lineCount)
    {
        try
        {
            var lines = new List<string>(lineCount);

            using (var stream = new FileStream(
                       sourcePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            {
                using var reader = new StreamReader(stream);

                // 使用固定大小的队列保存最后N行
                var queue = new Queue<string>(lineCount);

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (queue.Count >= lineCount)
                    {
                        queue.Dequeue(); // 移除最旧的行
                    }
                    queue.Enqueue(line);
                }

                lines.AddRange(queue);
            }

            // 将提取的行写入新文件
            using var writer = new StreamWriter(destPath, false, Encoding.UTF8);
            foreach (var line in lines)
            {
                writer.WriteLine(line);
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"提取文件 {sourcePath} 的最后 {lineCount} 行时出错: {ex}");
            // 如果提取失败，复制原始文件
            try { File.Copy(sourcePath, destPath); }
            catch (Exception e)
            {
                LoggerHelper.Error(e);
            }
        }
    }
}

// 日志文件信息类（增加LineCount属性）
public class LogFileInfo
{
    public string? FullName { get; set; }
    public string? RelativePath { get; set; }
    public int LineCount { get; set; } = 0;
}
