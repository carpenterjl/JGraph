% Open item 150 (component groups): what App Designer's tree holds for a group, read from the
% .mlapp files R2025b ships in place (nothing of theirs is written here but counts, names and
% shapes). For every shipped app with groups: components.Groups as the loader gives it (its class,
% shape and fields), each group's Id and ParentGroupId, and each grouped component's CodeName,
% class, parent and GroupId. Headless.
files = dir(fullfile(matlabroot, 'toolbox', '**', '*.mlapp'));
fprintf('%d shipped .mlapp files\n', numel(files));
withGroups = 0; shapes = containers.Map(); members = 0; labelled = 0;
for k = 1:numel(files)
    fn = fullfile(files(k).folder, files(k).name);
    try
        des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
        a = des.getAppData();
    catch e
        fprintf('%s: load error %s\n', files(k).name, e.identifier); continue
    end
    if ~isfield(a.components, 'Groups'), continue; end
    g = a.components.Groups;
    withGroups = withGroups + 1;
    if iscell(g), f1 = g{1}; else, f1 = g; end
    key = sprintf('%s %s [%s]', class(g), mat2str(size(g)), strjoin(fieldnames(f1)', ','));
    if isKey(shapes, key), shapes(key) = shapes(key) + 1; else, shapes(key) = 1; end
    fprintf('--- %s: %d groups\n', files(k).name, numel(g));
    for i = 1:numel(g)
        if iscell(g), s = g{i}; else, s = g(i); end
        fprintf('  group %s parent=[%s]\n', s.Id, s.ParentGroupId);
    end
    comps = findall(a.components.UIFigure, '-property', 'DesignTimeProperties');
    for c = 1:numel(comps)
        d = comps(c).DesignTimeProperties;
        if ~isstruct(d) || ~isfield(d, 'GroupId') || isempty(d.GroupId), continue; end
        members = members + 1;
        p = comps(c).Parent;
        pname = '?';
        if isprop(p, 'DesignTimeProperties'), pname = p.DesignTimeProperties.CodeName; end
        fprintf('  member %s (%s) in %s group=%s\n', d.CodeName, class(comps(c)), pname, d.GroupId);
    end
    delete(a.components.UIFigure);
end
fprintf('%d apps with groups, %d grouped components\n', withGroups, members);
ks = keys(shapes);
for i = 1:numel(ks), fprintf('shape %s: %d apps\n', ks{i}, shapes(ks{i})); end

% A group made through the serializer: a labelled pair (label + edit field) grouped by hand, the
% GroupId set as DesignTimeController sets it, saved with MLAPPSerializer, loaded back.
out = fullfile(pwd, 'rt'); if ~exist(out, 'dir'), mkdir(out); end
fig = uifigure('Visible', 'off', 'Position', [100 100 640 480]);
lbl = uilabel(fig, 'Position', [20 400 60 22], 'Text', 'Edit Field');
fld = uieditfield(fig, 'text', 'Position', [85 400 100 22]);
btn = uibutton(fig, 'push', 'Position', [20 300 100 23]);
names = {'UIFigure', 'EditFieldLabel', 'EditField', 'Button'};
objs = {fig, lbl, fld, btn};
gids = {'', 'g1', 'g1', ''};
for i = 1:numel(objs)
    addprop(objs{i}, 'DesignTimeProperties');
    objs{i}.DesignTimeProperties = struct('CodeName', names{i}, 'GroupId', gids{i}, 'ComponentCode', {{}});
end
ser = appdesigner.internal.serialization.MLAPPSerializer(fullfile(out, 'Grouped.mlapp'), fig);
ser.Groups = struct('Id', {'g1'}, 'ParentGroupId', {''});
ser.MatlabCodeText = sprintf('classdef Grouped < matlab.apps.AppBase\nend\n');
try
    ser.save();
    fprintf('saved Grouped.mlapp\n');
    des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fullfile(out, 'Grouped.mlapp'));
    b = des.getAppData();
    g = b.components.Groups;
    if iscell(g), f1 = g{1}; else, f1 = g; end
    fprintf('loaded back: Groups %s %s fields [%s]\n', class(g), mat2str(size(g)), strjoin(fieldnames(f1)', ','));
    comps = findall(b.components.UIFigure, '-property', 'DesignTimeProperties');
    for c = 1:numel(comps)
        d = comps(c).DesignTimeProperties;
        fprintf('  %s group=[%s]\n', d.CodeName, d.GroupId);
    end
    delete(b.components.UIFigure);
catch e
    fprintf('serializer: %s %s\n', e.identifier, e.message);
end
delete(fig);
