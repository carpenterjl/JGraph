function m = ix_msg(f)
% IX_MSG  Call f for no output. Return the message of the error it throws, one line, with any
%   hyperlink reduced to its text; or 'none'.
try
    f();
    m = 'none';
catch e
    m = ix_flat(e.message);
end
end
