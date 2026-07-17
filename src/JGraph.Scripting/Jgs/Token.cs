namespace JGraph.Scripting.Jgs;

/// <summary>The lexical categories the <see cref="Lexer"/> produces.</summary>
internal enum TokenType
{
    // Literals and identifiers.
    Number,
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

    // Punctuation and operators.
    LParen,
    RParen,
    LBrace,
    RBrace,
    LBracket,
    RBracket,
    Comma,
    Assign,       // =
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    Bang,         // !
    EqualEqual,   // ==
    BangEqual,    // !=
    Less,         // <
    LessEqual,    // <=
    Greater,      // >
    GreaterEqual, // >=
    AmpAmp,       // &&
    PipePipe,     // ||

    /// <summary>A statement separator (a significant newline or ';').</summary>
    Newline,

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
        TokenType.String => "string",
        TokenType.Identifier => $"'{Text}'",
        _ => $"'{Text}'",
    };
}
