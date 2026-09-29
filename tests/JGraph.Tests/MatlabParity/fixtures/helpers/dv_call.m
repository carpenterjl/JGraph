function r = dv_call(f)
% DV_CALL  Call f for no output and answer 0: lets dv_warn watch a statement that answers nothing.
f();
r = 0;
end
