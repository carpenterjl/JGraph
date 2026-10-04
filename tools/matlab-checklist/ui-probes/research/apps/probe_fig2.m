% probe_fig2.m - what the hgS_070000 tree holds for callbacks (no objects created). Then checkcode the examples.
here = pwd;
diary(fullfile(here, 'probe_fig2.out')); diary on
set(groot, 'DefaultFigureVisible', 'off');
files = {fullfile(here, 'guide_pair', 'myguide.fig'), ...
         fullfile(matlabroot, 'toolbox', 'lte', 'lte', 'saLteDLConformanceTestBenchGUI.fig')};
for i = 1:numel(files)
    s = load(files{i}, '-mat', 'hgS_070000');
    m = load(files{i}, '-mat', 'meta_data');
    fprintf('== %s\n   meta_data: %s\n', files{i}, jsonencode(m.meta_data));
    walk(s.hgS_070000, 0);
end
fprintf('--- version of fig format written by savefig\n');
f = figure('Visible', 'off'); uicontrol(f, 'Style', 'pushbutton', 'Callback', 'disp(1)');
tmp = [tempname '.fig'];
savefig(f, tmp); delete(f);
w = whos('-file', tmp); fprintf('savefig vars: %s\n', strjoin({w.name}, ', '));
tmp2 = [tempname '.fig'];
f = figure('Visible', 'off'); savefig(f, tmp2, 'compact'); delete(f);
w = whos('-file', tmp2); fprintf('savefig compact vars: %s\n', strjoin({w.name}, ', '));
delete(tmp); delete(tmp2);

fprintf('--- checkcode on examples\n');
ex = dir(fullfile(here, 'examples', '*.m'));
for k = 1:numel(ex)
    p = fullfile(ex(k).folder, ex(k).name);
    msgs = checkcode(p, '-id');
    fprintf('%s: %d messages\n', ex(k).name, numel(msgs));
    for j = 1:numel(msgs), fprintf('   L%d %s: %s\n', msgs(j).line, msgs(j).id, msgs(j).message); end
    % parse errors would show up as id 'SYNER' etc.; also confirm the tree builds
    try
        t = mtree(fileread(p));
        fprintf('   mtree kinds ok, parse error count = %d\n', numel(t.mtfind('Kind', 'ERR')));
    catch e, fprintf('   mtree err %s\n', e.message); end
end
p = fullfile(here, 'guide_pair', 'myguide.m');
msgs = checkcode(p, '-id'); fprintf('myguide.m: %d messages\n', numel(msgs));

fprintf('--- ComponentContainer\n');
mc = ?matlab.ui.componentcontainer.ComponentContainer;
fprintf('superclasses: %s\n', strjoin({mc.SuperclassList.Name}, ', '));
fprintf('abstract: %d\n', mc.Abstract);
ml = mc.MethodList;
ab = ml([ml.Abstract]);
fprintf('abstract methods: %s\n', strjoin({ab.Name}, ', '));
pl = mc.PropertyList;
pub = pl(strcmp({pl.GetAccess}, 'public') & ~[pl.Hidden]);
fprintf('public properties (%d): %s\n', numel(pub), strjoin(sort({pub.Name}), ', '));
addpath(fullfile(here, 'examples'));
try
    f = uifigure('Visible', 'off');
    c = SpinnerGauge(f, 'Value', 40);
    fprintf('after construct: SetupCalls=%d UpdateCalls=%d\n', c.SetupCalls, c.UpdateCalls);
    drawnow;
    fprintf('after drawnow: SetupCalls=%d UpdateCalls=%d\n', c.SetupCalls, c.UpdateCalls);
    c.Value = 50; c.Value = 60;
    fprintf('after 2 sets (no drawnow): UpdateCalls=%d\n', c.UpdateCalls);
    drawnow;
    fprintf('after drawnow: UpdateCalls=%d\n', c.UpdateCalls);
    fprintf('has ValueChangedFcn prop: %d, class %s, isa ComponentContainer %d, Type=%s\n', ...
        isprop(c, 'ValueChangedFcn'), class(c), isa(c, 'matlab.ui.componentcontainer.ComponentContainer'), c.Type);
    hit = 0;
    c.ValueChangedFcn = @(s, e) evalin('base', 'disp(''ValueChangedFcn ran'')');
    notifyHelper = @() []; %#ok<NASGU>
    delete(f);
    fprintf('component valid after fig delete: %d\n', isvalid(c));
catch e
    fprintf('cc err: %s | %s\n', e.identifier, e.message);
    for k = 1:numel(e.stack), fprintf('   at %s:%d\n', e.stack(k).name, e.stack(k).line); end
end
delete(findall(groot, 'Type', 'figure'));
diary off

function walk(node, depth)
    persistent shown
    if depth == 0, shown = 0; end
    props = node.properties;
    cbn = {'Callback', 'CreateFcn', 'DeleteFcn', 'ButtonDownFcn', 'KeyPressFcn', 'CloseRequestFcn', 'ResizeFcn', 'SizeChangedFcn', 'SelectionChangedFcn'};
    for c = cbn
        if isfield(props, c{1}) && shown < 8
            v = props.(c{1});
            if isa(v, 'function_handle'), txt = func2str(v); elseif ischar(v), txt = v; else, txt = class(v); end
            fprintf('   %s%s.%s (%s) = %s\n', repmat(' ', 1, depth), node.type, c{1}, class(v), txt);
            shown = shown + 1;
        end
    end
    for k = 1:numel(node.children)
        walk(node.children(k), depth + 1);
    end
end
