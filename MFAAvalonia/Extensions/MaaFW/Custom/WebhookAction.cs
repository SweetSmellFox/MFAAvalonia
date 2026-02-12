using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using System;
using System.Net.Http;
using System.Text;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

public class WebhookAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(WebhookAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            var url = "";
            var method = "POST";
            var body = "";
            var contentType = "application/json";

            if (!string.IsNullOrWhiteSpace(args.ActionParam))
            {
                var json = ActionParamHelper.Parse(args.ActionParam);
                url = (string?)json["url"] ?? "";
                method = (string?)json["method"] ?? "POST";
                body = (string?)json["body"] ?? "";
                contentType = (string?)json["content_type"] ?? "application/json";
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                LoggerHelper.Warning("[WebhookAction] 未指定URL");
                return false;
            }

            LoggerHelper.Info($"[WebhookAction] {method} {url}");
            using var client = VersionChecker.CreateHttpClientWithProxy();
            client.Timeout = TimeSpan.FromSeconds(30);

            HttpResponseMessage response;
            if (method.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                response = client.GetAsync(url).GetAwaiter().GetResult();
            }
            else
            {
                var content = new StringContent(body, Encoding.UTF8, contentType);
                response = client.PostAsync(url, content).GetAwaiter().GetResult();
            }

            LoggerHelper.Info($"[WebhookAction] Response: {response.StatusCode}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[WebhookAction] Error: {e.Message}");
            return false;
        }
    }
}
