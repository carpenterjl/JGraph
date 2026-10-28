function n = acc_count()
%ACC_COUNT The acceptance set's persistent state: one more each call (stage K22).
persistent calls
if isempty(calls)
    calls = 0;
end
calls = calls + 1;
n = calls;
end
