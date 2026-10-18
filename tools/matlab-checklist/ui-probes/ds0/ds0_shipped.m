% DS0 (designer plan): what App Designer's canvas is made of in the 44 .mlapp files R2025b ships,
% and how each file's generated createComponents relates to it. MathWorks' files are read in place;
% nothing of theirs is written here but counts, names and shapes.
%
% For every app: the full load App Designer starts with (DeserializerFactory ... getAppData), the
% components that carry DesignTimeProperties (App Designer's own key for a canvas component) and
% the fields of that struct; whether the tree's code names are exactly the components the code
% creates, in the same order; and, per component, which properties the code assigns against which
% differ from a freshly made component of the same class (the rule the code generator follows).
files = dir(fullfile(matlabroot, 'toolbox', '**', '*.mlapp'));
fprintf('%d shipped .mlapp files\n', numel(files));
tally = containers.Map();
assignedSame = containers.Map(); assignedDiff = containers.Map(); unassignedDiff = containers.Map();
orderTally = containers.Map();
host = uifigure('Visible', 'off');
for k = 1:numel(files)
    fn = fullfile(files(k).folder, files(k).name);
    try
        des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
        a = des.getAppData();
    catch e
        fprintf('%s: load error %s\n', files(k).name, e.identifier); continue
    end
    fig = a.components.UIFigure;
    comps = findall(fig, '-property', 'DesignTimeProperties');
    bump(tally, sprintf('figure has DesignTimeProperties=%d', isprop(fig, 'DesignTimeProperties')));
    names = cell(1, numel(comps));
    for c = 1:numel(comps)
        p = findprop(comps(c), 'DesignTimeProperties');
        bump(tally, sprintf('DTP is %s', class(p)));
        d = comps(c).DesignTimeProperties;
        bump(tally, sprintf('DTP value class %s', class(d)));
        if isstruct(d)
            bump(tally, sprintf('DTP fields %s', strjoin(fieldnames(d)', ',')));
            names{c} = d.CodeName;
            if isfield(d, 'ComponentCode'), bump(tally, sprintf('ComponentCode class=%s empty=%d', class(d.ComponentCode), isempty(d.ComponentCode))); end
            if isfield(d, 'GroupId'), bump(tally, sprintf('GroupId class=%s empty=%d', class(d.GroupId), isempty(d.GroupId))); end
        end
    end
    % without DesignTimeProperties: what else is in the tree
    all = findall(fig);
    rest = setdiff(all, comps);
    for c = 1:numel(rest), bump(tally, sprintf('no DTP: %s', class(rest(c)))); end

    % the code's createComponents
    raw = strrep(char(matlab.internal.getCode(fn)), sprintf('\r\n'), newline);
    lines = splitlines(raw);
    i0 = find(~cellfun(@isempty, regexp(lines, '^\s*function createComponents\(app\)')), 1);
    if isempty(i0), bump(tally, 'no createComponents'); continue; end
    indent = regexp(lines{i0}, '^\s*', 'match', 'once');
    i1 = i0 + find(strcmp(lines(i0+1:end), [indent 'end']), 1);
    body = lines(i0+1:i1-1);
    created = {};
    assigned = containers.Map();
    for j = 1:numel(body)
        t = regexp(body{j}, '^\s*app\.(\w+) = (\w+)\(', 'tokens', 'once');
        if ~isempty(t), created{end+1} = t{1}; assigned(t{1}) = {}; continue; end %#ok<AGROW>
        t = regexp(body{j}, '^\s*app\.(\w+)\.(\w+)', 'tokens', 'once');
        if ~isempty(t) && isKey(assigned, t{1})
            v = assigned(t{1});
            if ~any(strcmp(v, t{2})), v{end+1} = t{2}; end %#ok<AGROW>
            assigned(t{1}) = v;
        end
    end
    inTree = names(~cellfun(@isempty, names));
    bump(tally, sprintf('tree names == created set: %d', isempty(setxor(inTree, created))));
    if ~isempty(setdiff(created, inTree)), fprintf('  %s: created, not in tree: %s\n', files(k).name, strjoin(setdiff(created, inTree), ',')); end
    if ~isempty(setdiff(inTree, created)), fprintf('  %s: in tree, not created: %s\n', files(k).name, strjoin(setdiff(inTree, created), ',')); end

    % assigned properties against differing ones
    for c = 1:numel(comps)
        nm = names{c};
        if isempty(nm) || ~isKey(assigned, nm), continue; end
        obj = comps(c);
        fresh = makeFresh(obj, host);
        if isempty(fresh), bump(tally, sprintf('no fresh %s', class(obj))); continue; end
        props = publicProps(obj);
        as = assigned(nm);
        % the order of the code's lines against the class's property order
        [tf, pos] = ismember(as, props);
        pos = pos(tf);
        bump(orderTally, sprintf('assigned lines in class order: %d', issorted(pos)));
        for q = 1:numel(props)
            pn = props{q};
            if any(strcmp(pn, {'Parent', 'Children', 'DesignTimeProperties'})), continue; end
            try, differs = ~isequal(obj.(pn), fresh.(pn)); catch, continue; end
            key = [shortClass(obj) '.' pn];
            if any(strcmp(as, pn))
                if differs, bump(assignedDiff, key); else, bump(assignedSame, key); end
            elseif differs
                bump(unassignedDiff, key);
            end
        end
        delete(fresh);
    end
    delete(fig);
end
delete(host);
dump('tally', tally, 200);
dump('assigned and differing (top)', assignedDiff, 60);
dump('assigned but equal to a fresh one', assignedSame, 80);
dump('differing but not assigned', unassignedDiff, 120);
dump('order', orderTally, 10);

function bump(m, key)
    if isKey(m, key), m(key) = m(key) + 1; else, m(key) = 1; end
end

function dump(title, m, limit)
    fprintf('--- %s (%d keys)\n', title, m.Count);
    k = keys(m); v = cell2mat(values(m));
    [v, o] = sort(v, 'descend'); k = k(o);
    for i = 1:min(limit, numel(k)), fprintf('%5d  %s\n', v(i), k{i}); end
end

function s = shortClass(obj)
    s = class(obj);
    s = s(find(s == '.', 1, 'last') + 1:end);
end

function props = publicProps(obj)
    mc = metaclass(obj);
    pl = mc.PropertyList;
    keep = arrayfun(@(p) ischar(p.GetAccess) && strcmp(p.GetAccess, 'public') && ischar(p.SetAccess) && strcmp(p.SetAccess, 'public') && ~p.Hidden && ~p.Constant, pl);
    props = {pl(keep).Name};
end

function fresh = makeFresh(obj, host)
    % A component of the same class with nothing set, under a parent of the same class as obj's.
    fresh = [];
    try
        fresh = feval(class(obj), 'Parent', host);
        return
    catch
    end
    try
        par = obj.Parent;
        p = feval(class(par), 'Parent', host);
        fresh = feval(class(obj), 'Parent', p);
    catch
        try
            gp = feval(class(par.Parent), 'Parent', host);
            p = feval(class(par), 'Parent', gp);
            fresh = feval(class(obj), 'Parent', p);
        catch
            fresh = [];
        end
    end
end
