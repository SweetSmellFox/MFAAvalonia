using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Android.Content;

namespace MFAAvalonia.Android;

internal static class AndroidAssetBootstrap
{
    private const string AssetRoot = "MfaPackage";
    private const string PackageMarker = ".mfa-package.sha256";

    public static void EnsureExtracted(Context context)
    {
        try
        {
            var targetRoot = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var marker = Path.Combine(targetRoot, PackageMarker);
            var packageFingerprint = ComputePackageFingerprint(context);
            if (File.Exists(Path.Combine(targetRoot, "interface.json"))
                && File.Exists(marker)
                && string.Equals(File.ReadAllText(marker).Trim(), packageFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            CopyDirectory(context, AssetRoot, targetRoot);
            var archivePath = Path.Combine(targetRoot, "resource.zip");
            if (File.Exists(archivePath))
            {
                var resourceRoot = Path.Combine(targetRoot, "resource");
                if (Directory.Exists(resourceRoot))
                    Directory.Delete(resourceRoot, recursive: true);
                Directory.CreateDirectory(resourceRoot);
                ZipFile.ExtractToDirectory(archivePath, resourceRoot, overwriteFiles: true);
                File.Delete(archivePath);
            }
            File.WriteAllText(marker, packageFingerprint);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MFAAvalonia", $"Asset bootstrap failed: {ex}");
        }
    }

    private static string ComputePackageFingerprint(Context context)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var assets = context.Assets ?? throw new InvalidOperationException("Android AssetManager is unavailable.");
        var assetNames = assets.List(AssetRoot) ?? [];

        foreach (var assetName in assetNames.OrderBy(name => name, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(assetName));
            using var stream = assets.Open($"{AssetRoot}/{assetName}");
            var buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer, 0, bytesRead);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void CopyDirectory(Context context, string assetPath, string targetPath)
    {
        var children = context.Assets?.List(assetPath) ?? [];
        if (children.Length == 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            using var source = context.Assets!.Open(assetPath);
            using var target = File.Create(targetPath);
            source.CopyTo(target);
            return;
        }

        Directory.CreateDirectory(targetPath);
        foreach (var child in children)
            CopyDirectory(context, $"{assetPath}/{child}", Path.Combine(targetPath, child));
    }
}
