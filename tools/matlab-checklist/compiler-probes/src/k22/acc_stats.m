function s = acc_stats(v)
%ACC_STATS The acceptance set's struct answer: the count, mean and largest of a row (stage K22).
arguments
    v (1,:) double
end
s = struct('n', numel(v), 'mean', mean(v), 'max', max(v));
end
