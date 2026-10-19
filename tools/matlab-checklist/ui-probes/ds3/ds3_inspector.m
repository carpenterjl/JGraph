% DS3 (designer plan): App Designer's inspector for each component class. For each class the
% catalog knows (ds1_catalog.json), made as ds2_sizes makes it, builds the property view App
% Designer's inspector shows (inspector.internal.<Short>PropertyView, which
% UIComponentsInspectorRegistrator registers) and records its groups in order - the group's id,
% its title, whether it opens expanded, and its rows: a property, an editor group (properties
% edited together, as Value with Items) or a sub-group - and, for every property in a row, the
% class the view types it as and, for an enumeration, its members (or the EnumeratedValues of the
% inspector's string enumerations). A row naming a property the view does not have is not shown
% (class '?'). Headless. Writes
% ds3_inspector.json.
catalog = jsondecode(fileread(fullfile(pwd, '..', 'ds1', 'ds1_catalog.json')));
parents = struct('Tab', 'uitabgroup(fig)', 'TreeNode', 'uitree(fig)', 'PushTool', 'uitoolbar(fig)', ...
    'ToggleTool', 'uitoolbar(fig)', 'RadioButton', 'uibuttongroup(fig)', 'ToggleButton', 'uibuttongroup(fig)', ...
    'Menu', 'fig', 'ContextMenu', 'fig');
rows = {};
for k = 1:numel(catalog.classes)
    c = catalog.classes(k);
    if iscell(catalog.classes), c = catalog.classes{k}; end
    short = c.type(find(c.type == '.', 1, 'last') + 1:end);
    fig = uifigure('Visible', 'off');
    if strcmp(short, 'Figure')
        obj = fig;
    else
        if isfield(parents, short), p = eval(parents.(short)); else, p = fig; end %#ok<NASGU>
        obj = eval(strrep(c.creation, '%PARENT%', 'p'));
    end
    row = struct('type', c.type, 'view', ['inspector.internal.' short 'PropertyView'], 'groups', {{}}, ...
        'properties', struct(), 'error', '');
    try
        view = feval(row.view, obj);
        for g = 1:numel(view.GroupList)
            group = view.GroupList(g);
            title = group.Title;
            try
                title = getString(message(group.GroupID));
            catch
            end
            items = {};
            for i = 1:numel(group.PropertyList)
                item = group.PropertyList{i};
                if ischar(item) || isstring(item)
                    items{end + 1} = struct('kind', 'property', 'names', {{char(item)}}); %#ok<SAGROW>
                    names = {char(item)};
                elseif isa(item, 'internal.matlab.inspector.InspectorEditorGroup')
                    names = flatten(item.PropertyList);
                    items{end + 1} = struct('kind', 'editor', 'names', {names}); %#ok<SAGROW>
                else
                    names = flatten(item.PropertyList);
                    items{end + 1} = struct('kind', 'sub', 'names', {names}); %#ok<SAGROW>
                end
                for n = 1:numel(names)
                    row.properties.(names{n}) = describe(view, names{n});
                end
            end
            row.groups{end + 1} = struct('id', group.GroupID, 'title', title, 'expanded', logical(group.Expanded), ...
                'items', {items});
        end
    catch e
        row.error = [e.identifier ': ' e.message];
    end
    rows{end + 1} = row; %#ok<SAGROW>
    delete(fig);
end
fid = fopen(fullfile(pwd, 'ds3_inspector.json'), 'w', 'n', 'UTF-8');
fwrite(fid, jsonencode(struct('release', version('-release'), 'classes', {rows}), 'PrettyPrint', true));
fclose(fid);
for k = 1:numel(rows)
    r = rows{k};
    titles = cellfun(@(g) g.title, r.groups, 'UniformOutput', false);
    fprintf('%-42s %2d groups %s %s\n', r.type, numel(r.groups), strjoin(titles, ' | '), r.error);
end

function names = flatten(list)
    % An editor group or sub-group's properties; one can hold another (the axes' limits).
    names = {};
    for i = 1:numel(list)
        if ischar(list{i}) || isstring(list{i})
            names{end + 1} = char(list{i}); %#ok<AGROW>
        else
            names = [names flatten(list{i}.PropertyList)]; %#ok<AGROW>
        end
    end
end

function d = describe(view, name)
    d = struct('class', '', 'members', {{}});
    p = findprop(view, name);
    if isempty(p)
        d.class = '?';
        return
    end
    if ~isempty(p.Validation) && ~isempty(p.Validation.Class)
        d.class = p.Validation.Class.Name;
        m = p.Validation.Class;
        if ~isempty(m.EnumerationMemberList)
            d.members = {m.EnumerationMemberList.Name};
        else
            % The inspector's string enumerations list their choices as a constant.
            e = m.PropertyList(strcmp({m.PropertyList.Name}, 'EnumeratedValues'));
            if ~isempty(e) && e.Constant
                d.members = cellstr(eval([m.Name '.EnumeratedValues']));
            end
        end
    else
        try
            d.class = class(view.(name));
        catch
            d.class = '?';
        end
    end
end
