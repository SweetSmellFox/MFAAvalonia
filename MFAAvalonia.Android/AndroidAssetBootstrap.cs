using System;
using System.IO;
using System.IO.Compression;
using Android.Content;

namespace MFAAvalonia.Android;

internal static class AndroidAssetBootstrap
{
    private const string AssetRoot = "MfaPackage";
    private const string PackageAssetName = "package.zip";
    private const string PackageFingerprintAssetName = "package.sha256";
    private const string PackageMarker = ".mfa-package.sha256";

    public static void EnsureExtracted(Context context)
    {
        try
        {
            // 中文：官方空壳 APK 默认没有 MfaPackage 载荷，此时直接跳过，不创建文件也不记录异常。
            // English: The official shell APK has no MfaPackage payload by default; skip without creating files or logging an error.
            if (!HasEmbeddedPackage(context))
                return;

            var targetRoot = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var marker = Path.Combine(targetRoot, PackageMarker);
            var packageFingerprint = ComputePackageFingerprint(context);
            if ((File.Exists(Path.Combine(targetRoot, "interface.json"))
                 || File.Exists(Path.Combine(targetRoot, "interface.jsonc")))
                && File.Exists(marker)
                && string.Equals(File.ReadAllText(marker).Trim(), packageFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            using var packageStream = OpenAsset(context, PackageAssetName);
            ExtractPackage(packageStream, targetRoot);
            File.WriteAllText(marker, packageFingerprint);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MFAAvalonia", $"Asset bootstrap failed: {ex}");
        }
    }

    private static bool HasEmbeddedPackage(Context context)
    {
        var assets = context.Assets ?? throw new InvalidOperationException("Android AssetManager is unavailable.");
        var assetNames = assets.List(AssetRoot) ?? [];
        var hasArchive = Array.Exists(assetNames, name => string.Equals(name, PackageAssetName, StringComparison.Ordinal));
        var hasFingerprint = Array.Exists(assetNames, name => string.Equals(name, PackageFingerprintAssetName, StringComparison.Ordinal));

        if (!hasArchive && !hasFingerprint)
            return false;

        // 中文：只出现其中一个文件说明 APK 打包不完整，不能静默使用损坏的载荷。
        // English: If only one file exists, APK packaging is incomplete and the corrupt payload must not be used silently.
        if (!hasArchive || !hasFingerprint)
            throw new InvalidDataException("The embedded Android resource package is incomplete.");

        return true;
    }

    private static string ComputePackageFingerprint(Context context)
    {
        using var stream = OpenAsset(context, PackageFingerprintAssetName);
        using var reader = new StreamReader(stream);
        var fingerprint = reader.ReadToEnd().Trim();
        if (fingerprint.Length == 0)
            throw new InvalidDataException("The Android package fingerprint is empty.");

        return fingerprint;
    }

    private static void ExtractPackage(Stream packageStream, string targetRoot)
    {
        var normalizedRoot = Path.GetFullPath(targetRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(targetRoot, entry.FullName));
            if (!destinationPath.StartsWith(normalizedRoot, StringComparison.Ordinal))
                throw new InvalidDataException($"Package entry escapes the application directory: {entry.FullName}");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using var source = entry.Open();
            using var target = File.Create(destinationPath);
            source.CopyTo(target);
        }
    }

    private static Stream OpenAsset(Context context, string assetName)
    {
        var assets = context.Assets ?? throw new InvalidOperationException("Android AssetManager is unavailable.");
        return assets.Open($"{AssetRoot}/{assetName}");
    }
}
