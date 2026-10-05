function u8_matrix
% U8 probe: tables, tabs, menus and toolbar tools, in a classic figure and in a uifigure: every
% property's default, and every settable property against one battery of values. Headless.
makers = u8_makers();
T = table([1; 2], {'a'; 'b'}, 'VariableNames', {'N', 'S'});
battery = {'on', 'off', true, false, 1, 0, -1, 2.5, 50, [1 2], [10 20], [1 2 3], [0.2 0.4 0.6], [1 2 3 4], [5 6 7 8], ...
    'abc', "str", {'a', 'b'}, ["p"; "q"], {'a'; 'b'}, [], '', {}, NaN, Inf, -Inf, 'red', '#0f0', 'none', @sin, struct('a', 1), ...
    int8(3), single(2.5), 'left', 'center', 'right', 'top', 'bottom', 'bold', 'italic', 'auto', 'manual', 'fit', '1x', {'fit', '2x', 30}, ...
    ['ab'; 'cd'], "", string(missing), {1, 2}, 2, 3, [2 4], magic(3), {1, 'a'; 2, 'b'}, T, [true false; false true], ...
    'numeric', 'char', 'logical', {'numeric', 'char'}, {'numeric', {'x', 'y'}}, 'numbered', {'auto', 50}, {50 60}, ...
    [true false], {'bank', 'short'}, 'cell', 'row', 'column', rand(16, 16, 3), uint8(zeros(4, 4, 3)), 'pixels', 'normalized', ...
    'points', 'A', 'ab', [1 1], [2 1; 1 2], uint8([10 20 30]), 'tex', 'latex', 'html', {'1x', '2x'}, 'longG', 'normal'};
skip = {'Parent', 'Children', 'Type', 'BeingDeleted', 'UserData', 'ContextMenu', 'UIContextMenu', 'Layout', 'StyleConfigurations', ...
    'CreateFcn', 'DeleteFcn', 'SelectedTab'};
kinds = {'F', @() figure('Visible', 'off', 'Position', [100 100 560 420]); 'U', @() uifigure('Visible', 'off', 'Position', [100 100 560 420])};
for q = 1:2
    p = kinds{q, 2}();
    for m = 1:size(makers, 1)
        cls = [kinds{q, 1} '.' makers{m, 1}];
        try
            h = makers{m, 2}(p);
        catch e
            fprintf('## %s MAKE ERR %s | %s\n', cls, e.identifier, oneline(e.message));
            continue;
        end
        names = fieldnames(get(h));
        fprintf('## %s class=%s type=%s\n', cls, class(h), h.Type);
        fprintf('## %s get names (%d): %s\n', cls, numel(names), strjoin(names', ' '));
        try
            sv = set(h);
            snames = fieldnames(sv);
            fprintf('## %s set names (%d): %s\n', cls, numel(snames), strjoin(snames', ' '));
            for n = 1:numel(snames)
                if ~isempty(sv.(snames{n})), fprintf('%s.%s words : %s\n', cls, snames{n}, v2s(sv.(snames{n}))); end
            end
        catch e
            fprintf('## %s set(h) ERR %s | %s\n', cls, e.identifier, oneline(e.message));
        end
        for n = 1:numel(names)
            fprintf('%s.%s default : %s\n', cls, names{n}, v2s(get(h, names{n})));
        end
        delete(allchild(p));
        for n = 1:numel(names)
            name = names{n};
            if any(strcmp(name, skip)), continue; end
            isfcn = (numel(name) > 3 && strcmp(name(end-2:end), 'Fcn')) || (numel(name) > 8 && strcmp(name(end-7:end), 'Callback'));
            for k = 1:numel(battery)
                v = battery{k};
                if isfcn && ~fcnvalue(v)
                    continue;
                end
                h = makers{m, 2}(p);
                lastwarn('');
                try
                    set(h, name, v);
                    r = v2s(get(h, name));
                catch e
                    r = sprintf('ERR %s | %s', e.identifier, oneline(e.message));
                end
                [wm, wid] = lastwarn;
                if ~isempty(wm), r = sprintf('%s  WARN %s | %s', r, wid, oneline(wm)); end
                fprintf('%s.%s <- %s : %s\n', cls, name, v2s(v), r);
                delete(allchild(p));
            end
        end
    end
    delete(p);
end
end

function tf = fcnvalue(v)
tf = isa(v, 'function_handle') || isequal(v, 1) || isequal(v, 'abc') || isequal(v, []) ...
    || (isstring(v) && isscalar(v) && ~ismissing(v) && v == "str") || (iscell(v) && isequal(size(v), [1 2]) && isnumeric(v{1}));
end
