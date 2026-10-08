% record: -noFigureWindows
% U12 of the app-building plan: what the regenerated property coverage found missing - the root's
% CallbackObject, FixedWidthFontName, PointerLocation and ScreenDepth, an axes toolbar button's
% ButtonPushedFcn or ValueChangedFcn, and h(1) on one object. Probes u12_props, u12_more. The
% pointer is only read here, and written wrongly: a good write moves the mouse.
r = groot;
names = fieldnames(get(r));
u9b_chk('root_listed', @() cellfun(@(n) any(strcmp(names, n)), {'CallbackObject', 'FixedWidthFontName', 'PointerLocation', 'ScreenDepth'}));
u9b_chk('root_font', @() r.FixedWidthFontName);
set(r, 'FixedWidthFontName', 'Consolas');
u9b_chk('root_font_set', @() get(r, 'FixedWidthFontName'));
u9b_chk('root_font_num', @() set(r, 'FixedWidthFontName', 5));
r.FixedWidthFontName = "Lucida Console";
u9b_chk('root_font_string', @() r.FixedWidthFontName);
set(r, 'FixedWidthFontName', 'Courier New');
u9b_chk('root_depth', @() r.ScreenDepth);
set(r, 'ScreenDepth', 8);
u9b_chk('root_depth_set', @() r.ScreenDepth);
set(r, 'ScreenDepth', 32);
p = r.PointerLocation;
u9b_chk('root_pointer', @() {class(p), size(p)});
u9b_chk('root_pointer_bad', @() set(r, 'PointerLocation', [1 2 3]));
u9b_chk('root_cbo_empty', @() isempty(r.CallbackObject));
u9b_chk('root_cbo_set', @() set(r, 'CallbackObject', []));
seen = {};
f = figure('Visible', 'off', 'DeleteFcn', @(s, e) assignin('base', 'seen', {isequal(get(groot, 'CallbackObject'), s), isequal(gcbo, s)}));
delete(f);
u9b_chk('root_cbo_in_callback', @() seen);
u9b_chk('root_cbo_after', @() isempty(get(groot, 'CallbackObject')));

% --- an axes toolbar's buttons
f = figure('Visible', 'off');
ax = axes(f);
tb = axtoolbar(ax);
pb = axtoolbarbtn(tb, 'push');
sb = axtoolbarbtn(tb, 'state');
u9b_chk('push_default', @() pb.ButtonPushedFcn);
u9b_chk('state_default', @() sb.ValueChangedFcn);
pn = fieldnames(get(pb));
sn = fieldnames(get(sb));
u9b_chk('push_listed', @() [any(strcmp(pn, 'ButtonPushedFcn')), any(strcmp(pn, 'ValueChangedFcn'))]);
u9b_chk('state_listed', @() [any(strcmp(sn, 'ButtonPushedFcn')), any(strcmp(sn, 'ValueChangedFcn'))]);
% (compared, not printed: func2str keeps an anonymous function's spaces here, ADR 0204)
pushed = @(s, e) disp(e);
pb.ButtonPushedFcn = pushed;
sb.ValueChangedFcn = 'disp(1)';
u9b_chk('push_set', @() {class(pb.ButtonPushedFcn), isequal(pb.ButtonPushedFcn, pushed)});
u9b_chk('state_set', @() sb.ValueChangedFcn);
u9b_chk('push_isprop', @() [isprop(pb, 'ButtonPushedFcn'), isprop(pb, 'ValueChangedFcn')]);
u9b_chk('state_isprop', @() [isprop(sb, 'ButtonPushedFcn'), isprop(sb, 'ValueChangedFcn')]);
u9b_chk('push_numeric', @() set(pb, 'ButtonPushedFcn', 5));
p2 = axtoolbarbtn(tb, 'push', 'ButtonPushedFcn', pushed);
u9b_chk('push_pair', @() isequal(p2.ButtonPushedFcn, pushed));
pb.ButtonPushedFcn = '';
u9b_chk('push_cleared', @() pb.ButtonPushedFcn);
delete(f);

% --- one object, subscripted
s = uistyle('FontWeight', 'bold');
u9b_chk('obj_one', @() class(s(1)));
u9b_chk('obj_one_one', @() class(s(1, 1)));
u9b_chk('obj_end', @() class(s(end)));
u9b_chk('obj_colon', @() class(s(:)));
u9b_chk('obj_empty_parens', @() class(s()));
u9b_chk('obj_true', @() class(s(true)));
u9b_chk('obj_field', @() s(1).FontWeight);
% an object's subscript is refused as any one-element array's is, in R2025b's words (item 79, ADR 0214)
u9b_chk('obj_two', @() s(2));
u9b_chk('obj_zero', @() s(0));
