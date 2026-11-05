function b6_iscl(v)
i1 = numel(v) ~= 1;
i2 = length(v) > 1;
i3 = 1 ~= numel(v);
i4 = isequal(length(v), 1);
i5 = length(v) == 0;
i6 = numel(v) == 1 && v > 0;
i7 = size(v, 1) == 1;
i8 = ndims(v) ~= 2;
i9 = ndims(v) > 2;
disp([i1 i2 i3 i4 i5 i6 i7 i8 i9])
end
