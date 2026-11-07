function r = total(o)
%TOTAL The weighted sum of the series: each value by the folder's private weight.
r = sum(weight(o.Values));
end
