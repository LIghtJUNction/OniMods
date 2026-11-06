using System;
using System.Globalization;
using OniMcp;

internal static class SpeechTextRegression
{
    internal static void Run()
    {
        var markdown = SpeechTextFormatter.Format("# Status\n\n**Oxygen** and *food*, ~~old~~ and `**literal**`.");
        Require(markdown.PlainText == "Status\nOxygen and food, old and **literal**.", "Markdown text was corrupted");
        Require(markdown.RichText.Contains("<b>") && markdown.RichText.Contains("<i>")
            && markdown.RichText.Contains("<s>") && markdown.RichText.Contains("<mspace=0.6em>"), "Markdown styles were not rendered");
        var native = SpeechTextFormatter.Format("<color=#ff0000><b>" + new string('x', 240) + "</b></color>");
        Require(native.PlainText.Length == 220 && native.PlainText.EndsWith("…"), "Visible truncation limit changed");
        Require(native.RichText.EndsWith("</b></color>…"), "Truncation left unbalanced native tags");
        var longTag = SpeechTextFormatter.Format("<color=#123456><b>short</b></color>", 10);
        Require(longTag.PlainText == "short", "Formatting syntax consumed the visible budget");
        var escaped = SpeechTextFormatter.Format(@"\*literal\* and `<b>x</b>` and &lt;safe&gt;");
        Require(escaped.PlainText == "*literal* and <b>x</b> and <safe>", "Escapes or code were interpreted as markup");
        Require(!escaped.RichText.Contains("<b>x</b>"), "Code injected TMP formatting");
        var literal = SpeechTextFormatter.Format("`</NoPaRsE><sprite=5>`");
        Require(literal.RichText.Contains("<\u200B/NoPaRsE>") && literal.RichText.Contains("<\u200Bsprite=5>"), "Literal text escaped its noparse boundary");
        var emoji = SpeechTextFormatter.Format("**🙂🙂🙂🙂🙂**", 4);
        Require(new StringInfo(emoji.PlainText).LengthInTextElements == 4, "Truncation split a Unicode character");
        var list = SpeechTextFormatter.Format("3. **First**\n4. Second\n\n[Guide](https://example.com)\n\n> Pause first");
        Require(list.PlainText.Contains("3. First\n4. Second") && list.PlainText.Contains("Guide")
            && list.PlainText.Contains("│ Pause first"), "Lists, links or quotes were lost");
        var code = SpeechTextFormatter.Format("```cs\n<b>raw</b> **code**\n```");
        Require(code.PlainText.Contains("<b>raw</b> **code**"), "Fenced code lost literal content");
        var table = SpeechTextFormatter.Format("| A | B |\n| --- | --- |\n| 1 | 2 |");
        Require(table.PlainText.Contains("A | B") && table.PlainText.Contains("1 | 2"), "Table text disappeared");
        Require(SpeechTextFormatter.Format("<size=9999>large</size>").RichText.Contains("<\u200Bsize=9999>"), "Unbounded size was rendered");
        Console.WriteLine("PASS speech Markdown, native rich text, literal code, Unicode and truncation");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
