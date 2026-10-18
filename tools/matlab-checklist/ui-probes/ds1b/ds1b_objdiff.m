function out = ds1b_objdiff(a, b)
% DS1b (designer plan): how object a differs from object b, as a cell of short texts: every
% property public to get, hidden ones included, compared with isequaln; a handle or other object by
% class and count; DesignTimeProperties field by field (a missing ImageRelativePath counts as '').
% Properties that hold run-time state (a figure's number, an object's id, a view's tokens) are skipped.
out = {};
tag = class(a); tag = tag(find(tag == '.', 1, 'last') + 1:end);
if ~strcmp(class(a), class(b))
    out{end + 1} = sprintf('%s class %s', tag, class(b));
    return
end
% ParentMode, HasAppBuildingDefaults: a tree loaded from any saved file, MATLAB's own included, has
% them 'auto' and 'off' (neither is stored).
skip = {'DesignTimeProperties', 'Number', 'Uuid', 'UpdateToken', 'ControllerMode', 'FigureViewReady', 'ObjectID', 'ParentMode', 'HasAppBuildingDefaults'};
if isa(b, 'matlab.ui.control.UIAxes')
    % A hidden axes toolbar is App Designer's own custom toolbar, which code does not make; its
    % Visible is compared below.
    skip = [skip, {'Toolbar_IS', 'ToolbarMode'}];
end
mc = metaclass(b);
for p = mc.PropertyList'
    if ~ischar(p.GetAccess) || ~strcmp(p.GetAccess, 'public') || any(strcmp(p.Name, skip))
        continue
    end
    f = p.Name;
    try, vb = b.(f); catch, continue; end
    try, va = a.(f); catch, out{end + 1} = sprintf('%s.%s get fails', tag, f); continue; end %#ok<AGROW>
    if isobject(vb) && ~isa(vb, 'matlab.lang.OnOffSwitchState') && ~isa(vb, 'datetime') && ~isenum(vb) ...
            && ~isa(vb, 'matlab.ui.layout.LayoutOptions') && ~isa(vb, 'matlab.graphics.theme.GraphicsTheme')
        same = strcmp(class(va), class(vb)) && numel(va) == numel(vb);
    else
        same = isequaln(va, vb);
    end
    if ~same
        out{end + 1} = sprintf('%s.%s=%s (ref %s)', tag, f, show(va), show(vb)); %#ok<AGROW>
    end
end
if isa(b, 'matlab.ui.control.UIAxes')
    % An axes' labels and toolbar are objects of their own: compare what App Designer sets on them.
    for part = {'Title', 'Subtitle', 'XLabel', 'YLabel', 'ZLabel'}
        if ~isequal(a.(part{1}).String, b.(part{1}).String)
            out{end + 1} = sprintf('%s.%s.String=%s (ref %s)', tag, part{1}, show(a.(part{1}).String), show(b.(part{1}).String)); %#ok<AGROW>
        end
    end
    if ~isequal(a.Toolbar.Visible, b.Toolbar.Visible)
        out{end + 1} = sprintf('%s.Toolbar.Visible=%s (ref %s)', tag, a.Toolbar.Visible, b.Toolbar.Visible);
    end
end
hasA = isprop(a, 'DesignTimeProperties'); hasB = isprop(b, 'DesignTimeProperties');
if hasA ~= hasB
    out{end + 1} = sprintf('%s.DesignTimeProperties present %d (ref %d)', tag, hasA, hasB);
elseif hasB
    da = a.DesignTimeProperties; db = b.DesignTimeProperties;
    if ~isfield(da, 'ImageRelativePath'), da.ImageRelativePath = ''; end
    if ~isfield(db, 'ImageRelativePath'), db.ImageRelativePath = ''; end
    for f = union(fieldnames(da), fieldnames(db))'
        if ~isfield(da, f{1}) || ~isfield(db, f{1})
            out{end + 1} = sprintf('%s.DesignTimeProperties.%s missing', tag, f{1}); %#ok<AGROW>
        elseif ~isequal(da.(f{1}), db.(f{1}))
            if strcmp(f{1}, 'ComponentCode') && iscell(da.(f{1})) && iscell(db.(f{1}))
                x = da.(f{1}); y = db.(f{1});
                k = find(~ismember(x(:), y(:)) , 1);
                if isempty(k), k = find(~ismember(y(:), x(:)), 1); detail = ['ref has ' y{k}]; else, detail = ['has ' x{k}]; end
                if isempty(k), detail = sprintf('size %s (ref %s)', mat2str(size(x)), mat2str(size(y))); end
                out{end + 1} = sprintf('%s.DesignTimeProperties.ComponentCode %s', tag, detail); %#ok<AGROW>
            else
                out{end + 1} = sprintf('%s.DesignTimeProperties.%s=%s (ref %s)', tag, f{1}, show(da.(f{1})), show(db.(f{1}))); %#ok<AGROW>
            end
        end
    end
end
end

function s = show(v)
try
    if ischar(v) || isstring(v) || isnumeric(v) || islogical(v) || isa(v, 'matlab.lang.OnOffSwitchState')
        s = mat2str(string(v));
        if strlength(s) > 40, s = extractBefore(s, 41) + "..."; end
        s = char(s);
    else
        s = ['<' class(v) ' ' mat2str(size(v)) '>'];
    end
catch
    s = ['<' class(v) '>'];
end
end
