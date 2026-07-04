using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static string GetMapHtml(string title, string backUrl, string backText, string description, int width, string cellsHtml, string legendHtml)
        {
            return @"<!doctype html>
        <html>
        <head>
        <meta charset=""utf-8"">
        <title>" + title + @"</title>
        <style>
          body { font-family: system-ui, -apple-system, sans-serif; background: #1a202c; color: #e2e8f0; margin: 20px; }
          .grid { display: grid; grid-template-columns: repeat(" + width + @", 10px); gap: 1px; background: #2d3748; padding: 10px; border-radius: 8px; overflow-x: auto; width: max-content; max-width: 100%; }
          .cell { width: 10px; height: 10px; border-radius: 1px; cursor: pointer; transition: transform 0.1s; }
          .cell:hover { transform: scale(1.5); z-index: 10; outline: 1px solid #fff; }
          .legend { display: flex; flex-wrap: wrap; gap: 12px; margin-top: 20px; background: #2d3748; padding: 15px; border-radius: 8px; font-size: 12px; max-width: 1100px; }
          .legend-item { display: flex; align-items: center; gap: 6px; }
          .legend-color { width: 14px; height: 14px; border-radius: 3px; }
          .back-btn { display: inline-block; margin-bottom: 20px; color: #63b3ed; text-decoration: none; font-weight: bold; }
          .toggle-btn { background: #4a5568; color: #fff; border: none; padding: 6px 12px; border-radius: 4px; cursor: pointer; font-weight: bold; font-size: 13px; margin-left: 12px; vertical-align: middle; }
          .toggle-btn:hover { background: #718096; }
          #raw-code-container { background: #2d3748; color: #f7fafc; padding: 16px; border-radius: 8px; overflow-x: auto; font-family: monospace; font-size: 13px; max-width: 1100px; white-space: pre-wrap; word-break: break-all; margin-top: 20px; display: none; }
        </style>
        </head>
        <body>
          <div style=""display: flex; align-items: center; justify-content: space-between; max-width: 1100px;"">
            <a class=""back-btn"" href=""" + backUrl + @""">" + backText + @"</a>
            <button id=""toggle-btn"" class=""toggle-btn"" onclick=""toggleRaw()"">Show Raw HTML</button>
          </div>
          <h1>" + title + @"</h1>
          <div id=""main-desc""><p>" + description + @"</p></div>

          <div class=""grid"">
        " + cellsHtml + @"
          </div>

          <pre id=""raw-code-container""><code id=""raw-code""></code></pre>

          <div class=""legend"">
        " + legendHtml + @"
          </div>

          <script>
            function escapeHtml(str) {
              return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/""/g, '&quot;');
            }
            function toggleRaw() {
              var grid = document.querySelector('.grid');
              var legend = document.querySelector('.legend');
              var raw = document.getElementById('raw-code-container');
              var btn = document.getElementById('toggle-btn');
              var desc = document.getElementById('main-desc');
              if (raw.style.display === 'none' || raw.style.display === '') {
        grid.style.display = 'none';
        if (legend) legend.style.display = 'none';
        raw.style.display = 'block';

        var code = document.documentElement.outerHTML;
        var lines = code.split('\n');
        var html = '';
        for (var i = 0; i < lines.length; i++) {
          var lineNum = i + 1;
          html += '<span style=""color:#718096;user-select:none;margin-right:12px;text-align:right;display:inline-block;width:30px;"">' + lineNum + '</span>' + escapeHtml(lines[i]) + '\n';
        }
        document.getElementById('raw-code').innerHTML = html;
        btn.textContent = 'Show Visual Map';
              } else {
        grid.style.display = 'grid';
        if (legend) legend.style.display = 'flex';
        raw.style.display = 'none';
        btn.textContent = 'Show Raw HTML';
              }
            }
          </script>
        </body>
        </html>";
        }

        private static string GetElementColor(Element elem)
        {
            if (elem == null) return "#1a202c";
            string name = elem.id.ToString().ToLowerInvariant();

            if (elem.IsSolid)
            {
                if (name.Contains("dirt")) return "#8c5b30";
                if (name.Contains("sandstone")) return "#a88060";
                if (name.Contains("algae")) return "#38a169";
                if (name.Contains("granite")) return "#718096";
                if (name.Contains("igneous")) return "#4a5568";
                if (name.Contains("copper")) return "#b7791f";
                if (name.Contains("coal")) return "#2d3748";
                if (name.Contains("iron")) return "#c53030";
                if (name.Contains("neutronium")) return "#000000";
                return "#718096";
            }
            if (elem.IsLiquid)
            {
                if (name.Contains("dirtywater") || name.Contains("pollutedwater")) return "#4a3728";
                if (name.Contains("water")) return "#3182ce";
                if (name.Contains("saltwater") || name.Contains("brine")) return "#4299e1";
                if (name.Contains("oil")) return "#2d3748";
                return "#3182ce";
            }
            if (name.Contains("oxygen")) return "#e6fffa";
            if (name.Contains("carbondioxide")) return "#feb2b2";
            if (name.Contains("hydrogen")) return "#faf089";
            if (name.Contains("chlorine")) return "#d69e2e";
            if (name.Contains("dirtyoxygen") || name.Contains("pollutedoxygen")) return "#ecc94b";
            return "#cbd5e0";
        }

    }
}
