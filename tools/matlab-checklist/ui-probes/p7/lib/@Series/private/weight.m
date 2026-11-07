function w = weight(n)
%WEIGHT The weights of n samples, 1 for the first rising to 2 for the last. A private function of
%the class folder: the class's method files reach it, nothing else does.
if n == 1
    w = 1;
else
    w = linspace(1, 2, n);
end
end
