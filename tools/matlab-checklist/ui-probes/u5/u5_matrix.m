function u5_matrix
% U5 probe: every batch-1 uifigure component, every settable property, against one battery of
% values - what is kept (as get reads it back) and what is refused. Headless.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
makers = u5_makers();
battery = {'on', 'off', true, false, 1, 0, -1, 2.5, 50, [1 2], [10 20], [1 2 3], [0.2 0.4 0.6], [1 2 3 4], [5 6 7 8], ...
    'abc', "str", {'a', 'b'}, ["p"; "q"], {'a'; 'b'}, [], '', {}, NaN, Inf, -Inf, 'red', '#0f0', 'none', @sin, struct('a', 1), ...
    int8(3), single(2.5), 'left', 'center', 'right', 'top', 'bottom', 'bold', 'italic', 'auto', 'manual', 'fit', '1x', {'fit', '2x', 30}, ...
    'horizontal', 'vertical', ['ab'; 'cd'], 1 + 2i, "", string(missing), {1, 2}, 'Option 2', 'Item 3', {'Item 1', 'Item 3'}, 2, 3, [2 4], ...
    '%.2f', 'text', 'html', 'latex', 'tex', categorical({'x'}), uint8([10 20 30]), [0 1], [5 1], [-Inf Inf], [0 Inf]};
skip = {'Parent', 'Children', 'Type', 'BeingDeleted', 'UserData', 'ContextMenu', 'Layout', 'StyleConfigurations', ...
    'CreateFcn', 'DeleteFcn'};
for m = 1:size(makers, 1)
    cls = makers{m, 1};
    h = makers{m, 2}(uf);
    names = fieldnames(get(h));
    fprintf('## %s class=%s type=%s\n', cls, class(h), h.Type);
    fprintf('## %s get names (%d): %s\n', cls, numel(names), strjoin(names', ' '));
    try
        snames = fieldnames(set(h));
        fprintf('## %s set names (%d): %s\n', cls, numel(snames), strjoin(snames', ' '));
    catch e
        fprintf('## %s set(h) ERR %s | %s\n', cls, e.identifier, oneline(e.message));
    end
    for n = 1:numel(names)
        fprintf('%s.%s default : %s\n', cls, names{n}, v2s(get(h, names{n})));
    end
    delete(allchild(uf));
    for n = 1:numel(names)
        name = names{n};
        if any(strcmp(name, skip)), continue; end
        isfcn = numel(name) > 3 && strcmp(name(end-2:end), 'Fcn');
        for k = 1:numel(battery)
            v = battery{k};
            if isfcn && ~fcnvalue(v)
                continue;
            end
            h = makers{m, 2}(uf);
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
            delete(allchild(uf));
        end
    end
end
delete(uf);
end

function tf = fcnvalue(v)
% The few values worth trying on a callback property.
tf = isa(v, 'function_handle') || isequal(v, 1) || isequal(v, 'abc') || isequal(v, []) ...
    || (isstring(v) && isscalar(v) && ~ismissing(v) && v == "str") || (iscell(v) && isequal(size(v), [1 2]) && isnumeric(v{1}));
end
