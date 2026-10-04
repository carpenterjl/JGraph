function u1_eventdata
% U1 probe: the event data R2025b hands the callbacks a script can fire headless.
f = figure('Visible', 'off');
h = uicontrol(f, 'Style', 'pushbutton');
h.DeleteFcn = @(s, e) report('uicontrol DeleteFcn', s, e);
delete(h);
a = axes(f);
a.DeleteFcn = @(s, e) report('axes DeleteFcn', s, e);
delete(a);
f.DeleteFcn = @(s, e) report('figure DeleteFcn', s, e);
f.CloseRequestFcn = @(s, e) report('figure CloseRequestFcn', s, e);
close(f);
f.CloseRequestFcn = 'closereq';
close(f);
fprintf('figure closed: %d\n', ~isgraphics(f));
for c = {'matlab.ui.eventdata.ActionData', 'matlab.ui.eventdata.WindowCloseRequestData', 'event.EventData', ...
        'matlab.ui.eventdata.KeyData', 'matlab.graphics.eventdata.Hit', 'matlab.ui.eventdata.WindowMouseData', ...
        'matlab.ui.eventdata.ScrollWheelData', 'matlab.ui.eventdata.SizeChangedData', 'matlab.ui.eventdata.MenuSelectedData'}
    m = meta.class.fromName(c{1});
    props = arrayfun(@(p) p.Name, m.PropertyList(~[m.PropertyList.Hidden]), 'UniformOutput', false);
    fprintf('%s: supers=%s props=%s\n', c{1}, strjoin(superclasses(c{1})', ','), strjoin(props', ','));
end
end

function report(what, s, e)
fprintf('%s: class(e)=%s', what, class(e));
if isobject(e)
    fprintf(' EventName=%s Source=%s', e.EventName, class(e.Source));
    fprintf(' fields=%s', strjoin(properties(e)', ','));
end
fprintf(' isa EventData=%d\n', isa(e, 'event.EventData'));
end
