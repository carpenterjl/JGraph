% u12_more - the root's four names and the axes toolbar buttons' callbacks (U12), headless.
r = groot;
fprintf('FixedWidthFontName = %s\n', r.FixedWidthFontName);
fprintf('ScreenDepth = %s (%s)\n', mat2str(r.ScreenDepth), class(r.ScreenDepth));
p = r.PointerLocation;
fprintf('PointerLocation class %s size %s\n', class(p), mat2str(size(p)));
c = r.CallbackObject;
fprintf('CallbackObject class %s size %s\n', class(c), mat2str(size(c)));
f = figure;
b = uicontrol(f, 'Callback', @(s, e) fprintf('in callback: CallbackObject is the source %d, gcbo too %d\n', ...
    isequal(get(groot, 'CallbackObject'), s), isequal(gcbo, s)));
feval(b.Callback, b, []);
% writes
set(r, 'FixedWidthFontName', 'Consolas'); fprintf('FixedWidthFontName after set = %s\n', r.FixedWidthFontName);
try, set(r, 'ScreenDepth', 8); fprintf('ScreenDepth set ok -> %s\n', mat2str(r.ScreenDepth)); catch e, fprintf('ScreenDepth set: %s | %s\n', e.identifier, e.message); end
try, set(r, 'CallbackObject', b); fprintf('CallbackObject set ok\n'); catch e, fprintf('CallbackObject set: %s | %s\n', e.identifier, e.message); end
try, set(r, 'FixedWidthFontName', 5); fprintf('FixedWidthFontName numeric ok -> %s\n', mat2str(r.FixedWidthFontName)); catch e, fprintf('FixedWidthFontName numeric: %s | %s\n', e.identifier, e.message); end
try, set(r, 'PointerLocation', [1 2 3]); catch e, fprintf('PointerLocation bad: %s | %s\n', e.identifier, e.message); end
set(r, 'FixedWidthFontName', 'Courier New');
% the toolbar buttons
ax = axes(figure); tb = axtoolbar(ax);
pb = axtoolbarbtn(tb, 'push'); sb = axtoolbarbtn(tb, 'state');
fprintf('push ButtonPushedFcn default %s, class %s\n', mat2str(pb.ButtonPushedFcn), class(pb.ButtonPushedFcn));
fprintf('state ValueChangedFcn default %s\n', mat2str(sb.ValueChangedFcn));
fprintf('push props: %s\n', strjoin(fieldnames(get(pb))', ' '));
fprintf('state props: %s\n', strjoin(fieldnames(get(sb))', ' '));
pb.ButtonPushedFcn = @(s, e) disp(e); sb.ValueChangedFcn = 'disp(1)';
fprintf('after set: %s | %s\n', func2str(pb.ButtonPushedFcn), sb.ValueChangedFcn);
try, pb.ValueChangedFcn = @disp; catch e, fprintf('push ValueChangedFcn: %s | %s\n', e.identifier, e.message); end
try, sb.ButtonPushedFcn = @disp; catch e, fprintf('state ButtonPushedFcn: %s | %s\n', e.identifier, e.message); end
try, pb.ButtonPushedFcn = 5; catch e, fprintf('push numeric: %s | %s\n', e.identifier, e.message); end
p2 = axtoolbarbtn(tb, 'push', 'ButtonPushedFcn', @(s, e) 1); fprintf('pair form ok: %s\n', func2str(p2.ButtonPushedFcn));
% the event data classes the documentation names
for n = {'matlab.graphics.controls.eventdata.ButtonPushedEventData', 'matlab.graphics.controls.eventdata.ValueChangedEventData', ...
         'matlab.graphics.controls.eventdata.ButtonPushedEvent', 'matlab.graphics.controls.eventdata.ValueChangedEvent', ...
         'matlab.graphics.controls.eventdata.SelectionChangedEventData'}
    m = meta.class.fromName(n{1});
    if isempty(m), fprintf('%s: none\n', n{1}); else
        fprintf('%s: %s\n', n{1}, strjoin(cellfun(@(x) x, {m.PropertyList(~[m.PropertyList.Hidden]).Name}, 'UniformOutput', false), ' '));
    end
end
w = what('matlab/graphics/controls/eventdata');
for k = 1:numel(w), fprintf('package eventdata: %s | %s\n', strjoin(w(k).m', ' '), strjoin(w(k).classes', ' ')); end
