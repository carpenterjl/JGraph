using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.Designer;

/// <summary>The editor the inspector gives a managed property's default (R2025b's inferred renderer, <c>getInspectorRenderer</c>).</summary>
public enum DesignManagedEditor
{
    /// <summary>No editor: a value App Designer has none for (a struct, a date, a matrix cell, a string array), shown as information.</summary>
    None,

    /// <summary>A number or numeric matrix, typed as text.</summary>
    Number,

    /// <summary>A char row or a string scalar.</summary>
    Text,

    /// <summary>A logical scalar: a check box.</summary>
    Logical,

    /// <summary>A cell vector of items, one per line.</summary>
    Items,
}

/// <summary>What checking a managed property's default gave (<c>validateAndInferPropertyDetails</c>).</summary>
/// <param name="Problem">Why the default is refused, in MATLAB's words; null when it stands.</param>
/// <param name="ClassName">The class the default has, or the property declares.</param>
/// <param name="Value">The default as a design value, for the inspector's row; null for none or one no design value holds.</param>
/// <param name="Editor">The editor the inspector gives it.</param>
public sealed record DesignManagedCheck(string? Problem, string ClassName, DesignValue? Value, DesignManagedEditor Editor);

/// <content>
/// A custom UI component's managed properties checked as R2025b checks them (App Designer window plan W15,
/// <c>appdesigner.internal.usercomponent.validatePropertyValue</c> and <c>getInferredPropertyDetails</c>,
/// measured by the probe <c>w15_component</c>, section 5): the default evaluated, brought to the declared
/// class as MATLAB's property validation brings it (a char to a double's codes, a number to a logical),
/// its size held to the declaration, each validation function run over it; and the inspector's edit of a
/// default put into R2025b's canonical text.
/// </content>
public sealed partial class DesignRuntime
{
    /// <summary>Checks <paramref name="property"/>'s default against its class, size and validation functions.</summary>
    public DesignManagedCheck ValidateManagedProperty(DesignManagedProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        using Scope scope = Enter();
        return Check(property, property.DefaultValue);
    }

    /// <summary>
    /// The default text R2025b stores after the inspector's edit of <paramref name="property"/> to
    /// <paramref name="typed"/> (<c>validateInspectorPropertyValue</c>: a logical or a number as typed, text
    /// quoted, then <c>propertyValueToString</c> of the value), or why the edit is refused.
    /// </summary>
    public (string? Text, string? Problem) CanonicalDefault(DesignManagedProperty property, string typed)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(typed);
        using Scope scope = Enter();
        DesignManagedCheck current = Check(property, property.DefaultValue);
        string expression = current.Editor switch
        {
            DesignManagedEditor.Logical or DesignManagedEditor.Number or DesignManagedEditor.Items => typed.Trim(),
            _ => DesignValueText.CharText(typed),
        };
        DesignManagedCheck check = Check(property, expression);
        if (check.Problem is not null)
        {
            return (null, check.Problem);
        }

        return (check.Value is { } value ? DesignValueText.Format(value, DesignComponentCode.ObjectName) : expression, null);
    }

    private DesignManagedCheck Check(DesignManagedProperty property, string expression)
    {
        string className = property.DataType.Trim();
        if (expression.Trim().Length == 0)
        {
            // No default: nothing to check; the class is the declared one, and an untyped one has no editor.
            return new DesignManagedCheck(null, className, null, EditorOf(className, null));
        }

        if (Run($"{ValueVariable} = {expression};") is { } refused)
        {
            return new DesignManagedCheck(refused, className, null, DesignManagedEditor.None);
        }

        // MATLAB's validation order: class, then size, then the functions left to right.
        if (className.Length > 0 && Run(Conversion(className)) is { } notClass)
        {
            return new DesignManagedCheck(notClass, className, null, DesignManagedEditor.None);
        }

        if (!_environment.TryGet(ValueVariable, out JgsValue held))
        {
            return new DesignManagedCheck("The default value could not be read.", className, null, DesignManagedEditor.None);
        }

        if (className.Length == 0)
        {
            className = JgsBuiltins.ClassOf(held, JgsDialect.Matlab);
        }

        if (SizeProblem(property.Size, held) is { } wrongSize)
        {
            return new DesignManagedCheck(wrongSize, className, null, DesignManagedEditor.None);
        }

        foreach (DesignValidationFunction function in property.ValidationFunctions)
        {
            if (Run(function.Signature(ValueVariable) + ";") is { } failed)
            {
                return new DesignManagedCheck(failed, className, null, DesignManagedEditor.None);
            }
        }

        DesignValue? value = ValueOf(held, null);
        return new DesignManagedCheck(null, className, value, EditorOf(className, held));
    }

    /// <summary>The statement that brings the value to <paramref name="className"/> as a property's class validation does, or refuses.</summary>
    private static string Conversion(string className) => className switch
    {
        "double" => $"{ValueVariable} = double({ValueVariable});",
        "single" => $"{ValueVariable} = single({ValueVariable});",
        "char" => $"{ValueVariable} = char({ValueVariable});",
        "string" => $"{ValueVariable} = string({ValueVariable});",
        "logical" => $"{ValueVariable} = logical({ValueVariable});",
        "cell" => $"if ~iscell({ValueVariable}), error('MATLAB:validation:UnableToConvert', 'Value must be of type cell.'); end",
        _ => $"if ~isa({ValueVariable}, '{className}'), {ValueVariable} = {className}({ValueVariable}); end",
    };

    /// <summary>Why the value's size is not the declared <paramref name="size"/>, in MATLAB's words; null when it is, or none is declared.</summary>
    private static string? SizeProblem(IReadOnlyList<string> size, JgsValue value)
    {
        if (size.Count == 0)
        {
            return null;
        }

        int[] dims = value.Dims;
        int[] wanted = new int[Math.Max(size.Count, 2)];
        for (int i = 0; i < wanted.Length; i++)
        {
            wanted[i] = i < size.Count && size[i].Trim() != ":" && int.TryParse(size[i].Trim(), out int n) ? n : -1;
        }

        bool fits = true;
        for (int i = 0; i < wanted.Length; i++)
        {
            int have = i < dims.Length ? dims[i] : 1;
            if (wanted[i] >= 0 && have != wanted[i])
            {
                fits = false;
            }
        }

        if (fits && dims.Length <= wanted.Length)
        {
            return null;
        }

        if (wanted is [1, 1])
        {
            return "Value must be a scalar.";
        }

        if (wanted is [1, > 0] or [> 0, 1])
        {
            return $"Value must be a vector with {Math.Max(wanted[0], wanted[1])} elements.";
        }

        if (wanted is [1, -1])
        {
            return "Value must be a row vector.";
        }

        if (wanted is [-1, 1])
        {
            return "Value must be a column vector.";
        }

        return $"Value must be of size {string.Join("x", wanted.Select(static w => w < 0 ? ":" : w.ToString(System.Globalization.CultureInfo.InvariantCulture)))}.";
    }

    /// <summary>The editor for a class and value (<c>doesPropertyHaveValidEditor</c>, <c>getInspectorRenderer</c>): a cell as a vector, a string as a scalar, a char as a row; the four data types; anything else none.</summary>
    private static DesignManagedEditor EditorOf(string className, JgsValue? value) => className switch
    {
        "double" or "single" or "int8" or "int16" or "int32" or "int64" or "uint8" or "uint16" or "uint32" or "uint64" => DesignManagedEditor.Number,
        "logical" => DesignManagedEditor.Logical,
        "char" => value is null || value.IsCharRow() ? DesignManagedEditor.Text : DesignManagedEditor.None,
        "string" => value is null || value.IsStringScalar() ? DesignManagedEditor.Text : DesignManagedEditor.None,
        "cell" => value is null || value.IsVectorCell() ? DesignManagedEditor.Items : DesignManagedEditor.None,
        _ => DesignManagedEditor.None,
    };
}

/// <summary>Shape questions the checks ask of a value.</summary>
file static class ManagedValueShape
{
    public static bool IsCharRow(this JgsValue value) => value.Type == JgsType.String || (value.IsCharMatrix && value.Dims is [<= 1, _]);

    public static bool IsStringScalar(this JgsValue value) => value.IsStringArray ? value.ArrayLength == 1 : value.Type == JgsType.String;

    public static bool IsVectorCell(this JgsValue value) => value.Type == JgsType.Cell && value.Dims is [var rows, var cols] && (rows <= 1 || cols <= 1);
}
