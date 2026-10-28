function acc_fail(msg)
%ACC_FAIL The acceptance set's error, with an identifier and the caller's text (stage K22).
arguments
    msg (1,:) char
end
error('acc:fail', 'acc_fail: %s', msg);
end
