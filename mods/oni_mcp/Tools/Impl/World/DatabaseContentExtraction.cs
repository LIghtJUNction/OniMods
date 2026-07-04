using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class DatabaseTools
    {
        private static List<string> ExtractContent(IEnumerable<ContentContainer> containers)
        {
            var lines = new List<string>();
            if (containers == null)
                return lines;

            foreach (var container in containers)
            {
                if (container == null || container.content == null)
                    continue;

                foreach (ICodexWidget widget in container.content)
                    AddWidgetText(lines, widget);
            }

            return lines
                .Select(CleanText)
                .Where(line => !string.IsNullOrEmpty(line))
                .Distinct()
                .ToList();
        }

        private static void AddWidgetText(List<string> lines, ICodexWidget widget)
        {
            if (widget == null)
                return;

            var text = widget as CodexText;
            if (text != null)
            {
                Add(lines, text.text);
                Add(lines, text.stringKey);
                return;
            }

            var tooltip = widget as CodexTextWithTooltip;
            if (tooltip != null)
            {
                Add(lines, tooltip.text);
                Add(lines, tooltip.tooltip);
                Add(lines, tooltip.stringKey);
                return;
            }

            var labelWithIcon = widget as CodexLabelWithIcon;
            if (labelWithIcon != null)
            {
                Add(lines, labelWithIcon.label != null ? labelWithIcon.label.text : null);
                Add(lines, labelWithIcon.stringKey);
                return;
            }

            var indentedLabel = widget as CodexIndentedLabelWithIcon;
            if (indentedLabel != null)
            {
                Add(lines, indentedLabel.label != null ? indentedLabel.label.text : null);
                Add(lines, indentedLabel.stringKey);
                return;
            }

            var largeIcon = widget as CodexLabelWithLargeIcon;
            if (largeIcon != null)
                Add(lines, largeIcon.linkID);

            var video = widget as CodexVideo;
            if (video != null)
            {
                Add(lines, video.name);
                Add(lines, video.videoName);
                Add(lines, video.overlayName);
                if (video.overlayTexts != null)
                    foreach (string overlayText in video.overlayTexts)
                        Add(lines, overlayText);
                return;
            }

            AddReflectiveStrings(lines, widget);
        }

        private static void AddReflectiveStrings(List<string> lines, object obj)
        {
            Type type = obj.GetType();
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (property.GetIndexParameters().Length != 0)
                    continue;
                if (property.PropertyType == typeof(string))
                    AddStringProperty(lines, obj, property);
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.FieldType == typeof(string))
                    AddStringField(lines, obj, field);
            }
        }

        private static void AddStringProperty(List<string> lines, object obj, PropertyInfo property)
        {
            try
            {
                Add(lines, property.GetValue(obj, null) as string);
            }
            catch
            {
            }
        }

        private static void AddStringField(List<string> lines, object obj, FieldInfo field)
        {
            try
            {
                Add(lines, field.GetValue(obj) as string);
            }
            catch
            {
            }
        }

        private static void Add(List<string> lines, string value)
        {
            value = CleanText(value);
            if (!string.IsNullOrEmpty(value))
                lines.Add(value);
        }
    }
}
