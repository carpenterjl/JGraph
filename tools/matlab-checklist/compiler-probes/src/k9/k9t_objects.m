function [p, c] = k9t_objects(color)
%K9T_OBJECTS Typed object and enumeration outputs.
arguments (Input)
    color (1,1) k9color
end
arguments (Output)
    p (1,1) k9point
    c (1,1) k9color
end
p = k9point(1, 1);
c = color;
end
