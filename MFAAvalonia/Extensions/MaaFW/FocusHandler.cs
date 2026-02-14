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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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

            // 处理详情数据（用于新协议占位符替换）
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
                // 1. 处理新协议（如果有）
                if (newProtocolFocus is { HasValues: true } && newProtocolFocus.TryGetValue(message, out var templateToken))
                {
                    ProcessNewProtocol(templateToken, taskModel, detailsObj, imageBuffer);
                }
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
    /// 处理新协议消息
    /// </summary>
    private void ProcessNewProtocol(JToken templateToken, JObject taskModel, JObject? detailsObj, MaaImageBuffer? imageBuffer)
    {
        // 处理字符串数组类型
        if (templateToken.Type == JTokenType.Array)
        {
            foreach (var item in templateToken.Children())
            {
                if (item.Type == JTokenType.String)
                {
                    var template = item.Value<string>();
                    var displayText = ReplacePlaceholders(template!.ResolveContentAsync().Result, detailsObj, imageBuffer);
                    _viewModel.AddMarkdown(TaskQueueView.ConvertCustomMarkup(displayText));
                }
            }
        }
        // 处理单个字符串类型
        else if (templateToken.Type == JTokenType.String)
        {
            var template = templateToken.Value<string>();
            var displayText = ReplacePlaceholders(template!.ResolveContentAsync().Result, detailsObj, imageBuffer);
            _viewModel.AddMarkdown(TaskQueueView.ConvertCustomMarkup(displayText));
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

        // 替换 {image} 为 MaaImageBuffer 的 base64 编码图片
        if (result.Contains("{image}") && imageBuffer != null)
        {
            var base64Image = GetImageBase64(imageBuffer);
            if (!string.IsNullOrEmpty(base64Image))
            {
                result = result.Replace("{image}", base64Image);
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
    /// 从 MaaImageBuffer 获取 base64 编码的图片数据（Markdown 内联图片格式）
    /// </summary>
    private static string? GetImageBase64(MaaImageBuffer imageBuffer)
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
                var base64 = Convert.ToBase64String(memoryStream.ToArray());
                return $"data:image/png;base64,{base64}";
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"获取图片 base64 失败: {ex.Message}");
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
