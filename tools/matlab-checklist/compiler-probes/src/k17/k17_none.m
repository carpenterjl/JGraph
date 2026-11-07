function k17_none()
%K17_NONE Takes nothing and answers nothing (compiler and SDK plan, stage K17).
persistent calls
if isempty(calls), calls = 0; end
calls = calls + 1;
end
