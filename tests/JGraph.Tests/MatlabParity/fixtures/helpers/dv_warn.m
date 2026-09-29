function [v, w] = dv_warn(f)
% DV_WARN  Call f for one output and answer it with the warning it raised, as
%   "identifier ## message" on one line with any hyperlink reduced to its text; 'none' when none.
lastwarn('');
v = f();
[m, id] = lastwarn;
if isempty(id) && isempty(m)
    w = 'none';
else
    w = [id ' ## ' ix_flat(m)];
end
end
