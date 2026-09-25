function y = dc_persist()
% A persistent handed out (V11): a caller writing its copy must not change the slot.
persistent p
if isempty(p)
    p = [1 2 3];
end
y = p;
end
