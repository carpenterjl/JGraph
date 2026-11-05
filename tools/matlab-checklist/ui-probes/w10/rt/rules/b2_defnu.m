function b2_defnu()
f = @unused2;
cellfun(@unused3, {1});
feval('unused4');
disp(f)
    function nestedUnused()
    end
end
function unused2()
end
function unused3(a)
disp(a)
end
function unused4()
end
function unused5(varargin)
end
