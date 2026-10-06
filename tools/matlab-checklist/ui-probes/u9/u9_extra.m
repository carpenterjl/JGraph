function u9_extra
% U9 probe, after the first recording: the few rules the fixtures could not tell apart. Headless.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);

%% a knob's and a lamp's Position when the width and the height differ
k = uiknob(uf);
tryp('knob [10 10 80 40]', @() setget(k, 'Position', [10 10 80 40]));
tryp('knob [10 10 40 80]', @() setget(k, 'Position', [10 10 40 80]));
tryp('knob inner [10 10 80 40]', @() setget(k, 'InnerPosition', [10 10 80 40]));
tryp('knob [10 10 0 50]', @() setget(k, 'Position', [10 10 0 50]));
lp = uilamp(uf);
tryp('lamp [10 10 80 40]', @() setget(lp, 'Position', [10 10 80 40]));
tryp('lamp [10 10 40 80]', @() setget(lp, 'Position', [10 10 40 80]));
dk = uiknob(uf, 'discrete');
tryp('dknob [10 10 80 40]', @() setget(dk, 'Position', [10 10 80 40]));
g = uigauge(uf);
tryp('gauge [10 10 80 40]', @() setget(g, 'Position', [10 10 80 40]));
g2 = uigauge(uf, 'semicircular');
tryp('semi [10 10 80 40]', @() setget(g2, 'Position', [10 10 80 40]));
g3 = uigauge(uf, 'ninetydegree');
tryp('ninety [10 10 80 40]', @() setget(g3, 'Position', [10 10 80 40]));
sw = uiswitch(uf);
tryp('switch [10 10 80 40]', @() setget(sw, 'Position', [10 10 80 40]));
cp = uicolorpicker(uf);
tryp('colorpicker [10 10 80 40]', @() setget(cp, 'Position', [10 10 80 40]));

%% the value when a disabled weekday is written over it
dp = uidatepicker(uf, 'Value', datetime(2024, 1, 15));
tryp('dp weekday over value', @() setget(dp, 'DisabledDaysOfWeek', 2, 'Value'));
tryp('dp value format after', @() dp.Value.Format);
dp2 = uidatepicker(uf, 'Value', datetime(2024, 1, 15));
tryp('dp disabled date over value', @() setget(dp2, 'DisabledDates', datetime(2024, 1, 15), 'Value'));
tryp('dp2 value format after', @() dp2.Value.Format);
dp3 = uidatepicker(uf, 'Value', datetime(2024, 1, 15));
tryp('dp limits over value', @() setget(dp3, 'Limits', [datetime(2025, 1, 1) datetime(2025, 2, 1)], 'Value'));
tryp('dp3 value format after', @() dp3.Value.Format);
tryp('dp3 value format then DisplayFormat', @() setget(dp3, 'DisplayFormat', 'uuuu-MM-dd', 'Value'));
dp4 = uidatepicker(uf);
tryp('dp4 NaT format fresh', @() dp4.Value.Format);
tryp('dp4 value NaT written', @() setget(dp4, 'Value', NaT));
tryp('dp4 value NaT format', @() dp4.Value.Format);
tryp('dp4 disabled empty datetime', @() setget(dp4, 'DisabledDates', datetime.empty));
tryp('dp4 disabled empty datetime 0x1', @() setget(dp4, 'DisabledDates', datetime.empty(0, 1)));
tryp('dp4 disabled empty cell', @() setget(dp4, 'DisabledDates', {}));
tryp('dp4 disabled empty char', @() setget(dp4, 'DisabledDates', ''));
tryp('dp4 limits 2x1', @() setget(dp4, 'Limits', [datetime(2024, 1, 1); datetime(2024, 2, 1)]));
tryp('dp4 limits 1x3', @() setget(dp4, 'Limits', datetime(2024, 1, [1 2 3])));
tryp('dp4 disabled 2x2', @() setget(dp4, 'DisabledDates', reshape(datetime(2024, 1, [1 2 3 4]), 2, 2)));
tryp('dp4 value year 0', @() setget(dp4, 'Value', datetime(0, 1, 1)));
tryp('dp4 value year 9999 end', @() setget(dp4, 'Value', datetime(9999, 12, 31)));
tryp('dp4 value year 10000', @() setget(dp4, 'Value', datetime(10000, 1, 1)));
tryp('datetime(0,1,1) year', @() year(datetime(0, 1, 1)));
tryp('datetime(-1,1,1) text', @() char(datetime(-1, 1, 1)));
tryp('datetime(-1,1,1) year', @() year(datetime(-1, 1, 1)));
tryp('datetime(0,1,1)-1 text', @() char(datetime(0, 1, 1) - days(1)));

%% a colour picker's Icon words
tryp('cp icon text', @() setget(cp, 'Icon', 'text'));
tryp('cp icon TEXT', @() setget(cp, 'Icon', 'TEXT'));
tryp('cp icon te', @() setget(cp, 'Icon', 'te'));
tryp('cp icon t', @() setget(cp, 'Icon', 't'));
tryp('cp icon none', @() setget(cp, 'Icon', 'none'));
tryp('cp icon default', @() setget(cp, 'Icon', 'default'));
tryp('cp icon error', @() setget(cp, 'Icon', 'error'));
tryp('cp icon "text"', @() setget(cp, 'Icon', "text"));
tryp('cp icon text then cube', @() setget(cp, 'Icon', zeros(2, 2, 3)));
tryp('cp icon text again', @() setget(cp, 'Icon', 'text'));
tryp('cp icon class', @() class(cp.Icon));
tn = uitreenode(uitree(uf));
tryp('node icon text', @() setget(tn, 'Icon', 'text'));
b = uibutton(uf);
tryp('button icon text', @() setget(b, 'Icon', 'text'));

%% items rewritten under a value, and ItemsData of a discrete knob
dk2 = uiknob(uf, 'discrete', 'Items', {'a', 'b', 'c'}, 'Value', 'b');
tryp('dknob items keep value', @() setget(dk2, 'Items', {'c', 'b'}, 'Value', 'ValueIndex'));
tryp('dknob items lose value', @() setget(dk2, 'Items', {'x', 'y'}, 'Value', 'ValueIndex'));
tryp('dknob items lose value index 2', @() setget(dk2, 'Value', 'y'));
tryp('dknob items 3 keep index?', @() setget(dk2, 'Items', {'p', 'q', 'r'}, 'Value', 'ValueIndex'));
tryp('dknob itemsdata 1 element', @() setget(dk2, 'ItemsData', 5));
tryp('dknob itemsdata 2 of 3', @() setget(dk2, 'ItemsData', [10 20], 'Value', 'ValueIndex'));
tryp('dknob itemsdata 4 of 3', @() setget(dk2, 'ItemsData', [10 20 30 40], 'Value', 'ValueIndex'));
tryp('dknob itemsdata char abc', @() setget(dk2, 'ItemsData', 'abc', 'Value', 'ValueIndex'));
tryp('dknob itemsdata string scalar', @() setget(dk2, 'ItemsData', "ab"));
tryp('dknob itemsdata empty string', @() setget(dk2, 'ItemsData', ""));
tryp('dknob itemsdata empty', @() setget(dk2, 'ItemsData', [], 'Value', 'ValueIndex'));
sw2 = uiswitch(uf, 'Value', 'On');
tryp('switch items lose value', @() setget(sw2, 'Items', {'Lo', 'Hi'}, 'Value', 'ValueIndex'));
tryp('switch value hi', @() setget(sw2, 'Value', 'Hi', 'Value', 'ValueIndex'));
tryp('switch items swapped keep', @() setget(sw2, 'Items', {'Hi', 'Lo'}, 'Value', 'ValueIndex'));
tryp('switch itemsdata over index 1', @() setget(sw2, 'ItemsData', [0 1], 'Value', 'ValueIndex'));
tryp('switch value 1 datum', @() setget(sw2, 'Value', 1, 'Value', 'ValueIndex'));
tryp('switch itemsdata rewritten', @() setget(sw2, 'ItemsData', [5 6], 'Value', 'ValueIndex'));
tryp('switch itemsdata cleared', @() setget(sw2, 'ItemsData', [], 'Value', 'ValueIndex'));
dd = uidropdown(uf, 'Items', {'a', 'b', 'c'}, 'Value', 'b');
tryp('dropdown items lose value', @() setget(dd, 'Items', {'x', 'y'}, 'Value', 'ValueIndex'));
dd.Value = 'y';
tryp('dropdown itemsdata over index 2', @() setget(dd, 'ItemsData', [10 20], 'Value', 'ValueIndex'));
tryp('dropdown itemsdata rewritten', @() setget(dd, 'ItemsData', [5 6], 'Value', 'ValueIndex'));

%% on/off with blanks, and a datetime written to text
tryp('enable off blank', @() setget(b, 'Enable', 'off '));
tryp('enable blank on', @() setget(b, 'Enable', ' on'));
tryp('enable On blank', @() setget(b, 'Enable', 'On  '));
tryp('enable tab on', @() setget(b, 'Enable', sprintf('on\t')));
tryp('button text datetime', @() setget(b, 'Text', datetime(2024, 1, 15)));
[~, wid] = lastwarn; fprintf('WARN after button text datetime : %s\n', wid);
lastwarn('');
tryp('button fontname datetime', @() setget(b, 'FontName', datetime(2024, 1, 15)));
[~, wid] = lastwarn; fprintf('WARN after button fontname datetime : %s\n', wid);
lastwarn('');
tryp('knob value datetime', @() setget(k, 'Value', datetime(2024, 1, 15)));
[~, wid] = lastwarn; fprintf('WARN after knob value datetime : %s\n', wid);
lastwarn('');
tryp('knob limits datetime row', @() setget(k, 'Limits', [datetime(2024, 1, 15) datetime(2024, 3, 5)]));
[~, wid] = lastwarn; fprintf('WARN after knob limits datetime : %s\n', wid);
lastwarn('');
tryp('button tooltip datetime', @() setget(b, 'Tooltip', datetime(2024, 1, 15)));
[~, wid] = lastwarn; fprintf('WARN after button tooltip datetime : %s\n', wid);
lastwarn('');
tryp('button tag datetime', @() setget(b, 'Tag', datetime(2024, 1, 15)));
[~, wid] = lastwarn; fprintf('WARN after button tag datetime : %s\n', wid);
lastwarn('');
tryp('dropdown items datetime', @() setget(dd, 'Items', datetime(2024, 1, 15)));
[~, wid] = lastwarn; fprintf('WARN after dropdown items datetime : %s\n', wid);
lastwarn('');
tryp('dp value char', @() setget(dp4, 'Value', 'abc'));
[~, wid, ] = lastwarn; fprintf('WARN after dp value char : %s\n', wid);
[wmsg, ~] = lastwarn; fprintf('WARN message : %s\n', oneline(wmsg));
lastwarn('');
tryp('duration into text', @() setget(b, 'Text', seconds(5)));
[~, wid] = lastwarn; fprintf('WARN after duration into text : %s\n', wid);

%% styles
t = uitable(uf, 'Data', magic(4));
addStyle(t, uistyle('FontWeight', 'bold'), 'row', [1; 3]);
tryp('style index column stored', @() size(t.StyleConfigurations.TargetIndex{1}));
addStyle(t, uistyle('FontWeight', 'bold'), 'cell', [1 1; 2 2]);
tryp('style cell pairs stored', @() size(t.StyleConfigurations.TargetIndex{2}));
tryp('style set', @() set(uistyle(), 'FontWeight', 'bold'));
tryp('style get', @() get(uistyle('FontWeight', 'bold'), 'FontWeight'));
tryp('style isprop', @() isprop(uistyle(), 'FontWeight'));

%% expand with several nodes, and move with a prefix
tr = uitree(uf); n1 = uitreenode(tr); n2 = uitreenode(tr); c1 = uitreenode(n1); c2 = uitreenode(n1);
tryp('expand two', @() setexpand([n1 n2]));
tryp('expand tree and node', @() setexpand([tr; n1]));
tryp('expand 5', @() setexpand(5));
tryp('move bef', @() move(c1, c2, 'bef'));
tryp('move b', @() move(c1, c2, 'b'));
tryp('move AFTER', @() move(c1, c2, 'AFTER'));
tryp('move x', @() move(c1, c2, 'x'));
tryp('copyobj node to figure', @() class(copyobj(n1, uf)));
tryp('copyobj node to tree', @() class(copyobj(n1, tr)));
tryp('copyobj node to node', @() class(copyobj(n1, n2)));
tryp('copyobj tree to figure', @() class(copyobj(tr, uf)));
delete(uf);
end

function s = setget(h, name, value, varargin)
set(h, name, value);
if isempty(varargin), varargin = {name}; end
parts = cell(1, numel(varargin));
for k = 1:numel(varargin)
    parts{k} = [varargin{k} '=' v2s(get(h, varargin{k}))];
end
s = strjoin(parts, ' ');
end

function s = setexpand(nodes)
expand(nodes);
s = 'ran';
end
