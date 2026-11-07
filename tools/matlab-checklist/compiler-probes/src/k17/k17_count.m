function n = k17_count()
%K17_COUNT One more each call, kept in a persistent (stage K17: one runtime per process).
persistent calls
if isempty(calls), calls = 0; end
calls = calls + 1;
n = calls;
end
