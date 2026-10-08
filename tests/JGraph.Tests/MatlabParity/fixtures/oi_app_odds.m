% Open items 50 and 80 (ADR 0217): uifigure and uiaxes run a CreateFcn named among their options,
% once the others are set, with the new object as gcbo and [] as the event; dialog keeps one without
% running it. uiconfirm refuses a figure that is not visible. Every figure here stays invisible.
% Probes probe_b7 and probe_b7b (open-items scratch).

global oi_log
oi_log = {};
f = uifigure('Visible', 'off', 'CreateFcn', @(s, e) oi_note('uifigure', s, e), 'Name', 'nm');
u9b_chk('uifigure_ran', @() oi_log);
oi_log = {};
ax = uiaxes(f, 'CreateFcn', @(s, e) oi_note('uiaxes', s, e), 'Tag', 'tg');
u9b_chk('uiaxes_ran', @() oi_log);
oi_log = {};
d = dialog('Visible', 'off', 'CreateFcn', @(s, e) oi_note('dialog', s, e), 'Name', 'dn');
u9b_chk('dialog_kept', @() oi_log);
u9b_chk('dialog_stored', @() class(d.CreateFcn));
u9b_chk('uifigure_stored', @() class(f.CreateFcn));
u9b_chk('uiaxes_stored', @() class(ax.CreateFcn));
oi_log = {};
f2 = uifigure('Visible', 'off', 'CreateFcn', 'global oi_log; oi_log{end+1} = ''text callback'';');
u9b_chk('text_callback', @() oi_log);
u9b_chk('confirm_invisible', @() uiconfirm(f, 'Message', 'Title'));
delete(d); delete(f2); delete(f);

function oi_note(kind, s, e)
global oi_log
name = '';
if ~strcmp(kind, 'uiaxes'), name = s.Name; end   % an axes has no Name in R2025b (item 82)
tag = '';
if isprop(s, 'Tag'), tag = s.Tag; end
oi_log{end+1} = sprintf('%s name=%s tag=%s event=%s gcbo=%d', kind, name, tag, class(e), isequal(gcbo, s));
end
