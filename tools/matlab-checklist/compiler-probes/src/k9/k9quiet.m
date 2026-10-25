function old = k9quiet()
%K9QUIET Turn the beep off and say what it was.
old = beep;
beep('off');
end
