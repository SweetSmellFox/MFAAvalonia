using Avalonia.Media;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Notification;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.Converters;
using MFAAvalonia.ViewModels.Pages;
using MFAAvalonia.Views.Pages;
using MFAAvalonia.Views.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.MessageBox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MFAAvalonia.Extensions.MaaFW;

/// <summary>
/// Focus 消息处理类
/// </summary>
public class FocusHandler
{
    private AutoInitDictionary autoInitDictionary;
    private readonly TaskQueueViewModel _viewModel;

    public FocusHandler(AutoInitDictionary autoInitDictionary, TaskQueueViewModel viewModel)
    {
        this.autoInitDictionary = autoInitDictionary;
        _viewModel = viewModel;
    }

    public void UpdateDictionary(AutoInitDictionary dictionary)
    {
        autoInitDictionary = dictionary;
    }

    /// <summary>
    /// Focus 数据模型
    /// </summary>
    public class Focus
    {
        [JsonConverter(typeof(GenericSingleOrListConverter<string>))] [JsonProperty("start")]
        public List<string>? Start;

        [JsonConverter(typeof(GenericSingleOrListConverter<string>))] [JsonProperty("succeeded")]
        public List<string>? Succeeded;

        [JsonConverter(typeof(GenericSingleOrListConverter<string>))] [JsonProperty("failed")]
        public List<string>? Failed;

        [JsonConverter(typeof(GenericSingleOrListConverter<string>))] [JsonProperty("toast")]
        public List<string>? Toast;

        [JsonProperty("aborted")] public bool? Aborted;
    }

    /// <summary>
    /// 解析带颜色标记的文本
    /// </summary>
    public static (string Text, string? Color) ParseColorText(string input)
    {
        var match = Regex.Match(input.Trim(), @"\[color:(?<color>.*?)\](?<text>.*?)\[/color\]", RegexOptions.IgnoreCase);

        if (match.Success)
        {
            string color = match.Groups["color"].Value.Trim();
            string text = match.Groups["text"].Value;
            return (text, color);
        }

        return (input, null);
    }

    /// <summary>
    /// 显示 Focus 消息
    /// </summary>
    /// <param name="taskModel">任务模型 JObject</param>
    /// <param name="message">消息类型</param>
    /// <param name="detail">详情</param>
    /// <param name="imageBuffer">当前截图的 MaaImageBuffer，用于新协议中 {image} 占位符替换为 base64</param>
    /// <param name="onAborted">中止回调</param>
    public void DisplayFocus(JObject taskModel, string message, string detail, MaaImageBuffer? imageBuffer = null, Action? onAborted = null)
    {
        try
        {
            if (taskModel["focus"] == null)
                return;

            var focusToken = taskModel["focus"];
            var focus = new Focus();
            JObject? newProtocolFocus = null;

            // 解析focus内容，同时提取新旧协议数据
            if (focusToken!.Type == JTokenType.String)
            {
                // 旧协议：字符串形式（等价于start）
                focus.Start = new List<string>
                {
                    focusToken.Value<string>()!
                };
            }
            else if (focusToken.Type == JTokenType.Object)
            {
                var focusObj = focusToken as JObject;
                // 提取旧协议字段（start/succeeded/failed/toast等）
                focus = focusObj!.ToObject<Focus>();
                // 提取新协议字段（消息类型为键的条目）
                newProtocolFocus = new JObject(
                    focusObj.Properties()
                        .Select(prop => new JProperty(prop.Name, prop.Value))
                );
            }

            // 解析详情数据（用于新协议占位符替换，detail 为空时 detailsObj 保持 null）
            JObject? detailsObj = null;
            if (!string.IsNullOrEmpty(detail))
            {
                try
                {
                    detailsObj = JObject.Parse(detail);
                }
                catch
                {
                    // 忽略详情解析错误
                }
            }

            // 1. 处理新协议（不依赖 detail 是否为空，node.action.starting 的 detail 通常为空）
            if (newProtocolFocus is { HasValues: true } && newProtocolFocus.TryGetValue(message, out var templateToken))
            {
                ProcessNewProtocol(templateToken, taskModel, detailsObj, imageBuffer);
            }

            // 2. 处理旧协议（如果有）
            ProcessOldProtocol(focus, message, onAborted);
        }
        catch (Exception e)
        {
            LoggerHelper.Error(e);
        }
    }

    /// <summary>
    /// 处理新协议消息。
    /// 支持三种 focus 值形式：
    /// 1. 字符串: "content" → display=log
    /// 2. 字符串数组: ["c1","c2"] → 每项 display=log
    /// 3. 对象: { "content": "...", "display": "toast" | ["log","toast"] }
    /// </summary>
    private void ProcessNewProtocol(JToken templateToken, JObject taskModel, JObject? detailsObj, MaaImageBuffer? imageBuffer)
    {
        string? content = null;
        List<string> displays = new List<string> { "log" };

        if (templateToken.Type == JTokenType.Object)
        {
            // 对象形式：{ "content": "...", "display": "toast" | ["log","toast"] }
            var obj = (JObject)templateToken;
            content = obj["content"]?.Value<string>();
            var displayToken = obj["display"];
            if (displayToken != null)
            {
                if (displayToken.Type == JTokenType.Array)
                    displays = displayToken.ToObject<List<string>>() ?? displays;
                else if (displayToken.Type == JTokenType.String)
                    displays = new List<string> { displayToken.Value<string>()! };
            }
            if (content != null)
            {
                var displayText = ReplacePlaceholders(content.ResolveContentAsync().Result, detailsObj, imageBuffer);
                DispatchToChannels(displayText, displays);
            }
        }
        else if (templateToken.Type == JTokenType.Array)
        {
            // 数组形式：每项为字符串，全部 display=log
            foreach (var item in templateToken.Children())
            {
                if (item.Type == JTokenType.String)
                {
                    var template = item.Value<string>();
                    var displayText = ReplacePlaceholders(template!.ResolveContentAsync().Result, detailsObj, imageBuffer);
                    DispatchToChannels(displayText, new List<string> { "log" });
                }
            }
        }
        else if (templateToken.Type == JTokenType.String)
        {
            // 字符串形式：display=log
            var template = templateToken.Value<string>();
            var displayText = ReplacePlaceholders(template!.ResolveContentAsync().Result, detailsObj, imageBuffer);
            DispatchToChannels(displayText, new List<string> { "log" });
        }
    }

    /// <summary>
    /// 根据 display 渠道列表分发消息
    /// </summary>
    private void DispatchToChannels(string displayText, List<string> displays)
    {
        foreach (var channel in displays)
        {
            switch (channel.ToLower())
            {
                case "log":
                    _viewModel.AddMarkdown(TaskQueueView.ConvertCustomMarkup(displayText));
                    break;
                case "toast":
                    ToastHelper.Info(displayText);
                    break;
                case "notification":
                    ToastNotification.Show(displayText);
                    break;
                case "dialog":
                    // 非阻塞式弹窗：fire-and-forget，任务继续执行
                    Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                    {
                        await SukiMessageBox.ShowDialog(new SukiMessageBoxHost
                        {
                            Content = displayText,
                            ActionButtonsPreset = SukiMessageBoxButtons.OK,
                        }, new SukiMessageBoxOptions
                        {
                            Title = LangKeys.Tip.ToLocalization(),
                        });
                    });
                    break;
                case "modal":
                    // 阻塞式弹窗：InvokeAsync 返回 Task，.Wait() 阻塞回调线程直到用户关闭弹窗
                    var modalProcessor = _viewModel.Processor;
                    modalProcessor?.SetWaitingForModal(true);
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync((async () =>
                    {
                        await SukiMessageBox.ShowDialog(new SukiMessageBoxHost
                        {
                            Content = displayText,
                            ActionButtonsPreset = SukiMessageBoxButtons.OK,
                        }, new SukiMessageBoxOptions
                        {
                            Title = LangKeys.Tip.ToLocalization(),
                        });
                    })).Wait();
                    modalProcessor?.SetWaitingForModal(false);
                    break;
            }
        }
    }

    /// <summary>
    /// 处理旧协议消息
    /// </summary>
    private void ProcessOldProtocol(Focus? focus, string message, Action? onAborted)
    {
        if (focus == null) return;

        switch (message)
        {
            case MaaMsg.Node.Action.Succeeded:
                if (focus.Succeeded != null)
                {
                    foreach (var line in focus.Succeeded)
                    {
                        var (text, color) = ParseColorText(line);
                        _viewModel.AddLog(HandleStringsWithVariables(text), color == null ? null : BrushHelper.ConvertToBrush(color));
                    }
                }
                break;

            case MaaMsg.Node.Action.Failed:
                if (focus.Failed != null)
                {
                    foreach (var line in focus.Failed)
                    {
                        var (text, color) = ParseColorText(line);
                        _viewModel.AddLog(HandleStringsWithVariables(text), color == null ? null : BrushHelper.ConvertToBrush(color));
                    }
                }
                break;

            case MaaMsg.Node.Action.Starting:
                if (focus.Aborted == true)
                {
                    onAborted?.Invoke();
                }
                if (focus.Toast is { Count: > 0 })
                {
                    var (title, _) = ParseColorText(focus.Toast[0]);
                    var (content, _) = focus.Toast.Count >= 2 ? ParseColorText(focus.Toast[1]) : ("", "");
                    ToastNotification.Show(HandleStringsWithVariables(title), HandleStringsWithVariables(content));
                }
                if (focus.Start != null)
                {
                    foreach (var line in focus.Start)
                    {
                        var (text, color) = ParseColorText(line);
                        _viewModel.AddLog(HandleStringsWithVariables(text), color == null ? null : BrushHelper.ConvertToBrush(color));
                    }
                }
                break;
        }
    }

    /// <summary>
    /// 替换模板中的占位符
    /// </summary>
    private string ReplacePlaceholders(string template, JObject? details, MaaImageBuffer? imageBuffer = null)
    {
        string result = template;

        // 替换 {image} 为 Markdown 内联图片（使用 maa://image/ 协议）
        if (result.Contains("{image}") && imageBuffer != null)
        {
            var imageUrl = StoreImageAndGetUrl(imageBuffer);
            if (!string.IsNullOrEmpty(imageUrl))
            {
                result = result.Replace("{image}", $"![image]({imageUrl})");
            }
        }

        // 再用details中的属性替换（如果有的话）
        if (details != null)
        {
            foreach (var prop in details.Properties())
            {
                result = result.Replace($"{{{prop.Name}}}", prop.Value.ToString());
            }
        }

        return result;
    }

    /// <summary>
    /// 将 MaaImageBuffer 存入内存图片存储，返回 maa://image/{key} URL
    /// </summary>
    private static string? StoreImageAndGetUrl(MaaImageBuffer imageBuffer)
    {
        try
        {
            if (imageBuffer.IsInvalid || imageBuffer.IsEmpty)
                return null;

            if (!imageBuffer.TryGetEncodedData(out Stream encodedDataStream))
                return null;

            using (encodedDataStream)
            {
                encodedDataStream.Seek(0, SeekOrigin.Begin);
                using var memoryStream = new MemoryStream();
                encodedDataStream.CopyTo(memoryStream);
                return CustomPathResolver.StoreImage(memoryStream.ToArray());
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"存储图片失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 处理带变量的字符串
    /// </summary>
    public string HandleStringsWithVariables(string content)
    {
        try
        {
            return Regex.Replace(content, @"\{(\+\+|\-\-)?(\w+)(\+\+|\-\-)?([\+\-\*/]\w+)?\}", match =>
            {
                var prefix = match.Groups[1].Value;
                var counterKey = match.Groups[2].Value;
                var suffix = match.Groups[3].Value;
                var operation = match.Groups[4].Value;

                int value = autoInitDictionary.GetValueOrDefault(counterKey, 0);

                // 前置操作符
                if (prefix == "++")
                {
                    value = ++autoInitDictionary[counterKey];
                }
                else if (prefix == "--")
                {
                    value = --autoInitDictionary[counterKey];
                }

                // 后置操作符
                if (suffix == "++")
                {
                    value = autoInitDictionary[counterKey]++;
                }
                else if (suffix == "--")
                {
                    value = autoInitDictionary[counterKey]--;
                }

                // 算术操作
                if (!string.IsNullOrEmpty(operation))
                {
                    string operationType = operation[0].ToString();
                    string operandKey = operation.Substring(1);

                    if (autoInitDictionary.TryGetValue(operandKey, out var operandValue))
                    {
                        value = operationType switch
                        {
                            "+" => value + operandValue,
                            "-" => value - operandValue,
                            "*" => value * operandValue,
                            "/" => value / operandValue,
                            _ => value
                        };
                    }
                }

                return value.ToString();
            });
        }
        catch (Exception e)
        {
            LoggerHelper.Error(e);
            ErrorView.ShowException(e);
            return content;
        }
    }

    /// <summary>
    /// 静态方法：处理带变量的字符串（使用指定的字典）
    /// </summary>
    public static string HandleStringsWithVariables(string content, AutoInitDictionary autoInitDictionary)
    {
        var handler = new FocusHandler(autoInitDictionary, null!);
        return handler.HandleStringsWithVariables(content);
    }
}
