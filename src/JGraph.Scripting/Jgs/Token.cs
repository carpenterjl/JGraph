namespace JGraph.Scripting.Jgs;

/// <summary>The lexical categories the <see cref="Lexer"/> produces.</summary>
internal enum TokenType
{
    // Literals and identifiers.
    Number,
    ImaginaryNumber, // 2i / 1.5i / 3j — the numeric text without the suffix is in Token.Number
    String,
    Identifier,

    // Keywords.
    Let,
    Fn,
    Return,
    If,
    Else,
    For,
    While,
    In,
    Break,
    Continue,
    True,
    False,
    End,     // MATLAB block terminator, and "last index" inside an index expression
    ElseIf,  // MATLAB 'elseif'

    // Punctuation and operators.
    LParen,
    RParen,
    LBrace,
    RBrace,
    LBracket,
    RBracket,
    Comma,
    Assign,       // =
    PlusAssign,   // +=
    MinusAssign,  // -=
    StarAssign,   // *=
    SlashAssign,  // /=
    PercentAssign, // %=
    PlusPlus,     // ++
    MinusMinus,   // --
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    Caret,        // ^ (also the target of '.^')
    Colon,        // : (MATLAB range, and 'all' inside an index)
    Bang,         // !
    EqualEqual,   // ==
    BangEqual,    // !=
    Less,         // <
    LessEqual,    // <=
    Greater,      // >
    GreaterEqual, // >=
    AmpAmp,       // &&
    PipePipe,     // ||

    /// <summary>A statement separator (a significant newline).</summary>
    Newline,

    /// <summary>';' — a statement separator that also suppresses console echo (and separates array rows).</summary>
    Semicolon,

    /// <summary>End of input.</summary>
    Eof,
}

/// <summary>
/// A single lexical token with its source position. <see cref="Text"/> holds the raw lexeme (or the decoded
/// value for strings); <see cref="Number"/> holds the parsed value for numeric tokens.
/// </summary>
internal readonly record struct Token(TokenType Type, string Text, double Number, int Line, int Column)
{
    /// <summary>A short human-readable description of the token, for error messages.</summary>
    public string Describe() => Type switch
    {
        TokenType.Eof => "end of input",
        TokenType.Newline => "end of line",
        TokenType.Number => $"number '{Text}'",
        TokenType.ImaginaryNumber => $"number '{Text}'",
        TokenType.String => "string",
        TokenType.Identifier => $"'{Text}'",
        _ => $"'{Text}'",
    };
}
