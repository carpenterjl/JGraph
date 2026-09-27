using System.Globalization;
using System.Text;

namespace JGraph.Scripting.Jgs.Native;

/// <summary>
/// MATLAB's prototype file (ADR 0181): the M-file <c>loadlibrary(…, 'mfilename', name)</c> writes and
/// <c>loadlibrary(lib, @name)</c> reads. It is a function answering
/// <c>[methodinfo, structs, enuminfo, ThunkLibName]</c>: <c>methodinfo</c> a struct of parallel cells
/// (<c>name</c>, <c>calltype</c>, <c>LHS</c>, <c>RHS</c>, <c>alias</c>, <c>thunkname</c>), <c>structs</c>
/// a struct of <c>members</c> (and <c>packing</c>), <c>enuminfo</c> a struct of name-value structs.
/// </summary>
/// <remarks>
/// JGraph reads a file R2025b wrote as it is, and writes one in R2025b's layout. The thunk library
/// is not JGraph's: the native host calls every function through <c>calli</c>, so
/// <c>ThunkLibName</c> is ignored when read, and <c>thunkname</c> is kept only to be written back.
/// </remarks>
internal static class PrototypeFile
{
    /// <summary>
    /// The model a prototype file's four outputs describe, or the reason they do not describe one.
    /// </summary>
    public static LibraryModel Read(JgsValue methodInfo, JgsValue structs, JgsValue enumInfo)
    {
        var model = new LibraryModel();
        if (methodInfo.Type == JgsType.Struct && !methodInfo.IsStructArray)
        {
            Dictionary<string, JgsValue> fields = methodInfo.AsStruct;
            JgsValue[] names = CellOf(fields, "name");
            JgsValue[] callTypes = CellOf(fields, "calltype");
            JgsValue[] lhs = CellOf(fields, "LHS");
            JgsValue[] rhs = CellOf(fields, "RHS");
            JgsValue[] aliases = CellOf(fields, "alias");
            JgsValue[] thunks = CellOf(fields, "thunkname");
            for (int i = 0; i < names.Length; i++)
            {
                string name = Text(names[i]) ?? throw new FormatException($"methodinfo.name{{{i + 1}}} is not text.");
                string callType = At(callTypes, i) is { } c ? Text(c) ?? "cdecl" : "cdecl";
                string? returns = At(lhs, i) is { } l ? NonEmpty(Text(l)) : null;
                IReadOnlyList<string> parameters = At(rhs, i) is { Type: JgsType.Cell } p
                    ? p.AsCell.Select(v => Text(v) ?? "error").ToList()
                    : At(rhs, i) is { } single && Text(single) is { Length: > 0 } one ? [one] : [];
                string? alias = At(aliases, i) is { } a ? NonEmpty(Text(a)) : null;
                string? thunk = At(thunks, i) is { } t ? NonEmpty(Text(t)) : null;
                model.Functions.Add(new LibFunction(name, callType, returns, parameters, alias, thunk));
            }
        }

        if (structs.Type == JgsType.Struct && !structs.IsStructArray)
        {
            foreach ((string name, JgsValue definition) in structs.AsStruct)
            {
                if (definition.Type != JgsType.Struct || definition.IsStructArray)
                {
                    continue;
                }

                Dictionary<string, JgsValue> parts = definition.AsStruct;
                var members = new List<LibMember>();
                if (parts.TryGetValue("members", out JgsValue? memberValue) && memberValue.Type == JgsType.Struct && !memberValue.IsStructArray)
                {
                    foreach ((string member, JgsValue type) in memberValue.AsStruct)
                    {
                        members.Add(new LibMember(member, Text(type) ?? "error"));
                    }
                }

                int? packing = parts.TryGetValue("packing", out JgsValue? pack) && Number(pack) is { } p ? (int)p : null;
                model.Structs.Add(new LibStruct(name, members, packing));
            }
        }

        if (enumInfo.Type == JgsType.Struct && !enumInfo.IsStructArray)
        {
            foreach ((string name, JgsValue values) in enumInfo.AsStruct)
            {
                if (values.Type != JgsType.Struct || values.IsStructArray)
                {
                    continue;
                }

                model.Enums.Add(new LibEnum(name, values.AsStruct.Select(v => (v.Key, (long)(Number(v.Value) ?? 0))).ToList()));
            }
        }

        return model;
    }

    private static JgsValue[] CellOf(Dictionary<string, JgsValue> fields, string name) =>
        fields.TryGetValue(name, out JgsValue? value) && value.Type == JgsType.Cell ? value.AsCell : [];

    private static JgsValue? At(JgsValue[] values, int i) => i < values.Length ? values[i] : null;

    private static string? NonEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;

    private static string? Text(JgsValue value) =>
        value.Type == JgsType.String ? value.AsString
        : JgsBuiltins.IsTextScalar(value) ? JgsBuiltins.TextOf(value)
        : null;

    private static double? Number(JgsValue value) => value.Type switch
    {
        JgsType.Number or JgsType.Bool => value.AsNumber,
        JgsType.Array when value.ArrayLength == 1 => value.AsArray[0].AsNumber,
        _ => null,
    };

    /// <summary>
    /// The text of a prototype file named <paramref name="name"/> for <paramref name="model"/>, in
    /// R2025b's layout: a comment with each declaration, one line of <c>fcns</c> assignments per
    /// function, then the structs and enums. <paramref name="header"/> is the header's name without
    /// its extension, as the file's help line names it; <paramref name="library"/> is the name the
    /// library was loaded as, which R2025b names its thunk library after.
    /// </summary>
    public static string Write(LibraryModel model, string name, string header, string library, DateTime now)
    {
        var text = new StringBuilder();
        text.Append("function [methodinfo,structs,enuminfo,ThunkLibName]=").Append(name).Append('\n');
        text.Append('%').Append(name.ToUpperInvariant()).Append(" Create structures to define interfaces found in '").Append(header).Append("'.\n");
        text.Append('\n');
        text.Append("%This function was generated by JGraph's loadlibrary on ")
            .Append(now.ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture)).Append('\n');
        text.Append("ival={cell(1,0)}; % change 0 to the actual number of functions to preallocate the data.\n");
        text.Append("structs=[];enuminfo=[];fcnNum=1;\n");
        text.Append("fcns=struct('name',ival,'calltype',ival,'LHS',ival,'RHS',ival,'alias',ival,'thunkname', ival);\n");
        text.Append("MfilePath=fileparts(mfilename('fullpath'));\n");
        text.Append("ThunkLibName=fullfile(MfilePath,'").Append(library).Append("_thunk_pcwin64');\n");
        foreach (LibFunction function in model.Functions)
        {
            if (function.Declaration is { } declaration)
            {
                text.Append("%  ").Append(declaration).Append(" \n");
            }

            if (function.ThunkName is { } thunk)
            {
                text.Append("fcns.thunkname{fcnNum}=").Append(Quote(thunk)).Append(';');
            }

            text.Append("fcns.name{fcnNum}=").Append(Quote(function.Name)).Append("; ");
            text.Append("fcns.calltype{fcnNum}=").Append(Quote(function.CallType)).Append("; ");
            text.Append("fcns.LHS{fcnNum}=").Append(function.Lhs is { } lhs ? Quote(lhs) : "[]").Append("; ");
            text.Append("fcns.RHS{fcnNum}=")
                .Append(function.Rhs.Count == 0 ? "[]" : "{" + string.Join(", ", function.Rhs.Select(Quote)) + "}")
                .Append(';');
            if (function.Alias is { } alias)
            {
                text.Append("fcns.alias{fcnNum}=").Append(Quote(alias)).Append(';');
            }

            text.Append("fcnNum=fcnNum+1;\n");
        }

        foreach (LibStruct type in model.Structs)
        {
            if (type.Packing is { } packing)
            {
                text.Append("structs.").Append(type.Name).Append(".packing=").Append(packing.ToString(CultureInfo.InvariantCulture)).Append(";\n");
            }

            text.Append("structs.").Append(type.Name).Append(".members=struct(")
                .AppendJoin(", ", type.Members.Select(m => $"{Quote(m.Name)}, {Quote(m.Type)}"))
                .Append(");\n");
        }

        foreach (LibEnum type in model.Enums)
        {
            text.Append("enuminfo.").Append(type.Name).Append("=struct(")
                .AppendJoin(",", type.Members.Select(m => $"{Quote(m.Name)},{m.Value.ToString(CultureInfo.InvariantCulture)}"))
                .Append(");\n");
        }

        text.Append("methodinfo=fcns;");
        return text.ToString();
    }

    private static string Quote(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
}
