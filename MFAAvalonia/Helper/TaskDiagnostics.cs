using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace MFAAvalonia.Helper;

internal sealed class TaskEvidenceSnapshot
{
    internal required DateTimeOffset StartedAt { get; init; }
    internal required Dictionary<string, long> LogLengths { get; init; }
    internal required Dictionary<string, (long Length, DateTime LastWriteUtc)> ExistingErrorImages { get; init; }
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

internal sealed record DiagnosticLogContent(
    string Source,
    string Kind,
    string Content,
    long RawBytes);

internal sealed record DiagnosticLogBuildResult(
    TaskEvidenceBuildStatus Status,
    IReadOnlyList<DiagnosticLogContent> Logs,
    long RawBytes,
    bool Truncated);

internal sealed record ImageEvidenceBuildResult(
    TaskEvidenceBuildStatus Status,
    byte[]? Data,
    int ImageCount,
    long RawBytes);

internal sealed record TaskEvidenceBuildResult(
    DiagnosticLogBuildResult Logs,
    ImageEvidenceBuildResult Images);

internal static class TaskDiagnostics
{
    private const long MaxImageRawBytes = 64 * 1024 * 1024;
    private const int MaxImageCompressedBytes = 10 * 1024 * 1024;
    private const int MaxLogRawBytes = 1024 * 1024;
    private const int MaxLogFileBytes = 512 * 1024;
    private const int LogPreludeBytes = 64 * 1024;
    private const int ExceptionLogTailBytes = 256 * 1024;
    private const int MaxExceptionLogFiles = 5;

    internal static TaskEvidenceSnapshot CaptureStart(bool isolated)
    {
        var logs = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var images = new Dictionary<string, (long Length, DateTime LastWriteUtc)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateEvidenceFiles())
        {
            var relative = Relative(path);
            try
            {
                if (IsLog(relative)) logs[relative] = new FileInfo(path).Length;
                else if (IsErrorImage(relative))
                {
                    var info = new FileInfo(path);
                    images[relative] = (info.Length, info.LastWriteTimeUtc);
                }
            }
            catch { }
        }
        return new TaskEvidenceSnapshot { StartedAt = DateTimeOffset.UtcNow, LogLengths = logs, ExistingErrorImages = images, Isolated = isolated };
    }

    internal static TaskEvidenceBuildResult Build(TaskEvidenceSnapshot snapshot)
    {
        if (!snapshot.Isolated)
        {
            return new TaskEvidenceBuildResult(
                EmptyLogs(TaskEvidenceBuildStatus.NotIsolated),
                EmptyImages(TaskEvidenceBuildStatus.NotIsolated));
        }

        var logCandidates = new List<LogCandidate>();
        var imageCandidates = new List<(string Path, string Name, long Length)>();
        foreach (var path in EnumerateEvidenceFiles())
        {
            var relative = Relative(path);
            try
            {
                var info = new FileInfo(path);
                if (IsLog(relative) && snapshot.LogLengths.TryGetValue(relative, out var oldLength) && info.Length > oldLength)
                {
                    var start = Math.Max(0, oldLength - LogPreludeBytes);
                    logCandidates.Add(new LogCandidate(path, relative, start, info.Length - start, info.LastWriteTimeUtc));
                }
                else if (IsLog(relative)
                         && !snapshot.LogLengths.ContainsKey(relative)
                         && info.LastWriteTimeUtc >= snapshot.StartedAt.UtcDateTime.AddSeconds(-2))
                {
                    logCandidates.Add(new LogCandidate(path, relative, 0, info.Length, info.LastWriteTimeUtc));
                }
                else if (IsErrorImage(relative)
                         && info.LastWriteTimeUtc >= snapshot.StartedAt.UtcDateTime.AddSeconds(-2)
                         && (!snapshot.ExistingErrorImages.TryGetValue(relative, out var previous)
                             || previous.Length != info.Length
                             || previous.LastWriteUtc != info.LastWriteTimeUtc))
                {
                    imageCandidates.Add((path, relative, info.Length));
                }
            }
            catch { }
        }

        return new TaskEvidenceBuildResult(BuildLogs(logCandidates), BuildImages(imageCandidates));
    }

    internal static DiagnosticLogBuildResult BuildRecentLogs()
    {
        var selected = EnumerateEvidenceFiles()
            .Where(path => IsLog(Relative(path)))
            .Select(path =>
            {
                try
                {
                    var info = new FileInfo(path);
                    var length = Math.Min(info.Length, ExceptionLogTailBytes);
                    return new LogCandidate(path, Relative(path), info.Length - length, length, info.LastWriteTimeUtc);
                }
                catch { return null; }
            })
            .Where(item => item != null)
            .Cast<LogCandidate>()
            .OrderBy(item => GetLogPriority(item.Name))
            .ThenByDescending(item => item.LastWriteUtc)
            .Take(MaxExceptionLogFiles)
            .ToList();

        return BuildLogs(selected);
    }

    private static DiagnosticLogBuildResult BuildLogs(IEnumerable<LogCandidate> candidates)
    {
        var logs = new List<DiagnosticLogContent>();
        var rawBytes = 0L;
        var truncated = false;
        foreach (var item in candidates
                     .OrderBy(candidate => GetLogPriority(candidate.Name))
                     .ThenByDescending(candidate => candidate.LastWriteUtc))
        {
            var remainingBudget = MaxLogRawBytes - rawBytes;
            if (remainingBudget <= 0)
            {
                truncated = true;
                break;
            }

            var readLength = Math.Min(item.Length, Math.Min(MaxLogFileBytes, remainingBudget));
            if (readLength <= 0)
                continue;

            // Keep the end of each selected range: failure details are normally emitted last.
            var readStart = item.Start + item.Length - readLength;
            try
            {
                var data = ReadBytes(item.Path, readStart, readLength);
                if (data.Length == 0)
                    continue;

                var content = Encoding.UTF8.GetString(data).Trim('\0');
                if (string.IsNullOrWhiteSpace(content))
                    continue;

                logs.Add(new DiagnosticLogContent(item.Name, GetLogKind(item.Name), content, data.Length));
                rawBytes += data.Length;
                truncated |= readLength < item.Length;
            }
            catch { }
        }

        return logs.Count == 0
            ? EmptyLogs(TaskEvidenceBuildStatus.NoEvidence)
            : new DiagnosticLogBuildResult(TaskEvidenceBuildStatus.Success, logs, rawBytes, truncated);
    }

    private static ImageEvidenceBuildResult BuildImages(IReadOnlyCollection<(string Path, string Name, long Length)> selected)
    {
        if (selected.Count == 0)
            return EmptyImages(TaskEvidenceBuildStatus.NoEvidence);

        var rawBytes = selected.Sum(item => item.Length);
        if (rawBytes > MaxImageRawBytes)
            return new ImageEvidenceBuildResult(TaskEvidenceBuildStatus.RawTooLarge, null, selected.Count, rawBytes);

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in selected)
            {
                var entry = archive.CreateEntry(item.Name, CompressionLevel.Fastest);
                using var target = entry.Open();
                using var source = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                source.CopyTo(target);
            }
        }

        return output.Length > MaxImageCompressedBytes
            ? new ImageEvidenceBuildResult(TaskEvidenceBuildStatus.CompressedTooLarge, null, selected.Count, rawBytes)
            : new ImageEvidenceBuildResult(TaskEvidenceBuildStatus.Success, output.ToArray(), selected.Count, rawBytes);
    }

    private static byte[] ReadBytes(string path, long start, long length)
    {
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        source.Position = start;
        using var output = new MemoryStream((int)Math.Min(length, int.MaxValue));
        var remaining = length;
        var buffer = new byte[81920];
        while (remaining > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0) break;
            output.Write(buffer, 0, read);
            remaining -= read;
        }
        return output.ToArray();
    }

    private static IEnumerable<string> EnumerateEvidenceFiles()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { AppPaths.LogsDirectory, Path.Combine(AppPaths.DataRoot, "debug") })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var fullPath = Path.GetFullPath(path);
                if (!seen.Add(fullPath)) continue;
                var relative = Relative(fullPath);
                if (!relative.Contains("vision", StringComparison.OrdinalIgnoreCase)
                    && (IsLog(relative) || IsErrorImage(relative)))
                    yield return fullPath;
            }
        }
    }

    private static int GetLogPriority(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.StartsWith("maafw.log", StringComparison.OrdinalIgnoreCase)) return 0;
        if (fileName.StartsWith("maa.log", StringComparison.OrdinalIgnoreCase)) return 1;
        if (fileName.StartsWith("log-", StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }

    private static string GetLogKind(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.StartsWith("maafw.log", StringComparison.OrdinalIgnoreCase)) return "maafw";
        if (fileName.StartsWith("maa.log", StringComparison.OrdinalIgnoreCase)) return "maa";
        if (fileName.StartsWith("log-", StringComparison.OrdinalIgnoreCase)) return "mfa";
        return "other";
    }

    private static DiagnosticLogBuildResult EmptyLogs(TaskEvidenceBuildStatus status) =>
        new(status, Array.Empty<DiagnosticLogContent>(), 0, false);

    private static ImageEvidenceBuildResult EmptyImages(TaskEvidenceBuildStatus status) =>
        new(status, null, 0, 0);

    private static string Relative(string path) => Path.GetRelativePath(AppPaths.DataRoot, path).Replace('\\', '/');
    private static bool IsLog(string path) => path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || path.Contains(".log.", StringComparison.OrdinalIgnoreCase);
    private static bool IsErrorImage(string path) => path.Contains("/on_error/", StringComparison.OrdinalIgnoreCase)
        && new[] { ".png", ".jpg", ".jpeg" }.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private sealed record LogCandidate(string Path, string Name, long Start, long Length, DateTime LastWriteUtc);
}
