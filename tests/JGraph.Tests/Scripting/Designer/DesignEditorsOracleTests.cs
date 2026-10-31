using System.Globalization;
using System.Text.Json;
using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// The App Designer window plan's stage W7c: the inspector's editors' tables, View Format, the names of new rows, a property's
/// mode and a component sized to its text, against R2025b's own <c>UniqueNameGenerator</c>, <c>ItemsModel</c>,
/// <c>TickDialogTableModel</c>, <c>ScaleColorsModel</c>, <c>DisplayFormatMode</c>, <c>UITableColumnEditorModel</c>, <c>DateModel</c>,
/// <c>PropertyCommandFactory</c> and <c>setComponentToPreferredSize</c>, run by <c>ui-probes/w7/oracle/editors.js</c> (DesignEditorsOracle.json).
/// </summary>
public class DesignEditorsOracleTests
{
    private static JsonElement Oracle(string part)
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "DesignEditorsOracle.json")));
        return oracle.RootElement.GetProperty(part).Clone();
    }

    private static string[] Strings(JsonElement e) => e.EnumerateArray().Select(static x => x.GetString()!).ToArray();

    private static double Number(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.Null => double.NaN,
        JsonValueKind.String when e.GetString() == "Inf" => double.PositiveInfinity,
        JsonValueKind.String when e.GetString() == "-Inf" => double.NegativeInfinity,
        _ => throw new ArgumentException(e.ToString()),
    };

    private static double[] Numbers(JsonElement e) => e.EnumerateArray().Select(Number).ToArray();

    // A JSON value and what the model says, as the same text.
    private static string Say(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s + "\"",
        bool b => b ? "true" : "false",
        int i => i.ToString(CultureInfo.InvariantCulture),
        double d => double.IsNaN(d) ? "null" : d.ToString("R", CultureInfo.InvariantCulture),
        double[] row => "[" + string.Join(",", row.Select(x => Say(x))) + "]",
        System.Collections.IEnumerable list => "[" + string.Join(",", list.Cast<object?>().Select(Say)) + "]",
        _ => throw new ArgumentException(value.GetType().Name),
    };

    private static string Say(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Null => "null",
        JsonValueKind.String => "\"" + e.GetString() + "\"",
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => e.GetDouble().ToString("R", CultureInfo.InvariantCulture),
        JsonValueKind.Array => "[" + string.Join(",", e.EnumerateArray().Select(Say)) + "]",
        _ => throw new ArgumentException(e.ToString()),
    };

    private static string SayCommits(IEnumerable<DesignEditorCommit> commits) =>
        "[" + string.Join(",", commits.Select(c => "[\"" + c.Property + "\"," + Say(c.After) + "]")) + "]";

    [Fact]
    public void TheNamesOfNewRowsAreR2025bs()
    {
        foreach (JsonElement scene in Oracle("names").EnumerateArray())
        {
            string[] items = Strings(scene.GetProperty("items"));
            string name = DesignItemNames.Next(items, DesignItemNames.DefaultName(scene.GetProperty("key").GetString()!));
            Assert.True(scene.GetProperty("name").GetString() == name, $"[{string.Join("|", items)}] {scene.GetProperty("key")}: R2025b {scene.GetProperty("name")}, here {name}");
        }
    }

    [Fact]
    public void TheItemsTableIsR2025bs()
    {
        foreach (JsonElement scene in Oracle("items").EnumerateArray())
        {
            JsonElement i = scene.GetProperty("info");
            var info = new DesignEditorInfo
            {
                MinNumber = i.TryGetProperty("minNumber", out JsonElement min) ? min.GetDouble() : null,
                MaxNumber = i.TryGetProperty("maxNumber", out JsonElement max) ? max.GetDouble() : null,
                DefaultNameKey = i.TryGetProperty("defaultNameKey", out JsonElement key) ? key.GetString()! : string.Empty,
            };
            var model = new DesignItemsModel(Strings(scene.GetProperty("items")), scene.GetProperty("selected").GetInt32(), info);
            foreach (JsonElement op in scene.GetProperty("ops").EnumerateArray())
            {
                int row = op.GetProperty("row").GetInt32();
                string kind = op.GetProperty("op").GetString()!;
                IReadOnlyList<DesignEditorCommit> commits;
                int toSelect = -2;
                switch (kind)
                {
                    case "add":
                        commits = model.AddRow(row, out toSelect);
                        break;
                    case "delete":
                        commits = model.DeleteRow(row, out toSelect);
                        break;
                    case "edit":
                        commits = model.SetItem(row, op.GetProperty("text").GetString()!);
                        break;
                    default:
                        commits = model.Select(row);
                        break;
                }

                Assert.Equal(Strings(op.GetProperty("items")), model.Items);
                Assert.Equal(op.GetProperty("selected").GetInt32(), model.SelectedIndex);
                Assert.Equal(Say(op.GetProperty("commits")), SayCommits(commits));
                Assert.Equal(op.GetProperty("canAdd").GetBoolean(), model.CanAdd);
                Assert.Equal(op.GetProperty("canDelete").GetBoolean(), model.CanDelete(0));
                if (op.TryGetProperty("toSelect", out JsonElement selects))
                {
                    Assert.Equal(selects.GetInt32(), toSelect);
                }
            }
        }
    }

    [Fact]
    public void TheTicksTableIsR2025bs()
    {
        foreach (JsonElement scene in Oracle("ticks").EnumerateArray())
        {
            var model = new DesignTicksModel(
                "MajorTicks",
                "MajorTickLabels",
                Numbers(scene.GetProperty("ticks")),
                Strings(scene.GetProperty("labels")),
                Numbers(scene.GetProperty("limits")),
                "auto",
                scene.GetProperty("labelMode").GetString()!);
            foreach (JsonElement op in scene.GetProperty("ops").EnumerateArray())
            {
                int row = op.GetProperty("row").GetInt32();
                switch (op.GetProperty("op").GetString())
                {
                    case "add":
                        model.AddRow(row);
                        break;
                    case "delete":
                        model.DeleteRow(row);
                        break;
                    case "tick":
                        model.EditTick(row, op.GetProperty("text").GetString()!);
                        break;
                    case "label":
                        model.EditLabel(row, op.GetProperty("text").GetString()!);
                        break;
                    default:
                        model.SetInterval(op.GetProperty("step").GetDouble());
                        break;
                }

                string said = "[" + string.Join(",", model.Rows.Select(r => "[" + Say(r.Tick) + "," + Say(r.Label) + "]")) + "]";
                Assert.Equal(Say(op.GetProperty("rows")), said);
                Assert.Equal(Say(op.GetProperty("commits")), SayCommits(model.Commits));
                Assert.Equal(op.GetProperty("error").GetString(), model.Error);
                JsonElement interval = op.GetProperty("interval2");
                Assert.Equal(interval.ValueKind == JsonValueKind.String ? null : Number(interval), model.CurrentInterval);
                Assert.Equal(op.GetProperty("toSelect").GetInt32(), model.RowToSelect);
            }
        }
    }

    [Fact]
    public void TheScaleColorsTableIsR2025bs()
    {
        foreach (JsonElement scene in Oracle("scale").EnumerateArray())
        {
            double[][] Rows(JsonElement e) => e.EnumerateArray().Select(Numbers).ToArray();
            var model = new DesignScaleColorsModel(Rows(scene.GetProperty("colors")), Rows(scene.GetProperty("limits")), Numbers(scene.GetProperty("range")));
            foreach (JsonElement op in scene.GetProperty("ops").EnumerateArray())
            {
                int row = op.GetProperty("row").GetInt32();
                IReadOnlyList<DesignEditorCommit>? commits;
                string? refused = null;
                switch (op.GetProperty("op").GetString())
                {
                    case "add":
                        commits = model.AddRow(row, out _);
                        break;
                    case "delete":
                        commits = model.DeleteRow(row, out _);
                        break;
                    case "color":
                        commits = model.SetColor(row, Numbers(op.GetProperty("color")));
                        break;
                    case "start":
                        commits = model.SetStart(row, op.GetProperty("text").GetString()!, out refused);
                        break;
                    default:
                        commits = model.SetEnd(row, op.GetProperty("text").GetString()!, out refused);
                        break;
                }

                Assert.Equal(op.TryGetProperty("threw", out JsonElement threw) ? threw.GetString() : null, refused);
                Assert.Equal(Say(op.GetProperty("colors")), Say(model.Colors));
                Assert.Equal(Say(op.GetProperty("limits")), Say(model.Limits));
                Assert.Equal(Say(op.GetProperty("commits")), SayCommits(commits ?? []));
            }
        }
    }

    [Fact]
    public void ViewFormatIsR2025bs()
    {
        JsonElement format = Oracle("format");
        foreach (JsonElement read in format.GetProperty("reads").EnumerateArray())
        {
            (DesignFormatMode mode, string? value) = DesignDisplayFormat.Read(read.GetProperty("text").GetString()!);
            Assert.Equal(read.GetProperty("mode").GetString(), mode.ToString().ToUpperInvariant());
            Assert.Equal(read.GetProperty("value").GetString(), value);
        }

        foreach (JsonElement write in format.GetProperty("writes").EnumerateArray())
        {
            DesignFormatMode mode = Enum.Parse<DesignFormatMode>(write.GetProperty("mode").GetString()!, ignoreCase: true);
            string? written = DesignDisplayFormat.Write(mode, write.GetProperty("value").GetString());
            Assert.Equal(write.GetProperty("format").GetString(), written);
            Assert.Equal(write.GetProperty("threw").ValueKind != JsonValueKind.Null, written is null);
        }

        Assert.Equal("Only positive whole numbers are allowed.", format.GetProperty("writes").EnumerateArray().First(w => w.GetProperty("threw").ValueKind != JsonValueKind.Null).GetProperty("threw").GetString());
    }

    [Fact]
    public void TheColumnsTableIsR2025bs()
    {
        foreach (JsonElement scene in Oracle("columns").EnumerateArray())
        {
            object[] Widths(JsonElement e) => e.EnumerateArray().Select(static w => w.ValueKind == JsonValueKind.Number ? (object)w.GetDouble() : w.GetString()!).ToArray();
            bool[] Flags(JsonElement e) => e.EnumerateArray().Select(static f => f.GetBoolean()).ToArray();
            bool hasSortable = scene.GetProperty("sortable").ValueKind != JsonValueKind.Null;
            var model = new DesignColumnsModel(
                Strings(scene.GetProperty("names")),
                Widths(scene.GetProperty("widths")),
                Flags(scene.GetProperty("editable")),
                hasSortable ? Flags(scene.GetProperty("sortable")) : null);
            foreach (JsonElement op in scene.GetProperty("ops").EnumerateArray())
            {
                int row = op.GetProperty("row").GetInt32();
                int toSelect = -2;
                IReadOnlyList<DesignEditorCommit> commits = op.GetProperty("op").GetString() switch
                {
                    "add" => model.AddRow(row, out toSelect),
                    "delete" => model.DeleteRow(row, out toSelect),
                    "name" => model.SetName(row, op.GetProperty("text").GetString()!),
                    "width" => model.SetWidth(row, op.GetProperty("text").GetString()!),
                    "auto" => model.SetAuto(row, op.GetProperty("on").GetBoolean()),
                    "editable" => model.SetEditable(row, op.GetProperty("on").GetBoolean()),
                    _ => model.SetSortable(row, op.GetProperty("on").GetBoolean()),
                };
                Assert.Equal(Strings(op.GetProperty("names")), model.Names);
                Assert.Equal(Say(op.GetProperty("widths")), Say(model.Widths));
                Assert.Equal(Say(op.GetProperty("editable")), Say(model.Editable));
                if (hasSortable)
                {
                    Assert.Equal(Say(op.GetProperty("sortable")), Say(model.Sortable));
                }

                Assert.Equal(Say(op.GetProperty("commits")), SayCommits(commits));
                if (op.TryGetProperty("toSelect", out JsonElement selects))
                {
                    Assert.Equal(selects.GetInt32(), toSelect);
                }

                JsonElement cells = op.GetProperty("cells");
                for (int i = 0; i < model.Names.Count; i++)
                {
                    JsonElement cell = cells[i][0];
                    Assert.Equal(cell.ValueKind == JsonValueKind.Number ? DesignDocument.NumberText(cell.GetDouble()) : cell.GetString(), model.WidthCell(i));
                    Assert.Equal(cells[i][1].GetBoolean(), model.IsAuto(i));
                }
            }
        }
    }

    [Fact]
    public void TheDatesTableIsR2025bs()
    {
        foreach (JsonElement scene in Oracle("dates").EnumerateArray())
        {
            DesignDate[] Dates(JsonElement e) => e.EnumerateArray()
                .Select(static d => new DesignDate(d.GetProperty("Year").GetInt32(), d.GetProperty("Month").GetInt32(), d.GetProperty("Day").GetInt32())).ToArray();
            var model = new DesignDatesModel(Dates(scene.GetProperty("dates")));
            foreach (JsonElement op in scene.GetProperty("ops").EnumerateArray())
            {
                int row = op.GetProperty("row").GetInt32();
                if (op.GetProperty("op").GetString() == "add")
                {
                    Assert.Equal(op.GetProperty("toSelect").GetInt32(), model.AddRow(row, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)));
                }
                else
                {
                    model.DeleteRow(row);
                }

                Assert.Equal(Dates(op.GetProperty("dates")), model.Dates);
            }
        }
    }

    [Fact]
    public void AModeBesideItsPropertyIsR2025bs()
    {
        DesignValue Value(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.Number => NumericValue.Scalar(e.GetDouble()),
            JsonValueKind.String => new CharValue(e.GetString()!),
            JsonValueKind.Array when e.GetArrayLength() == 0 => new NumericValue(0, 0, []),
            JsonValueKind.Array when e[0].ValueKind == JsonValueKind.Number => NumericValue.Row(Numbers(e)),
            _ => CellValue.Row(Strings(e).Select(static s => (DesignValue)new CharValue(s)).ToArray()),
        };
        foreach (JsonElement scene in Oracle("modes").EnumerateArray())
        {
            if (!scene.GetProperty("withMode").GetBoolean())
            {
                Assert.Equal(JsonValueKind.Null, scene.GetProperty("mode").ValueKind);
                continue;
            }

            string mode = DesignPropertyMode.For(Value(scene.GetProperty("base")), Value(scene.GetProperty("value")));
            Assert.True(scene.GetProperty("mode").GetString() == mode, $"{scene}: here {mode}");
        }
    }

    [Fact]
    public void AComponentSizedToItsTextIsR2025bs()
    {
        Size2D Size(JsonElement e) => e.ValueKind == JsonValueKind.Array
            ? new Size2D(e[0].GetDouble(), e[1].GetDouble())
            : new Size2D(e.GetProperty("width").GetDouble(), e.GetProperty("height").GetDouble());
        foreach (JsonElement scene in Oracle("preferred").EnumerateArray())
        {
            Size2D shown = Size(scene.GetProperty("shown"));
            JsonElement previous = scene.GetProperty("previous");
            JsonElement at = scene.GetProperty("location");
            (Size2D size, Point2D location) = DesignPreferredSize.Fit(
                new DesignPreferredRule(scene.GetProperty("preserveWidth").GetBoolean(), scene.GetProperty("preserveMinimum").GetBoolean()),
                Size(scene.GetProperty("minimum")),
                Size(scene.GetProperty("made")),
                Size(scene.GetProperty("preferred")),
                previous.ValueKind == JsonValueKind.Null ? null : Size(previous),
                shown,
                shown,
                new Point2D(at[0].GetDouble(), at[1].GetDouble()),
                scene.GetProperty("alignment").GetString()!,
                scene.GetProperty("wrap").GetBoolean());
            Assert.True(
                Size(scene.GetProperty("size")).Equals(size) && scene.GetProperty("at")[0].GetDouble() == location.X && scene.GetProperty("at")[1].GetDouble() == location.Y,
                $"{scene}: here {size} at {location}");
        }
    }

    [Fact]
    public void FieldsAreRefusedAsR2025bRefusesThem()
    {
        var info = new DesignEditorInfo { Fields = ["min", "max"], Validation = ["maxGreaterThanMin"] };
        foreach (JsonElement scene in Oracle("fields").EnumerateArray())
        {
            Assert.Equal(
                scene.GetProperty("refused").GetBoolean(),
                DesignFieldsEditor.Refused(info, [Number(scene.GetProperty("min")), Number(scene.GetProperty("max"))]));
        }
    }
}
