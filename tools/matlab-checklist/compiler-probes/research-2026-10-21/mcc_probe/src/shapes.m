function [st, ce, lg, i8, cx, str] = shapes()
%SHAPES Return one value of each of several MATLAB classes.
st = struct('name', 'probe', 'value', [1 2 3]);
ce = {1, 'two', [3 4; 5 6]};
lg = [true false true];
i8 = int8([-5 0 127]);
cx = [1+2i, 3-4i];
str = ["alpha", "beta"];
end
