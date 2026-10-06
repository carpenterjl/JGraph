function u9_matrix
% U9 probe: knobs, switches, gauges, a lamp, a date picker, a colour picker, trees and nodes.
% In a uifigure every property's default and every settable property against one battery of
% values; in a classic figure the defaults and the names only (U8 found one class in both).
% Headless.
makers = u9_makers();
d1 = datetime(2024, 1, 15);
d2 = datetime(2024, 3, 5, 14, 30, 0);
battery = {'on', 'off', true, false, 1, 0, -1, 2.5, 50, [1 2], [10 20], [1 2 3], [0.2 0.4 0.6], [1 2 3 4], [5 6 7 8], ...
    'abc', "str", {'a', 'b'}, ["p"; "q"], {'a'; 'b'}, [], '', {}, NaN, Inf, -Inf, 'red', '#0f0', 'none', @sin, struct('a', 1), ...
    int8(3), single(2.5), 'left', 'center', 'right', 'top', 'bottom', 'bold', 'italic', 'auto', 'manual', ...
    ['ab'; 'cd'], "", string(missing), {1, 2}, 2, 3, [2 4], magic(3), {1, 'a'; 2, 'b'}, [true false; false true], ...
    'numeric', {'a', 'b', 'c'}, {'Off', 'On'}, {'On', 'Off'}, 'Off', 'On', 'off ', 'Low', 'High', 'Medium', 100, 101, 0.5, [0 1], [0 100], [100 0], [50 50], ...
    'clockwise', 'counterclockwise', 'north', 'south', 'east', 'west', 'northwest', 'northeast', 'southwest', 'southeast', ...
    'horizontal', 'vertical', 'HORIZONTAL', 'horiz', [1 0 0; 0 1 0], {'red', 'green'}, [0 50; 50 100], [0 50; 60 100], [50 0], 'r', 'g', 'y', ...
    d1, d2, NaT, [d1 d2], [d2 d1], [d1; d2], datetime(2024, 1, [1 2 3]), 'dd/MM/uuuu', 'MM/dd/uuuu', 'uuuu', 'dd-MMM-uuuu', 'yyyy-MM-dd', 'dd-MM-yy', 'HH:mm', 'bogus', ...
    'Monday', 'monday', {'Monday', 'Sunday'}, ["Saturday" "Sunday"], [1 7], 1:7, 8, [1 1], {1, 2}, 'Tree Node', [1 0 0 0.5], {d1}, '15-Jan-2024', "15-Jan-2024", 20240115, ...
    'pixels', 'normalized', 'tex', 'latex', 'html', 'A', 'ab', [2 1; 1 2], uint8([10 20 30]), 'ON', 'TOP', 'checkbox', 'discrete', ...
    'linear', 'rocker', seconds(5), categorical({'a'})};
skip = {'Parent', 'Children', 'Type', 'BeingDeleted', 'UserData', 'ContextMenu', 'UIContextMenu', 'Layout', 'StyleConfigurations', ...
    'CreateFcn', 'DeleteFcn', 'SelectedNodes', 'CheckedNodes'};
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
        if q == 1, continue; end
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
if isdatetime(v) || isduration(v) || iscategorical(v), tf = false; return; end
tf = isa(v, 'function_handle') || isequal(v, 1) || isequal(v, 'abc') || isequal(v, []) ...
    || (isstring(v) && isscalar(v) && ~ismissing(v) && v == "str") || (iscell(v) && isequal(size(v), [1 2]) && isnumeric(v{1}));
end
