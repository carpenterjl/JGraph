function r = pfp()
try
    r = evalin('caller', 'phelp()');
catch e
    r = ['ERR: ' e.message];
end
end
