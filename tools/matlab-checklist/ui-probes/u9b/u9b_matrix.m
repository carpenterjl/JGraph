function u9b_matrix
% U9b probe: uihtml's property table. In a classic figure and a uifigure the class, the names,
% set(h)'s words and every default; in a uifigure every settable property against one battery.
% Headless. HTMLSource values that name no file are markup, so the battery is safe to set.
d1 = datetime(2024, 1, 15);
battery = {'on', 'off', true, false, 1, 0, -1, 2.5, [1 2], [10 20 30 40], [1 2 3], 'abc', "str", {'a', 'b'}, ["p"; "q"], ...
    [], '', {}, NaN, Inf, @sin, struct('a', 1), int8(3), single(2.5), 'left', 'auto', ['ab'; 'cd'], "", string(missing), ...
    {1, 2}, {@sin, 1}, magic(3), 'pixels', 'normalized', 'ON', 'Off', d1, 1+2i, {'a'; 'b'}, uint8([10 20 30]), ...
    '<p>hi</p>', 'page.html', 'missing.html', 'https://www.mathworks.com', categorical({'a'}), [0 0 0], 'red'};
skip = {'Parent', 'Children', 'Type', 'BeingDeleted', 'ContextMenu', 'UIContextMenu', 'Layout', 'CreateFcn', 'DeleteFcn'};
kinds = {'F', @() figure('Visible', 'off', 'Position', [100 100 560 420]); 'U', @() uifigure('Visible', 'off', 'Position', [100 100 560 420])};
here = fileparts(mfilename('fullpath'));
addpath(here);
cd(fullfile(here, 'site'));
for q = 1:2
    p = kinds{q, 2}();
    cls = [kinds{q, 1} '.HTML'];
    h = uihtml(p);
    names = fieldnames(get(h));
    fprintf('## %s class=%s type=%s\n', cls, class(h), h.Type);
    fprintf('## %s get names (%d): %s\n', cls, numel(names), strjoin(names', ' '));
    fprintf('## %s properties (%d): %s\n', cls, numel(properties(h)), strjoin(properties(h)', ' '));
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
    fprintf('## %s methods: %s\n', cls, strjoin(methods(h)', ' '));
    fprintf('## %s events: %s\n', cls, strjoin(events(h)', ' '));
    fprintf('## %s display:\n%s\n', cls, evalc('disp(h)'));
    fprintf('## %s display named:\n%s\n', cls, evalc('h'));
    delete(allchild(p));
    if q == 1
        delete(p);
        continue;
    end
    for n = 1:numel(names)
        name = names{n};
        if any(strcmp(name, skip)), continue; end
        for k = 1:numel(battery)
            v = battery{k};
            h = uihtml(p);
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
    % dot assignment and the string-keeping question for the two text properties
    h = uihtml(p);
    h.HTMLSource = "<b>s</b>"; fprintf('dot HTMLSource <- string : %s\n', v2s(h.HTMLSource));
    h.Data = "s"; fprintf('dot Data <- string : %s\n', v2s(h.Data));
    h.Data = {"s"}; fprintf('dot Data <- {string} : %s\n', v2s(h.Data));
    h.Tooltip = "t"; fprintf('dot Tooltip <- string : %s\n', v2s(h.Tooltip));
    delete(p);
end
end
