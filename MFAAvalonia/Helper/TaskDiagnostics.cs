using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace MFAAvalonia.Helper;

internal sealed class TaskEvidenceSnapshot
{
    internal required DateTimeOffset StartedAt { get; init; }
    internal required Dictionary<string, long> LogLengths { get; init; }
    internal required HashSet<string> ExistingErrorImages { get; init; }
    internal required bool Isolated { get; init; }
}

internal enum TaskEvidenceBuildStatus
{
    Success,
    NotIsolated,
    NoEvidence,
    RawTooLarge,
    CompressedTooLarge
}

internal sealed record TaskEvidenceBuildResult(
    TaskEvidenceBuildStatus Status,
    byte[]? Data,
    int LogCount,
    int ImageCount,
    long RawBytes);

internal static class TaskDiagnostics
{
    private const long MaxRawBytes = 64 * 1024 * 1024;
    private const int MaxCompressedBytes = 10 * 1024 * 1024;
    private const int LogPreludeBytes = 64 * 1024;

    internal static TaskEvidenceSnapshot CaptureStart(bool isolated)
    {
        var logs = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var images = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateEvidenceFiles())
        {
            var relative = Relative(path);
            try
            {
                if (IsLog(relative)) logs[relative] = new FileInfo(path).Length;
                else if (IsErrorImage(relative)) images.Add(relative);
            }
            catch { }
        }
        return new TaskEvidenceSnapshot { StartedAt = DateTimeOffset.UtcNow, LogLengths = logs, ExistingErrorImages = images, Isolated = isolated };
    }

    internal static TaskEvidenceBuildResult Build(TaskEvidenceSnapshot snapshot)
    {
        var logCount = 0;
        var imageCount = 0;
        if (!snapshot.Isolated)
            return new TaskEvidenceBuildResult(TaskEvidenceBuildStatus.NotIsolated, null, 0, 0, 0);

        var selected = new List<(string path, string name, long start, long length)>();
        foreach (var path in EnumerateEvidenceFiles())
        {
            var relative = Relative(path);
            try
            {
                var info = new FileInfo(path);
                if (IsLog(relative) && snapshot.LogLengths.TryGetValue(relative, out var oldLength) && info.Length > oldLength)
                {
                    var start = Math.Max(0, oldLength - LogPreludeBytes);
                    selected.Add((path, relative, start, info.Length - start));
                    logCount++;
                }
                else if (IsErrorImage(relative) && !snapshot.ExistingErrorImages.Contains(relative)
                         && info.LastWriteTimeUtc >= snapshot.StartedAt.UtcDateTime.AddSeconds(-2))
                {
                    selected.Add((path, relative, 0, info.Length));
                    imageCount++;
                }
            }
            catch { }
        }
        var rawBytes = selected.Sum(x => x.length);
        if (selected.Count == 0)
            return new TaskEvidenceBuildResult(TaskEvidenceBuildStatus.NoEvidence, null, 0, 0, 0);
        if (rawBytes > MaxRawBytes)
            return new TaskEvidenceBuildResult(TaskEvidenceBuildStatus.RawTooLarge, null, logCount, imageCount, rawBytes);

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in selected)
            {
                var entry = archive.CreateEntry(item.name, CompressionLevel.Fastest);
                using var target = entry.Open();
                using var source = new FileStream(item.path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                source.Position = item.start;
                var remaining = item.length;
                var buffer = new byte[81920];
                while (remaining > 0)
                {
                    var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read <= 0) break;
                    target.Write(buffer, 0, read);
                    remaining -= read;
                }
            }
        }
        if (output.Length > MaxCompressedBytes)
            return new TaskEvidenceBuildResult(TaskEvidenceBuildStatus.CompressedTooLarge, null, logCount, imageCount, rawBytes);

        return new TaskEvidenceBuildResult(TaskEvidenceBuildStatus.Success, output.ToArray(), logCount, imageCount, rawBytes);
    }

    private static IEnumerable<string> EnumerateEvidenceFiles()
    {
        foreach (var root in new[] { AppPaths.LogsDirectory, Path.Combine(AppPaths.DataRoot, "debug") })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = Relative(path);
                if (!relative.Contains("vision", StringComparison.OrdinalIgnoreCase)
                    && (IsLog(relative) || IsErrorImage(relative)))
                    yield return path;
            }
        }
    }

    private static string Relative(string path) => Path.GetRelativePath(AppPaths.DataRoot, path).Replace('\\', '/');
    private static bool IsLog(string path) => path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || path.Contains(".log.", StringComparison.OrdinalIgnoreCase);
    private static bool IsErrorImage(string path) => path.Contains("/on_error/", StringComparison.OrdinalIgnoreCase)
        && new[] { ".png", ".jpg", ".jpeg" }.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
}
