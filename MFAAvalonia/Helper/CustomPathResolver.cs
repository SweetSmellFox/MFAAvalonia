using Avalonia.Controls;
using Avalonia.Platform;
using Markdown.Avalonia.Utils;
using MFAAvalonia.Extensions;
using MFAAvalonia.ViewModels.Windows;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading.Tasks;

namespace MFAAvalonia.Helper;

/// <summary>
/// 自定义路径解析器：修复DefaultPathResolver的HTTP请求异常问题，增强容错性（无第三方依赖）
/// </summary>
public class CustomPathResolver : IPathResolver
{
    // 优化HttpClient配置：设置连接池和超时，避免连接耗尽
    private static readonly HttpClient _httpClient;

    /// <summary>
    /// 内存图片存储：用于 maa://image/{key} 协议，避免 base64 编码开销
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte[]> _imageStore = new();

    private const string MaaImageScheme = "maa://image/";

    // 保留原属性
    public string? AssetPathRoot { set; private get; }
    public IEnumerable<string>? CallerAssemblyNames { set; private get; }

    // 静态构造函数：初始化HttpClient（全局唯一，避免频繁创建）
    static CustomPathResolver()
    {
        _httpClient = VersionChecker.CreateHttpClientWithProxy();
    }

    /// <summary>
    /// 存储图片数据并返回 maa://image/{key} URL
    /// </summary>
    public static string StoreImage(byte[] imageData)
    {
        var key = Guid.NewGuid().ToString("N");
        _imageStore[key] = imageData;
        return $"{MaaImageScheme}{key}";
    }

    /// <summary>
    /// 移除已存储的图片数据
    /// </summary>
    public static void RemoveImage(string key)
    {
        _imageStore.TryRemove(key, out _);
    }

    /// <summary>
    /// 清除所有已存储的图片数据
    /// </summary>
    public static void ClearImages()
    {
        _imageStore.Clear();
    }

    /// <summary>
    /// 重写图片资源解析逻辑：整合新的相对路径解析规则
    /// </summary>
    public async Task<Stream?> ResolveImageResource(string relativeOrAbsolutePath)
    {
        // 空值校验：避免无效请求
        if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
            return null;

        // 处理 maa://image/ 协议：从内存存储中获取图片
        if (relativeOrAbsolutePath.StartsWith(MaaImageScheme))
        {
            var key = relativeOrAbsolutePath.Substring(MaaImageScheme.Length);
            if (_imageStore.TryRemove(key, out var imageData))
            {
                return new MemoryStream(imageData);
            }
            return null;
        }

        try
        {
            // 第一步：使用新的ResolveUrl解析路径（核心修改点）
            var root = AssetPathRoot ?? AppContext.BaseDirectory;
            string resolvedPath = relativeOrAbsolutePath.ResolveUrl(root);
            if (string.IsNullOrWhiteSpace(resolvedPath))
                return null;

            // 第二步：处理解析后的路径，转换为Uri并打开流
            Uri? targetUri = null;
            // 处理网络URL（http/https）
            if (resolvedPath.IsUrl())
            {
                targetUri = new Uri(resolvedPath);
            }
            // 处理本地文件路径（转换为file协议Uri）
            else if (resolvedPath.IsAbsolutePath() || File.Exists(resolvedPath))
            {
                targetUri = new Uri(resolvedPath);
               
            }
            // 保留avares协议的原有处理（若解析后的路径是avares格式）
            else if (resolvedPath.StartsWith("avares://") && Uri.TryCreate(resolvedPath, UriKind.Absolute, out var avaresUri))
            {
                targetUri = avaresUri;
            }

            // 第三步：打开流（复用原OpenStream方法）
            if (targetUri != null)
            {
                var stream = await OpenStream(targetUri);
                if (stream != null)
                {
                    // 确保返回的流是可寻址的（第三方库依赖）
                    if (!stream.CanSeek)
                    {
                        var memoryStream = new MemoryStream();
                        await stream.CopyToAsync(memoryStream);
                        memoryStream.Position = 0;
                        await stream.DisposeAsync();
                        return memoryStream;
                    }
                    stream.Position = 0;
                    return stream;
                }
            }

            // 保留原avares协议的兜底处理（兼容原有逻辑）
            if (CallerAssemblyNames != null && !relativeOrAbsolutePath.IsUrl() && !relativeOrAbsolutePath.IsAbsolutePath())
            {
                foreach (string callerAssemblyName in CallerAssemblyNames)
                {
                    var avaresUri = new Uri($"avares://{callerAssemblyName}/{resolvedPath}");
                    var stream = await OpenStream(avaresUri);
                    if (stream != null)
                        return stream;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            // 捕获所有未预期异常，输出日志并返回null（杜绝异常传递到第三方库）
            LoggerHelper.Error($"解析图片失败：{relativeOrAbsolutePath}，错误：{ex}");
            return null;
        }
    }

    #region 原有流处理逻辑（保留并优化）

    /// <summary>
    /// 打开流：处理http/https/file/avares协议
    /// </summary>
    private async Task<Stream?> OpenStream(Uri? url)
    {
        if (url == null)
            return null;

        try
        {
            switch (url.Scheme)
            {
                case "http":
                case "https":
                    // 新增：手动重试2次，应对临时网络波动
                    return await RetryAsync(() => OpenHttpStream(url), retryCount: 2);

                case "file":
                    if (File.Exists(url.LocalPath))
                        return File.OpenRead(url.LocalPath);
                    return null;

                case "avares":
                    if (AssetLoader.Exists(url))
                        return AssetLoader.Open(url);
                    return null;

                default:
                    LoggerHelper.Info($"不支持的协议：{url.Scheme}，URL：{url}");
                    return null;
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Info($"打开流失败（URL：{url}）：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 处理HTTP/HTTPS流：核心优化→将网络流拷贝到内存流，避免ResponseEnded
    /// </summary>
    async private Task<Stream?> OpenHttpStream(Uri url)
    {
        HttpResponseMessage? response = null;
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("MFA", RootViewModel.Version));

            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

            response = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            // 严格校验状态码（包括301/302重定向，自动跟随）
            response.EnsureSuccessStatusCode();

            // 核心优化：将网络流立即拷贝到内存流，脱离网络依赖
            var memoryStream = new MemoryStream();
            await response.Content.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            if (memoryStream.Length == 0)
            {
                LoggerHelper.Warning($"HTTP响应流为空（URL：{url}）");
                await memoryStream.DisposeAsync();
                return null;
            }

            return memoryStream; // 返回内存流，而非原始网络流
        }
        catch (HttpRequestException ex)
        {
            LoggerHelper.Info($"HTTP请求异常（URL：{url}）：{ex.Message}");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            LoggerHelper.Info($"HTTP请求超时（URL：{url}）：{ex.Message}");
            return null;
        }
        catch (IOException ex)
        {
            LoggerHelper.Info($"HTTP流拷贝失败（URL：{url}）：{ex.Message}");
            return null;
        }
        finally
        {
            response?.Dispose(); // 及时释放响应对象
        }
    }

    /// <summary>
    /// 手动重试逻辑：应对临时网络波动
    /// </summary>
    private async Task<T?> RetryAsync<T>(Func<Task<T?>> action, int retryCount) where T : class
    {
        int attempt = 0;
        while (attempt <= retryCount)
        {
            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                attempt++;
                if (attempt > retryCount)
                {
                    LoggerHelper.Info($"重试{retryCount}次后仍失败：{ex.Message}");
                    throw;
                }
                // 等待100ms后重试
                await Task.Delay(100);
            }
        }
        return null;
    }

    #endregion
}
