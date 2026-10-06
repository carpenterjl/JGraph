using System.Globalization;
using System.Text;
using System.Text.Json;
using JGraph.Core.Model;
using JGraph.Scripting;

namespace JGraph.Tests.Scripting;

/// <summary>
/// A stand-in for a browser that knows one page: <c>u9b_echo.html</c> (U9b), the page the parity
/// fixtures and the probes drive. It plays the page and JGraph's bridge script together: on a load,
/// <c>setup</c> sees the model's Data and the page says <c>ready</c>; a DataChanged follows when that
/// Data is not <c>[]</c>; it answers <c>ping</c>, <c>set</c> and <c>send</c> as the page does. Values
/// go through a browser's <c>JSON.parse</c> and <c>JSON.stringify</c>, so what comes back is what
/// Chromium writes (shortest numbers, <c>1e+21</c>, NaN as <c>null</c>, the last of two equal names).
/// Any other page has no listeners, and hears nothing.
/// </summary>
internal sealed class EchoHtmlPageHost : IUiHtmlPageHost
{
    public const string EchoFile = "u9b_echo.html";

    public IUiHtmlPage? Open(UiHtmlModel html) => new Page(html);

    private sealed class Page : IUiHtmlPage
    {
        private readonly UiHtmlModel _html;
        private bool _echo;
        private bool _disposed;
        private object? _data;

        public Page(UiHtmlModel html)
        {
            _html = html;
            html.MessagesPosted += OnPosted;
            html.SourceChanged += OnSourceChanged;
            Load();
        }

        public bool InWindow => false;

        public void Dispose()
        {
            _disposed = true;
            _html.MessagesPosted -= OnPosted;
            _html.SourceChanged -= OnSourceChanged;
            _html.Detach(this);
        }

        private void OnSourceChanged(UiHtmlModel html) => Load();

        private void Load()
        {
            string? file = _html.SourceFile;
            _echo = file is not null && Path.GetFileName(file).Equals(EchoFile, StringComparison.OrdinalIgnoreCase);
            if (!_echo)
            {
                _html.TakePosted();
                return;
            }

            _data = Js.Parse(_html.DataJson);
            Send("ready", Js.Text(_data));
            if (_html.DataJson != UiHtmlModel.EmptyJson)
            {
                Send("echo", Js.Text(_data));
            }

            Deliver(_html.TakePostedAfterLoad());
        }

        private void OnPosted(UiHtmlModel html)
        {
            if (_disposed)
            {
                return;
            }

            IReadOnlyList<UiHtmlMessage> messages = html.TakePosted();
            if (_echo)
            {
                Deliver(messages);
            }
        }

        private void Deliver(IReadOnlyList<UiHtmlMessage> messages)
        {
            foreach (UiHtmlMessage message in messages)
            {
                if (message.EventName is null)
                {
                    _data = Js.Parse(message.Json ?? UiHtmlModel.EmptyJson);
                    Send("echo", Js.Text(_data));
                    continue;
                }

                object? data = message.Json is null ? Js.Undefined : Js.Parse(message.Json);
                switch (message.EventName)
                {
                    case "ping":
                        Send("pong", Js.Text(data));
                        break;
                    case "set" when data is string text:
                        SetData(Js.Special(text, out object? special) ? special : Js.Parse(text));
                        Send("setdone", Js.Text(_data));
                        break;
                    case "send" when data is Js.Obj d:
                        object? name = d.Get("name");
                        object? json = d.Get("json");
                        if (json is string t)
                        {
                            SendRaw(name, Js.Special(t, out object? s) ? s : Js.Parse(t));
                        }
                        else
                        {
                            SendRaw(name, Js.Undefined);
                        }

                        break;
                }
            }
        }

        // The bridge: a Data the page sets goes to MATLAB as its JSON, unless JSON has none for it.
        private void SetData(object? value)
        {
            _data = value;
            if (Js.Stringify(value) is { } json)
            {
                ScriptGraphicsCallbacks.NotifyComponent(_html, "htmldata", json);
            }
        }

        private void Send(string name, object? data) => SendRaw(name, data);

        private void SendRaw(object? name, object? data)
        {
            if (Js.Stringify(name) is not { } nameJson)
            {
                return; // R2025b's bridge cannot read an event with no name
            }

            ScriptGraphicsCallbacks.NotifyComponent(_html, "htmlevent", new UiHtmlEvent(nameJson, Js.Stringify(data)));
        }
    }

    /// <summary>The few JavaScript values JSON has, and a browser's JSON.parse and JSON.stringify of them.</summary>
    private static class Js
    {
        public static readonly object Undefined = new();

        public sealed class Obj
        {
            public readonly List<(string Name, object? Value)> Members = [];

            public object? Get(string name) => Members.FindIndex(m => m.Name == name) is int at and >= 0 ? Members[at].Value : Undefined;

            public void Set(string name, object? value)
            {
                int at = Members.FindIndex(m => m.Name == name);
                if (at >= 0)
                {
                    Members[at] = (name, value);
                }
                else
                {
                    Members.Add((name, value));
                }
            }
        }

        public static bool Special(string text, out object? value)
        {
            value = text switch
            {
                "__undefined__" => Undefined,
                "__nan__" => double.NaN,
                "__inf__" => double.PositiveInfinity,
                "__ninf__" => double.NegativeInfinity,
                _ => null,
            };
            return text is "__undefined__" or "__nan__" or "__inf__" or "__ninf__";
        }

        public static object? Parse(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false });
            return From(document.RootElement);
        }

        private static object? From(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray().Select(From).ToList(),
            JsonValueKind.Object => ObjectFrom(element),
            _ => null,
        };

        private static Obj ObjectFrom(JsonElement element)
        {
            var obj = new Obj();
            foreach (JsonProperty property in element.EnumerateObject())
            {
                obj.Set(property.Name, From(property.Value));
            }

            return obj;
        }

        /// <summary>The page's text(v): '__undefined__' for undefined, else JSON.stringify(v).</summary>
        public static string Text(object? value) => Stringify(value) ?? "__undefined__";

        /// <summary>JSON.stringify: null for undefined, which it does not write.</summary>
        public static string? Stringify(object? value)
        {
            if (ReferenceEquals(value, Undefined))
            {
                return null;
            }

            var text = new StringBuilder();
            Write(text, value);
            return text.ToString();
        }

        private static void Write(StringBuilder text, object? value)
        {
            switch (value)
            {
                case null:
                    text.Append("null");
                    break;
                case bool b:
                    text.Append(b ? "true" : "false");
                    break;
                case double d:
                    text.Append(Number(d));
                    break;
                case string s:
                    Quote(text, s);
                    break;
                case List<object?> items:
                    text.Append('[');
                    for (int i = 0; i < items.Count; i++)
                    {
                        text.Append(i > 0 ? "," : string.Empty);
                        Write(text, ReferenceEquals(items[i], Undefined) ? null : items[i]);
                    }

                    text.Append(']');
                    break;
                case Obj obj:
                    text.Append('{');
                    bool first = true;
                    foreach ((string name, object? member) in obj.Members)
                    {
                        if (ReferenceEquals(member, Undefined))
                        {
                            continue;
                        }

                        text.Append(first ? string.Empty : ",");
                        first = false;
                        Quote(text, name);
                        text.Append(':');
                        Write(text, member);
                    }

                    text.Append('}');
                    break;
                default:
                    text.Append("null");
                    break;
            }
        }

        /// <summary>A number as JavaScript writes it: the shortest digits, exponential from 1e21 and below 1e-6.</summary>
        public static string Number(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                return "null";
            }

            if (d == 0)
            {
                return "0";
            }

            // The shortest digits and where the decimal point falls among them (ECMAScript's n).
            bool negative = d < 0;
            string s = Math.Abs(d).ToString("R", CultureInfo.InvariantCulture);
            int exponent = 0;
            int at = s.IndexOf('E');
            if (at >= 0)
            {
                exponent = int.Parse(s[(at + 1)..], CultureInfo.InvariantCulture);
                s = s[..at];
            }

            int dot = s.IndexOf('.');
            string whole = dot < 0 ? s : s[..dot];
            string all = whole + (dot < 0 ? string.Empty : s[(dot + 1)..]);
            int lead = 0;
            while (lead < all.Length - 1 && all[lead] == '0')
            {
                lead++;
            }

            string digits = all[lead..].TrimEnd('0');
            if (digits.Length == 0)
            {
                digits = "0";
            }

            int position = whole.Length - lead + exponent;

            string sign = negative ? "-" : string.Empty;
            int n = position;
            int k = digits.Length;
            if (k <= n && n <= 21)
            {
                return sign + digits + new string('0', n - k);
            }

            if (0 < n && n <= 21)
            {
                return sign + digits[..n] + "." + digits[n..];
            }

            if (-6 < n && n <= 0)
            {
                return sign + "0." + new string('0', -n) + digits;
            }

            string exp = (n - 1 >= 0 ? "+" : "-") + Math.Abs(n - 1).ToString(CultureInfo.InvariantCulture);
            return k == 1 ? $"{sign}{digits}e{exp}" : $"{sign}{digits[0]}.{digits[1..]}e{exp}";
        }

        private static void Quote(StringBuilder text, string s)
        {
            text.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\f': text.Append("\\f"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            text.Append(c);
                        }

                        break;
                }
            }

            text.Append('"');
        }
    }
}
