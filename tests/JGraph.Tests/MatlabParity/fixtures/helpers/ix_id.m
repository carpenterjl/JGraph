function id = ix_id(f)
% IX_ID  Call f for no output. Return the identifier of the error it throws, or 'none'.
try
    f();
    id = 'none';
catch e
    id = e.identifier;
end
end
