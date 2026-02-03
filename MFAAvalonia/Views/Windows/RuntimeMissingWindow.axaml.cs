using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using SukiUI.Controls;

namespace MFAAvalonia.Views.Windows;

public partial class RuntimeMissingWindow : SukiWindow
{
    public RuntimeMissingWindow()
    {
        InitializeComponent();
    }

    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        DownloadButton.IsVisible = false;
        DownloadButton.IsEnabled = false;
        DownloadButton.Content = LangKeys.DownloadingStatus.ToLocalization();
        DownloadProgress.IsVisible = true;
        StatusText.Text = LangKeys.DownloadingStatus.ToLocalization();
        StatusLoading.IsVisible = true;
        DownloadInfoGrid.IsVisible = true;

        // Architecture detection
        string downloadUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
        string fileName = "vc_redist.x64.exe";

        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            downloadUrl = "https://aka.ms/vs/17/release/vc_redist.arm64.exe";
            fileName = "vc_redist.arm64.exe";
        }
        else if (RuntimeInformation.ProcessArchitecture == Architecture.X86)
        {
            downloadUrl = "https://aka.ms/vs/17/release/vc_redist.x86.exe";
            fileName = "vc_redist.x86.exe";
        }

        string tempPath = Path.Combine(Path.GetTempPath(), fileName);

        try
        {
            using var httpClient = VersionChecker.CreateHttpClientWithProxy();
            using (var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    var totalRead = 0L;
                    var isMoreToRead = true;
                    var lastUiUpdate = DateTime.MinValue;
                    var lastBytesRead = 0L;

                    do
                    {
                        var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                        if (read == 0)
                        {
                            isMoreToRead = false;
                        }
                        else
                        {
                            await fileStream.WriteAsync(buffer, 0, read);

                            totalRead += read;
                            if (totalBytes != -1)
                            {
                                var now = DateTime.Now;
                                if ((now - lastUiUpdate).TotalMilliseconds >= 100)
                                {
                                    var speed = (totalRead - lastBytesRead) / (now - lastUiUpdate).TotalSeconds;
                                    var currentBytesRead = totalRead; // Capture for lambda
                                    lastBytesRead = totalRead;
                                    lastUiUpdate = now;

                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        DownloadProgress.Value = (double)currentBytesRead / totalBytes * 100;
                                        DownloadSpeedText.Text = $"{FormatSize((long)speed)}/s";
                                        DownloadSizeText.Text = $"{FormatSize(currentBytesRead)} / {FormatSize(totalBytes)}";
                                    });
                                }
                            }
                        }
                    } while (isMoreToRead);
                }
            }

            StatusText.Text = LangKeys.InstallingStatus.ToLocalization();
            DownloadProgress.IsVisible = false;
            DownloadButton.IsVisible = false;
            DownloadInfoGrid.IsVisible = false;
            StatusLoading.IsVisible = true;

            // Run the installer
            var psi = new ProcessStartInfo
            {
                FileName = tempPath,
                Arguments = "/install /norestart", // Show UI, no restart
                UseShellExecute = true
            };

            // Using Task.Run to separate process wait from UI thread
            await Task.Run(() =>
            {
                var process = Process.Start(psi);
                process?.WaitForExit();
            });

            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // ignored
            }

            StatusText.Text = LangKeys.InstallSuccess.ToLocalization();
            StatusLoading.IsVisible = false;
            DownloadButton.IsVisible = true;
            DownloadButton.Content = LangKeys.Close.ToLocalization();
            DownloadButton.IsEnabled = true;
            DownloadButton.Click -= OnDownloadClick;
            DownloadButton.Click += (s, args) => Environment.Exit(0); // Exit app after install
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(LangKeys.DownloadError.ToLocalization(), ex.Message);
            DownloadButton.Content = LangKeys.Retry.ToLocalization();
            DownloadButton.IsEnabled = true;
            DownloadButton.IsVisible = true;
            DownloadProgress.IsVisible = false;
            StatusLoading.IsVisible = false;
            DownloadInfoGrid.IsVisible = false;
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
