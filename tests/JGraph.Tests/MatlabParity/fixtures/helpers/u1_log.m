function u1_log(text)
% Appends one line to the U1 fixtures' callback log.
global U1LOG
U1LOG{end + 1} = text;
end
