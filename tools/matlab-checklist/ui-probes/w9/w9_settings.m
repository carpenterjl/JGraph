% Lists R2025b's editor settings headless, and tries the formatter behind indentcode.
s = settings;
show(s.matlab.editor, 'matlab.editor');
try
    show(s.matlab.keyboard, 'matlab.keyboard');
catch e
    disp(e.message);
end
disp(which('matlab.codeanalyzer.internal.formatCode'));
try
    t = matlab.codeanalyzer.internal.formatCode(sprintf('function y=f(x)\nif x\n  y=1;\nend\nend'), ...
        IndentMode='AllFunctionIndent', IndentSize=4, UseSpacesInsteadOfTabs=true, TabSize=4, BlankLineMode='RemoveWhitespace');
    disp(t);
catch e
    disp(e.message);
end
try
    t = matlab.codeanalyzer.internal.formatCode(sprintf('function y=f(x)\nif x\n  y=1;\nend\nend'), ...
        IndentMode='ClassicFunctionIndent', IndentSize=4, UseSpacesInsteadOfTabs=true, TabSize=4, BlankLineMode='RemoveWhitespace');
    disp(t);
catch e
    disp(e.message);
end
try
    t = matlab.codeanalyzer.internal.formatCode(sprintf('x = 1;\n  if x\n  y=1;\n   end\n   y = 2;'), ...
        IndentMode='AllFunctionIndent', IndentSize=4, UseSpacesInsteadOfTabs=true, TabSize=4, BlankLineMode='RemoveWhitespace', startLine=2, endLine=3);
    disp(t);
catch e
    disp(e.message);
end

function show(g, name)
try
    p = properties(g);
    for i = 1:numel(p)
        v = g.(p{i});
        if isa(v, 'matlab.settings.Setting')
            try
                a = v.ActiveValue;
                if isnumeric(a) || islogical(a)
                    a = mat2str(a);
                elseif isstring(a)
                    a = char(a);
                elseif ~ischar(a)
                    a = class(a);
                end
            catch
                a = '?';
            end
            fprintf('%s.%s = %s\n', name, p{i}, a);
        else
            show(v, [name '.' p{i}]);
        end
    end
catch e
    fprintf('%s: %s\n', name, e.message);
end
end
