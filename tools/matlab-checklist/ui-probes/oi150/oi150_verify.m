% Open item 150 (component groups): what R2025b's own loader makes of the files JGraph wrote with
% groups (AppDesignerGroupTests with JGRAPH_OI150_OUT set to this folder's rt): the groups it
% reads from components.Groups and each component's GroupId, printed as oi150_groups prints
% them, so that the two can be put side by side. Headless.
folder = fullfile(pwd, 'rt');
files = [dir(fullfile(folder, 'grouped', '*.mlapp')); dir(fullfile(folder, 'new', '*.mlapp'))];
fprintf('%d files\n', numel(files));
for k = 1:numel(files)
    fn = fullfile(files(k).folder, files(k).name);
    try
        des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
        a = des.getAppData();
    catch e
        fprintf('%s: load error %s %s\n', files(k).name, e.identifier, e.message); continue
    end
    if isfield(a.components, 'Groups'), g = a.components.Groups; else, g = {}; end
    fprintf('--- %s: %d groups (%s)\n', files(k).name, numel(g), class(g));
    for i = 1:numel(g)
        if iscell(g), s = g{i}; else, s = g(i); end
        fprintf('  group %s parent=[%s]\n', s.Id, s.ParentGroupId);
    end
    comps = findall(a.components.UIFigure, '-property', 'DesignTimeProperties');
    for c = 1:numel(comps)
        d = comps(c).DesignTimeProperties;
        if ~isstruct(d) || ~isfield(d, 'GroupId'), continue; end
        fprintf('  %s (%s) group=[%s]\n', d.CodeName, class(comps(c)), d.GroupId);
    end
    delete(a.components.UIFigure);
end
