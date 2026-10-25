function k10quiet()
%K10QUIET Turn the runtime's beep off. The C client calls this first: a deployed component beeps
%(MessageBeep) on every error it raises, and the probe raises errors on purpose.
beep('off');
end
