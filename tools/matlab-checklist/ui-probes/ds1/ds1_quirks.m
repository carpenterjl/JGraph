% DS1 (designer plan): two places where R2025b's propertyValueToString writes code that does not
% read back as the value, measured on real components. For each, the property is set, the class it
% keeps the value in is printed, the generator is run, and the line it writes for the property is
% printed with whether it evaluates back to the value. Headless.
adapters = appdesigner.internal.appmetadata.getProductionComponentAdapterMap();
gen = appdesigner.internal.codegeneration.ComponentCodeGenerator();
fig = uifigure('Visible', 'off');
cases = {
    % component maker, property, value
    @() uilabel(fig),       'Text',        "two" + newline + "lines"
    @() uibutton(fig),      'Text',        "two" + newline + "lines"
    @() uieditfield(fig),   'Placeholder', "two" + newline + "lines"
    @() uibutton(fig),      'Tooltip',     "two" + newline + "lines"
    @() uilabel(fig),       'Tooltip',     "two" + newline + "lines"
    @() uitextarea(fig),    'Value',       "two" + newline + "lines"
    @() uitextarea(fig),    'Placeholder', "two" + newline + "lines"
    @() uilistbox(fig),     'Items',       ["a" "b"]
    @() uidropdown(fig),    'Items',       {"a", "b"}
    @() uitable(fig),       'ColumnName',  {"A"; "B"}
    @() uitable(fig),       'ColumnName',  ["A" "B"]
    @() uitable(fig),       'ColumnWidth', {100, [50 60]}
    @() uitable(fig),       'ColumnFormat', {'char', {'x', 'y'}}
    @() uigridlayout(fig),  'RowHeight',   {'1x', "fit"}
    @() uitextarea(fig),    'Value',       {'a', 'b'; 'c', 'd'}
    };
for k = 1:size(cases, 1)
    h = cases{k, 1}();
    name = cases{k, 2};
    try
        h.(name) = cases{k, 3};
    catch e
        fprintf('%2d %-18s %-12s refused: %s\n', k, class(h), name, e.identifier); continue
    end
    held = h.(name);
    shape = sprintf('%s %s', class(held), mat2str(size(held)));
    if iscell(held), shape = [shape ' of ' strjoin(unique(cellfun(@class, held(:), 'UniformOutput', false)), '/')]; end
    adapter = eval(adapters(class(h)));
    code = gen.getComponentGenerationCode(h, adapter, 'Standard', 'light');
    line = code(startsWith(code, ['ad_OBJECTNAME_ad.ad_CODENAME_ad.' name ' = ']) | startsWith(code, ['app.ad_CODENAME_ad.' name ' = ']));
    if isempty(line)
        fprintf('%2d %-18s %-12s keeps %-28s generator writes no line\n', k, class(h), name, shape); continue
    end
    rhs = regexprep(line{1}, '^[^=]*= (.*);$', '$1');
    try
        back = eval(rhs);
        same = isequal(back, held);
        note = sprintf('reads back equal: %d', same);
    catch e
        note = ['does not evaluate: ' e.identifier];
    end
    fprintf('%2d %-18s %-12s keeps %-28s writes %-40s %s\n', k, class(h), name, shape, rhs, note);
end
delete(fig);
