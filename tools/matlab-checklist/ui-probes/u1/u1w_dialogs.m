function u1w_dialogs
% U1 WINDOW probe (needs a display; each dialog opens on screen for about a second — run only with
% the user's leave). The blocking dialogs refuse without a display (U0), so their layouts are
% recorded here: a timer dumps the dialog's object tree once it is up, then deletes it, which ends
% the wait. U4 transcribes these trees.
cases = {
    'questdlg default', @() questdlg('Do you want to continue?', 'Question');
    'questdlg two buttons', @() questdlg('Keep the changes?', 'Two', 'Keep', 'Discard', 'Keep');
    'questdlg long text', @() questdlg(repmat('A long question that wraps over several lines. ', 1, 6), 'Long');
    'questdlg modal struct', @() questdlg('Proceed?', 'Struct', 'Yes', 'No', struct('Default', 'No', 'Interpreter', 'none'));
    'inputdlg one', @() inputdlg('Single value:');
    'inputdlg two with defaults', @() inputdlg({'Name:', 'Age:'}, 'Input', [1 35], {'Ada', '36'});
    'inputdlg multiline', @() inputdlg({'Notes:'}, 'Lines', [5 50]);
    'listdlg default', @() listdlg('ListString', {'alpha', 'beta', 'gamma'});
    'listdlg single', @() listdlg('ListString', {'one', 'two'}, 'SelectionMode', 'single', 'PromptString', 'Pick one', 'Name', 'Chooser');
    };
for k = 1:size(cases, 1)
    fprintf('==== %s\n', cases{k, 1});
    before = findall(groot, 'Type', 'figure');
    t = timer('StartDelay', 1.2, 'TimerFcn', @(~, ~) dumpAndClose(before));
    start(t);
    try
        answer = cases{k, 2}();
        fprintf('returned: class=%s size=%s\n', class(answer), mat2str(size(answer)));
    catch e
        fprintf('ERR %s %s\n', e.identifier, e.message);
    end
    stop(t); delete(t);
end
end

function dumpAndClose(before)
figs = setdiff(findall(groot, 'Type', 'figure'), before);
for f = figs(:)'
    fprintf('figure Name=[%s] Tag=[%s] Units=%s Position=%s WindowStyle=%s Resize=%s Color=%s HandleVisibility=%s\n', ...
        f.Name, f.Tag, f.Units, mat2str(f.Position, 6), f.WindowStyle, char(f.Resize), mat2str(f.Color, 4), f.HandleVisibility);
    fprintf('  pixels Position=%s\n', mat2str(getpixelposition(f), 6));
    kids = findall(f);
    for h = kids(:)'
        if h == f, continue; end
        line = sprintf('  %s', h.Type);
        line = add(line, h, 'Style');
        line = add(line, h, 'Tag');
        line = add(line, h, 'String');
        line = add(line, h, 'Units');
        line = add(line, h, 'Position');
        line = add(line, h, 'FontName');
        line = add(line, h, 'FontSize');
        line = add(line, h, 'FontWeight');
        line = add(line, h, 'HorizontalAlignment');
        line = add(line, h, 'VerticalAlignment');
        line = add(line, h, 'Max');
        line = add(line, h, 'Value');
        line = add(line, h, 'Visible');
        line = add(line, h, 'BackgroundColor');
        if isprop(h, 'Position') && isprop(h, 'Units') && ~strcmp(h.Type, 'text')
            try, line = sprintf('%s px=%s', line, mat2str(getpixelposition(h), 6)); catch, end
        end
        fprintf('%s\n', line);
    end
    delete(f);
end
end

function line = add(line, h, name)
if ~isprop(h, name), return; end
v = h.(name);
if iscell(v), v = ['{' strjoin(cellfun(@(x) char(string(x)), v, 'UniformOutput', false), '|') '}']; end
if isstring(v), v = char(strjoin(v, '|')); end
if ischar(v) && size(v, 1) > 1, v = strjoin(cellstr(v), '|'); end
if isnumeric(v) || islogical(v), v = mat2str(v, 6); end
if isa(v, 'matlab.lang.OnOffSwitchState'), v = char(v); end
if ~ischar(v), v = class(v); end
line = sprintf('%s %s=[%s]', line, name, v);
end
