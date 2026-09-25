function dc_temp_holder()
% Makes a DeleteHolder and keeps it in its own frame (V11): the frame's exit releases it, at the
% caller's next statement boundary, whichever dialect the caller speaks.
t = DeleteHolder('T');
vlog('in');
end
