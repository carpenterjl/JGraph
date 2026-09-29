function sp_cbevt(src, evt)
% SP_CBEVT  A BytesAvailableFcn that logs what its event data is: class, fields, and display.
global CBLOG
CBLOG{end + 1} = class(src);
CBLOG{end + 1} = class(evt);
CBLOG{end + 1} = strjoin(properties(evt)', ',');
CBLOG{end + 1} = strtrim(regexprep(evalc('disp(evt)'), '\s*\n\s*', ' | '));
try
    CBLOG{end + 1} = evt.BytesAvailableFcnCount;
catch e
    CBLOG{end + 1} = e.identifier;
end
try
    CBLOG{end + 1} = class(evt.AbsTime);
    CBLOG{end + 1} = abs(etime(datevec(evt.AbsTime), clock)) < 5;
catch e
    CBLOG{end + 1} = e.identifier;
end
CBLOG{end + 1} = strjoin(superclasses(evt)', ',');
end
