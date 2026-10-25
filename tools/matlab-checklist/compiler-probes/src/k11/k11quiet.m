function k11quiet()
%K11QUIET Turn the runtime's beep off. The client calls this first: a deployed component beeps
%(MessageBeep) on every error it raises, and the client raises one on purpose.
beep('off');
end
