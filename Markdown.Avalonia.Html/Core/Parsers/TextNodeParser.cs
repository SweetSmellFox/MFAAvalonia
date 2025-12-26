using Avalonia;
using Avalonia.Controls.Documents;
using ColorTextBlock.Avalonia;
using HtmlAgilityPack;
using Markdown.Avalonia.Html.Core.Utils;
using System.Collections.Generic;

namespace Markdown.Avalonia.Html.Core.Parsers;

public class TextNodeParser : IInlineTagParser
{
    public IEnumerable<string> SupportTag => [HtmlNode.HtmlNodeTypeNameText];

    bool ITagParser.TryReplace(HtmlNode node, ReplaceManager manager, out IEnumerable<StyledElement> generated)
    {
        var rtn = TryReplace(node, manager, out var list);
        generated = list;
        return rtn;
    }

    public bool TryReplace(HtmlNode node, ReplaceManager manager, out IEnumerable<CInline> generated)
    {
        if (node is HtmlTextNode textNode)
        {
            generated = Replace(textNode.Text, manager);
            return true;
        }

        generated = EnumerableExt.Empty<CInline>();
        return false;
    }

    public IEnumerable<CInline> Replace(string text, ReplaceManager manager)
    {
        // 只去除前导换行符，保留其他空格（避免破坏单词间的空格）
        // 这样可以解决HTML内联标签后因换行符产生的间隔问题
        var processedText = text.TrimStart('\n', '\r');

        // 将内部的换行符替换为空格
        processedText = processedText.Replace('\n', ' ');

        // 如果处理后为空，返回空列表
        if (string.IsNullOrEmpty(processedText))
        {
            return EnumerableExt.Empty<CInline>();
        }

        // 使用Markdown引擎处理文本
        return manager.Engine.RunSpanGamut(processedText);
    }
}
