% U0: can the App Testing Framework drive components headless under -batch?
% If it can, it is an oracle for user gestures (press, type, choose, drag) without a window.
% Everything stays Visible off: no window may open.
say('batch=%d desktop=%d', batchStartupOptionUsed, usejava('desktop'));
f = uifigure('Visible','off');
b = uibutton(f,'push','Text','Go');
b.ButtonPushedFcn = @(s,e) fprintf('ButtonPushed evt=%s\n', class(e));
tc = matlab.uitest.TestCase.forInteractiveUse;
try
    tc.press(b);
    drawnow;
    say('press on invisible uifigure: ok');
catch e
    say('press on invisible uifigure: %s | %s', e.identifier, e.message);
end
ef = uieditfield(f,'text');
ef.ValueChangedFcn = @(s,e) fprintf('ValueChanged prev=%s new=%s evt=%s\n', e.PreviousValue, e.Value, class(e));
try
    tc.type(ef,'hello');
    drawnow;
    say('type: ok value=%s', ef.Value);
catch e
    say('type: %s | %s', e.identifier, e.message);
end
g = figure('Visible','off');
u = uicontrol(g,'Style','pushbutton','String','X','Callback',@(s,e) fprintf('uicontrol cb %s\n', class(e)));
try
    tc.press(u);
    drawnow;
    say('press uicontrol in invisible figure: ok');
catch e
    say('press uicontrol in invisible figure: %s | %s', e.identifier, e.message);
end
delete(g); delete(f);

function say(fmt, varargin)
    fprintf([fmt '\n'], varargin{:});
end
