function k0_quiet()
%K0_QUIET Record the beep state, then turn the beep off. Every probe main calls this first: a
%deployed program beeps (MessageBeep through libmwnativedisplay.dll) and the probes raise errors
%on purpose.
s = beep;
beep('off');
fprintf('beep.default\t%s\n', s);
end
