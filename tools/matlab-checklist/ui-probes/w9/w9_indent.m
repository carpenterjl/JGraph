% W9b's Smart Indent oracle: what R2025b's formatter (matlab.codeanalyzer.internal.formatCode, which indentcode calls) makes of
% seeded, well-formed MATLAB programs with their indentation spoiled, in each indenting mode, with blank lines padded or emptied,
% over the whole text. Run headless:
%   matlab -noFigureWindows -batch "cd('<this folder>'); w9_indent" > ../../../../tests/JGraph.Tests/Scripting/Editing/SmartIndentOracle.json
rng(20261101, 'twister');
cases = {};
modes = {'MixedFunctionIndent', 'AllFunctionIndent', 'ClassicFunctionIndent'};
for n = 1:360
    kind = randi(5);
    switch kind
        case 1
            lines = program(0, 3);
        case 2
            lines = [{'function y = f(x)'}, argumentsBlock(), program(1, 3), {'end'}];
            if rand < 0.5
                lines = [lines, {''}, {'function helper(a, b)'}, argumentsBlock(), program(1, 2), {'end'}];
            end
        case 3
            % Functions without ends.
            lines = [{'function y = f(x)'}, program(1, 3), {''}, {'function z = g(x)'}, program(1, 2)];
        case 4
            % A nested function.
            lines = [{'function y = f(x)'}, program(1, 2), {'function r = inner(q)'}, program(2, 2), {'end'}, program(1, 1), {'end'}];
        otherwise
            lines = [{'classdef Thing < handle'}, {'properties'}, {'Value'}, {'Name = "x"'}, {'end'}];
            if rand < 0.4
                lines = [lines, {'events'}, {'Changed'}, {'end'}];
            end
            if rand < 0.3
                lines = [lines, {'enumeration'}, {'One, Two'}, {'end'}];
            end
            lines = [lines, {'methods'}, {'function obj = Thing(v)'}, argumentsBlock(), program(2, 2), {'end'}, {'function r = run(obj)'}, program(2, 2), {'end'}, {'end'}, {'end'}];
    end
    text = spoil(lines);
    mode = modes{randi(3)};
    if rand < 0.3
        blank = 'Indent';
    else
        blank = 'RemoveWhitespace';
    end
    % The whole text: formatCode's result for a range of lines was measured to depend on where the range ends (w9_probe5), so a
    % selection's Smart Indent follows the rich text component's IndentUtilities as read, not this formatter.
    count = numel(lines);
    startLine = 1; endLine = count;
    try
        result = matlab.codeanalyzer.internal.formatCode(text, IndentMode = mode, IndentSize = 4, UseSpacesInsteadOfTabs = true, ...
            TabSize = 4, BlankLineMode = blank, startLine = startLine, endLine = endLine);
        cases{end + 1} = struct('text', text, 'mode', mode, 'blank', blank, 'startLine', startLine, 'endLine', endLine, 'result', char(result)); %#ok<SAGROW>
    catch e
        cases{end + 1} = struct('text', text, 'mode', mode, 'blank', blank, 'startLine', startLine, 'endLine', endLine, 'error', e.message); %#ok<SAGROW>
    end
end
fprintf('%s\n', jsonencode(cases));

function lines = argumentsBlock()
    lines = {};
    if rand < 0.3
        lines = {'arguments', 'x (1,1) double', 'y = 2', 'end'};
    end
end

function lines = program(depth, budget)
    lines = {};
    count = randi([1, 4]);
    for i = 1:count
        pick = rand;
        if depth >= 3 || budget <= 0 || pick < 0.45
            lines = [lines, simple()]; %#ok<AGROW>
        elseif pick < 0.58
            lines = [lines, {'if x > 1'}, program(depth + 1, budget - 1)]; %#ok<AGROW>
            if rand < 0.4
                lines = [lines, {'elseif x < 0'}, program(depth + 1, budget - 1)]; %#ok<AGROW>
            end
            if rand < 0.5
                lines = [lines, {'else'}, program(depth + 1, budget - 1)]; %#ok<AGROW>
            end
            lines = [lines, {'end'}]; %#ok<AGROW>
        elseif pick < 0.68
            lines = [lines, {'for k = 1:n'}, program(depth + 1, budget - 1), {'end'}]; %#ok<AGROW>
        elseif pick < 0.74
            lines = [lines, {'while k < 10'}, program(depth + 1, budget - 1), {'end'}]; %#ok<AGROW>
        elseif pick < 0.82
            lines = [lines, {'switch s'}, {'case 1'}, program(depth + 1, budget - 1), {'case {2, 3}'}, program(depth + 1, budget - 1)]; %#ok<AGROW>
            if rand < 0.5
                lines = [lines, {'otherwise'}, program(depth + 1, budget - 1)]; %#ok<AGROW>
            end
            lines = [lines, {'end'}]; %#ok<AGROW>
        elseif pick < 0.88
            lines = [lines, {'try'}, program(depth + 1, budget - 1), {'catch e'}, program(depth + 1, budget - 1), {'end'}]; %#ok<AGROW>
        elseif pick < 0.94
            lines = [lines, {'%{'}, {'a block comment'}, {'  with two lines'}, {'%}'}]; %#ok<AGROW>
        else
            lines = [lines, {'parfor k = 1:n'}, program(depth + 1, budget - 1), {'end'}]; %#ok<AGROW>
        end
    end
end

function lines = simple()
    pool = {{'x = 1;'}, {'disp(x)'}, {'y = x + 1; % trailing comment'}, {'% a comment'}, {''}, {'   '}, {'z = a(end);'}, {'s = ''if x'';'}, {'t = "end";'}, ...
        {'q = [1 2 ...', '3 4];'}, {'w = f(x, ...', 'y);'}, {'u = {1, ''end'', 2};'}, {'v = x''; if_x = 2;'}, {'fprintf(''%d\n'', k);'}, {'c = x(end) + y(end-1);'}, ...
        {'if x, y = 1; end'}, {'for k = 1:3, disp(k), end'}, {'m = [1 ...', '2 ...', '3];'}, {'% another', '%   comment'}, {'r = struct(''end'', 1).end;'}, {'if x, y = 1; else, y = 2; end'}};
    lines = pool{randi(numel(pool))};
end

function text = spoil(lines)
    out = cell(size(lines));
    for i = 1:numel(lines)
        r = rand;
        if r < 0.2
            pad = '';
        elseif r < 0.3
            pad = sprintf('\t');
        elseif r < 0.4
            pad = [repmat(' ', 1, randi(3)) sprintf('\t')];
        else
            pad = repmat(' ', 1, randi([0, 9]));
        end
        line = lines{i};
        if rand < 0.15
            line = [line repmat(' ', 1, randi(3))];
        end
        out{i} = [pad line];
    end
    text = strjoin(out, newline);
end
