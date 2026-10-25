function old = k8quiet()
%K8QUIET Turn the beep off and say what it was.
old = beep;
beep('off');
end
