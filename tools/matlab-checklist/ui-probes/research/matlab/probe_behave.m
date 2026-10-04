function probe_behave(outdir)
%PROBE_BEHAVE validation errors, value coercions, figure/uifigure rules
set(groot, 'DefaultFigureVisible', 'off');
fid = fopen(fullfile(outdir, 'behave.txt'), 'w');
cleaner = onCleanup(@() fclose(fid));
P = @(varargin) fprintf(fid, varargin{:});

uf = uifigure('Visible', 'off');
P('uifigure HandleVisibility=%s Units=%s Position=%s Color=%s Theme=%s\n', ...
    uf.HandleVisibility, uf.Units, mat2str(uf.Position), mat2str(uf.Color, 4), tryget(uf, 'Theme'));
P('after uifigure: groot.CurrentFigure isempty=%d, numel(findobj(groot,''type'',''figure''))=%d, numel(findall(groot,''type'',''figure''))=%d\n', ...
    isempty(get(groot, 'CurrentFigure')), numel(findobj(groot, 'type', 'figure')), numel(findall(groot, 'type', 'figure')));
g = gcf;
P('gcf with only a uifigure -> new figure? %d (isequal(g,uf)=%d) class %s HandleVisibility=%s\n', ~isequal(g, uf), isequal(g, uf), class(g), g.HandleVisibility);
delete(g);
f = figure('Visible', 'off');
P('figure HandleVisibility=%s Units=%s Position=%s Color=%s\n', f.HandleVisibility, f.Units, mat2str(f.Position), mat2str(f.Color, 4));
P('isequal(gcf,f)=%d\n', isequal(gcf, f));
P('matlab.ui.internal.isUIFigure(uf)=%s isUIFigure(f)=%s\n', trycall(@() matlab.ui.internal.isUIFigure(uf)), trycall(@() matlab.ui.internal.isUIFigure(f)));

% ---- defaults
b = uibutton(uf);
P('\n== uibutton defaults: Position=%s FontName=%s FontSize=%g FontWeight=%s FontColor=%s BackgroundColor=%s Enable=%s Visible=%s Tooltip=%s Tag=%s UserData=%s Text=%s\n', ...
    mat2str(b.Position), b.FontName, b.FontSize, b.FontWeight, mat2str(b.FontColor, 4), mat2str(b.BackgroundColor, 4), ...
    char(b.Enable), char(b.Visible), v2s(b.Tooltip), v2s(b.Tag), v2s(b.UserData), v2s(b.Text));
c = uicontrol(f);
P('== uicontrol defaults: Style=%s Units=%s Position=%s FontName=%s FontSize=%g FontUnits=%s BackgroundColor=%s Enable=%s Visible=%s Tooltip=%s String=%s Value=%s Min=%g Max=%g\n', ...
    c.Style, c.Units, mat2str(c.Position), c.FontName, c.FontSize, c.FontUnits, mat2str(c.BackgroundColor, 4), ...
    char(c.Enable), char(c.Visible), v2s(c.Tooltip), v2s(c.String), v2s(c.Value), c.Min, c.Max);
P('groot DefaultUicontrolFontName=%s DefaultUicontrolFontSize=%g FixedWidthFontName=%s\n', ...
    get(groot, 'DefaultUicontrolFontName'), get(groot, 'DefaultUicontrolFontSize'), get(groot, 'FixedWidthFontName'));

% ---- error captures
T = {};
T(end+1, :) = {'numeric edit Value=''abc''', @() set(uieditfield(uf, 'numeric'), 'Value', 'abc')};
T(end+1, :) = {'numeric edit Value=200 with Limits [0 100]', @() set(uieditfield(uf, 'numeric', 'Limits', [0 100]), 'Value', 200)};
T(end+1, :) = {'numeric edit Limits=[5 1]', @() set(uieditfield(uf, 'numeric'), 'Limits', [5 1])};
T(end+1, :) = {'numeric edit Value=[1 2]', @() set(uieditfield(uf, 'numeric'), 'Value', [1 2])};
T(end+1, :) = {'numeric edit Value=NaN', @() set(uieditfield(uf, 'numeric'), 'Value', NaN)};
T(end+1, :) = {'text edit Value=5', @() set(uieditfield(uf), 'Value', 5)};
T(end+1, :) = {'dropdown Value not in Items', @() set(uidropdown(uf, 'Items', {'a', 'b'}), 'Value', 'zz')};
T(end+1, :) = {'dropdown ItemsData Value not in ItemsData', @() set(uidropdown(uf, 'Items', {'a', 'b'}, 'ItemsData', [10 20]), 'Value', 30)};
T(end+1, :) = {'listbox Value not in Items', @() set(uilistbox(uf), 'Value', 'nope')};
T(end+1, :) = {'slider Value=500', @() set(uislider(uf), 'Value', 500)};
T(end+1, :) = {'slider Limits=[1 1]', @() set(uislider(uf), 'Limits', [1 1])};
T(end+1, :) = {'spinner Value=1.5 RoundFractionalValues on', @() set(uispinner(uf, 'RoundFractionalValues', 'on'), 'Value', 1.5)};
T(end+1, :) = {'checkbox Value=2', @() set(uicheckbox(uf), 'Value', 2)};
T(end+1, :) = {'checkbox Value=''yes''', @() set(uicheckbox(uf), 'Value', 'yes')};
T(end+1, :) = {'switch Value=''Maybe''', @() set(uiswitch(uf), 'Value', 'Maybe')};
T(end+1, :) = {'button Text=5', @() set(uibutton(uf), 'Text', 5)};
T(end+1, :) = {'button Position=[1 2 3]', @() set(uibutton(uf), 'Position', [1 2 3])};
T(end+1, :) = {'button Position width -5', @() set(uibutton(uf), 'Position', [1 2 -5 20])};
T(end+1, :) = {'button FontSize=-1', @() set(uibutton(uf), 'FontSize', -1)};
T(end+1, :) = {'button Enable=''maybe''', @() set(uibutton(uf), 'Enable', 'maybe')};
T(end+1, :) = {'button ButtonPushedFcn=5', @() set(uibutton(uf), 'ButtonPushedFcn', 5)};
T(end+1, :) = {'button ButtonPushedFcn={5}', @() set(uibutton(uf), 'ButtonPushedFcn', {5})};
T(end+1, :) = {'button Bogus property', @() set(uibutton(uf), 'Bogus', 1)};
T(end+1, :) = {'uibutton(uf,''Bogus'',1) ctor', @() uibutton(uf, 'Bogus', 1)};
T(end+1, :) = {'uibutton(uf,''sideways'') style', @() uibutton(uf, 'sideways')};
T(end+1, :) = {'button Units=''normalized''', @() set(uibutton(uf), 'Units', 'normalized')};
T(end+1, :) = {'gridlayout RowHeight={''2x'',''fit'',50,''bogus''}', @() set(uigridlayout(uf), 'RowHeight', {'2x', 'fit', 50, 'bogus'})};
T(end+1, :) = {'gridlayout RowHeight=-5', @() set(uigridlayout(uf), 'RowHeight', {-5})};
T(end+1, :) = {'gridlayout child Layout.Row=5 out of range', @() setLayout(uf)};
T(end+1, :) = {'knob discrete Value not in Items', @() set(uiknob(uf, 'discrete'), 'Value', 'Max')};
T(end+1, :) = {'gauge Value=1000 (out of Limits)', @() set(uigauge(uf), 'Value', 1000)};
T(end+1, :) = {'datepicker Value=''notadate''', @() set(uidatepicker(uf), 'Value', 'notadate')};
T(end+1, :) = {'colorpicker Value=''notacolor''', @() set(uicolorpicker(uf), 'Value', 'notacolor')};
T(end+1, :) = {'lamp Color=''zz''', @() set(uilamp(uf), 'Color', 'zz')};
T(end+1, :) = {'uicontrol Style=bogus', @() uicontrol(f, 'Style', 'bogus')};
T(end+1, :) = {'uicontrol Value=''x''', @() set(uicontrol(f), 'Value', 'x')};
T(end+1, :) = {'uicontrol Callback=5', @() set(uicontrol(f), 'Callback', 5)};
T(end+1, :) = {'uicontrol Units=''bogus''', @() set(uicontrol(f), 'Units', 'bogus')};
T(end+1, :) = {'uitable Data=struct', @() set(uitable(uf), 'Data', struct('a', 1))};
T(end+1, :) = {'uibutton parent = axes', @() uibutton(axes(f))};
T(end+1, :) = {'uiradiobutton parent = uifigure', @() uiradiobutton(uf)};
T(end+1, :) = {'uitab parent = uifigure', @() uitab(uf)};
T(end+1, :) = {'uitreenode parent = uifigure', @() uitreenode(uf)};
T(end+1, :) = {'uifigure Units=''normalized''', @() set(uf, 'Units', 'normalized')};
T(end+1, :) = {'uifigure HandleVisibility=''on''', @() set(uf, 'HandleVisibility', 'on')};
P('\n== ERROR CAPTURES\n');
for i = 1:size(T, 1)
    try
        r = T{i, 2}(); %#ok<NASGU>
        P('[%s] NO ERROR\n', T{i, 1});
    catch e
        P('[%s]\n   id=%s\n   msg=%s\n', T{i, 1}, e.identifier, strrep(e.message, newline, ' / '));
    end
end
set(uf, 'HandleVisibility', 'off');

% ---- coercions
try, P('\n== COERCIONS / VALUE TYPES\n'); catch e__, P('LINE 92 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, ef = uieditfield(uf, 'numeric'); catch e__, P('LINE 93 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
P('numeric edit default Value=%s class %s Limits=%s RoundFractionalValues=%s ValueDisplayFormat=%s AllowEmpty=%s LowerLimitInclusive=%s\n', ...
    v2s(ef.Value), class(ef.Value), mat2str(ef.Limits), char(ef.RoundFractionalValues), ef.ValueDisplayFormat, tryget(ef, 'AllowEmpty'), char(ef.LowerLimitInclusive));
try, ef.RoundFractionalValues = 'on'; ef.Value = 2.6; P('RoundFractionalValues on, set 2.6 -> %s\n', v2s(ef.Value)); catch e__, P('LINE 96 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, ef.Value = int8(5); P('set int8(5) -> %s class %s\n', v2s(ef.Value), class(ef.Value)); catch e__, P('LINE 97 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, ef.Value = true; P('set true -> %s class %s\n', v2s(ef.Value), class(ef.Value)); catch e__, P('LINE 98 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, ef.AllowEmpty = 'on'; ef.Value = []; P('AllowEmpty on, set [] -> %s\n', v2s(ef.Value)); catch e, P('AllowEmpty: %s\n', e.message); end
try, te = uieditfield(uf); catch e__, P('LINE 100 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, te.Value = "str"; P('text edit set string "str" -> %s class %s\n', v2s(te.Value), class(te.Value)); catch e__, P('LINE 101 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, ta = uitextarea(uf); P('textarea default Value=%s\n', v2s(ta.Value)); catch e__, P('LINE 102 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, ta.Value = 'one line'; P('textarea set char -> %s\n', v2s(ta.Value)); catch e__, P('LINE 103 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, ta.Value = ["a"; "b"]; P('textarea set string col -> %s\n', v2s(ta.Value)); catch e__, P('LINE 104 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, ta.Value = sprintf('x\ny'); P('textarea set char with newline -> %s\n', v2s(ta.Value)); catch e__, P('LINE 105 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, dd = uidropdown(uf); P('dropdown default Items=%s Value=%s ItemsData=%s Editable=%s Placeholder=%s\n', v2s(dd.Items), v2s(dd.Value), v2s(dd.ItemsData), char(dd.Editable), tryget(dd, 'Placeholder')); catch e__, P('LINE 106 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, dd.Items = {'x', 'y', 'z'}; P('after Items={x,y,z} Value=%s\n', v2s(dd.Value)); catch e__, P('LINE 107 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, dd.ItemsData = [1 2 3]; P('after ItemsData=[1 2 3] Value=%s\n', v2s(dd.Value)); catch e__, P('LINE 108 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, dd.Items = ["p", "q"]; P('Items set as string array -> Items=%s Value=%s\n', v2s(dd.Items), v2s(dd.Value)); catch e__, P('LINE 109 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, dd2 = uidropdown(uf, 'Items', {}); P('dropdown Items={} Value=%s\n', v2s(dd2.Value)); catch e__, P('LINE 110 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, dd3 = uidropdown(uf, 'Editable', 'on'); dd3.Value = 'free text'; P('editable dropdown set free text -> %s\n', v2s(dd3.Value)); catch e__, P('LINE 111 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, lb = uilistbox(uf); P('listbox default Items=%s Value=%s Multiselect=%s\n', v2s(lb.Items), v2s(lb.Value), char(lb.Multiselect)); catch e__, P('LINE 112 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, lb.Multiselect = 'on'; P('listbox Multiselect on -> Value=%s\n', v2s(lb.Value)); catch e__, P('LINE 113 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, lb.Value = {'Item 1', 'Item 3'}; P('listbox multi set -> %s\n', v2s(lb.Value)); catch e__, P('LINE 114 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, cb = uicheckbox(uf); cb.Value = 1; P('checkbox set 1 -> %s class %s\n', v2s(cb.Value), class(cb.Value)); catch e__, P('LINE 115 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, sl = uislider(uf); P('slider Value=%s Limits=%s MajorTicks=%s Orientation=%s Step=%s\n', v2s(sl.Value), mat2str(sl.Limits), v2s(sl.MajorTicks), sl.Orientation, tryget(sl, 'Step')); catch e__, P('LINE 116 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, sp = uispinner(uf); P('spinner Value=%s Limits=%s Step=%s\n', v2s(sp.Value), mat2str(sp.Limits), v2s(sp.Step)); catch e__, P('LINE 117 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, kd = uiknob(uf, 'discrete'); P('discrete knob Items=%s Value=%s\n', v2s(kd.Items), v2s(kd.Value)); catch e__, P('LINE 118 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, sw = uiswitch(uf); P('switch Items=%s Value=%s\n', v2s(sw.Items), v2s(sw.Value)); catch e__, P('LINE 119 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, sb = uibutton(uf, 'state'); P('state button Value=%s class %s\n', v2s(sb.Value), class(sb.Value)); catch e__, P('LINE 120 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, dp = uidatepicker(uf); P('datepicker Value=%s DisplayFormat=%s Limits=%s\n', v2s(dp.Value), dp.DisplayFormat, v2s(dp.Limits)); catch e__, P('LINE 121 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, cp = uicolorpicker(uf); P('colorpicker Value=%s\n', v2s(cp.Value)); cp.Value = 'red'; P('colorpicker set ''red'' -> %s\n', v2s(cp.Value)); catch e__, P('LINE 122 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, cp.Value = '#00FF00'; P('colorpicker set hex -> %s\n', v2s(cp.Value)); catch e__, P('LINE 123 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, lp = uilamp(uf); lp.Color = 'g'; P('lamp set ''g'' -> %s\n', v2s(lp.Color)); catch e__, P('LINE 124 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, b.BackgroundColor = 'red'; P('button BackgroundColor set red -> %s\n', v2s(b.BackgroundColor)); catch e__, P('LINE 125 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, b.BackgroundColor = '#FF8800'; P('button BackgroundColor set hex -> %s\n', v2s(b.BackgroundColor)); catch e__, P('LINE 126 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, b.Enable = true; P('button Enable=true -> %s class %s\n', v2s(b.Enable), class(b.Enable)); catch e__, P('LINE 127 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, b.Enable = 'off'; P('Enable stored: %s isequal(b.Enable,''off'')=%d logical=%d\n', v2s(b.Enable), isequal(b.Enable, 'off'), logical(b.Enable)); catch e__, P('LINE 128 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, b.Text = ["line1", "line2"]; P('button Text string row -> %s\n', v2s(b.Text)); catch e__, P('LINE 129 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, b.Tooltip = 'tip'; P('button Tooltip set char -> %s\n', v2s(b.Tooltip)); catch e__, P('LINE 130 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, b.ButtonPushedFcn = 'disp(1)'; P('callback char accepted -> %s\n', v2s(b.ButtonPushedFcn)); catch e__, P('LINE 131 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, b.ButtonPushedFcn = {@disp, 3}; P('callback cell accepted -> %s\n', v2s(b.ButtonPushedFcn)); catch e__, P('LINE 132 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, b.ButtonPushedFcn = @(s, e) disp(e); P('callback handle accepted -> %s\n', v2s(b.ButtonPushedFcn)); catch e__, P('LINE 133 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, b.ButtonPushedFcn = ''; P('callback '''' accepted -> %s\n', v2s(b.ButtonPushedFcn)); catch e__, P('LINE 134 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, b.ButtonPushedFcn = []; P('callback [] accepted -> %s\n', v2s(b.ButtonPushedFcn)); catch e__, P('LINE 135 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, tab = uitable(uf); P('uitable(uifigure) Data=%s ColumnName=%s ColumnEditable=%s ColumnWidth=%s RowName=%s\n', v2s(tab.Data), v2s(tab.ColumnName), v2s(tab.ColumnEditable), v2s(tab.ColumnWidth), v2s(tab.RowName)); catch e__, P('LINE 136 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, tab.Data = table([1; 2], {'a'; 'b'}); P('uitable table Data -> ColumnName=%s DisplayData=%s\n', v2s(tab.ColumnName), v2s(tab.DisplayData)); catch e__, P('LINE 137 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, tab.Data = magic(3); P('uitable magic(3) -> ColumnName=%s\n', v2s(tab.ColumnName)); catch e__, P('LINE 138 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end

% ---- grid layout
try, gl = uigridlayout(uf); catch e__, P('LINE 141 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
P('\n== GRID: default RowHeight=%s ColumnWidth=%s Padding=%s RowSpacing=%g ColumnSpacing=%g Scrollable=%s\n', ...
    v2s(gl.RowHeight), v2s(gl.ColumnWidth), mat2str(gl.Padding), gl.RowSpacing, gl.ColumnSpacing, char(gl.Scrollable));
try, gl.RowHeight = {'1x', 'fit', 40, '2x'}; P('set mixed -> %s\n', v2s(gl.RowHeight)); catch e__, P('LINE 144 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, gl.RowHeight = [30 40]; P('set numeric [30 40] -> %s\n', v2s(gl.RowHeight)); catch e__, P('LINE 145 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, gl.ColumnWidth = ["1x", "fit"]; P('set string array -> %s\n', v2s(gl.ColumnWidth)); catch e__, P('LINE 146 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, gl2 = uigridlayout(uf, [3 4]); P('uigridlayout(uf,[3 4]) RowHeight=%s ColumnWidth=%s\n', v2s(gl2.RowHeight), v2s(gl2.ColumnWidth)); catch e__, P('LINE 147 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, bb = uibutton(gl2); P('child auto-placed Layout.Row=%s Column=%s class(Layout)=%s\n', v2s(bb.Layout.Row), v2s(bb.Layout.Column), class(bb.Layout)); catch e__, P('LINE 148 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, bb2 = uibutton(gl2); P('second child Layout.Row=%s Column=%s\n', v2s(bb2.Layout.Row), v2s(bb2.Layout.Column)); catch e__, P('LINE 149 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, bb.Layout.Row = [1 2]; P('span set Row=[1 2] -> %s\n', v2s(bb.Layout.Row)); catch e__, P('LINE 150 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, P('button Position in grid (no drawnow) = %s\n', mat2str(bb.Position)); catch e__, P('LINE 151 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, bb.Position = [1 1 50 50]; P('set Position in grid: no error, Position now %s\n', mat2str(bb.Position)); catch e, P('set Position in grid: %s | %s\n', e.identifier, e.message); end
try, pl = uipanel(uf); P('uipanel(uifigure) Scrollable=%s Units=%s Position=%s BorderType=%s Title=%s FontSize=%g\n', char(pl.Scrollable), pl.Units, mat2str(pl.Position), pl.BorderType, v2s(pl.Title), pl.FontSize); catch e__, P('LINE 153 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, P('uifigure Scrollable=%s AutoResizeChildren=%s\n', char(uf.Scrollable), char(uf.AutoResizeChildren)); catch e__, P('LINE 154 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, pl2 = uipanel(f); P('uipanel(figure) Units=%s Position=%s\n', pl2.Units, mat2str(pl2.Position)); catch e__, P('LINE 155 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, P('uifigure WindowStyle=%s Resize=%s\n', uf.WindowStyle, char(uf.Resize)); catch e__, P('LINE 156 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end

% ---- uicontrol styles
try, P('\n== UICONTROL value semantics\n'); catch e__, P('LINE 159 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, c1 = uicontrol(f, 'Style', 'popupmenu', 'String', {'a', 'b', 'c'}); P('popupmenu Value=%s\n', v2s(c1.Value)); catch e__, P('LINE 160 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, c2 = uicontrol(f, 'Style', 'listbox', 'String', {'a', 'b', 'c'}); P('listbox Value=%s ListboxTop=%s Max=%g\n', v2s(c2.Value), v2s(c2.ListboxTop), c2.Max); catch e__, P('LINE 161 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, c3 = uicontrol(f, 'Style', 'slider'); P('slider Value=%s Min=%g Max=%g SliderStep=%s\n', v2s(c3.Value), c3.Min, c3.Max, mat2str(c3.SliderStep)); catch e__, P('LINE 162 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, c4 = uicontrol(f, 'Style', 'checkbox'); P('checkbox Value=%s Min=%g Max=%g\n', v2s(c4.Value), c4.Min, c4.Max); catch e__, P('LINE 163 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, c5 = uicontrol(f, 'Style', 'edit', 'String', 'hi'); P('edit String=%s Value=%s HorizontalAlignment=%s\n', v2s(c5.String), v2s(c5.Value), c5.HorizontalAlignment); catch e__, P('LINE 164 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, c6 = uicontrol(f, 'Style', 'text'); P('text HorizontalAlignment=%s BackgroundColor=%s\n', c6.HorizontalAlignment, mat2str(c6.BackgroundColor, 4)); catch e__, P('LINE 165 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, c3.Value = 5; drawnow; P('slider Value=5 out of [0 1]: no error, Value=%s\n', v2s(c3.Value)); catch e, P('slider out of range: %s | %s\n', e.identifier, e.message); end
try, lastw = lastwarn; P('lastwarn after slider out of range: %s\n', lastw); catch e__, P('LINE 167 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, c1.Value = 7; drawnow; P('popup Value=7 out of range: Value=%s lastwarn=%s\n', v2s(c1.Value), lastwarn); catch e__, P('LINE 168 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, c7 = uicontrol(f, 'Units', 'normalized'); P('uicontrol normalized Position=%s\n', mat2str(c7.Position, 4)); catch e__, P('LINE 169 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, c7.Units = 'pixels'; P('-> pixels %s\n', mat2str(c7.Position, 4)); catch e__, P('LINE 170 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, c8 = uicontrol(f, 'Style', 'pushbutton', 'String', 'Go', 'Callback', @(s, e) disp(class(e))); catch e__, P('LINE 171 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, P('uicontrol Callback stored %s\n', v2s(c8.Callback)); catch e__, P('LINE 172 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, P('uicontrol ButtonDownFcn/KeyPressFcn present: %d %d\n', isprop(c8, 'ButtonDownFcn'), isprop(c8, 'KeyPressFcn')); catch e__, P('LINE 173 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end

% ---- appdata/guidata
try, setappdata(f, 'k', 5); P('\n== appdata: isappdata=%d getappdata=%s keys=%s\n', isappdata(f, 'k'), v2s(getappdata(f, 'k')), v2s(fieldnames(getappdata(f)))); catch e__, P('LINE 176 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, guidata(c8, struct('x', 1)); P('guidata via child -> %s; stored in appdata UsedByGUIData_m: %d\n', v2s(guidata(f)), isappdata(f, 'UsedByGUIData_m')); catch e__, P('LINE 177 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, rmappdata(f, 'k'); P('after rmappdata isappdata=%d\n', isappdata(f, 'k')); catch e__, P('LINE 178 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, set(c8, 'Tag', 'goBtn'); h = guihandles(f); P('guihandles fields: %s\n', strjoin(fieldnames(h)', ' ')); catch e__, P('LINE 179 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, guidata(b, struct('y', 2)); P('guidata on uifigure child -> %s\n', v2s(guidata(uf))); catch e__, P('LINE 180 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end

% ---- uistyle
try, s = uistyle('BackgroundColor', 'red'); P('\n== uistyle class %s\n', class(s)); catch e__, P('LINE 183 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, addStyle(tab, s, 'cell', [1 1]); P('addStyle(table,cell) OK StyleConfigurations=%s\n', v2s(tab.StyleConfigurations)); catch e, P('addStyle table: %s\n', e.message); end
try, removeStyle(tab); P('removeStyle OK\n'); catch e, P('removeStyle: %s\n', e.message); end
try, addStyle(lb, uistyle('FontColor', 'b'), 'item', 1); P('addStyle(listbox,item) OK\n'); catch e, P('addStyle listbox: %s\n', e.message); end
try, addStyle(dd, uistyle('FontColor', 'b'), 'item', 1); P('addStyle(dropdown,item) OK\n'); catch e, P('addStyle dropdown: %s\n', e.message); end
try, addStyle(b, uistyle('FontColor', 'b')); P('addStyle(button) OK\n'); catch e, P('addStyle(button): %s | %s\n', e.identifier, e.message); end

% ---- focus / scroll
try, scroll(uf, 'bottom'); P('scroll(uf,bottom): no error\n'); catch e, P('scroll: %s | %s\n', e.identifier, e.message); end
try, r = isInScrollView(b); P('isInScrollView -> %s\n', v2s(r)); catch e, P('isInScrollView: %s | %s\n', e.identifier, e.message); end

try, P('uifigure Visible still off after scroll: %s\n', char(uf.Visible)); catch e__, P('LINE 194 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end

% ---- uiwait timeout on uifigure (non-blocking, 0.5 s)
try, t0 = tic; uiwait(uf, 0.5); P('uiwait(uf,0.5) returned after %.2f s\n', toc(t0)); catch e__, P('LINE 197 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end

% ---- programmatic Value set does not fire ValueChangedFcn
try, hits = 0; sl2 = uislider(uf, 'ValueChangedFcn', @(s, e) assignin('base', 'zzhit', 1)); catch e__, P('LINE 200 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, sl2.Value = 50; drawnow; catch e__, P('LINE 201 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, P('programmatic Value set fired ValueChangedFcn? %d\n', evalin('base', 'exist(''zzhit'',''var'')') == 1); catch e__, P('LINE 202 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end

% ---- uicontrol callback when invoked manually via the property
try, P('\n== nargin checks\n'); catch e__, P('LINE 205 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
P('nargin uialert=%d uiconfirm=%d uiprogressdlg=%d uigetfile=%d inputdlg=%d questdlg=%d listdlg=%d msgbox=%d waitbar=%d\n', ...
    nargin('uialert'), nargin('uiconfirm'), nargin('uiprogressdlg'), nargin('uigetfile'), nargin('inputdlg'), nargin('questdlg'), nargin('listdlg'), nargin('msgbox'), nargin('waitbar'));

% ---- event names on figures
try, P('\n== uifigure callbacks:\n'); catch e__, P('LINE 210 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, pf = properties(uf); P('%s\n', strjoin(pf(endsWith(pf, 'Fcn') | endsWith(pf, 'Callback'))', ' ')); catch e__, P('LINE 211 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, pf = properties(f); P('figure callbacks:\n%s\n', strjoin(pf(endsWith(pf, 'Fcn') | endsWith(pf, 'Callback'))', ' ')); catch e__, P('LINE 212 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, P('uifigure-only props vs figure: %s\n', strjoin(setdiff(properties(uf), properties(f))', ' ')); catch e__, P('LINE 213 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end
try, P('figure-only props vs uifigure: %s\n', strjoin(setdiff(properties(f), properties(uf))', ' ')); catch e__, P('LINE 214 ERR: %s | %s\n', e__.identifier, strrep(e__.message, newline, ' / ')); end

delete(f); delete(uf);
close all force;
end

function s = tryget(h, p)
try, s = v2s(h.(p)); catch, s = '<no such property>'; end
end
function s = trycall(fn)
try, s = v2s(fn()); catch e, s = ['<err ' e.message '>']; end
end
function setLayout(uf)
g = uigridlayout(uf, [2 2]);
b = uibutton(g);
b.Layout.Row = 5;
end
