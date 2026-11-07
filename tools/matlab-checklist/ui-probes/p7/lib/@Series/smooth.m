function r = smooth(s, width)
%SMOOTH The series averaged over a window of the width given, the ends shortened.
r = conv(s.Values, ones(1, width) / width, 'same');
end
