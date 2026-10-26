function n = k13state()
%K13STATE Count this session's calls: a persistent count, which two package instances share or not.
persistent count
if isempty(count)
    count = 0;
end
count = count + 1;
n = count;
end
