w = which('max','-all'); disp(numel(w)); disp(class(w));
s = w{3}; disp(s); disp(double(s(end-20:end)));
t = regexp(s, '@(double|single|int8|int16|int32|int64|uint8|uint16|uint32|uint64|logical|char|cell|struct|function_handle|string)[\/]', 'tokens', 'once'); disp(t);
t2 = regexp(s, '@(\w+)', 'tokens'); disp(t2);
