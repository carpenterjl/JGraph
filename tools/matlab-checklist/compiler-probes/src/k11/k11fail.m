function k11fail(message)
%K11FAIL Raise an error with the message given, from a local function, for the stack trace.
deeper(message);
end

function deeper(message)
error('k11:fail', '%s', message);
end
