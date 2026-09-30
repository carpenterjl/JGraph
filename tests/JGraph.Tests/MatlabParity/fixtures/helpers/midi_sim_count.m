function [count, value] = midi_sim_count(arg)
% MIDI_SIM_COUNT  A counter for midi_sim's callback case: midi_sim_count('reset') zeroes it, a call
%   with a number counts one and keeps the number, and a call with nothing answers the count and the
%   last number kept.
persistent n last
if isempty(n) || (nargin == 1 && ischar(arg))
    n = 0;
    last = NaN;
elseif nargin == 1
    n = n + 1;
    last = arg;
end
count = n;
value = last;
end
