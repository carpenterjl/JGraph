function r = total(s)
%TOTAL The weighted sum of the series: later samples count more (private/weight.m).
r = sum(s.Values .* weight(numel(s.Values)));
end
