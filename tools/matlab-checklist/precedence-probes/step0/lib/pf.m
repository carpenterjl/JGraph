function r = pf()
try
    r = evalin('caller', 'helper(1)');
catch e
    r = ['ERR: ' e.message];
end
end
