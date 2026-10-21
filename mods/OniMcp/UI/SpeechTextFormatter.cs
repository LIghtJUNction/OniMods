using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Parsers.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace OniMcp
{
    // Parse Markdown first, then emit only the TMP tags the speech UI supports.
    // The visible-character budget is independent of markup length.
    internal sealed class SpeechTextFormatter
    {
        private static readonly MarkdownPipeline Pipeline = CreatePipeline();
        private static readonly Regex NativeTag = new Regex(
            @"^<(?<close>/)?(?<name>b|i|u|s|color|size)(?:=(?<value>[^<>]+))?>$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex ColorValue = new Regex(
            @"^(#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?|red|green|blue|white|black|yellow|orange|purple)$");

        private readonly StringBuilder rich = new StringBuilder();
        private readonly StringBuilder plain = new StringBuilder();
        private readonly List<string> styles = new List<string>();
        private int remaining;
        private bool truncated;

        private static MarkdownPipeline CreatePipeline()
        {
            var builder = new MarkdownPipelineBuilder()
                .UsePipeTables().UseEmphasisExtras(EmphasisExtraOptions.Strikethrough);
            builder.BlockParsers.RemoveAll(parser => parser is HtmlBlockParser);
            builder.InlineParsers.InsertBefore<AutolinkInlineParser>(new NativeTagParser());
            return builder.Build();
        }

        private sealed class NativeTagParser : InlineParser
        {
            internal NativeTagParser() { OpeningCharacters = new[] { '<' }; }
            public override bool Match(InlineProcessor processor, ref StringSlice slice)
            {
                int end = slice.Text.IndexOf('>', slice.Start);
                if (end < slice.Start || end > slice.End || end - slice.Start > 64) return false;
                string tag = slice.Text.Substring(slice.Start, end - slice.Start + 1);
                if (!NativeTag.IsMatch(tag)) return false;
                processor.Inline = new HtmlInline(tag);
                slice.Start = end + 1;
                return true;
            }
        }

        private SpeechTextFormatter(int maximum) { remaining = maximum - 1; }
        internal string RichText { get { return rich.ToString(); } }
        internal string PlainText { get { return plain.ToString(); } }

        internal static SpeechTextFormatter Format(string source, int maximum = 220)
        {
            if (maximum < 1) throw new ArgumentOutOfRangeException(nameof(maximum));
            var output = new SpeechTextFormatter(maximum);
            if (!string.IsNullOrWhiteSpace(source))
                output.Blocks(Markdown.Parse(source.Trim(), Pipeline));
            output.CloseTo(0);
            if (output.truncated)
            {
                output.rich.Append("…");
                output.plain.Append("…");
            }
            return output;
        }

        private void Literal(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            var text = new StringBuilder();
            var characters = StringInfo.GetTextElementEnumerator(value);
            while (characters.MoveNext())
            {
                if (remaining == 0) { truncated = true; break; }
                text.Append(characters.GetTextElement());
                remaining--;
            }
            if (text.Length == 0) return;
            plain.Append(text);
            // A zero-width separator prevents literal </noparse> from ending
            // TMP's literal span, including differently cased closing tags.
            rich.Append("<noparse>").Append(text.ToString().Replace("<", "<\u200B"))
                .Append("</noparse>");
        }

        private void Open(string name, string value = null)
        {
            rich.Append('<').Append(name);
            if (value != null) rich.Append('=').Append(value);
            rich.Append('>');
            styles.Add(name);
        }

        private void CloseTo(int count)
        {
            while (styles.Count > count)
            {
                int last = styles.Count - 1;
                rich.Append("</").Append(styles[last]).Append('>');
                styles.RemoveAt(last);
            }
        }

        private void Styled(string name, System.Action body, string value = null)
        {
            int depth = styles.Count;
            Open(name, value);
            body();
            CloseTo(depth);
        }

        private void Blocks(ContainerBlock blocks)
        {
            bool first = true;
            foreach (Block block in blocks)
            {
                if (!first) Literal("\n");
                first = false;
                Block(block);
                if (truncated) break;
            }
        }

        private void Block(Block block)
        {
            var list = block as ListBlock;
            if (list != null)
            {
                int start;
                if (!int.TryParse(list.OrderedStart, out start)) start = 1;
                foreach (Block item in list)
                {
                    if (item != list[0]) Literal("\n");
                    Literal(list.IsOrdered ? (start++).ToString(CultureInfo.InvariantCulture) + ". " : "• ");
                    Blocks((ContainerBlock)item);
                    if (truncated) break;
                }
                return;
            }
            var row = block as Markdig.Extensions.Tables.TableRow;
            if (row != null)
            {
                bool first = true;
                foreach (Block cell in row)
                {
                    if (!first) Literal(" | ");
                    first = false;
                    if (row.IsHeader) Styled("b", () => Blocks((ContainerBlock)cell));
                    else Blocks((ContainerBlock)cell);
                }
                return;
            }
            if (block is QuoteBlock) Literal("│ ");
            if (block is ThematicBreakBlock) { Literal("—"); return; }
            var leaf = block as LeafBlock;
            if (leaf != null)
            {
                System.Action render = () =>
                {
                    if (leaf.Inline != null) Inlines(leaf.Inline);
                    else Literal(leaf.Lines.ToString());
                };
                if (leaf is HeadingBlock) Styled("b", render);
                else if (leaf is CodeBlock) Styled("mspace", render, "0.6em");
                else render();
                return;
            }
            var container = block as ContainerBlock;
            if (container != null) Blocks(container);
        }

        private void Inlines(ContainerInline container)
        {
            foreach (Inline child in container)
            {
                var literal = child as LiteralInline;
                var code = child as CodeInline;
                var emphasis = child as EmphasisInline;
                var html = child as HtmlInline;
                var entity = child as HtmlEntityInline;
                var autoLink = child as AutolinkInline;
                if (literal != null) Literal(literal.Content.ToString());
                else if (code != null) Styled("mspace", () => Literal(code.Content), "0.6em");
                else if (emphasis != null)
                    Styled(emphasis.DelimiterChar == '~' ? "s" : emphasis.DelimiterCount == 2 ? "b" : "i",
                        () => Inlines(emphasis));
                else if (child is LineBreakInline) Literal("\n");
                else if (entity != null) Literal(entity.Transcoded.ToString());
                else if (autoLink != null) Literal(autoLink.Url);
                else if (html != null) { if (!RichTag(html.Tag)) Literal(html.Tag); }
                else if (child is ContainerInline) Inlines((ContainerInline)child);
                if (truncated) break;
            }
        }

        private bool RichTag(string value)
        {
            Match match = NativeTag.Match(value);
            if (!match.Success) return false;
            string name = match.Groups["name"].Value.ToLowerInvariant();
            if (match.Groups["close"].Success)
            {
                int index = styles.LastIndexOf(name);
                if (index >= 0) CloseTo(index);
                return true;
            }
            string argument = match.Groups["value"].Success
                ? match.Groups["value"].Value.Trim('"', '\'') : null;
            if (name == "color")
            {
                if (argument == null || !ColorValue.IsMatch(argument)) return false;
            }
            else if (name == "size")
            {
                double size;
                bool percent = argument != null && argument.EndsWith("%", StringComparison.Ordinal);
                if (argument == null || !double.TryParse(argument.TrimEnd('%'), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out size) || size < (percent ? 50 : 6) || size > (percent ? 150 : 24))
                    return false;
            }
            else if (argument != null) return false;
            Open(name, argument);
            return true;
        }
    }
}
