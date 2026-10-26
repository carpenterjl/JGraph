function n = k14state()
%K14STATE A count of the calls this process has answered.
persistent count
if isempty(count), count = 0; end
count = count + 1;
n = count;
end
